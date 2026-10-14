using System.Text;
using AITweaker.Core.Image;

namespace AITweaker.App.Platform;

/// <summary>Downloads the newest cumulative update from the official Microsoft Update Catalog and adds it to a mounted image.</summary>
public static class UpdateCatalog
{
    /// <returns>The integrated KB, e.g. "KB5066835".</returns>
    public static async Task<string> IntegrateLatestAsync(string mount, string displayVersion, string workRoot, string scratch, Action<double> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayVersion)) throw new InvalidOperationException("unknown Windows version in the image");
        // checkpoint cumulative updates need a 24H2-or-newer servicing stack: an older Windows here can't add them
        if (Environment.OSVersion.Version.Build < 26100)
            throw new InvalidOperationException("this PC runs a Windows older than 24H2, whose DISM can't add current updates");

        var search = await CatalogAsync(c => Downloader.Http.GetStringAsync(UpdateCatalogParser.SearchUrl + Uri.EscapeDataString(UpdateCatalogParser.Query(displayVersion)), c), ct);
        var update = UpdateCatalogParser.Latest(search, displayVersion)
            ?? throw new InvalidOperationException($"no cumulative update for Windows 11 {displayVersion} in the Microsoft Update Catalog");
        AppLog.Info("image", $"latest cumulative update: {update.Title} ({update.Date:yyyy-MM-dd})");

        var dialog = await CatalogAsync(async c =>
        {
            using var resp = await Downloader.Http.PostAsync(UpdateCatalogParser.DownloadDialogUrl,
                new StringContent(UpdateCatalogParser.DownloadRequest(update.Id), Encoding.UTF8, "application/x-www-form-urlencoded"), c);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(c);
        }, ct);
        var urls = UpdateCatalogParser.DownloadUrls(dialog)
            .Where(u => u.EndsWith(".msu", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".cab", StringComparison.OrdinalIgnoreCase)).ToList();
        if (urls.Count == 0) throw new InvalidOperationException("the catalog returned no download links");

        var folder = Path.Combine(workRoot, "updates");
        try
        {
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
            var r = await ProcessRunner.RunAsync("dism.exe", $"/Image:\"{mount}\" /ScratchDir:\"{scratch}\" /Add-Package /PackagePath:\"{target}\" /NoRestart", TimeSpan.FromHours(4), ct: ct);
            if (!r.Success) throw new InvalidOperationException("DISM /Add-Package: " + (r.Output + r.Error).Trim().Split('\n').LastOrDefault());
            progress(1);
            return update.Kb;
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn("image", $"update files left in {folder}: {ex.Message}"); }
        }
    }

    /// <summary>One catalog page: a minute at most, so a hung page can't stall the build for hours.</summary>
    private static async Task<string> CatalogAsync(Func<CancellationToken, Task<string>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("the Microsoft Update Catalog didn't answer within a minute");
        }
    }
}
