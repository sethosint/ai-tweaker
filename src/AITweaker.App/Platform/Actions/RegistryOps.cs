using AITweaker.Core.Model;
using Microsoft.Win32;

namespace AITweaker.App.Platform.Actions;

public static class RegistryOps
{
    public static Task ApplyAsync(RegistryAction a, ExecContext ctx)
    {
        var paths = RegistryHelper.Expand(a.Path);
        if (paths.Count == 0)
        {
            ctx.Log($"skip {a.Path} (no matching keys)");
            return Task.CompletedTask;
        }

        int done = 0, denied = 0;
        foreach (var path in paths)
        {
            if (a.OnlyIfKeyExists && !RegistryHelper.KeyExists(path)) continue;
            try
            {
                if (a.Delete)
                {
                    var before = RegistryHelper.Read(path, a.Name);
                    if (!before.Exists) continue;
                    Capture(ctx, path, a.Name, before, createdKey: null);
                    RegistryHelper.DeleteValue(path, a.Name);
                }
                else
                {
                    var kind = RegistryHelper.ToWin32(a.Kind);
                    SetValue(path, a.Name, RegistryHelper.ToRegistryObject(a.Value!.Value, kind), kind, ctx);
                }
                done++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                denied++; // some per-device keys are owned by SYSTEM; best effort for wildcard paths
                if (paths.Count == 1) throw;
            }
        }
        if (denied > 0) ctx.Log($"{a.Path}\\{a.Name}: {done} set, {denied} access denied");
        return Task.CompletedTask;
    }

    /// <summary>Sets a value, capturing the previous value and any key it had to create.</summary>
    public static void SetValue(string path, string name, object value, RegistryValueKind kind, ExecContext ctx)
    {
        var createdKey = RegistryHelper.FirstMissingAncestor(path);
        var before = RegistryHelper.Read(path, name);
        Capture(ctx, path, name, before, createdKey);
        RegistryHelper.Write(path, name, value, kind);
    }

    private static void Capture(ExecContext ctx, string path, string name, RegistryHelper.ValueSnapshot before, string? createdKey) =>
        ctx.Record(ChangeRecord.Of("registry",
            ("path", path), ("name", name), ("existed", before.Exists ? "1" : "0"),
            ("kind", before.Kind.ToString()), ("data", before.Data), ("createdKey", createdKey)));

    public static Task RevertAsync(ChangeRecord r)
    {
        var path = r["path"]!;
        var name = r["name"] ?? "";
        if (r["createdKey"] is { Length: > 0 } created)
        {
            RegistryHelper.DeleteKeyTree(created);
            return Task.CompletedTask;
        }
        if (r["existed"] == "1")
        {
            var kind = Enum.Parse<RegistryValueKind>(r["kind"]!);
            RegistryHelper.Write(path, name, RegistryHelper.Deserialize(r["data"], kind), kind);
        }
        else
        {
            RegistryHelper.DeleteValue(path, name);
        }
        return Task.CompletedTask;
    }

    public static bool? Check(RegistryAction a)
    {
        var paths = RegistryHelper.Expand(a.Path);
        if (a.Path.Contains('*') && paths.Count == 0) return null;
        foreach (var path in paths)
        {
            var cur = RegistryHelper.Read(path, a.Name);
            if (a.Delete)
            {
                if (cur.Exists) return false;
                continue;
            }
            if (a.OnlyIfKeyExists && !RegistryHelper.KeyExists(path)) continue;
            if (!RegistryHelper.Matches(cur, a.Value!.Value, RegistryHelper.ToWin32(a.Kind))) return false;
        }
        return true;
    }
}
