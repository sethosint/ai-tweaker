namespace AITweaker.App.Platform;

public static class RestorePoint
{
    /// <summary>Enables System Protection on the system drive if needed and creates a restore point (bypassing the 24 h limit).</summary>
    public static async Task<bool> CreateAsync(string description)
    {
        AppLog.Info("restore", "creating a system restore point…");
        var script =
            "$ErrorActionPreference='Stop'; " +
            "try { Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" } catch { }; " +
            "New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name SystemRestorePointCreationFrequency -Value 0 -PropertyType DWord -Force | Out-Null; " +
            $"Checkpoint-Computer -Description {ProcessRunner.PsQuote(description)} -RestorePointType MODIFY_SETTINGS; 'OK'";
        var r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromMinutes(5));
        if (r.Output.Contains("OK"))
        {
            AppLog.Ok("restore", "restore point created");
            return true;
        }
        AppLog.Warn("restore", "restore point failed: " + (r.Error.Length > 0 ? r.Error : r.Output).Trim());
        return false;
    }

    public static void OpenSystemRestore() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ProcessRunner.System32("rstrui.exe")) { UseShellExecute = true });
}
