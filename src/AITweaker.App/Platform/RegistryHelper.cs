using System.Text.Json;
using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform;

/// <summary>Registry plumbing: hive parsing, wildcard expansion, typed values and string snapshots for backups.</summary>
public static class RegistryHelper
{
    // Hive roots are opened once (64-bit view) and never disposed.
    private static readonly RegistryKey Hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    private static readonly RegistryKey Hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
    private static readonly RegistryKey Hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
    private static readonly RegistryKey Hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);

    public static (RegistryKey Root, string SubPath) Split(string path)
    {
        int i = path.IndexOf('\\');
        string hive = i < 0 ? path : path[..i];
        string sub = i < 0 ? "" : path[(i + 1)..];
        RegistryKey root = hive.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Hklm,
            "HKCU" or "HKEY_CURRENT_USER" => Hkcu,
            "HKU" or "HKEY_USERS" => Hku,
            "HKCR" or "HKEY_CLASSES_ROOT" => Hkcr,
            _ => throw new ArgumentException($"Unknown registry hive in '{path}'"),
        };
        return (root, sub);
    }

    public static bool KeyExists(string path)
    {
        var (root, sub) = Split(path);
        if (sub.Length == 0) return true;
        try
        {
            using var k = root.OpenSubKey(sub);
            return k is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true; // exists but is not readable
        }
    }

    /// <summary>Expands '*' segments into every existing matching key. Paths without '*' are returned as-is.</summary>
    public static IReadOnlyList<string> Expand(string path)
    {
        if (!path.Contains('*')) return [path];
        var parts = path.Split('\\');
        var results = new List<string> { parts[0] };
        for (int i = 1; i < parts.Length; i++)
        {
            var next = new List<string>();
            foreach (var prefix in results)
            {
                if (parts[i] == "*")
                {
                    var (root, sub) = Split(prefix);
                    try
                    {
                        if (sub.Length == 0)
                        {
                            next.AddRange(root.GetSubKeyNames().Select(n => prefix + "\\" + n));
                            continue;
                        }
                        using var k = root.OpenSubKey(sub);
                        if (k is null) continue;
                        next.AddRange(k.GetSubKeyNames().Select(n => prefix + "\\" + n));
                    }
                    catch (System.Security.SecurityException) { }
                    catch (UnauthorizedAccessException) { }
                }
                else
                {
                    next.Add(prefix + "\\" + parts[i]);
                }
            }
            results = next;
        }
        return results.Where(KeyExists).ToList();
    }

    /// <summary>Returns the top-most ancestor of <paramref name="path"/> that does not exist yet (to delete it on rollback), or null.</summary>
    public static string? FirstMissingAncestor(string path)
    {
        var parts = path.Split('\\');
        for (int i = 2; i <= parts.Length; i++)
        {
            var candidate = string.Join('\\', parts.Take(i));
            if (!KeyExists(candidate)) return candidate;
        }
        return null;
    }

    public static RegistryValueKind ToWin32(RegistryValueKind2 kind) => kind switch
    {
        RegistryValueKind2.Dword => RegistryValueKind.DWord,
        RegistryValueKind2.Qword => RegistryValueKind.QWord,
        RegistryValueKind2.String => RegistryValueKind.String,
        RegistryValueKind2.Expand => RegistryValueKind.ExpandString,
        RegistryValueKind2.Multi => RegistryValueKind.MultiString,
        RegistryValueKind2.Binary => RegistryValueKind.Binary,
        _ => RegistryValueKind.String,
    };

    /// <summary>Converts a catalog JSON value into the CLR object RegistryKey.SetValue expects.</summary>
    public static object ToRegistryObject(JsonElement v, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)(uint)v.GetInt64()),
        RegistryValueKind.QWord => v.GetInt64(),
        RegistryValueKind.MultiString => v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(e => e.GetString() ?? "").ToArray() : new[] { v.GetString() ?? "" },
        RegistryValueKind.Binary => v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray() : Convert.FromBase64String(v.GetString() ?? ""),
        _ => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString(),
    };

    public sealed record ValueSnapshot(bool Exists, RegistryValueKind Kind, string? Data);

    public static ValueSnapshot Read(string path, string name)
    {
        var (root, sub) = Split(path);
        using var k = root.OpenSubKey(sub);
        if (k is null) return new(false, RegistryValueKind.Unknown, null);
        var value = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) return new(false, RegistryValueKind.Unknown, null);
        var kind = k.GetValueKind(name);
        return new(true, kind, Serialize(value, kind));
    }

    public static void Write(string path, string name, object value, RegistryValueKind kind)
    {
        var (root, sub) = Split(path);
        using var k = root.CreateSubKey(sub, writable: true) ?? throw new InvalidOperationException($"Cannot open {path}");
        k.SetValue(name, value, kind);
    }

    public static void WriteSnapshot(string path, string name, ValueSnapshot snap)
    {
        if (!snap.Exists)
        {
            DeleteValue(path, name);
            return;
        }
        Write(path, name, Deserialize(snap.Data, snap.Kind), snap.Kind);
    }

    public static void DeleteValue(string path, string name)
    {
        var (root, sub) = Split(path);
        using var k = root.OpenSubKey(sub, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public static void DeleteKeyTree(string path)
    {
        var (root, sub) = Split(path);
        if (sub.Count(c => c == '\\') < 1) throw new InvalidOperationException($"Refusing to delete a top-level key: {path}");
        root.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
    }

    public static string Serialize(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => unchecked((uint)(int)value).ToString(),
        RegistryValueKind.QWord => ((long)value).ToString(),
        RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])value),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
        _ => value.ToString() ?? "",
    };

    public static object Deserialize(string? data, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)uint.Parse(data ?? "0")),
        RegistryValueKind.QWord => long.Parse(data ?? "0"),
        RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(data ?? "[]") ?? [],
        RegistryValueKind.Binary => Convert.FromBase64String(data ?? ""),
        _ => data ?? "",
    };

    /// <summary>True if the current value equals the catalog target value.</summary>
    public static bool Matches(ValueSnapshot current, JsonElement target, RegistryValueKind kind)
    {
        if (!current.Exists) return false;
        var expected = Serialize(ToRegistryObject(target, kind), kind);
        return kind is RegistryValueKind.String or RegistryValueKind.ExpandString
            ? string.Equals(current.Data, expected, StringComparison.OrdinalIgnoreCase)
            : current.Data == expected;
    }
}
