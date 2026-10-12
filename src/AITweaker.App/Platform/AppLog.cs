using System.Collections.Concurrent;

namespace AITweaker.App.Platform;

public sealed record LogEntry(DateTime Time, string Level, string Source, string Text);

/// <summary>App-wide log: streamed to the UI event console and written to C:\ProgramData\AITweaker\logs.</summary>
public static class AppLog
{
    public static event Action<LogEntry>? Written;

    private static readonly ConcurrentQueue<LogEntry> Recent = new();
    private static readonly object FileLock = new();

    public static IReadOnlyList<LogEntry> Snapshot() => Recent.ToArray();

    public static void Info(string source, string text) => Write("info", source, text);
    public static void Ok(string source, string text) => Write("ok", source, text);
    public static void Warn(string source, string text) => Write("warn", source, text);
    public static void Error(string source, string text) => Write("error", source, text);

    public static void Write(string level, string source, string text)
    {
        var e = new LogEntry(DateTime.Now, level, source, text);
        Recent.Enqueue(e);
        while (Recent.Count > 500) Recent.TryDequeue(out _);
        try
        {
            lock (FileLock)
            {
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"{e.Time:yyyy-MM-dd}.log"), $"{e.Time:HH:mm:ss.fff} [{level.ToUpperInvariant(),-5}] {source}: {text}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break a tweak.
        }
        Written?.Invoke(e);
    }
}
