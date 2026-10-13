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
        var path = Resolve(a.Path);
        bool create = !File.Exists(path) && a.Create && Directory.Exists(Path.GetDirectoryName(path));
        if (!File.Exists(path) && !create)
        {
            ctx.Log($"skip {path} (not found — the program isn't set up)");
            return;
        }
        if (a.Process is not null && a.IfRunning == "fail" && Process.GetProcessesByName(a.Process).Length > 0)
            throw new InvalidOperationException($"{a.Process} is open — close it and apply again (it may be streaming or recording)");
        var restart = a.Process is null ? null : await CloseAsync(a.Process);
        try
        {
            var text = create ? "" : await File.ReadAllTextAsync(path, ctx.Ct);
            var before = new Dictionary<string, string?>();
            var updated = SettingsFile.Edit(text, a.Format, a.Set.ToDictionary(kv => kv.Key, kv => (string?)kv.Value.GetRawText()), before);
            ctx.Record(ChangeRecord.Of("file", ("path", path), ("format", a.Format), ("process", a.Process), ("before", JsonSerializer.Serialize(before)), ("created", create ? "1" : null)));
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
            if (r["created"] == "1")
            {
                File.Delete(path); // the program writes its defaults again
                return;
            }
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
        var path = Resolve(a.Path);
        if (!File.Exists(path)) return null;
        return SettingsFile.Holds(File.ReadAllText(path), a.Format, a.Set.ToDictionary(kv => kv.Key, kv => kv.Value.GetRawText()));
    }

    /// <summary>Expands %VARS% and {obs-profile}; of "a|b" takes the first file that exists (or the first one).</summary>
    public static string Resolve(string path)
    {
        var options = path.Split('|').Select(p => Environment.ExpandEnvironmentVariables(p.Trim()))
            .Select(p => p.Contains("{obs-profile}") && ObsProfileDir() is { } dir ? p.Replace("{obs-profile}", dir) : p)
            .ToList();
        return options.FirstOrDefault(File.Exists) ?? options[0];
    }

    /// <summary>OBS's active profile folder name: [Basic] ProfileDir in user.ini (OBS 31+) or global.ini, else the only profile there is.</summary>
    private static string? ObsProfileDir()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");
        foreach (var name in new[] { "user.ini", "global.ini" })
        {
            var file = Path.Combine(root, name);
            if (!File.Exists(file)) continue;
            if (SettingsFile.Read(File.ReadAllText(file), "ini", ["Basic/ProfileDir"])["Basic/ProfileDir"] is { } raw
                && JsonSerializer.Deserialize<string>(raw) is { Length: > 0 } dir)
                return dir;
        }
        var profiles = Path.Combine(root, "basic", "profiles");
        return Directory.Exists(profiles) && Directory.GetDirectories(profiles) is [var only] ? Path.GetFileName(only) : null;
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
