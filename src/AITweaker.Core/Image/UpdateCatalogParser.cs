using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace AITweaker.Core.Image;

public sealed record CatalogUpdate(string Id, string Title, DateTime Date, string Kb);

/// <summary>
/// Reads Microsoft Update Catalog pages (catalog.update.microsoft.com): picks the newest regular cumulative update
/// for a Windows 11 version and extracts its download links (24H2+ updates come with a checkpoint .msu as well).
/// </summary>
public static partial class UpdateCatalogParser
{
    public const string SearchUrl = "https://www.catalog.update.microsoft.com/Search.aspx?q=";
    public const string DownloadDialogUrl = "https://www.catalog.update.microsoft.com/DownloadDialog.aspx";

    public static string Query(string displayVersion) => $"Cumulative Update for Windows 11 Version {displayVersion} for x64-based Systems";

    [GeneratedRegex(@"<a\s+id=['""](?<id>[0-9a-fA-F-]{36})_link['""][^>]*>\s*(?<title>[^<]+?)\s*</a>", RegexOptions.Singleline)]
    private static partial Regex TitleRx();

    [GeneratedRegex(@"id=['""](?<id>[0-9a-fA-F-]{36})_C4_R\d+['""][^>]*>\s*(?<date>\d{1,2}/\d{1,2}/\d{4})\s*<")]
    private static partial Regex DateRx();

    [GeneratedRegex(@"\(KB(?<kb>\d{6,8})\)", RegexOptions.IgnoreCase)]
    private static partial Regex KbRx();

    [GeneratedRegex(@"files\[\d+\]\.url\s*=\s*['""](?<url>https?://[^'""]+)['""]")]
    private static partial Regex UrlRx();

    /// <summary>The newest non-preview cumulative update for x64, or null if the page lists none.</summary>
    public static CatalogUpdate? Latest(string searchHtml, string displayVersion)
    {
        var dates = DateRx().Matches(searchHtml).ToDictionary(m => m.Groups["id"].Value, m => m.Groups["date"].Value, StringComparer.OrdinalIgnoreCase);
        var want = Query(displayVersion);
        return TitleRx().Matches(searchHtml)
            .Select(m => (Id: m.Groups["id"].Value, Title: WebUtility.HtmlDecode(m.Groups["title"].Value).Trim()))
            .Where(x => x.Title.Contains(want, StringComparison.OrdinalIgnoreCase)
                        && !x.Title.Contains("Preview", StringComparison.OrdinalIgnoreCase)
                        && !x.Title.Contains(".NET", StringComparison.OrdinalIgnoreCase)
                        && !x.Title.Contains("Dynamic", StringComparison.OrdinalIgnoreCase))
            .Select(x => new CatalogUpdate(x.Id, x.Title,
                dates.TryGetValue(x.Id, out var d) && DateTime.TryParseExact(d, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : DateTime.MinValue,
                KbRx().Match(x.Title) is { Success: true } k ? "KB" + k.Groups["kb"].Value : ""))
            .OrderByDescending(u => u.Date)
            .ThenByDescending(u => u.Kb, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Form body for DownloadDialog.aspx.</summary>
    public static string DownloadRequest(string updateId) =>
        "updateIDs=" + Uri.EscapeDataString($$"""[{"size":0,"languages":"","uidInfo":"{{updateId}}","updateID":"{{updateId}}"}]""");

    public static List<string> DownloadUrls(string dialogHtml) =>
        UrlRx().Matches(dialogHtml).Select(m => m.Groups["url"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
