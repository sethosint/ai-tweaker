using System.IO.Compression;
using System.Text;
using AITweaker.Core.Catalog;
using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform;

/// <param name="Method">installer | winget | download | page</param>
public sealed record InstallResult(string Id, string Name, bool Ok, string Method, string? Error);

public sealed record InstallSummary(IReadOnlyList<InstallResult> Results, bool RebootRequired);

/// <summary>
/// Installs Install-tab programs from the vendor's own installer: downloaded from the official site (or the
/// vendor's GitHub releases), checked for a valid digital signature and run silently where the installer allows it.
/// winget is the fallback for programs without a stable direct link; portable zips are unpacked; the official page
/// is opened as the last resort.
/// </summary>
public static class ProgramInstaller
{
    public static string ToolsDir => Path.Combine(AppPaths.Data, "Tools");

    // https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    private const int InstallAlreadyInstalled = unchecked((int)0x8A15010D);
    private const int InstallDowngrade = unchecked((int)0x8A15010E);
    private const int RebootRequiredToFinish = unchecked((int)0x8A150109);
    private const int RebootInitiated = unchecked((int)0x8A15010B);
    private const int InstallInProgress = unchecked((int)0x8A150102);

    /// <summary>winget.exe is an app-execution alias in the user's WindowsApps folder, not always on PATH.</summary>
    public static string? FindWinget()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(alias)) return alias;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var f = Path.Combine(dir.Trim(), "winget.exe");
                if (File.Exists(f)) return f;
            }
            catch (ArgumentException) { /* malformed PATH entry */ }
        }
        return null;
    }

    /// <summary>Re-registers App Installer (it ships with Windows 11 but can be broken or unregistered for the user).</summary>
    public static async Task<bool> RepairWingetAsync()
    {
        AppLog.Info("install", "re-registering App Installer (winget)…");
        await ProcessRunner.PowerShellAsync("Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", TimeSpan.FromMinutes(3));
        return FindWinget() is not null;
    }

    /// <summary>Which catalog programs are already installed (by their "Apps &amp; features" names).</summary>
    public static Dictionary<string, bool> Installed(TweakCatalog catalog)
    {
        var names = new List<string>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var un = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (un is null) continue;
                foreach (var sub in un.GetSubKeyNames())
                {
                    using var k = un.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is string n && n.Length > 0) names.Add(n);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { /* skip unreadable hive */ }
        }

        var result = new Dictionary<string, bool>();
        foreach (var p in catalog.Programs)
        {
            bool found = p.Download is not null
                ? Directory.Exists(Path.Combine(ToolsDir, p.Id))
                : p.Detect is { Length: > 0 } d && d.Split('|').Any(frag => names.Any(n => n.Contains(frag, StringComparison.OrdinalIgnoreCase)));
            result[p.Id] = found;
        }
        return result;
    }

    public static async Task<InstallSummary> InstallAsync(TweakCatalog catalog, IReadOnlyList<string> ids, Action<int, int, string> progress, CancellationToken ct)
    {
        var byId = catalog.Programs.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var chosen = ids.Select(id => byId.GetValueOrDefault(id)).OfType<InstallProgram>().ToList();
        var results = new List<InstallResult>();
        bool reboot = false;
        string? winget = FindWinget();
        if (winget is null && chosen.Any(p => p.Winget is not null) && await RepairWingetAsync()) winget = FindWinget();

        for (int i = 0; i < chosen.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = chosen[i];
            progress(i + 1, chosen.Count, p.Name);
            InstallResult r;
            try
            {
                InstallResult? direct = p.Installer is not null || p.Github is not null ? await InstallerAsync(p, ct) : null;
                if (direct is { Ok: true })
                {
                    r = direct;
                }
                else if (p.Winget is not null && winget is not null)
                {
                    var (ok, needsReboot, error) = await WingetAsync(winget, p, ct);
                    reboot |= needsReboot;
                    r = new InstallResult(p.Id, p.Name, ok, "winget", ok ? null : (direct?.Error is { } de ? de + "; " : "") + error);
                }
                else if (direct is not null)
                {
                    r = direct;
                }
                else if (p.Download is not null)
                {
                    r = await DownloadAsync(p, ct);
                }
                else
                {
                    r = OpenPage(p, p.Winget is not null ? "winget is not available" : null);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                r = new InstallResult(p.Id, p.Name, false, "winget", ex.Message);
            }
            AppLog.Write(r.Ok ? "ok" : "warn", "install", r.Ok ? $"{p.Name}: installed ({r.Method})" : $"{p.Name}: {r.Error}");
            results.Add(r);
        }
        return new InstallSummary(results, reboot);
    }

    private static async Task<(bool Ok, bool Reboot, string? Error)> WingetAsync(string winget, InstallProgram p, CancellationToken ct)
    {
        var args = $"install --id {p.Winget} --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity"
            + (p.Override is null ? "" : $" --override \"{p.Override}\"");
        int code = 0;
        string output = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (p.User)
            {
                code = await RunAsSignedInUserAsync(winget, args, ct);
            }
            else
            {
                var r = await ProcessRunner.RunAsync(winget, args, TimeSpan.FromMinutes(30), encoding: Encoding.UTF8, ct: ct);
                code = r.ExitCode;
                output = (r.Output + r.Error).Trim();
            }
            if (code != InstallInProgress) break;
            await Task.Delay(TimeSpan.FromSeconds(20), ct); // another installer (e.g. Windows Update) is busy
        }
        return code switch
        {
            0 or UpdateNotApplicable or PackageAlreadyInstalled or InstallAlreadyInstalled or InstallDowngrade => (true, false, null),
            RebootRequiredToFinish or RebootInitiated => (true, true, null),
            _ => (false, false, $"winget exit 0x{code:X8}" + (output.Length > 0 ? ": " + output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1].Trim() : "")),
        };
    }

    /// <summary>Uninstalls an Install-tab program with winget (silent where the package allows it).</summary>
    public static async Task<InstallResult> UninstallAsync(TweakCatalog catalog, string id, CancellationToken ct = default)
    {
        var p = catalog.Programs.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("unknown program " + id);
        if (p.Winget is null) return new InstallResult(p.Id, p.Name, false, "none", "no winget package");
        var winget = FindWinget() ?? throw new InvalidOperationException("winget is not available");
        var args = $"uninstall --id {p.Winget} --exact --silent --accept-source-agreements --disable-interactivity";
        int code;
        string output = "";
        if (p.User) code = await RunAsSignedInUserAsync(winget, args, ct);
        else
        {
            var r = await ProcessRunner.RunAsync(winget, args, TimeSpan.FromMinutes(20), encoding: Encoding.UTF8, ct: ct);
            code = r.ExitCode;
            output = (r.Output + r.Error).Trim();
        }
        bool ok = code is 0 or RebootRequiredToFinish or RebootInitiated;
        AppLog.Write(ok ? "ok" : "warn", "install", $"{p.Name}: uninstall exit 0x{code:X8}");
        return new InstallResult(p.Id, p.Name, ok, "winget", ok ? null : $"winget exit 0x{code:X8}" + (output.Length > 0 ? ": " + output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1].Trim() : ""));
    }

    /// <summary>
    /// Some installers refuse to run elevated. Runs the command through a one-off scheduled task as the signed-in
    /// user with limited rights, waits for it and returns its exit code.
    /// </summary>
    internal static async Task<int> RunAsSignedInUserAsync(string file, string args, CancellationToken ct)
    {
        var name = "AITweaker-UserInstall-" + Guid.NewGuid().ToString("N")[..8];
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $name = {{ProcessRunner.PsQuote(name)}}
            $user = (Get-CimInstance Win32_ComputerSystem).UserName
            if (-not $user) { $user = "$env:USERDOMAIN\$env:USERNAME" }
            $action = New-ScheduledTaskAction -Execute {{ProcessRunner.PsQuote(file)}} -Argument {{ProcessRunner.PsQuote(args)}}
            $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
            Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Force | Out-Null
            try {
              Start-ScheduledTask -TaskName $name
              Start-Sleep -Seconds 2
              while ((Get-ScheduledTask -TaskName $name).State -eq 'Running') { Start-Sleep -Seconds 2 }
              Write-Output ('RESULT:' + (Get-ScheduledTaskInfo -TaskName $name).LastTaskResult)
            } finally { Unregister-ScheduledTask -TaskName $name -Confirm:$false }
            """;
        var r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromMinutes(30), ct: ct);
        var line = r.Output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("RESULT:", StringComparison.Ordinal));
        return line is not null && long.TryParse(line["RESULT:".Length..], out var v) ? unchecked((int)v) : -1;
    }

    /// <summary>
    /// Downloads the vendor's installer, makes sure Windows trusts its signature and runs it: silently when the
    /// program has known switches, otherwise the installer window opens for the user.
    /// </summary>
    private static async Task<InstallResult> InstallerAsync(InstallProgram p, CancellationToken ct)
    {
        string url, name;
        try
        {
            if (p.Github is not null)
            {
                var rx = new System.Text.RegularExpressions.Regex(p.Asset ?? @"\.exe$");
                (name, url) = await Downloader.LatestGitHubAssetAsync(p.Github, n => rx.IsMatch(n), ct);
            }
            else
            {
                url = p.Installer!.Replace("{lang}", SettingsStore.Current.Language == "en" ? "en-US" : "ru");
                name = p.Id + "-setup";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new InstallResult(p.Id, p.Name, false, "installer", "no installer link: " + ex.Message);
        }

        var dir = Path.Combine(AppPaths.Downloads, "installers");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name);
        try
        {
            await Downloader.DownloadAsync(url, file, null, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new InstallResult(p.Id, p.Name, false, "installer", "download failed: " + ex.Message);
        }
        // links like ".../latest" don't say what they are: name the file by its content (MSI or EXE)
        bool msi = IsMsi(file);
        var target = Path.ChangeExtension(file, msi ? ".msi" : ".exe");
        if (!string.Equals(target, file, StringComparison.OrdinalIgnoreCase)) File.Move(file, target, overwrite: true);

        var sig = await ProcessRunner.PowerShellAsync($"(Get-AuthenticodeSignature -FilePath {ProcessRunner.PsQuote(target)}).Status", TimeSpan.FromMinutes(1), ct: ct);
        if (!sig.Output.Contains("Valid"))
        {
            File.Delete(target);
            return new InstallResult(p.Id, p.Name, false, "installer", "the downloaded installer has no valid signature: " + sig.Output.Trim());
        }

        string exe = target, args = p.Silent ?? "";
        if (msi)
        {
            exe = ProcessRunner.System32("msiexec.exe");
            args = $"/i \"{target}\" /qn /norestart";
        }
        AppLog.Info("install", $"{p.Name}: {Path.GetFileName(target)} {args}".TrimEnd());
        int code;
        if (p.User && (p.Silent is not null || msi))
        {
            code = await RunAsSignedInUserAsync(exe, args, ct);
        }
        else if (p.Silent is not null || msi)
        {
            code = (await ProcessRunner.RunAsync(exe, args, TimeSpan.FromMinutes(30), ct: ct)).ExitCode;
        }
        else
        {
            // no silent switches: the installer opens and the user finishes it
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            if (proc is not null) await proc.WaitForExitAsync(ct);
            code = 0;
        }
        // 3010 / 1641: done, restart needed
        bool ok = code is 0 or 3010 or 1641;
        return new InstallResult(p.Id, p.Name, ok, "installer", ok ? null : $"installer exit code {code}");
    }

    /// <summary>MSI files are OLE compound documents (D0 CF 11 E0 …).</summary>
    private static bool IsMsi(string file)
    {
        Span<byte> head = stackalloc byte[4];
        using var f = File.OpenRead(file);
        return f.Read(head) == 4 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0;
    }

    /// <summary>Official portable zip → C:\ProgramData\AITweaker\Tools\&lt;id&gt; with a Start menu shortcut.</summary>
    private static async Task<InstallResult> DownloadAsync(InstallProgram p, CancellationToken ct)
    {
        var zip = Path.Combine(AppPaths.Downloads, p.Id + ".zip");
        try
        {
            await Downloader.DownloadAsync(p.Download!, zip, null, ct);
        }
        catch (HttpRequestException ex)
        {
            return OpenPage(p, "download failed: " + ex.Message);
        }
        var target = Path.Combine(ToolsDir, p.Id);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        ZipFile.ExtractToDirectory(zip, target);
        File.Delete(zip);
        var exe = Directory.EnumerateFiles(target, "*.exe", SearchOption.AllDirectories)
            .OrderByDescending(f => Path.GetFileNameWithoutExtension(f).Equals(p.Id, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (exe is not null)
        {
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), p.Name.Split(" — ")[0] + ".lnk");
            await ProcessRunner.PowerShellAsync($"$s = (New-Object -ComObject WScript.Shell).CreateShortcut({ProcessRunner.PsQuote(lnk)}); $s.TargetPath = {ProcessRunner.PsQuote(exe)}; $s.WorkingDirectory = {ProcessRunner.PsQuote(Path.GetDirectoryName(exe)!)}; $s.Save()", TimeSpan.FromSeconds(30), ct: ct);
        }
        ProcessRunner.OpenFolder(target);
        return new InstallResult(p.Id, p.Name, true, "download", null);
    }

    private static InstallResult OpenPage(InstallProgram p, string? reason)
    {
        if (p.Url is { Length: > 0 }) ProcessRunner.OpenUrl(p.Url);
        // Opening the page is the user's next step, not a finished install.
        return new InstallResult(p.Id, p.Name, false, "page", reason ?? "opened the official page");
    }
}
