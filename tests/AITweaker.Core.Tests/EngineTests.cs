using AITweaker.Core.Bios;
using AITweaker.Core.Catalog;
using AITweaker.Core.Engine;
using AITweaker.Core.Model;

namespace AITweaker.Core.Tests;

public class EngineTests
{
    private static readonly RecommendationEngine Engine = new(TweakCatalog.LoadEmbedded());

    private static HashSet<string> Recommended(TunePlan plan) => plan.Recommended.Select(i => i.Tweak.Id).ToHashSet();
    private static HashSet<string> Applicable(TunePlan plan) => plan.Items.Select(i => i.Tweak.Id).ToHashSet();

    [Fact]
    public void DesktopGetsUltimatePlanAndHardwareTweaks()
    {
        var plan = Engine.BuildPlan(TestProfiles.Desktop(), TuneProfile.Performance);
        var ids = Recommended(plan);
        Assert.Contains("power.plan.ultimate", ids);
        Assert.Contains("devices.interrupt-affinity", ids);
        Assert.Contains("gaming.hags", ids);
        Assert.Contains("gaming.dynamic-tick.off", ids);
        Assert.DoesNotContain("power.plan.laptop", Applicable(plan));
        Assert.DoesNotContain("network.wifi-performance", Applicable(plan));
        Assert.DoesNotContain("system.bluetooth.off", Applicable(plan));
    }

    [Fact]
    public void LaptopKeepsBalancedPlanAndSkipsDesktopOnlyTweaks()
    {
        var plan = Engine.BuildPlan(TestProfiles.Laptop(), TuneProfile.Extreme);
        var applicable = Applicable(plan);
        Assert.Contains("power.plan.laptop", applicable);
        Assert.Contains("network.wifi-performance", applicable);
        Assert.DoesNotContain("power.plan.ultimate", applicable);
        Assert.DoesNotContain("gaming.dynamic-tick.off", applicable);
        Assert.DoesNotContain("power.hibernation.off", applicable);
        Assert.DoesNotContain("gaming.power-throttling.off", applicable); // hybrid + laptop
    }

    [Fact]
    public void DualCcdX3DKeepsCoreParkingAndBalancedPlan()
    {
        var plan = Engine.BuildPlan(TestProfiles.DualCcdX3D(), TuneProfile.Extreme);
        var applicable = Applicable(plan);
        Assert.Contains("power.plan.x3d", applicable);
        Assert.DoesNotContain("power.plan.ultimate", applicable);
        Assert.DoesNotContain("power.core-parking.off", applicable);
        Assert.DoesNotContain("power.min-processor-state", applicable);
        // Game DVR policy line must be filtered out so Game Bar keeps working for the V-Cache driver.
        var dvr = plan.Items.Single(i => i.Tweak.Id == "gaming.gamedvr.off").Tweak;
        Assert.Contains(dvr.Actions, a => a.When == "!cpu.x3dMultiCcd");
    }

    [Fact]
    public void ProfilesAreCumulative()
    {
        var p = TestProfiles.Desktop();
        var safe = Recommended(Engine.BuildPlan(p, TuneProfile.Safe));
        var balanced = Recommended(Engine.BuildPlan(p, TuneProfile.Balanced));
        var perf = Recommended(Engine.BuildPlan(p, TuneProfile.Performance));
        var extreme = Recommended(Engine.BuildPlan(p, TuneProfile.Extreme));
        Assert.Subset(balanced, safe);
        Assert.Subset(perf, balanced);
        Assert.Subset(extreme, perf);
        Assert.True(safe.Count < balanced.Count && balanced.Count < perf.Count);
    }

    [Fact]
    public void ManualOnlyTweaksAreNeverAutomatic()
    {
        var plan = Engine.BuildPlan(TestProfiles.Desktop(), TuneProfile.Extreme);
        Assert.DoesNotContain(plan.Recommended, i => i.Tweak.Level == 0);
        Assert.Contains(plan.Items, i => i.Tweak.Id == "security.cpu-mitigations.off" && !i.Recommended && i.NeedsConfirm);
    }

    [Fact]
    public void VirtualMachineSkipsHardwareTweaks()
    {
        var p = TestProfiles.Desktop();
        p.Machine.IsVirtualMachine = true;
        var ids = Recommended(Engine.BuildPlan(p, TuneProfile.Extreme));
        Assert.DoesNotContain("devices.interrupt-affinity", ids);
        Assert.DoesNotContain("gaming.platform-clock", ids);
        Assert.Contains("privacy.telemetry.policy", ids);
    }

    [Fact]
    public void ConfirmTweaksAreFlagged()
    {
        var p = TestProfiles.Desktop();
        p.Security.HvciRunning = true;
        var plan = Engine.BuildPlan(p, TuneProfile.Extreme);
        var vbs = plan.Items.Single(i => i.Tweak.Id == "security.vbs.off");
        Assert.True(vbs.NeedsConfirm);
        Assert.True(vbs.Recommended);
    }

    [Fact]
    public void MaxRefreshTweakOnlyWhenMonitorIsBelowMax()
    {
        var p = TestProfiles.Desktop();
        Assert.DoesNotContain("display.maxhz", Applicable(Engine.BuildPlan(p, TuneProfile.Safe)));
        p.Displays[0].RefreshHz = 60;
        Assert.Contains("display.maxhz", Recommended(Engine.BuildPlan(p, TuneProfile.Safe)));
    }

    [Fact]
    public void BiosAdvisorUsesBoardVendorPaths()
    {
        var p = TestProfiles.Desktop();
        p.Memory.Modules.ForEach(m => m.ConfiguredSpeed = 4800);
        var advice = BiosAdvisor.Advise(TweakCatalog.LoadEmbedded(), p);
        var xmp = advice.Single(a => a.Item.Id == "xmp");
        Assert.StartsWith("Ai Tweaker", xmp.Path);
        Assert.Equal("fix", xmp.Status);
        Assert.DoesNotContain(advice, a => a.Item.Id == "microcode");
        Assert.Contains(advice, a => a.Item.Id == "amd-psic");
    }
}
