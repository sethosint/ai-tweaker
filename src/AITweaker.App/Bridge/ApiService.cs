using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using AITweaker.App.Platform;
using AITweaker.Core.Ai;
using AITweaker.Core.Bios;
using AITweaker.Core.Catalog;
using AITweaker.Core.Conditions;
using AITweaker.Core.Engine;
using AITweaker.Core.Image;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.App.Bridge;

/// <summary>Every method the web UI can call. Methods run off the UI thread unless they need a dialog.</summary>
public sealed class ApiService
{
    private readonly TweakCatalog _catalog = TweakCatalog.Default;
    private readonly RecommendationEngine _engine;
    private readonly Func<Func<object?>, Task<object?>> _onUi;
    private readonly Action<string, object?> _emit;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private SystemProfile? _profile;
    private List<Insight> _insights = [];
    private CancellationTokenSource? _longTask;

    public Action<string>? ThemeChanged { get; set; }

    private NvidiaDriverStatus? _nvidia;

    public ApiService(Func<Func<object?>, Task<object?>> onUi, Action<string, object?> emit)
    {
        _engine = new RecommendationEngine(_catalog);
        _onUi = onUi;
        _emit = emit;
    }

    public async Task<object?> InvokeAsync(string method, JsonElement p) => method switch
    {
        "app.init" => await InitAsync(),
        "system.scan" => await ScanAsync(),
        "plan.build" => BuildPlan(Str(p, "profile") ?? SettingsStore.Current.Profile),
        "tweaks.catalog" => Catalog(),
        "tweaks.status" => await StatusAsync(Ids(p)),
        "tweaks.apply" => await ApplyAsync(Ids(p) ?? [], Bool(p, "restorePoint") ?? SettingsStore.Current.CreateRestorePoint, Str(p, "profile") ?? SettingsStore.Current.Profile),
        "tweaks.revert" => await RevertAsync(Ids(p) ?? []),
        "backup.list" => Backups(),
        "restore.create" => await Exclusive(() => RestorePoint.CreateAsync("AI Tweaker — manual restore point")),
        "restore.open" => Do(RestorePoint.OpenSystemRestore),
        "appx.list" => await AppxListAsync(),
        "appx.restore" => await Exclusive(() => Platform.Actions.AppxOps.RestoreAsync(Str(p, "name") ?? "",
            _catalog.Apps.FirstOrDefault(a => a.Package.Equals(Str(p, "name"), StringComparison.OrdinalIgnoreCase))?.StoreId, NewLongTask())),
        "appx.remove" => await Exclusive(() => Platform.Actions.AppxOps.RemoveAsync(Ids(p, "names") ?? [], n => _emit("appx.progress", n))),
        "drivers.list" => DriverCenter.Build(await ProfileAsync()),
        "drivers.rescan" => await DriverCenter.RescanAsync(),
        "nvidia.check" => _nvidia = await NvidiaTools.CheckAsync(await ProfileAsync()),
        "nvidia.install" => await Exclusive(async () =>
        {
            var offer = (_nvidia ??= await NvidiaTools.CheckAsync(await ProfileAsync())).Latest
                ?? throw new InvalidOperationException(_nvidia.Error ?? "No driver found for this card");
            var r = await NvidiaTools.InstallAsync(offer, Bool(p, "hdAudio") ?? true, Bool(p, "physX") ?? true, Bool(p, "clean") ?? true,
                (step, v) => _emit("nvidia.progress", new { step, value = v }), NewLongTask());
            _profile = null; // the new driver version shows up on the next scan
            _nvidia = null;
            return r;
        }),
        "guides.list" => await GuidesAsync(),
        "programs.list" => await Task.Run(Programs),
        "features.list" => await FeaturesAsync(),
        "display.modes" => DisplayModes.List(),
        "display.set" => DisplayModes.Set(Str(p, "device") ?? "", Int(p, "width") ?? 0, Int(p, "height") ?? 0, Int(p, "hz") ?? 0, 15),
        "display.keep" => DisplayModes.Keep(Str(p, "device") ?? ""),
        "display.revert" => DisplayModes.Revert(Str(p, "device") ?? ""),
        "programs.install" => await Exclusive(async () =>
        {
            var summary = await ProgramInstaller.InstallAsync(_catalog, Ids(p) ?? [], (i, n, name) => _emit("install.progress", new { i, n, name }), NewLongTask());
            if (summary.RebootRequired) SettingsStore.MarkRebootPending();
            _profile = null; // newly installed apps (e.g. Open-Shell) unlock their tweaks on the next scan
            return summary;
        }),
        "programs.uninstall" => await Exclusive(() => ProgramInstaller.UninstallAsync(_catalog, Str(p, "id") ?? "", NewLongTask())),
        "programs.repairWinget" => await ProgramInstaller.RepairWingetAsync(),
        "bios.list" => await BiosAsync(),
        "bios.reboot" => await RebootAsync(firmware: true),
        "system.reboot" => await RebootAsync(firmware: false),
        "image.preview" => ImagePreview(p),
        "image.build" => await Exclusive(() => ImageBuilder.BuildAsync(Str(p, "iso") ?? "", OfflineImagePlan.Build(_catalog, ImageOptions(p)), (s, v) => _emit("image.progress", new { step = s, value = v }), NewLongTask())),
        "image.ame" => await AmeExport.ExportAsync(_catalog, SettingsStore.Current.Language),
        "image.existing" => File.Exists(Path.Combine(ImageBuilder.OutputFolder, "sources", "install.wim")) ? ImageBuilder.OutputFolder : null,
        "system.logoff" => await LogoffAsync(),
        "system.rebootPending" => SettingsStore.RebootPending,
        "firstrun.done" => Do(() => { SettingsStore.Current.FirstRunDone = true; SettingsStore.Save(); }),
        "usb.list" => await UsbCreator.ListAsync(),
        "usb.rufus" => await RufusTool.OpenAsync(Str(p, "iso"), SettingsStore.Current.Language),
        "usb.copyTweaker" => RufusTool.CopyTweakerToSticks(),
        "usb.pickIso" => await _onUi(PickIso),
        "usb.downloadIso" => await Exclusive(() => UsbCreator.DownloadIsoAsync(Str(p, "language") ?? "Russian", (s, v) => _emit("usb.progress", new { step = s, value = v }), NewLongTask())),
        "reinstall.start" => await Exclusive(async () => await LocalReinstall.StartAsync(await ProfileAsync(),
            Str(p, "source") switch { "built" => ImageBuilder.OutputFolder, "iso" => Str(p, "iso"), _ => null },
            Str(p, "mode") ?? "clean", Str(p, "language") ?? "English", (s, v) => _emit("reinstall.progress", new { step = s, value = v }), NewLongTask())),
        "usb.preview" => UnattendGenerator.Generate(Unattend(p)),
        "usb.create" => await Exclusive(async () =>
        {
            await UsbCreator.CreateAsync(Int(p, "disk") ?? -1, Str(p, "iso") ?? "", Unattend(p), (s, v) => _emit("usb.progress", new { step = s, value = v }), NewLongTask());
            return true;
        }),
        "task.cancel" => Do(() => _longTask?.Cancel()),
        "ai.analyze" => await AnalyzeAsync(Str(p, "profile") ?? SettingsStore.Current.Profile),
        "settings.set" => SetSettings(p),
        "report.export" => await ExportReportAsync(),
        "shell.open" => Do(() => ProcessRunner.OpenUrl(Str(p, "url") ?? "")),
        "shell.reveal" => Do(() => ProcessRunner.Reveal(Str(p, "path") ?? "")),
        "shell.folder" => Do(() => ProcessRunner.OpenFolder(Str(p, "name") switch { "backups" => AppPaths.Backups, "downloads" => AppPaths.Downloads, _ => AppPaths.Logs })),
        "window.theme" => Do(() => ThemeChanged?.Invoke(Str(p, "theme") ?? "day")),
        "system.theme" => SystemThemeReader.Read(),
        "net.test" => await Exclusive(() => NetworkTest.RunAsync(Targets(p), Int(p, "count") ?? 30, (h, i, n) => _emit("net.progress", new { host = h, i, n }), NewLongTask())),
        "net.flushdns" => await NetworkTest.FlushDnsAsync(),
        "net.dns" => await NetworkTest.GetDnsAsync(),
        "net.dnsSet" => await Exclusive(async () => { await NetworkTest.SetDnsAsync(Str(p, "preset") ?? "auto"); return true; }),
        "net.dnsBench" => await NetworkTest.BenchmarkDnsAsync(),
        "services.list" => await Task.Run(() => ServiceManager.List(_catalog)),
        "services.setStart" => await ServiceManager.SetStartAsync(Str(p, "name") ?? "", Str(p, "start") ?? ""),
        "services.run" => await ServiceManager.StartAsync(Str(p, "name") ?? "", Bool(p, "start") ?? true),
        "services.restore" => await ServiceManager.RestoreAsync(Str(p, "name") ?? ""),
        "net.reset" => await Exclusive(async () => { await NetworkTest.ResetStackAsync(); SettingsStore.MarkRebootPending(); return true; }),
        "startup.list" => StartupManager.List(),
        "startup.set" => Do(() => StartupManager.SetEnabled(Str(p, "id") ?? "", Bool(p, "enabled") ?? true)),
        "startup.cleanup" => StartupManager.DisableUnneeded(),
        "clean.analyze" => await Task.Run(Cleaner.Analyze),
        "clean.run" => await Exclusive(() => Cleaner.CleanAsync(Ids(p) ?? [])),
        "memory.purge" => MemoryTools.PurgeStandby(),
        "games.list" => await Task.Run(async () => GameProfiles.List(await ProfileAsync())),
        "games.set" => await GameProfiles.SetAsync(Str(p, "exe") ?? "", Bool(p, "priority"), Bool(p, "gpu"), Bool(p, "qos")),
        "games.all" => await GameProfiles.SetAllAsync(await ProfileAsync()),
        "games.add" => await _onUi(PickGame) is string exe && GameProfiles.Add(exe),
        "games.remove" => await GameProfiles.RemoveAsync(Str(p, "exe") ?? ""),
        "gamemode.state" => GameSession.Describe(await ProfileAsync()),
        "gamemode.start" => await Exclusive(async () => await GameSession.StartAsync(Ids(p, "groups") ?? [], await ProfileAsync())),
        "gamemode.stop" => await GameSession.StopAsync(),
        "explorer.restart" => await RestartExplorer(),
        _ => throw new InvalidOperationException($"Unknown method {method}"),
    };

    // ------------------------------------------------------------------ app & scan

    private async Task<object> InitAsync()
    {
        await GameSession.RestoreLeftoverAsync();
        var s = SettingsStore.Current;
        return new
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
            isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),
            machine = Environment.MachineName,
            system = SystemThemeReader.Read(),
            settings = new
            {
                language = s.Language,
                theme = s.Theme,
                accent = s.Accent,
                showAdvisor = s.ShowAdvisor,
                profile = s.Profile,
                model = s.ClaudeModel,
                hasKey = SettingsStore.GetClaudeKey() is not null,
                pingHost = s.PingHost,
                createRestorePoint = s.CreateRestorePoint,
                autoRestartSeconds = s.AutoRestartSeconds,
            },
            rebootPending = SettingsStore.RebootPending,
            image = InstalledImage(),
            // Started by autounattend after a clean install, or the first launch on a PC installed from our image.
            firstRun = !SettingsStore.Current.FirstRunDone
                && (Environment.GetCommandLineArgs().Contains("--first-run", StringComparer.OrdinalIgnoreCase) || InstalledImage() is not null),
            models = ClaudeAdvisor.Models,
            categories = _catalog.Categories,
            programIds = _catalog.Programs.Select(x => x.Id),
            log = AppLog.Snapshot().TakeLast(100),
        };
    }

    /// <summary>AITweaker-image.json that <see cref="ImageBuilder"/> leaves in Windows\ when this PC was installed from our image.</summary>
    private static JsonElement? InstalledImage()
    {
        try
        {
            var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "AITweaker-image.json");
            return File.Exists(f) ? JsonDocument.Parse(File.ReadAllText(f)).RootElement.Clone() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<SystemProfile> ProfileAsync()
    {
        if (_profile is null) await ScanAsync();
        return _profile!;
    }

    private async Task<object> ScanAsync()
    {
        AppLog.Info("scan", "scanning hardware and Windows configuration…");
        var sw = Stopwatch.StartNew();
        _profile = await Task.Run(SystemProfiler.Scan);
        _insights = InsightEngine.Analyze(_profile);
        AppLog.Ok("scan", $"scan finished in {sw.ElapsedMilliseconds} ms: {_profile.Cpu.Name}, {_profile.PrimaryGpu?.Name}, {_profile.Memory.TotalGb:0} GB");
        foreach (var i in _insights.Where(i => i.Severity != InsightSeverity.Info))
            AppLog.Write(i.Severity == InsightSeverity.Critical ? "error" : "warn", "diag", i.Title.En);
        return new { profile = _profile, facts = _profile.ToFacts(), insights = _insights };
    }

    // ------------------------------------------------------------------ tweaks

    private object BuildPlan(string profileName)
    {
        var profile = _profile ?? throw new InvalidOperationException("Scan the system first");
        var level = Enum.TryParse<TuneProfile>(profileName, true, out var l) ? l : TuneProfile.Performance;
        var plan = _engine.BuildPlan(profile, level);
        return new
        {
            profile = level.ToString(),
            items = plan.Items.Select(i => new { id = i.Tweak.Id, recommended = i.Recommended, needsConfirm = i.NeedsConfirm, reason = i.Reason }),
            skipped = plan.SkippedIds,
        };
    }

    private object Catalog() => _catalog.Tweaks.Select(t => new
    {
        t.Id,
        t.Category,
        t.Name,
        t.Description,
        t.Level,
        risk = t.Risk.ToString().ToLowerInvariant(),
        t.Confirm,
        t.ConfirmText,
        t.Gain,
        t.Keep,
        t.Reboot,
        t.Logoff,
        t.RestartExplorer,
        t.Reversible,
        t.When,
        t.Why,
        actions = t.Actions.Select(a => a.Describe()),
    });

    private async Task<object> StatusAsync(List<string>? ids)
    {
        var profile = await ProfileAsync();
        var tweaks = ids is null ? _catalog.Tweaks.ToList() : ids.Select(_catalog.Find).OfType<TweakDefinition>().ToList();
        return await Task.Run(() => new TweakExecutor(profile).StatusAsync(tweaks));
    }

    private async Task<object> ApplyAsync(List<string> ids, bool restorePoint, string profileName)
    {
        var profile = await ProfileAsync();
        // Keep catalog order: power plan before power settings, services before tasks, etc.
        var set = ids.ToHashSet();
        // Power plan switches run first so every power setting lands on the final active scheme.
        var tweaks = _catalog.Tweaks.Where(t => set.Contains(t.Id))
            .OrderBy(t => t.Actions.Any(a => a is PowerSchemeAction) ? 0 : 1)
            .ToList();
        if (tweaks.Count == 0) throw new InvalidOperationException("Nothing selected");
        return await Exclusive(async () =>
        {
            AppLog.Info("apply", $"applying {tweaks.Count} tweaks (profile {profileName})");
            var exec = new TweakExecutor(profile);
            exec.Progress += (i, n, id) => _emit("apply.progress", new { i, n, id });
            var summary = await exec.ApplyAsync(tweaks, restorePoint, profileName, NewLongTask());
            AppLog.Ok("apply", $"{summary.Results.Count(r => r.Ok)}/{summary.Results.Count} tweaks applied{(summary.RebootRequired ? ", reboot required" : "")}");
            if (summary.RebootRequired) SettingsStore.MarkRebootPending();
            return summary;
        });
    }

    private async Task<object> RevertAsync(List<string> ids)
    {
        var profile = await ProfileAsync();
        var tweaks = ids.Select(_catalog.Find).OfType<TweakDefinition>().ToList();
        var results = await Exclusive(() => new TweakExecutor(profile).RevertAsync(tweaks));
        if (results.Any(r => r.Ok && _catalog.Find(r.Id)?.Reboot == true)) SettingsStore.MarkRebootPending();
        return results;
    }

    private object Backups() => new
    {
        applied = BackupStore.All().Select(b => new { b.TweakId, b.AppliedAt, changes = b.Changes.Count, name = _catalog.Find(b.TweakId)?.Name }),
        sessions = BackupStore.Sessions(),
        folder = AppPaths.Backups,
    };

    // ------------------------------------------------------------------ debloat / guides / bios

    private async Task<object> AppxListAsync()
    {
        var profile = await ProfileAsync();
        var facts = profile.ToFacts();
        var installed = await Platform.Actions.AppxOps.ListAsync();
        var byName = installed.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var known = _catalog.Apps.Select(a => new
        {
            a.Package,
            a.Name,
            a.Group,
            a.Recommended,
            a.Important,
            a.Warning,
            keep = a.KeepWhen is not null && ConditionEvaluator.Evaluate(a.KeepWhen, facts),
            a.KeepReason,
            a.StoreId,
            installed = byName.ContainsKey(a.Package),
        }).ToList();
        // removed earlier (by AI Tweaker or anything else): offered for reinstall
        var removed = known.Where(a => !a.installed && (a.StoreId is not null || a.Package is "Microsoft.WindowsStore")).ToList();
        known = known.Where(a => a.installed).ToList();

        var catalogNames = _catalog.Apps.Select(a => a.Package).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = installed
            .Where(p => !p.IsFramework && !p.NonRemovable && !catalogNames.Contains(p.Name)
                        && !p.Name.StartsWith("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase)
                        && !p.Name.StartsWith("Microsoft.NET", StringComparison.OrdinalIgnoreCase)
                        && !p.Name.StartsWith("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase)
                        && !p.Name.StartsWith("windows.", StringComparison.OrdinalIgnoreCase)
                        && !p.Name.StartsWith("MicrosoftWindows.Client.Core", StringComparison.OrdinalIgnoreCase))
            .Select(p => new { name = p.Name, version = p.Version })
            .OrderBy(p => p.name)
            .ToList();
        return new { packages = known, removed, others };
    }

    private async Task<object> GuidesAsync()
    {
        var facts = (await ProfileAsync()).ToFacts();
        return _catalog.Guides
            .Where(g => ConditionEvaluator.Evaluate(g.When, facts))
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Icon,
                g.Summary,
                g.Sections,
                g.AutoTweaks,
                installed = g.Detect is null || ConditionEvaluator.Evaluate(g.Detect, facts),
            })
            .OrderByDescending(g => g.installed);
    }

    /// <summary>"Do you use this?" questions with a guess from the hardware scan and the tweaks that apply on this PC.</summary>
    private async Task<object> FeaturesAsync()
    {
        var facts = (await ProfileAsync()).ToFacts();
        return _catalog.Features
            .Select(f => new
            {
                f.Id,
                f.Name,
                used = f.UsedWhen is not null && ConditionEvaluator.Evaluate(f.UsedWhen, facts),
                keep = f.KeepByDefault,
                tweaks = f.Tweaks.Where(id => _catalog.Find(id) is { } t && ConditionEvaluator.Evaluate(t.When, facts)).ToList(),
            })
            .Where(f => f.tweaks.Count > 0);
    }

    /// <summary>Where the installer comes from, as shown next to the program: the vendor's site or GitHub repository.</summary>
    private static string? Source(InstallProgram p) =>
        p.Store is not null ? "Microsoft Store"
        : p.Github is not null ? "github.com/" + p.Github
        : (p.Installer ?? p.Page) is { } link && Uri.TryCreate(link, UriKind.Absolute, out var u) ? u.Host
        : p.Download is not null && Uri.TryCreate(p.Download, UriKind.Absolute, out var d) ? d.Host
        : null;

    private object Programs()
    {
        var installed = ProgramInstaller.Installed(_catalog);
        var facts = _profile?.ToFacts();
        return new
        {
            winget = ProgramInstaller.FindWinget() is not null,
            store = ProgramInstaller.StoreAvailable(),
            programs = _catalog.Programs
                .Where(x => facts is null || ConditionEvaluator.Evaluate(x.When, facts))
                .Select(x => new { x.Id, x.Name, x.Group, x.Winget, x.Url, x.Note, x.Recommended, x.Paid, x.User, direct = x.Download is not null, source = Source(x), x.Store, silent = x.Silent is not null || (x.Installer ?? "").Contains(".msi"), installed = installed.GetValueOrDefault(x.Id) }),
            extensions = _catalog.Extensions.Select(e => new { e.Id, e.Name, e.ExtensionId, e.MinVersion, e.Note, e.StoreUrl }),
        };
    }

    private async Task<object> BiosAsync()
    {
        var p = await ProfileAsync();
        return new
        {
            board = new { p.Board.Manufacturer, p.Board.Product, p.Board.BiosVersion, p.Board.BiosDate, vendor = p.Board.VendorKey },
            cpu = new { p.Cpu.Name, microcode = p.Cpu.MicrocodeRevision is { } m ? $"0x{m:X}" : null },
            ram = new { p.Memory.Type, speed = p.Memory.ConfiguredSpeed, rated = p.Memory.Modules.Count > 0 ? p.Memory.Modules.Max(x => x.Speed) : 0 },
            security = p.Security,
            advice = BiosAdvisor.Advise(_catalog, p).Select(a => new { a.Item.Id, a.Item.Title, a.Item.Detail, a.Item.Value, a.Item.Priority, a.Path, a.Status }),
        };
    }

    private static OfflineImageOptions ImageOptions(JsonElement p)
    {
        var o = new OfflineImageOptions();
        if (!p.TryGetProperty("options", out var e) || e.ValueKind != JsonValueKind.Object) return o;
        o.MaxLevel = Int(e, "level") is { } lvl ? Math.Clamp(lvl, 1, 3) : 2;
        o.ExtraApps = Ids(e, "extraApps") ?? [];
        o.RemoveApps = Bool(e, "removeApps") ?? true;
        o.RemoveLegacyComponents = Bool(e, "removeComponents") ?? true;
        o.InjectDrivers = Bool(e, "drivers") ?? false;
        o.CleanupComponents = Bool(e, "cleanup") ?? true;
        o.IntegrateUpdates = Bool(e, "updates") ?? true;
        o.ApplyLook = Bool(e, "look") ?? true;
        return o;
    }

    private object ImagePreview(JsonElement p)
    {
        var plan = OfflineImagePlan.Build(_catalog, ImageOptions(p));
        return new
        {
            tweaks = plan.TweakIds.Select(id => new { id, name = _catalog.Find(id)?.Name, category = _catalog.Find(id)?.Category }),
            registryValues = plan.Registry.Count,
            services = plan.Services.Select(s => s.Name).Distinct(),
            apps = plan.Apps,
            capabilities = plan.Capabilities,
            afterInstall = plan.Skipped.Count,
            look = plan.Options.ApplyLook,
            updates = plan.Options.IntegrateUpdates,
        };
    }

    private static async Task<object> LogoffAsync()
    {
        AppLog.Warn("system", "signing out in 5 seconds");
        await Task.Delay(5000);
        await ProcessRunner.RunAsync("shutdown.exe", "/l");
        return true;
    }

    private static async Task<object> RestartExplorer()
    {
        await TweakExecutor.RestartExplorerAsync();
        return true;
    }

    private static List<(string Host, string Label)> Targets(JsonElement p)
    {
        var list = NetworkTest.DefaultTargets.ToList();
        if (Str(p, "custom") is { Length: > 0 } custom && Uri.CheckHostName(custom.Trim()) != UriHostNameType.Unknown)
            list.Add((custom.Trim(), custom.Trim()));
        return list;
    }

    private static async Task<object> RebootAsync(bool firmware)
    {
        AppLog.Warn("system", firmware ? "rebooting into BIOS/UEFI setup in 5 seconds" : "rebooting in 5 seconds");
        var r = await ProcessRunner.RunAsync("shutdown.exe", firmware ? "/r /fw /t 5" : "/r /t 5");
        if (!r.Success) throw new InvalidOperationException((r.Output + r.Error).Trim());
        return true;
    }

    // ------------------------------------------------------------------ image

    private static object? PickGame()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Game (*.exe)|*.exe",
            Title = SettingsStore.Current.Language == "en" ? "Choose the game's .exe" : "Выберите .exe игры",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    private static object? PickIso()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Windows ISO (*.iso)|*.iso",
            Title = "Windows 11 ISO",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    private static UnattendOptions Unattend(JsonElement p)
    {
        var o = new UnattendOptions();
        if (!p.TryGetProperty("options", out var e) || e.ValueKind != JsonValueKind.Object) return o;
        o.UserName = Str(e, "userName") ?? o.UserName;
        o.Password = Str(e, "password") ?? "";
        o.ComputerName = Str(e, "computerName") ?? "";
        o.TimeZone = Str(e, "timeZone") ?? o.TimeZone;
        o.BypassRequirements = Bool(e, "bypass") ?? true;
        o.CopyTweaker = Bool(e, "copyTweaker") ?? true;
        o.RunTweakerAfterSetup = o.CopyTweaker && (Bool(e, "runAfterSetup") ?? true);
        o.DisableAutoBitLocker = Bool(e, "disableBitLocker") ?? true;
        if (Str(e, "locale") is { } loc)
        {
            o.SystemLocale = o.UserLocale = loc;
            o.InputLocale = loc == "en-US" ? "0409:00000409" : "0409:00000409;0419:00000419";
        }
        return o;
    }

    // ------------------------------------------------------------------ AI

    private async Task<object> AnalyzeAsync(string profileName)
    {
        var key = SettingsStore.GetClaudeKey() ?? throw new AiAdvisorException(SettingsStore.Current.Language == "en" ? "Add your Claude API key in AI settings first." : "Сначала добавьте API-ключ Claude в настройках AI.");
        var profile = await ProfileAsync();
        var level = Enum.TryParse<TuneProfile>(profileName, true, out var l) ? l : TuneProfile.Performance;
        var plan = _engine.BuildPlan(profile, level);
        AppLog.Info("ai", $"asking {SettingsStore.Current.ClaudeModel} to analyze this PC…");
        var analysis = await new ClaudeAdvisor(key, SettingsStore.Current.ClaudeModel).AnalyzeAsync(profile, plan, _insights, SettingsStore.Current.Language);
        AppLog.Ok("ai", $"Claude recommends {analysis.Recommendations.Count} tweaks, advises against {analysis.Avoid.Count}");
        return analysis;
    }

    // ------------------------------------------------------------------ settings / report

    private static object SetSettings(JsonElement p)
    {
        var s = SettingsStore.Current;
        if (Str(p, "language") is { } lang) s.Language = lang == "en" ? "en" : "ru";
        if (Str(p, "theme") is { } theme) s.Theme = theme is "night" or "day" ? theme : "auto";
        if (Str(p, "accent") is { } accent) s.Accent = accent is "mono" or "system" ? accent : "industrial";
        if (Bool(p, "showAdvisor") is { } adv) s.ShowAdvisor = adv;
        if (Str(p, "profile") is { } prof && Enum.TryParse<TuneProfile>(prof, true, out _)) s.Profile = prof;
        if (Str(p, "model") is { } model && ClaudeAdvisor.Models.Contains(model)) s.ClaudeModel = model;
        if (Str(p, "pingHost") is { Length: > 0 } host) s.PingHost = host;
        if (Bool(p, "createRestorePoint") is { } rp) s.CreateRestorePoint = rp;
        if (Int(p, "autoRestartSeconds") is { } ar) s.AutoRestartSeconds = Math.Clamp(ar, 0, 600);
        SettingsStore.Save();
        if (p.TryGetProperty("apiKey", out var k)) SettingsStore.SetClaudeKey(k.ValueKind == JsonValueKind.String ? k.GetString() : null);
        return new { hasKey = SettingsStore.GetClaudeKey() is not null };
    }

    private async Task<object> ExportReportAsync()
    {
        var profile = await ProfileAsync();
        var status = await new TweakExecutor(profile).StatusAsync(_catalog.Tweaks);
        var path = Path.Combine(AppPaths.Desktop, $"AI-Tweaker-report-{DateTime.Now:yyyyMMdd-HHmm}.html");
        await File.WriteAllTextAsync(path, ReportBuilder.Build(profile, _insights, _catalog, status, SettingsStore.Current.Language));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        AppLog.Ok("report", $"report saved to {path}");
        return path;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<T> Exclusive<T>(Func<Task<T>> work)
    {
        if (!await _busy.WaitAsync(0)) throw new InvalidOperationException(SettingsStore.Current.Language == "en" ? "Another operation is still running." : "Уже выполняется другая операция.");
        try { return await work(); }
        finally { _busy.Release(); }
    }

    private CancellationToken NewLongTask()
    {
        _longTask?.Dispose();
        _longTask = new CancellationTokenSource();
        return _longTask.Token;
    }

    private static object? Do(Action a)
    {
        a();
        return true;
    }

    private static string? Str(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static int? Int(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static List<string>? Ids(JsonElement p, string name = "ids") =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : null;
}
