using System.Net;
using System.Text;
using AITweaker.Core.Catalog;
using AITweaker.Core.Engine;
using AITweaker.Core.Profile;

namespace AITweaker.App.Platform;

/// <summary>One-click "shift report": hardware, diagnostics and applied tweaks as a standalone HTML file.</summary>
public static class ReportBuilder
{
    public static string Build(SystemProfile p, IReadOnlyList<Insight> insights, TweakCatalog catalog, IReadOnlyDictionary<string, string> status, string lang)
    {
        string T(string ru, string en) => lang == "en" ? en : ru;
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>AI Tweaker report</title><style>");
        sb.Append("body{font:14px/1.5 'Cascadia Mono',Consolas,monospace;background:#f2efe6;color:#1d1d1b;margin:32px}h1{font-size:22px;letter-spacing:.08em}");
        sb.Append("h2{font-size:15px;border-bottom:2px solid #1d1d1b;padding-bottom:4px;margin-top:28px;text-transform:uppercase;letter-spacing:.1em}");
        sb.Append("table{border-collapse:collapse;width:100%}td,th{border:1px solid #c9c4b5;padding:4px 8px;text-align:left;vertical-align:top}th{background:#e4dfd1}");
        sb.Append(".critical{color:#c0261c;font-weight:bold}.warning{color:#b36b00}.info{color:#2d5f8a}.applied{color:#1f7a3a}.off{color:#8a857a}</style></head><body>");
        sb.Append($"<h1>AI TWEAKER // {T("ОТЧЁТ О СИСТЕМЕ", "SYSTEM REPORT")}</h1><p>{H(Environment.MachineName)} · {DateTime.Now:yyyy-MM-dd HH:mm}</p>");

        sb.Append($"<h2>{T("Железо", "Hardware")}</h2><table>");
        void Row(string k, string? v) => sb.Append($"<tr><th>{H(k)}</th><td>{H(v)}</td></tr>");
        Row("OS", $"{p.Os.Caption} {p.Os.DisplayVersion} ({p.Os.Build}.{p.Os.Ubr})");
        Row("CPU", $"{p.Cpu.Name} — {p.Cpu.Cores}C/{p.Cpu.Threads}T{(p.Cpu.IsHybrid ? $" ({p.Cpu.PCores}P+{p.Cpu.ECores}E)" : "")}");
        foreach (var g in p.Gpus) Row("GPU", $"{g.Name} · driver {g.DriverVersion} ({g.DriverDate:yyyy-MM-dd})");
        Row("RAM", $"{p.Memory.TotalGb:0} GB {p.Memory.Type} @ {p.Memory.ConfiguredSpeed} MHz · {p.Memory.Modules.Count} {T("модуля", "modules")}");
        foreach (var d in p.Disks) Row(T("Диск", "Disk"), $"{d.Model} · {d.MediaType} {d.BusType} · {d.SizeGb:0} GB{(d.IsSystem ? " · system" : "")}");
        foreach (var n in p.Network) Row(T("Сеть", "Network"), $"{n.Description} · {n.Kind} · {(n.Connected ? $"{n.SpeedMbps} Mbps" : "disconnected")}");
        foreach (var d in p.Displays) Row(T("Монитор", "Display"), $"{d.Name} · {d.Width}×{d.Height} @ {d.RefreshHz} Hz (max {d.MaxRefreshHz})");
        Row(T("Плата", "Board"), $"{p.Board.Manufacturer} {p.Board.Product} · BIOS {p.Board.BiosVersion}");
        sb.Append("</table>");

        sb.Append($"<h2>{T("Диагностика", "Diagnostics")}</h2><table>");
        foreach (var i in insights)
            sb.Append($"<tr><td class=\"{i.Severity.ToString().ToLowerInvariant()}\">{i.Severity}</td><td><b>{H(i.Title.Get(lang))}</b><br>{H(i.Detail.Get(lang))}</td></tr>");
        sb.Append("</table>");

        sb.Append($"<h2>{T("Твики", "Tweaks")}</h2><table><tr><th>ID</th><th>{T("Название", "Name")}</th><th>{T("Статус", "Status")}</th></tr>");
        foreach (var t in catalog.Tweaks)
        {
            var st = status.GetValueOrDefault(t.Id, "n/a");
            if (st == "n/a") continue;
            sb.Append($"<tr><td>{H(t.Id)}</td><td>{H(t.Name.Get(lang))}</td><td class=\"{(st == "applied" ? "applied" : "off")}\">{H(st)}</td></tr>");
        }
        sb.Append("</table>");

        sb.Append($"<h2>{T("История", "History")}</h2><table>");
        foreach (var s in BackupStore.Sessions().Take(20))
            sb.Append($"<tr><td>{s.Time:yyyy-MM-dd HH:mm}</td><td>{H(s.Kind)} {H(s.Profile)}</td><td>{s.Applied.Count} ok · {s.Failed.Count} failed{(s.RestorePoint ? " · restore point" : "")}</td></tr>");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }
}
