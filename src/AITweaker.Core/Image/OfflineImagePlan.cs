using System.Globalization;
using System.Text.Json;
using AITweaker.Core.Catalog;
using AITweaker.Core.Conditions;
using AITweaker.Core.Model;

namespace AITweaker.Core.Image;

/// <summary>Registry hives of the mounted install.wim, loaded under HKLM by the builder.</summary>
public enum OfflineHive
{
    /// <summary>Windows\System32\config\SOFTWARE</summary>
    Software,
    /// <summary>Windows\System32\config\SYSTEM (ControlSet001)</summary>
    System,
    /// <summary>Users\Default\NTUSER.DAT — becomes HKCU of every new account.</summary>
    DefaultUser,
}

/// <param name="Data">Value as text: DWORD as signed 32-bit decimal (registry bit pattern), strings verbatim.</param>
public sealed record OfflineRegistryOp(OfflineHive Hive, string SubKey, string Name, RegistryValueKind2 Kind, string? Data, bool Delete, string TweakId);

public sealed record OfflineServiceOp(string Name, int Start, bool Delayed, string TweakId);

public sealed record SkippedTweak(string Id, string Reason);

public sealed class OfflineImageOptions
{
    /// <summary>Bake tweaks up to this profile level (1 = Safe, 2 = Balanced).</summary>
    public int MaxLevel { get; set; } = 2;
    /// <summary>Explicit tweak selection; overrides <see cref="MaxLevel"/> and allows confirm-tweaks the user accepted.</summary>
    public List<string>? TweakIds { get; set; }
    /// <summary>Important packages the user explicitly chose to remove (Store, Xbox, Phone Link...).</summary>
    public List<string> ExtraApps { get; set; } = [];
    public bool RemoveApps { get; set; } = true;
    public bool RemoveLegacyComponents { get; set; } = true;
    public bool InjectDrivers { get; set; }
    public bool CleanupComponents { get; set; } = true;
    /// <summary>Download the latest cumulative update for the image's Windows version and integrate it.</summary>
    public bool IntegrateUpdates { get; set; } = true;
    /// <summary>Bake the AI Tweaker look (category "look", level 1+) into the default user profile, regardless of <see cref="MaxLevel"/>.</summary>
    public bool ApplyLook { get; set; } = true;
}

/// <summary>
/// AI Tweaker's own custom Windows image: which catalog tweaks, app removals and component removals get baked
/// into install.wim offline. Hardware-specific tweaks are left for AI Tweaker to apply after installation,
/// once the real machine is known.
/// </summary>
public sealed class OfflineImagePlan
{
    /// <summary>Optional components (Remove-WindowsCapability name prefixes) nobody needs on a gaming PC.</summary>
    public static readonly string[] LegacyCapabilities =
    [
        "Browser.InternetExplorer~",
        "MathRecognizer~",
        "Microsoft.Windows.WordPad~",
        "App.StepsRecorder~",
        "App.Support.QuickAssist~",
        "Media.WindowsMediaPlayer~",
    ];

    /// <summary>Facts known for any Windows 11 24H2+ image, regardless of the PC it will be installed on.</summary>
    private static readonly Dictionary<string, object?> ImageFacts = new() { ["os.build"] = 26100, ["os.win11"] = true };

    public List<string> TweakIds { get; } = [];
    public List<OfflineRegistryOp> Registry { get; } = [];
    public List<OfflineServiceOp> Services { get; } = [];
    public List<string> Apps { get; } = [];
    public List<string> Capabilities { get; } = [];
    public List<SkippedTweak> Skipped { get; } = [];
    /// <summary>The image needs AI Tweaker's wallpapers and aitweaker.theme copied into ProgramData\AITweaker\Look.</summary>
    public bool IncludesLook { get; private set; }
    public OfflineImageOptions Options { get; private init; } = new();

    public static OfflineImagePlan Build(TweakCatalog catalog, OfflineImageOptions options)
    {
        var plan = new OfflineImagePlan { Options = options };
        var explicitIds = options.TweakIds?.ToHashSet(StringComparer.Ordinal);

        foreach (var t in catalog.Tweaks)
        {
            bool wanted = explicitIds is not null ? explicitIds.Contains(t.Id)
                : t.Category == "look" ? options.ApplyLook && t.Level >= 1 // level 0 = optional alternatives (e.g. wallpaper art)
                : t.Level >= 1 && t.Level <= options.MaxLevel && !t.Confirm;
            if (!wanted) continue;

            if (Reject(t) is { } reason)
            {
                plan.Skipped.Add(new SkippedTweak(t.Id, reason));
                continue;
            }

            var regs = new List<OfflineRegistryOp>();
            var svcs = new List<OfflineServiceOp>();
            foreach (var a in t.Actions.Where(a => ConditionEvaluator.Evaluate(a.When, ImageFacts)))
            {
                switch (a)
                {
                    case RegistryAction r:
                        var (hive, sub) = Map(r.Path)!.Value;
                        regs.Add(new OfflineRegistryOp(hive, sub, r.Name, r.Kind, r.Delete ? null : Text(r), r.Delete, t.Id));
                        break;
                    case WallpaperAction w:
                        regs.AddRange(Wallpaper(w, t.Id));
                        plan.IncludesLook = true;
                        break;
                    case ServiceAction s:
                        svcs.Add(new OfflineServiceOp(s.Name, s.Startup switch
                        {
                            ServiceStartup.Disabled => 4,
                            ServiceStartup.Manual => 3,
                            _ => 2,
                        }, s.Startup == ServiceStartup.Delayed, t.Id));
                        break;
                }
            }
            plan.TweakIds.Add(t.Id);
            plan.Registry.AddRange(regs);
            plan.Services.AddRange(svcs);
        }

        if (options.RemoveApps)
        {
            var extra = options.ExtraApps.ToHashSet(StringComparer.OrdinalIgnoreCase);
            plan.Apps.AddRange(catalog.Apps
                .Where(a => (a.Recommended && !a.Important && a.KeepWhen is null) || extra.Contains(a.Package))
                .Select(a => a.Package));
        }
        if (options.RemoveLegacyComponents) plan.Capabilities.AddRange(LegacyCapabilities);
        if (plan.IncludesLook)
            // New accounts start with the AI Tweaker theme (wallpaper, dark mode, accent) instead of the stock one.
            plan.Registry.Add(new OfflineRegistryOp(OfflineHive.Software, @"Microsoft\Windows\CurrentVersion\Themes", "InstallTheme",
                RegistryValueKind2.Expand, LookAssets.ThemePathExpand, false, "look"));
        return plan;
    }

    /// <summary>Why a tweak can't be baked offline, or null if it can.</summary>
    private static string? Reject(TweakDefinition t)
    {
        if (!Offline(t.When)) return "hardware";
        foreach (var a in t.Actions)
        {
            if (!Offline(a.When)) return "hardware";
            switch (a)
            {
                case RegistryAction r when r.Path.Contains('*'):
                    return "per-device";
                case RegistryAction r when Map(r.Path) is null:
                    return "registry-hive";
                case RegistryAction:
                case ServiceAction:
                case WallpaperAction:
                    continue;
                default:
                    return "live-only"; // power plans, bcd, devices, NIC, commands… need the running system
            }
        }
        return null;
    }

    /// <summary>A condition is decidable offline when it only uses facts true for every Windows 11 image.</summary>
    private static bool Offline(string? when) =>
        string.IsNullOrWhiteSpace(when) || ConditionEvaluator.Identifiers(when).All(ImageFacts.ContainsKey) && ConditionEvaluator.Evaluate(when, ImageFacts);

    /// <summary>Maps a live registry path onto the loaded offline hives (null when the hive can't be serviced offline).</summary>
    public static (OfflineHive Hive, string SubKey)? Map(string path)
    {
        const string sw = @"HKLM\SOFTWARE\", sys = @"HKLM\SYSTEM\CurrentControlSet\", cu = @"HKCU\";
        if (path.StartsWith(sw, StringComparison.OrdinalIgnoreCase)) return (OfflineHive.Software, path[sw.Length..]);
        if (path.StartsWith(sys, StringComparison.OrdinalIgnoreCase)) return (OfflineHive.System, @"ControlSet001\" + path[sys.Length..]);
        if (path.StartsWith(cu, StringComparison.OrdinalIgnoreCase)) return (OfflineHive.DefaultUser, path[cu.Length..]);
        return null;
    }

    /// <summary>The wallpaper file is copied into the image; new accounts and the lock screen point at it.</summary>
    private static IEnumerable<OfflineRegistryOp> Wallpaper(WallpaperAction w, string id)
    {
        if (w.Target == "lock")
        {
            const string csp = @"Microsoft\Windows\CurrentVersion\PersonalizationCSP";
            yield return new(OfflineHive.Software, csp, "LockScreenImagePath", RegistryValueKind2.String, w.File, false, id);
            yield return new(OfflineHive.Software, csp, "LockScreenImageUrl", RegistryValueKind2.String, w.File, false, id);
            yield return new(OfflineHive.Software, csp, "LockScreenImageStatus", RegistryValueKind2.Dword, "1", false, id);
            yield break;
        }
        yield return new(OfflineHive.DefaultUser, @"Control Panel\Desktop", "WallPaper", RegistryValueKind2.String, w.File, false, id);
        yield return new(OfflineHive.DefaultUser, @"Control Panel\Desktop", "WallpaperStyle", RegistryValueKind2.String, "10", false, id);
        yield return new(OfflineHive.DefaultUser, @"Control Panel\Desktop", "TileWallpaper", RegistryValueKind2.String, "0", false, id);
        // 1 = solid colour, 0 = picture (and no Windows Spotlight on the desktop)
        yield return new(OfflineHive.DefaultUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers", "BackgroundType", RegistryValueKind2.Dword, w.SolidBlack ? "1" : "0", false, id);
        if (w.SolidBlack)
            yield return new(OfflineHive.DefaultUser, @"Control Panel\Colors", "Background", RegistryValueKind2.String, LookAssets.BlackRgb, false, id);
    }

    /// <summary>DWORD as signed decimal, binary as base64, multi-string as a JSON array, the rest verbatim.</summary>
    private static string Text(RegistryAction r)
    {
        var v = r.Value!.Value;
        return r.Kind switch
        {
            RegistryValueKind2.Dword => unchecked((int)(uint)v.GetInt64()).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind2.Qword => v.GetInt64().ToString(CultureInfo.InvariantCulture),
            RegistryValueKind2.Binary => v.ValueKind == JsonValueKind.Array ? Convert.ToBase64String(v.EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray()) : v.GetString() ?? "",
            RegistryValueKind2.Multi => v.ValueKind == JsonValueKind.Array ? v.GetRawText() : JsonSerializer.Serialize(new[] { v.GetString() ?? "" }),
            _ => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString(),
        };
    }
}
