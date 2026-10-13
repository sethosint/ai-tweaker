using System.Text.RegularExpressions;

namespace AITweaker.Core.Profile;

/// <summary>Everything the scanner learns about the machine. Filled by the Windows layer, consumed by the engine.</summary>
public sealed class SystemProfile
{
    public OsInfo Os { get; set; } = new();
    public CpuInfo Cpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = [];
    public MemoryInfo Memory { get; set; } = new();
    public List<DiskInfo> Disks { get; set; } = [];
    public List<NetAdapterInfo> Network { get; set; } = [];
    public List<DisplayInfo> Displays { get; set; } = [];
    public BoardInfo Board { get; set; } = new();
    public MachineInfo Machine { get; set; } = new();
    public SecurityInfo Security { get; set; } = new();
    public List<ProblemDevice> ProblemDevices { get; set; } = [];
    /// <summary>Installed drivers of the devices that matter for games: display, network, Bluetooth, audio, chipset.</summary>
    public List<DeviceDriver> Drivers { get; set; } = [];
    public bool HasBluetooth { get; set; }
    public bool HasPrinter { get; set; }
    /// <summary>Ids of detected applications: steam, discord, chrome, edge, firefox, brave, obs, spotify, epic, nvidiaApp, amdSoftware, exitlag, xbox, store, onedrive, teams.</summary>
    public List<string> Apps { get; set; } = [];
    public double SystemDriveFreePercent { get; set; } = 100;
    public DateTime ScannedAt { get; set; } = DateTime.Now;

    public GpuInfo? PrimaryGpu => Gpus.FirstOrDefault(g => g.IsDiscrete) ?? Gpus.FirstOrDefault();
    public DiskInfo? SystemDisk => Disks.FirstOrDefault(d => d.IsSystem);

    /// <summary>Flattens the profile into the fact dictionary used by catalog conditions.</summary>
    public Dictionary<string, object?> ToFacts()
    {
        var f = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["os.build"] = Os.Build,
            ["os.ubr"] = Os.Ubr,
            ["os.win11"] = Os.Build >= 22000,
            ["os.edition"] = Os.Edition,
            ["os.version"] = Os.DisplayVersion,

            ["cpu.vendor"] = Cpu.Vendor,
            ["cpu.name"] = Cpu.Name,
            ["cpu.cores"] = Cpu.Cores,
            ["cpu.threads"] = Cpu.Threads,
            ["cpu.hybrid"] = Cpu.IsHybrid,
            ["cpu.pcores"] = Cpu.PCores,
            ["cpu.ecores"] = Cpu.ECores,
            ["cpu.x3d"] = Cpu.IsX3D,
            ["cpu.x3dMultiCcd"] = Cpu.IsX3D && Cpu.Cores > 8,
            ["cpu.intelGen"] = Cpu.IntelGeneration,
            ["cpu.microcode"] = Cpu.MicrocodeRevision,
            ["cpu.smt"] = Cpu.Threads > Cpu.Cores,

            ["gpu.nvidia"] = Gpus.Any(g => g.Vendor == "NVIDIA"),
            ["gpu.amd"] = Gpus.Any(g => g.Vendor == "AMD"),
            ["gpu.intel"] = Gpus.Any(g => g.Vendor == "Intel"),
            ["gpu.discrete"] = Gpus.Any(g => g.IsDiscrete),
            ["gpu.hybrid"] = Gpus.Any(g => g.IsDiscrete) && Gpus.Any(g => !g.IsDiscrete),
            ["gpu.av1"] = Gpus.Any(g => g.EncodesAv1),
            ["gpu.vendor"] = PrimaryGpu?.Vendor,
            ["gpu.name"] = PrimaryGpu?.Name,

            ["ram.gb"] = Memory.TotalGb,
            ["ram.type"] = Memory.Type,
            ["ram.speed"] = Memory.ConfiguredSpeed,
            ["ram.modules"] = Memory.Modules.Count,
            ["ram.xmpOff"] = Memory.XmpLikelyOff,

            ["disk.systemSsd"] = SystemDisk?.MediaType == "SSD",
            ["disk.systemNvme"] = SystemDisk?.BusType == "NVMe",
            ["disk.hdd"] = Disks.Any(d => d.MediaType == "HDD"),

            ["system.laptop"] = Machine.IsLaptop,
            ["system.desktop"] = !Machine.IsLaptop,
            ["system.battery"] = Machine.HasBattery,
            ["system.vm"] = Machine.IsVirtualMachine,

            ["net.wifi"] = Network.Any(n => n.Kind == "wifi"),
            ["net.ethernet"] = Network.Any(n => n.Kind == "ethernet"),
            ["net.wifiOnly"] = Network.Any(n => n.Kind == "wifi") && !Network.Any(n => n.Kind == "ethernet" && n.Connected),

            ["bluetooth"] = HasBluetooth,
            ["printer"] = HasPrinter,
            ["display.maxHzUnused"] = Displays.Any(d => d.MaxRefreshHz > d.RefreshHz),

            ["sec.secureBoot"] = Security.SecureBoot,
            ["sec.tpm"] = Security.Tpm,
            ["sec.vbs"] = Security.VbsRunning,
            ["sec.hvci"] = Security.HvciRunning,
            ["sec.hyperv"] = Security.HypervisorPresent,
        };
        foreach (var app in Apps) f["app." + app] = true;
        return f;
    }

    /// <summary>All fact keys the catalog is allowed to reference (app.* is open-ended).</summary>
    public static IReadOnlySet<string> KnownFacts { get; } = new SystemProfile().ToFacts().Keys.ToHashSet();
}

public sealed class OsInfo
{
    public string Caption { get; set; } = "";
    public int Build { get; set; }
    public int Ubr { get; set; }
    public string Edition { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string Language { get; set; } = "";
    public DateTime? InstallDate { get; set; }
}

public sealed class CoreInfo
{
    public int Index { get; set; }
    /// <summary>Higher = faster core (P-core on hybrid CPUs). 0 on non-hybrid parts.</summary>
    public int EfficiencyClass { get; set; }
    public List<int> LogicalProcessors { get; set; } = [];
}

public sealed class CpuInfo
{
    public string Name { get; set; } = "";
    /// <summary>Intel | AMD | Other</summary>
    public string Vendor { get; set; } = "Other";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public int MaxClockMhz { get; set; }
    public uint? MicrocodeRevision { get; set; }
    public List<CoreInfo> Topology { get; set; } = [];

    public bool IsHybrid => Topology.Select(c => c.EfficiencyClass).Distinct().Count() > 1;
    public int PCores => IsHybrid ? Topology.Count(c => c.EfficiencyClass == Topology.Max(x => x.EfficiencyClass)) : Cores;
    public int ECores => IsHybrid ? Cores - PCores : 0;
    public bool IsX3D => Name.Contains("X3D", StringComparison.OrdinalIgnoreCase);

    /// <summary>Intel Core generation parsed from the marketing name (e.g. 14 for "i7-14700K", 0 if unknown / Core Ultra).</summary>
    public int IntelGeneration
    {
        get
        {
            if (Vendor != "Intel") return 0;
            var m = Regex.Match(Name, @"(\d{1,2})th Gen", RegexOptions.IgnoreCase);
            if (m.Success) return int.Parse(m.Groups[1].Value);
            m = Regex.Match(Name, @"i[3579]-(\d{4,5})");
            if (!m.Success) return 0;
            var digits = m.Groups[1].Value;
            return digits.Length == 5 ? int.Parse(digits[..2]) : int.Parse(digits[..1]);
        }
    }
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    /// <summary>NVIDIA | AMD | Intel | Other</summary>
    public string Vendor { get; set; } = "Other";
    public string PnpId { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public DateTime? DriverDate { get; set; }
    public long VramMb { get; set; }
    public bool IsDiscrete { get; set; }

    /// <summary>Hardware AV1 encoder: NVIDIA RTX 40/50 (Ada, Blackwell), AMD RX 7000/9000, Intel Arc.</summary>
    public bool EncodesAv1 => Vendor switch
    {
        "NVIDIA" => Regex.IsMatch(Name, @"RTX\s*(40|50)\d{2}|RTX\s*PRO|\bAda\b|Blackwell", RegexOptions.IgnoreCase),
        "AMD" => Regex.IsMatch(Name, @"RX\s*(7|9)\d{3}", RegexOptions.IgnoreCase),
        "Intel" => Name.Contains("Arc", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}

public sealed class MemoryModule
{
    public double CapacityGb { get; set; }
    public int Speed { get; set; }
    public int ConfiguredSpeed { get; set; }
    public string Manufacturer { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Slot { get; set; } = "";
}

public sealed class MemoryInfo
{
    public double TotalGb { get; set; }
    /// <summary>DDR4 | DDR5 | LPDDR5 | Unknown</summary>
    public string Type { get; set; } = "Unknown";
    public List<MemoryModule> Modules { get; set; } = [];

    public int ConfiguredSpeed => Modules.Count == 0 ? 0 : Modules.Min(m => m.ConfiguredSpeed > 0 ? m.ConfiguredSpeed : m.Speed);

    /// <summary>Heuristic: memory runs at a JEDEC fallback speed, so XMP/EXPO is probably disabled in BIOS.</summary>
    public bool XmpLikelyOff => ConfiguredSpeed > 0 && Type switch
    {
        "DDR4" => ConfiguredSpeed <= 2666,
        "DDR5" => ConfiguredSpeed <= 4800,
        _ => false,
    };
}

public sealed class DiskInfo
{
    public string Model { get; set; } = "";
    public double SizeGb { get; set; }
    /// <summary>SSD | HDD | Unknown</summary>
    public string MediaType { get; set; } = "Unknown";
    /// <summary>NVMe | SATA | USB | RAID | Other</summary>
    public string BusType { get; set; } = "Other";
    public bool IsSystem { get; set; }
}

public sealed class NetAdapterInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>ethernet | wifi</summary>
    public string Kind { get; set; } = "ethernet";
    public string PnpId { get; set; } = "";
    public string Guid { get; set; } = "";
    public long SpeedMbps { get; set; }
    public bool Connected { get; set; }
}

public sealed class DisplayInfo
{
    public string Name { get; set; } = "";
    public string Device { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    public int MaxRefreshHz { get; set; }
}

public sealed class BoardInfo
{
    public string Manufacturer { get; set; } = "";
    public string Product { get; set; } = "";
    public string BiosVendor { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public DateTime? BiosDate { get; set; }

    /// <summary>Board maker only: asus | msi | gigabyte | asrock | other. BIOS advice uses the vendor table instead.</summary>
    public string VendorKey
    {
        get
        {
            var m = Manufacturer.ToLowerInvariant();
            if (m.Contains("asus")) return "asus";
            if (m.Contains("micro-star") || m.Contains("msi")) return "msi";
            if (m.Contains("gigabyte")) return "gigabyte";
            if (m.Contains("asrock")) return "asrock";
            return "other";
        }
    }
}

public sealed class MachineInfo
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public bool IsLaptop { get; set; }
    public bool HasBattery { get; set; }
    public bool IsVirtualMachine { get; set; }
}

public sealed class SecurityInfo
{
    public bool SecureBoot { get; set; }
    public bool Tpm { get; set; }
    public bool VbsRunning { get; set; }
    public bool HvciRunning { get; set; }
    public bool HypervisorPresent { get; set; }
}

public sealed class DeviceDriver
{
    /// <summary>Windows device class as WMI reports it: DISPLAY, NET, BLUETOOTH, MEDIA, SYSTEM.</summary>
    public string Class { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string HardwareId { get; set; } = "";
    /// <summary>Hardware maker from the driver (e.g. "Realtek", "Intel Corporation"); "(Standard …)" for Windows' own.</summary>
    public string Manufacturer { get; set; } = "";
    /// <summary>Who wrote the driver: "Microsoft" means Windows' built-in one.</summary>
    public string Provider { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime? Date { get; set; }
}

public sealed class ProblemDevice
{
    public string Name { get; set; } = "";
    public string PnpId { get; set; } = "";
    public int ErrorCode { get; set; }
}
