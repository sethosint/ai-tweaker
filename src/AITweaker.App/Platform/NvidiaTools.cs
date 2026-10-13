using System.IO.Compression;
using AITweaker.Core.Nvidia;
using AITweaker.Core.Profile;

namespace AITweaker.App.Platform;

public sealed record NvidiaDriverStatus(string Gpu, string? Installed, DateTime? InstalledDate, NvidiaDriverOffer? Latest, bool UpdateAvailable, string? Error);

public sealed record NvidiaInstallResult(bool Ok, int ExitCode, string Version, bool Minimal, string? Error);

/// <summary>
/// NVIDIA without the extras: the newest official driver for this exact card, unpacked and installed silently with
/// only the driver, HD Audio and PhysX (no telemetry, no bundled apps); the global 3D settings are written with
/// NVIDIA Profile Inspector (Orbmu2k/nvidiaProfileInspector, MIT), downloaded from its GitHub releases.
/// </summary>
public static class NvidiaTools
{
    private static string InspectorDir => Path.Combine(ProgramInstaller.ToolsDir, "NvidiaProfileInspector");
    private static readonly SemaphoreSlim InspectorGate = new(1, 1);

    // ------------------------------------------------------------------ driver

    public static async Task<NvidiaDriverStatus> CheckAsync(SystemProfile p, CancellationToken ct = default)
    {
        var g = p.Gpus.FirstOrDefault(x => x.Vendor == "NVIDIA") ?? throw new InvalidOperationException("No NVIDIA graphics card found");
        var installed = NvidiaDriver.FromWindowsVersion(g.DriverVersion);
        try
        {
            var products = await Downloader.Http.GetStringAsync(NvidiaDriver.ProductListUrl, ct);
            var ids = NvidiaDriver.MatchProduct(products, g.Name, p.Machine.IsLaptop)
                ?? throw new InvalidOperationException($"{g.Name} is not in NVIDIA's product list");
            var json = await Downloader.Http.GetStringAsync(NvidiaDriver.LookupUrl(ids.Psid, ids.Pfid, p.Os.Build >= 22000), ct);
            var offer = NvidiaDriver.ParseLookup(json) ?? throw new InvalidOperationException("NVIDIA returned no driver for this card");
            return new NvidiaDriverStatus(g.Name, installed, g.DriverDate, offer, NvidiaDriver.Compare(offer.Version, installed) > 0, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Warn("nvidia", "driver lookup failed: " + ex.Message);
            return new NvidiaDriverStatus(g.Name, installed, g.DriverDate, null, false, ex.Message);
        }
    }

    /// <summary>
    /// Downloads the driver, checks NVIDIA's signature, unpacks it and runs setup.exe for the chosen components only.
    /// When it can't be unpacked, the package itself is run silently (a full install).
    /// </summary>
    public static async Task<NvidiaInstallResult> InstallAsync(NvidiaDriverOffer offer, bool hdAudio, bool physX, bool clean, Action<string, double> progress, CancellationToken ct = default)
    {
        if (!NvidiaDriver.IsOfficialUrl(offer.Url)) throw new InvalidOperationException("Not an NVIDIA download: " + offer.Url);
        var file = Path.Combine(AppPaths.Downloads, Path.GetFileName(new Uri(offer.Url).LocalPath));

        if (!File.Exists(file) || !await SignedByNvidiaAsync(file, ct))
        {
            progress("download", 0);
            await Downloader.DownloadAsync(offer.Url, file, (done, total) => { if (total > 0) progress("download", (double)done / total.Value); }, ct);
            if (!await SignedByNvidiaAsync(file, ct))
            {
                File.Delete(file);
                throw new InvalidOperationException("The downloaded driver is not signed by NVIDIA; it was deleted.");
            }
        }
        AppLog.Ok("nvidia", $"{Path.GetFileName(file)}: signature verified");

        progress("extract", 0);
        var dir = Path.Combine(AppPaths.Downloads, "nvidia-" + offer.Version);
        var setup = await ExtractAsync(file, dir, ct);

        progress("install", 0);
        string exe, args;
        if (setup is not null)
        {
            exe = setup;
            args = NvidiaDriver.SetupArgs(Directory.EnumerateDirectories(dir).Select(Path.GetFileName).OfType<string>(), hdAudio, physX, clean);
        }
        else
        {
            exe = file;
            args = "-s -noreboot -noeula" + (clean ? " -clean" : "");
        }
        AppLog.Info("nvidia", $"installing {offer.Version}: {Path.GetFileName(exe)} {args}");
        var r = await ProcessRunner.RunAsync(exe, args, TimeSpan.FromMinutes(30), ct: ct, workingDir: Path.GetDirectoryName(exe));
        // 0 = done, 1 = done but Windows wants a restart
        var ok = r.ExitCode is 0 or 1;
        if (ok)
        {
            AppLog.Ok("nvidia", $"driver {offer.Version} installed ({(setup is null ? "full package" : "driver" + (hdAudio ? " + HD Audio" : "") + (physX ? " + PhysX" : ""))})");
            SettingsStore.MarkRebootPending();
            try { Directory.Delete(dir, true); } catch { /* setup may still hold a file */ }
        }
        else
        {
            AppLog.Error("nvidia", $"setup exited with {r.ExitCode} {r.Error.Trim()}");
        }
        return new NvidiaInstallResult(ok, r.ExitCode, offer.Version, setup is not null, ok ? null : $"setup.exe exit code {r.ExitCode}");
    }

    private static async Task<bool> SignedByNvidiaAsync(string file, CancellationToken ct)
    {
        var sig = await ProcessRunner.PowerShellAsync($"$s = Get-AuthenticodeSignature -FilePath {ProcessRunner.PsQuote(file)}; \"$($s.Status)|$($s.SignerCertificate.Subject)\"", TimeSpan.FromMinutes(2), ct: ct);
        return sig.Output.Contains("Valid|") && sig.Output.Contains("NVIDIA Corporation", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The driver package is a 7-Zip self-extractor: unpacks it with 7-Zip (installed, or the official 7zr.exe).</summary>
    private static async Task<string?> ExtractAsync(string package, string dir, CancellationToken ct)
    {
        var sevenZip = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Path.Combine(Environment.GetFolderPath(f), "7-Zip", "7z.exe"))
            .FirstOrDefault(File.Exists);
        if (sevenZip is null)
        {
            var reduced = Path.Combine(ProgramInstaller.ToolsDir, "7zr.exe");
            if (!File.Exists(reduced))
            {
                try
                {
                    Directory.CreateDirectory(ProgramInstaller.ToolsDir);
                    await Downloader.DownloadAsync("https://www.7-zip.org/a/7zr.exe", reduced, null, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Warn("nvidia", "7-Zip is not available: " + ex.Message);
                    return null;
                }
            }
            sevenZip = reduced;
        }
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var r = await ProcessRunner.RunAsync(sevenZip, $"x \"{package}\" -o\"{dir}\" -y", TimeSpan.FromMinutes(10), ct: ct);
        var setup = Path.Combine(dir, "setup.exe");
        if (r.ExitCode == 0 && File.Exists(setup) && Directory.Exists(Path.Combine(dir, "Display.Driver"))) return setup;
        AppLog.Warn("nvidia", $"could not unpack the driver ({r.ExitCode}); the full package will be installed");
        return null;
    }

    // ------------------------------------------------------------------ global 3D settings (Profile Inspector)

    public static async Task<string> EnsureInspectorAsync(CancellationToken ct = default)
    {
        static string? Find() => Directory.Exists(InspectorDir)
            ? Directory.EnumerateFiles(InspectorDir, "nvidiaProfileInspector.exe", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (Find() is { } exe) return exe;
        var (name, url) = await Downloader.LatestGitHubAssetAsync("Orbmu2k/nvidiaProfileInspector", n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), ct);
        var zip = Path.Combine(AppPaths.Downloads, name);
        await Downloader.DownloadAsync(url, zip, null, ct);
        if (Directory.Exists(InspectorDir)) Directory.Delete(InspectorDir, true);
        ZipFile.ExtractToDirectory(zip, InspectorDir);
        File.Delete(zip);
        AppLog.Ok("nvidia", $"NVIDIA Profile Inspector downloaded ({name})");
        return Find() ?? throw new InvalidOperationException("nvidiaProfileInspector.exe not found in " + name);
    }

    /// <summary>
    /// Writes settings to the driver's Base Profile and returns the values they had before (only the customized ones).
    /// The current profile is exported first — the export is kept in the backups folder — and the file that is
    /// imported is that profile with the new values, so nothing else changes even with builds that rebuild the
    /// profile on import (newer builds merge with -mergeImport).
    /// </summary>
    public static async Task<Dictionary<uint, uint>> SetBaseProfileAsync(IReadOnlyCollection<NvidiaProfileFile.Setting> settings, CancellationToken ct = default)
    {
        var exe = await EnsureInspectorAsync(ct);
        await InspectorGate.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(exe)!;
            var started = DateTime.Now.AddSeconds(-2);
            await ProcessRunner.RunAsync(exe, "-exportCustomized", TimeSpan.FromSeconds(90), ct: ct, workingDir: dir);
            var export = new DirectoryInfo(dir).GetFiles("*.nip")
                .Where(f => f.LastWriteTime >= started).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            string? xml = null;
            if (export is not null)
            {
                var backup = Path.Combine(AppPaths.Backups, $"nvidia-profiles-{DateTime.Now:yyyyMMdd-HHmmss}.nip");
                File.Move(export.FullName, backup, overwrite: true);
                xml = await File.ReadAllTextAsync(backup, ct);
            }
            var before = xml is null ? [] : NvidiaProfileFile.ReadDwords(xml);

            var nip = Path.Combine(Path.GetTempPath(), $"aitweaker-nvidia-{Guid.NewGuid():N}.nip");
            NvidiaProfileFile.Save(nip, NvidiaProfileFile.WithSettings(xml, settings));
            try
            {
                var r = await ProcessRunner.RunAsync(exe, $"-silentImport -mergeImport \"{nip}\"", TimeSpan.FromSeconds(90), ct: ct, workingDir: dir);
                if (r.ExitCode == -1) throw new InvalidOperationException("NVIDIA Profile Inspector did not finish (is the NVIDIA driver installed?)");
            }
            finally { File.Delete(nip); }
            return before;
        }
        finally { InspectorGate.Release(); }
    }
}
