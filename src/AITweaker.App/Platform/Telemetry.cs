using System.Net.NetworkInformation;

namespace AITweaker.App.Platform;

public sealed record TelemetrySample(DateTime Time, double Cpu, double Ram, double RamUsedGb, double RamTotalGb, long? PingMs, int Processes);

/// <summary>Live CPU / RAM / ping samples for the dashboard stream (1 Hz).</summary>
public sealed class Telemetry : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private (long Idle, long Kernel, long User) _last = Native.GetSystemTimes();
    private readonly Ping _ping = new();
    private readonly Sensors _sensors = new();
    private int _tick;
    private long? _lastPing;

    /// <summary>The window is minimized: sample every 5 s and skip the sensors (WMI) until it's back.</summary>
    public bool Background { get; set; }

    public event Action<TelemetrySample>? Sample;
    public event Action<SensorSample>? SensorsRead;

    public void Start() => _ = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var now = Native.GetSystemTimes();
                long idle = now.Idle - _last.Idle, total = (now.Kernel - _last.Kernel) + (now.User - _last.User);
                _last = now;
                double cpu = total > 0 ? Math.Clamp(100.0 * (total - idle) / total, 0, 100) : 0;
                var (load, totalBytes, avail) = Native.GetMemoryStatus();
                if (_tick++ % 2 == 0) _lastPing = await PingAsync();
                Sample?.Invoke(new TelemetrySample(DateTime.Now, Math.Round(cpu, 1), load,
                    Math.Round((totalBytes - avail) / 1073741824.0, 1), Math.Round(totalBytes / 1073741824.0, 1), _lastPing, Native.ProcessCount()));
                if (_tick % 2 == 1 && !Background) SensorsRead?.Invoke(_sensors.Read());
            }
            catch (Exception ex)
            {
                AppLog.Warn("telemetry", ex.Message);
            }
            try { await Task.Delay(Background ? 5000 : 1000, _cts.Token); } catch (OperationCanceledException) { }
        }
    }

    private async Task<long?> PingAsync()
    {
        try
        {
            var reply = await _ping.SendPingAsync(SettingsStore.Current.PingHost, 900);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _ping.Dispose();
        _sensors.Dispose();
    }
}
