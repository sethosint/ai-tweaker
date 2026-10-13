using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AITweaker.Core.Nvidia;

/// <summary>A driver release offered by NVIDIA's own driver lookup (the one behind nvidia.com/drivers).</summary>
public sealed record NvidiaDriverOffer(string Version, string Name, string? ReleaseDate, string Url, string? Size, string? DetailsUrl);

/// <summary>
/// Finds the newest Game Ready driver for a given GPU through NVIDIA's public lookup: the product list gives the
/// series and product ids for the card, the manual-lookup service returns the newest WHQL DCH driver for it.
/// </summary>
public static partial class NvidiaDriver
{
    public const string ProductListUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";
    public const string DriversPage = "https://www.nvidia.com/en-us/drivers/";

    public static string LookupUrl(string psid, string pfid, bool windows11) =>
        "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" +
        $"?func=DriverManualLookup&psid={psid}&pfid={pfid}&osID={(windows11 ? 135 : 57)}&languageCode=1033" +
        "&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=0&qnf=0&sort1=0&numberOfResults=1";

    /// <summary>Windows driver version → NVIDIA version: 32.0.15.8157 → 581.57.</summary>
    public static string? FromWindowsVersion(string? windows)
    {
        if (string.IsNullOrWhiteSpace(windows)) return null;
        var parts = windows.Split('.');
        if (parts.Length < 4) return null;
        var digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5 || !digits.All(char.IsAsciiDigit)) return null;
        digits = digits[^5..];
        return $"{int.Parse(digits[..3], CultureInfo.InvariantCulture)}.{digits[3..]}";
    }

    /// <summary>Compares NVIDIA versions like 581.57 and 576.88 numerically.</summary>
    public static int Compare(string? a, string? b)
    {
        static (int, int) P(string? v)
        {
            var m = VersionRx().Match(v ?? "");
            return m.Success ? (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value.PadRight(2, '0'), CultureInfo.InvariantCulture)) : (0, 0);
        }
        return P(a).CompareTo(P(b));
    }

    [GeneratedRegex(@"(\d+)\.(\d+)")]
    private static partial Regex VersionRx();

    /// <summary>"NVIDIA GeForce RTX 4070 Laptop GPU" → "geforce rtx 4070 laptop gpu".</summary>
    public static string Normalize(string name) =>
        Regex.Replace(name.Replace("(TM)", "", StringComparison.OrdinalIgnoreCase).Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("NVIDIA", "", StringComparison.OrdinalIgnoreCase), @"\s+", " ").Trim().ToLowerInvariant();

    /// <summary>Series id (psid) and product id (pfid) for the GPU in NVIDIA's product list, or null when it isn't listed.</summary>
    public static (string Psid, string Pfid)? MatchProduct(string productListXml, string gpuName, bool laptop)
    {
        var items = XDocument.Parse(productListXml).Descendants("LookupValue")
            .Select(e => (Name: Normalize((string?)e.Element("Name") ?? ""), Pfid: (string?)e.Element("Value") ?? "",
                Psid: (string?)e.Attribute("ParentID") ?? (string?)e.Element("ParentID") ?? ""))
            .Where(x => x.Name.Length > 0 && x.Pfid.Length > 0 && x.Psid.Length > 0)
            .ToList();
        var name = Normalize(gpuName);
        var wanted = new List<string> { name };
        if (laptop && !name.Contains("laptop")) wanted.Insert(0, name + " laptop gpu");
        if (name.EndsWith(" laptop gpu")) wanted.Add(name.Replace(" laptop gpu", " (notebooks)"));
        foreach (var w in wanted)
            if (items.FirstOrDefault(x => x.Name == w) is { Pfid.Length: > 0 } hit) return (hit.Psid, hit.Pfid);
        return null;
    }

    /// <summary>The first driver in a manual-lookup answer (fields there are URL-encoded).</summary>
    public static NvidiaDriverOffer? ParseLookup(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0) return null;
        if (!ids[0].TryGetProperty("downloadInfo", out var info)) return null;
        string? S(string k) => info.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? WebUtility.UrlDecode(v.GetString()) : null;
        var url = S("DownloadURL");
        var version = S("Version");
        if (url is null || version is null || !IsOfficialUrl(url)) return null;
        return new NvidiaDriverOffer(version, S("Name") ?? "GeForce Game Ready Driver", S("ReleaseDateTime"), url, S("DownloadURLFileSize"), S("DetailsURL"));
    }

    /// <summary>Only HTTPS downloads from NVIDIA's own servers are accepted.</summary>
    public static bool IsOfficialUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && (u.Host.Equals("nvidia.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase))
        && u.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Arguments for setup.exe of an unpacked driver: silent, no reboot, only the chosen components.
    /// Component ids differ from their folder names (HDAudio.Driver lives in HDAudio).
    /// </summary>
    public static string SetupArgs(IEnumerable<string> presentFolders, bool hdAudio, bool physX, bool clean)
    {
        var have = presentFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string> { "-s", "-noreboot", "-noeula" };
        if (clean) parts.Add("-clean");
        parts.Add("Display.Driver");
        if (hdAudio && have.Contains("HDAudio")) parts.Add("HDAudio.Driver");
        if (physX && have.Contains("PhysX")) parts.Add("PhysX");
        return string.Join(' ', parts);
    }
}
