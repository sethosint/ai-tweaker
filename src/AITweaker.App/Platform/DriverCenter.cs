namespace AITweaker.App.Platform;

/// <summary>Driver list logic lives in Core (DriverAdvisor); this is the Windows side.</summary>
public static class DriverCenter
{
    /// <summary>Asks Windows to re-detect devices, so freshly installed drivers attach without a reboot.</summary>
    public static Task<ProcessResult> RescanAsync() => ProcessRunner.RunAsync("pnputil.exe", "/scan-devices", TimeSpan.FromMinutes(2));
}
