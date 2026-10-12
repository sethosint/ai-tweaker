using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AITweaker.App.Platform;

/// <summary>
/// Rufus (pbatard/rufus) for writing an ordinary Windows ISO with its own setup options: the official build is
/// downloaded from GitHub once and opened with the chosen ISO already selected (<c>--iso</c>, <c>--locale</c>).
/// Afterwards AI Tweaker can copy itself onto the finished stick.
/// </summary>
public static partial class RufusTool
{
    public sealed record OpenResult(string Path, bool Downloaded);

    private static string Exe => Path.Combine(ProgramInstaller.ToolsDir, "Rufus", "rufus.exe");

    [GeneratedRegex(@"^rufus-\d+(\.\d+)+\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex StandardBuild(); // not the _x86 / _arm64 / "p" builds

    public static async Task<OpenResult> OpenAsync(string? iso, string lang, CancellationToken ct = default)
    {
        bool downloaded = false;
        if (!File.Exists(Exe))
        {
            var (name, url) = await Downloader.LatestGitHubAssetAsync("pbatard/rufus", n => StandardBuild().IsMatch(n), ct);
            Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
            await Downloader.DownloadAsync(url, Exe, null, ct);
            // only run it if Windows confirms the author's signature
            var sig = await ProcessRunner.PowerShellAsync($"$s = Get-AuthenticodeSignature -FilePath {ProcessRunner.PsQuote(Exe)}; \"$($s.Status)|$($s.SignerCertificate.Subject)\"", TimeSpan.FromSeconds(30), ct: ct);
            if (!sig.Output.Contains("Valid|") || !sig.Output.Contains("Akeo", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(Exe);
                throw new InvalidOperationException("The downloaded Rufus is not signed by its author; it was deleted. " + sig.Output.Trim());
            }
            AppLog.Ok("rufus", $"downloaded {name} (signature verified)");
            downloaded = true;
        }
        var args = $"--locale={(lang == "en" ? "en-US" : "ru-RU")}";
        if (!string.IsNullOrWhiteSpace(iso) && File.Exists(iso)) args += $" --iso=\"{iso}\"";
        Process.Start(new ProcessStartInfo(Exe, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Exe)! });
        AppLog.Info("rufus", "opened " + args);
        return new OpenResult(Exe, downloaded);
    }

    /// <summary>
    /// Copies AITweaker.exe to <c>X:\AITweaker\</c> on every attached Windows install stick (a removable or external
    /// drive with <c>sources\install.*</c>), the same place the built-in USB writer uses.
    /// </summary>
    public static List<string> CopyTweakerToSticks()
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("can't locate AITweaker.exe");
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        var copied = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady || d.DriveType is not (DriveType.Removable or DriveType.Fixed)) continue;
            var root = d.RootDirectory.FullName;
            if (string.Equals(root, system, StringComparison.OrdinalIgnoreCase)) continue;
            var sources = Path.Combine(root, "sources");
            if (!Directory.Exists(sources) || !Directory.EnumerateFiles(sources, "install.*").Any()) continue;
            var dir = Path.Combine(root, "AITweaker");
            Directory.CreateDirectory(dir);
            File.Copy(self, Path.Combine(dir, "AITweaker.exe"), overwrite: true);
            copied.Add(root.TrimEnd('\\'));
            AppLog.Ok("usb", $"AI Tweaker copied to {root}AITweaker");
        }
        return copied;
    }
}
