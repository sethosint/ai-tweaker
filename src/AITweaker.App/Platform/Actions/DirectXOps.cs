using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform.Actions;

/// <summary>Windows stores Graphics settings as "Key=Value;Key=Value;" in one string — merge instead of overwrite.</summary>
public static class DirectXOps
{
    private const string Path = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private const string Name = "DirectXUserGlobalSettings";

    public static Task ApplyAsync(DirectXSettingAction a, ExecContext ctx)
    {
        var cur = RegistryHelper.Read(Path, Name);
        RegistryOps.SetValue(Path, Name, AITweaker.Core.Engine.DirectXSettings.Merge(cur.Data, a.Key, a.Value), RegistryValueKind.String, ctx);
        return Task.CompletedTask;
    }

    public static bool? Check(DirectXSettingAction a)
    {
        var cur = RegistryHelper.Read(Path, Name).Data ?? "";
        return cur.Split(';', StringSplitOptions.TrimEntries).Any(p => p.Equals($"{a.Key}={a.Value}", StringComparison.OrdinalIgnoreCase));
    }
}
