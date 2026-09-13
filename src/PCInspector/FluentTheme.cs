using System.Drawing.Drawing2D;

namespace PCInspector;

// Shared presentation settings. Collectors and CPU calculations do not depend on this file.
internal static class FluentTheme
{
    public static readonly Color Canvas = Color.FromArgb(243, 245, 248);
    public static readonly Color Surface = Color.White;
    public static readonly Color Text = Color.FromArgb(32, 35, 40);
    public static readonly Color Muted = Color.FromArgb(99, 108, 122);
    public static readonly Color Line = Color.FromArgb(229, 233, 239);
    public static readonly Color Accent = Color.FromArgb(0, 103, 192);
    public static readonly Color Selection = Color.FromArgb(227, 240, 253);
    public static readonly Font BodyFont = new("Segoe UI", 10);
    public static readonly Font CaptionFont = new("Segoe UI", 9);
    public static readonly Font HeadingFont = new("Segoe UI Semibold", 24);
    public static readonly Font SectionFont = new("Segoe UI Semibold", 11);
    public static readonly Font ValueFont = new("Segoe UI Semibold", 20);

    public static void StyleGrid(DataGridView grid)
    {
        grid.Font = BodyFont;
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Line;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(249, 250, 252), ForeColor = Muted,
            SelectionBackColor = Color.FromArgb(249, 250, 252), SelectionForeColor = Text,
            Font = CaptionFont, Padding = new Padding(10, 8, 10, 8)
        };
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Selection;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.RowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Text,
            SelectionBackColor = Selection, SelectionForeColor = Text
        };
        grid.DefaultCellStyle.Padding = new Padding(10, 6, 10, 6);
        grid.RowTemplate.Height = 38;
        grid.ColumnHeadersHeight = 44;
        grid.RowHeadersVisible = false;
    }

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class FluentCard : Panel
{
    public FluentCard()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Padding = new Padding(16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentTheme.RoundedRectangle(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8 * DeviceDpi / 96f);
        using var fill = new SolidBrush(FluentTheme.Surface);
        using var border = new Pen(FluentTheme.Line);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal sealed class FluentButton : Button
{
    private bool hovered;
    public FluentButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = FluentTheme.BodyFont;
        ForeColor = Color.White;
        BackColor = FluentTheme.Accent;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? FluentTheme.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentTheme.RoundedRectangle(new RectangleF(.5f, .5f, Width - 1, Height - 1), 5 * DeviceDpi / 96f);
        using var fill = new SolidBrush(!Enabled ? FluentTheme.Line : hovered ? Color.FromArgb(0, 88, 166) : FluentTheme.Accent);
        e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Enabled ? Color.White : FluentTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Color.White, FluentTheme.Accent);
    }
}

internal sealed class FluentTabs : TabControl
{
    public FluentTabs()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(155, 46);
        Font = FluentTheme.BodyFont;
        Padding = new Point(16, 8);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = e.Index == SelectedIndex;
        using var background = new SolidBrush(FluentTheme.Canvas);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, FluentTheme.SectionFont, e.Bounds,
            selected ? FluentTheme.Accent : FluentTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (selected)
        {
            using var pen = new Pen(FluentTheme.Accent, 3 * DeviceDpi / 96f);
            e.Graphics.DrawLine(pen, e.Bounds.Left + 26, e.Bounds.Bottom - 3, e.Bounds.Right - 26, e.Bounds.Bottom - 3);
        }
        if (selected && Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -5, -7));
    }
}
