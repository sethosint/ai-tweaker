using AITweaker.Core.Engine;

namespace AITweaker.Core.Tests;

public class LinkPickerTests
{
    [Fact]
    public void PicksTheNewestFromAFolderListing()
    {
        const string html = """
            <a href="../">../</a>
            <a href="vlc-3.0.21-win64.exe">vlc-3.0.21-win64.exe</a>
            <a href="vlc-3.0.24-win64.exe">vlc-3.0.24-win64.exe</a>
            <a href="vlc-3.0.24-win64.exe.sha256">vlc-3.0.24-win64.exe.sha256</a>
            <a href="vlc-3.0.24-win64.msi">vlc-3.0.24-win64.msi</a>
            """;
        var r = LinkPicker.Newest(html, "https://download.videolan.org/pub/videolan/vlc/last/win64/", @"^vlc-[\d.]+-win64\.exe$");
        Assert.Equal(("vlc-3.0.24-win64.exe", "https://download.videolan.org/pub/videolan/vlc/last/win64/vlc-3.0.24-win64.exe"), r);
    }

    [Fact]
    public void ResolvesSiteRelativeLinks()
    {
        const string html = """<a href="/Everything-1.4.1.1032.x64.msi">MSI</a> <a href='/Everything-1.4.1.1032.x64-Setup.exe'>Installer</a> <a href="/Everything-1.4.1.1026.x64-Setup.exe">old</a>""";
        var r = LinkPicker.Newest(html, "https://www.voidtools.com/downloads/", @"^Everything-[\d.]+\.x64-Setup\.exe$");
        Assert.Equal("https://www.voidtools.com/Everything-1.4.1.1032.x64-Setup.exe", r?.Url);
    }

    [Fact]
    public void NothingMatchingIsNull() => Assert.Null(LinkPicker.Newest("<a href='a.zip'>", "https://x.example/", @"\.exe$"));
}
