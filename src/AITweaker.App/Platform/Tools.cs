using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AITweaker.Core.Engine;
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

    /// <summary>Public DNS resolvers offered next to "from the provider" (DHCP).</summary>
    public static readonly Dictionary<string, string[]> DnsPresets = new()
    {
        ["cloudflare"] = ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"],
        ["google"] = ["8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"],
        ["quad9"] = ["9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"],
    };

    private const string ActiveAdapters = "Get-NetAdapter -Physical | Where-Object Status -eq 'Up'";

    /// <summary>Which preset the connected adapters use ("auto" = from the router/provider, "custom" = something else).</summary>
    public static async Task<object> GetDnsAsync()
    {
        var r = await ProcessRunner.PowerShellAsync(
            $"{ActiveAdapters} | ForEach-Object {{ [pscustomobject]@{{ alias = $_.Name; servers = @((Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4).ServerAddresses); dhcp = ((Get-NetIPInterface -InterfaceIndex $_.ifIndex -AddressFamily IPv4).Dhcp -eq 'Enabled') -and -not (Get-ItemProperty -Path ('HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\' + $_.InterfaceGuid) -Name NameServer -ErrorAction SilentlyContinue).NameServer }} }} | ConvertTo-Json -Compress -Depth 3",
            TimeSpan.FromSeconds(30));
        var adapters = new List<(string Alias, string[] Servers, bool Auto)>();
        if (r.Output.Trim().Length > 0)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(r.Output);
            var items = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            foreach (var e in items)
                adapters.Add((e.GetProperty("alias").GetString() ?? "",
                    e.GetProperty("servers").ValueKind == System.Text.Json.JsonValueKind.Array ? e.GetProperty("servers").EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [],
                    e.TryGetProperty("dhcp", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.True));
        }
        string Preset((string Alias, string[] Servers, bool Auto) a) =>
            a.Auto ? "auto" : DnsPresets.FirstOrDefault(p => a.Servers.Length > 0 && p.Value.Contains(a.Servers[0])).Key ?? "custom";
        var presets = adapters.Select(Preset).Distinct().ToList();
        return new
        {
            current = presets.Count == 1 ? presets[0] : presets.Count == 0 ? "auto" : "mixed",
            adapters = adapters.Select(a => new { alias = a.Alias, servers = a.Servers, preset = Preset(a) }),
        };
    }

    /// <param name="preset">auto (back to the provider's DNS) or a key of <see cref="DnsPresets"/>.</param>
    public static async Task SetDnsAsync(string preset)
    {
        string body = preset == "auto"
            ? "Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses"
            : DnsPresets.TryGetValue(preset, out var servers)
                ? $"Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses {string.Join(",", servers.Select(ProcessRunner.PsQuote))}"
                : throw new ArgumentException("unknown DNS preset " + preset);
        var r = await ProcessRunner.PowerShellAsync($"$ErrorActionPreference = 'Stop'; {ActiveAdapters} | ForEach-Object {{ {body} }}; Clear-DnsClientCache", TimeSpan.FromSeconds(60));
        if (!r.Success) throw new InvalidOperationException("DNS change failed: " + r.Error.Trim());
        AppLog.Ok("network", $"DNS -> {preset}");
    }

    /// <summary>Average lookup time of each resolver from this connection (ms; -1 = no answer).</summary>
    public static async Task<Dictionary<string, int>> BenchmarkDnsAsync()
    {
        var targets = new Dictionary<string, string>(DnsPresets.ToDictionary(p => p.Key, p => p.Value[0]));
        var current = await ProcessRunner.PowerShellAsync($"({ActiveAdapters} | Select-Object -First 1 | ForEach-Object {{ (Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4).ServerAddresses }} | Select-Object -First 1)", TimeSpan.FromSeconds(20));
        var cur = current.Output.Trim();
        if (System.Net.IPAddress.TryParse(cur, out _) && !targets.ContainsValue(cur)) targets["current"] = cur;
        var script = string.Join("; ", targets.Select(t =>
            $"$t = @(); foreach ($n in 'www.google.com','www.youtube.com','discord.com','steamcommunity.com','store.epicgames.com') {{ $sw = [Diagnostics.Stopwatch]::StartNew(); try {{ Resolve-DnsName -Name $n -Server {ProcessRunner.PsQuote(t.Value)} -Type A -DnsOnly -NoHostsFile -QuickTimeout -ErrorAction Stop | Out-Null; $t += $sw.ElapsedMilliseconds }} catch {{ }} }}; " +
            $"Write-Output ({ProcessRunner.PsQuote(t.Key + "=")} + $(if ($t.Count) {{ [int](($t | Measure-Object -Average).Average) }} else {{ -1 }}))"));
        var r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromMinutes(2));
        return r.Output.Split('\n').Select(l => l.Trim().Split('=')).Where(a => a.Length == 2 && int.TryParse(a[1], out _))
            .ToDictionary(a => a[0], a => int.Parse(a[1]));
    }

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

/// <param name="Suggest">"off" (not needed at boot), "keep" (drivers' helpers, audio, peripherals, anti-cheat, security) or null.</param>
public sealed record StartupEntry(string Id, string Name, string Command, string Location, bool Enabled, string Scope, string? Suggest);

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
                {
                var cmd = run.GetValue(name)?.ToString() ?? "";
                list.Add(new StartupEntry($"{s.HiveName}|{s.ApprovedKey}|{name}", name, cmd, s.Location, IsEnabled(s.Hive, s.ApprovedKey, name), s.HiveName == "HKCU" ? "user" : "all users", StartupAdvisor.Suggest(name, cmd)));
            }
        }
        foreach (var (folder, hive, hiveName, scope) in Folders())
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(file);
                list.Add(new StartupEntry($"{hiveName}|StartupFolder|{name}", Path.GetFileNameWithoutExtension(file), file, "Startup folder", IsEnabled(hive, "StartupFolder", name), scope, StartupAdvisor.Suggest(name, file)));
            }
        }
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Switches off every enabled entry marked "off" — the same reversible switch as Task Manager's Startup tab.</summary>
    public static int DisableUnneeded()
    {
        var targets = List().Where(e => e.Enabled && e.Suggest == "off").ToList();
        foreach (var e in targets) SetEnabled(e.Id, false);
        AppLog.Ok("startup", $"{targets.Count} startup entries switched off");
        return targets.Count;
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
