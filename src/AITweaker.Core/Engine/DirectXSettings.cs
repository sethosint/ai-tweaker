namespace AITweaker.Core.Engine;

/// <summary>Windows stores per-user graphics settings as one "Key=Value;Key=Value;" string.</summary>
public static class DirectXSettings
{
    public static string Merge(string? current, string key, string value)
    {
        var pairs = (current ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && !p[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p[0]}={p[1]}")
            .ToList();
        pairs.Add($"{key}={value}");
        return string.Join(';', pairs) + ";";
    }
}
