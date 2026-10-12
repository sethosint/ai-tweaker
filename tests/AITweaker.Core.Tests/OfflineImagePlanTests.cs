using AITweaker.Core.Catalog;
using AITweaker.Core.Image;
using AITweaker.Core.Model;

namespace AITweaker.Core.Tests;

public class OfflineImagePlanTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadEmbedded();

    [Fact]
    public void BakesSafeRegistryAndServiceTweaks()
    {
        var plan = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { MaxLevel = 2 });
        Assert.Contains("privacy.telemetry.policy", plan.TweakIds);
        Assert.Contains("privacy.copilot", plan.TweakIds);
        Assert.Contains("privacy.recall", plan.TweakIds); // os.build >= 26100 is decidable offline
        Assert.Contains("system.services.unused", plan.TweakIds);
        Assert.Contains(plan.Services, s => s.Name == "DiagTrack" && s.Start == 4);
    }

    [Fact]
    public void LeavesHardwareAndLiveOnlyTweaksForAfterInstall()
    {
        var plan = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { MaxLevel = 4 });
        Assert.DoesNotContain("gaming.hags", plan.TweakIds);              // needs a discrete GPU
        Assert.DoesNotContain("power.plan.ultimate", plan.TweakIds);      // power scheme on the live system
        Assert.DoesNotContain("devices.interrupt-affinity", plan.TweakIds);
        Assert.DoesNotContain("network.nagle.off", plan.TweakIds);        // per-interface wildcard
        Assert.DoesNotContain("system.ntfs", plan.TweakIds);              // command
        Assert.Contains(plan.Skipped, s => s.Id == "gaming.hags" && s.Reason == "hardware");
        Assert.Contains(plan.Skipped, s => s.Id == "network.nagle.off" && s.Reason == "per-device");
    }

    [Fact]
    public void NeverBakesConfirmTweaksUnlessChosen()
    {
        var auto = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { MaxLevel = 4 });
        Assert.DoesNotContain(auto.TweakIds, id => Catalog.Find(id)!.Confirm);

        var chosen = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { TweakIds = ["system.search-indexer.off", "privacy.advertising"] });
        Assert.Equal(new[] { "privacy.advertising", "system.search-indexer.off" }, chosen.TweakIds.OrderBy(x => x));
    }

    [Fact]
    public void MapsLivePathsToOfflineHives()
    {
        Assert.Equal((OfflineHive.Software, @"Policies\Microsoft\Windows\DataCollection"), OfflineImagePlan.Map(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection"));
        Assert.Equal((OfflineHive.System, @"ControlSet001\Control\PriorityControl"), OfflineImagePlan.Map(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl"));
        Assert.Equal((OfflineHive.DefaultUser, @"Control Panel\Mouse"), OfflineImagePlan.Map(@"HKCU\Control Panel\Mouse"));
        Assert.Null(OfflineImagePlan.Map(@"HKU\.DEFAULT\Control Panel\Keyboard"));

        var plan = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { MaxLevel = 2 });
        Assert.All(plan.Registry, r => Assert.False(string.IsNullOrWhiteSpace(r.SubKey)));
        Assert.Contains(plan.Registry, r => r.Hive == OfflineHive.DefaultUser && r.Name == "MouseSpeed" && r.Data == "0");
        Assert.Contains(plan.Registry, r => r.Name == "NetworkThrottlingIndex" && r.Data == "-1"); // 0xFFFFFFFF bit pattern
    }

    [Fact]
    public void RemovesOnlySafeAppsByDefault()
    {
        var plan = OfflineImagePlan.Build(Catalog, new OfflineImageOptions());
        Assert.Contains("Microsoft.BingNews", plan.Apps);
        Assert.DoesNotContain("Microsoft.WindowsStore", plan.Apps);
        Assert.DoesNotContain("Microsoft.XboxGamingOverlay", plan.Apps);

        var withStore = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { ExtraApps = ["Microsoft.WindowsStore"] });
        Assert.Contains("Microsoft.WindowsStore", withStore.Apps);

        var none = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { RemoveApps = false, RemoveLegacyComponents = false });
        Assert.Empty(none.Apps);
        Assert.Empty(none.Capabilities);
    }
}

public class LookAndUpdatesTests
{
    private static readonly TweakCatalog Catalog = TweakCatalog.LoadEmbedded();

    [Fact]
    public void BakesTheAITweakerLookIntoNewAccounts()
    {
        var plan = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { MaxLevel = 1 });
        Assert.True(plan.IncludesLook);
        Assert.Contains("look.dark", plan.TweakIds);
        Assert.Contains("look.accent", plan.TweakIds);
        Assert.Contains(plan.Registry, r => r.Hive == OfflineHive.DefaultUser && r.Name == "WallPaper" && r.Data == LookAssets.WallpaperFile("dark"));
        Assert.Contains(plan.Registry, r => r.Hive == OfflineHive.Software && r.Name == "LockScreenImagePath" && r.Data == LookAssets.WallpaperFile("lock"));
        Assert.Contains(plan.Registry, r => r.Name == "InstallTheme" && r.Kind == RegistryValueKind2.Expand && r.Data == LookAssets.ThemePathExpand);
        var palette = plan.Registry.Single(r => r.Name == "AccentPalette");
        Assert.Equal(LookAssets.AccentPalette(), Convert.FromBase64String(palette.Data!));
        Assert.Contains(plan.Registry, r => r.Name == "AccentColorMenu" && unchecked((uint)int.Parse(r.Data!)) == LookAssets.Abgr(LookAssets.Accent));
    }

    [Fact]
    public void LookCanBeLeftOut()
    {
        var plan = OfflineImagePlan.Build(Catalog, new OfflineImageOptions { MaxLevel = 4, ApplyLook = false });
        Assert.False(plan.IncludesLook);
        Assert.DoesNotContain(plan.TweakIds, id => id.StartsWith("look."));
        Assert.DoesNotContain(plan.Registry, r => r.Name == "InstallTheme");
    }

    [Fact]
    public void CatalogAccentMatchesLookAssets()
    {
        var accent = Catalog.Find("look.accent")!.Actions.OfType<RegistryAction>().ToList();
        Assert.Equal(Convert.ToBase64String(LookAssets.AccentPalette()), accent.Single(a => a.Name == "AccentPalette").Value!.Value.GetString());
        Assert.Equal(LookAssets.Abgr(LookAssets.Accent), (uint)accent.Single(a => a.Name == "AccentColorMenu").Value!.Value.GetInt64());
        Assert.Equal(LookAssets.Abgr(LookAssets.Palette[5]), (uint)accent.Single(a => a.Name == "StartColorMenu").Value!.Value.GetInt64());
        Assert.Equal(LookAssets.Argb(LookAssets.Accent), (uint)accent.Single(a => a.Name == "ColorizationColor").Value!.Value.GetInt64());
        Assert.Equal(0xFFFFFFu & 0xE07A12u, LookAssets.Argb(LookAssets.Accent) & 0xFFFFFFu);
    }

    [Fact]
    public void ThemeFileIsDarkWithOurWallpaper()
    {
        var theme = LookAssets.ThemeFile();
        Assert.Contains("DisplayName=AI Tweaker", theme);
        Assert.Contains("Wallpaper=" + LookAssets.WallpaperFile("dark"), theme);
        Assert.Contains("SystemMode=Dark", theme);
        Assert.Contains("ColorizationColor=0XC4E07A12", theme);
    }

    private const string SearchHtml = """
        <table><tr id="aaaaaaaa-1111-2222-3333-444444444444_R1">
          <td class="resultspadding" id="aaaaaaaa-1111-2222-3333-444444444444_C1_R1"><a id='aaaaaaaa-1111-2222-3333-444444444444_link' href="javascript:void(0);">
            2026-09 Cumulative Update for Windows 11 Version 25H2 for x64-based Systems (KB5065001) (26200.6500)
          </a></td><td class="resultspadding" id="aaaaaaaa-1111-2222-3333-444444444444_C4_R1">9/9/2026</td></tr>
        <tr><td id="bbbbbbbb-1111-2222-3333-444444444444_C1_R2"><a id='bbbbbbbb-1111-2222-3333-444444444444_link'>
            2026-09 Cumulative Update Preview for Windows 11 Version 25H2 for x64-based Systems (KB5066002) (26200.6600)
          </a></td><td class="resultspadding" id="bbbbbbbb-1111-2222-3333-444444444444_C4_R2">9/23/2026</td></tr>
        <tr><td id="cccccccc-1111-2222-3333-444444444444_C1_R3"><a id='cccccccc-1111-2222-3333-444444444444_link'>
            2026-10 Cumulative Update for Windows 11 Version 25H2 for x64-based Systems (KB5067003) (26200.6700)
          </a></td><td class="resultspadding" id="cccccccc-1111-2222-3333-444444444444_C4_R3">10/14/2026</td></tr>
        <tr><td id="dddddddd-1111-2222-3333-444444444444_C1_R4"><a id='dddddddd-1111-2222-3333-444444444444_link'>
            2026-10 Cumulative Update for Windows 11 Version 25H2 for arm64-based Systems (KB5067003) (26200.6700)
          </a></td><td class="resultspadding" id="dddddddd-1111-2222-3333-444444444444_C4_R4">10/14/2026</td></tr>
        <tr><td id="eeeeeeee-1111-2222-3333-444444444444_C1_R5"><a id='eeeeeeee-1111-2222-3333-444444444444_link'>
            2026-10 Cumulative Update for .NET Framework 3.5 and 4.8.1 for Windows 11 Version 25H2 for x64-based Systems (KB5067100)
          </a></td><td class="resultspadding" id="eeeeeeee-1111-2222-3333-444444444444_C4_R5">10/20/2026</td></tr></table>
        """;

    [Fact]
    public void PicksNewestRegularCumulativeUpdate()
    {
        var u = UpdateCatalogParser.Latest(SearchHtml, "25H2");
        Assert.NotNull(u);
        Assert.Equal("cccccccc-1111-2222-3333-444444444444", u!.Id);
        Assert.Equal("KB5067003", u.Kb);
        Assert.Equal(new DateTime(2026, 10, 14), u.Date);
        Assert.Null(UpdateCatalogParser.Latest(SearchHtml, "26H2"));
    }

    [Fact]
    public void ExtractsCheckpointAndLatestDownloadLinks()
    {
        const string dialog = """
            var downloadInformation = new Array();
            downloadInformation[0].files[0].url = 'https://catalog.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/x/public/windows11.0-kb5043080-x64_aaa.msu';
            downloadInformation[0].files[1].url = 'https://catalog.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/y/public/windows11.0-kb5067003-x64_bbb.msu';
            downloadInformation[0].files[1].url = 'https://catalog.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/y/public/windows11.0-kb5067003-x64_bbb.msu';
            """;
        var urls = UpdateCatalogParser.DownloadUrls(dialog);
        Assert.Equal(2, urls.Count);
        Assert.EndsWith("kb5067003-x64_bbb.msu", urls[1]);
        Assert.Contains("uidInfo%22%3A%22cccc", UpdateCatalogParser.DownloadRequest("cccc"));
    }
}
