using System.Text.Json;

namespace AITweaker.Core.Update;

/// <summary>The newest published AI Tweaker release, read from the GitHub releases API.</summary>
/// <param name="Sha256">Lower-case hex digest GitHub computed for the uploaded exe; null on releases from before GitHub published digests.</param>
public sealed record ReleaseInfo(string Tag, string Version, bool Newer, string Notes, DateTime? Published, string? AssetUrl, long Size, string? Sha256, string PageUrl)
{
    public const string AssetName = "AITweaker.exe";

    /// <summary>Reads a <c>releases/latest</c> response and compares its tag with the running version.</summary>
    public static ReleaseInfo Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        string? url = null, sha = null;
        long size = 0;
        if (r.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                if (!string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                url = a.GetProperty("browser_download_url").GetString();
                size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = digest[7..].ToLowerInvariant();
                break;
            }
        var latest = ParseTag(tag);
        DateTime? published = r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTime() : null;
        return new ReleaseInfo(tag, latest?.ToString(3) ?? tag.TrimStart('v', 'V'), latest is not null && latest > Normalize(current) && url is not null,
            r.TryGetProperty("body", out var b) ? (b.GetString() ?? "").Replace("\r\n", "\n") : "",
            published, url, size, sha, r.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "");
    }

    /// <summary>"v1.2.0" / "1.2" → 1.2.0; anything else (pre-release suffixes included) → null, so it never counts as newer.</summary>
    public static Version? ParseTag(string tag)
    {
        var t = tag.Trim().TrimStart('v', 'V');
        return System.Version.TryParse(t, out var v) && t.All(c => char.IsDigit(c) || c == '.') ? Normalize(v) : null;
    }

    /// <summary>Compares on major.minor.build only: the assembly's fourth part is always 0.</summary>
    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
