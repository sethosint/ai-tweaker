using AITweaker.Core.Image;

namespace AITweaker.Core.Tests;

public class LocalSetupTests
{
    [Theory]
    [InlineData("clean")]
    [InlineData("dataonly")]
    [InlineData("upgrade")]
    public void ArgumentsUseTheDocumentedSwitches(string mode)
    {
        var a = LocalSetup.Arguments(mode);
        Assert.StartsWith($"/auto {mode} ", a);
        Assert.Contains("/eula accept", a);
        Assert.Contains("/compat ignorewarning", a);
        Assert.DoesNotContain("/quiet", a); // the user sees Windows Setup's own progress
    }

    [Fact]
    public void RejectsUnknownModes() => Assert.Throws<ArgumentException>(() => LocalSetup.Arguments("format c:"));

    [Fact]
    public void ReadsTheImageBuild()
    {
        const string dism = "Deployment Image Servicing and Management tool\r\nDetails for image : E:\\sources\\install.wim\r\n\r\nIndex : 1\r\nName : Windows 11 Home\r\nArchitecture : x64\r\nHal : <undefined>\r\nVersion : 10.0.26100\r\nServicePack Build : 4061\r\n";
        Assert.Equal(26100, LocalSetup.ImageBuild(dism));
        Assert.Null(LocalSetup.ImageBuild("Error: 2"));
    }

    [Fact]
    public void BlocksOlderOrNonWindows11Images()
    {
        Assert.Null(LocalSetup.Problem(26100, 26100));
        Assert.Null(LocalSetup.Problem(22631, 26100));
        Assert.Equal("older-image", LocalSetup.Problem(26200, 26100));
        Assert.Equal("not-windows11", LocalSetup.Problem(19045, 19045));
        Assert.Equal("unknown-image", LocalSetup.Problem(26100, null));
    }
}
