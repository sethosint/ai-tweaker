using System.Text.Json;
using AITweaker.Core.Catalog;
using AITweaker.Core.Image;
using AITweaker.App.Platform.Actions;
using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform;

public sealed record ImageBuildResult(string Folder, int Tweaks, int Apps, int Capabilities, bool Drivers, long WimBytes, string Version, string Build, string? Update);

/// <summary>
/// Builds the AI Tweaker Windows 11 image from an official ISO with DISM:
/// exports the Pro edition, removes bloat packages and legacy components, bakes the offline-safe part of the
/// catalog into the image registry (SOFTWARE / SYSTEM / Default user profile), optionally injects this PC's drivers,
/// cleans the component store and re-compresses. The result is a folder the USB writer copies to the stick.
/// </summary>
public static class ImageBuilder
{
    private const string HiveSoftware = @"AIT_SOFTWARE", HiveSystem = @"AIT_SYSTEM", HiveDefault = @"AIT_DEFAULT", HiveVersion = "AIT_VER";
    /// <summary>Written into the finished folder last: a folder without it is an unfinished build.</summary>
    private const string Marker = "AITweaker-build.json";

    public static string WorkRoot => Path.Combine(AppPaths.Data, "image");
    public static string OutputFolder => Path.Combine(WorkRoot, "iso");
    private static string Staging => Path.Combine(WorkRoot, "iso.new");
    private static string Scratch => Path.Combine(WorkRoot, "scratch");
    private static string MountDir => Path.Combine(WorkRoot, "mount");
    private static string WorkWim => Path.Combine(WorkRoot, "install.wim");
    private static string DriversDir => Path.Combine(WorkRoot, "drivers");

    /// <summary>The finished image folder, or null when there is none (or only an interrupted build).</summary>
    public static string? Existing() =>
        File.Exists(Path.Combine(OutputFolder, Marker)) && File.Exists(Path.Combine(OutputFolder, "sources", "install.wim")) ? OutputFolder : null;

    /// <summary>Edition the finished image holds (Professional / Core), from its build marker.</summary>
    public static string? BuiltEdition()
    {
        try
        {
            if (Existing() is not { } folder) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, Marker)));
            return doc.RootElement.TryGetProperty("edition", out var e) ? e.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task<ImageBuildResult> BuildAsync(string isoPath, OfflineImagePlan plan, Action<string, double> progress, CancellationToken ct)
    {
        if (!File.Exists(isoPath)) throw new FileNotFoundException("ISO not found", isoPath);

        progress("prepare", 0.01);
        await RecoverAsync();
        // peak use: ISO files + working image + mounted tree (+ update download) + final export + DISM scratch
        long needGb = plan.Options.IntegrateUpdates ? 35 : 24;
        var drive = new DriveInfo(Path.GetPathRoot(WorkRoot)!);
        if (drive.AvailableFreeSpace < needGb << 30)
            throw new InvalidOperationException($"Need at least {needGb} GB free on {drive.Name} to build the image ({drive.AvailableFreeSpace >> 30} GB free).");
        foreach (var d in new[] { Staging, MountDir, Scratch }) Directory.CreateDirectory(d);
        var edition = plan.Options.Edition == "Core" ? "Core" : "Professional";

        // 1. ISO files (all but the install image) and the chosen edition, exported straight from the mounted ISO
        progress("copy", 0.03);
        string info;
        try
        {
            info = await Ps($$"""
                $img = Mount-DiskImage -ImagePath {{Q(isoPath)}} -PassThru
                try {
                  $src = $null
                  for ($i = 0; $i -lt 15 -and -not $src; $i++) { $src = ($img | Get-Volume).DriveLetter; if (-not $src) { Start-Sleep 1 } }
                  if (-not $src) { throw 'The ISO has no drive letter' }
                  robocopy "$($src):\." {{Q(Staging)}} /E /XF install.wim install.esd /A-:R /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
                  if ($LASTEXITCODE -ge 8) { throw "robocopy failed with code $LASTEXITCODE" }
                  $image = "$($src):\sources\install.wim"
                  if (-not (Test-Path $image)) { $image = "$($src):\sources\install.esd" }
                  if (-not (Test-Path $image)) { throw 'AIT:NOIMAGE' }
                  $pick = $null
                  foreach ($i in (Get-WindowsImage -ImagePath $image)) {
                    $d = Get-WindowsImage -ImagePath $image -Index $i.ImageIndex
                    if ($d.EditionId -eq {{Q(edition)}}) { $pick = $d; break }
                  }
                  if (-not $pick) { throw 'AIT:EDITION' }
                  if ($pick.Architecture -ne 9) { throw 'AIT:ARCH' }
                  if ([int]$pick.Version.Split('.')[2] -lt 26100) { throw "AIT:BUILD $($pick.Version)" }
                  Write-Output "INFO|$($pick.Version)|$($pick.ImageName)"
                  Write-Output 'STEP:export'
                  # fast compression for the working copy: mounting and committing it is much quicker; the final export uses max
                  Export-WindowsImage -SourceImagePath $image -SourceIndex $pick.ImageIndex -DestinationImagePath {{Q(WorkWim)}} -CompressionType fast -ScratchDirectory {{Q(Scratch)}} | Out-Null
                } finally { Dismount-DiskImage -ImagePath {{Q(isoPath)}} | Out-Null }
                """, ct, onLine: line => { if (line == "STEP:export") progress("export", 0.12); });
        }
        catch (Exception ex)
        {
            // cancelled or failed while copying: the script never reached its own Dismount
            await Ps($"Dismount-DiskImage -ImagePath {Q(isoPath)} -ErrorAction SilentlyContinue | Out-Null", CancellationToken.None, TimeSpan.FromMinutes(2), allowFail: true);
            await CleanupAsync();
            if (ex is not InvalidOperationException || !ex.Message.Contains("AIT:")) throw;
            throw new InvalidOperationException(ex.Message switch
            {
                var m when m.Contains("AIT:NOIMAGE") => "This ISO has no sources\\install.wim / install.esd — it isn't a Windows installation ISO.",
                var m when m.Contains("AIT:EDITION") => $"Windows 11 {(edition == "Core" ? "Home" : "Pro")} isn't in this ISO — use the multi-edition ISO from Microsoft.",
                var m when m.Contains("AIT:ARCH") => "This ISO isn't for x64 PCs (ARM64?) — AI Tweaker builds x64 images.",
                _ => "This ISO is older than Windows 11 24H2 (build 26100) — download the current Windows 11 ISO.",
            });
        }
        var picked = info.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("INFO|")) ?? "";
        AppLog.Info("image", $"source: {picked.Replace("INFO|", "").Replace('|', ' ')}");

        bool mounted = false;
        string? update = null;
        (string Version, string Build) os;
        try
        {
            progress("mount", 0.25);
            await Ps($"Mount-WindowsImage -ImagePath {Q(WorkWim)} -Index 1 -Path {Q(MountDir)} -ScratchDirectory {Q(Scratch)} | Out-Null", ct);
            mounted = true;
            os = await ReadVersionAsync(MountDir, ct);
            AppLog.Info("image", $"Windows 11 {os.Version}, build {os.Build}");

            // 2. Newest cumulative update from the Microsoft Update Catalog, so setup starts fully patched
            if (plan.Options.IntegrateUpdates)
            {
                progress("updates", 0.27);
                try
                {
                    update = await UpdateCatalog.IntegrateLatestAsync(MountDir, os.Version, WorkRoot, Scratch, v => progress("updates", 0.27 + v * 0.04), ct);
                    os = await ReadVersionAsync(MountDir, ct);
                    AppLog.Ok("image", $"{update} integrated, build {os.Build}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Warn("image", $"cumulative update not integrated ({ex.Message}); Windows Update will install it after setup");
                }
            }

            // 3. Bloat packages
            if (plan.Apps.Count > 0)
            {
                progress("apps", 0.32);
                var list = string.Join(",", plan.Apps.Select(Q));
                await Ps($$"""
                    $names = @({{list}})
                    Get-AppxProvisionedPackage -Path {{Q(MountDir)}} | Where-Object { $n = $_.DisplayName; $names | Where-Object { $n -like $_ } } | ForEach-Object {
                      try { Remove-AppxProvisionedPackage -Path {{Q(MountDir)}} -PackageName $_.PackageName | Out-Null; Write-Output "removed $($_.DisplayName)" }
                      catch { Write-Output "kept $($_.DisplayName): $($_.Exception.Message)" }
                    }
                    """, ct, onLine: line => AppLog.Info("image", line));
            }

            // 4. Legacy optional components
            if (plan.Capabilities.Count > 0)
            {
                progress("components", 0.42);
                var prefixes = string.Join(",", plan.Capabilities.Select(Q));
                await Ps($$"""
                    $prefixes = @({{prefixes}})
                    Get-WindowsCapability -Path {{Q(MountDir)}} | Where-Object { $p = $_.Name; $_.State -eq 'Installed' -and ($prefixes | Where-Object { $p.StartsWith($_) }) } | ForEach-Object {
                      try { Remove-WindowsCapability -Path {{Q(MountDir)}} -Name $_.Name -ScratchDirectory {{Q(Scratch)}} | Out-Null; Write-Output "removed $($_.Name)" }
                      catch { Write-Output "kept $($_.Name): $($_.Exception.Message)" }
                    }
                    """, ct, onLine: line => AppLog.Info("image", line));
            }

            // 5. Offline registry: tweaks + services
            progress("registry", 0.52);
            await ApplyRegistryAsync(MountDir, plan, ct);
            if (plan.IncludesLook)
            {
                try
                {
                    WallpaperOps.ExtractAssets(MountDir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    AppLog.Warn("image", $"look files not copied ({ex.Message}); AI Tweaker applies the look after setup");
                }
            }

            // 6. Drivers of this PC (perfect for reinstalling the same machine: network works on first boot)
            if (plan.Options.InjectDrivers)
            {
                progress("drivers", 0.58);
                Directory.CreateDirectory(DriversDir);
                var export = await ProcessRunner.RunAsync("pnputil.exe", $"/export-driver * \"{DriversDir}\"", TimeSpan.FromMinutes(20), ct: ct);
                if (!export.Success) AppLog.Warn("image", $"pnputil exported only some drivers (code {export.ExitCode})");
                await Ps($$"""
                    $added = @(Add-WindowsDriver -Path {{Q(MountDir)}} -Driver {{Q(DriversDir)}} -Recurse -ScratchDirectory {{Q(Scratch)}} -ErrorAction SilentlyContinue -ErrorVariable failed)
                    Write-Output "drivers added: $($added.Count), skipped: $($failed.Count)"
                    """, ct, onLine: line => AppLog.Info("image", line));
            }

            // 7. Component store cleanup (smaller image, faster install)
            if (plan.Options.CleanupComponents)
            {
                progress("cleanup", 0.65);
                var clean = await ProcessRunner.RunAsync("dism.exe", $"/Image:\"{MountDir}\" /ScratchDir:\"{Scratch}\" /Cleanup-Image /StartComponentCleanup /ResetBase", LongStep, ct: ct);
                if (!clean.Success) AppLog.Warn("image", $"component cleanup skipped (DISM code {clean.ExitCode}); the image is just larger");
            }

            File.WriteAllText(Path.Combine(MountDir, "Windows", "AITweaker-image.json"), JsonSerializer.Serialize(new
            {
                builtBy = "AI Tweaker",
                built = DateTime.Now,
                windows = os.Version,
                build = os.Build,
                edition,
                update,
                look = plan.IncludesLook,
                tweaks = plan.TweakIds,
                apps = plan.Apps,
                capabilities = plan.Capabilities,
            }, new JsonSerializerOptions { WriteIndented = true }));

            progress("commit", 0.78);
            await Ps($"Dismount-WindowsImage -Path {Q(MountDir)} -Save -ScratchDirectory {Q(Scratch)} | Out-Null", ct);
            mounted = false;
        }
        catch
        {
            // interrupted or failed: nothing half-done stays behind, the previous image (if any) is untouched
            if (mounted)
            {
                AppLog.Warn("image", "discarding the mounted image after an error");
                await Ps($"Dismount-WindowsImage -Path {Q(MountDir)} -Discard | Out-Null", CancellationToken.None, allowFail: true);
            }
            await CleanupAsync();
            throw;
        }

        // 8. Re-compress into the new folder (drops the space freed by cleanup), then swap it in
        progress("compress", 0.85);
        var sources = Path.Combine(Staging, "sources");
        try
        {
            await Ps($"Export-WindowsImage -SourceImagePath {Q(WorkWim)} -SourceIndex 1 -DestinationImagePath {Q(Path.Combine(sources, "install.wim"))} -CompressionType max -ScratchDirectory {Q(Scratch)} | Out-Null", ct);
            long size = new FileInfo(Path.Combine(sources, "install.wim")).Length;
            File.WriteAllText(Path.Combine(Staging, Marker), JsonSerializer.Serialize(new { built = DateTime.Now, windows = os.Version, build = os.Build, edition, update, tweaks = plan.TweakIds.Count, apps = plan.Apps.Count }));
            ForceDelete(OutputFolder);
            Directory.Move(Staging, OutputFolder);
            progress("done", 1);
            AppLog.Ok("image", $"AI Tweaker image ready: {plan.TweakIds.Count} tweaks, {plan.Apps.Count} apps removed, install.wim {size / 1048576} MB");
            return new ImageBuildResult(OutputFolder, plan.TweakIds.Count, plan.Apps.Count, plan.Capabilities.Count, plan.Options.InjectDrivers, size, os.Version, os.Build, update);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <summary>DISM steps can take a long time on a slow disk: they end on completion or on Cancel, not on a clock.</summary>
    private static readonly TimeSpan LongStep = TimeSpan.FromHours(6);

    /// <summary>
    /// Leftovers of a crashed or closed build: loaded hives, a mounted image, a corrupt mount point and half-copied folders.
    /// Without this one interrupted build would make every later one fail.
    /// </summary>
    private static async Task RecoverAsync()
    {
        foreach (var hive in new[] { HiveSoftware, HiveSystem, HiveDefault, HiveVersion })
            await ProcessRunner.RunAsync("reg.exe", $"unload HKLM\\{hive}"); // fails harmlessly when it isn't loaded
        await Ps($$"""
            Get-WindowsImage -Mounted | Where-Object { $_.Path -like {{Q(WorkRoot + "\\*")}} } | ForEach-Object { Dismount-WindowsImage -Path $_.Path -Discard -ErrorAction SilentlyContinue | Out-Null }
            Clear-WindowsCorruptMountPoint -ErrorAction SilentlyContinue | Out-Null
            """, CancellationToken.None, allowFail: true);
        await CleanupAsync();
    }

    private static async Task CleanupAsync()
    {
        await ProcessRunner.RunAsync("dism.exe", "/Cleanup-Wim", TimeSpan.FromMinutes(30));
        foreach (var d in new[] { Staging, MountDir, DriversDir, Scratch, Path.Combine(WorkRoot, "updates") })
        {
            try { ForceDelete(d); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn("image", $"could not delete {d}: {ex.Message}"); }
        }
        try { if (File.Exists(WorkWim)) File.Delete(WorkWim); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn("image", $"could not delete {WorkWim}: {ex.Message}"); }
    }

    /// <summary>Deletes a folder even when files in it are read-only (copied from an ISO).</summary>
    private static void ForceDelete(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>Loads the image hives, writes the plan through .NET registry APIs (handles disposed), then unloads.</summary>
    private static async Task ApplyRegistryAsync(string mount, OfflineImagePlan plan, CancellationToken ct)
    {
        var hives = new (string Name, string File)[]
        {
            (HiveSoftware, Path.Combine(mount, "Windows", "System32", "config", "SOFTWARE")),
            (HiveSystem, Path.Combine(mount, "Windows", "System32", "config", "SYSTEM")),
            (HiveDefault, Path.Combine(mount, "Users", "Default", "NTUSER.DAT")),
        };
        var loaded = new List<string>();
        try
        {
            foreach (var (name, file) in hives)
            {
                var r = await ProcessRunner.RunAsync("reg.exe", $"load HKLM\\{name} \"{file}\"", ct: ct);
                if (!r.Success) throw new InvalidOperationException($"reg load {name}: {(r.Output + r.Error).Trim()}");
                loaded.Add(name);
            }

            int written = 0;
            foreach (var op in plan.Registry)
            {
                var root = op.Hive switch { OfflineHive.Software => HiveSoftware, OfflineHive.System => HiveSystem, _ => HiveDefault };
                var path = $@"HKLM\{root}\{op.SubKey}";
                if (op.Delete)
                {
                    RegistryHelper.DeleteValue(path, op.Name);
                    continue;
                }
                var kind = RegistryHelper.ToWin32(op.Kind);
                object value = kind switch
                {
                    RegistryValueKind.DWord => int.Parse(op.Data!),
                    RegistryValueKind.QWord => long.Parse(op.Data!),
                    RegistryValueKind.Binary => Convert.FromBase64String(op.Data ?? ""),
                    RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(op.Data ?? "[]") ?? [],
                    _ => op.Data ?? "",
                };
                RegistryHelper.Write(path, op.Name, value, kind);
                written++;
            }

            int services = 0;
            foreach (var s in plan.Services)
            {
                var key = $@"HKLM\{HiveSystem}\ControlSet001\Services\{s.Name}";
                if (!RegistryHelper.KeyExists(key)) continue;
                RegistryHelper.Write(key, "Start", s.Start, RegistryValueKind.DWord);
                if (s.Start == 2) RegistryHelper.Write(key, "DelayedAutostart", s.Delayed ? 1 : 0, RegistryValueKind.DWord);
                services++;
            }
            AppLog.Ok("image", $"offline registry: {written} values, {services} services");
        }
        finally
        {
            var stuck = new List<string>();
            foreach (var name in Enumerable.Reverse(loaded)) if (!await UnloadAsync(name)) stuck.Add(name);
            // a hive left loaded would make the commit fail later with an obscure DISM error: stop here with the real cause
            if (stuck.Count > 0)
                throw new InvalidOperationException($"Registry hives of the image are still in use ({string.Join(", ", stuck)}). Close Registry Editor and other tools that may hold them, or restart the PC, then build again.");
        }
    }

    /// <summary>Unloads an offline hive; .NET may still hold a handle for a moment, so collect and retry with a growing pause.</summary>
    private static async Task<bool> UnloadAsync(string name)
    {
        ProcessResult? r = null;
        for (int i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            r = await ProcessRunner.RunAsync("reg.exe", $"unload HKLM\\{name}");
            if (r.Success) return true;
            await Task.Delay(500 * (i + 1));
        }
        AppLog.Error("image", $"reg unload {name} failed: {(r!.Output + r.Error).Trim()}");
        return false;
    }

    /// <summary>DisplayVersion (e.g. 25H2) and full build (26200.6899) from the image's SOFTWARE hive.</summary>
    private static async Task<(string Version, string Build)> ReadVersionAsync(string mount, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync("reg.exe", $"load HKLM\\{HiveVersion} \"{Path.Combine(mount, "Windows", "System32", "config", "SOFTWARE")}\"", ct: ct);
        if (!r.Success) throw new InvalidOperationException($"The image registry can't be opened: {(r.Output + r.Error).Trim()}");
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"{HiveVersion}\Microsoft\Windows NT\CurrentVersion");
            return (k?.GetValue("DisplayVersion") as string ?? "", $"{k?.GetValue("CurrentBuild")}.{k?.GetValue("UBR")}");
        }
        finally
        {
            if (!await UnloadAsync(HiveVersion)) throw new InvalidOperationException("The image registry stays in use — restart the PC and build again.");
        }
    }

    private static string Q(string s) => ProcessRunner.PsQuote(s);

    private static async Task<string> Ps(string script, CancellationToken ct, TimeSpan? timeout = null, Action<string>? onLine = null, bool allowFail = false)
    {
        var r = await ProcessRunner.PowerShellAsync("$ErrorActionPreference = 'Stop'\n" + script, timeout ?? LongStep, onLine, ct);
        if (!r.Success && !allowFail) throw new InvalidOperationException((r.Error.Length > 0 ? r.Error : r.Output).Trim());
        return r.Output;
    }
}
