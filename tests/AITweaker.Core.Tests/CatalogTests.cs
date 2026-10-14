using System.Text.Json;
using System.Text.RegularExpressions;
using AITweaker.Core.Catalog;
using AITweaker.Core.Conditions;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.Core.Tests;

public class CatalogTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadEmbedded();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AITweaker.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private static IEnumerable<string> Conditions()
    {
        foreach (var t in Catalog.Tweaks)
        {
            if (t.When is not null) yield return t.When;
            foreach (var a in t.Actions) if (a.When is not null) yield return a.When;
        }
        foreach (var g in Catalog.Guides) { if (g.When is not null) yield return g.When; if (g.Detect is not null) yield return g.Detect; }
        foreach (var b in Catalog.Bios) if (b.When is not null) yield return b.When;
        foreach (var a in Catalog.Apps) if (a.KeepWhen is not null) yield return a.KeepWhen;
    }

    [Fact]
    public void LoadsEverything()
    {
        Assert.True(Catalog.Tweaks.Count >= 60, $"only {Catalog.Tweaks.Count} tweaks");
        Assert.True(Catalog.Apps.Count >= 30);
        Assert.Equal(7, Catalog.Guides.Count); // the programs the user configures: GPU apps, Discord, Steam, Epic, OBS, Spotify
        Assert.True(Catalog.Bios.Count >= 6);
        Assert.Equal(11, Catalog.Categories.Count);
    }

    [Fact]
    public void ConditionsParseAndUseKnownFacts()
    {
        foreach (var expr in Conditions())
        {
            var ids = ConditionEvaluator.Identifiers(expr);
            foreach (var id in ids)
                Assert.True(SystemProfile.KnownFacts.Contains(id) || id.StartsWith("app.", StringComparison.Ordinal), $"Unknown fact '{id}' in '{expr}'");
        }
    }

    [Fact]
    public void TweaksAreWellFormed()
    {
        var categories = Catalog.Categories.Select(c => c.Id).ToHashSet();
        foreach (var t in Catalog.Tweaks)
        {
            Assert.Matches(@"^[a-z0-9]+(\.[a-z0-9-]+)+$", t.Id);
            Assert.Contains(t.Category, categories);
            Assert.InRange(t.Level, 0, 4);
            Assert.False(string.IsNullOrWhiteSpace(t.Name.Ru), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Name.En), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description.Ru), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description.En), t.Id);
            Assert.NotEmpty(t.Actions);
            if (t.Confirm) Assert.True(t.ConfirmText is { Ru.Length: > 0, En.Length: > 0 }, $"{t.Id} needs confirmText");
            if (t.Risk == RiskLevel.High) Assert.True(t.Confirm, $"{t.Id}: high-risk tweaks must ask first");
        }
    }

    [Fact]
    public void RegistryActionsAreValid()
    {
        var hive = new Regex(@"^(HKLM|HKCU|HKU|HKCR)\\");
        foreach (var t in Catalog.Tweaks)
        foreach (var r in t.Actions.OfType<RegistryAction>())
        {
            Assert.Matches(hive, r.Path);
            Assert.DoesNotContain("/", r.Path);
            Assert.False(r.Path.EndsWith('\\'), r.Path);
            if (r.Delete) { Assert.Null(r.Value); continue; }
            Assert.True(r.Value.HasValue, $"{t.Id}: {r.Path}\\{r.Name} has no value");
            var v = r.Value!.Value;
            switch (r.Kind)
            {
                case RegistryValueKind2.Dword:
                    Assert.Equal(JsonValueKind.Number, v.ValueKind);
                    Assert.InRange(v.GetInt64(), 0, uint.MaxValue);
                    break;
                case RegistryValueKind2.Qword:
                    Assert.Equal(JsonValueKind.Number, v.ValueKind);
                    break;
                case RegistryValueKind2.String or RegistryValueKind2.Expand:
                    Assert.Equal(JsonValueKind.String, v.ValueKind);
                    break;
            }
            if (r.Path.Contains('*')) Assert.True(r.OnlyIfKeyExists, $"{t.Id}: wildcard paths must be onlyIfKeyExists");
        }
    }

    [Fact]
    public void OtherActionsAreValid()
    {
        foreach (var t in Catalog.Tweaks)
        foreach (var a in t.Actions)
        {
            switch (a)
            {
                case PowerSettingAction p:
                    Assert.True(Guid.TryParse(p.Subgroup, out _), p.Subgroup);
                    Assert.True(Guid.TryParse(p.Setting, out _), p.Setting);
                    Assert.True(p.Ac.HasValue || p.Dc.HasValue, t.Id);
                    break;
                case PowerSchemeAction s:
                    Assert.Contains(s.Scheme, new[] { "aitweaker", "ultimate", "high", "balanced" });
                    Assert.Contains(s.Base, new[] { "ultimate", "current" });
                    if (s.Overlay is not null) Assert.Contains(s.Overlay, new[] { "best-performance", "balanced", "efficiency" });
                    break;
                case ServiceAction s:
                    Assert.Matches(@"^[A-Za-z0-9_.]+$", s.Name);
                    break;
                case ScheduledTaskAction s:
                    Assert.StartsWith("\\", s.Path);
                    break;
                case CommandAction c:
                    Assert.EndsWith(".exe", c.File);
                    Assert.True(!t.Reversible || c.UndoFile is not null, $"{t.Id}: reversible command needs undo");
                    break;
                case PowerShellAction ps:
                    Assert.True(!t.Reversible || ps.Undo is not null, $"{t.Id}: reversible script needs undo");
                    break;
                case NicPropertyAction n:
                    Assert.Contains(n.Target, new[] { "ethernet", "wifi", "all" });
                    Assert.NotEmpty(n.Keywords);
                    break;
                case MsiModeAction m:
                    Assert.Contains(m.Target, new[] { "gpu", "nic" });
                    break;
                case InterruptAffinityAction ia:
                    Assert.All(ia.Targets, x => Assert.Contains(x, new[] { "gpu", "nic", "usb" }));
                    break;
                case DeviceAction d:
                    Assert.All(d.Match, m => Assert.EndsWith("\\", m));
                    break;
                case FileSettingAction f:
                    // per-user program settings: always under the user's profile, never a system path
                    Assert.All(f.Path.Split('|'), p => Assert.Matches(@"^%(APPDATA|LOCALAPPDATA)%\\", p));
                    Assert.Contains(f.Format, new[] { "json", "prefs", "ini" });
                    if (f.Format == "ini") Assert.All(f.Set.Keys, k => Assert.Matches(@"^[A-Za-z]+/[A-Za-z0-9]+$", k));
                    Assert.Contains(f.IfRunning, new[] { "restart", "fail" });
                    Assert.NotEmpty(f.Set);
                    Assert.True(t.When is not null && t.When.StartsWith("app.", StringComparison.Ordinal), $"{t.Id}: a program's settings file only when the program is installed");
                    break;
            }
        }
    }

    [Fact]
    public void ObsSettingsUseRealObsKeys()
    {
        // ids and values checked against obs-studio's obs-nvenc plugin and settings dialog
        var t = Catalog.Find("apps.obs.settings")!;
        Assert.True(t.Confirm);
        var files = t.Actions.Cast<FileSettingAction>().ToList();
        // OBS may be live: never closed behind the user's back
        Assert.All(files, f => Assert.Equal(("obs64", "fail"), (f.Process, f.IfRunning)));
        var basic = files.First(f => f.Path.EndsWith("basic.ini") && f.When is null).Set;
        Assert.Equal("obs_nvenc_h264_tex", basic["AdvOut/Encoder"].GetString());
        Assert.Equal("hybrid_mp4", basic["AdvOut/RecFormat2"].GetString());
        Assert.Equal("IPv4", basic["Output/IPFamily"].GetString());
        Assert.Equal(512, basic["AdvOut/RecRBSize"].GetInt32());
        Assert.Contains(files, f => f.When == "gpu.av1" && f.Set["AdvOut/RecEncoder"].GetString() == "obs_nvenc_av1_tex");
        Assert.Contains(files, f => f.When == "!gpu.av1" && f.Set["AdvOut/RecEncoder"].GetString() == "obs_nvenc_hevc_tex");
        foreach (var enc in files.Where(f => f.Path.EndsWith("Encoder.json")))
        {
            Assert.True(enc.Create);
            Assert.Equal("p6", enc.Set["preset"].GetString());     // the new NVENC plugin's key is "preset", not "preset2"
            Assert.Contains(enc.Set["rate_control"].GetString(), new[] { "CBR", "CQP" });
            Assert.True(enc.Set["adaptive_quantization"].GetBoolean());
        }
    }

    [Theory]
    [InlineData("NVIDIA", "NVIDIA GeForce RTX 4070 SUPER", true)]
    [InlineData("NVIDIA", "NVIDIA GeForce RTX 5070 Ti", true)]
    [InlineData("NVIDIA", "NVIDIA GeForce RTX 3080", false)]
    [InlineData("NVIDIA", "NVIDIA GeForce GTX 1660 SUPER", false)]
    [InlineData("AMD", "AMD Radeon RX 7800 XT", true)]
    [InlineData("AMD", "AMD Radeon RX 6700 XT", false)]
    [InlineData("Intel", "Intel(R) Arc(TM) B580 Graphics", true)]
    [InlineData("Intel", "Intel(R) UHD Graphics 770", false)]
    public void KnowsWhichGpusEncodeAv1(string vendor, string name, bool av1) =>
        Assert.Equal(av1, new GpuInfo { Vendor = vendor, Name = name }.EncodesAv1);

    [Fact]
    public void AppsAreUniqueAndImportantOnesWarn()
    {
        Assert.Equal(Catalog.Apps.Count, Catalog.Apps.Select(a => a.Package).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var a in Catalog.Apps)
        {
            Assert.False(a.Important && a.Recommended, $"{a.Package}: important apps must never be pre-selected");
            if (a.Important) Assert.NotNull(a.Warning);
        }
        Assert.Contains(Catalog.Apps, a => a.Package == "Microsoft.WindowsStore" && a.Important);
    }

    [Fact]
    public void GuidesAreCompleteAndReferenceExistingTweaks()
    {
        Assert.Equal(Catalog.Guides.Count, Catalog.Guides.Select(g => g.Id).Distinct().Count());
        foreach (var g in Catalog.Guides)
        {
            Assert.NotNull(g.Summary);
            Assert.NotEmpty(g.Sections);
            foreach (var s in g.Sections)
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Path), $"{g.Id}: section without a path");
                Assert.NotEmpty(s.Settings);
                // every section shows the real settings screen next to the text
                Assert.True(s.Images.Count > 0, $"{g.Id}: {s.Title.En} has no screenshot");
                foreach (var image in s.Images)
                    Assert.True(File.Exists(Path.Combine(RepoRoot(), "src", "AITweaker.App", "wwwroot", "guides", image)), $"missing guide screenshot {image}");
            }
            foreach (var id in g.AutoTweaks)
                Assert.NotNull(Catalog.Find(id));
        }
        // and no screenshot ships that no guide uses
        var used = Catalog.Guides.SelectMany(g => g.Sections).SelectMany(s => s.Images).ToHashSet();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "src", "AITweaker.App", "wwwroot", "guides")))
            Assert.Contains(Path.GetFileName(file), used);
    }

    [Fact]
    public void BiosItemsHaveFallbackPath()
    {
        // laptop-only items fall back to the generic laptop path instead (see BiosVendorTests)
        foreach (var b in Catalog.Bios) Assert.True(b.Paths.ContainsKey("other") || b.Paths.ContainsKey("laptop"), b.Id);
    }

    [Fact]
    public void ProgramsAreInstallable()
    {
        var groups = new[] { "browsers", "communication", "gaming", "media", "tools", "runtimes", "look", "store" };
        Assert.Equal(Catalog.Programs.Count, Catalog.Programs.Select(p => p.Id).Distinct().Count());
        foreach (var p in Catalog.Programs)
        {
            Assert.Contains(p.Group, groups);
            Assert.True(p.Winget is not null || p.Download is not null || p.Url is not null || p.Store is not null, $"{p.Id}: no way to install");
            if (p.Page is not null)
            {
                Assert.StartsWith("https://", p.Page);
                Assert.NotNull(p.Asset);
                _ = new System.Text.RegularExpressions.Regex(p.Asset!);
            }
            // Store-only apps sit in their own group, are found by package name and have nothing else to install from
            Assert.Equal(p.Store is not null, p.Group == "store");
            if (p.Store is not null)
            {
                Assert.Matches("^[0-9A-Z]{12}$", p.Store);
                Assert.Matches(@"^[\w.]+_$", p.Detect ?? "");
                Assert.True(p.Installer is null && p.Github is null && p.Page is null && p.Download is null, p.Id);
            }
            if (p.Winget is not null) Assert.Matches(@"^[A-Za-z0-9][\w+-]*(\.[\w+-]+)+$", p.Winget);
            if (p.Download is not null) Assert.StartsWith("https://", p.Download);
            if (p.Url is not null) Assert.StartsWith("https://", p.Url);
            if (p.Download is not null) Assert.NotNull(p.Url); // fallback when the direct link moves
            if (p.Installer is not null) Assert.StartsWith("https://", p.Installer);
            if (p.Github is not null)
            {
                Assert.Matches(@"^[\w.-]+/[\w.-]+$", p.Github);
                Assert.NotNull(p.Asset);
                _ = new System.Text.RegularExpressions.Regex(p.Asset!); // compiles
            }
            // installers that refuse to run elevated must be run as the signed-in user, and that path needs silent switches
            if (p.User && (p.Installer ?? p.Github) is not null && !(p.Installer ?? "").Contains(".msi")) Assert.NotNull(p.Silent);
            Assert.False(p.Paid && p.Recommended, $"{p.Id}: paid software is never preselected");
        }
        // the main programs come from their vendors' own installers, not winget
        foreach (var id in new[] { "chrome", "discord", "telegram", "steam", "epic", "spotify", "obs", "lightshot", "7zip", "vcredist", "directx", "dotnet8", "openshell", "mpv" })
            Assert.True(Catalog.Programs.Single(p => p.Id == id) is { } x && (x.Installer ?? x.Github ?? x.Page) is not null, id);
        // the apps from the user's own setup are there
        foreach (var id in new[] { "chrome", "discord", "ayugram", "spotify", "obs", "epic", "steam", "openshell", "cru" })
            Assert.Contains(Catalog.Programs, p => p.Id == id);
        // and what the user took out stays out
        foreach (var id in new[] { "brave", "firefox", "claude", "battlenet", "vlc", "capcut", "everything", "notepadpp", "powertoys", "autoruns", "startallback", "whatsapp", "netflix", "gamingservices", "xbox", "minecraft" })
            Assert.DoesNotContain(Catalog.Programs, p => p.Id == id);
    }

    [Fact]
    public void ChromeExtensionPolicyMatchesTheExtensionList()
    {
        Assert.All(Catalog.Extensions, e => Assert.Matches("^[a-p]{32}$", e.ExtensionId));
        var policy = Catalog.Find("apps.chrome.extensions")!.Actions.OfType<RegistryAction>().Single();
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Google\Chrome", policy.Path);
        Assert.Equal("ExtensionSettings", policy.Name);
        using var doc = System.Text.Json.JsonDocument.Parse(policy.Value!.Value.GetString()!);
        var ids = doc.RootElement.EnumerateObject().Select(o => o.Name).OrderBy(x => x).ToList();
        Assert.Equal(Catalog.Extensions.Select(e => e.ExtensionId).OrderBy(x => x), ids);
        foreach (var o in doc.RootElement.EnumerateObject())
        {
            Assert.Equal("normal_installed", o.Value.GetProperty("installation_mode").GetString()); // installs itself, user can still disable
            Assert.Equal("https://clients2.google.com/service/update2/crx", o.Value.GetProperty("update_url").GetString());
        }
    }

    [Fact]
    public void FeatureQuestionsPointAtRealTweaks()
    {
        Assert.Equal(Catalog.Features.Count, Catalog.Features.Select(f => f.Id).Distinct().Count());
        foreach (var f in Catalog.Features)
        {
            Assert.NotEmpty(f.Tweaks);
            foreach (var id in f.Tweaks)
            {
                var tw = Catalog.Find(id);
                Assert.True(tw is not null, $"{f.Id}: unknown tweak {id}");
            }
            if (f.UsedWhen is not null)
                foreach (var fact in ConditionEvaluator.Identifiers(f.UsedWhen))
                    Assert.True(SystemProfile.KnownFacts.Contains(fact) || fact.StartsWith("app.", StringComparison.Ordinal), $"{f.Id}: unknown fact {fact}");
        }
        // anything that disables a feature people use asks first
        foreach (var id in new[] { "system.hyperv.off", "system.smartcard.off", "system.remote.off", "system.biometrics.off" })
            Assert.True(Catalog.Find(id)!.Confirm, id);
    }

    [Fact]
    public void EveryQuestionHasBothSides()
    {
        // Game setup asks about these one by one: "switch off" (gain, and what is lost) against "keep".
        var featureTweaks = Catalog.Features.SelectMany(f => f.Tweaks).ToHashSet();
        foreach (var t in Catalog.Tweaks.Where(t => t.Confirm || t.Risk == RiskLevel.High || featureTweaks.Contains(t.Id)))
        {
            Assert.True(t.Gain is { Ru.Length: > 0, En.Length: > 0 }, $"{t.Id} needs gain");
            Assert.True(t.Keep is { Ru.Length: > 0, En.Length: > 0 }, $"{t.Id} needs keep");
        }
        foreach (var id in new[] { "system.update.manual", "system.update.defer-feature", "system.store-autoupdate.off", "gaming.gamebar.off" })
            Assert.True(Catalog.Find(id) is { Confirm: true }, id);
    }

    [Fact]
    public void NothingTurnsOffWindowsProtection()
    {
        // Antivirus and UAC prompts stay on: no tweak may switch them off.
        // Neither do memory integrity / VBS, Credential Guard, CPU vulnerability mitigations, SmartScreen or the firewall.
        string[] banned = ["DisableRealtimeMonitoring", "DisableAntiSpyware", "DisableAntiVirus", "DisableBehaviorMonitoring", "DisableIOAVProtection", "EnableLUA", "ConsentPromptBehaviorAdmin", "PromptOnSecureDesktop",
            "EnableVirtualizationBasedSecurity", "HypervisorEnforcedCodeIntegrity", "LsaCfgFlags", "FeatureSettingsOverride", "DisableExceptionChainValidation", "hypervisorlaunchtype",
            "EnableSmartScreen", "SmartScreenEnabled", "EnableFirewall", "Set-NetFirewallProfile", "Set-MpPreference -Disable"];
        string[] services = ["WinDefend", "WdNisSvc", "Sense", "SecurityHealthService", "wscsvc", "mpssvc"];
        foreach (var t in Catalog.Tweaks)
            foreach (var a in t.Actions)
            {
                var text = a switch
                {
                    RegistryAction r => r.Path + " " + r.Name,
                    PowerShellAction ps => ps.Script,
                    CommandAction c => c.Describe(),
                    BcdAction b => b.Element,
                    _ => a.Describe(),
                };
                foreach (var b in banned) Assert.False(text.Contains(b, StringComparison.OrdinalIgnoreCase), $"{t.Id}: {b}");
                if (a is ServiceAction sv) Assert.DoesNotContain(sv.Name, services, StringComparer.OrdinalIgnoreCase);
            }
    }
}
