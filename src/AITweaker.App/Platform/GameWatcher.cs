using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using AITweaker.Core.Profile;

namespace AITweaker.App.Platform;

/// <summary>
/// Automatic Game Mode: Windows reports every process start and stop (WMI process traces, no polling); when a game from
/// the Steam/Epic libraries or the user's list starts, Game Mode goes on with the user's saved choices, and a few seconds
/// after the last such game closes it goes off again. A Game Mode the user started by hand is never touched.
/// </summary>
public sealed class GameWatcher : IDisposable
{
    private readonly Func<Task<SystemProfile>> _profile;
    private readonly Action<string, object?> _emit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>Game processes seen starting; read on every process stop in the system, so it must not need the gate.</summary>
    private readonly ConcurrentDictionary<int, string> _running = new();
    private readonly object _listLock = new();
    private ManagementEventWatcher? _starts, _stops;
    private HashSet<string> _games = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _gamesRead;

    /// <summary>What a session started for a game turns on when the user hasn't chosen yet.</summary>
    public static readonly string[] DefaultGroups = ["updates", "indexing", "telemetry", "print", "apps", "power", "timer"];

    public GameWatcher(Func<Task<SystemProfile>> profile, Action<string, object?> emit)
    {
        _profile = profile;
        _emit = emit;
    }

    public bool Running => _starts is not null;

    public void Start()
    {
        if (Running) return;
        try
        {
            _starts = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID, ProcessName FROM Win32_ProcessStartTrace"));
            _stops = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID, ProcessName FROM Win32_ProcessStopTrace"));
            _starts.EventArrived += (_, e) => _ = OnStartAsync(Pid(e), Name(e));
            _stops.EventArrived += (_, e) => _ = OnStopAsync(Pid(e));
            _starts.Start();
            _stops.Start();
            AppLog.Info("game", "automatic Game Mode is watching for games");
            _ = Task.Run(CatchUpAsync); // a game that was already running when AI Tweaker started
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn("game", "automatic Game Mode unavailable: " + ex.Message);
            Stop();
        }
    }

    public void Stop()
    {
        foreach (var w in new[] { _starts, _stops })
        {
            try { w?.Stop(); } catch (ManagementException) { /* already stopped */ }
            w?.Dispose();
        }
        _starts = _stops = null;
    }

    /// <summary>Re-reads the game list on the next start event (after the user adds or removes a game).</summary>
    public void Refresh() => _gamesRead = DateTime.MinValue;

    private static int Pid(EventArrivedEventArgs e) => Convert.ToInt32(e.NewEvent["ProcessID"]);
    private static string Name(EventArrivedEventArgs e) => e.NewEvent["ProcessName"]?.ToString() ?? "";

    /// <summary>Called for every process that starts on the PC: a set lookup, with the library re-read at most every 10 minutes.</summary>
    private bool IsGame(string exeName)
    {
        lock (_listLock)
        {
            if (DateTime.UtcNow - _gamesRead > TimeSpan.FromMinutes(10))
            {
                try { _games = GameProfiles.KnownExes().Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase); }
                catch (Exception ex) { AppLog.Warn("game", "game list not read: " + ex.Message); } // a broken library file must not break every process start
                _gamesRead = DateTime.UtcNow;
            }
            return _games.Contains(exeName);
        }
    }

    private async Task CatchUpAsync()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                string name;
                try { name = p.ProcessName + ".exe"; } catch (InvalidOperationException) { continue; }
                if (IsGame(name)) { await OnStartAsync(p.Id, name); return; }
            }
        }
    }

    private async Task OnStartAsync(int pid, string name)
    {
        if (!SettingsStore.Current.AutoGameMode || !IsGame(name)) return;
        await _gate.WaitAsync();
        try
        {
            _running[pid] = name;
            if (GameSession.Active) return; // already on, by hand or for another game
            var groups = SettingsStore.Current.AutoGameGroups is { Count: > 0 } g ? g : [.. DefaultGroups];
            var game = Path.GetFileNameWithoutExtension(name);
            await GameSession.StartAsync(groups, await _profile(), game);
            _emit("gamemode.auto", new { on = true, game });
        }
        catch (Exception ex)
        {
            AppLog.Warn("game", $"automatic Game Mode for {name} failed: {ex.Message}");
        }
        finally { _gate.Release(); }
    }

    private async Task OnStopAsync(int pid)
    {
        if (!_running.TryRemove(pid, out var name) || !_running.IsEmpty) return;

        // launchers often restart the game or hand over to a second process: give it a moment before putting things back
        await Task.Delay(TimeSpan.FromSeconds(8));
        await _gate.WaitAsync();
        try
        {
            if (!_running.IsEmpty || GameSession.AutoGame is null) return;
            if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)) is { Length: > 0 } again)
            {
                foreach (var p in again) using (p) _running[p.Id] = name;
                return;
            }
            var game = GameSession.AutoGame;
            await GameSession.StopAsync();
            _emit("gamemode.auto", new { on = false, game });
        }
        catch (Exception ex)
        {
            AppLog.Warn("game", "automatic Game Mode stop failed: " + ex.Message);
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => Stop();
}
