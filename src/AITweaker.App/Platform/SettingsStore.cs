using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AITweaker.App.Platform;

public sealed class AppSettings
{
    public string Language { get; set; } = "ru";
    /// <summary>auto (follow Windows) | day | night</summary>
    public string Theme { get; set; } = "auto";
    /// <summary>system (Windows accent color) | industrial (safety orange / amber)</summary>
    public string Accent { get; set; } = "system";
    public string Profile { get; set; } = "Performance";
    public string ClaudeModel { get; set; } = AITweaker.Core.Ai.ClaudeAdvisor.DefaultModel;
    /// <summary>API key encrypted with DPAPI (current user), base64.</summary>
    public string? ClaudeKeyProtected { get; set; }
    public string PingHost { get; set; } = "1.1.1.1";
    public string? UltimateSchemeGuid { get; set; }
    public bool CreateRestorePoint { get; set; } = true;
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

    public static string? GetClaudeKey()
    {
        var p = Current.ClaudeKeyProtected;
        if (string.IsNullOrEmpty(p)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(p), null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null;
        }
    }

    public static void SetClaudeKey(string? key)
    {
        Current.ClaudeKeyProtected = string.IsNullOrWhiteSpace(key)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
        Save();
    }
}
