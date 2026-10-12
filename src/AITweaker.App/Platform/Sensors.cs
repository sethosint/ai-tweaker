using System.Management;
using System.Runtime.InteropServices;

namespace AITweaker.App.Platform;

public sealed record DiskSensor(string Name, double? TempC, double? TempMaxC, double? WearPercent);

public sealed record SensorSample(
    DateTime Time,
    double? CpuTempC,
    string? CpuTempSource,
    double? GpuTempC,
    double? GpuLoad,
    int? GpuFanRpm,
    double? GpuPowerPercent,
    double TimerResolutionMs,
    IReadOnlyList<DiskSensor> Disks);

/// <summary>
/// Hardware sensors without third-party kernel drivers:
/// GPU temperature/fan/power through the WDDM adapter perf data (what Task Manager shows),
/// GPU 3D load through PDH, NVMe temperature/wear through Storage reliability counters,
/// CPU temperature from LibreHardwareMonitor/OpenHardwareMonitor WMI when running, else ACPI thermal zones.
/// </summary>
public sealed class Sensors : IDisposable
{
    private readonly GpuPerf _gpu = new();
    private readonly Pdh _pdh = new();
    private IReadOnlyList<DiskSensor> _disks = [];
    private DateTime _disksAt = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _alerted = [];

    public SensorSample Read()
    {
        var (cpuTemp, source) = ReadCpuTemperature();
        var gpu = _gpu.Read();
        if (DateTime.Now - _disksAt > TimeSpan.FromSeconds(30))
        {
            _disks = ReadDisks();
            _disksAt = DateTime.Now;
        }
        var s = new SensorSample(DateTime.Now, cpuTemp, source, gpu.TempC, _pdh.GpuLoad(), gpu.FanRpm, gpu.PowerPercent, TimerResolutionMs(), _disks);
        Alerts(s);
        return s;
    }

    /// <summary>Thermal alerts go to the event stream at most once every 5 minutes per sensor.</summary>
    private void Alerts(SensorSample s)
    {
        void Check(string key, double? value, double warn, double crit, string label)
        {
            if (value is not { } v || v < warn) return;
            if (_alerted.TryGetValue(key, out var last) && DateTime.Now - last < TimeSpan.FromMinutes(5)) return;
            _alerted[key] = DateTime.Now;
            AppLog.Write(v >= crit ? "error" : "warn", "thermal", $"{label} {v:0} °C");
        }
        Check("cpu", s.CpuTempC, 85, 95, "CPU");
        Check("gpu", s.GpuTempC, 80, 88, "GPU");
        foreach (var d in s.Disks) Check("disk:" + d.Name, d.TempC, 65, 75, d.Name);
    }

    // ------------------------------------------------------------------ CPU

    private DateTime _cpuSourceCheckedAt = DateTime.MinValue;
    private string? _cpuSource;

    private (double?, string?) ReadCpuTemperature()
    {
        // Re-probe sources every 30 s so starting LibreHardwareMonitor later is picked up.
        if (DateTime.Now - _cpuSourceCheckedAt > TimeSpan.FromSeconds(30))
        {
            _cpuSource = null;
            _cpuSourceCheckedAt = DateTime.Now;
        }
        foreach (var (ns, label) in new[] { (@"root\LibreHardwareMonitor", "LibreHardwareMonitor"), (@"root\OpenHardwareMonitor", "OpenHardwareMonitor") })
        {
            if (_cpuSource is not null && _cpuSource != label) continue;
            var t = HardwareMonitorCpuTemp(ns);
            if (t is not null)
            {
                _cpuSource = label;
                return (t, label);
            }
        }
        if (_cpuSource is null or "ACPI")
        {
            var t = AcpiTemp();
            if (t is not null)
            {
                _cpuSource = "ACPI";
                return (t, "ACPI");
            }
        }
        return (null, null);
    }

    private static double? HardwareMonitorCpuTemp(string ns)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(ns, "SELECT Name, Value, Parent FROM Sensor WHERE SensorType='Temperature'");
            var temps = searcher.Get().Cast<ManagementBaseObject>()
                .Select(o => (Name: o["Name"]?.ToString() ?? "", Parent: o["Parent"]?.ToString() ?? "", Value: Convert.ToDouble(o["Value"] ?? 0)))
                .Where(x => x.Parent.Contains("cpu", StringComparison.OrdinalIgnoreCase) && x.Value > 0)
                .ToList();
            if (temps.Count == 0) return null;
            var preferred = temps.FirstOrDefault(x => x.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase));
            return Math.Round(preferred.Name is { Length: > 0 } ? preferred.Value : temps.Max(x => x.Value), 1);
        }
        catch
        {
            return null; // namespace absent: the monitor isn't running
        }
    }

    private static double? AcpiTemp()
    {
        double? best = null;
        try
        {
            using var s1 = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (var o in s1.Get())
            {
                double c = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
                if (c is > 15 and < 115) best = Math.Max(best ?? 0, c);
            }
        }
        catch { /* not exposed by this firmware */ }
        if (best is null)
        {
            try
            {
                using var s2 = new ManagementObjectSearcher(@"root\cimv2", "SELECT HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                foreach (var o in s2.Get())
                {
                    double c = Convert.ToDouble(o["HighPrecisionTemperature"]) / 10.0 - 273.15;
                    if (c is > 15 and < 115) best = Math.Max(best ?? 0, c);
                }
            }
            catch { /* ignore */ }
        }
        return best is { } b ? Math.Round(b, 1) : null;
    }

    // ------------------------------------------------------------------ disks

    private static IReadOnlyList<DiskSensor> ReadDisks()
    {
        var list = new List<DiskSensor>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
            foreach (ManagementObject disk in searcher.Get())
            {
                var name = disk["FriendlyName"]?.ToString() ?? "Disk";
                if (Convert.ToInt32(disk["BusType"] ?? 0) == 7) continue; // USB sticks
                try
                {
                    foreach (ManagementObject r in disk.GetRelated("MSFT_StorageReliabilityCounter"))
                    {
                        double? temp = r["Temperature"] is { } t && Convert.ToDouble(t) > 0 ? Convert.ToDouble(t) : null;
                        double? max = r["TemperatureMax"] is { } m && Convert.ToDouble(m) > 0 ? Convert.ToDouble(m) : null;
                        double? wear = r["Wear"] is { } w ? Convert.ToDouble(w) : null;
                        list.Add(new DiskSensor(name, temp, max, wear));
                    }
                }
                catch { list.Add(new DiskSensor(name, null, null, null)); }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("sensors", "storage reliability counters unavailable: " + ex.Message);
        }
        return list;
    }

    // ------------------------------------------------------------------ timer

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    /// <summary>Current system timer resolution in milliseconds (15.625 ms default, 0.5 ms when a game requests it).</summary>
    public static double TimerResolutionMs()
    {
        try { return NtQueryTimerResolution(out _, out _, out var cur) == 0 ? Math.Round(cur / 10000.0, 3) : 0; }
        catch { return 0; }
    }

    public void Dispose() => _pdh.Dispose();

    // ------------------------------------------------------------------ WDDM adapter perf data

    private sealed class GpuPerf
    {
        private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ADAPTERINFO
        {
            public uint hAdapter;
            public uint LuidLow;
            public int LuidHigh;
            public uint NumOfSources;
            public int bPrecisePresentRegionsPreferred;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ENUMADAPTERS2
        {
            public uint NumAdapters;
            public IntPtr pAdapters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_QUERYADAPTERINFO
        {
            public uint hAdapter;
            public int Type;
            public IntPtr pPrivateDriverData;
            public uint PrivateDriverDataSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ADAPTER_PERFDATA
        {
            public uint PhysicalAdapterIndex;
            public ulong MemoryFrequency;
            public ulong MaxMemoryFrequency;
            public ulong MaxMemoryFrequencyOC;
            public ulong MemoryBandwidth;
            public ulong PCIEBandwidth;
            public uint FanRPM;
            public uint Power;        // tenths of a percent of TDP
            public uint Temperature;  // deci-°C
            public byte PowerStateOverride;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_CLOSEADAPTER
        {
            public uint hAdapter;
        }

        [DllImport("gdi32.dll")]
        private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 data);

        [DllImport("gdi32.dll")]
        private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO data);

        [DllImport("gdi32.dll")]
        private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER data);

        private bool _unsupported;

        public (double? TempC, int? FanRpm, double? PowerPercent) Read()
        {
            if (_unsupported) return (null, null, null);
            try
            {
                var e = new D3DKMT_ENUMADAPTERS2();
                if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0) return (null, null, null);
                int size = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
                e.pAdapters = Marshal.AllocHGlobal(size * (int)e.NumAdapters);
                try
                {
                    if (D3DKMTEnumAdapters2(ref e) != 0) return (null, null, null);
                    (double? T, int? F, double? P) best = (null, null, null);
                    for (int i = 0; i < e.NumAdapters; i++)
                    {
                        var info = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(e.pAdapters + i * size);
                        var perf = new D3DKMT_ADAPTER_PERFDATA { PhysicalAdapterIndex = 0 };
                        var buf = Marshal.AllocHGlobal(Marshal.SizeOf<D3DKMT_ADAPTER_PERFDATA>());
                        try
                        {
                            Marshal.StructureToPtr(perf, buf, false);
                            var q = new D3DKMT_QUERYADAPTERINFO { hAdapter = info.hAdapter, Type = KMTQAITYPE_ADAPTERPERFDATA, pPrivateDriverData = buf, PrivateDriverDataSize = (uint)Marshal.SizeOf<D3DKMT_ADAPTER_PERFDATA>() };
                            if (D3DKMTQueryAdapterInfo(ref q) == 0)
                            {
                                perf = Marshal.PtrToStructure<D3DKMT_ADAPTER_PERFDATA>(buf);
                                if (perf.Temperature > 0 && perf.Temperature < 1500 && (best.T is null || perf.Temperature / 10.0 > best.T))
                                    best = (perf.Temperature / 10.0, perf.FanRPM > 0 ? (int)perf.FanRPM : null, perf.Power > 0 ? perf.Power / 10.0 : null);
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(buf);
                            var c = new D3DKMT_CLOSEADAPTER { hAdapter = info.hAdapter };
                            D3DKMTCloseAdapter(ref c);
                        }
                    }
                    return best;
                }
                finally
                {
                    Marshal.FreeHGlobal(e.pAdapters);
                }
            }
            catch (EntryPointNotFoundException)
            {
                _unsupported = true; // pre-WDDM 2.4
                return (null, null, null);
            }
        }
    }

    // ------------------------------------------------------------------ PDH (GPU 3D engine load)

    private sealed class Pdh : IDisposable
    {
        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_MORE_DATA = 0x800007D2;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        private IntPtr _query, _counter;
        private bool _ready;

        public Pdh()
        {
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;
                if (PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _counter) != 0) return;
                PdhCollectQueryData(_query);
                _ready = true;
            }
            catch
            {
                _ready = false;
            }
        }

        /// <summary>Busiest physical GPU's 3D engine load, %.</summary>
        public double? GpuLoad()
        {
            if (!_ready) return null;
            try
            {
                if (PdhCollectQueryData(_query) != 0) return null;
                uint size = 0;
                uint rc = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out uint count, IntPtr.Zero);
                if (rc != PDH_MORE_DATA || size == 0) return count == 0 ? 0 : null;
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out count, buffer) != 0) return null;
                    // PDH_FMT_COUNTERVALUE_ITEM_W { LPWSTR szName; PDH_FMT_COUNTERVALUE { DWORD CStatus; double value; } } = 24 bytes on x64
                    var perAdapter = new Dictionary<string, double>();
                    for (int i = 0; i < count; i++)
                    {
                        var item = buffer + i * 24;
                        var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                        double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                        int luid = name.IndexOf("luid_", StringComparison.Ordinal);
                        int phys = name.IndexOf("_phys_", StringComparison.Ordinal);
                        var key = luid >= 0 && phys > luid ? name[luid..(phys + 7 <= name.Length ? phys + 7 : name.Length)] : "gpu";
                        perAdapter[key] = perAdapter.GetValueOrDefault(key) + v;
                    }
                    return perAdapter.Count == 0 ? 0 : Math.Round(Math.Min(100, perAdapter.Values.Max()), 1);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}
