using AITweaker.Core.Update;

namespace AITweaker.Core.Tests;

public class UpdateTests
{
    private static string Release(string tag, string digest = "\"digest\": \"sha256:544B95787C1C2D01BB7DDE1C581045B54CB6D99F755CC066842456E6B8C65EFF\",", string asset = "AITweaker.exe") => $$"""
        {
          "tag_name": "{{tag}}", "html_url": "https://github.com/sethosint/ai-tweaker/releases/tag/{{tag}}", "published_at": "2026-10-13T22:45:02Z",
          "body": "## AI Tweaker\r\n\r\n- one\r\n\r\n---\r\n\r\n**English.**",
          "assets": [
            { "name": "notes.txt", "size": 10, "browser_download_url": "https://example.invalid/notes.txt" },
            { "name": "{{asset}}", "size": 61123597, {{digest}} "browser_download_url": "https://github.com/sethosint/ai-tweaker/releases/download/{{tag}}/{{asset}}" }
          ]
        }
        """;

    [Fact]
    public void NewerTagWithItsExeAndChecksum()
    {
        var r = ReleaseInfo.Parse(Release("v1.2.0"), new Version(1, 1, 0, 0));
        Assert.True(r.Newer);
        Assert.Equal("1.2.0", r.Version);
        Assert.Equal("544b95787c1c2d01bb7dde1c581045b54cb6d99f755cc066842456e6b8c65eff", r.Sha256);
        Assert.Equal(61123597, r.Size);
        Assert.EndsWith("/v1.2.0/AITweaker.exe", r.AssetUrl);
        Assert.DoesNotContain("\r", r.Notes);
        Assert.Equal(new DateTime(2026, 10, 13), r.Published!.Value.Date);
    }

    [Theory]
    [InlineData("v1.1.0", false)]   // same version
    [InlineData("v1.0.9", false)]   // older
    [InlineData("v1.10.0", true)]   // numeric, not string, comparison
    [InlineData("v2.0", true)]
    [InlineData("v1.2.0-beta", false)] // pre-releases never count
    [InlineData("nightly", false)]
    public void ComparesVersions(string tag, bool newer) =>
        Assert.Equal(newer, ReleaseInfo.Parse(Release(tag), new Version(1, 1, 0, 0)).Newer);

    [Fact]
    public void WithoutTheExeThereIsNothingToInstall()
    {
        var r = ReleaseInfo.Parse(Release("v9.0.0", asset: "AITweaker.zip"), new Version(1, 1, 0));
        Assert.False(r.Newer);
        Assert.Null(r.AssetUrl);
    }

    [Fact]
    public void OldReleasesHaveNoChecksum() =>
        Assert.Null(ReleaseInfo.Parse(Release("v1.2.0", digest: ""), new Version(1, 1, 0)).Sha256);
}
