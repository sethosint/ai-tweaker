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
        Assert.True(Catalog.Guides.Count >= 8);
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
                    Assert.Contains(s.Scheme, new[] { "ultimate", "high", "balanced" });
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
            }
        }
    }

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
    public void GuidesReferenceExistingImagesAndTweaks()
    {
        var root = Path.Combine(RepoRoot(), "src", "AITweaker.App", "wwwroot", "guides");
        foreach (var g in Catalog.Guides)
        {
            foreach (var s in g.Sections.Where(s => s.Image is not null))
                Assert.True(File.Exists(Path.Combine(root, s.Image!)), $"missing guide image {s.Image}");
            foreach (var id in g.AutoTweaks)
                Assert.NotNull(Catalog.Find(id));
        }
    }

    [Fact]
    public void BiosItemsHaveFallbackPath()
    {
        foreach (var b in Catalog.Bios) Assert.True(b.Paths.ContainsKey("other"), b.Id);
    }
}
