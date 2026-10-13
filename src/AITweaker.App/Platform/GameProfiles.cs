using System.Text.Json;
using System.Text.RegularExpressions;
using AITweaker.Core.Profile;
using Microsoft.Win32;

namespace AITweaker.App.Platform;

/// <summary>
/// Per-game settings Windows keeps by executable: network priority (a QoS policy tagging the game's packets DSCP 46,
/// which routers and Wi-Fi that honour QoS send first), the high-performance GPU on PCs with two graphics adapters
/// (Settings → Display → Graphics) and, only if the user wants it, high CPU priority (Image File Execution Options).
/// Games are found in the Steam and Epic libraries; any other .exe can be added by hand.
/// </summary>
public static partial class GameProfiles
{
    public sealed record Game(string Name, string Exe, string Source, bool Added, bool Priority, bool Gpu, bool Qos = false, bool NoFso = false);

    private const string Ifeo = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string GpuPrefs = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    /// <summary>The exe's Properties → Compatibility flags; "Disable fullscreen optimizations" is this token.</summary>
    private const string Layers = @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string NoFsoFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";
    private const string HighGpu = "GpuPreference=2;";
    private const int HighPriority = 3;

    private static string AddedFile => Path.Combine(AppPaths.Data, "games.json");
    private static string QosFile => Path.Combine(AppPaths.Data, "games-qos.json");

    /// <summary>Tools, launchers and installers that ship next to the game and must not get its settings.</summary>
    [GeneratedRegex(@"(?i)(unins|setup|install|redist|vc_?redist|dxsetup|directx|crash|report|launcher|helper|update|easyanticheat|eac_|battleye|be_?service|cef|webhelper|dotnet|prereq|bootstrap|server|editor|benchmark|config|settings|7z|uploader|overlay)")]
    private static partial Regex NotTheGame();

    /// <summary>Steam tools that are not games.</summary>
    private static readonly HashSet<string> SteamTools = ["228980", "1070560", "1391110", "1628350", "1493710", "250820", "1826330", "2180100"];

    public static object List(SystemProfile profile)
    {
        var games = Detect().Concat(LoadAdded().Select(e => new Game(Path.GetFileNameWithoutExtension(e), e, "manual", true, false, false)))
            .Where(g => File.Exists(g.Exe))
            .GroupBy(g => g.Exe, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Select(g => g with { Priority = HasPriority(g.Exe), Gpu = HasGpu(g.Exe), Qos = LoadQos().Contains(g.Exe, StringComparer.OrdinalIgnoreCase), NoFso = Flags(g.Exe).Contains(NoFsoFlag) })
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new { twoGpus = TwoGpus(profile), games };
    }

    /// <summary>Two graphics adapters (integrated + discrete): only then does the GPU choice mean anything.</summary>
    public static bool TwoGpus(SystemProfile profile) => profile.Gpus.Count >= 2;

    public static async Task<bool> SetAsync(string exe, bool? priority, bool? gpu, bool? qos, bool? noFso = null)
    {
        if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("not a program: " + exe);
        var perf = $@"{Ifeo}\{Path.GetFileName(exe)}\PerfOptions";
        if (priority == true) RegistryHelper.Write(perf, "CpuPriorityClass", HighPriority, RegistryValueKind.DWord);
        if (priority == false && HasPriority(exe)) RegistryHelper.DeleteValue(perf, "CpuPriorityClass");
        if (gpu == true) RegistryHelper.Write(GpuPrefs, exe, HighGpu, RegistryValueKind.String);
        if (gpu == false && HasGpu(exe)) RegistryHelper.DeleteValue(GpuPrefs, exe);
        if (qos is { } q) await SetQosAsync([exe], q);
        if (noFso is { } nf) SetFlag(exe, NoFsoFlag, nf);
        AppLog.Ok("games", $"{Path.GetFileName(exe)}: priority {Show(priority)}, GPU {Show(gpu)}, QoS {Show(qos)}");
        return true;
    }

    /// <summary>Every game found gets network priority, and the discrete GPU where there are two. CPU priority stays the user's call.</summary>
    public static async Task<int> SetAllAsync(SystemProfile profile)
    {
        bool gpu = TwoGpus(profile);
        var exes = Detect().Select(g => g.Exe).Concat(LoadAdded()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (gpu) foreach (var e in exes) RegistryHelper.Write(GpuPrefs, e, HighGpu, RegistryValueKind.String);
        await SetQosAsync(exes, true);
        AppLog.Ok("games", $"{exes.Count} games: QoS on{(gpu ? ", high-performance GPU" : "")}");
        return exes.Count;
    }

    /// <summary>
    /// One QoS policy per game (New-NetQosPolicy, DSCP 46 "Expedited Forwarding"). Windows only tags packets on PCs
    /// outside a domain when "Do not use NLA" is set. It only helps where the router or Wi-Fi honours DSCP; it never hurts.
    /// </summary>
    private static async Task SetQosAsync(IReadOnlyCollection<string> exes, bool on)
    {
        if (exes.Count == 0) return;
        var lines = new List<string>();
        if (on) lines.Add(@"New-Item -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\QoS' -Force | Out-Null; Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\QoS' -Name 'Do not use NLA' -Value '1' -Type String");
        foreach (var exe in exes)
        {
            var name = ProcessRunner.PsQuote("AITweaker " + Path.GetFileName(exe));
            lines.Add($"Remove-NetQosPolicy -Name {name} -Confirm:$false -ErrorAction SilentlyContinue");
            if (on) lines.Add($"New-NetQosPolicy -Name {name} -AppPathNameMatchCondition {ProcessRunner.PsQuote(Path.GetFileName(exe))} -DSCPAction 46 -NetworkProfile All -ErrorAction Stop | Out-Null");
        }
        var r = await ProcessRunner.PowerShellAsync(string.Join("\n", lines), TimeSpan.FromMinutes(2));
        if (!r.Success) throw new InvalidOperationException("QoS: " + (r.Error.Length > 0 ? r.Error : r.Output).Trim());
        var set = LoadQos().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in exes) { if (on) set.Add(e); else set.Remove(e); }
        File.WriteAllText(QosFile, JsonSerializer.Serialize(set.ToList()));
    }

    private static List<string> LoadQos()
    {
        try { return File.Exists(QosFile) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(QosFile)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    public static bool Add(string exe)
    {
        if (!File.Exists(exe)) return false;
        var list = LoadAdded();
        if (!list.Contains(exe, StringComparer.OrdinalIgnoreCase)) list.Add(exe);
        File.WriteAllText(AddedFile, JsonSerializer.Serialize(list));
        return true;
    }

    /// <summary>Forgets a game added by hand and takes its settings back off.</summary>
    public static async Task<bool> RemoveAsync(string exe)
    {
        var list = LoadAdded();
        list.RemoveAll(e => e.Equals(exe, StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(AddedFile, JsonSerializer.Serialize(list));
        if (File.Exists(exe)) await SetAsync(exe, false, false, false, false);
        return true;
    }

    private static string Show(bool? v) => v switch { true => "on", false => "off", null => "unchanged" };

    private static bool HasPriority(string exe) =>
        RegistryHelper.Read($@"{Ifeo}\{Path.GetFileName(exe)}\PerfOptions", "CpuPriorityClass") is { Exists: true, Data: var d } && d == HighPriority.ToString();

    /// <summary>Compatibility flags of the exe, the same list its Properties → Compatibility tab edits.</summary>
    private static HashSet<string> Flags(string exe) =>
        (RegistryHelper.Read(Layers, exe).Data ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(f => f != "~").ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void SetFlag(string exe, string flag, bool on)
    {
        var flags = Flags(exe);
        if (on ? !flags.Add(flag) : !flags.Remove(flag)) return;
        if (flags.Count == 0) RegistryHelper.DeleteValue(Layers, exe);
        else RegistryHelper.Write(Layers, exe, "~ " + string.Join(' ', flags), RegistryValueKind.String);
    }

    private static bool HasGpu(string exe) =>
        RegistryHelper.Read(GpuPrefs, exe) is { Exists: true, Data: var d } && d?.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase) == true;

    private static List<string> LoadAdded()
    {
        try { return File.Exists(AddedFile) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(AddedFile)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    // ------------------------------------------------------------------ detection

    public static List<Game> Detect()
    {
        var games = new List<Game>();
        try { games.AddRange(Steam()); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("warn", "games", "Steam: " + ex.Message); }
        try { games.AddRange(Epic()); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { AppLog.Write("warn", "games", "Epic: " + ex.Message); }
        return games;
    }

    private static IEnumerable<Game> Steam()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is not string steam) yield break;
        steam = steam.Replace('/', '\\');
        var libs = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            libs.AddRange(Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var text = File.ReadAllText(acf);
                string Field(string name) => Regex.Match(text, $"\"{name}\"\\s+\"([^\"]*)\"").Groups[1].Value;
                var (id, name, dir) = (Field("appid"), Field("name"), Field("installdir"));
                if (SteamTools.Contains(id) || name.Length == 0 || dir.Length == 0 || name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase)) continue;
                var exe = PickExe(Path.Combine(apps, "common", dir), null);
                if (exe is not null) yield return new Game(name, exe, "steam", false, false, false);
            }
        }
    }

    private static IEnumerable<Game> Epic()
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
        if (!Directory.Exists(manifests)) yield break;
        foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(item));
            var r = doc.RootElement;
            string Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var (name, dir, launch) = (Str("DisplayName"), Str("InstallLocation"), Str("LaunchExecutable"));
            if (name.Length == 0 || dir.Length == 0 || !Directory.Exists(dir)) continue;
            var exe = PickExe(dir, launch.Length > 0 ? Path.Combine(dir, launch) : null);
            if (exe is not null) yield return new Game(name, exe, "epic", false, false, false);
        }
    }

    /// <summary>
    /// The game's own executable: an Unreal "-Shipping" binary when there is one (the top-level exe is only a stub),
    /// then the launcher's own choice, then the biggest program that isn't a tool or installer.
    /// </summary>
    private static string? PickExe(string dir, string? hint)
    {
        if (!Directory.Exists(dir)) return null;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var exes = Directory.EnumerateFiles(dir, "*.exe", opts).Where(f => !NotTheGame().IsMatch(Path.GetFileNameWithoutExtension(f))).ToList();
        var shipping = exes.FirstOrDefault(f => f.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase));
        if (shipping is not null) return shipping;
        if (hint is not null && File.Exists(hint)) return hint;
        return exes.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
    }
}
