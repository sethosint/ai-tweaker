using System.Management;
using System.Text.RegularExpressions;
using AITweaker.Core.Profile;
using Microsoft.Win32;

namespace AITweaker.App.Platform;

/// <summary>Scans the machine (WMI, registry, Win32) into a <see cref="SystemProfile"/>.</summary>
public static class SystemProfiler
{
    private const string Cimv2 = @"root\cimv2";

    public static SystemProfile Scan()
    {
        var p = new SystemProfile { ScannedAt = DateTime.Now };
        Section("os", () => ScanOs(p));
        Section("cpu", () => ScanCpu(p));
        Section("gpu", () => ScanGpus(p));
        Section("memory", () => ScanMemory(p));
        Section("disks", () => ScanDisks(p));
        Section("network", () => ScanNetwork(p));
        Section("displays", () => p.Displays = Native.GetDisplays());
        Section("board", () => ScanBoard(p));
        Section("machine", () => ScanMachine(p));
        Section("security", () => ScanSecurity(p));
        Section("devices", () => ScanDevices(p));
        Section("apps", () => p.Apps = DetectApps());
        Section("storage", () =>
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            p.SystemDriveFreePercent = d.TotalSize > 0 ? 100.0 * d.AvailableFreeSpace / d.TotalSize : 100;
        });
        return p;
    }

    private static void Section(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Warn("scan", $"{name}: {ex.Message}");
        }
    }

    internal static List<ManagementBaseObject> Query(string scope, string wql)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch (Exception ex)
        {
            AppLog.Warn("wmi", $"{scope}: {wql}: {ex.Message}");
            return [];
        }
    }

    private static string S(ManagementBaseObject o, string prop) => (o[prop]?.ToString() ?? "").Trim();

    private static long L(ManagementBaseObject o, string prop) => o[prop] is { } v ? Convert.ToInt64(v) : 0;

    private static DateTime? Cim(ManagementBaseObject o, string prop)
    {
        var s = S(o, prop);
        if (s.Length == 0) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); }
        catch { return null; }
    }

    // ------------------------------------------------------------------ sections

    private static void ScanOs(SystemProfile p)
    {
        foreach (var o in Query(Cimv2, "SELECT Caption, BuildNumber, InstallDate FROM Win32_OperatingSystem"))
        {
            p.Os.Caption = S(o, "Caption");
            p.Os.Build = int.TryParse(S(o, "BuildNumber"), out var b) ? b : 0;
            p.Os.InstallDate = Cim(o, "InstallDate");
        }
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (k is not null)
        {
            p.Os.Ubr = k.GetValue("UBR") is int ubr ? ubr : 0;
            p.Os.DisplayVersion = k.GetValue("DisplayVersion")?.ToString() ?? "";
            p.Os.Edition = k.GetValue("EditionID")?.ToString() ?? "";
        }
        p.Os.Language = System.Globalization.CultureInfo.InstalledUICulture.Name;
    }

    private static void ScanCpu(SystemProfile p)
    {
        foreach (var o in Query(Cimv2, "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            p.Cpu.Name = Regex.Replace(S(o, "Name"), @"\s+", " ");
            var m = S(o, "Manufacturer");
            p.Cpu.Vendor = m.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : m.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD" : "Other";
            p.Cpu.Cores += (int)L(o, "NumberOfCores");
            p.Cpu.Threads += (int)L(o, "NumberOfLogicalProcessors");
            p.Cpu.MaxClockMhz = (int)L(o, "MaxClockSpeed");
        }
        p.Cpu.Topology = Native.GetCoreTopology();
        if (p.Cpu.Cores == 0) p.Cpu.Cores = p.Cpu.Topology.Count;
        if (p.Cpu.Threads == 0) p.Cpu.Threads = Environment.ProcessorCount;

        using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        if (k?.GetValue("Update Revision") is byte[] { Length: >= 8 } rev)
        {
            // The revision lives in the high DWORD on Intel; some AMD systems report it in the low DWORD.
            uint hi = BitConverter.ToUInt32(rev, 4), lo = BitConverter.ToUInt32(rev, 0);
            p.Cpu.MicrocodeRevision = hi != 0 ? hi : lo != 0 ? lo : null;
        }
    }

    private static void ScanGpus(SystemProfile p)
    {
        foreach (var o in Query(Cimv2, "SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController"))
        {
            var name = S(o, "Name");
            var pnp = S(o, "PNPDeviceID");
            if (!pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue; // virtual / remote adapters
            string vendor = pnp.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                : pnp.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? "AMD"
                : pnp.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ? "Intel" : "Other";
            bool discrete = vendor switch
            {
                "NVIDIA" => true,
                "AMD" => Regex.IsMatch(name, @"\bRX\b|Radeon Pro|Radeon VII|Vega (56|64)", RegexOptions.IgnoreCase),
                "Intel" => Regex.IsMatch(name, @"Arc.*\b[AB]\d{3}", RegexOptions.IgnoreCase),
                _ => false,
            };
            p.Gpus.Add(new GpuInfo
            {
                Name = name,
                Vendor = vendor,
                PnpId = pnp,
                DriverVersion = S(o, "DriverVersion"),
                DriverDate = Cim(o, "DriverDate"),
                VramMb = ReadVram(name) ?? L(o, "AdapterRAM") / (1024 * 1024),
                IsDiscrete = discrete,
            });
        }
        p.Gpus = p.Gpus.OrderByDescending(g => g.IsDiscrete).ToList();
    }

    /// <summary>AdapterRAM is capped at 4 GB; the display class key has the real 64-bit size.</summary>
    private static long? ReadVram(string name)
    {
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return null;
        foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            try
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc")?.ToString() != name) continue;
                if (k.GetValue("HardwareInformation.qwMemorySize") is long q) return q / (1024 * 1024);
                if (k.GetValue("HardwareInformation.qwMemorySize") is byte[] { Length: 8 } b) return BitConverter.ToInt64(b) / (1024 * 1024);
            }
            catch (System.Security.SecurityException) { }
        }
        return null;
    }

    private static void ScanMemory(SystemProfile p)
    {
        foreach (var o in Query(Cimv2, "SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, DeviceLocator, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            p.Memory.Modules.Add(new MemoryModule
            {
                CapacityGb = L(o, "Capacity") / 1073741824.0,
                Speed = (int)L(o, "Speed"),
                ConfiguredSpeed = (int)L(o, "ConfiguredClockSpeed"),
                Manufacturer = S(o, "Manufacturer"),
                PartNumber = S(o, "PartNumber"),
                Slot = S(o, "DeviceLocator"),
            });
            p.Memory.Type = L(o, "SMBIOSMemoryType") switch
            {
                26 => "DDR4",
                34 => "DDR5",
                35 => "LPDDR5",
                30 => "LPDDR4",
                24 => "DDR3",
                _ => p.Memory.Type,
            };
        }
        var (_, total, _) = Native.GetMemoryStatus();
        p.Memory.TotalGb = Math.Round(Math.Max(total / 1073741824.0, p.Memory.Modules.Sum(m => m.CapacityGb)));
    }

    private static void ScanDisks(SystemProfile p)
    {
        const string storage = @"root\Microsoft\Windows\Storage";
        var systemLetter = char.ToUpperInvariant(Environment.SystemDirectory[0]);
        long systemDisk = -1;
        foreach (var o in Query(storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
        {
            if (o["DriveLetter"] is char c && char.ToUpperInvariant(c) == systemLetter) systemDisk = L(o, "DiskNumber");
            else if (o["DriveLetter"] is ushort u && char.ToUpperInvariant((char)u) == systemLetter) systemDisk = L(o, "DiskNumber");
        }
        foreach (var o in Query(storage, "SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk"))
        {
            p.Disks.Add(new DiskInfo
            {
                Model = S(o, "FriendlyName"),
                SizeGb = Math.Round(L(o, "Size") / 1e9),
                MediaType = L(o, "MediaType") switch { 3 => "HDD", 4 or 5 => "SSD", _ => "Unknown" },
                BusType = L(o, "BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", _ => "Other" },
                IsSystem = S(o, "DeviceId") == systemDisk.ToString(),
            });
        }
        // NVMe drives sometimes report MediaType 0 (unspecified) — they are always solid state.
        foreach (var d in p.Disks.Where(d => d.MediaType == "Unknown" && d.BusType == "NVMe")) d.MediaType = "SSD";
    }

    private static void ScanNetwork(SystemProfile p)
    {
        foreach (var o in Query(@"root\StandardCimv2", "SELECT Name, InterfaceDescription, PnPDeviceID, InterfaceGuid, Speed, MediaConnectState, NdisPhysicalMedium, Virtual FROM MSFT_NetAdapter"))
        {
            if (o["Virtual"] is true) continue;
            long medium = L(o, "NdisPhysicalMedium");
            string? kind = medium switch { 9 => "wifi", 14 or 0 => "ethernet", _ => null };
            if (kind is null) continue; // bluetooth PAN, WWAN, etc.
            var pnp = S(o, "PnPDeviceID");
            if (!(pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) || pnp.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))) continue;
            p.Network.Add(new NetAdapterInfo
            {
                Name = S(o, "Name"),
                Description = S(o, "InterfaceDescription"),
                Kind = kind,
                PnpId = pnp,
                Guid = S(o, "InterfaceGuid"),
                SpeedMbps = L(o, "Speed") / 1_000_000,
                Connected = L(o, "MediaConnectState") == 1,
            });
        }
        p.HasBluetooth = Query(Cimv2, "SELECT Name FROM Win32_PnPEntity WHERE PNPClass='Bluetooth'").Count > 0;
    }

    private static void ScanBoard(SystemProfile p)
    {
        foreach (var o in Query(Cimv2, "SELECT Manufacturer, Product FROM Win32_BaseBoard"))
        {
            p.Board.Manufacturer = S(o, "Manufacturer");
            p.Board.Product = S(o, "Product");
        }
        foreach (var o in Query(Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
        {
            p.Board.BiosVendor = S(o, "Manufacturer");
            p.Board.BiosVersion = S(o, "SMBIOSBIOSVersion");
            p.Board.BiosDate = Cim(o, "ReleaseDate");
        }
    }

    private static void ScanMachine(SystemProfile p)
    {
        foreach (var o in Query(Cimv2, "SELECT Manufacturer, Model, PCSystemType, HypervisorPresent FROM Win32_ComputerSystem"))
        {
            p.Machine.Manufacturer = S(o, "Manufacturer");
            p.Machine.Model = S(o, "Model");
            p.Machine.IsLaptop = L(o, "PCSystemType") == 2;
            p.Security.HypervisorPresent = o["HypervisorPresent"] is true;
        }
        int[] portable = [8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32];
        foreach (var o in Query(Cimv2, "SELECT ChassisTypes FROM Win32_SystemEnclosure"))
        {
            if (o["ChassisTypes"] is ushort[] types && types.Any(t => portable.Contains(t))) p.Machine.IsLaptop = true;
        }
        p.Machine.HasBattery = Query(Cimv2, "SELECT Name FROM Win32_Battery").Count > 0;
        if (p.Machine.HasBattery) p.Machine.IsLaptop = true;

        var id = (p.Machine.Manufacturer + " " + p.Machine.Model).ToLowerInvariant();
        p.Machine.IsVirtualMachine = new[] { "virtual machine", "vmware", "virtualbox", "kvm", "qemu", "xen", "parallels", "hvm domu", "bochs" }.Any(id.Contains);
    }

    private static void ScanSecurity(SystemProfile p)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
            p.Security.SecureBoot = k?.GetValue("UEFISecureBootEnabled") is int sb && sb == 1;

        foreach (var o in Query(@"root\cimv2\Security\MicrosoftTpm", "SELECT IsEnabled_InitialValue FROM Win32_Tpm"))
            p.Security.Tpm = o["IsEnabled_InitialValue"] is true;

        foreach (var o in Query(@"root\Microsoft\Windows\DeviceGuard", "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard"))
        {
            p.Security.VbsRunning = L(o, "VirtualizationBasedSecurityStatus") == 2;
            p.Security.HvciRunning = o["SecurityServicesRunning"] is uint[] running && running.Contains(2u);
        }
    }

    private static void ScanDevices(SystemProfile p)
    {
        foreach (var o in Query(Cimv2, "SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
        {
            int code = (int)L(o, "ConfigManagerErrorCode");
            if (code is 22 or 45) continue; // disabled on purpose / not connected
            p.ProblemDevices.Add(new ProblemDevice { Name = S(o, "Name").Length > 0 ? S(o, "Name") : S(o, "PNPDeviceID"), PnpId = S(o, "PNPDeviceID"), ErrorCode = code });
        }

        string[] virtualPorts = ["PORTPROMPT:", "nul:", "SHRFAX:", "XPSPort:"];
        foreach (var o in Query(Cimv2, "SELECT Name, PortName, Network FROM Win32_Printer"))
        {
            var port = S(o, "PortName");
            var name = S(o, "Name");
            bool isVirtual = virtualPorts.Any(v => port.Equals(v, StringComparison.OrdinalIgnoreCase))
                             || Regex.IsMatch(name, "PDF|XPS|OneNote|Fax", RegexOptions.IgnoreCase);
            if (!isVirtual || o["Network"] is true) p.HasPrinter = true;
        }
    }

    // ------------------------------------------------------------------ apps

    public static List<string> DetectApps()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string packages = Path.Combine(local, "Packages");

        bool AppPath(string exe) => RegistryHelper.KeyExists($@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}")
                                    || RegistryHelper.KeyExists($@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
        bool Package(string prefix) => Directory.Exists(packages) && Directory.EnumerateDirectories(packages, prefix + "_*").Any();
        bool Service(string name) => RegistryHelper.KeyExists($@"HKLM\SYSTEM\CurrentControlSet\Services\{name}");

        var checks = new Dictionary<string, Func<bool>>
        {
            ["steam"] = () => RegistryHelper.KeyExists(@"HKCU\Software\Valve\Steam") || File.Exists(Path.Combine(pf86, "Steam", "steam.exe")),
            ["discord"] = () => File.Exists(Path.Combine(local, "Discord", "Update.exe")),
            ["chrome"] = () => AppPath("chrome.exe"),
            ["edge"] = () => AppPath("msedge.exe"),
            ["firefox"] = () => AppPath("firefox.exe"),
            ["brave"] = () => AppPath("brave.exe") || Directory.Exists(Path.Combine(local, "BraveSoftware")),
            ["obs"] = () => File.Exists(Path.Combine(pf, "obs-studio", "bin", "64bit", "obs64.exe")),
            ["spotify"] = () => File.Exists(Path.Combine(roaming, "Spotify", "Spotify.exe")) || Package("SpotifyAB.SpotifyMusic"),
            ["epic"] = () => Directory.Exists(Path.Combine(pf86, "Epic Games", "Launcher")) || Directory.Exists(Path.Combine(pf, "Epic Games", "Launcher")),
            ["nvidiaApp"] = () => Directory.Exists(Path.Combine(pf, "NVIDIA Corporation", "NVIDIA app")),
            ["amdSoftware"] = () => File.Exists(Path.Combine(pf, "AMD", "CNext", "CNext", "RadeonSoftware.exe")),
            ["exitlag"] = () => Service("ExitLagPmService") || Directory.Exists(Path.Combine(pf, "ExitLag")) || Directory.Exists(Path.Combine(pf86, "ExitLag")),
            ["xbox"] = () => Package("Microsoft.GamingApp"),
            ["store"] = () => Package("Microsoft.WindowsStore"),
            ["onedrive"] = () => File.Exists(Path.Combine(local, "Microsoft", "OneDrive", "OneDrive.exe")) || File.Exists(Path.Combine(pf, "Microsoft OneDrive", "OneDrive.exe")),
            ["teams"] = () => Package("MSTeams"),
        };

        var found = new List<string>();
        foreach (var (id, check) in checks)
        {
            try { if (check()) found.Add(id); }
            catch { /* ignore inaccessible paths */ }
        }
        return found;
    }
}
