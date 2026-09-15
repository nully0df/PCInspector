using System.Drawing.Drawing2D;
using PCInspector.Models;

namespace PCInspector;

internal sealed class ResourceChart : Control
{
    private ProcessRow? process;
    private string metric = "CPU";
    private double now;
    public ResourceChart()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        Dock = DockStyle.Fill;
        AccessibleName = "Selected process resource history chart";
    }
    public void Update(ProcessRow? row, string selectedMetric, double timestamp)
    {
        process = row; metric = selectedMetric; now = timestamp;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 150 || Height < 100) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var color = metric switch { "GPU" => Color.FromArgb(161, 81, 208), "Memory" => Color.FromArgb(41, 163, 124),
            "Read I/O" or "Write I/O" => Color.FromArgb(218, 135, 35), _ => FluentTheme.Accent };
        double? Value(ResourceSample sample) => metric switch
        {
            "GPU" => sample.GpuPercent, "Memory" => sample.MemoryMiB,
            "Read I/O" => sample.ReadMiBPerSecond, "Write I/O" => sample.WriteMiBPerSecond, _ => sample.CpuPercent
        };
        var samples = process?.ResourceHistory?.OrderBy(sample => sample.TimestampSeconds).ToArray() ?? [];
        if (samples.Length == 0 && metric == "CPU" && process is not null)
            samples = process.History.Select(i => new ResourceSample(i.End, i.Percent, null, null, null, null)).ToArray();
        var points = samples.Where(sample => sample.TimestampSeconds >= now - 60 && sample.TimestampSeconds <= now).ToArray();
        var valid = points.Select(Value).Where(value => value.HasValue && double.IsFinite(value.Value)).Select(value => value!.Value).ToArray();
        double? current = process is null ? null : metric switch
        {
            "GPU" => process.GpuPercent, "Memory" => process.MemoryMiB, "Read I/O" => process.ReadMiBPerSecond,
            "Write I/O" => process.WriteMiBPerSecond, _ => process.CpuPercent
        };
        var unit = metric is "CPU" or "GPU" ? "%" : metric == "Memory" ? " MiB" : " MiB/s";
        TextRenderer.DrawText(g, current is { } value ? $"{value:N1}{unit}" : "Unavailable", FluentTheme.ValueFont,
            new Rectangle(0, 0, Width, (int)(40 * scale)), FluentTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        var plot = new RectangleF(4 * scale, 61 * scale, Width - 48 * scale, Height - 88 * scale);
        if (plot.Height < 10) return;
        var ceiling = metric is "CPU" or "GPU" ? 100 : Math.Max(1, Math.Ceiling((valid.DefaultIfEmpty(0).Max() * 1.15) / 10) * 10);
        using var grid = new Pen(FluentTheme.Line, 1);
        for (var i = 0; i < 3; i++)
        {
            var y = plot.Top + plot.Height * i / 2;
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            TextRenderer.DrawText(g, (ceiling * (2 - i) / 2).ToString("0.#"), FluentTheme.CaptionFont,
                new Rectangle((int)plot.Right + 5, (int)y - 8, (int)(42 * scale), 20), FluentTheme.Muted);
        }
        if (valid.Length == 0)
            TextRenderer.DrawText(g, process is null ? "Select a process to inspect its activity" : "Waiting for readable samples",
                FluentTheme.CaptionFont, Rectangle.Round(plot), FluentTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var stroke = new Pen(color, 2 * scale) { LineJoin = LineJoin.Round };
        using var fill = new LinearGradientBrush(plot, Color.FromArgb(52, color), Color.FromArgb(3, color), 90);
        var segment = new List<PointF>();
        void DrawSegment()
        {
            if (segment.Count >= 2)
            {
                using var area = new GraphicsPath();
                area.AddLines(segment.ToArray());
                area.AddLine(segment[^1], new PointF(segment[^1].X, plot.Bottom));
                area.AddLine(new PointF(segment[^1].X, plot.Bottom), new PointF(segment[0].X, plot.Bottom));
                area.CloseFigure();
                g.FillPath(fill, area);
                g.DrawLines(stroke, segment.ToArray());
            }
            else if (segment.Count == 1)
            {
                using var dot = new SolidBrush(color);
                g.FillEllipse(dot, segment[0].X - 2, segment[0].Y - 2, 4, 4);
            }
            segment.Clear();
        }
        foreach (var sample in points)
        {
            if (Value(sample) is not { } amount || !double.IsFinite(amount)) { DrawSegment(); continue; }
            segment.Add(new PointF(plot.Left + (float)((sample.TimestampSeconds - now + 60) / 60) * plot.Width,
                plot.Bottom - (float)(Math.Clamp(amount, 0, ceiling) / ceiling) * plot.Height));
        }
        DrawSegment();
        TextRenderer.DrawText(g, "60 seconds ago", FluentTheme.CaptionFont, new Point((int)plot.Left, (int)plot.Bottom + 8), FluentTheme.Muted);
        TextRenderer.DrawText(g, "Now", FluentTheme.CaptionFont, new Rectangle((int)plot.Right - 50, (int)plot.Bottom + 8, 50, 22),
            FluentTheme.Muted, TextFormatFlags.Right);
    }
}
