using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform.Actions;

/// <summary>
/// NIC advanced properties are REG_SZ values under the adapter's class key. Only keywords the driver already
/// exposes are changed — unknown keywords are never invented.
/// </summary>
public static class NicOps
{
    private const string NetClass = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    public static string? ClassKeyFor(string interfaceGuid)
    {
        var (root, sub) = RegistryHelper.Split(NetClass);
        using var cls = root.OpenSubKey(sub);
        if (cls is null) return null;
        foreach (var name in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            try
            {
                using var k = cls.OpenSubKey(name);
                if (string.Equals(k?.GetValue("NetCfgInstanceId")?.ToString(), interfaceGuid, StringComparison.OrdinalIgnoreCase))
                    return NetClass + "\\" + name;
            }
            catch (System.Security.SecurityException) { }
        }
        return null;
    }

    private static IEnumerable<AITweaker.Core.Profile.NetAdapterInfo> Targets(NicPropertyAction a, ExecContext ctx) =>
        ctx.Profile.Network.Where(n => a.Target == "all" || n.Kind == a.Target);

    public static Task ApplyAsync(NicPropertyAction a, ExecContext ctx)
    {
        foreach (var nic in Targets(a, ctx))
        {
            var key = ClassKeyFor(nic.Guid);
            if (key is null) continue;
            int changed = 0;
            foreach (var (keyword, value) in a.Keywords)
            {
                var cur = RegistryHelper.Read(key, keyword);
                if (!cur.Exists || cur.Data == value) continue;
                RegistryOps.SetValue(key, keyword, value, RegistryValueKind.String, ctx);
                changed++;
            }
            if (changed > 0)
            {
                ctx.AdaptersToRestart.Add(nic.Guid);
                ctx.Log($"{nic.Description}: {changed} driver properties changed");
            }
        }
        return Task.CompletedTask;
    }

    public static bool? Check(NicPropertyAction a, ExecContext ctx)
    {
        bool any = false;
        foreach (var nic in Targets(a, ctx))
        {
            var key = ClassKeyFor(nic.Guid);
            if (key is null) continue;
            foreach (var (keyword, value) in a.Keywords)
            {
                var cur = RegistryHelper.Read(key, keyword);
                if (!cur.Exists) continue;
                any = true;
                if (cur.Data != value) return false;
            }
        }
        return any ? true : null;
    }

    public static async Task RestartAdaptersAsync(IEnumerable<string> guids)
    {
        foreach (var guid in guids)
        {
            AppLog.Info("network", $"restarting adapter {guid} to apply driver settings");
            await ProcessRunner.PowerShellAsync($"Get-NetAdapter | Where-Object {{ $_.InterfaceGuid -eq {ProcessRunner.PsQuote(guid)} }} | Restart-NetAdapter -Confirm:$false", TimeSpan.FromSeconds(60));
        }
    }
}
