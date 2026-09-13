using System.Net;
using System.Text;
using System.Text.Json;
using PCInspector.Models;

namespace PCInspector.Services;

internal static class ExportReportService
{
    public static void Write(string path, IReadOnlyList<ProcessRow> rows)
    {
        var report = rows.Select(row => new
        {
            row.Name,
            row.Pid,
            row.Status,
            row.CpuPercent,
            row.GpuPercent,
            row.AverageCpuPercent,
            row.PeakCpuPercent,
            row.MemoryMiB,
            row.Path,
            row.CommandLine,
            row.ParentPid,
            row.ParentName,
            History = row.History.Select(interval => new { interval.Start, interval.End, interval.Percent })
        }).ToArray();
        if (Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(path, Html(report), Encoding.UTF8);
        else
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Note = "High resource usage is an investigation lead, not a malware verdict.",
                Processes = report
            }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    private static string Html<T>(T[] report)
    {
        var builder = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>PCInspector report</title>");
        builder.Append("<style>body{font:14px Segoe UI,Arial;color:#202328;background:#f3f5f8;padding:24px}table{border-collapse:collapse;background:white}th,td{padding:8px 12px;border-bottom:1px solid #e5e9ef;text-align:left}th{color:#636c7a}</style>");
        builder.Append("<h1>PCInspector process report</h1><p>High resource usage is an investigation lead, not a malware verdict.</p><table><tr><th>Name</th><th>PID</th><th>CPU %</th><th>GPU %</th><th>RAM MiB</th><th>State</th><th>Path</th></tr>");
        foreach (var row in report)
        {
            var json = JsonSerializer.Serialize(row);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            builder.Append("<tr>");
            Cell(builder, root, "Name"); Cell(builder, root, "Pid"); Cell(builder, root, "CpuPercent");
            Cell(builder, root, "GpuPercent"); Cell(builder, root, "MemoryMiB"); Cell(builder, root, "Status"); Cell(builder, root, "Path");
            builder.Append("</tr>");
        }
        return builder.Append("</table>").ToString();
    }

    private static void Cell(StringBuilder builder, JsonElement row, string name)
    {
        var value = row.TryGetProperty(name, out var property) ? property.ToString() : "";
        builder.Append("<td>").Append(WebUtility.HtmlEncode(value)).Append("</td>");
    }
}
