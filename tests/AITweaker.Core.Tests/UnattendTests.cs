using System.Xml.Linq;
using AITweaker.Core.Image;

namespace AITweaker.Core.Tests;

public class UnattendTests
{
    private static readonly XNamespace U = "urn:schemas-microsoft-com:unattend";

    [Fact]
    public void ProducesValidXmlWithLocalAccount()
    {
        var xml = UnattendGenerator.Generate(new UnattendOptions { UserName = "Gamer", Password = "p<a>ss&\"" });
        var doc = XDocument.Parse(xml);
        var names = doc.Descendants(U + "LocalAccount").Elements(U + "Name").Select(e => e.Value).ToList();
        Assert.Equal(new[] { "Gamer" }, names);
        Assert.Equal("p<a>ss&\"", doc.Descendants(U + "LocalAccount").Descendants(U + "Value").First().Value);
        Assert.Contains(UnattendGenerator.GenericProKey, xml);
        Assert.Contains("BypassTPMCheck", xml);
        Assert.Contains("PreventDeviceEncryption", xml);
        Assert.Contains("AITweaker.exe", xml);
        Assert.Equal("true", doc.Descendants(U + "HideOnlineAccountScreens").Single().Value);
    }

    [Fact]
    public void NeverWipesDisksAutomatically()
    {
        var xml = UnattendGenerator.Generate(new UnattendOptions());
        Assert.DoesNotContain("DiskConfiguration", xml);
        Assert.DoesNotContain("WillWipeDisk", xml);
    }

    [Fact]
    public void OptionsCanBeTurnedOff()
    {
        var xml = UnattendGenerator.Generate(new UnattendOptions { BypassRequirements = false, CopyTweaker = false, SelectPro = false, DisableAutoBitLocker = false });
        Assert.DoesNotContain("LabConfig", xml);
        Assert.DoesNotContain("AITweaker.exe", xml);
        Assert.DoesNotContain("ProductKey", xml);
        Assert.DoesNotContain("PreventDeviceEncryption", xml);
        XDocument.Parse(xml);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("a@b")]
    [InlineData("Administrator")]
    [InlineData("AVeryLongUserNameOverTwenty")]
    public void RejectsInvalidUserNames(string name) =>
        Assert.Throws<ArgumentException>(() => UnattendGenerator.Generate(new UnattendOptions { UserName = name }));

    [Theory]
    [InlineData("Мой ПК")]            // setup stops in the specialize pass on these
    [InlineData("GAMING-PC-2026-NEW")] // over 15 characters
    [InlineData("12345")]
    [InlineData("Gamer")]             // same as the user name
    public void RejectsComputerNamesSetupWouldStopOn(string pc) =>
        Assert.Throws<ArgumentException>(() => UnattendGenerator.Generate(new UnattendOptions { UserName = "Gamer", ComputerName = pc }));

    [Fact]
    public void HomeEditionGetsTheHomeKeyAndThePasswordIsClearedAfterLogon()
    {
        var xml = UnattendGenerator.Generate(new UnattendOptions { UserName = "Gamer", ComputerName = "GAMING-PC", Edition = "Core" });
        Assert.Contains(UnattendGenerator.GenericHomeKey, xml);
        Assert.DoesNotContain(UnattendGenerator.GenericProKey, xml);
        Assert.Contains("DefaultPassword", xml);
        XDocument.Parse(xml);
    }

    [Fact]
    public void StartsAITweakerAfterSetup()
    {
        var xml = UnattendGenerator.Generate(new UnattendOptions());
        Assert.Contains("--first-run", xml);
        Assert.True(xml.IndexOf("<Order>1</Order>", StringComparison.Ordinal) < xml.IndexOf("--first-run", StringComparison.Ordinal));
        Assert.DoesNotContain("--first-run", UnattendGenerator.Generate(new UnattendOptions { RunTweakerAfterSetup = false }));
        System.Xml.Linq.XDocument.Parse(xml);
    }
}
