using System.Text.RegularExpressions;
using System.Xml.Linq;
using AITweaker.Core.Catalog;
using AITweaker.Core.Image;

namespace AITweaker.Core.Tests;

public class AmePlaybookTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadEmbedded();
    private static readonly AmePlaybook Playbook = AmePlaybook.Build(Catalog, "ru");

    private static IEnumerable<string> Yaml => Playbook.Files.Where(f => f.Key.EndsWith(".yml")).SelectMany(f => f.Value.Split('\n'));

    [Fact]
    public void HasTheLookOpenShellAndOneTaskPerFeature()
    {
        Assert.Contains("playbook.conf", Playbook.Files.Keys);
        Assert.Contains(@"Configuration\custom.yml", Playbook.Files.Keys);
        Assert.Contains(@"Configuration\look.yml", Playbook.Files.Keys);
        Assert.Contains(@"Configuration\openshell.yml", Playbook.Files.Keys);
        foreach (var f in Catalog.Features)
            Assert.Contains($@"Configuration\features\{f.Id}.yml", Playbook.Files.Keys);
        Assert.Contains("aitweaker-lock.jpg", Playbook.Wallpapers);
        Assert.Empty(Playbook.Skipped);
    }

    [Fact]
    public void ConfFollowsAmeWizardRules()
    {
        var conf = XElement.Parse(Playbook.Files["playbook.conf"]);
        Assert.Equal("AI Tweaker", conf.Element("Name")!.Value);
        Assert.Matches(@"^\d+(\.\d+){0,2}$", conf.Element("Version")!.Value);
        Assert.NotEqual(Guid.Empty, Guid.Parse(conf.Element("UniqueId")!.Value));

        var pages = conf.Element("FeaturePages")!.Elements().ToList();
        foreach (var page in pages)
        {
            var options = page.Element("Options")!.Elements().ToList();
            int lines = (page.Element("TopLine") is null ? 0 : 1) + (page.Element("BottomLine") is null ? 0 : 1);
            Assert.True(options.Count <= 4 - lines, $"{page.Name} has {options.Count} options and {lines} lines");
            if (page.Attribute("DefaultOption") is { } def)
                Assert.Contains(options, o => o.Element("Name")!.Value == def.Value);
        }
        // checkboxes start ticked unless IsChecked says otherwise; core isolation stays on by default
        var vbs = pages.SelectMany(p => p.Descendants("CheckboxOption")).Single(o => o.Element("Name")!.Value == "off-vbs");
        Assert.Equal("false", vbs.Attribute("IsChecked")!.Value);

        // every option on a page drives a task, and every option a task uses is on a page
        var offered = pages.SelectMany(p => p.Descendants("Name")).Select(n => n.Value).ToHashSet();
        var used = Regex.Matches(Playbook.Files[@"Configuration\custom.yml"], @"option: '([^']+)'").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Equal(offered.Where(o => o != "start-windows").OrderBy(o => o), used.OrderBy(o => o));
    }

    [Fact]
    public void EveryTaskAndScriptExists()
    {
        foreach (Match m in Regex.Matches(string.Join('\n', Yaml), @"!task: \{path: '([^']+)'"))
            Assert.Contains(@"Configuration\" + m.Groups[1].Value, Playbook.Files.Keys);
        foreach (Match m in Regex.Matches(string.Join('\n', Yaml), @"& \.\\([^']+\.ps1)"))
            Assert.Contains(@"Executables\" + m.Groups[1].Value, Playbook.Files.Keys);
    }

    [Fact]
    public void ActionsUseAmeSemantics()
    {
        var lines = Yaml.ToList();
        // !service without an operation deletes the service
        Assert.All(lines.Where(l => l.Contains("!service:")), l => Assert.Matches("operation: (stop|change)", l));
        Assert.Contains(lines, l => l.Contains("name: 'Spooler', operation: change, startup: 4"));
        // AME runs PowerShell as -C "<command>"
        Assert.All(lines.Where(l => l.Contains("!powerShell:")), l => Assert.DoesNotContain("\"", l));
        // REG_DWORD is unsigned decimal, REG_BINARY hex, REG_MULTI_SZ joined with a literal \0
        Assert.Contains(lines, l => l.Contains("value: 'ColorizationColor', type: REG_DWORD, data: '3290044954'"));
        Assert.Contains(lines, l => Regex.IsMatch(l, "value: 'AccentPalette', type: REG_BINARY, data: '[0-9A-F]{64}'"));
        Assert.Contains(lines, l => l.Contains(@"value: 'MenuItems7', type: REG_MULTI_SZ, data: 'Item1.Command=downloads\0Item1.Settings=NOEXPAND\0"));
        Assert.Contains(lines, l => l.Contains("exe: 'bcdedit.exe', args: '/set hypervisorlaunchtype off'"));
    }

    [Fact]
    public void LoadsTheDefaultProfileSoNewAccountsGetTheLook()
    {
        var root = Playbook.Files[@"Configuration\custom.yml"].Split('\n').Where(l => l.StartsWith("  - ")).ToList();
        Assert.Contains(@"reg load HKU\AME_UserHive_Default", root[0]);
        Assert.Contains(@"reg unload HKU\AME_UserHive_Default", root[^1]);
        Assert.Contains(Yaml, l => l.Contains("value: 'InstallTheme', type: REG_EXPAND_SZ"));
    }

    [Fact]
    public void SpeaksTheUsersLanguage()
    {
        var en = AmePlaybook.Build(Catalog, "en");
        Assert.Contains("Start menu", en.Files["playbook.conf"]);
        Assert.Contains("Меню «Пуск»", Playbook.Files["playbook.conf"]);
    }
}
