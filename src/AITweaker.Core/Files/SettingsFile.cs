using System.Text.Json;
using System.Text.Json.Nodes;

namespace AITweaker.Core.Files;

/// <summary>Reads and edits a program's settings file: a JSON object, or "key=value" lines like Spotify's prefs.</summary>
public static class SettingsFile
{
    /// <summary>
    /// Sets every key to its raw value (a JSON literal; null removes the key) and fills <paramref name="before"/>
    /// with the raw values it replaced, null where the key was absent — feeding that back restores the file.
    /// </summary>
    public static string Edit(string text, string format, IReadOnlyDictionary<string, string?> values, Dictionary<string, string?> before)
    {
        if (format == "prefs")
        {
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            foreach (var (key, raw) in values)
            {
                int i = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
                before[key] = i >= 0 ? lines[i][(key.Length + 1)..] : null;
                if (raw is null) { if (i >= 0) lines.RemoveAt(i); }
                else if (i >= 0) lines[i] = key + "=" + raw;
                else lines.Add(key + "=" + raw);
            }
            return string.Join("\n", lines) + "\n";
        }
        var obj = JsonNode.Parse(text.Trim().Length == 0 ? "{}" : text) as JsonObject ?? throw new InvalidDataException("settings file is not a JSON object");
        foreach (var (key, raw) in values)
        {
            before[key] = obj.TryGetPropertyValue(key, out var old) ? old?.ToJsonString() ?? "null" : null;
            if (raw is null) obj.Remove(key);
            else obj[key] = JsonNode.Parse(raw);
        }
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Raw current values of the keys (null when absent).</summary>
    public static Dictionary<string, string?> Read(string text, string format, IEnumerable<string> keys)
    {
        var result = new Dictionary<string, string?>();
        if (format == "prefs")
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (var key in keys)
                result[key] = lines.FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..];
            return result;
        }
        var obj = JsonNode.Parse(text.Trim().Length == 0 ? "{}" : text) as JsonObject;
        foreach (var key in keys)
            result[key] = obj is not null && obj.TryGetPropertyValue(key, out var v) ? v?.ToJsonString() ?? "null" : null;
        return result;
    }

    /// <summary>True when every key already holds the wanted value.</summary>
    public static bool Holds(string text, string format, IReadOnlyDictionary<string, string> wanted)
    {
        var current = Read(text, format, wanted.Keys);
        return wanted.All(kv => current[kv.Key] is { } v
            && (format == "prefs" ? v.Trim() == kv.Value.Trim() : JsonNode.DeepEquals(JsonNode.Parse(v), JsonNode.Parse(kv.Value))));
    }
}
