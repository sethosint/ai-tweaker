using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using AITweaker.Core.Update;

namespace AITweaker.App.Platform;

/// <summary>
/// Updates AI Tweaker from its own GitHub releases: the new AITweaker.exe is downloaded next to the running one, checked
/// against the SHA-256 GitHub computed when it was uploaded, swapped in (a running exe can be renamed, not overwritten)
/// and started; the old copy is deleted on the next start.
/// </summary>
public static class Updater
{
    public const string Repo = "sethosint/ai-tweaker";
    public const string RestartArg = "--updated";

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown exe path");

    public static async Task<ReleaseInfo> CheckAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var resp = await Downloader.Http.SendAsync(req, cts.Token);
        resp.EnsureSuccessStatusCode();
        var info = ReleaseInfo.Parse(await resp.Content.ReadAsStringAsync(cts.Token), Current);
        AppLog.Info("update", info.Newer ? $"version {info.Version} is available (running {Current.ToString(3)})" : $"up to date ({Current.ToString(3)})");
        return info;
    }

    /// <summary>Downloads, verifies and swaps in the new exe, then starts it. The caller closes this instance afterwards.</summary>
    public static async Task InstallAsync(Action<double> progress, CancellationToken ct = default)
    {
        var exe = Exe;
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Updates work in the published AITweaker.exe only");
        var info = await CheckAsync(ct);
        if (!info.Newer || info.AssetUrl is null) throw new InvalidOperationException("No newer version");
        if (info.Sha256 is null) throw new InvalidOperationException("The release has no checksum — download it from GitHub by hand");

        var fresh = exe + ".new";
        var old = exe + ".old";
        TryDelete(fresh);
        await Downloader.DownloadAsync(info.AssetUrl, fresh, (done, total) => progress((double)done / (total ?? (info.Size > 0 ? info.Size : done))), ct);

        string actual;
        await using (var f = File.OpenRead(fresh)) actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
        if (actual != info.Sha256 || (info.Size > 0 && new FileInfo(fresh).Length != info.Size))
        {
            TryDelete(fresh);
            throw new InvalidOperationException("The downloaded file doesn't match the release checksum — nothing was changed");
        }

        TryDelete(old);
        File.Move(exe, old);
        try
        {
            File.Move(fresh, exe);
        }
        catch
        {
            File.Move(old, exe); // put the running version back
            throw;
        }
        AppLog.Ok("update", $"updated to {info.Version}, restarting");
        Process.Start(new ProcessStartInfo(exe, RestartArg) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    /// <summary>Removes the previous exe left by an update; the old process may still be exiting, so it retries for a while.</summary>
    public static void CleanupAfterUpdate()
    {
        string old, fresh;
        try { old = Exe + ".old"; fresh = Exe + ".new"; }
        catch (InvalidOperationException) { return; }
        if (!File.Exists(old) && !File.Exists(fresh)) return;
        Task.Run(async () =>
        {
            for (int i = 0; i < 30 && (File.Exists(old) || File.Exists(fresh)); i++)
            {
                TryDelete(old);
                TryDelete(fresh);
                await Task.Delay(1000);
            }
        });
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* still in use; retried later */ }
    }
}
