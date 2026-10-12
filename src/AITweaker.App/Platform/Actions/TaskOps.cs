using AITweaker.Core.Model;

namespace AITweaker.App.Platform.Actions;

/// <summary>Scheduled tasks via the Task Scheduler COM API (locale-independent, unlike schtasks output).</summary>
public static class TaskOps
{
    private static dynamic? Find(string fullPath)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler COM not available");
        dynamic svc = Activator.CreateInstance(type)!;
        svc.Connect();
        int slash = fullPath.LastIndexOf('\\');
        string folder = slash <= 0 ? "\\" : fullPath[..slash];
        string name = fullPath[(slash + 1)..];
        try
        {
            dynamic f = svc.GetFolder(folder);
            return f.GetTask(name);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null; // folder or task does not exist on this build
        }
    }

    public static Task ApplyAsync(ScheduledTaskAction a, ExecContext ctx)
    {
        var task = Find(a.Path);
        if (task is null)
        {
            ctx.Log($"task {a.Path} not present, skipped");
            return Task.CompletedTask;
        }
        bool prev = task.Enabled;
        ctx.Record(ChangeRecord.Of("task", ("path", a.Path), ("enabled", prev ? "1" : "0")));
        try
        {
            task.Enabled = a.Enabled;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            ctx.Log($"task {a.Path}: access denied ({ex.Message})");
        }
        return Task.CompletedTask;
    }

    public static Task RevertAsync(ChangeRecord r)
    {
        var task = Find(r["path"]!);
        if (task is not null) task.Enabled = r["enabled"] == "1";
        return Task.CompletedTask;
    }

    public static bool? Check(ScheduledTaskAction a)
    {
        try
        {
            var task = Find(a.Path);
            return task is null ? null : (bool)task.Enabled == a.Enabled;
        }
        catch
        {
            return null;
        }
    }
}
