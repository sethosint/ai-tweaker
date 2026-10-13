using System.Text.RegularExpressions;

namespace AITweaker.Core.Engine;

/// <summary>Finds the newest installer linked from a vendor's download page or folder listing.</summary>
public static partial class LinkPicker
{
    [GeneratedRegex("href\\s*=\\s*[\"']([^\"'#?]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    [GeneratedRegex(@"\d+(\.\d+){1,3}")]
    private static partial Regex VersionRx();

    /// <summary>Every href whose file name matches <paramref name="pattern"/>, highest version first; relative links resolved against the page.</summary>
    public static (string Name, string Url)? Newest(string html, string page, string pattern)
    {
        var rx = new Regex(pattern);
        var best = Href().Matches(html)
            .Select(m => m.Groups[1].Value)
            .Select(href => (href, file: Uri.UnescapeDataString(href[(href.LastIndexOf('/') + 1)..])))
            .Where(x => rx.IsMatch(x.file))
            .OrderByDescending(x => VersionOf(x.file))
            .FirstOrDefault();
        return best.href is null ? null : (best.file, new Uri(new Uri(page), best.href).ToString());
    }

    private static Version VersionOf(string file) =>
        VersionRx().Match(file) is { Success: true } m && Version.TryParse(m.Value, out var v) ? v : new Version(0, 0);
}
