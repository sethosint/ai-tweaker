using System.Text.RegularExpressions;
using AITweaker.Core.Bios;
using AITweaker.Core.Catalog;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Tests;

public class BiosVendorTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadEmbedded();

    [Theory]
    [InlineData(false, "ASUSTeK COMPUTER INC.", "System manufacturer", "asus")]
    [InlineData(false, "Micro-Star International Co., Ltd.", "Micro-Star International Co., Ltd.", "msi")]
    [InlineData(false, "Gigabyte Technology Co., Ltd.", "Gigabyte Technology Co., Ltd.", "gigabyte")]
    [InlineData(false, "ASRock", "To Be Filled By O.E.M.", "asrock")]
    [InlineData(false, "Dell Inc.", "Alienware", "dell")]
    [InlineData(false, "To Be Filled By O.E.M.", "To Be Filled By O.E.M.", "other")]
    [InlineData(true, "ASUSTeK COMPUTER INC.", "ASUSTeK COMPUTER INC.", "asus-laptop")]
    [InlineData(true, "Micro-Star International Co., Ltd.", "Micro-Star International Co., Ltd.", "msi-laptop")]
    [InlineData(true, "LENOVO", "LENOVO", "lenovo")]
    [InlineData(true, "HP", "HP", "hp")]
    [InlineData(true, "Alienware", "Alienware", "dell")]
    [InlineData(true, "Acer", "Acer", "acer")]
    [InlineData(true, "Razer", "Razer", "razer")]
    [InlineData(true, "Microsoft Corporation", "Microsoft Corporation", "microsoft")]
    [InlineData(true, "Notebook", "MACHENIKE", "laptop")]
    public void FindsTheMaker(bool laptop, string board, string machine, string expected)
    {
        var p = TestProfiles.Desktop();
        p.Board.Manufacturer = board;
        p.Machine = new MachineInfo { Manufacturer = machine, IsLaptop = laptop };
        Assert.Equal(expected, BiosAdvisor.Vendor(Catalog.Vendors, p).Key);
    }

    [Fact]
    public void LaptopsGetLaptopAdviceOnly()
    {
        var p = TestProfiles.Laptop();
        p.Machine.Manufacturer = "LENOVO";
        p.Gpus = [.. p.Gpus, new GpuInfo { Name = "Intel(R) UHD Graphics", Vendor = "Intel" }];
        var advice = BiosAdvisor.Advise(Catalog, p);
        var ids = advice.Select(a => a.Item.Id).ToList();
        Assert.Contains("laptop-power-mode", ids);
        Assert.Contains("laptop-gpu-mode", ids);
        Assert.Contains("bios-update", ids);
        foreach (var desktopOnly in new[] { "xmp", "rebar", "amd-pbo", "bios-profile", "spread-spectrum", "vendor-software", "microcode" })
            Assert.DoesNotContain(desktopOnly, ids);
        Assert.Contains("GPU Working Mode", advice.Single(a => a.Item.Id == "laptop-gpu-mode").Path);
        Assert.Contains("Vantage", advice.Single(a => a.Item.Id == "bios-update").Path);
    }

    [Fact]
    public void NoMuxAdviceWithoutHybridGraphics()
    {
        var ids = BiosAdvisor.Advise(Catalog, TestProfiles.Laptop()).Select(a => a.Item.Id);
        Assert.DoesNotContain("laptop-gpu-mode", ids);
    }

    [Fact]
    public void DesktopsKeepBoardPaths()
    {
        var advice = BiosAdvisor.Advise(Catalog, TestProfiles.Desktop());
        Assert.StartsWith("Ai Tweaker", advice.Single(a => a.Item.Id == "xmp").Path);
        Assert.DoesNotContain(advice, a => a.Item.Id.StartsWith("laptop-", StringComparison.Ordinal));
    }

    [Fact]
    public void VendorTableIsConsistent()
    {
        var keys = Catalog.Vendors.Select(v => v.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains("laptop", keys);
        Assert.Contains("other", keys);
        foreach (var v in Catalog.Vendors)
        {
            _ = new Regex(v.Match);
            Assert.False(string.IsNullOrWhiteSpace(v.Enter), v.Key);
            if (v.Support is not null) Assert.StartsWith("https://", v.Support);
            if (v.Hint is not null) Assert.False(string.IsNullOrWhiteSpace(v.Hint.Ru) || string.IsNullOrWhiteSpace(v.Hint.En), v.Key);
        }
        foreach (var b in Catalog.Bios)
        {
            foreach (var key in b.Paths.Keys) Assert.Contains(key, keys);
            // every item has a path for whatever machine it is shown on
            bool laptopOnly = b.When?.Contains("system.laptop") == true, desktopOnly = b.When?.Contains("system.desktop") == true;
            if (!laptopOnly) Assert.True(b.Paths.ContainsKey("other"), b.Id);
            if (!desktopOnly) Assert.True(b.Paths.ContainsKey("laptop") || b.Paths.ContainsKey("other"), b.Id);
        }
    }
}
