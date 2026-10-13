using AITweaker.Core.Catalog;
using AITweaker.Core.Drivers;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Tests;

public class DriverAdvisorTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadEmbedded();
    private static readonly DateTime Now = new(2026, 10, 1);

    private static SystemProfile Desktop()
    {
        var p = TestProfiles.Desktop();
        p.Gpus[0].DriverDate = Now.AddDays(-20);
        p.Network = [new NetAdapterInfo { Description = "Realtek Gaming 2.5GbE Family Controller", Kind = "ethernet", PnpId = @"PCI\VEN_10EC&DEV_8125&SUBSYS_86771043&REV_05\01000000684CE00000", Connected = true }];
        p.Drivers =
        [
            new DeviceDriver { Class = "DISPLAY", Name = p.Gpus[0].Name, DeviceId = p.Gpus[0].PnpId, Provider = "NVIDIA", Version = "32.0.15.9144", Date = Now.AddDays(-20) },
            new DeviceDriver { Class = "NET", Name = "Realtek Gaming 2.5GbE Family Controller", DeviceId = @"PCI\VEN_10EC&DEV_8125&SUBSYS_86771043&REV_05\01000000684CE00000", Manufacturer = "Realtek", Provider = "Microsoft", Version = "1125.21.903.2024", Date = Now.AddDays(-400) },
            new DeviceDriver { Class = "MEDIA", Name = "High Definition Audio Device", DeviceId = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897&SUBSYS_10438882&REV_1001\5&1", Manufacturer = "Microsoft", Provider = "Microsoft", Date = Now.AddDays(-900) },
            new DeviceDriver { Class = "MEDIA", Name = "NVIDIA High Definition Audio", DeviceId = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_00A5&SUBSYS_10DE1234&REV_1001\5&2", Provider = "NVIDIA Corporation" },
            new DeviceDriver { Class = "BLUETOOTH", Name = "Intel(R) Wireless Bluetooth(R)", DeviceId = @"USB\VID_8087&PID_0033\5&3", Provider = "Intel Corporation", Date = Now.AddDays(-100) },
            new DeviceDriver { Class = "BLUETOOTH", Name = "Microsoft Bluetooth LE Enumerator", DeviceId = @"BTH\MS_BTHLE\7&1", Provider = "Microsoft" },
        ];
        return p;
    }

    [Fact]
    public void PicksTheSourcePerComponent()
    {
        var items = DriverAdvisor.Build(Catalog.Vendors, Desktop(), Now);

        var gpu = items.Single(i => i.Category == "gpu");
        Assert.Equal("ok", gpu.Status);
        Assert.Contains(gpu.Links, l => l.Title == "NVIDIA App");

        var lan = items.Single(i => i.Category == "ethernet");
        Assert.Equal("Realtek", lan.Vendor);
        Assert.Equal("inbox", lan.Status);
        Assert.Contains(lan.Links, l => l.Url.Contains("asus.com"));

        var audio = Assert.Single(items, i => i.Category == "audio");
        Assert.Equal("inbox", audio.Status);
        Assert.Equal("low", audio.Priority);

        var bt = Assert.Single(items, i => i.Category == "bluetooth");
        Assert.Equal("Intel", bt.Vendor);
        Assert.Contains(bt.Links, l => l.Title.StartsWith("Intel Driver", StringComparison.Ordinal));

        // the board maker's page comes first
        Assert.Equal("oem", items[0].Category);
        Assert.Equal("ASUSTeK COMPUTER INC. ROG STRIX B650E-F", items[0].Name);
    }

    [Fact]
    public void BasicDisplayDriverIsMissing()
    {
        var p = Desktop();
        p.Gpus[0].Name = "Microsoft Basic Display Adapter";
        p.Drivers[0].Provider = "Microsoft";
        var gpu = DriverAdvisor.Build(Catalog.Vendors, p, Now).Single(i => i.Category == "gpu");
        Assert.Equal("missing", gpu.Status);
        Assert.Equal("high", gpu.Priority);
    }

    [Fact]
    public void OldGpuDriverIsFlagged()
    {
        var p = Desktop();
        p.Gpus[0].DriverDate = Now.AddDays(-240);
        Assert.Equal("old", DriverAdvisor.Build(Catalog.Vendors, p, Now).Single(i => i.Category == "gpu").Status);
    }

    [Fact]
    public void AmdChipsetMattersMostOnDualCcdX3D()
    {
        var p = TestProfiles.DualCcdX3D();
        var chipset = DriverAdvisor.Build(Catalog.Vendors, p, Now).Single(i => i.Category == "chipset");
        Assert.Equal("missing", chipset.Status);
        Assert.Equal("high", chipset.Priority);

        p.Drivers.Add(new DeviceDriver { Class = "SYSTEM", Name = "AMD PSP 11.0 Device", DeviceId = @"PCI\VEN_1022&DEV_1649\3&1", Provider = "Advanced Micro Devices, Inc.", Version = "5.39.0.0", Date = Now.AddDays(-60) });
        Assert.Equal("ok", DriverAdvisor.Build(Catalog.Vendors, p, Now).Single(i => i.Category == "chipset").Status);
    }

    [Fact]
    public void KillerGetsThePlainIntelDriver()
    {
        var p = TestProfiles.Laptop();
        p.Network = [new NetAdapterInfo { Description = "Killer(R) Wi-Fi 6E AX1675i 160MHz Wireless Network Adapter (211NGW)", Kind = "wifi", PnpId = @"PCI\VEN_8086&DEV_51F0&SUBSYS_16721A56\3&1" }];
        p.Drivers = [new DeviceDriver { Class = "NET", Name = p.Network[0].Description, DeviceId = p.Network[0].PnpId, Provider = "Intel", Date = Now.AddDays(-30) }];
        var wifi = DriverAdvisor.Build(Catalog.Vendors, p, Now).Single(i => i.Category == "wifi");
        Assert.Equal("Killer", wifi.Vendor);
        Assert.Contains("Killer Control Center", wifi.Note!.En);
        Assert.Contains(wifi.Links, l => l.Url.Contains("intel.com"));
    }

    [Fact]
    public void LaptopsPointToTheMakerFirst()
    {
        var p = TestProfiles.Laptop();
        p.Machine.Manufacturer = "LENOVO";
        p.Machine.Model = "82JW";
        var items = DriverAdvisor.Build(Catalog.Vendors, p, Now);
        Assert.Equal("oem", items[0].Category);
        Assert.Contains(items[0].Links, l => l.Url.Contains("lenovo.com"));
        Assert.Contains("Vantage", items[0].Note!.Ru);
        // Intel laptop chipset goes to the maker's page before Intel's tool
        Assert.Contains("lenovo.com", items.Single(i => i.Category == "chipset").Links[0].Url);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10EC&DEV_8125", "Realtek Gaming 2.5GbE", "", "Realtek")]
    [InlineData(@"PCI\VEN_14C3&DEV_0616", "MediaTek Wi-Fi 6E MT7922", "", "MediaTek")]
    [InlineData(@"USB\VID_13D3&PID_3568", "MediaTek Bluetooth Adapter", "MediaTek, Inc.", "MediaTek")]
    [InlineData(@"PCI\VEN_8086&DEV_15F3", "Intel(R) Ethernet Controller I225-V", "", "Intel")]
    [InlineData(@"PCI\VEN_1969&DEV_E0B1", "Killer E2600 Gigabit Ethernet Controller", "", "Killer")]
    [InlineData(@"ROOT\SYSTEM\0001", "Composite Bus Enumerator", "(Standard system devices)", "")]
    public void ReadsTheChipMaker(string id, string name, string manufacturer, string expected) =>
        Assert.Equal(expected, DriverAdvisor.VendorOf(id, name, manufacturer));
}
