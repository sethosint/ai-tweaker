using AITweaker.Core.Model;

namespace AITweaker.App.Platform.Actions;

public static class CommandOps
{
    public static async Task ApplyCommandAsync(CommandAction a, ExecContext ctx)
    {
        var r = await ProcessRunner.RunAsync(a.File, a.Args, TimeSpan.FromMinutes(5), ct: ctx.Ct);
        if (!r.Success) throw new InvalidOperationException($"{a.File} {a.Args} -> exit {r.ExitCode}: {(r.Output + r.Error).Trim()}");
        if (a.UndoFile is not null) ctx.Record(ChangeRecord.Of("undo-command", ("file", a.UndoFile), ("args", a.UndoArgs ?? "")));
    }

    public static async Task ApplyPowerShellAsync(PowerShellAction a, ExecContext ctx)
    {
        var r = await ProcessRunner.PowerShellAsync(a.Script, TimeSpan.FromMinutes(10), ct: ctx.Ct);
        if (!r.Success) throw new InvalidOperationException($"PowerShell exit {r.ExitCode}: {(r.Error.Length > 0 ? r.Error : r.Output).Trim()}");
        if (a.Undo is not null) ctx.Record(ChangeRecord.Of("undo-powershell", ("script", a.Undo)));
    }

    public static async Task RevertAsync(ChangeRecord r)
    {
        if (r.Kind == "undo-command")
            await ProcessRunner.RunAsync(r["file"]!, r["args"] ?? "", TimeSpan.FromMinutes(5));
        else
            await ProcessRunner.PowerShellAsync(r["script"]!, TimeSpan.FromMinutes(10));
    }

    public static async Task<bool?> CheckPowerShellAsync(PowerShellAction a)
    {
        if (a.Check is null) return null;
        var r = await ProcessRunner.PowerShellAsync(a.Check, TimeSpan.FromSeconds(30));
        return r.Output.Trim().EndsWith('1');
    }
}
