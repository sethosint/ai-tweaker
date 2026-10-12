using AITweaker.Core.Affinity;

namespace AITweaker.Core.Tests;

public class InterruptPlannerTests
{
    [Fact]
    public void AvoidsCoreZeroAndUsesDistinctCores()
    {
        var plan = InterruptPlanner.Plan(TestProfiles.Cores(8), ["gpu", "nic", "usb"]);
        Assert.Equal(3, plan.Count);
        Assert.DoesNotContain(plan, a => a.LogicalProcessor == 0 || a.CoreIndex == 0);
        Assert.Equal(3, plan.Select(a => a.CoreIndex).Distinct().Count());
        Assert.Equal(new[] { 2, 4, 6 }, plan.Select(a => a.LogicalProcessor));
    }

    [Fact]
    public void HybridCpuUsesOnlyPCores()
    {
        // 6 P-cores with HT (LP 0..11), 8 E-cores (LP 12..19)
        var plan = InterruptPlanner.Plan(TestProfiles.Cores(14, pCores: 6), ["gpu", "nic", "usb"]);
        Assert.All(plan, a => Assert.InRange(a.CoreIndex, 1, 5));
        Assert.All(plan, a => Assert.True(a.LogicalProcessor < 12));
    }

    [Fact]
    public void MaskBytesAreLittleEndian()
    {
        var a = new AffinityAssignment("gpu", 1, 2);
        Assert.Equal(4UL, a.Mask);
        Assert.Equal(new byte[] { 4, 0, 0, 0, 0, 0, 0, 0 }, a.MaskBytes);
    }

    [Fact]
    public void SmallCpuGetsNoPlan() => Assert.Empty(InterruptPlanner.Plan(TestProfiles.Cores(2), ["gpu"]));
}
