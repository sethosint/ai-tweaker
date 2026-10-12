using AITweaker.Core.Engine;

namespace AITweaker.Core.Tests;

public class DirectXSettingsTests
{
    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable=1;")]
    [InlineData("", "SwapEffectUpgradeEnable=1;")]
    [InlineData("VRROptimizeEnable=0;", "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=0;VRROptimizeEnable=1;", "VRROptimizeEnable=1;SwapEffectUpgradeEnable=1;")]
    [InlineData("AutoHDREnable=1; garbage ;", "AutoHDREnable=1;SwapEffectUpgradeEnable=1;")]
    public void MergeKeepsOtherSettings(string? current, string expected) =>
        Assert.Equal(expected, DirectXSettings.Merge(current, "SwapEffectUpgradeEnable", "1"));
}
