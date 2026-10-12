using System.Text.Json;
using AITweaker.App.Platform.Actions;
using AITweaker.Core.Catalog;
using AITweaker.Core.Model;

namespace AITweaker.App.Platform;

/// <summary>
/// Every Windows service with its start type and state: switch any of them on or off by hand, start or stop it.
/// The first manual change of a service saves its original start type so it can be put back with one click.
/// </summary>
public static class ServiceManager
{
    public sealed record ServiceInfo(string Name, string DisplayName, string Description, string State, string Start, bool Essential, bool Changed, List<string> Tweaks);

    /// <summary>Services Windows can't run without: never offered for "Disabled".</summary>
    private static readonly HashSet<string> Essential = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "BrokerInfrastructure", "SystemEventsBroker", "Power", "PlugPlay",
        "ProfSvc", "Schedule", "Winmgmt", "EventLog", "CryptSvc", "Dhcp", "Dnscache", "nsi", "mpssvc", "BFE", "WinDefend",
        "SamSs", "gpsvc", "CoreMessagingRegistrar", "StateRepository", "AppXSvc", "ClipSVC", "AudioSrv", "AudioEndpointBuilder",
        "UserManager", "TrustedInstaller", "msiserver", "wscsvc", "SecurityHealthService", "TimeBrokerSvc", "Netman",
        "NlaSvc", "netprofm", "LanmanWorkstation", "KeyIso", "VaultSvc", "TokenBroker", "camsvc", "DispBrokerDesktopSvc",
        "WinHttpAutoProxySvc", "Wcmsvc", "WlanSvc", "EapHost", "DeviceInstall", "DsmSvc", "Themes", "FontCache", "ShellHWDetection",
    };

    private static string OriginalsFile => Path.Combine(AppPaths.Backups, "services-manual.json");

    public static List<ServiceInfo> List(TweakCatalog catalog)
    {
        var byService = catalog.Tweaks
            .SelectMany(t => t.Actions.OfType<ServiceAction>().Select(a => (a.Name, t.Id)))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);
        var originals = LoadOriginals();
        var list = new List<ServiceInfo>();
        foreach (var o in SystemProfiler.Query(@"root\cimv2", "SELECT Name, DisplayName, Description, State, StartMode, DelayedAutoStart FROM Win32_Service"))
        {
            var name = o["Name"]?.ToString() ?? "";
            if (name.Length == 0) continue;
            var mode = o["StartMode"]?.ToString() ?? "Manual";
            var start = mode switch
            {
                "Auto" when o["DelayedAutoStart"] is true => "delayed",
                "Auto" => "auto",
                "Disabled" => "disabled",
                "Boot" or "System" => "system",
                _ => "manual",
            };
            list.Add(new ServiceInfo(name, o["DisplayName"]?.ToString() ?? name, o["Description"]?.ToString() ?? "",
                o["State"]?.ToString() == "Running" ? "running" : "stopped", start,
                Essential.Contains(name) || start == "system", originals.ContainsKey(name), byService.GetValueOrDefault(name) ?? []));
        }
        return list.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <param name="start">auto | delayed | manual | disabled</param>
    public static async Task<bool> SetStartAsync(string name, string start)
    {
        if (!ServiceOps.Exists(name)) throw new InvalidOperationException($"Service '{name}' not found");
        if (start == "disabled" && Essential.Contains(name)) throw new InvalidOperationException($"'{name}' is required by Windows and can't be disabled");
        var (s, d) = start switch
        {
            "auto" => (2, 0),
            "delayed" => (2, 1),
            "manual" => (3, 0),
            "disabled" => (4, 0),
            _ => throw new ArgumentException("unknown start type " + start),
        };
        SaveOriginal(name);
        await ServiceOps.SetAsync(name, s, d);
        AppLog.Ok("services", $"{name}: start type -> {start}");
        if (s == 4) await ProcessRunner.RunAsync("sc.exe", $"stop \"{name}\"", TimeSpan.FromSeconds(20));
        return true;
    }

    public static async Task<bool> StartAsync(string name, bool start)
    {
        var r = await ProcessRunner.RunAsync("sc.exe", $"{(start ? "start" : "stop")} \"{name}\"", TimeSpan.FromSeconds(30));
        // 1056 = already running, 1062 = not started: the wanted state already holds
        bool ok = r.Success || r.ExitCode is 1056 or 1062;
        AppLog.Write(ok ? "ok" : "warn", "services", $"{name}: {(start ? "start" : "stop")} -> exit {r.ExitCode}");
        if (!ok) throw new InvalidOperationException(r.Output.Trim().Split('\n').LastOrDefault()?.Trim() ?? $"sc exit {r.ExitCode}");
        return true;
    }

    /// <summary>Puts back the start type the service had before the first manual change.</summary>
    public static async Task<bool> RestoreAsync(string name)
    {
        var originals = LoadOriginals();
        if (!originals.TryGetValue(name, out var o)) return false;
        await ServiceOps.SetAsync(name, o[0], o[1]);
        originals.Remove(name);
        File.WriteAllText(OriginalsFile, JsonSerializer.Serialize(originals));
        AppLog.Ok("services", $"{name}: original start type restored");
        if (o[0] == 2) await ProcessRunner.RunAsync("sc.exe", $"start \"{name}\"", TimeSpan.FromSeconds(20));
        return true;
    }

    private static Dictionary<string, int[]> LoadOriginals()
    {
        try
        {
            return File.Exists(OriginalsFile)
                ? new Dictionary<string, int[]>(JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(OriginalsFile)) ?? [], StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveOriginal(string name)
    {
        var originals = LoadOriginals();
        if (originals.ContainsKey(name)) return; // keep the very first state
        var (start, delayed) = ServiceOps.Read(name);
        originals[name] = [start, delayed];
        File.WriteAllText(OriginalsFile, JsonSerializer.Serialize(originals));
    }
}
