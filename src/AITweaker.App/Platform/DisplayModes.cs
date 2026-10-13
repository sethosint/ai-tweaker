namespace AITweaker.App.Platform;

/// <summary>
/// Switches the display mode (e.g. to a stretched 4:3 resolution made in CRU) and switches back by itself after a
/// countdown unless the user confirms — in case the monitor can't show the new mode.
/// </summary>
public static class DisplayModes
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (Native.DisplayMode Previous, CancellationTokenSource Cts)> Pending = new(StringComparer.OrdinalIgnoreCase);

    public static object List() => Native.GetDisplays().Select(d =>
    {
        var (current, modes) = Native.GetModes(d.Device);
        return new { d.Device, d.Name, current, modes };
    }).ToList();

    public static bool Set(string device, int width, int height, int hz, int revertAfterSeconds)
    {
        var (previous, _) = Native.GetModes(device);
        if (!Native.SetRefreshRate(device, width, height, hz)) return false;
        AppLog.Info("display", $"{device}: {previous.Width}×{previous.Height}@{previous.Hz} -> {width}×{height}@{hz}, keeps it after confirmation");
        var cts = new CancellationTokenSource();
        lock (Gate)
        {
            if (Pending.Remove(device, out var old)) { old.Cts.Cancel(); previous = old.Previous; } // keep the original mode across retries
            Pending[device] = (previous, cts);
        }
        _ = Task.Delay(TimeSpan.FromSeconds(revertAfterSeconds), cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Revert(device);
        }, TaskScheduler.Default);
        return true;
    }

    public static bool Keep(string device)
    {
        lock (Gate)
        {
            if (!Pending.Remove(device, out var p)) return false;
            p.Cts.Cancel();
        }
        AppLog.Ok("display", $"{device}: new mode kept");
        return true;
    }

    public static bool Revert(string device)
    {
        (Native.DisplayMode Previous, CancellationTokenSource Cts) p;
        lock (Gate)
        {
            if (!Pending.Remove(device, out p)) return false;
            p.Cts.Cancel();
        }
        var ok = Native.SetRefreshRate(device, p.Previous.Width, p.Previous.Height, p.Previous.Hz);
        AppLog.Write(ok ? "ok" : "error", "display", $"{device}: back to {p.Previous.Width}×{p.Previous.Height}@{p.Previous.Hz}");
        return ok;
    }
}
