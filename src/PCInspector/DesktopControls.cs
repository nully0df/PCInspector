using System.Drawing.Drawing2D;

namespace PCInspector;

// Small vector controls keep the application crisp at different Windows display scales.
internal sealed class NavigationButton : Button
{
    private bool selected;
    private bool hovered;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Symbol { get; init; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }

    public NavigationButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = FluentTheme.Sidebar;
        Font = FluentTheme.BodyFont;
        Cursor = Cursors.Hand;
        Height = 42;
        Margin = new Padding(0, 0, 0, 5);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        var g = e.Graphics;
        g.Clear(FluentTheme.Sidebar);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (selected || hovered)
        {
            using var path = FluentTheme.RoundedRectangle(new RectangleF(.5f, .5f, Width - 1, Height - 1), 7 * scale);
            using var brush = new SolidBrush(selected ? FluentTheme.Accent : Color.FromArgb(227, 230, 236));
            g.FillPath(brush, path);
        }
        var color = selected ? Color.White : FluentTheme.Text;
        var state = g.Save();
        g.TranslateTransform(14 * scale, (Height - 20 * scale) / 2);
        g.ScaleTransform(scale, scale);
        DrawSymbol(g, color, Symbol);
        g.Restore(state);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle((int)(45 * scale), 0, Width - (int)(50 * scale), Height), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4), color, BackColor);
    }

    internal static void DrawSymbol(Graphics g, Color color, int symbol)
    {
        using var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (symbol)
        {
            case 0:
                using (var path = FluentTheme.RoundedRectangle(new RectangleF(1, 2, 18, 12), 2)) g.DrawPath(pen, path);
                g.DrawLine(pen, 10, 14, 10, 18); g.DrawLine(pen, 6, 18, 14, 18);
                break;
            case 1:
                g.DrawLines(pen, [new(1, 11), new(5, 11), new(8, 3), new(12, 17), new(15, 8), new(19, 8)]);
                break;
            case 2:
                g.DrawArc(pen, 2, 3, 15, 15, -45, 285);
                g.DrawLines(pen, [new(13, 2), new(18, 2), new(18, 7)]);
                g.DrawLine(pen, 10, 7, 10, 11); g.DrawLine(pen, 10, 11, 13, 13);
                break;
        }
    }
}

internal sealed class DeviceIllustration : Control
{
    public DeviceIllustration()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleName = "Computer illustration";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Min(Width / 140f, Height / 100f);
        g.TranslateTransform((Width - 140 * scale) / 2, (Height - 100 * scale) / 2);
        g.ScaleTransform(scale, scale);
        using var screen = FluentTheme.RoundedRectangle(new RectangleF(17, 5, 106, 69), 7);
        using var frame = new SolidBrush(Color.FromArgb(49, 54, 65));
        g.FillPath(frame, screen);
        using var display = FluentTheme.RoundedRectangle(new RectangleF(21, 9, 98, 59), 4);
        using var wallpaper = new LinearGradientBrush(new Point(22, 9), new Point(115, 69),
            Color.FromArgb(147, 211, 252), Color.FromArgb(39, 106, 213));
        g.FillPath(wallpaper, display);
        var clip = g.Save();
        g.SetClip(display);
        using var glow = new SolidBrush(Color.FromArgb(100, 209, 238, 255));
        g.FillEllipse(glow, -10, -30, 120, 111);
        using var wave = new SolidBrush(Color.FromArgb(100, 98, 170, 240));
        g.FillEllipse(wave, 48, 23, 106, 83);
        g.Restore(clip);
        using var stem = new SolidBrush(Color.FromArgb(175, 181, 191));
        g.FillPolygon(stem, [new(62, 74), new(78, 74), new(81, 87), new(59, 87)]);
        using var foot = FluentTheme.RoundedRectangle(new RectangleF(48, 86, 44, 4), 2);
        g.FillPath(stem, foot);
        using var shadow = new SolidBrush(Color.FromArgb(15, 38, 55, 83));
        g.FillEllipse(shadow, 39, 93, 63, 4);
    }
}

internal sealed class UsageBar : Control
{
    private double? fraction;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double? Fraction { get => fraction; set { fraction = value; Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = FluentTheme.Accent;
    public UsageBar()
    {
        Height = 5;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(FluentTheme.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = FluentTheme.RoundedRectangle(new RectangleF(0, 0, Width, Height), Height / 2f);
        using var background = new SolidBrush(FluentTheme.Secondary);
        e.Graphics.FillPath(background, track);
        if (fraction is not { } value || value <= 0) return;
        using var fill = new SolidBrush(FillColor);
        using var filled = FluentTheme.RoundedRectangle(new RectangleF(0, 0,
            Math.Max(Height, (float)(Width * Math.Clamp(value, 0, 1))), Height), Height / 2f);
        e.Graphics.FillPath(fill, filled);
    }
}
