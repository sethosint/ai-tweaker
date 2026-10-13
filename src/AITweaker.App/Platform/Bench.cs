using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AITweaker.Core.Bench;
using AITweaker.Core.Catalog;

namespace AITweaker.App.Platform;

/// <summary>Measurement history (C:\ProgramData\AITweaker\bench\history.json), newest last.</summary>
public static class BenchStore
{
    private static readonly string HistoryFile = Path.Combine(AppPaths.Bench, "history.json");
    private static readonly object Gate = new();

    public static List<BenchEntry> List()
    {
        lock (Gate)
        {
            try { return File.Exists(HistoryFile) ? JsonSerializer.Deserialize<List<BenchEntry>>(File.ReadAllText(HistoryFile), JsonDefaults.Options) ?? [] : []; }
            catch (Exception ex) { AppLog.Warn("bench", "history unreadable: " + ex.Message); return []; }
        }
    }

    public static BenchEntry Add(string kind, string label, SystemSnapshot? system, FrameStats? frames)
    {
        var entry = new BenchEntry(Guid.NewGuid().ToString("N")[..10], DateTime.Now, kind, label, system, frames);
        lock (Gate)
        {
            var list = List();
            list.Add(entry);
            Save(list.TakeLast(60).ToList());
        }
        return entry;
    }

    public static void Remove(string id) { lock (Gate) Save(List().Where(e => e.Id != id).ToList()); }
    public static void Clear() { lock (Gate) Save([]); }

    private static void Save(List<BenchEntry> list) => File.WriteAllText(HistoryFile, JsonSerializer.Serialize(list, JsonDefaults.Options));
}

/// <summary>
/// Idle-system measurement: background CPU, DPC/interrupt load, context switches, processes and services, RAM,
/// how precisely a thread wakes from Sleep(1), router and internet ping, and the last boot time.
/// </summary>
public static class SystemBench
{
    public static async Task<SystemSnapshot> RunAsync(Action<string, int>? progress, CancellationToken ct)
    {
        progress?.Invoke("counters", 5);
        var countersTask = Task.Run(() => SampleCounters(TimeSpan.FromSeconds(8), ct), ct);
        var gateway = Gateway();
        var targets = new List<(string, string)> { ("1.1.1.1", "internet") };
        if (gateway is not null) targets.Insert(0, (gateway, "router"));
        var pingTask = NetworkTest.RunAsync(targets, 40, ct: ct);

        var counters = await countersTask;
        progress?.Invoke("network", 60);
        var pings = await pingTask;
        progress?.Invoke("timer", 75);
        var (sleepAvg, sleepP99) = await Task.Run(SleepPrecision, ct);
        progress?.Invoke("boot", 90);
        var boot = await BootSecondsAsync();
        var (_, total, avail) = Native.GetMemoryStatus();
        var router = pings.FirstOrDefault(r => r.Label == "router");
        var net = pings.First(r => r.Label == "internet");

        var snap = new SystemSnapshot
        {
            CpuIdlePercent = R(counters.GetValueOrDefault("cpu")),
            DpcPercent = R(counters.GetValueOrDefault("dpc"), 2),
            InterruptPercent = R(counters.GetValueOrDefault("isr"), 2),
            DpcPerSec = R(counters.GetValueOrDefault("dpcRate"), 0),
            InterruptsPerSec = R(counters.GetValueOrDefault("isrRate"), 0),
            ContextSwitchesPerSec = R(counters.GetValueOrDefault("ctx"), 0),
            Processes = (int)counters.GetValueOrDefault("processes"),
            Threads = (int)counters.GetValueOrDefault("threads"),
            Services = RunningServices(),
            StartupItems = StartupManager.List().Count(e => e.Enabled),
            RamUsedGb = R((total - avail) / 1073741824.0, 2),
            TimerMs = Sensors.TimerResolutionMs(),
            SleepAvgMs = sleepAvg,
            SleepP99Ms = sleepP99,
            RouterPingMs = router?.AvgMs,
            RouterJitterMs = router?.JitterMs,
            PingMs = net.AvgMs,
            JitterMs = net.JitterMs,
            LossPercent = net.LossPercent,
            BootSeconds = boot,
        };
        progress?.Invoke("done", 100);
        AppLog.Info("bench", $"system: cpu {snap.CpuIdlePercent}% dpc {snap.DpcPercent}% isr {snap.InterruptPercent}% sleep(1) {snap.SleepAvgMs}/{snap.SleepP99Ms} ms ping {snap.PingMs} ms");
        return snap;
    }

    private static readonly (string Key, string Path)[] Counters =
    [
        ("cpu", @"\Processor(_Total)\% Processor Time"),
        ("dpc", @"\Processor(_Total)\% DPC Time"),
        ("isr", @"\Processor(_Total)\% Interrupt Time"),
        ("dpcRate", @"\Processor(_Total)\DPCs Queued/sec"),
        ("isrRate", @"\Processor(_Total)\Interrupts/sec"),
        ("ctx", @"\System\Context Switches/sec"),
        ("processes", @"\System\Processes"),
        ("threads", @"\System\Threads"),
    ];

    /// <summary>Averages each counter over <paramref name="span"/>, one sample per second.</summary>
    private static Dictionary<string, double> SampleCounters(TimeSpan span, CancellationToken ct)
    {
        var sums = new Dictionary<string, double>();
        if (PdhOpenQuery(null, IntPtr.Zero, out var query) != 0) return sums;
        try
        {
            var handles = new List<(string Key, IntPtr Counter)>();
            foreach (var (key, path) in Counters)
                if (PdhAddEnglishCounter(query, path, IntPtr.Zero, out var c) == 0) handles.Add((key, c));
            PdhCollectQueryData(query);
            int samples = 0;
            for (var sw = Stopwatch.StartNew(); sw.Elapsed < span && !ct.IsCancellationRequested;)
            {
                Thread.Sleep(1000);
                if (PdhCollectQueryData(query) != 0) continue;
                samples++;
                foreach (var (key, c) in handles)
                    // CStatus 0 = valid data, 1 = new data (also valid)
                    if (PdhGetFormattedCounterValue(c, PDH_FMT_DOUBLE, IntPtr.Zero, out var v) == 0 && v.CStatus <= 1)
                        sums[key] = sums.GetValueOrDefault(key) + v.Double;
            }
            return samples == 0 ? sums : sums.ToDictionary(kv => kv.Key, kv => kv.Value / samples);
        }
        finally
        {
            PdhCloseQuery(query);
        }
    }

    /// <summary>Asks for a 1 ms timer like a game does and measures how long Sleep(1) really takes.</summary>
    private static (double Avg, double P99) SleepPrecision()
    {
        timeBeginPeriod(1);
        try
        {
            var samples = new List<double>(400);
            var sw = new Stopwatch();
            for (int i = 0; i < 400; i++)
            {
                sw.Restart();
                Thread.Sleep(1);
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            return (R(samples.Average(), 3), R(FrameStats.Percentile(samples, 99), 3));
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    /// <summary>Last boot time from the Diagnostics-Performance log (event 100), if Windows recorded one.</summary>
    private static async Task<double?> BootSecondsAsync()
    {
        try
        {
            var r = await ProcessRunner.RunAsync("wevtutil.exe", "qe Microsoft-Windows-Diagnostics-Performance/Operational /q:\"*[System[(EventID=100)]]\" /c:1 /rd:true /f:xml", TimeSpan.FromSeconds(20));
            var m = Regex.Match(r.Output, @"Name=['""]BootTime['""]>(\d+)<");
            return m.Success ? Math.Round(long.Parse(m.Groups[1].Value) / 1000.0, 1) : null;
        }
        catch
        {
            return null;
        }
    }

    private static int RunningServices()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Service WHERE State='Running'");
            return s.Get().Count;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>The router of the active connection: its ping shows Wi-Fi/LAN quality on its own.</summary>
    private static string? Gateway() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(System.Net.IPAddress.Any))?.ToString();

    private static double R(double v, int digits = 1) => Math.Round(v, digits);

    private const uint PDH_FMT_DOUBLE = 0x00000200;

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double Double;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PdhValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
}

/// <summary>
/// Frame-time capture with Intel PresentMon (MIT, GameTechDev/PresentMon). The console build is pinned and its SHA-256 checked,
/// so what runs is exactly the published release.
/// </summary>
public static class PresentMonTool
{
    private const string Version = "2.6.0";
    private const string Url = "https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe";
    private const string Sha256 = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";

    public static string ExePath => Path.Combine(AppPaths.Tools, $"PresentMon-{Version}-x64.exe");

    public static bool Ready => File.Exists(ExePath);

    public static async Task<string> EnsureAsync(Action<long, long?>? progress, CancellationToken ct)
    {
        if (File.Exists(ExePath) && HashOf(ExePath) == Sha256) return ExePath;
        var tmp = ExePath + ".download";
        await Downloader.DownloadAsync(Url, tmp, progress, ct);
        if (HashOf(tmp) != Sha256)
        {
            File.Delete(tmp);
            throw new InvalidDataException("PresentMon: the downloaded file doesn't match the published checksum, so it was deleted.");
        }
        File.Move(tmp, ExePath, overwrite: true);
        return ExePath;
    }

    /// <summary>
    /// Waits <paramref name="delay"/> s (time to switch back to the game), records <paramref name="seconds"/> s of every process that
    /// presents frames, beeps at start and end, and summarises the busiest game.
    /// </summary>
    public static async Task<FrameStats> CaptureAsync(int seconds, int delay, Action<string, int>? progress, CancellationToken ct)
    {
        var exe = await EnsureAsync((done, total) => progress?.Invoke("download", total is > 0 ? (int)(done * 30 / total.Value) : 10), ct);
        var csv = Path.Combine(AppPaths.Bench, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var full = $"--output_file \"{csv}\" --delay {delay} --timed {seconds} --terminate_after_timed --no_console_stats --session_name AITweaker_PresentMon --stop_existing_session";
        var minimal = $"--output_file \"{csv}\" --delay {delay} --timed {seconds} --terminate_after_timed";

        progress?.Invoke("waiting", 35);
        using var beeps = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(delay), beeps.Token);
            Beep(880, 180);
            progress?.Invoke("recording", 40);
            for (int s = 0; s < seconds; s++)
            {
                await Task.Delay(1000, beeps.Token);
                progress?.Invoke("recording", 40 + 55 * (s + 1) / seconds);
            }
            Beep(660, 140); Beep(660, 140);
        }, beeps.Token);

        var timeout = TimeSpan.FromSeconds(delay + seconds + 60);
        var r = await ProcessRunner.RunAsync(exe, full, timeout, ct: ct, workingDir: AppPaths.Tools);
        if (!File.Exists(csv) && Regex.IsMatch(r.Output + r.Error, "argument|option|usage", RegexOptions.IgnoreCase))
        {
            AppLog.Warn("bench", "PresentMon rejected the extended options, retrying with the basic ones");
            r = await ProcessRunner.RunAsync(exe, minimal, timeout, ct: ct, workingDir: AppPaths.Tools);
        }
        beeps.Cancel();
        if (!File.Exists(csv))
            throw new InvalidOperationException("PresentMon: " + LastLine(r.Error + "\n" + r.Output));

        var stats = FrameStats.Parse(await File.ReadAllTextAsync(csv, ct))
            ?? throw new InvalidOperationException("no-game");
        progress?.Invoke("done", 100);
        AppLog.Info("bench", $"{stats.App}: {stats.AvgFps} fps avg, 1% low {stats.Low1Fps}, 0.1% low {stats.Low01Fps}, p99 {stats.P99FrameMs} ms");
        return stats;
    }

    private static string HashOf(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f));
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "no output";

    private static void Beep(int hz, int ms)
    {
        try { NativeBeep((uint)hz, (uint)ms); } catch { /* no sound device */ }
    }

    [DllImport("kernel32.dll", EntryPoint = "Beep")] private static extern bool NativeBeep(uint frequency, uint duration);
}
