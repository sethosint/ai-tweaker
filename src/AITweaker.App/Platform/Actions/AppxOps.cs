using System.Text.Json;
using AITweaker.Core.Model;

namespace AITweaker.App.Platform.Actions;

public sealed record InstalledPackage(string Name, string FullName, string Version, bool NonRemovable, bool IsFramework);

/// <summary>Built-in app removal through Windows PowerShell's Appx module (all users + provisioned image).</summary>
public static class AppxOps
{
    public static async Task<List<InstalledPackage>> ListAsync()
    {
        const string script = "Get-AppxPackage -AllUsers | Select-Object Name, PackageFullName, Version, NonRemovable, IsFramework | ConvertTo-Json -Compress";
        var r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromMinutes(2));
        if (!r.Success || r.Output.Trim().Length == 0) throw new InvalidOperationException("Get-AppxPackage failed: " + r.Error.Trim());
        using var doc = JsonDocument.Parse(r.Output);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        return items.Select(e => new InstalledPackage(
                e.GetProperty("Name").GetString() ?? "",
                e.GetProperty("PackageFullName").GetString() ?? "",
                e.TryGetProperty("Version", out var v) ? v.ToString() : "",
                e.TryGetProperty("NonRemovable", out var nr) && nr.ValueKind == JsonValueKind.True,
                e.TryGetProperty("IsFramework", out var fw) && fw.ValueKind == JsonValueKind.True))
            .GroupBy(p => p.Name).Select(g => g.First())
            .ToList();
    }

    public sealed record RestoreResult(bool Ok, string Method);

    /// <summary>
    /// Brings a removed built-in app back: the Store with Windows' own <c>wsreset -i</c>, anything else from the Microsoft
    /// Store through winget (as the signed-in user, Store apps are per-user). If that fails the Store page opens instead.
    /// </summary>
    public static async Task<RestoreResult> RestoreAsync(string package, string? storeId, CancellationToken ct = default)
    {
        if (package.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase) || package.Equals("Microsoft.StorePurchaseApp", StringComparison.OrdinalIgnoreCase))
        {
            await ProgramInstaller.RunAsSignedInUserAsync(ProcessRunner.System32("wsreset.exe"), "-i", ct);
            for (int i = 0; i < 30 && !await IsInstalledAsync(package); i++) await Task.Delay(TimeSpan.FromSeconds(4), ct); // wsreset installs in the background
        }
        else if (storeId is not null && ProgramInstaller.FindWinget() is { } winget)
        {
            await ProgramInstaller.RunAsSignedInUserAsync(winget, $"install --id {storeId} --source msstore --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", ct);
        }
        if (await IsInstalledAsync(package))
        {
            AppLog.Ok("debloat", $"{package} restored");
            return new RestoreResult(true, "store");
        }
        var page = storeId is not null ? $"ms-windows-store://pdp/?ProductId={storeId}" : $"ms-windows-store://search/?query={Uri.EscapeDataString(package)}";
        ProcessRunner.OpenUrl(page);
        AppLog.Warn("debloat", $"{package}: automatic reinstall failed, opened the Store page");
        return new RestoreResult(false, "page");
    }

    private static async Task<bool> IsInstalledAsync(string package)
    {
        var r = await ProcessRunner.PowerShellAsync($"if (Get-AppxPackage -AllUsers -Name {ProcessRunner.PsQuote(package)}) {{ 'YES' }}", TimeSpan.FromMinutes(1));
        return r.Output.Contains("YES");
    }

    /// <summary>Removes packages for every user and from the provisioned image so they don't come back for new accounts.</summary>
    public static async Task<Dictionary<string, bool>> RemoveAsync(IReadOnlyList<string> names, Action<string>? progress = null)
    {
        var result = new Dictionary<string, bool>();
        foreach (var name in names)
        {
            progress?.Invoke(name);
            var n = ProcessRunner.PsQuote(name);
            var script =
                $"$n = {n}; " +
                "Get-AppxPackage -AllUsers -Name $n | ForEach-Object { try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop } catch { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction SilentlyContinue } }; " +
                "Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like $n } | ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null }; " +
                "if (Get-AppxPackage -AllUsers -Name $n) { 'LEFT' } else { 'GONE' }";
            var r = await ProcessRunner.PowerShellAsync(script, TimeSpan.FromMinutes(5));
            bool ok = r.Output.Contains("GONE");
            result[name] = ok;
            if (ok) AppLog.Ok("debloat", $"removed {name}");
            else AppLog.Warn("debloat", $"{name} could not be removed: {(r.Error.Length > 0 ? r.Error : r.Output).Trim()}");
        }
        return result;
    }

    public static async Task ApplyAsync(AppxRemoveAction a, ExecContext ctx)
    {
        var res = await RemoveAsync(a.Packages);
        ctx.Record(ChangeRecord.Of("info", ("removed", string.Join(",", res.Where(r => r.Value).Select(r => r.Key)))));
    }
}
