using System.Diagnostics;
using AITweaker.App.Platform.Actions;
using AITweaker.Core.Model;
using AITweaker.Core.Profile;

namespace AITweaker.App.Platform;

public sealed record TweakResult(string Id, bool Ok, string? Error);

public sealed record ApplySummary(
    IReadOnlyList<TweakResult> Results,
    bool RestorePointCreated,
    bool RebootRequired,
    bool LogoffRequired);

/// <summary>Applies, reverts and inspects catalog tweaks on the live system.</summary>
public sealed class TweakExecutor(SystemProfile profile)
{
    public event Action<int, int, string>? Progress;

    public async Task<ApplySummary> ApplyAsync(IReadOnlyList<TweakDefinition> tweaks, bool createRestorePoint, string profileName, CancellationToken ct = default)
    {
        bool rp = false;
        if (createRestorePoint)
        {
            Progress?.Invoke(0, tweaks.Count, "restore-point");
            rp = await RestorePoint.CreateAsync("AI Tweaker — before optimization");
        }

        var ctx = new ExecContext(profile, ct);
        var results = new List<TweakResult>();
        bool restartExplorer = false, reboot = false, logoff = false;

        for (int i = 0; i < tweaks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = tweaks[i];
            Progress?.Invoke(i + 1, tweaks.Count, t.Id);
            var r = await ApplyOneAsync(t, ctx);
            results.Add(r);
            if (r.Ok)
            {
                restartExplorer |= t.RestartExplorer;
                reboot |= t.Reboot;
                logoff |= t.Logoff;
            }
        }

        if (ctx.AdaptersToRestart.Count > 0) await NicOps.RestartAdaptersAsync(ctx.AdaptersToRestart);
        Native.BroadcastSettingChange("ImmersiveColorSet", "WindowsThemeElement", "Policy");
        if (restartExplorer) await RestartExplorerAsync();

        BackupStore.AddSession(new SessionLog
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Time = DateTime.Now,
            Kind = "apply",
            Profile = profileName,
            Applied = results.Where(r => r.Ok).Select(r => r.Id).ToList(),
            Failed = results.Where(r => !r.Ok).Select(r => r.Id).ToList(),
            RestorePoint = rp,
        });
        return new ApplySummary(results, rp, reboot, logoff);
    }

    private static async Task<TweakResult> ApplyOneAsync(TweakDefinition t, ExecContext ctx)
    {
        ctx.CurrentTweak = t.Id;
        ctx.Changes.Clear();
        ctx.Capture = true; // BackupStore keeps only the first (original) snapshot of every item
        var errors = new List<string>();
        var sw = Stopwatch.StartNew();
        AppLog.Info(t.Id, $"applying: {t.Name.En}");

        foreach (var action in t.Actions)
        {
            if (!ctx.Matches(action.When)) continue;
            try
            {
                await ApplyActionAsync(action, ctx);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                AppLog.Error(t.Id, $"{action.Describe()} failed: {ex.Message}");
            }
        }

        BackupStore.Save(t.Id, ctx.Changes.ToList());

        if (errors.Count == 0)
        {
            AppLog.Ok(t.Id, $"done in {sw.ElapsedMilliseconds} ms");
            return new TweakResult(t.Id, true, null);
        }
        return new TweakResult(t.Id, false, string.Join("; ", errors));
    }

    private static Task ApplyActionAsync(TweakAction action, ExecContext ctx) => action switch
    {
        RegistryAction a => RegistryOps.ApplyAsync(a, ctx),
        ServiceAction a => ServiceOps.ApplyAsync(a, ctx),
        ScheduledTaskAction a => TaskOps.ApplyAsync(a, ctx),
        PowerSettingAction a => PowerOps.ApplySettingAsync(a, ctx),
        PowerSchemeAction a => PowerOps.ApplySchemeAsync(a, ctx),
        BcdAction a => BcdOps.ApplyAsync(a, ctx),
        CommandAction a => CommandOps.ApplyCommandAsync(a, ctx),
        PowerShellAction a => CommandOps.ApplyPowerShellAsync(a, ctx),
        DeviceAction a => DeviceOps.ApplyAsync(a, ctx),
        NicPropertyAction a => NicOps.ApplyAsync(a, ctx),
        MsiModeAction a => InterruptOps.ApplyMsiAsync(a, ctx),
        InterruptAffinityAction a => InterruptOps.ApplyAffinityAsync(a, ctx),
        DirectXSettingAction a => DirectXOps.ApplyAsync(a, ctx),
        AppxRemoveAction a => AppxOps.ApplyAsync(a, ctx),
        DisplayRefreshAction a => DisplayOps.ApplyAsync(a, ctx),
        WallpaperAction a => WallpaperOps.ApplyAsync(a, ctx),
        NvidiaProfileAction a => NvidiaOps.ApplyAsync(a, ctx),
        _ => throw new NotSupportedException(action.GetType().Name),
    };

    // ------------------------------------------------------------------ revert

    public async Task<IReadOnlyList<TweakResult>> RevertAsync(IReadOnlyList<TweakDefinition> tweaks)
    {
        var results = new List<TweakResult>();
        bool restartExplorer = false;
        var adapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tweaks)
        {
            var backup = BackupStore.Get(t.Id);
            if (backup is null)
            {
                results.Add(new TweakResult(t.Id, false, "no backup"));
                continue;
            }
            var errors = new List<string>();
            foreach (var change in Enumerable.Reverse(backup.Changes))
            {
                try
                {
                    await RevertChangeAsync(change);
                    if (change.Kind == "registry" && change["path"]?.Contains(@"\Class\{4d36e972", StringComparison.OrdinalIgnoreCase) == true)
                        foreach (var nic in profile.Network.Where(n => NicOps.ClassKeyFor(n.Guid) is { } k && change["path"]!.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
                            adapters.Add(nic.Guid);
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                    AppLog.Error(t.Id, $"rollback of {change.Kind} failed: {ex.Message}");
                }
            }
            if (errors.Count == 0)
            {
                BackupStore.Remove(t.Id);
                AppLog.Ok(t.Id, "rolled back");
            }
            restartExplorer |= t.RestartExplorer;
            results.Add(new TweakResult(t.Id, errors.Count == 0, errors.Count == 0 ? null : string.Join("; ", errors)));
        }
        if (adapters.Count > 0) await NicOps.RestartAdaptersAsync(adapters);
        Native.BroadcastSettingChange("ImmersiveColorSet", "WindowsThemeElement", "Policy");
        if (restartExplorer) await RestartExplorerAsync();
        BackupStore.AddSession(new SessionLog
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Time = DateTime.Now,
            Kind = "revert",
            Applied = results.Where(r => r.Ok).Select(r => r.Id).ToList(),
            Failed = results.Where(r => !r.Ok).Select(r => r.Id).ToList(),
        });
        return results;
    }

    private static Task RevertChangeAsync(ChangeRecord r) => r.Kind switch
    {
        "registry" => RegistryOps.RevertAsync(r),
        "service" => ServiceOps.RevertAsync(r),
        "task" => TaskOps.RevertAsync(r),
        "power-setting" => PowerOps.RevertSettingAsync(r),
        "power-scheme" => PowerOps.RevertSchemeAsync(r),
        "bcd" => BcdOps.RevertAsync(r),
        "undo-command" or "undo-powershell" => CommandOps.RevertAsync(r),
        "device" => DeviceOps.RevertAsync(r),
        "display" => DisplayOps.RevertAsync(r),
        "wallpaper" => WallpaperOps.RevertAsync(r),
        "nvidia" => NvidiaOps.RevertAsync(r),
        _ => Task.CompletedTask, // "info": irreversible (removed apps)
    };

    // ------------------------------------------------------------------ status

    /// <summary>applied | partial | off | n/a</summary>
    public async Task<Dictionary<string, string>> StatusAsync(IReadOnlyList<TweakDefinition> tweaks)
    {
        var ctx = new ExecContext(profile);
        var status = new Dictionary<string, string>();
        foreach (var t in tweaks)
        {
            int yes = 0, no = 0;
            foreach (var action in t.Actions.Where(a => ctx.Matches(a.When)))
            {
                bool? applied;
                try
                {
                    applied = await CheckActionAsync(action, ctx);
                }
                catch
                {
                    applied = null;
                }
                applied ??= action is CommandAction or PowerShellAction or AppxRemoveAction or NvidiaProfileAction ? BackupStore.Has(t.Id) : null;
                if (applied == true) yes++;
                else if (applied == false) no++;
            }
            status[t.Id] = (yes, no) switch
            {
                (0, 0) => BackupStore.Has(t.Id) ? "applied" : "n/a",
                (_, 0) => "applied",
                (0, _) => "off",
                _ => "partial",
            };
        }
        return status;
    }

    private static async Task<bool?> CheckActionAsync(TweakAction action, ExecContext ctx) => action switch
    {
        RegistryAction a => RegistryOps.Check(a),
        ServiceAction a => ServiceOps.Check(a),
        ScheduledTaskAction a => TaskOps.Check(a),
        PowerSettingAction a => PowerOps.CheckSetting(a),
        PowerSchemeAction a => PowerOps.CheckScheme(a),
        BcdAction a => await BcdOps.CheckAsync(a, ctx),
        PowerShellAction a => await CommandOps.CheckPowerShellAsync(a),
        DeviceAction a => DeviceOps.Check(a),
        NicPropertyAction a => NicOps.Check(a, ctx),
        MsiModeAction a => InterruptOps.CheckMsi(a, ctx),
        InterruptAffinityAction a => InterruptOps.CheckAffinity(a, ctx),
        DirectXSettingAction a => DirectXOps.Check(a),
        DisplayRefreshAction => DisplayOps.Check(),
        WallpaperAction a => WallpaperOps.Check(a),
        _ => null,
    };

    // ------------------------------------------------------------------ helpers

    /// <summary>Kills Explorer and lets winlogon restart it un-elevated (AutoRestartShell); starts it manually only if it doesn't come back.</summary>
    public static async Task RestartExplorerAsync()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { /* access denied for other sessions */ }
        }
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(250);
            if (Process.GetProcessesByName("explorer").Length > 0) return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }
}
