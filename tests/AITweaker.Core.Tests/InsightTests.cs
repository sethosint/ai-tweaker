using AITweaker.Core.Engine;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Tests;

public class InsightTests
{
    [Fact]
    public void HealthyDesktopHasNoCriticalIssues()
    {
        var insights = InsightEngine.Analyze(TestProfiles.Desktop());
        Assert.DoesNotContain(insights, i => i.Severity == InsightSeverity.Critical);
    }

    [Fact]
    public void DetectsXmpOffAndSingleChannel()
    {
        var p = TestProfiles.Desktop();
        p.Memory = new MemoryInfo { TotalGb = 16, Type = "DDR4", Modules = [new MemoryModule { CapacityGb = 16, Speed = 2133, ConfiguredSpeed = 2133 }] };
        var ids = InsightEngine.Analyze(p).Select(i => i.Id).ToList();
        Assert.Contains("ram.xmp", ids);
        Assert.Contains("ram.single", ids);
    }

    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13900K", 0x129u, true)]
    [InlineData("14th Gen Intel(R) Core(TM) i7-14700K", 0x12Bu, false)]
    [InlineData("14th Gen Intel(R) Core(TM) i7-14700K", null, true)]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13400F", 0x100u, false)]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13980HX", 0x100u, false)]
    [InlineData("12th Gen Intel(R) Core(TM) i9-12900K", 0x10u, false)]
    public void IntelVminShiftWarning(string name, uint? microcode, bool expected)
    {
        var p = TestProfiles.Desktop();
        p.Cpu = new CpuInfo { Name = name, Vendor = "Intel", Cores = 8, Threads = 16, MicrocodeRevision = microcode, Topology = TestProfiles.Cores(8) };
        Assert.Equal(expected, InsightEngine.Analyze(p).Any(i => i.Id == "cpu.intel.vmin"));
    }

    [Fact]
    public void DetectsMonitorBelowMaxRefresh()
    {
        var p = TestProfiles.Desktop();
        p.Displays[0].RefreshHz = 60;
        var hz = InsightEngine.Analyze(p).Single(i => i.Id.StartsWith("display.hz", StringComparison.Ordinal));
        Assert.Equal(InsightSeverity.Critical, hz.Severity);
        Assert.Equal("tweak:display.maxhz", hz.Action);
    }

    [Fact]
    public void DetectsOldGpuDriverAndHdd()
    {
        var p = TestProfiles.Desktop();
        p.Gpus[0].DriverDate = DateTime.Today.AddYears(-1);
        p.Disks[0].MediaType = "HDD";
        var ids = InsightEngine.Analyze(p).Select(i => i.Id).ToList();
        Assert.Contains("gpu.driver.old", ids);
        Assert.Contains("disk.hdd", ids);
    }

    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700K", 13)]
    [InlineData("Intel(R) Core(TM) i9-14900K", 14)]
    [InlineData("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz", 9)]
    [InlineData("Intel(R) Core(TM) Ultra 7 265K", 0)]
    public void ParsesIntelGeneration(string name, int gen) =>
        Assert.Equal(gen, new CpuInfo { Name = name, Vendor = "Intel" }.IntelGeneration);
}
