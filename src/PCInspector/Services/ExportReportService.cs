using System.Net;
using System.Text;
using System.Text.Json;
using PCInspector.Models;

namespace PCInspector.Services;

internal static class ExportReportService
{
    public static void Write(string path, IReadOnlyList<ProcessRow> rows) => Write(path,
        new DiagnosticReport(DateTime.UtcNow, new ActivitySnapshot(DateTime.UtcNow, rows), null,
            [], ["Startup has not been captured."], null, null, null));

    public static void Write(string path, DiagnosticReport report)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase)
            ? Html(report, json) : json, new UTF8Encoding(false));
    }

    private static string Html(DiagnosticReport report, string json)
    {
        static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "Unavailable");
        var b = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>PCInspector · Inspection report</title>");
        b.Append("<style>body{font:14px -apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#f5f5f7;color:#1d1d1f;margin:40px auto;padding:0 28px;max-width:1400px}h1{font-size:36px;letter-spacing:-1px}h2{font-size:22px;margin-top:32px}.note{color:#666;line-height:1.6}section{background:white;border:1px solid #e5e5e8;border-radius:16px;padding:22px;margin:20px 0;overflow:auto}table{border-collapse:collapse;width:100%;white-space:nowrap}th,td{text-align:left;padding:12px;border-bottom:1px solid #eee}th{color:#777;font-size:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:12px ui-monospace,Consolas,monospace;line-height:1.6}summary{cursor:pointer;color:#007aff}@media print{body{background:white;margin:0}section{break-inside:avoid}}</style>");
        b.Append("<p class=note>PCINSPECTOR / INSPECTION REPORT</p><h1>A closer look at your PC.</h1><p class=note>")
            .Append(E(report.GeneratedAtUtc.ToString("u"))).Append(" · ").Append(report.Activity.Processes.Count).Append(" exported processes</p><p class=note>")
            .Append(E(report.Note)).Append("</p>");
        if (report.System is { } system)
        {
            b.Append("<section><h2>System overview</h2><p>").Append(E(system.ComputerName)).Append(" · ")
                .Append(E(system.WindowsVersion)).Append("</p><p>").Append(E(system.Cpu)).Append("</p><p>")
                .Append(E(string.Join(" · ", system.GraphicsAdapters.Select(a => a.Name)))).Append("</p><p class=note>")
                .Append(E(string.Join("; ", system.Warnings))).Append("</p></section>");
        }
        b.Append("<section><h2>Process activity</h2><p class=note>Captured ").Append(E(report.Activity.CapturedAtUtc.ToString("u")))
            .Append("</p><table><tr><th>Process</th><th>PID</th><th>CPU %</th><th>GPU %</th><th>Memory MiB</th><th>Read MiB/s</th><th>Write MiB/s</th><th>State</th></tr>");
        foreach (var row in report.Activity.Processes)
        {
            b.Append("<tr>");
            foreach (var cell in new object?[] { row.Name, row.Pid, row.CpuPercent?.ToString("N1"), row.GpuPercent?.ToString("N1"),
                row.MemoryMiB?.ToString("N1"), row.ReadMiBPerSecond?.ToString("N2"), row.WriteMiBPerSecond?.ToString("N2"), row.Status })
                b.Append("<td>").Append(E(cell)).Append("</td>");
            b.Append("</tr>");
        }
        b.Append("</table></section>");
        if (!string.IsNullOrEmpty(report.SelectedProcessDetails))
            b.Append("<section><h2>Selected process details</h2><pre>").Append(E(report.SelectedProcessDetails)).Append("</pre></section>");
        b.Append("<section><h2>Startup observations</h2><p class=note>").Append(E(string.Join("; ", report.StartupWarnings))).Append("</p><table>");
        foreach (var item in report.Startup)
            b.Append("<tr><td>").Append(E(item.Source)).Append("</td><td>").Append(E(item.Name)).Append("</td><td>").Append(E(item.Command)).Append("</td></tr>");
        b.Append("</table></section>");
        if (report.Comparison is { } comparison)
        {
            b.Append("<section><h2>Snapshot comparison</h2><table><tr><th>Observation</th><th>Process</th><th>PID</th><th>Δ CPU pp</th><th>Δ Memory MiB</th></tr>");
            foreach (var change in comparison)
                b.Append("<tr><td>").Append(E(change.Change)).Append("</td><td>").Append(E(change.Name)).Append("</td><td>")
                    .Append(change.Pid).Append("</td><td>").Append(E(change.CpuDelta?.ToString("+0.0;-0.0;0.0")))
                    .Append("</td><td>").Append(E(change.MemoryDeltaMiB?.ToString("+0.0;-0.0;0.0"))).Append("</td></tr>");
            b.Append("</table></section>");
        }
        // HTML encoding prevents process command lines from becoming executable markup.
        return b.Append("<section><details><summary>Complete captured data, including paths and resource history</summary><pre>")
            .Append(E(json)).Append("</pre></details></section></html>").ToString();
    }
}
