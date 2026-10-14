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
        // some hidden settings exist only on some PCs (NVMe, hybrid-CPU classes, older builds): skip them instead of failing the tweak
        if (ac is null && dc is null)
        {
            ctx.Log($"power setting {a.Alias ?? a.Setting} doesn't exist on this PC, skipped");
            return Task.CompletedTask;
        }
        ctx.Record(ChangeRecord.Of("power-setting",
            ("scheme", scheme.ToString()), ("subgroup", a.Subgroup), ("setting", a.Setting),
            ("ac", a.Ac.HasValue ? ac?.ToString() : null), ("dc", a.Dc.HasValue ? dc?.ToString() : null)));
        Native.WritePowerSetting(scheme, sub, set, a.Ac, a.Dc);
        return Task.CompletedTask;
    }

    public static async Task RevertSettingAsync(ChangeRecord r)
    {
        uint? ac = uint.TryParse(r["ac"], out var a) ? a : null;
        uint? dc = uint.TryParse(r["dc"], out var d) ? d : null;
        if (ac is null && dc is null) return;
        var scheme = Guid.Parse(r["scheme"]!);
        // the value lived in the AI Tweaker scheme, which its own rollback already deleted: nothing left to restore
        if (!(await ListSchemesAsync()).Contains(scheme)) return;
        Native.WritePowerSetting(scheme, Guid.Parse(r["subgroup"]!), Guid.Parse(r["setting"]!), ac, dc);
    }

    public static bool? CheckSetting(PowerSettingAction a)
    {
        var (ac, dc) = Native.ReadPowerSetting(Native.GetActiveScheme(), Guid.Parse(a.Subgroup), Guid.Parse(a.Setting));
        if (ac is null && dc is null) return null; // not on this PC: decided by the tweak's other settings
        if (a.Ac is { } wantAc && ac != wantAc) return false;
        if (a.Dc is { } wantDc && dc != wantDc) return false;
        return true;
    }

    // ------------------------------------------------------------------ schemes

    internal static async Task<HashSet<Guid>> ListSchemesAsync()
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

    public const string OwnName = "AI Tweaker";

    /// <summary>Our own "AI Tweaker" scheme, copied from <paramref name="baseKind"/> the first time; returns it and whether it was created now.</summary>
    private static async Task<(Guid Scheme, bool Created)?> EnsureOwnAsync(string baseKind, Guid active, ExecContext ctx)
    {
        var schemes = await ListSchemesAsync();
        var s = SettingsStore.Current;
        if (Guid.TryParse(s.OwnSchemeGuid, out var own) && schemes.Contains(own))
        {
            if (s.OwnSchemeBase == baseKind) return (own, false);
            // made for another kind of PC (the profile changed): start over from the right base
            if (active == own) Native.SetActiveScheme(schemes.Contains(Balanced) ? Balanced : schemes.First(g => g != own));
            await ProcessRunner.RunAsync("powercfg.exe", $"-delete {own}");
        }

        // OEM balanced plans keep the maker's tuning (laptops); otherwise Windows' own Balanced, then whatever is active
        var sources = baseKind == "current"
            ? [IsNonBalanced(active) || !schemes.Contains(active) ? Balanced : active, Balanced]
            : new[] { Balanced, active };
        foreach (var src in sources.Distinct())
        {
            var r = await ProcessRunner.RunAsync("powercfg.exe", $"-duplicatescheme {src}");
            var m = GuidRx.Match(r.Output);
            if (!r.Success || !m.Success) continue;
            var guid = Guid.Parse(m.Value);
            await ProcessRunner.RunAsync("powercfg.exe", $"-changename {guid} \"{OwnName}\" \"{Describe(src)}\"");
            s.OwnSchemeGuid = guid.ToString();
            s.OwnSchemeBase = baseKind;
            SettingsStore.Save();
            ctx.Log($"created the {OwnName} power scheme {guid} from {src}");
            return (guid, true);
        }
        ctx.Log("powercfg could not copy any power scheme");
        return null;
    }

    private static string Describe(Guid src) =>
        src == Balanced ? "Lowest latency, tuned by AI Tweaker; Windows power modes and Game Mode keep working"
        : "Based on your PC's balanced plan, tuned by AI Tweaker";

    /// <summary>High, Ultimate or the desktop AI Tweaker plan (tuned to the same level): Game Mode has nothing to raise.</summary>
    public static bool IsPerformancePlan(Guid g) =>
        g == High || g == UltimateTemplate || g.ToString() == SettingsStore.Current.UltimateSchemeGuid
        || (IsOwn(g) && SettingsStore.Current.OwnSchemeBase == "balanced");

    private static bool IsOwn(Guid g) => g.ToString() == SettingsStore.Current.OwnSchemeGuid;

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
        if (a.Scheme == "aitweaker")
        {
            var own = await EnsureOwnAsync(a.Base, prevScheme, ctx)
                ?? throw new InvalidOperationException("Windows didn't let powercfg create the AI Tweaker power plan");
            ctx.Record(ChangeRecord.Of("power-scheme", ("scheme", (IsOwn(prevScheme) ? Balanced : prevScheme).ToString()), ("overlay", prevOverlay?.ToString()),
                ("created", own.Created ? own.Scheme.ToString() : null)));
            Native.SetActiveScheme(own.Scheme);
            ctx.Log($"active power scheme -> {OwnName} {own.Scheme}");
            if (OverlayGuid(a.Overlay) is { } ov && !Native.SetOverlay(ov))
                ctx.Log("power mode overlay could not be set on this system");
            return;
        }
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

    /// <summary>Plans that don't behave like Balanced: High, Ultimate, Power saver and the tuned desktop AI Tweaker plan.</summary>
    private static bool IsNonBalanced(Guid g) => g == PowerSaver || IsPerformancePlan(g);

    public static async Task RevertSchemeAsync(ChangeRecord r)
    {
        if (Guid.TryParse(r["scheme"], out var scheme))
        {
            var schemes = await ListSchemesAsync();
            Native.SetActiveScheme(schemes.Contains(scheme) ? scheme : Balanced);
        }
        if (Guid.TryParse(r["overlay"], out var overlay)) Native.SetOverlay(overlay);
        // the AI Tweaker scheme this change created goes away with it
        if (Guid.TryParse(r["created"], out var created) && Native.GetActiveScheme() != created)
        {
            await ProcessRunner.RunAsync("powercfg.exe", $"-delete {created}");
            if (IsOwn(created)) { SettingsStore.Current.OwnSchemeGuid = null; SettingsStore.Current.OwnSchemeBase = null; SettingsStore.Save(); }
        }
    }

    public static bool? CheckScheme(PowerSchemeAction a)
    {
        var active = Native.GetActiveScheme();
        bool schemeOk = a.Scheme switch
        {
            "aitweaker" => IsOwn(active) && SettingsStore.Current.OwnSchemeBase == a.Base,
            "ultimate" => active == UltimateTemplate || active.ToString() == SettingsStore.Current.UltimateSchemeGuid,
            "high" => active == High,
            _ => !IsNonBalanced(active),
        };
        if (!schemeOk) return false;
        if (OverlayGuid(a.Overlay) is { } want) return Native.GetOverlay() is not { } cur || cur == want;
        return true;
    }
}
