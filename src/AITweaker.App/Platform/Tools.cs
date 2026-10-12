using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AITweaker.App.Platform;

// ====================================================================== network quality

public sealed record PingResult(string Host, string Label, int Sent, int Received, double? AvgMs, double? MinMs, double? MaxMs, double? JitterMs, double LossPercent);

public static class NetworkTest
{
    public static readonly (string Host, string Label)[] DefaultTargets =
    [
        ("1.1.1.1", "Cloudflare"),
        ("8.8.8.8", "Google DNS"),
        ("9.9.9.9", "Quad9"),
    ];

    /// <summary>Pings every host <paramref name="count"/> times (in parallel per host) and computes latency, jitter and loss.</summary>
    public static async Task<List<PingResult>> RunAsync(IEnumerable<(string Host, string Label)> targets, int count, Action<string, int, int>? progress = null, CancellationToken ct = default)
    {
        var tasks = targets.Select(async t =>
        {
            using var ping = new Ping();
            var samples = new List<double>();
            for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
            {
                try
                {
                    var r = await ping.SendPingAsync(t.Host, 1000);
                    if (r.Status == IPStatus.Success) samples.Add(r.RoundtripTime);
                }
                catch (PingException) { /* counts as loss */ }
                progress?.Invoke(t.Host, i + 1, count);
                await Task.Delay(120, ct);
            }
            double? jitter = samples.Count > 1 ? samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average() : null;
            return new PingResult(t.Host, t.Label, count, samples.Count,
                samples.Count > 0 ? Math.Round(samples.Average(), 1) : null,
                samples.Count > 0 ? samples.Min() : null,
                samples.Count > 0 ? samples.Max() : null,
                jitter is { } j ? Math.Round(j, 1) : null,
                Math.Round(100.0 * (count - samples.Count) / count, 1));
        });
        var results = (await Task.WhenAll(tasks)).ToList();
        foreach (var r in results) AppLog.Info("net-test", $"{r.Label}: avg {r.AvgMs?.ToString() ?? "—"} ms, jitter {r.JitterMs?.ToString() ?? "—"} ms, loss {r.LossPercent}%");
        return results;
    }

    public static Task<ProcessResult> FlushDnsAsync() => ProcessRunner.RunAsync("ipconfig.exe", "/flushdns");

    /// <summary>Winsock + TCP/IP stack reset (needs a reboot).</summary>
    public static async Task ResetStackAsync()
    {
        await ProcessRunner.RunAsync("netsh.exe", "winsock reset");
        await ProcessRunner.RunAsync("netsh.exe", "int ip reset");
        await ProcessRunner.RunAsync("ipconfig.exe", "/flushdns");
        AppLog.Warn("network", "network stack reset, reboot required");
    }
}

// ====================================================================== startup apps

public sealed record StartupEntry(string Id, string Name, string Command, string Location, bool Enabled, string Scope);

/// <summary>Enables/disables startup apps exactly like Task Manager does (Explorer\StartupApproved flags), so changes are reversible there too.</summary>
public static class StartupManager
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private sealed record Source(RegistryKey Hive, string HiveName, string RunPath, string ApprovedKey, string Location);

    private static IEnumerable<Source> RunSources()
    {
        yield return new(Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "HKCU Run");
        yield return new(Registry.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run", "HKLM Run");
        yield return new(Registry.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", "HKLM Run (32-bit)");
    }

    private static IEnumerable<(string Folder, RegistryKey Hive, string HiveName, string Scope)> Folders()
    {
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, "HKCU", "user");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, "HKLM", "all users");
    }

    private static bool IsEnabled(RegistryKey hive, string approvedKey, string name)
    {
        using var k = hive.OpenSubKey(Approved + approvedKey);
        return k?.GetValue(name) is not byte[] { Length: > 0 } b || (b[0] & 1) == 0; // 02/06 = enabled, 03/07 = disabled
    }

    public static List<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        foreach (var s in RunSources())
        {
            using var run = s.Hive.OpenSubKey(s.RunPath);
            if (run is null) continue;
            foreach (var name in run.GetValueNames().Where(n => n.Length > 0))
                list.Add(new StartupEntry($"{s.HiveName}|{s.ApprovedKey}|{name}", name, run.GetValue(name)?.ToString() ?? "", s.Location, IsEnabled(s.Hive, s.ApprovedKey, name), s.HiveName == "HKCU" ? "user" : "all users"));
        }
        foreach (var (folder, hive, hiveName, scope) in Folders())
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(file);
                list.Add(new StartupEntry($"{hiveName}|StartupFolder|{name}", Path.GetFileNameWithoutExtension(file), file, "Startup folder", IsEnabled(hive, "StartupFolder", name), scope));
            }
        }
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void SetEnabled(string id, bool enabled)
    {
        var parts = id.Split('|', 3);
        if (parts.Length != 3) throw new ArgumentException("bad startup id");
        var hive = parts[0] == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
        using var k = hive.CreateSubKey(Approved + parts[1], writable: true);
        var data = new byte[12];
        data[0] = (byte)(enabled ? 0x02 : 0x03);
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        k.SetValue(parts[2], data, RegistryValueKind.Binary);
        AppLog.Info("startup", $"{parts[2]} -> {(enabled ? "enabled" : "disabled")}");
    }
}

// ====================================================================== cleanup

public sealed record CleanTarget(string Id, string Path, long Bytes, int Files);

public static class Cleaner
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>id → folders. Shader caches rebuild automatically on the next game launch.</summary>
    public static Dictionary<string, string[]> Targets() => new()
    {
        ["temp-user"] = [Path.GetTempPath()],
        ["temp-windows"] = [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")],
        ["shader-nvidia"] = [Path.Combine(Local, "NVIDIA", "DXCache"), Path.Combine(Local, "NVIDIA", "GLCache"), Path.Combine(Local, "NVIDIA Corporation", "NV_Cache")],
        ["shader-amd"] = [Path.Combine(Local, "AMD", "DxCache"), Path.Combine(Local, "AMD", "DxcCache"), Path.Combine(Local, "AMD", "VkCache"), Path.Combine(Local, "AMD", "GLCache")],
        ["shader-directx"] = [Path.Combine(Local, "D3DSCache")],
        ["windows-update"] = [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download")],
        ["crash-dumps"] = [Path.Combine(Local, "CrashDumps"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive")],
    };

    public static List<CleanTarget> Analyze() => Targets().Select(t =>
    {
        long bytes = 0;
        int files = 0;
        foreach (var dir in t.Value.Where(Directory.Exists))
        {
            foreach (var f in SafeFiles(dir))
            {
                try { bytes += new FileInfo(f).Length; files++; } catch { /* vanished */ }
            }
        }
        return new CleanTarget(t.Key, string.Join("; ", t.Value), bytes, files);
    }).ToList();

    public static async Task<long> CleanAsync(IReadOnlyList<string> ids)
    {
        var targets = Targets();
        long freed = 0;
        bool wu = ids.Contains("windows-update");
        if (wu)
        {
            await ProcessRunner.RunAsync("net.exe", "stop wuauserv /y", TimeSpan.FromSeconds(60));
            await ProcessRunner.RunAsync("net.exe", "stop bits /y", TimeSpan.FromSeconds(60));
        }
        try
        {
            foreach (var id in ids)
            {
                if (!targets.TryGetValue(id, out var dirs)) continue;
                long before = freed;
                foreach (var dir in dirs.Where(Directory.Exists))
                {
                    foreach (var f in SafeFiles(dir))
                    {
                        try
                        {
                            long len = new FileInfo(f).Length;
                            File.SetAttributes(f, FileAttributes.Normal);
                            File.Delete(f);
                            freed += len;
                        }
                        catch { /* in use — skip */ }
                    }
                    foreach (var d in SafeDirs(dir).OrderByDescending(d => d.Length))
                    {
                        try { Directory.Delete(d, recursive: false); } catch { /* not empty / in use */ }
                    }
                }
                AppLog.Ok("cleanup", $"{id}: {(freed - before) / 1048576.0:0.0} MB");
            }
        }
        finally
        {
            if (wu)
            {
                await ProcessRunner.RunAsync("net.exe", "start bits", TimeSpan.FromSeconds(60));
                await ProcessRunner.RunAsync("net.exe", "start wuauserv", TimeSpan.FromSeconds(60));
            }
        }
        return freed;
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList(); }
        catch { return []; }
    }
}

// ====================================================================== memory

/// <summary>Purges the standby list (what ISLC does): frees cached memory games may otherwise stutter on.</summary>
public sealed record PurgeResult(ulong FreedBytes, ulong AvailableBytes);

public static class MemoryTools
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const uint SE_PRIVILEGE_ENABLED = 2;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8;

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr ret);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) throw new InvalidOperationException("OpenProcessToken failed");
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) throw new InvalidOperationException("LookupPrivilegeValue failed");
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static PurgeResult PurgeStandby()
    {
        var before = FreeZeroBytes();
        EnablePrivilege("SeProfileSingleProcessPrivilege");
        int cmd = MemoryPurgeStandbyList;
        int status = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));
        if (status != 0) throw new InvalidOperationException($"NtSetSystemInformation failed: 0x{status:X8}");
        var after = FreeZeroBytes();
        AppLog.Ok("memory", $"standby list purged: free+zero {before / 1048576} MB -> {after / 1048576} MB");
        return new PurgeResult(after > before ? after - before : 0, Native.GetMemoryStatus().AvailableBytes);
    }

    /// <summary>"Free & Zero Page List Bytes" — memory not even used as cache.</summary>
    private static ulong FreeZeroBytes()
    {
        foreach (var o in SystemProfiler.Query(@"root\cimv2", "SELECT FreeAndZeroPageListBytes FROM Win32_PerfRawData_PerfOS_Memory"))
            return Convert.ToUInt64(o["FreeAndZeroPageListBytes"] ?? 0);
        return 0;
    }
}

// ====================================================================== system theme

public sealed record SystemTheme(string Accent, bool Dark);

/// <summary>Windows accent color and app light/dark mode; the UI derives its palette from them.</summary>
public static class SystemThemeReader
{
    public static SystemTheme Read()
    {
        string accent = "#0078D4";
        bool dark = false;
        try
        {
            using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (dwm?.GetValue("AccentColor") is int abgr)
            {
                uint v = unchecked((uint)abgr);
                accent = $"#{v & 0xFF:X2}{(v >> 8) & 0xFF:X2}{(v >> 16) & 0xFF:X2}";
            }
            else if (dwm?.GetValue("ColorizationColor") is int argb)
            {
                accent = $"#{unchecked((uint)argb) & 0xFFFFFF:X6}";
            }
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = personalize?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch { /* keep defaults */ }
        return new SystemTheme(accent, dark);
    }
}
