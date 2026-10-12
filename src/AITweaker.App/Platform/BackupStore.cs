using System.Text.Json;

namespace AITweaker.App.Platform;

/// <summary>One captured "before" state. Kinds: registry, service, task, power-setting, power-scheme, bcd, undo-command, undo-powershell, device, display, info.</summary>
public sealed class ChangeRecord
{
    public string Kind { get; set; } = "";
    public Dictionary<string, string?> Data { get; set; } = [];

    public static ChangeRecord Of(string kind, params (string Key, string? Value)[] data) =>
        new() { Kind = kind, Data = data.ToDictionary(d => d.Key, d => d.Value) };

    public string? this[string key] => Data.GetValueOrDefault(key);
}

public sealed class TweakBackup
{
    public string TweakId { get; set; } = "";
    public DateTime AppliedAt { get; set; }
    public List<ChangeRecord> Changes { get; set; } = [];
}

public sealed class SessionLog
{
    public string Id { get; set; } = "";
    public DateTime Time { get; set; }
    public string Kind { get; set; } = "apply";
    public string Profile { get; set; } = "";
    public List<string> Applied { get; set; } = [];
    public List<string> Failed { get; set; } = [];
    public bool RestorePoint { get; set; }
}

/// <summary>
/// Keeps the ORIGINAL pre-tweak state of every applied tweak (C:\ProgramData\AITweaker\backups\state.json).
/// Re-applying a tweak never overwrites the original snapshot, so rollback always returns to what Windows had before AI Tweaker.
/// </summary>
public static class BackupStore
{
    private static readonly string StateFile = Path.Combine(AppPaths.Backups, "state.json");
    private static readonly string SessionsFile = Path.Combine(AppPaths.Backups, "sessions.json");
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static Dictionary<string, TweakBackup>? _state;

    private static Dictionary<string, TweakBackup> State
    {
        get
        {
            if (_state is not null) return _state;
            try
            {
                _state = File.Exists(StateFile) ? JsonSerializer.Deserialize<Dictionary<string, TweakBackup>>(File.ReadAllText(StateFile)) : null;
            }
            catch (Exception ex)
            {
                var broken = StateFile + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
                File.Copy(StateFile, broken, overwrite: true);
                AppLog.Error("backup", $"state.json unreadable ({ex.Message}); kept a copy at {broken}");
            }
            return _state ??= new Dictionary<string, TweakBackup>();
        }
    }

    public static bool Has(string tweakId)
    {
        lock (Gate) return State.ContainsKey(tweakId);
    }

    public static TweakBackup? Get(string tweakId)
    {
        lock (Gate) return State.GetValueOrDefault(tweakId);
    }

    public static IReadOnlyList<TweakBackup> All()
    {
        lock (Gate) return State.Values.OrderByDescending(b => b.AppliedAt).ToList();
    }

    /// <summary>Stores the snapshot, appending only records the original snapshot doesn't have yet.</summary>
    public static void Save(string tweakId, IReadOnlyList<ChangeRecord> captured)
    {
        lock (Gate)
        {
            if (State.TryGetValue(tweakId, out var existing))
            {
                existing.AppliedAt = DateTime.Now;
                if (captured.Count > 0) existing.Changes.AddRange(captured);
            }
            else
            {
                State[tweakId] = new TweakBackup { TweakId = tweakId, AppliedAt = DateTime.Now, Changes = captured.ToList() };
            }
            Flush();
        }
    }

    public static void Remove(string tweakId)
    {
        lock (Gate)
        {
            if (State.Remove(tweakId)) Flush();
        }
    }

    private static void Flush()
    {
        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(State, Json));
        File.Move(tmp, StateFile, overwrite: true);
    }

    public static void AddSession(SessionLog s)
    {
        lock (Gate)
        {
            var list = Sessions();
            list.Insert(0, s);
            File.WriteAllText(SessionsFile, JsonSerializer.Serialize(list.Take(200).ToList(), Json));
        }
    }

    public static List<SessionLog> Sessions()
    {
        lock (Gate)
        {
            try
            {
                return File.Exists(SessionsFile) ? JsonSerializer.Deserialize<List<SessionLog>>(File.ReadAllText(SessionsFile)) ?? [] : [];
            }
            catch
            {
                return [];
            }
        }
    }
}
