namespace AITweaker.Core.Bench;

/// <summary>What the system looks like at idle: background load, interrupts, timer precision, network and boot time.</summary>
public sealed record SystemSnapshot
{
    public double CpuIdlePercent { get; init; }
    public double DpcPercent { get; init; }
    public double InterruptPercent { get; init; }
    public double DpcPerSec { get; init; }
    public double InterruptsPerSec { get; init; }
    public double ContextSwitchesPerSec { get; init; }
    public int Processes { get; init; }
    public int Threads { get; init; }
    public int Services { get; init; }
    public int StartupItems { get; init; }
    public double RamUsedGb { get; init; }
    public double TimerMs { get; init; }
    /// <summary>How long Sleep(1) really takes with a 1 ms timer, average and worst 1%.</summary>
    public double SleepAvgMs { get; init; }
    public double SleepP99Ms { get; init; }
    public double? RouterPingMs { get; init; }
    public double? RouterJitterMs { get; init; }
    public double? PingMs { get; init; }
    public double? JitterMs { get; init; }
    public double? LossPercent { get; init; }
    public double? BootSeconds { get; init; }
}

/// <param name="Kind">system | game</param>
/// <param name="Label">before | after | manual</param>
public sealed record BenchEntry(string Id, DateTime At, string Kind, string Label, SystemSnapshot? System, FrameStats? Frames);

/// <summary>The first measurement against the newest one, for the system and for each game.</summary>
public sealed record BenchPair(string Title, BenchEntry Before, BenchEntry? After, IReadOnlyList<BenchRow> Rows);

/// <param name="Better">true when the change is an improvement, false when worse, null when it's within noise.</param>
public sealed record BenchRow(string Key, double? Before, double? After, double? Delta, double? DeltaPercent, bool? Better);

public static class BenchCompare
{
    /// <summary>Metric, whether lower is better, and the change below which it's noise (absolute, in the metric's unit).</summary>
    public static readonly (string Key, bool LowerIsBetter, double Noise)[] SystemMetrics =
    [
        ("cpuIdlePercent", true, 0.5), ("dpcPercent", true, 0.05), ("interruptPercent", true, 0.05),
        ("dpcPerSec", true, 50), ("interruptsPerSec", true, 200), ("contextSwitchesPerSec", true, 300),
        ("processes", true, 2), ("threads", true, 50), ("services", true, 2), ("startupItems", true, 0.5),
        // timer resolution is a state (whatever another app asked for), not a score
        ("ramUsedGb", true, 0.1), ("timerMs", true, double.PositiveInfinity), ("sleepAvgMs", true, 0.03), ("sleepP99Ms", true, 0.1),
        ("routerPingMs", true, 0.5), ("routerJitterMs", true, 0.3), ("pingMs", true, 1), ("jitterMs", true, 0.5), ("lossPercent", true, 0.5),
        ("bootSeconds", true, 1),
    ];

    public static readonly (string Key, bool LowerIsBetter, double Noise)[] FrameMetrics =
    [
        ("avgFps", false, 1), ("low1Fps", false, 1), ("low01Fps", false, 1),
        ("p99FrameMs", true, 0.1), ("stuttersPerMin", true, 0.5), ("latencyMs", true, 0.2), ("pcLatencyMs", true, 0.2),
    ];

    /// <summary>System: first vs newest snapshot. Games: first vs newest capture of each game, newest game first.</summary>
    public static (BenchPair? System, List<BenchPair> Games) Summarize(IReadOnlyList<BenchEntry> entries)
    {
        var sys = entries.Where(e => e is { Kind: "system", System: not null }).OrderBy(e => e.At).ToList();
        BenchPair? system = sys.Count == 0 ? null : sys.Count == 1
            ? new BenchPair("system", sys[0], null, System(sys[0].System!, null))
            : new BenchPair("system", sys[0], sys[^1], System(sys[0].System!, sys[^1].System));
        var games = entries.Where(e => e is { Kind: "game", Frames: not null }).OrderBy(e => e.At)
            .GroupBy(e => e.Frames!.App, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.ToList())
            .Select(l => l.Count == 1 ? new BenchPair(l[0].Frames!.App, l[0], null, Frames(l[0].Frames!, null))
                                      : new BenchPair(l[0].Frames!.App, l[0], l[^1], Frames(l[0].Frames!, l[^1].Frames)))
            .OrderByDescending(p => (p.After ?? p.Before).At)
            .ToList();
        return (system, games);
    }

    public static List<BenchRow> System(SystemSnapshot before, SystemSnapshot? after) =>
        Rows(SystemMetrics, k => Value(before, k), k => after is null ? null : Value(after, k));

    public static List<BenchRow> Frames(FrameStats before, FrameStats? after) =>
        Rows(FrameMetrics, k => Value(before, k), k => after is null ? null : Value(after, k));

    private static List<BenchRow> Rows((string Key, bool LowerIsBetter, double Noise)[] metrics, Func<string, double?> before, Func<string, double?> after) =>
        metrics.Select(m =>
        {
            double? b = before(m.Key), a = after(m.Key);
            double? delta = b is { } x && a is { } y ? Math.Round(y - x, 2) : null;
            double? pct = delta is { } d && b is { } bb && bb != 0 ? Math.Round(d / Math.Abs(bb) * 100, 1) : null;
            bool? better = delta is { } dd && Math.Abs(dd) >= m.Noise ? (dd < 0) == m.LowerIsBetter : null;
            return new BenchRow(m.Key, b, a, delta, pct, better);
        }).Where(r => r.Before is not null || r.After is not null).ToList();

    /// <summary>Reads a metric by its camelCase name from either record.</summary>
    private static double? Value(object o, string key)
    {
        var prop = o.GetType().GetProperty(char.ToUpperInvariant(key[0]) + key[1..]);
        return prop?.GetValue(o) switch
        {
            double d => d,
            int i => i,
            _ => null,
        };
    }
}
