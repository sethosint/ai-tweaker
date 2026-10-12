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
    private const string HiveSoftware = @"AIT_SOFTWARE", HiveSystem = @"AIT_SYSTEM", HiveDefault = @"AIT_DEFAULT";

    public static string WorkRoot => Path.Combine(AppPaths.Data, "image");
    public static string OutputFolder => Path.Combine(WorkRoot, "iso");

    public static async Task<ImageBuildResult> BuildAsync(string isoPath, OfflineImagePlan plan, Action<string, double> progress, CancellationToken ct)
    {
        if (!File.Exists(isoPath)) throw new FileNotFoundException("ISO not found", isoPath);
        var drive = new DriveInfo(Path.GetPathRoot(WorkRoot)!);
        if (drive.AvailableFreeSpace < 25L * 1024 * 1024 * 1024)
            throw new InvalidOperationException($"Need at least 25 GB free on {drive.Name} to build the image ({drive.AvailableFreeSpace / 1073741824} GB free).");

        var mount = Path.Combine(WorkRoot, "mount");
        var wim = Path.Combine(WorkRoot, "install.wim");
        var drivers = Path.Combine(WorkRoot, "drivers");

        progress("prepare", 0.01);
        await Ps($"Get-WindowsImage -Mounted | Where-Object {{ $_.Path -eq {Q(mount)} }} | ForEach-Object {{ Dismount-WindowsImage -Path $_.Path -Discard | Out-Null }}", ct, allowFail: true);
        foreach (var d in new[] { OutputFolder, mount, drivers }) if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
        if (File.Exists(wim)) File.Delete(wim);
        Directory.CreateDirectory(OutputFolder);
        Directory.CreateDirectory(mount);

        // 1. ISO -> working folder
        progress("copy", 0.03);
        await Ps($$"""
            $img = Mount-DiskImage -ImagePath {{Q(isoPath)}} -PassThru
            try {
              $src = $null
              for ($i = 0; $i -lt 15 -and -not $src; $i++) { $src = ($img | Get-Volume).DriveLetter; if (-not $src) { Start-Sleep 1 } }
              if (-not $src) { throw 'The ISO has no drive letter' }
              robocopy "$($src):\." {{Q(OutputFolder)}} /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
              if ($LASTEXITCODE -ge 8) { throw "robocopy failed with code $LASTEXITCODE" }
            } finally { Dismount-DiskImage -ImagePath {{Q(isoPath)}} | Out-Null }
            Get-ChildItem -Path {{Q(OutputFolder)}} -Recurse -File | ForEach-Object { $_.IsReadOnly = $false }
            """, ct);

        var sources = Path.Combine(OutputFolder, "sources");
        var srcImage = File.Exists(Path.Combine(sources, "install.wim")) ? Path.Combine(sources, "install.wim") : Path.Combine(sources, "install.esd");
        if (!File.Exists(srcImage)) throw new InvalidOperationException("This ISO has no sources\\install.wim / install.esd");

        // 2. Export only the Pro edition (locale-independent: matched by EditionId)
        progress("export", 0.12);
        await Ps($$"""
            $idx = $null
            foreach ($i in (Get-WindowsImage -ImagePath {{Q(srcImage)}})) {
              if ((Get-WindowsImage -ImagePath {{Q(srcImage)}} -Index $i.ImageIndex).EditionId -eq 'Professional') { $idx = $i.ImageIndex; break }
            }
            if (-not $idx) { throw 'Windows 11 Pro edition not found in this ISO' }
            Export-WindowsImage -SourceImagePath {{Q(srcImage)}} -SourceIndex $idx -DestinationImagePath {{Q(wim)}} -CompressionType max | Out-Null
            """, ct, TimeSpan.FromMinutes(45));

        bool mounted = false;
        string? update = null;
        (string Version, string Build) os;
        try
        {
            progress("mount", 0.25);
            await Ps($"Mount-WindowsImage -ImagePath {Q(wim)} -Index 1 -Path {Q(mount)} | Out-Null", ct, TimeSpan.FromMinutes(20));
            mounted = true;
            os = await ReadVersionAsync(mount, ct);
            AppLog.Info("image", $"Windows 11 {os.Version}, build {os.Build}");

            // 2b. Newest cumulative update from the Microsoft Update Catalog, so setup starts fully patched
            if (plan.Options.IntegrateUpdates)
            {
                progress("updates", 0.27);
                try
                {
                    update = await UpdateCatalog.IntegrateLatestAsync(mount, os.Version, WorkRoot, v => progress("updates", 0.27 + v * 0.04), ct);
                    os = await ReadVersionAsync(mount, ct);
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
                    Get-AppxProvisionedPackage -Path {{Q(mount)}} | Where-Object { $n = $_.DisplayName; $names | Where-Object { $n -like $_ } } | ForEach-Object {
                      Remove-AppxProvisionedPackage -Path {{Q(mount)}} -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null
                      Write-Output "removed $($_.DisplayName)"
                    }
                    """, ct, TimeSpan.FromMinutes(30), line => AppLog.Info("image", line));
            }

            // 4. Legacy optional components
            if (plan.Capabilities.Count > 0)
            {
                progress("components", 0.42);
                var prefixes = string.Join(",", plan.Capabilities.Select(Q));
                await Ps($$"""
                    $prefixes = @({{prefixes}})
                    Get-WindowsCapability -Path {{Q(mount)}} | Where-Object { $p = $_.Name; $_.State -eq 'Installed' -and ($prefixes | Where-Object { $p.StartsWith($_) }) } | ForEach-Object {
                      Remove-WindowsCapability -Path {{Q(mount)}} -Name $_.Name -ErrorAction SilentlyContinue | Out-Null
                      Write-Output "removed $($_.Name)"
                    }
                    """, ct, TimeSpan.FromMinutes(30), line => AppLog.Info("image", line));
            }

            // 5. Offline registry: tweaks + services
            progress("registry", 0.52);
            await ApplyRegistryAsync(mount, plan, ct);
            if (plan.IncludesLook)
            {
                try
                {
                    WallpaperOps.ExtractAssets(mount);
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
                Directory.CreateDirectory(drivers);
                await ProcessRunner.RunAsync("pnputil.exe", $"/export-driver * \"{drivers}\"", TimeSpan.FromMinutes(20), ct: ct);
                await Ps($"Add-WindowsDriver -Path {Q(mount)} -Driver {Q(drivers)} -Recurse -ErrorAction SilentlyContinue | Out-Null", ct, TimeSpan.FromMinutes(30));
            }

            // 7. Component store cleanup (smaller image, faster install)
            if (plan.Options.CleanupComponents)
            {
                progress("cleanup", 0.65);
                await ProcessRunner.RunAsync("dism.exe", $"/Image:\"{mount}\" /Cleanup-Image /StartComponentCleanup /ResetBase", TimeSpan.FromMinutes(60), ct: ct);
            }

            File.WriteAllText(Path.Combine(mount, "Windows", "AITweaker-image.json"), JsonSerializer.Serialize(new
            {
                builtBy = "AI Tweaker",
                built = DateTime.Now,
                windows = os.Version,
                build = os.Build,
                update,
                look = plan.IncludesLook,
                tweaks = plan.TweakIds,
                apps = plan.Apps,
                capabilities = plan.Capabilities,
            }, new JsonSerializerOptions { WriteIndented = true }));

            progress("commit", 0.78);
            await Ps($"Dismount-WindowsImage -Path {Q(mount)} -Save | Out-Null", ct, TimeSpan.FromMinutes(40));
            mounted = false;
        }
        finally
        {
            if (mounted)
            {
                AppLog.Warn("image", "discarding the mounted image after an error");
                await Ps($"Dismount-WindowsImage -Path {Q(mount)} -Discard | Out-Null", CancellationToken.None, TimeSpan.FromMinutes(20), allowFail: true);
            }
        }

        // 8. Re-compress into the output tree (removes the space freed by cleanup)
        progress("compress", 0.85);
        foreach (var old in new[] { "install.wim", "install.esd" }.Select(n => Path.Combine(sources, n)).Where(File.Exists)) File.Delete(old);
        await Ps($"Export-WindowsImage -SourceImagePath {Q(wim)} -SourceIndex 1 -DestinationImagePath {Q(Path.Combine(sources, "install.wim"))} -CompressionType max | Out-Null", ct, TimeSpan.FromMinutes(45));
        File.Delete(wim);
        if (Directory.Exists(drivers)) Directory.Delete(drivers, recursive: true);

        progress("done", 1);
        long size = new FileInfo(Path.Combine(sources, "install.wim")).Length;
        AppLog.Ok("image", $"AI Tweaker image ready: {plan.TweakIds.Count} tweaks, {plan.Apps.Count} apps removed, install.wim {size / 1048576} MB");
        return new ImageBuildResult(OutputFolder, plan.TweakIds.Count, plan.Apps.Count, plan.Capabilities.Count, plan.Options.InjectDrivers, size, os.Version, os.Build, update);
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
            foreach (var name in Enumerable.Reverse(loaded)) await UnloadAsync(name);
        }
    }

    /// <summary>Unloads an offline hive; .NET may still hold a handle for a moment, so collect and retry.</summary>
    private static async Task UnloadAsync(string name)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        ProcessResult? r = null;
        for (int i = 0; i < 5; i++)
        {
            r = await ProcessRunner.RunAsync("reg.exe", $"unload HKLM\\{name}");
            if (r.Success) return;
            await Task.Delay(1000);
        }
        AppLog.Error("image", $"reg unload {name} failed: {(r!.Output + r.Error).Trim()}");
    }

    /// <summary>DisplayVersion (e.g. 25H2) and full build (26200.6899) from the image's SOFTWARE hive.</summary>
    private static async Task<(string Version, string Build)> ReadVersionAsync(string mount, CancellationToken ct)
    {
        const string hive = "AIT_VER";
        var r = await ProcessRunner.RunAsync("reg.exe", $"load HKLM\\{hive} \"{Path.Combine(mount, "Windows", "System32", "config", "SOFTWARE")}\"", ct: ct);
        if (!r.Success) return ("", "");
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"{hive}\Microsoft\Windows NT\CurrentVersion");
            return (k?.GetValue("DisplayVersion") as string ?? "", $"{k?.GetValue("CurrentBuild")}.{k?.GetValue("UBR")}");
        }
        finally
        {
            await UnloadAsync(hive);
        }
    }

    private static string Q(string s) => ProcessRunner.PsQuote(s);

    private static async Task Ps(string script, CancellationToken ct, TimeSpan? timeout = null, Action<string>? onLine = null, bool allowFail = false)
    {
        var r = await ProcessRunner.PowerShellAsync("$ErrorActionPreference = 'Stop'\n" + script, timeout ?? TimeSpan.FromMinutes(20), onLine, ct);
        if (!r.Success && !allowFail) throw new InvalidOperationException((r.Error.Length > 0 ? r.Error : r.Output).Trim());
    }
}
