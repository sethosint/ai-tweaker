using System.ComponentModel;
using System.Runtime.InteropServices;
using AITweaker.Core.Profile;

namespace AITweaker.App.Platform;

internal static partial class Native
{
    // ------------------------------------------------------------------ CPU topology

    private const int RelationProcessorCore = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);

    /// <summary>Physical cores with their efficiency class (P-cores have the higher class on hybrid CPUs).</summary>
    public static List<CoreInfo> GetCoreTopology()
    {
        uint len = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref len);
        if (len == 0) return [];
        var buffer = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref len)) throw new Win32Exception();
            var cores = new List<CoreInfo>();
            int offset = 0;
            while (offset < len)
            {
                var entry = buffer + offset;
                int relationship = Marshal.ReadInt32(entry, 0);
                int size = Marshal.ReadInt32(entry, 4);
                if (size <= 0) break;
                if (relationship == RelationProcessorCore)
                {
                    // PROCESSOR_RELATIONSHIP: Flags(1) EfficiencyClass(1) Reserved(20) GroupCount(2) GROUP_AFFINITY[]
                    byte efficiency = Marshal.ReadByte(entry, 9);
                    ushort groupCount = (ushort)Marshal.ReadInt16(entry, 30);
                    var lps = new List<int>();
                    for (int g = 0; g < groupCount; g++)
                    {
                        var ga = entry + 32 + g * 16; // GROUP_AFFINITY { KAFFINITY Mask; WORD Group; WORD Reserved[3]; }
                        ulong mask = (ulong)Marshal.ReadInt64(ga, 0);
                        ushort group = (ushort)Marshal.ReadInt16(ga, 8);
                        for (int bit = 0; bit < 64; bit++)
                            if ((mask & (1UL << bit)) != 0) lps.Add(group * 64 + bit);
                    }
                    cores.Add(new CoreInfo { Index = cores.Count, EfficiencyClass = efficiency, LogicalProcessors = lps });
                }
                offset += size;
            }
            return cores;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ------------------------------------------------------------------ memory / CPU load

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public static (uint LoadPercent, ulong TotalBytes, ulong AvailableBytes) GetMemoryStatus()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (m.dwMemoryLoad, m.ullTotalPhys, m.ullAvailPhys) : (0, 0, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    public static (long Idle, long Kernel, long User) GetSystemTimes()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        return (idle, kernel, user);
    }

    // ------------------------------------------------------------------ displays

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;
    private const int DM_DISPLAYFREQUENCY = 0x400000;
    private const int DM_INTERLACED = 0x2;
    private const int CDS_UPDATEREGISTRY = 0x1;
    private const int CDS_TEST = 0x2;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };

    public static List<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var current = NewDevMode();
            if (!EnumDisplaySettings(adapter.DeviceName, ENUM_CURRENT_SETTINGS, ref current)) continue;

            int max = current.dmDisplayFrequency;
            var mode = NewDevMode();
            for (int m = 0; EnumDisplaySettings(adapter.DeviceName, m, ref mode); m++)
            {
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight &&
                    mode.dmBitsPerPel == current.dmBitsPerPel && (mode.dmDisplayFlags & DM_INTERLACED) == 0)
                {
                    max = Math.Max(max, mode.dmDisplayFrequency);
                }
                mode = NewDevMode();
            }

            var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            string name = EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0) && !string.IsNullOrWhiteSpace(monitor.DeviceString)
                ? monitor.DeviceString
                : adapter.DeviceString;

            list.Add(new DisplayInfo
            {
                Device = adapter.DeviceName,
                Name = name,
                Width = current.dmPelsWidth,
                Height = current.dmPelsHeight,
                RefreshHz = current.dmDisplayFrequency,
                MaxRefreshHz = max,
            });
        }
        return list;
    }

    public sealed record DisplayMode(int Width, int Height, int Hz);

    /// <summary>Every progressive mode the driver offers at the current colour depth (custom CRU/NVIDIA modes included).</summary>
    public static (DisplayMode Current, List<DisplayMode> Modes) GetModes(string device)
    {
        var current = NewDevMode();
        if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref current)) return (new DisplayMode(0, 0, 0), []);
        var set = new HashSet<DisplayMode>();
        var mode = NewDevMode();
        for (int m = 0; EnumDisplaySettings(device, m, ref mode); m++)
        {
            if (mode.dmBitsPerPel == current.dmBitsPerPel && (mode.dmDisplayFlags & DM_INTERLACED) == 0 && mode.dmPelsWidth >= 640)
                set.Add(new DisplayMode(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency));
            mode = NewDevMode();
        }
        return (new DisplayMode(current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency),
            set.OrderByDescending(x => x.Width * x.Height).ThenByDescending(x => x.Hz).ToList());
    }

    /// <summary>Sets resolution and refresh rate. Tests the mode first; returns false if the driver refuses.</summary>
    public static bool SetRefreshRate(string device, int width, int height, int hz)
    {
        var mode = NewDevMode();
        if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref mode)) return false;
        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmDisplayFrequency = hz;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        if (ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, CDS_TEST, IntPtr.Zero) != DISP_CHANGE_SUCCESSFUL) return false;
        return ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
    }

    // ------------------------------------------------------------------ power

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlay);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static Guid GetActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) throw new Win32Exception("PowerGetActiveScheme failed");
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    public static void SetActiveScheme(Guid scheme)
    {
        var code = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (code != 0) throw new Win32Exception((int)code, $"PowerSetActiveScheme({scheme}) failed");
    }

    public static (uint? Ac, uint? Dc) ReadPowerSetting(Guid scheme, Guid subgroup, Guid setting)
    {
        uint? ac = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var a) == 0 ? a : null;
        uint? dc = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var d) == 0 ? d : null;
        return (ac, dc);
    }

    public static void WritePowerSetting(Guid scheme, Guid subgroup, Guid setting, uint? ac, uint? dc)
    {
        if (ac is { } a)
        {
            var code = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, a);
            if (code != 0) throw new Win32Exception((int)code, $"PowerWriteACValueIndex {setting} failed");
        }
        if (dc is { } d)
        {
            var code = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, d);
            if (code != 0) throw new Win32Exception((int)code, $"PowerWriteDCValueIndex {setting} failed");
        }
        // Re-activating the scheme applies the new indexes immediately.
        if (scheme == GetActiveScheme()) SetActiveScheme(scheme);
    }

    public static Guid? GetOverlay()
    {
        try { return PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    public static bool SetOverlay(Guid overlay)
    {
        try { return PowerSetActiveOverlayScheme(overlay) == 0; }
        catch (EntryPointNotFoundException) { return false; }
    }

    // ------------------------------------------------------------------ window chrome

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Paints the native title bar to match the UI theme (Windows 11; harmless no-op elsewhere).</summary>
    public static void StyleTitleBar(IntPtr hwnd, bool dark, int captionRgb, int textRgb)
    {
        int useDark = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = ToColorRef(captionRgb);
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
        int text = ToColorRef(textRgb);
        DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int)); // DWMWA_TEXT_COLOR
    }

    private static int ToColorRef(int rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
}
