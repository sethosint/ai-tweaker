using System.Text;
using AITweaker.Core.Bench;

namespace AITweaker.Core.Tests;

public class BenchTests
{
    private const string Header2 = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,FrameType,CPUStartTime,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,VideoBusy,DisplayLatency,DisplayedTime";

    /// <summary>986 frames of 4 ms and 14 hitches of 20 ms for the game, plus a busy desktop compositor.</summary>
    private static string Capture(string header, Func<string, double, string> row)
    {
        var sb = new StringBuilder(header).Append('\n');
        for (int i = 0; i < 2000; i++) sb.Append(row("dwm.exe", 6.94)).Append('\n');
        for (int i = 0; i < 1000; i++) sb.Append(row("cs2.exe", i % 66 == 65 && i < 990 ? 20 : 4)).Append('\n');
        return sb.ToString();
    }

    private static string Row2(string app, double ms) =>
        $"{app},1234,0x1,DXGI,0,0,1,Hardware: Independent Flip,Application,0.0,{ms},{ms * 0.5},{ms * 0.5},0.1,{ms * 0.9},{ms * 0.95},0.05,0,{ms + 2.5},{ms}";

    [Fact]
    public void SummarisesTheGameNotTheDesktop()
    {
        var s = FrameStats.Parse(Capture(Header2, Row2))!;
        Assert.Equal("cs2.exe", s.App);
        Assert.Equal(1000, s.Frames);
        Assert.InRange(s.AvgFps, 230, 245);
        Assert.Equal(50, s.Low1Fps);          // the 99th percentile frame is a 20 ms hitch
        Assert.Equal(20, s.P99FrameMs);
        Assert.True(s.StuttersPerMin > 100);
        Assert.InRange(s.LatencyMs!.Value, 6.7, 6.75);  // average frame time + 2.5 ms
        Assert.Equal("gpu", s.Bound);          // GPU busy is 95% of every frame
    }

    [Fact]
    public void ReadsPresentMon1Columns()
    {
        const string header1 = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,Dropped,TimeInSeconds,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI,MsUntilRenderComplete,MsUntilDisplayed";
        var s = FrameStats.Parse(Capture(header1, (app, ms) => $"{app},1,0x1,DXGI,0,0,1,Hardware: Independent Flip,0,0.0,{ms},{ms},0.1,{ms},NA"))!;
        Assert.Equal("cs2.exe", s.App);
        Assert.Null(s.LatencyMs);              // "NA" values are skipped
        Assert.Null(s.Bound);
    }

    [Fact]
    public void PicksTheRequestedApp()
    {
        var s = FrameStats.Parse(Capture(Header2, Row2), "dwm.exe")!;
        Assert.Equal("dwm.exe", s.App);
        Assert.Equal(144, Math.Round(s.AvgFps));
    }

    [Fact]
    public void TooFewFramesIsNoResult()
    {
        var csv = Header2 + "\n" + string.Join("\n", Enumerable.Range(0, 10).Select(_ => Row2("game.exe", 5)));
        Assert.Null(FrameStats.Parse(csv));
        Assert.Null(FrameStats.Parse("garbage"));
    }

    [Fact]
    public void ComparesWithNoiseFloor()
    {
        var before = new SystemSnapshot { CpuIdlePercent = 3.2, DpcPercent = 0.40, PingMs = 20, Services = 120 };
        var after = new SystemSnapshot { CpuIdlePercent = 1.1, DpcPercent = 0.42, PingMs = 25, Services = 88 };
        var rows = BenchCompare.System(before, after).ToDictionary(r => r.Key);
        Assert.True(rows["cpuIdlePercent"].Better);
        Assert.Null(rows["dpcPercent"].Better);          // 0.02 points is noise
        Assert.False(rows["pingMs"].Better);
        Assert.Equal(-32, rows["services"].Delta);
        Assert.Equal(-26.7, rows["services"].DeltaPercent);
        Assert.False(rows.ContainsKey("bootSeconds"));    // unknown on both sides

        var fps = BenchCompare.Frames(
            new FrameStats("cs2.exe", 1000, 4, 240, 150, 90, 4.2, 6.7, 3, null, null, null, null),
            new FrameStats("cs2.exe", 1000, 4, 262, 181, 120, 3.8, 5.5, 1, null, null, null, null)).ToDictionary(r => r.Key);
        Assert.True(fps["avgFps"].Better);
        Assert.True(fps["low1Fps"].Better);
        Assert.True(fps["p99FrameMs"].Better);
        Assert.False(fps.ContainsKey("latencyMs"));
    }

    [Fact]
    public void TimerResolutionIsNeverScored()
    {
        var rows = BenchCompare.System(new SystemSnapshot { TimerMs = 15.625 }, new SystemSnapshot { TimerMs = 0.5 }).ToDictionary(r => r.Key);
        Assert.Null(rows["timerMs"].Better);
        Assert.Equal(BenchCompare.SystemMetrics.Length, typeof(SystemSnapshot).GetProperties().Length); // every snapshot field is compared
    }

    [Fact]
    public void PairsFirstWithNewestPerGame()
    {
        static FrameStats F(string app, double fps) => new(app, 1000, 4, fps, fps * 0.6, fps * 0.4, 1000 / fps, 6, 1, null, null, null, null);
        var t0 = new DateTime(2026, 10, 1, 12, 0, 0);
        var entries = new List<BenchEntry>
        {
            new("1", t0, "system", "before", new SystemSnapshot { CpuIdlePercent = 3 }, null),
            new("2", t0.AddMinutes(5), "game", "manual", null, F("cs2.exe", 240)),
            new("3", t0.AddMinutes(9), "game", "manual", null, F("valorant.exe", 400)),
            new("4", t0.AddMinutes(40), "game", "manual", null, F("cs2.exe", 250)),
            new("5", t0.AddMinutes(50), "game", "manual", null, F("CS2.exe", 262)),
        };
        var (system, games) = BenchCompare.Summarize(entries);
        Assert.NotNull(system);
        Assert.Null(system!.After);
        Assert.Equal(3, system.Rows.Single(r => r.Key == "cpuIdlePercent").Before);
        Assert.Equal("cs2.exe", games[0].Title);           // most recent game first
        Assert.Equal("2", games[0].Before.Id);
        Assert.Equal("5", games[0].After!.Id);
        Assert.Equal(22, games[0].Rows.Single(r => r.Key == "avgFps").Delta);
        Assert.Null(games[1].After);
    }
}
