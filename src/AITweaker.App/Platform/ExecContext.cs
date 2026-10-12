using AITweaker.Core.Conditions;
using AITweaker.Core.Profile;

namespace AITweaker.App.Platform;

/// <summary>Per-run state shared by action handlers.</summary>
public sealed class ExecContext(SystemProfile profile, CancellationToken ct = default)
{
    public SystemProfile Profile { get; } = profile;
    public Dictionary<string, object?> Facts { get; } = profile.ToFacts();
    public CancellationToken Ct { get; } = ct;

    /// <summary>When true, handlers record the state they overwrite (first application of a tweak).</summary>
    public bool Capture { get; set; } = true;

    public List<ChangeRecord> Changes { get; } = [];

    /// <summary>NIC interface GUIDs whose driver properties changed; restarted once at the end of the run.</summary>
    public HashSet<string> AdaptersToRestart { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentTweak { get; set; } = "";

    private string? _bcdCache;

    public bool Matches(string? when) => ConditionEvaluator.Evaluate(when, Facts);

    public void Record(ChangeRecord r)
    {
        if (Capture) Changes.Add(r);
    }

    public void Log(string text) => AppLog.Info(CurrentTweak.Length > 0 ? CurrentTweak : "exec", text);

    public async Task<string> BcdEnumAsync()
    {
        if (_bcdCache is not null) return _bcdCache;
        var r = await ProcessRunner.RunAsync("bcdedit.exe", "/enum {current}", ct: Ct);
        return _bcdCache = r.Output;
    }

    public void InvalidateBcd() => _bcdCache = null;
}
