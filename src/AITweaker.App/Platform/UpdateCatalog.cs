using System.Text;
using AITweaker.Core.Image;

namespace AITweaker.App.Platform;

/// <summary>Downloads the newest cumulative update from the official Microsoft Update Catalog and adds it to a mounted image.</summary>
public static class UpdateCatalog
{
    /// <returns>The integrated KB, e.g. "KB5066835".</returns>
    public static async Task<string> IntegrateLatestAsync(string mount, string displayVersion, string workRoot, Action<double> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayVersion)) throw new InvalidOperationException("unknown Windows version in the image");
        var search = await Downloader.Http.GetStringAsync(UpdateCatalogParser.SearchUrl + Uri.EscapeDataString(UpdateCatalogParser.Query(displayVersion)), ct);
        var update = UpdateCatalogParser.Latest(search, displayVersion)
            ?? throw new InvalidOperationException($"no cumulative update for Windows 11 {displayVersion} in the Microsoft Update Catalog");
        AppLog.Info("image", $"latest cumulative update: {update.Title} ({update.Date:yyyy-MM-dd})");

        using var resp = await Downloader.Http.PostAsync(UpdateCatalogParser.DownloadDialogUrl,
            new StringContent(UpdateCatalogParser.DownloadRequest(update.Id), Encoding.UTF8, "application/x-www-form-urlencoded"), ct);
        resp.EnsureSuccessStatusCode();
        var urls = UpdateCatalogParser.DownloadUrls(await resp.Content.ReadAsStringAsync(ct))
            .Where(u => u.EndsWith(".msu", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".cab", StringComparison.OrdinalIgnoreCase)).ToList();
        if (urls.Count == 0) throw new InvalidOperationException("the catalog returned no download links");

        var folder = Path.Combine(workRoot, "updates");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        for (int i = 0; i < urls.Count; i++)
        {
            var file = Path.Combine(folder, Path.GetFileName(new Uri(urls[i]).LocalPath));
            await Downloader.DownloadAsync(urls[i], file, (d, t) => progress((i + (t is > 0 ? (double)d / t.Value : 0)) / urls.Count * 0.6), ct);
        }

        // Checkpoint cumulative updates: DISM finds the prerequisite .msu files in the same folder.
        var kb = update.Kb.ToLowerInvariant();
        var target = Directory.GetFiles(folder).FirstOrDefault(f => kb.Length > 0 && Path.GetFileName(f).Contains(kb, StringComparison.OrdinalIgnoreCase))
                     ?? Directory.GetFiles(folder).OrderBy(f => f).Last();
        progress(0.65);
        var r = await ProcessRunner.RunAsync("dism.exe", $"/Image:\"{mount}\" /Add-Package /PackagePath:\"{target}\" /NoRestart", TimeSpan.FromMinutes(120), ct: ct);
        Directory.Delete(folder, recursive: true);
        if (!r.Success) throw new InvalidOperationException("DISM /Add-Package: " + (r.Output + r.Error).Trim().Split('\n').LastOrDefault());
        progress(1);
        return update.Kb;
    }
}
