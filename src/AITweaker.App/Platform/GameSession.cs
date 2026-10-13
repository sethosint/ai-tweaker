using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AITweaker.App.Platform.Actions;
using AITweaker.Core.Profile;
using Microsoft.Win32;

namespace AITweaker.App.Platform;

/// <summary>
/// The temporary "Game Mode" button: while it is on, background services that are useless during a game are paused,
/// background helper apps are closed, power goes to maximum, cached memory is freed and the 0.5 ms timer is held.
/// Turning it off (or closing AI Tweaker) puts everything back. Security services are never touched.
/// </summary>
public static class GameSession
{
    private static readonly (string Id, string[] Services)[] ServiceGroups =
    [
        ("updates", ["wuauserv", "UsoSvc", "DoSvc", "BITS", "InstallService", "edgeupdate", "edgeupdatem", "gupdate", "gupdatem", "MozillaMaintenance"]),
        ("indexing", ["WSearch", "SysMain"]),
        ("telemetry", ["DiagTrack", "dmwappushservice", "WerSvc", "PcaSvc", "MapsBroker"]),
        ("print", ["Spooler"]),
    ];

    /// <summary>Background helpers nobody needs mid-game. Launchers, voice chat and music players are left alone.</summary>
    private static readonly (string Process, string Name)[] BackgroundApps =
    [
        ("OneDrive", "OneDrive"), ("ms-teams", "Microsoft Teams"), ("Teams", "Microsoft Teams"), ("Widgets", "Widgets"),
        ("WidgetService", "Widgets"), ("PhoneExperienceHost", "Phone Link"), ("GoogleDriveFS", "Google Drive"), ("Dropbox", "Dropbox"),
        ("YandexDisk2", "Yandex Disk"), ("Creative Cloud", "Adobe Creative Cloud"), ("CCXProcess", "Adobe Creative Cloud"),
        ("AdobeCollabSync", "Adobe Acrobat"), ("Skype", "Skype"), ("Cortana", "Cortana"),
    ];

    private sealed record State(DateTime Started, List<string> Groups, List<string> Services, List<string> Apps, string? Scheme, string? Overlay, bool Timer, ulong FreedBytes, List<string>? Wifi = null);

    private static State? _active;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string StateFile => Path.Combine(AppPaths.Data, "game-session.json");

    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desired, bool set, out uint current);

    public static bool Active => _active is not null;

    /// <summary>What the button would do right now (when off) or what it did (when on).</summary>
    public static object Describe(SystemProfile profile)
    {
        if (_active is { } s)
            return new
            {
                active = true,
                started = s.Started,
                groups = s.Groups,
                services = s.Services,
                apps = s.Apps.Select(a => Path.GetFileNameWithoutExtension(a)).Distinct(),
                freedMb = s.FreedBytes / 1048576,
            };
        var running = RunningServices();
        return new
        {
            active = false,
            options = new object?[]
            {
                Option("updates", running), Option("indexing", running), Option("telemetry", running), Option("print", running),
                AppsOption(),
                new { id = "power", items = new[] { PowerPlanName(profile) } },
                WifiInterfaces() is { Count: > 0 } wifi ? new { id = "wifi", items = wifi.ToArray() } : null,
                new { id = "memory", items = Array.Empty<string>() },
                TimerAvailable() ? new { id = "timer", items = Array.Empty<string>() } : null,
            }.Where(o => o is not null),
        };
    }

    private static object? Option(string group, Dictionary<string, string> running)
    {
        var names = ServiceGroups.First(g => g.Id == group).Services.Where(running.ContainsKey).Select(n => running[n]).ToList();
        return names.Count == 0 ? null : new { id = group, items = names };
    }

    private static object? AppsOption()
    {
        var names = BackgroundApps.Where(a => Process.GetProcessesByName(a.Process).Length > 0).Select(a => a.Name).Distinct().ToList();
        return names.Count == 0 ? null : new { id = "apps", items = names };
    }

    public static async Task<object> StartAsync(IReadOnlyCollection<string> groups, SystemProfile profile)
    {
        await Gate.WaitAsync();
        try
        {
            if (_active is not null) return Describe(profile);
            var want = groups.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var stopped = new List<string>();
            var closed = new List<string>();
            bool timer = false;
            ulong freed = 0;

            // Saved before anything changes, so a crash or power loss is still undone on the next start.
            var state = new State(DateTime.Now, [.. want], stopped, closed, null, null, false, 0);
            Save(state);

            var running = RunningServices();
            foreach (var (id, services) in ServiceGroups)
            {
                if (!want.Contains(id)) continue;
                foreach (var name in services.Where(running.ContainsKey))
                {
                    var r = await ProcessRunner.RunAsync("sc.exe", $"stop \"{name}\"", TimeSpan.FromSeconds(20));
                    if (r.ExitCode is 0 or 1062) { stopped.Add(name); Save(state); }
                    else AppLog.Write("warn", "game", $"{name}: sc stop exit {r.ExitCode}");
                }
            }

            if (want.Contains("apps"))
            {
                foreach (var (proc, _) in BackgroundApps)
                {
                    foreach (var p in Process.GetProcessesByName(proc))
                    {
                        try
                        {
                            var path = p.MainModule?.FileName;
                            p.Kill(entireProcessTree: true);
                            if (path is not null && !closed.Contains(path, StringComparer.OrdinalIgnoreCase)) closed.Add(path);
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                        {
                            AppLog.Write("warn", "game", $"{proc}: {ex.Message}");
                        }
                        finally { p.Dispose(); }
                    }
                }
                Save(state);
            }

            if (want.Contains("power"))
            {
                var prevScheme = Native.GetActiveScheme();
                state = state with { Scheme = prevScheme.ToString(), Overlay = Native.GetOverlay()?.ToString() };
                Save(state);
                await RaisePowerAsync(profile, prevScheme);
            }

            if (want.Contains("memory"))
            {
                try { freed = MemoryTools.PurgeStandby().FreedBytes; }
                catch (InvalidOperationException ex) { AppLog.Write("warn", "game", "memory purge: " + ex.Message); }
            }

            if (want.Contains("wifi"))
            {
                // Windows scans for networks about once a minute even while connected — the classic Wi-Fi ping spike.
                // With auto-config off for the session the connection stays, the scans stop.
                var paused = new List<string>();
                foreach (var name in WifiInterfaces())
                {
                    var r = await ProcessRunner.RunAsync("netsh.exe", $"wlan set autoconfig enabled=no interface=\"{name}\"", TimeSpan.FromSeconds(15));
                    if (r.ExitCode == 0) paused.Add(name);
                    else AppLog.Write("warn", "game", $"Wi-Fi {name}: netsh exit {r.ExitCode}");
                }
                state = state with { Wifi = paused };
                Save(state);
            }

            if (want.Contains("timer") && TimerAvailable())
                timer = NtSetTimerResolution(5000, true, out _) == 0;

            _active = state with { Timer = timer, FreedBytes = freed };
            Save(_active);
            AppLog.Ok("game", $"game mode on: {stopped.Count} services paused, {closed.Count} apps closed, power {(state.Scheme is null ? "unchanged" : "raised")}, {freed / 1048576} MB freed");
            return Describe(profile);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Puts back everything the session changed. Safe to call when nothing is active.</summary>
    public static async Task<bool> StopAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var s = _active ?? Load();
            if (s is null) return false;
            if (s.Timer) NtSetTimerResolution(5000, false, out _);
            foreach (var name in s.Wifi ?? [])
                await ProcessRunner.RunAsync("netsh.exe", $"wlan set autoconfig enabled=yes interface=\"{name}\"", TimeSpan.FromSeconds(15));
            if (Guid.TryParse(s.Scheme, out var scheme)) Native.SetActiveScheme(scheme);
            if (s.Scheme is not null) Native.SetOverlay(Guid.TryParse(s.Overlay, out var ov) ? ov : PowerOps.OverlayBalanced);
            foreach (var name in s.Services)
            {
                var r = await ProcessRunner.RunAsync("sc.exe", $"start \"{name}\"", TimeSpan.FromSeconds(20));
                // 1056 = already running; 1058 = disabled meanwhile (e.g. by a tweak) — nothing to restore then
                if (r.ExitCode is not (0 or 1056 or 1058)) AppLog.Write("warn", "game", $"{name}: sc start exit {r.ExitCode}");
            }
            foreach (var path in s.Apps.Where(File.Exists))
            {
                // Through Explorer, so the app comes back with normal user rights, not elevated like us.
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false }); }
                catch (System.ComponentModel.Win32Exception ex) { AppLog.Write("warn", "game", $"{path}: {ex.Message}"); }
            }
            _active = null;
            try { File.Delete(StateFile); } catch (IOException) { /* next start retries */ }
            AppLog.Ok("game", "game mode off: everything restored");
            return true;
        }
        finally { Gate.Release(); }
    }

    /// <summary>On start-up: a session left over from a crash or a power cut is undone.</summary>
    public static async Task RestoreLeftoverAsync()
    {
        if (_active is null && File.Exists(StateFile)) await StopAsync();
    }

    /// <summary>Connected Wi-Fi adapters by their connection name (what netsh calls the interface).</summary>
    private static List<string> WifiInterfaces() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211
                        && n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .Select(n => n.Name)
            .ToList();

    private static async Task RaisePowerAsync(SystemProfile profile, Guid active)
    {
        bool x3d = profile.Cpu.IsX3D && profile.Cpu.Cores > 8; // dual-CCD X3D needs the Balanced plan for core parking
        if (!profile.Machine.IsLaptop && !x3d && active != PowerOps.High && active != PowerOps.UltimateTemplate
            && active.ToString() != SettingsStore.Current.UltimateSchemeGuid && (await PowerOps.ListSchemesAsync()).Contains(PowerOps.High))
        {
            Native.SetActiveScheme(PowerOps.High);
            return;
        }
        Native.SetOverlay(PowerOps.OverlayBestPerformance);
    }

    private static string PowerPlanName(SystemProfile profile) =>
        !profile.Machine.IsLaptop && !(profile.Cpu.IsX3D && profile.Cpu.Cores > 8) ? "high" : "overlay";

    /// <summary>Only worth holding when Windows applies timer requests globally (the "global timer resolution" tweak).</summary>
    private static bool TimerAvailable()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel");
        return k?.GetValue("GlobalTimerResolutionRequests") is int v && v == 1;
    }

    private static Dictionary<string, string> RunningServices()
    {
        var all = ServiceGroups.SelectMany(g => g.Services).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in SystemProfiler.Query(@"root\cimv2", "SELECT Name, DisplayName FROM Win32_Service WHERE State = 'Running'"))
        {
            var name = o["Name"]?.ToString() ?? "";
            if (all.Contains(name)) map[name] = o["DisplayName"]?.ToString() ?? name;
        }
        return map;
    }

    private static void Save(State s)
    {
        try { File.WriteAllText(StateFile, JsonSerializer.Serialize(s)); }
        catch (IOException ex) { AppLog.Write("warn", "game", "state not saved: " + ex.Message); }
    }

    private static State? Load()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) : null; }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }
}
