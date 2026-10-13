using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform.Actions;

/// <summary>Service startup types are changed through the registry (works for services SCM refuses to reconfigure) with sc.exe as fallback.</summary>
public static class ServiceOps
{
    private static string Key(string name) => $@"HKLM\SYSTEM\CurrentControlSet\Services\{name}";

    public static bool Exists(string name) => RegistryHelper.KeyExists(Key(name));

    public static (int Start, int Delayed) Read(string name)
    {
        var start = RegistryHelper.Read(Key(name), "Start");
        var delayed = RegistryHelper.Read(Key(name), "DelayedAutostart");
        return (start.Exists ? int.Parse(start.Data!) : 3, delayed.Exists ? int.Parse(delayed.Data!) : 0);
    }

    private static (int Start, int Delayed) Target(ServiceStartup s) => s switch
    {
        ServiceStartup.Disabled => (4, 0),
        ServiceStartup.Manual => (3, 0),
        ServiceStartup.Automatic => (2, 0),
        ServiceStartup.Delayed => (2, 1),
        _ => (3, 0),
    };

    public static async Task ApplyAsync(ServiceAction a, ExecContext ctx)
    {
        if (!Exists(a.Name))
        {
            ctx.Log($"service {a.Name} not present, skipped");
            return;
        }
        var (prevStart, prevDelayed) = Read(a.Name);
        ctx.Record(ChangeRecord.Of("service", ("name", a.Name), ("start", prevStart.ToString()), ("delayed", prevDelayed.ToString())));
        var (start, delayed) = Target(a.Startup);
        await SetAsync(a.Name, start, delayed);
        if (a.Stop && a.Startup is ServiceStartup.Disabled or ServiceStartup.Manual)
            await ProcessRunner.RunAsync("sc.exe", $"stop \"{a.Name}\"", TimeSpan.FromSeconds(20));
    }

    internal static async Task SetAsync(string name, int start, int delayed)
    {
        try
        {
            RegistryHelper.Write(Key(name), "Start", start, RegistryValueKind.DWord);
            if (start == 2) RegistryHelper.Write(Key(name), "DelayedAutostart", delayed, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            var mode = start switch { 4 => "disabled", 3 => "demand", _ => delayed == 1 ? "delayed-auto" : "auto" };
            var r = await ProcessRunner.RunAsync("sc.exe", $"config \"{name}\" start= {mode}");
            if (!r.Success) throw new InvalidOperationException($"sc config {name} failed: {r.Output.Trim()}");
        }
    }

    public static async Task RevertAsync(ChangeRecord r)
    {
        var name = r["name"]!;
        if (!Exists(name)) return;
        int start = int.Parse(r["start"] ?? "3");
        await SetAsync(name, start, int.Parse(r["delayed"] ?? "0"));
        if (start == 2) await ProcessRunner.RunAsync("sc.exe", $"start \"{name}\"", TimeSpan.FromSeconds(20));
    }

    public static bool? Check(ServiceAction a)
    {
        if (!Exists(a.Name)) return null;
        var (start, delayed) = Read(a.Name);
        var (ts, td) = Target(a.Startup);
        return start == ts && (ts != 2 || delayed == td);
    }
}
