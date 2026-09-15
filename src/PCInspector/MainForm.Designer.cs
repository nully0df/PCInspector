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
    private StartupView startupView = null!;
    private Label memoryValue = null!;
    private Label memoryCaption = null!;
    private Label uptimeValue = null!;
    private Label volumeValue = null!;
    private Label volumeCaption = null!;
    private Label deviceName = null!;
    private Label deviceDescription = null!;
    private Label graphicsName = null!;
    private Label graphicsDriver = null!;
    private Label graphicsCaption = null!;
    private UsageBar memoryBar = null!;
    private NavigationButton[] navigationButtons = [];
    private Control[] pages = [];
    private bool startupLoaded;

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
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var sidebar = CreateSidebar();
        var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = FluentTheme.Canvas };
        var overview = CreateOverview();
        processesView = new ProcessesView { Visible = false };
        startupView = new StartupView { Visible = false };
        processesView.ReportStartupProvider = () => (startupView.ReportItems, startupView.ReportWarnings);
        pages = [overview, processesView, startupView];
        content.Controls.AddRange(pages);
        overview.BringToFront();
        shell.Controls.Add(sidebar, 0, 0);
        shell.Controls.Add(content, 1, 0);
        Controls.Add(shell);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(1100, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PCInspector";
        Shown += MainForm_Shown;
        ResumeLayout(false);
    }

    private Control CreateSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(16, 32, 16, 22),
            BackColor = FluentTheme.Sidebar, ColumnCount = 1, RowCount = 5
        };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 149));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 79));
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(8, 0, 0, 0) };
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        brand.Controls.Add(new Label { Text = "PCInspector", Font = new Font("Segoe UI Semibold", 16),
            Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        brand.Controls.Add(new Label { Text = "A closer look at your PC", Font = new Font("Segoe UI", 8.5f),
            ForeColor = FluentTheme.Muted, Dock = DockStyle.Fill, Margin = new Padding(1, 0, 0, 0) }, 0, 1);
        sidebar.Controls.Add(brand, 0, 0);
        sidebar.Controls.Add(new Label { Text = "WORKSPACE", Font = new Font("Segoe UI Semibold", 8),
            ForeColor = FluentTheme.Muted, Dock = DockStyle.Fill, Margin = new Padding(11, 0, 0, 0) }, 0, 1);
        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Margin = Padding.Empty };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var names = new[] { "Overview", "Activity", "Startup" };
        navigationButtons = new NavigationButton[3];
        for (var index = 0; index < names.Length; index++)
        {
            var pageIndex = index;
            navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            var button = new NavigationButton { Text = names[index], Symbol = index, Selected = index == 0,
                Dock = DockStyle.Fill, AccessibleName = $"Show {names[index]}" };
            button.Click += (_, _) => ShowPage(pageIndex);
            navigationButtons[index] = button;
            navigation.Controls.Add(button, 0, index);
        }
        sidebar.Controls.Add(navigation, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Margin = new Padding(9, 0, 0, 0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        footer.Controls.Add(new Label { Text = "THIS COMPUTER", ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Semibold", 8), Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        footer.Controls.Add(new Label { Text = Environment.MachineName, Font = FluentTheme.SectionFont,
            Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty }, 0, 1);
        footer.Controls.Add(new Label { Text = "Local inspection", ForeColor = FluentTheme.Muted,
            Font = FluentTheme.CaptionFont, Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 2);
        sidebar.Controls.Add(footer, 0, 4);
        return sidebar;
    }

    private Control CreateOverview()
    {
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(30, 24, 30, 20),
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            BackColor = FluentTheme.Canvas, Margin = Padding.Empty };
        var header = new TableLayoutPanel { Height = 64, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label { Text = "Overview", Font = FluentTheme.HeadingFont,
            Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        heading.Controls.Add(new Label { Text = "Your hardware. Clearly.", ForeColor = FluentTheme.Muted,
            Dock = DockStyle.Fill, Margin = new Padding(2, 3, 0, 0) }, 0, 1);
        refreshButton = new FluentButton { Text = "Refresh", Width = 114, Height = 36,
            Anchor = AnchorStyles.Right, Secondary = true, AccessibleName = "Refresh system information" };
        refreshButton.Click += RefreshButton_Click;
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(refreshButton, 1, 0);
        layout.Controls.Add(header);

        var hero = new FluentCard { Height = 104, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(22, 10, 22, 10) };
        var heroLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = FluentTheme.Surface };
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heroLayout.Controls.Add(new DeviceIllustration { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 18, 0) }, 0, 0);
        var device = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Margin = new Padding(12, 6, 0, 0) };
        device.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        device.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        device.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        device.Controls.Add(new Label { Text = "YOUR DEVICE", Font = new Font("Segoe UI Semibold", 8),
            ForeColor = FluentTheme.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        deviceName = new Label { Text = Environment.MachineName, Font = new Font("Segoe UI Semibold", 19),
            Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty };
        deviceDescription = new Label { Text = "Reading hardware information…", ForeColor = FluentTheme.Muted,
            Font = FluentTheme.CaptionFont, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty };
        device.Controls.Add(deviceName, 0, 1);
        device.Controls.Add(deviceDescription, 0, 2);
        heroLayout.Controls.Add(device, 1, 0);
        hero.Controls.Add(heroLayout);
        layout.Controls.Add(hero);

        var metrics = new TableLayoutPanel { Height = 100, ColumnCount = 3, Margin = new Padding(0, 0, 0, 12) };
        for (var i = 0; i < 3; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        var memoryCard = CreateMetricCard("Memory", out memoryValue, out memoryCaption);
        memoryBar = new UsageBar { Dock = DockStyle.Bottom, Height = 4, BackColor = FluentTheme.Surface };
        memoryCard.Controls.Add(memoryBar);
        metrics.Controls.Add(memoryCard, 0, 0);
        metrics.Controls.Add(CreateMetricCard("System uptime", out uptimeValue, out var uptimeCaption), 1, 0);
        metrics.Controls.Add(CreateMetricCard("Storage volumes", out volumeValue, out volumeCaption), 2, 0);
        uptimeCaption.Text = "Since Windows started";
        volumeCaption.Text = "Reading storage…";
        metrics.Controls[2].Margin = Padding.Empty;
        layout.Controls.Add(metrics);

        var details = new TableLayoutPanel { Height = 200, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 59));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));
        summaryGrid = new DataGridView();
        ConfigureGrid(summaryGrid);
        summaryGrid.Columns.Add("Property", "Property");
        summaryGrid.Columns.Add("Value", "Value");
        summaryGrid.Columns[0].FillWeight = 27;
        summaryGrid.Columns[0].DefaultCellStyle.ForeColor = FluentTheme.Muted;
        summaryGrid.Columns[1].FillWeight = 73;
        summaryGrid.ColumnHeadersVisible = false;
        summaryGrid.AccessibleName = "System information";
        var deviceCard = CreateTableCard("Device details", summaryGrid, 200);
        deviceCard.Dock = DockStyle.Fill;
        deviceCard.Margin = new Padding(0, 0, 16, 0);
        details.Controls.Add(deviceCard, 0, 0);
        details.Controls.Add(CreateGraphicsCard(), 1, 0);
        layout.Controls.Add(details);

        disksGrid = new DataGridView();
        ConfigureGrid(disksGrid);
        disksGrid.Columns.Add("Drive", "Volume");
        disksGrid.Columns.Add("Type", "Type");
        disksGrid.Columns.Add("Total", "Capacity");
        disksGrid.Columns.Add("Free", "Available");
        disksGrid.Columns.Add("Used", "Used");
        disksGrid.Columns.Add("Status", "Status");
        disksGrid.AccessibleName = "Local drives and volumes";
        layout.Controls.Add(CreateTableCard("Storage", disksGrid, 154));

        warningsBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Height = 60, Visible = false, BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(255, 246, 230), ForeColor = Color.FromArgb(132, 92, 34),
            Font = FluentTheme.CaptionFont, AccessibleName = "Collection warnings", Margin = new Padding(0, 0, 0, 8) };
        statusLabel = new Label { Text = "Ready", Height = 24, ForeColor = FluentTheme.Muted,
            Font = FluentTheme.CaptionFont, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        layout.Controls.Add(warningsBox);
        layout.Controls.Add(statusLabel);
        layout.SizeChanged += (_, _) => ResizeOverviewChildren(layout);
        layout.Layout += (_, _) => ResizeOverviewChildren(layout);
        return layout;
    }

    private static void ResizeOverviewChildren(FlowLayoutPanel layout)
    {
        var width = Math.Max(240, layout.ClientSize.Width - layout.Padding.Horizontal - 2);
        foreach (Control control in layout.Controls)
            if (control.Width != width) control.Width = width;
    }

    private FluentCard CreateGraphicsCard()
    {
        var card = new FluentCard { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(22, 14, 22, 14) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, RowCount = 4 };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        stack.Controls.Add(new Label { Text = "Graphics", Font = FluentTheme.SectionFont,
            Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        graphicsName = new Label { Text = "Reading graphics adapters…", Font = new Font("Segoe UI Semibold", 14),
            Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty };
        graphicsDriver = new Label { Text = "Driver information will appear here", Font = FluentTheme.CaptionFont,
            ForeColor = FluentTheme.Muted, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty };
        graphicsCaption = new LinkLabel { Text = "See per-process GPU use in Activity", Font = FluentTheme.CaptionFont,
            LinkColor = FluentTheme.Accent, ActiveLinkColor = FluentTheme.Accent, LinkBehavior = LinkBehavior.HoverUnderline,
            Dock = DockStyle.Fill, Margin = Padding.Empty, Cursor = Cursors.Hand, TabStop = true };
        ((LinkLabel)graphicsCaption).LinkClicked += (_, _) => ShowPage(1);
        stack.Controls.Add(graphicsName, 0, 1);
        stack.Controls.Add(graphicsDriver, 0, 2);
        stack.Controls.Add(graphicsCaption, 0, 3);
        card.Controls.Add(stack);
        return card;
    }

    private void ShowPage(int index)
    {
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = i == index;
            navigationButtons[i].Selected = i == index;
        }
        pages[index].BringToFront();
        if (index == 2 && !startupLoaded)
        {
            startupLoaded = true;
            startupView.StartLoading();
        }
    }

    private static FluentCard CreateMetricCard(string title, out Label value, out Label caption)
    {
        var card = new FluentCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0), Padding = new Padding(20, 12, 20, 12) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, RowCount = 3, Margin = Padding.Empty };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
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
        var card = new FluentCard { Height = height, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(18, 14, 18, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = FluentTheme.Surface, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
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
