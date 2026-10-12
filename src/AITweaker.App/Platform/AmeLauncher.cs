using System.Diagnostics;
using System.IO.Compression;

namespace AITweaker.App.Platform;

/// <summary>Downloads the official AME Wizard and a playbook (AtlasOS / ReviOS) and launches them.</summary>
public static class AmeLauncher
{
    public const string WizardUrl = "https://download.ameliorated.io/AME%20Wizard%20Beta.zip";

    public static readonly Dictionary<string, (string Repo, string Title, string Site)> Playbooks = new()
    {
        ["atlas"] = ("Atlas-OS/Atlas", "AtlasOS", "https://atlasos.net"),
        ["revi"] = ("meetrevision/playbook", "ReviOS", "https://revi.cc"),
    };

    private static string Root => Directory.CreateDirectory(Path.Combine(AppPaths.Downloads, "ame")).FullName;

    public static async Task<string> RunAsync(string playbookId, Action<string, double> progress, CancellationToken ct = default)
    {
        if (!Playbooks.TryGetValue(playbookId, out var pb)) throw new ArgumentException("Unknown playbook " + playbookId);

        progress("AME Wizard", 0);
        var zip = Path.Combine(Root, "AME Wizard Beta.zip");
        await Downloader.DownloadAsync(WizardUrl, zip, (d, t) => progress("AME Wizard", t is > 0 ? 0.4 * d / t.Value : 0.2), ct);
        var wizardDir = Path.Combine(Root, "wizard");
        if (Directory.Exists(wizardDir)) Directory.Delete(wizardDir, recursive: true);
        ZipFile.ExtractToDirectory(zip, wizardDir);
        var exe = Directory.EnumerateFiles(wizardDir, "*.exe", SearchOption.AllDirectories)
            .OrderByDescending(f => Path.GetFileName(f).Contains("Wizard", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault() ?? throw new FileNotFoundException("AME Wizard executable not found in the archive");

        progress(pb.Title, 0.45);
        var (name, url) = await Downloader.LatestGitHubAssetAsync(pb.Repo, ".apbx", ct);
        var playbook = Path.Combine(Root, name);
        await Downloader.DownloadAsync(url, playbook, (d, t) => progress(pb.Title, 0.45 + (t is > 0 ? 0.5 * d / t.Value : 0.25)), ct);

        progress("launch", 1);
        AppLog.Ok("ame", $"launching AME Wizard with {name}");
        Process.Start(new ProcessStartInfo(exe, $"\"{playbook}\"") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{playbook}\"") { UseShellExecute = true });
        return playbook;
    }
}
