using System.Text;
using System.Text.Json;

namespace AITweaker.App.Platform;

public sealed class AppSettings
{
    public string Language { get; set; } = "ru";
    /// <summary>auto (follow Windows) | day | night</summary>
    public string Theme { get; set; } = "auto";
    /// <summary>industrial (signal orange) | system (Windows accent color) | mono (monochrome)</summary>
    public string Accent { get; set; } = "industrial";
    public string Profile { get; set; } = "Extreme";
    public string PingHost { get; set; } = "1.1.1.1";
    public string? UltimateSchemeGuid { get; set; }
    /// <summary>The "AI Tweaker" power scheme created by the power plan tweak, and what it was copied from.</summary>
    public string? OwnSchemeGuid { get; set; }
    public string? OwnSchemeBase { get; set; }
    public bool CreateRestorePoint { get; set; } = true;
    /// <summary>When changes that need a reboot were made; cleared once Windows has booted after that.</summary>
    public DateTime? RebootPendingSince { get; set; }
    /// <summary>Seconds of the automatic restart countdown (0 = never restart automatically).</summary>
    public int AutoRestartSeconds { get; set; } = 60;
    /// <summary>The post-install welcome (scan → auto-tune → look → restart) was completed or dismissed for good.</summary>
    public bool FirstRunDone { get; set; }
    /// <summary>Look for a new AI Tweaker release on every start.</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Turn Game Mode on by itself while a known game runs.</summary>
    public bool AutoGameMode { get; set; }
    /// <summary>What automatic Game Mode turns on: the choices from the last time the user started it.</summary>
    public List<string>? AutoGameGroups { get; set; }
    /// <summary>Build stamp the WebView2 disk cache belongs to.</summary>
    public string? WebCacheStamp { get; set; }
}

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.User, "settings.json");
    private static readonly object Gate = new();
    private static AppSettings? _current;

    public static AppSettings Current
    {
        get
        {
            lock (Gate)
            {
                if (_current is not null) return _current;
                try
                {
                    _current = File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) : null;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("settings", "settings.json is corrupt, using defaults: " + ex.Message);
                }
                return _current ??= new AppSettings();
            }
        }
    }

    public static void Save()
    {
        lock (Gate)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>True while changes made by AI Tweaker still wait for a reboot.</summary>
    public static bool RebootPending
    {
        get
        {
            var since = Current.RebootPendingSince;
            if (since is null) return false;
            var boot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (since > boot) return true;
            Current.RebootPendingSince = null; // Windows restarted since then
            Save();
            return false;
        }
    }

    public static void MarkRebootPending()
    {
        Current.RebootPendingSince = DateTime.Now;
        Save();
    }

}
