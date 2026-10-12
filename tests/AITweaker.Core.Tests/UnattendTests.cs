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
        Assert.DoesNotContain("FirstLogonCommands", xml);
        Assert.DoesNotContain("ProductKey", xml);
        Assert.DoesNotContain("PreventDeviceEncryption", xml);
        XDocument.Parse(xml);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("a@b")]
    public void RejectsInvalidUserNames(string name) =>
        Assert.Throws<ArgumentException>(() => UnattendGenerator.Generate(new UnattendOptions { UserName = name }));
}
