using System.Text.RegularExpressions;
using AITweaker.Core.Model;

namespace AITweaker.App.Platform.Actions;

public static class BcdOps
{
    /// <summary>Reads an element of {current}. Element names are not localized; values like Yes/No may be.</summary>
    private static async Task<string?> ReadAsync(string element, ExecContext ctx)
    {
        var text = await ctx.BcdEnumAsync();
        var m = Regex.Match(text, $@"^\s*{Regex.Escape(element)}\s+(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string NormalizeBool(string v) => v.ToLowerInvariant() switch
    {
        "yes" or "да" or "on" or "true" => "yes",
        "no" or "нет" or "off" or "false" => "no",
        _ => v,
    };

    public static async Task ApplyAsync(BcdAction a, ExecContext ctx)
    {
        var prev = await ReadAsync(a.Element, ctx);
        ctx.Record(ChangeRecord.Of("bcd", ("element", a.Element), ("value", prev is null ? null : NormalizeBool(prev))));
        if (a.Value is null)
        {
            if (prev is null) return;
            var r = await ProcessRunner.RunAsync("bcdedit.exe", $"/deletevalue {{current}} {a.Element}");
            if (!r.Success) throw new InvalidOperationException($"bcdedit /deletevalue {a.Element}: {r.Output.Trim()} {r.Error.Trim()}");
        }
        else
        {
            var r = await ProcessRunner.RunAsync("bcdedit.exe", $"/set {{current}} {a.Element} {a.Value}");
            if (!r.Success) throw new InvalidOperationException($"bcdedit /set {a.Element}: {r.Output.Trim()} {r.Error.Trim()}");
        }
        ctx.InvalidateBcd();
    }

    public static async Task RevertAsync(ChangeRecord r)
    {
        var element = r["element"]!;
        var value = r["value"];
        var args = value is null ? $"/deletevalue {{current}} {element}" : $"/set {{current}} {element} {value}";
        await ProcessRunner.RunAsync("bcdedit.exe", args);
    }

    public static async Task<bool?> CheckAsync(BcdAction a, ExecContext ctx)
    {
        var cur = await ReadAsync(a.Element, ctx);
        if (a.Value is null) return cur is null;
        return cur is not null && NormalizeBool(cur) == NormalizeBool(a.Value);
    }
}
