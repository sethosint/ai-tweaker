using System.IO.Compression;
using System.Text;
using AITweaker.Core.Catalog;
using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform;

/// <param name="Method">winget | download | page</param>
public sealed record InstallResult(string Id, string Name, bool Ok, string Method, string? Error);

public sealed record InstallSummary(IReadOnlyList<InstallResult> Results, bool RebootRequired);

/// <summary>
/// Installs Install-tab programs with winget (Microsoft's package manager, official packages only), unpacks official
/// portable zips, or opens the official page as the last resort.
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
                if (p.Winget is not null && winget is not null)
                {
                    var (ok, needsReboot, error) = await WingetAsync(winget, p, ct);
                    reboot |= needsReboot;
                    r = new InstallResult(p.Id, p.Name, ok, "winget", error);
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
        var args = $"install --id {p.Winget} --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
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

    /// <summary>
    /// Some installers refuse to run elevated. Runs the command through a one-off scheduled task as the signed-in
    /// user with limited rights, waits for it and returns its exit code.
    /// </summary>
    private static async Task<int> RunAsSignedInUserAsync(string file, string args, CancellationToken ct)
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
