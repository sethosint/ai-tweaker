using AITweaker.Core.Nvidia;

namespace AITweaker.Core.Tests;

public class NvidiaDriverTests
{
    [Theory]
    [InlineData("32.0.15.8157", "581.57")]
    [InlineData("32.0.15.7688", "576.88")]
    [InlineData("31.0.15.5222", "552.22")]
    [InlineData("27.21.14.5671", "456.71")]
    [InlineData("", null)]
    [InlineData("10.0", null)]
    public void WindowsVersionToNvidia(string windows, string? nvidia) => Assert.Equal(nvidia, NvidiaDriver.FromWindowsVersion(windows));

    [Fact]
    public void ComparesNumerically()
    {
        Assert.True(NvidiaDriver.Compare("581.57", "576.88") > 0);
        Assert.True(NvidiaDriver.Compare("576.8", "576.28") > 0); // 576.80
        Assert.Equal(0, NvidiaDriver.Compare("581.57", "581.57"));
        Assert.True(NvidiaDriver.Compare("581.57", null) > 0);
    }

    private const string Products = """
        <?xml version="1.0" encoding="utf-8"?>
        <LookupValueSearch><LookupValues>
          <LookupValue ParentID="131" RequiresProduct="True"><Name>GeForce RTX 5070 Ti</Name><Value>1066</Value></LookupValue>
          <LookupValue ParentID="129" RequiresProduct="True"><Name>GeForce RTX 4070</Name><Value>1015</Value></LookupValue>
          <LookupValue ParentID="130" RequiresProduct="True"><Name>GeForce RTX 4070 Laptop GPU</Name><Value>1007</Value></LookupValue>
          <LookupValue ParentID="99" RequiresProduct="True"><Name>NVIDIA TITAN RTX</Name><Value>888</Value></LookupValue>
        </LookupValues></LookupValueSearch>
        """;

    [Fact]
    public void MatchesDesktopAndLaptopCards()
    {
        Assert.Equal(("131", "1066"), NvidiaDriver.MatchProduct(Products.Trim(), "NVIDIA GeForce RTX 5070 Ti", false));
        Assert.Equal(("129", "1015"), NvidiaDriver.MatchProduct(Products.Trim(), "NVIDIA GeForce RTX 4070", false));
        Assert.Equal(("130", "1007"), NvidiaDriver.MatchProduct(Products.Trim(), "NVIDIA GeForce RTX 4070 Laptop GPU", true));
        Assert.Equal(("130", "1007"), NvidiaDriver.MatchProduct(Products.Trim(), "NVIDIA GeForce RTX 4070", true)); // WMI name without "Laptop"
        Assert.Equal(("99", "888"), NvidiaDriver.MatchProduct(Products.Trim(), "NVIDIA TITAN RTX", false));
        Assert.Null(NvidiaDriver.MatchProduct(Products.Trim(), "NVIDIA GeForce GT 210", false));
    }

    [Fact]
    public void ParsesLookupAndRejectsForeignUrls()
    {
        const string ok = """{"Success":"1","IDS":[{"downloadInfo":{"Success":"1","Version":"581.57","Name":"GeForce%20Game%20Ready%20Driver","ReleaseDateTime":"Tue%20Oct%2014%2C%202025","DownloadURL":"https://us.download.nvidia.com/Windows/581.57/581.57-desktop-win10-win11-64bit-international-dch-whql.exe","DownloadURLFileSize":"870.12%20MB","DetailsURL":"https://www.nvidia.com/en-us/drivers/details/255000/"}}]}""";
        var o = NvidiaDriver.ParseLookup(ok)!;
        Assert.Equal("581.57", o.Version);
        Assert.Equal("GeForce Game Ready Driver", o.Name);
        Assert.Equal("870.12 MB", o.Size);
        Assert.EndsWith("dch-whql.exe", o.Url);

        Assert.Null(NvidiaDriver.ParseLookup(ok.Replace("https://us.download.nvidia.com", "https://evil.example.com")));
        Assert.Null(NvidiaDriver.ParseLookup(ok.Replace("https://us.download.nvidia.com", "http://us.download.nvidia.com")));
        Assert.Null(NvidiaDriver.ParseLookup("""{"Success":"0","IDS":[]}"""));
    }

    [Fact]
    public void SetupInstallsOnlyChosenComponents()
    {
        var folders = new[] { "Display.Driver", "HDAudio", "PhysX", "NVI2", "NVApp" };
        Assert.Equal("-s -noreboot -noeula -clean Display.Driver HDAudio.Driver PhysX", NvidiaDriver.SetupArgs(folders, true, true, true));
        Assert.Equal("-s -noreboot -noeula Display.Driver", NvidiaDriver.SetupArgs(folders, false, false, false));
        Assert.Equal("-s -noreboot -noeula -clean Display.Driver", NvidiaDriver.SetupArgs(["Display.Driver"], true, true, true));
    }
}
