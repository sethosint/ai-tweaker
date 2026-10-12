using System.Text.RegularExpressions;
using AITweaker.Core.Model;

namespace AITweaker.App.Platform.Actions;

public static class PowerOps
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimateTemplate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    public static readonly Guid OverlayBalanced = Guid.Empty;
    public static readonly Guid OverlayEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    private static readonly Regex GuidRx = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    // ------------------------------------------------------------------ settings

    public static Task ApplySettingAsync(PowerSettingAction a, ExecContext ctx)
    {
        var scheme = Native.GetActiveScheme();
        var sub = Guid.Parse(a.Subgroup);
        var set = Guid.Parse(a.Setting);
        var (ac, dc) = Native.ReadPowerSetting(scheme, sub, set);
        ctx.Record(ChangeRecord.Of("power-setting",
            ("scheme", scheme.ToString()), ("subgroup", a.Subgroup), ("setting", a.Setting),
            ("ac", a.Ac.HasValue ? ac?.ToString() : null), ("dc", a.Dc.HasValue ? dc?.ToString() : null)));
        Native.WritePowerSetting(scheme, sub, set, a.Ac, a.Dc);
        return Task.CompletedTask;
    }

    public static Task RevertSettingAsync(ChangeRecord r)
    {
        uint? ac = uint.TryParse(r["ac"], out var a) ? a : null;
        uint? dc = uint.TryParse(r["dc"], out var d) ? d : null;
        if (ac is null && dc is null) return Task.CompletedTask;
        Native.WritePowerSetting(Guid.Parse(r["scheme"]!), Guid.Parse(r["subgroup"]!), Guid.Parse(r["setting"]!), ac, dc);
        return Task.CompletedTask;
    }

    public static bool? CheckSetting(PowerSettingAction a)
    {
        var (ac, dc) = Native.ReadPowerSetting(Native.GetActiveScheme(), Guid.Parse(a.Subgroup), Guid.Parse(a.Setting));
        if (a.Ac is { } wantAc && ac != wantAc) return false;
        if (a.Dc is { } wantDc && dc != wantDc) return false;
        return true;
    }

    // ------------------------------------------------------------------ schemes

    private static async Task<HashSet<Guid>> ListSchemesAsync()
    {
        var r = await ProcessRunner.RunAsync("powercfg.exe", "/list");
        return GuidRx.Matches(r.Output).Select(m => Guid.Parse(m.Value)).ToHashSet();
    }

    /// <summary>Returns our Ultimate Performance copy, creating it from the hidden template if needed.</summary>
    private static async Task<Guid?> EnsureUltimateAsync(ExecContext ctx)
    {
        var schemes = await ListSchemesAsync();
        if (schemes.Contains(UltimateTemplate)) return UltimateTemplate;
        if (Guid.TryParse(SettingsStore.Current.UltimateSchemeGuid, out var saved) && schemes.Contains(saved)) return saved;

        var r = await ProcessRunner.RunAsync("powercfg.exe", $"-duplicatescheme {UltimateTemplate}");
        var m = GuidRx.Match(r.Output);
        if (!r.Success || !m.Success)
        {
            ctx.Log("Ultimate Performance is not available on this system (Modern Standby?), falling back to High performance");
            return schemes.Contains(High) ? High : null;
        }
        var guid = Guid.Parse(m.Value);
        SettingsStore.Current.UltimateSchemeGuid = guid.ToString();
        SettingsStore.Save();
        return guid;
    }

    private static Guid? OverlayGuid(string? name) => name switch
    {
        "best-performance" => OverlayBestPerformance,
        "balanced" => OverlayBalanced,
        "efficiency" => OverlayEfficiency,
        _ => null,
    };

    public static async Task ApplySchemeAsync(PowerSchemeAction a, ExecContext ctx)
    {
        var prevScheme = Native.GetActiveScheme();
        var prevOverlay = Native.GetOverlay();
        ctx.Record(ChangeRecord.Of("power-scheme", ("scheme", prevScheme.ToString()), ("overlay", prevOverlay?.ToString())));

        Guid? target = a.Scheme switch
        {
            "ultimate" => await EnsureUltimateAsync(ctx),
            "high" => High,
            // Keep OEM-tuned balanced plans on laptops; only leave High/Ultimate/Power saver (they break X3D core parking).
            _ => IsNonBalanced(prevScheme) ? Balanced : null,
        };
        if (target is { } t)
        {
            Native.SetActiveScheme(t);
            ctx.Log($"active power scheme -> {t}");
        }
        if (OverlayGuid(a.Overlay) is { } overlay && !Native.SetOverlay(overlay))
            ctx.Log("power mode overlay could not be set on this system");
    }

    private static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    private static bool IsNonBalanced(Guid g) =>
        g == High || g == UltimateTemplate || g == PowerSaver || g.ToString() == SettingsStore.Current.UltimateSchemeGuid;

    public static Task RevertSchemeAsync(ChangeRecord r)
    {
        if (Guid.TryParse(r["scheme"], out var scheme)) Native.SetActiveScheme(scheme);
        if (Guid.TryParse(r["overlay"], out var overlay)) Native.SetOverlay(overlay);
        return Task.CompletedTask;
    }

    public static bool? CheckScheme(PowerSchemeAction a)
    {
        var active = Native.GetActiveScheme();
        bool schemeOk = a.Scheme switch
        {
            "ultimate" => active == UltimateTemplate || active.ToString() == SettingsStore.Current.UltimateSchemeGuid,
            "high" => active == High,
            _ => !IsNonBalanced(active),
        };
        if (!schemeOk) return false;
        if (OverlayGuid(a.Overlay) is { } want) return Native.GetOverlay() is not { } cur || cur == want;
        return true;
    }
}
