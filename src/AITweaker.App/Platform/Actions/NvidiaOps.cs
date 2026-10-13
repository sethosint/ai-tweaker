using System.Globalization;
using AITweaker.Core.Model;
using AITweaker.Core.Nvidia;

namespace AITweaker.App.Platform.Actions;

/// <summary>Global NVIDIA 3D settings: remembers what was set before and writes the new values.</summary>
public static class NvidiaOps
{
    public static async Task ApplyAsync(NvidiaProfileAction a, ExecContext ctx)
    {
        if (!ctx.Profile.Gpus.Any(g => g.Vendor == "NVIDIA" && g.DriverVersion.Length > 0))
            throw new InvalidOperationException("The NVIDIA driver is not installed");
        var settings = a.Settings.Where(s => ctx.Matches(s.When)).ToList();
        if (settings.Count == 0) return;
        var before = await NvidiaTools.SetBaseProfileAsync(settings.Select(s => new NvidiaProfileFile.Setting(s.SettingId, s.Name, s.Value)).ToList(), ctx.Ct);
        // what to put back on rollback: the customized value, or the driver default when there was none
        ctx.Record(ChangeRecord.Of("nvidia", ("settings", string.Join(";", settings.Select(s =>
            $"{s.Id}={(before.TryGetValue(s.SettingId, out var v) ? v : s.Default).ToString(CultureInfo.InvariantCulture)}={s.Name}")))));
        ctx.Log(string.Join(", ", settings.Select(s => $"{s.Name}: {(before.TryGetValue(s.SettingId, out var v) ? v.ToString(CultureInfo.InvariantCulture) : "default")} -> {s.Value}")));
    }

    public static async Task RevertAsync(ChangeRecord r)
    {
        var settings = (r["settings"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 3))
            .Where(x => x.Length >= 2)
            .Select(x => new NvidiaProfileFile.Setting(Convert.ToUInt32(x[0], 16), x.Length > 2 ? x[2] : "", uint.Parse(x[1], CultureInfo.InvariantCulture)))
            .ToList();
        if (settings.Count > 0) await NvidiaTools.SetBaseProfileAsync(settings);
    }
}
