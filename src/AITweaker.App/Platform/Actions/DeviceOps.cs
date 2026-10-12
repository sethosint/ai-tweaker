using AITweaker.Core.Model;

namespace AITweaker.App.Platform.Actions;

/// <summary>Enables/disables PnP devices with pnputil (Windows 10 2004+).</summary>
public static class DeviceOps
{
    private const int Disabled = 22;

    private static List<(string Id, string Name, int Error)> Find(IEnumerable<string> prefixes)
    {
        var list = new List<(string, string, int)>();
        var pre = prefixes.ToList();
        foreach (var o in SystemProfiler.Query(@"root\cimv2", "SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity"))
        {
            var id = o["PNPDeviceID"]?.ToString() ?? "";
            if (pre.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                list.Add((id, o["Name"]?.ToString() ?? id, Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0)));
        }
        return list;
    }

    public static async Task ApplyAsync(DeviceAction a, ExecContext ctx)
    {
        foreach (var (id, name, error) in Find(a.Match))
        {
            bool isDisabled = error == Disabled;
            if (isDisabled == !a.Enabled) continue;
            var verb = a.Enabled ? "/enable-device" : "/disable-device";
            var r = await ProcessRunner.RunAsync("pnputil.exe", $"{verb} \"{id}\"", TimeSpan.FromSeconds(60));
            if (!r.Success && r.ExitCode != 3010) // 3010 = reboot required
            {
                ctx.Log($"pnputil {verb} {name}: {r.Output.Trim()}");
                continue;
            }
            ctx.Record(ChangeRecord.Of("device", ("id", id), ("enabled", isDisabled ? "0" : "1")));
            ctx.Log($"{(a.Enabled ? "enabled" : "disabled")} {name}");
        }
    }

    public static async Task RevertAsync(ChangeRecord r)
    {
        var verb = r["enabled"] == "1" ? "/enable-device" : "/disable-device";
        await ProcessRunner.RunAsync("pnputil.exe", $"{verb} \"{r["id"]}\"", TimeSpan.FromSeconds(60));
    }

    public static bool? Check(DeviceAction a)
    {
        var devices = Find(a.Match);
        if (devices.Count == 0) return null;
        return devices.All(d => (d.Error == Disabled) == !a.Enabled);
    }
}
