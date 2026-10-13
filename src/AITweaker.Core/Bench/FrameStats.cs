using System.Globalization;

namespace AITweaker.Core.Bench;

/// <summary>What a PresentMon capture says about one game: FPS, lows, stutter, latency and what holds the frame rate back.</summary>
/// <param name="Bound">gpu | cpu | null when PresentMon didn't report GPU busy time.</param>
public sealed record FrameStats(
    string App, int Frames, double Seconds,
    double AvgFps, double Low1Fps, double Low01Fps,
    double AvgFrameMs, double P99FrameMs, double StuttersPerMin,
    double? LatencyMs, double? PcLatencyMs, double? GpuBusyPercent, string? Bound)
{
    /// <summary>Processes that present frames but aren't the game.</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm.exe", "explorer.exe", "AITweaker.exe", "msedgewebview2.exe", "msedge.exe", "chrome.exe", "firefox.exe",
        "ShellExperienceHost.exe", "StartMenuExperienceHost.exe", "SearchHost.exe", "TextInputHost.exe", "obs64.exe",
        "Discord.exe", "steamwebhelper.exe", "EpicWebHelper.exe", "NVIDIA Overlay.exe", "Taskmgr.exe", "PresentMon.exe",
    };

    /// <summary>
    /// Reads a PresentMon CSV (2.x names, or 1.x "Ms…" names) and summarises the busiest swap chain of <paramref name="app"/>,
    /// or of whichever non-system process presented the most frames.
    /// </summary>
    public static FrameStats? Parse(string csv, string? app = null)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return null;
        var head = lines[0].Split(',').Select(h => h.Trim()).ToList();
        int Col(params string[] names) => names.Select(n => head.FindIndex(h => h.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(i => i >= 0, -1);
        int cApp = Col("Application"), cChain = Col("SwapChainAddress");
        int cFrame = Col("FrameTime", "MsBetweenPresents", "MsBetweenAppStart");
        int cLatency = Col("DisplayLatency", "MsDisplayLatency", "MsUntilDisplayed");
        int cPc = Col("PCLatency", "MsPCLatency");
        int cGpu = Col("GPUBusy", "MsGPUBusy");
        if (cApp < 0 || cFrame < 0) return null;

        var rows = lines.Skip(1).Select(l => l.Split(',')).Where(r => r.Length > cFrame).ToList();
        var target = app ?? rows.Select(r => r[cApp]).Where(a => !NotGames.Contains(a))
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        if (target is null) return null;
        var mine = rows.Where(r => r[cApp].Equals(target, StringComparison.OrdinalIgnoreCase)).ToList();
        if (cChain >= 0) mine = mine.GroupBy(r => r.Length > cChain ? r[cChain] : "").OrderByDescending(g => g.Count()).First().ToList();

        var frames = Numbers(mine, cFrame).Where(v => v > 0).ToList();
        if (frames.Count < 30) return null;
        double total = frames.Sum();
        var sorted = frames.OrderBy(v => v).ToList();
        double median = Percentile(sorted, 50);
        double p99 = Percentile(sorted, 99), p999 = Percentile(sorted, 99.9);
        // A stutter is a frame that takes more than 2.5x the typical frame: you see it as a hitch.
        int stutters = frames.Count(v => v > median * 2.5 && v > median + 4);

        double? latency = Average(Numbers(mine, cLatency));
        double? pc = Average(Numbers(mine, cPc));
        double? gpuBusy = Average(Numbers(mine, cGpu)) is { } busy ? Math.Min(100, busy / (total / frames.Count) * 100) : null;
        // >90% GPU busy: the card is the limit; well below: the CPU (or a frame cap) is.
        string? bound = gpuBusy is { } g ? g >= 90 ? "gpu" : g < 75 ? "cpu" : null : null;

        return new FrameStats(target, frames.Count, Math.Round(total / 1000, 1),
            R(frames.Count / (total / 1000)), R(1000 / p99), R(1000 / p999),
            R(total / frames.Count, 2), R(p99, 2), R(stutters / (total / 60000)),
            latency is { } l ? R(l, 2) : null, pc is { } p ? R(p, 2) : null, gpuBusy is { } gb ? R(gb) : null, bound);
    }

    private static IEnumerable<double> Numbers(List<string[]> rows, int col)
    {
        if (col < 0) yield break;
        foreach (var r in rows)
            if (r.Length > col && double.TryParse(r[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v))
                yield return v;
    }

    private static double? Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count > 0 ? list.Average() : null;
    }

    /// <summary>Nearest-rank percentile of an ascending list.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int rank = (int)Math.Ceiling(p / 100 * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private static double R(double v, int digits = 1) => Math.Round(v, digits);
}
