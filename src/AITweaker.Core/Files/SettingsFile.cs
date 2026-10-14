using System.Text.Json;
using System.Text.Json.Nodes;

namespace AITweaker.Core.Files;

/// <summary>
/// Reads and edits a program's settings file: a JSON object, "key=value" lines like Spotify's prefs,
/// or an INI file like OBS's, where keys are written "Section/Key".
/// </summary>
public static class SettingsFile
{
    /// <summary>
    /// Sets every key to its raw value (a JSON literal; null removes the key) and fills <paramref name="before"/>
    /// with the raw values it replaced, null where the key was absent — feeding that back restores the file.
    /// </summary>
    public static string Edit(string text, string format, IReadOnlyDictionary<string, string?> values, Dictionary<string, string?> before)
    {
        if (format == "ini") return EditIni(text, values, before);
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
        if (format == "ini")
        {
            var lines = Lines(text);
            foreach (var key in keys)
            {
                var (section, name) = Split(key);
                int i = FindKey(lines, section, name);
                result[key] = i >= 0 ? JsonSerializer.Serialize(ValueOf(lines[i])) : null;
            }
            return result;
        }
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
        return wanted.All(kv => current[kv.Key] is { } v && format switch
        {
            "prefs" => v.Trim() == kv.Value.Trim(),
            // INI holds text: "true" and "True" are the same value
            "ini" => string.Equals(IniText(v).Trim(), IniText(kv.Value).Trim(), StringComparison.OrdinalIgnoreCase),
            _ => JsonNode.DeepEquals(JsonNode.Parse(v), JsonNode.Parse(kv.Value)),
        });
    }

    // ------------------------------------------------------------------ INI

    /// <summary>
    /// Values come in as JSON literals: a string is written as its text, true/false and numbers as they are.
    /// What it replaced goes into <paramref name="before"/> as a JSON string, so feeding it back writes the exact old text.
    /// Missing keys are added at the end of their section; a missing section is added at the end of the file.
    /// </summary>
    private static string EditIni(string text, IReadOnlyDictionary<string, string?> values, Dictionary<string, string?> before)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = Lines(text);
        foreach (var (key, raw) in values)
        {
            var (section, name) = Split(key);
            int i = FindKey(lines, section, name);
            before[key] = i >= 0 ? JsonSerializer.Serialize(ValueOf(lines[i])) : null;
            if (raw is null)
            {
                if (i >= 0) lines.RemoveAt(i);
                continue;
            }
            var line = name + "=" + IniText(raw);
            if (i >= 0) { lines[i] = line; continue; }
            var (start, end) = FindSection(lines, section);
            if (start < 0)
            {
                if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(line);
                continue;
            }
            int at = end;
            while (at > start + 1 && lines[at - 1].Trim().Length == 0) at--;
            lines.Insert(at, line);
        }
        return string.Join(newline, lines) + newline;
    }

    private static List<string> Lines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static (string Section, string Name) Split(string key)
    {
        int slash = key.IndexOf('/');
        if (slash <= 0) throw new ArgumentException($"INI key '{key}' must be written Section/Key");
        return (key[..slash], key[(slash + 1)..]);
    }

    /// <summary>Header line index and the index just past the section's last line; (-1, -1) when absent.</summary>
    private static (int Start, int End) FindSection(List<string> lines, string section)
    {
        int start = lines.FindIndex(l => l.Trim() == "[" + section + "]");
        if (start < 0) return (-1, -1);
        int end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        return (start, end < 0 ? lines.Count : end);
    }

    private static int FindKey(List<string> lines, string section, string name)
    {
        var (start, end) = FindSection(lines, section);
        for (int i = start + 1; start >= 0 && i < end; i++)
        {
            int eq = lines[i].IndexOf('=');
            if (eq > 0 && lines[i][..eq].Trim() == name) return i;
        }
        return -1;
    }

    private static string ValueOf(string line) => line[(line.IndexOf('=') + 1)..];

    /// <summary>The text an INI file stores for a JSON literal.</summary>
    private static string IniText(string raw)
    {
        var node = JsonNode.Parse(raw);
        return node is JsonValue v && v.TryGetValue<string>(out var str) ? str : node?.ToJsonString() ?? "";
    }
}
