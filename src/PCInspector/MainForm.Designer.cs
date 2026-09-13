#nullable enable

namespace PCInspector;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private Button refreshButton = null!;
    private Label statusLabel = null!;
    private DataGridView summaryGrid = null!;
    private DataGridView disksGrid = null!;
    private TextBox warningsBox = null!;
    private ProcessesView processesView = null!;
    private Label memoryValue = null!;
    private Label memoryCaption = null!;
    private Label uptimeValue = null!;
    private Label volumeValue = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();
        BackColor = FluentTheme.Canvas;
        ForeColor = FluentTheme.Text;
        Font = FluentTheme.BodyFont;
        var tabs = new FluentTabs { Dock = DockStyle.Fill };
        var systemTab = new TabPage("System") { BackColor = FluentTheme.Canvas };
        var processesTab = new TabPage("Processes") { BackColor = FluentTheme.Canvas };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 18),
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = FluentTheme.Canvas
        };
        var header = new TableLayoutPanel { Height = 66, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = "Your computer at a glance", ForeColor = FluentTheme.Muted,
            Font = FluentTheme.BodyFont, Location = new Point(0, 40), Size = new Size(520, 25),
            AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
        });
        heading.Controls.Add(new Label
        {
            Text = "System overview", Font = FluentTheme.HeadingFont,
            AutoSize = true, Location = new Point(0, 0)
        });
        refreshButton = new FluentButton
        {
            Text = "Refresh", Width = 124, Height = 38, Anchor = AnchorStyles.Right,
            AccessibleName = "Refresh system information"
        };
        refreshButton.Click += RefreshButton_Click;
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(refreshButton, 1, 0);
        layout.Controls.Add(header);

        var metrics = new TableLayoutPanel { Height = 106, ColumnCount = 3, Margin = new Padding(0, 0, 0, 12) };
        for (var i = 0; i < 3; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        metrics.Controls.Add(CreateMetricCard("MEMORY", out memoryValue, out memoryCaption), 0, 0);
        metrics.Controls.Add(CreateMetricCard("SYSTEM UPTIME", out uptimeValue, out var uptimeCaption), 1, 0);
        metrics.Controls.Add(CreateMetricCard("LOCAL VOLUMES", out volumeValue, out var volumesCaption), 2, 0);
        uptimeCaption.Text = "Since the Windows kernel started";
        volumesCaption.Text = "Ready to inspect";
        metrics.Controls[2].Margin = Padding.Empty;
        layout.Controls.Add(metrics);

        summaryGrid = new DataGridView();
        ConfigureGrid(summaryGrid);
        summaryGrid.Columns.Add("Property", "Property");
        summaryGrid.Columns.Add("Value", "Value");
        summaryGrid.Columns[0].FillWeight = 25;
        summaryGrid.Columns[0].DefaultCellStyle.ForeColor = FluentTheme.Muted;
        summaryGrid.Columns[1].FillWeight = 75;
        summaryGrid.ColumnHeadersVisible = false;
        summaryGrid.AccessibleName = "System information";
        layout.Controls.Add(CreateTableCard("Device information", summaryGrid, 324));

        disksGrid = new DataGridView();
        ConfigureGrid(disksGrid);
        disksGrid.Columns.Add("Drive", "Drive");
        disksGrid.Columns.Add("Type", "Type");
        disksGrid.Columns.Add("Total", "Capacity");
        disksGrid.Columns.Add("Free", "Free space");
        disksGrid.Columns.Add("Status", "Status");
        disksGrid.AccessibleName = "Local drives and volumes";
        layout.Controls.Add(CreateTableCard("Storage", disksGrid, 160));

        warningsBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Height = 64, Visible = false, BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(255, 248, 231), ForeColor = Color.FromArgb(118, 77, 15),
            AccessibleName = "Collection warnings", Margin = new Padding(0, 0, 0, 8)
        };
        statusLabel = new Label
        {
            Text = "Ready", Height = 26, ForeColor = FluentTheme.Muted, Font = FluentTheme.CaptionFont,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
        };
        layout.Controls.Add(warningsBox);
        layout.Controls.Add(statusLabel);
        layout.SizeChanged += (_, _) =>
        {
            var width = Math.Max(240, layout.ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
            foreach (Control control in layout.Controls) control.Width = width;
        };
        processesView = new ProcessesView();
        systemTab.Controls.Add(layout);
        processesTab.Controls.Add(processesView);
        tabs.TabPages.Add(systemTab);
        tabs.TabPages.Add(processesTab);
        Controls.Add(tabs);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1180, 900);
        MinimumSize = new Size(920, 660);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PCInspector";
        Shown += MainForm_Shown;
        ResumeLayout(false);
    }

    private static FluentCard CreateMetricCard(string title, out Label value, out Label caption)
    {
        var card = new FluentCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, RowCount = 3 };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        value = new Label { Text = "—", Font = FluentTheme.ValueFont, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty };
        caption = new Label { Text = "Waiting for information", ForeColor = FluentTheme.Muted, Font = FluentTheme.CaptionFont,
            Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty };
        stack.Controls.Add(new Label { Text = title, ForeColor = FluentTheme.Muted, Font = FluentTheme.CaptionFont,
            Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        stack.Controls.Add(value, 0, 1);
        stack.Controls.Add(caption, 0, 2);
        card.Controls.Add(stack);
        return card;
    }

    private static FluentCard CreateTableCard(string title, Control grid, int height)
    {
        var card = new FluentCard { Height = height, Margin = new Padding(0, 0, 0, 12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = FluentTheme.Surface };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Font = FluentTheme.SectionFont, Dock = DockStyle.Fill,
            Margin = new Padding(4, 0, 0, 0) }, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        FluentTheme.StyleGrid(grid);
    }
}
