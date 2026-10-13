using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AITweaker.Core.Files;
using AITweaker.Core.Model;

namespace AITweaker.App.Platform.Actions;

/// <summary>Edits a program's own settings file (JSON or key=value lines), keeping the previous values for rollback.</summary>
public static class FileSettingOps
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task ApplyAsync(FileSettingAction a, ExecContext ctx)
    {
        var path = Environment.ExpandEnvironmentVariables(a.Path);
        if (!File.Exists(path))
        {
            ctx.Log($"skip {path} (not found — the program isn't set up)");
            return;
        }
        var restart = a.Process is null ? null : await CloseAsync(a.Process);
        try
        {
            var text = await File.ReadAllTextAsync(path, ctx.Ct);
            var before = new Dictionary<string, string?>();
            var updated = SettingsFile.Edit(text, a.Format, a.Set.ToDictionary(kv => kv.Key, kv => (string?)kv.Value.GetRawText()), before);
            ctx.Record(ChangeRecord.Of("file", ("path", path), ("format", a.Format), ("process", a.Process), ("before", JsonSerializer.Serialize(before))));
            await File.WriteAllTextAsync(path, updated, Utf8, ctx.Ct);
            ctx.Log($"{path}: {string.Join(", ", a.Set.Keys)}");
        }
        finally
        {
            Relaunch(restart);
        }
    }

    public static async Task RevertAsync(ChangeRecord r)
    {
        var path = r["path"]!;
        if (!File.Exists(path)) return;
        var restart = r["process"] is { Length: > 0 } proc ? await CloseAsync(proc) : null;
        try
        {
            var before = JsonSerializer.Deserialize<Dictionary<string, string?>>(r["before"] ?? "{}") ?? [];
            var text = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, SettingsFile.Edit(text, r["format"] ?? "json", before, []), Utf8);
        }
        finally
        {
            Relaunch(restart);
        }
    }

    public static bool? Check(FileSettingAction a)
    {
        var path = Environment.ExpandEnvironmentVariables(a.Path);
        if (!File.Exists(path)) return null;
        return SettingsFile.Holds(File.ReadAllText(path), a.Format, a.Set.ToDictionary(kv => kv.Key, kv => kv.Value.GetRawText()));
    }

    /// <summary>Closes every process with that name; returns the executable path to start again, or null if it wasn't running.</summary>
    private static async Task<string?> CloseAsync(string name)
    {
        var procs = Process.GetProcessesByName(name);
        if (procs.Length == 0) return null;
        string? exe = null;
        foreach (var p in procs)
        {
            try
            {
                exe ??= p.MainModule?.FileName;
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException or NotSupportedException)
            {
                AppLog.Write("warn", "file", $"{name}: {ex.Message}");
            }
            finally { p.Dispose(); }
        }
        return exe ?? "";
    }

    /// <summary>Starts the program again through Explorer, so it runs with the user's normal rights rather than elevated.</summary>
    private static void Relaunch(string? exe)
    {
        if (string.IsNullOrEmpty(exe) || exe.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = false }); }
        catch (System.ComponentModel.Win32Exception ex) { AppLog.Write("warn", "file", $"{exe}: {ex.Message}"); }
    }
}
