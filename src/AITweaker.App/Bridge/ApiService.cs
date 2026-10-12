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

    public ApiService(Func<Func<object?>, Task<object?>> onUi, Action<string, object?> emit)
    {
        _engine = new RecommendationEngine(_catalog);
        _onUi = onUi;
        _emit = emit;
    }

    public async Task<object?> InvokeAsync(string method, JsonElement p) => method switch
    {
        "app.init" => Init(),
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
        "appx.remove" => await Exclusive(() => Platform.Actions.AppxOps.RemoveAsync(Ids(p, "names") ?? [], n => _emit("appx.progress", n))),
        "drivers.list" => DriverCenter.Build(await ProfileAsync()),
        "drivers.rescan" => await DriverCenter.RescanAsync(),
        "guides.list" => await GuidesAsync(),
        "bios.list" => await BiosAsync(),
        "bios.reboot" => await RebootAsync(firmware: true),
        "system.reboot" => await RebootAsync(firmware: false),
        "ame.run" => await Exclusive(() => AmeLauncher.RunAsync(Str(p, "playbook") ?? "atlas", (s, v) => _emit("ame.progress", new { step = s, value = v }), NewLongTask())),
        "usb.list" => await UsbCreator.ListAsync(),
        "usb.pickIso" => await _onUi(PickIso),
        "usb.downloadIso" => await Exclusive(() => UsbCreator.DownloadIsoAsync(Str(p, "language") ?? "Russian", (s, v) => _emit("usb.progress", new { step = s, value = v }), NewLongTask())),
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
        "shell.folder" => Do(() => ProcessRunner.OpenFolder(Str(p, "name") switch { "backups" => AppPaths.Backups, "downloads" => AppPaths.Downloads, _ => AppPaths.Logs })),
        "window.theme" => Do(() => ThemeChanged?.Invoke(Str(p, "theme") ?? "day")),
        _ => throw new InvalidOperationException($"Unknown method {method}"),
    };

    // ------------------------------------------------------------------ app & scan

    private object Init()
    {
        var s = SettingsStore.Current;
        return new
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
            isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),
            machine = Environment.MachineName,
            settings = new
            {
                language = s.Language,
                theme = s.Theme,
                profile = s.Profile,
                model = s.ClaudeModel,
                hasKey = SettingsStore.GetClaudeKey() is not null,
                pingHost = s.PingHost,
                createRestorePoint = s.CreateRestorePoint,
            },
            models = ClaudeAdvisor.Models,
            categories = _catalog.Categories,
            log = AppLog.Snapshot().TakeLast(100),
        };
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
            return summary;
        });
    }

    private async Task<object> RevertAsync(List<string> ids)
    {
        var profile = await ProfileAsync();
        var tweaks = ids.Select(_catalog.Find).OfType<TweakDefinition>().ToList();
        return await Exclusive(() => new TweakExecutor(profile).RevertAsync(tweaks));
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
        }).Where(a => a.installed).ToList();

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
        return new { packages = known, others };
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
                g.Source,
                g.Summary,
                g.Sections,
                g.AutoTweaks,
                installed = g.Detect is null || ConditionEvaluator.Evaluate(g.Detect, facts),
            })
            .OrderByDescending(g => g.installed);
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

    private static async Task<object> RebootAsync(bool firmware)
    {
        AppLog.Warn("system", firmware ? "rebooting into BIOS/UEFI setup in 5 seconds" : "rebooting in 5 seconds");
        var r = await ProcessRunner.RunAsync("shutdown.exe", firmware ? "/r /fw /t 5" : "/r /t 5");
        if (!r.Success) throw new InvalidOperationException((r.Output + r.Error).Trim());
        return true;
    }

    // ------------------------------------------------------------------ image

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
        if (Str(p, "theme") is { } theme) s.Theme = theme == "night" ? "night" : "day";
        if (Str(p, "profile") is { } prof && Enum.TryParse<TuneProfile>(prof, true, out _)) s.Profile = prof;
        if (Str(p, "model") is { } model && ClaudeAdvisor.Models.Contains(model)) s.ClaudeModel = model;
        if (Str(p, "pingHost") is { Length: > 0 } host) s.PingHost = host;
        if (Bool(p, "createRestorePoint") is { } rp) s.CreateRestorePoint = rp;
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
