using AITweaker.Core.Profile;

namespace AITweaker.Core.Tests;

internal static class TestProfiles
{
    public static List<CoreInfo> Cores(int count, int pCores = 0, bool smt = true)
    {
        var list = new List<CoreInfo>();
        int lp = 0;
        for (int i = 0; i < count; i++)
        {
            bool isP = pCores == 0 || i < pCores;
            var lps = isP && smt ? new List<int> { lp, lp + 1 } : new List<int> { lp };
            lp += lps.Count;
            list.Add(new CoreInfo { Index = i, EfficiencyClass = pCores == 0 ? 0 : isP ? 1 : 0, LogicalProcessors = lps });
        }
        return list;
    }

    /// <summary>Typical gaming desktop: Ryzen 7 7800X3D, RTX 4070, 32 GB DDR5-6000, NVMe, Ethernet.</summary>
    public static SystemProfile Desktop()
    {
        var cores = Cores(8);
        return new SystemProfile
        {
            Os = new OsInfo { Caption = "Microsoft Windows 11 Pro", Build = 26100, Ubr = 4000, Edition = "Professional", DisplayVersion = "24H2" },
            Cpu = new CpuInfo { Name = "AMD Ryzen 7 7800X3D 8-Core Processor", Vendor = "AMD", Cores = 8, Threads = 16, Topology = cores },
            Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 4070", Vendor = "NVIDIA", IsDiscrete = true, PnpId = "PCI\\VEN_10DE&DEV_2786", DriverDate = DateTime.Today.AddDays(-30) }],
            Memory = new MemoryInfo
            {
                TotalGb = 32, Type = "DDR5",
                Modules = [new MemoryModule { CapacityGb = 16, Speed = 6000, ConfiguredSpeed = 6000 }, new MemoryModule { CapacityGb = 16, Speed = 6000, ConfiguredSpeed = 6000 }],
            },
            Disks = [new DiskInfo { Model = "Samsung 990 PRO", MediaType = "SSD", BusType = "NVMe", IsSystem = true, SizeGb = 1000 }],
            Network = [new NetAdapterInfo { Description = "Realtek Gaming 2.5GbE", Kind = "ethernet", Connected = true, SpeedMbps = 2500 }],
            Displays = [new DisplayInfo { Name = "LG 27GP850", Device = "\\\\.\\DISPLAY1", Width = 2560, Height = 1440, RefreshHz = 165, MaxRefreshHz = 165 }],
            Board = new BoardInfo { Manufacturer = "ASUSTeK COMPUTER INC.", Product = "ROG STRIX B650E-F" },
            Machine = new MachineInfo { Manufacturer = "ASUS", Model = "Desktop" },
            Security = new SecurityInfo { SecureBoot = true, Tpm = true },
            HasBluetooth = true,
            Apps = ["steam", "discord", "edge"],
        };
    }

    public static SystemProfile Laptop()
    {
        var p = Desktop();
        p.Cpu = new CpuInfo { Name = "13th Gen Intel(R) Core(TM) i7-13700H", Vendor = "Intel", Cores = 14, Threads = 20, Topology = Cores(14, pCores: 6) };
        p.Machine = new MachineInfo { IsLaptop = true, HasBattery = true, Manufacturer = "Lenovo", Model = "Legion 5" };
        p.Network = [new NetAdapterInfo { Description = "Intel Wi-Fi 6E AX211", Kind = "wifi", Connected = true }];
        p.Memory = new MemoryInfo { TotalGb = 16, Type = "DDR5", Modules = [new MemoryModule { CapacityGb = 8, Speed = 5200, ConfiguredSpeed = 5200 }, new MemoryModule { CapacityGb = 8, Speed = 5200, ConfiguredSpeed = 5200 }] };
        return p;
    }

    public static SystemProfile DualCcdX3D()
    {
        var p = Desktop();
        p.Cpu = new CpuInfo { Name = "AMD Ryzen 9 9950X3D 16-Core Processor", Vendor = "AMD", Cores = 16, Threads = 32, Topology = Cores(16) };
        return p;
    }
}
