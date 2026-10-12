using AITweaker.Core.Affinity;
using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform.Actions;

/// <summary>MSI mode and interrupt affinity live under each device's Enum key → Device Parameters → Interrupt Management.</summary>
public static class InterruptOps
{
    private static string Enum(string pnpId) => $@"HKLM\SYSTEM\CurrentControlSet\Enum\{pnpId}\Device Parameters\Interrupt Management";

    public static List<string> DevicesFor(string target, ExecContext ctx)
    {
        var p = ctx.Profile;
        IEnumerable<string> ids = target switch
        {
            "gpu" => p.Gpus.Where(g => g.IsDiscrete).Select(g => g.PnpId),
            "nic" => p.Network.Where(n => n.Kind == "ethernet" && n.Connected).Select(n => n.PnpId)
                .DefaultIfEmpty(p.Network.FirstOrDefault(n => n.Connected)?.PnpId ?? ""),
            "usb" => SystemProfiler.Query(@"root\cimv2", "SELECT PNPDeviceID FROM Win32_USBController").Select(o => o["PNPDeviceID"]?.ToString() ?? ""),
            _ => [],
        };
        return ids.Where(id => id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) && RegistryHelper.KeyExists($@"HKLM\SYSTEM\CurrentControlSet\Enum\{id}\Device Parameters"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Task ApplyMsiAsync(MsiModeAction a, ExecContext ctx)
    {
        foreach (var id in DevicesFor(a.Target, ctx))
        {
            RegistryOps.SetValue(Enum(id) + @"\MessageSignaledInterruptProperties", "MSISupported", 1, RegistryValueKind.DWord, ctx);
            ctx.Log($"MSI enabled for {id}");
        }
        return Task.CompletedTask;
    }

    public static bool? CheckMsi(MsiModeAction a, ExecContext ctx)
    {
        var devices = DevicesFor(a.Target, ctx);
        if (devices.Count == 0) return null;
        return devices.All(id => RegistryHelper.Read(Enum(id) + @"\MessageSignaledInterruptProperties", "MSISupported").Data == "1");
    }

    public static Task ApplyAffinityAsync(InterruptAffinityAction a, ExecContext ctx)
    {
        var plan = InterruptPlanner.Plan(ctx.Profile.Cpu.Topology, a.Targets);
        if (plan.Count == 0)
        {
            ctx.Log("interrupt affinity skipped: not enough cores");
            return Task.CompletedTask;
        }
        foreach (var assignment in plan)
        {
            foreach (var id in DevicesFor(assignment.Target, ctx))
            {
                var key = Enum(id) + @"\Affinity Policy";
                RegistryOps.SetValue(key, "DevicePolicy", 4, RegistryValueKind.DWord, ctx); // IrqPolicySpecifiedProcessors
                RegistryOps.SetValue(key, "AssignmentSetOverride", assignment.MaskBytes, RegistryValueKind.Binary, ctx);
                ctx.Log($"{assignment.Target} {id} -> CPU {assignment.LogicalProcessor} (core {assignment.CoreIndex})");
            }
        }
        return Task.CompletedTask;
    }

    public static bool? CheckAffinity(InterruptAffinityAction a, ExecContext ctx)
    {
        var plan = InterruptPlanner.Plan(ctx.Profile.Cpu.Topology, a.Targets);
        if (plan.Count == 0) return null;
        foreach (var assignment in plan)
        foreach (var id in DevicesFor(assignment.Target, ctx))
        {
            var cur = RegistryHelper.Read(Enum(id) + @"\Affinity Policy", "AssignmentSetOverride");
            if (cur.Data != Convert.ToBase64String(assignment.MaskBytes)) return false;
        }
        return true;
    }
}
