using System.ComponentModel;
using PCInspector.Models;
using PCInspector.Services;

namespace PCInspector;

public sealed class StartupView : UserControl
{
    private readonly DataGridView grid = new();
    private readonly Label status = new();
    private readonly TextBox searchBox = new();
    private readonly ComboBox sourceFilter = new();
    private readonly TextBox warningsBox = new();
    private readonly TextBox selectedCommand = new();
    private readonly FluentButton refreshButton = new() { Text = "Refresh", Width = 114, Height = 36, Secondary = true };
    private readonly FluentButton copyButton = new() { Text = "Copy command", Width = 136, Height = 34, Secondary = true };
    private bool loading;
    private bool changingFilters;
    private DateTime? updatedAt;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<PersistenceItem> ReportItems { get; private set; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> ReportWarnings { get; private set; } =
        ["Startup locations have not been scanned. Open Startup to collect them."];

    public StartupView()
    {
        Dock = DockStyle.Fill;
        BackColor = FluentTheme.Canvas;
        ForeColor = FluentTheme.Text;
        Font = FluentTheme.BodyFont;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(30, 26, 30, 22), ColumnCount = 1, RowCount = 5,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 16) };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label { Text = "Startup", Font = FluentTheme.HeadingFont,
            Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        heading.Controls.Add(new Label { Text = "What starts with Windows, and what keeps running.",
            ForeColor = FluentTheme.Muted, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 0, 0) }, 0, 1);
        refreshButton.AccessibleName = "Refresh startup items";
        refreshButton.Anchor = AnchorStyles.Right;
        refreshButton.Click += async (_, _) => await RefreshAsync();
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(refreshButton, 1, 0);
        layout.Controls.Add(header, 0, 0);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 0, 0, 16) };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        var searchCard = new FluentCard { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 7),
            Margin = new Padding(0, 0, 12, 0) };
        var searchLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = FluentTheme.Surface, Margin = Padding.Empty };
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchLayout.Controls.Add(new Label { Text = "Search", Font = FluentTheme.CaptionFont,
            ForeColor = FluentTheme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty }, 0, 0);
        searchBox.PlaceholderText = "Search startup entries or commands";
        searchBox.BorderStyle = BorderStyle.None;
        searchBox.BackColor = FluentTheme.Surface;
        searchBox.Font = FluentTheme.BodyFont;
        searchBox.Dock = DockStyle.Fill;
        searchBox.AccessibleName = "Search startup entries";
        searchBox.TextChanged += (_, _) => RenderItems();
        searchBox.Margin = new Padding(0, 3, 0, 0);
        searchLayout.Controls.Add(searchBox, 1, 0);
        searchCard.Controls.Add(searchLayout);
        toolbar.Controls.Add(searchCard, 0, 0);
        var filterCard = new FluentCard { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 5), Margin = Padding.Empty };
        sourceFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        sourceFilter.FlatStyle = FlatStyle.Flat;
        sourceFilter.BackColor = FluentTheme.Surface;
        sourceFilter.Font = FluentTheme.BodyFont;
        sourceFilter.Dock = DockStyle.Fill;
        sourceFilter.AccessibleName = "Filter startup source";
        sourceFilter.Items.Add("All locations");
        sourceFilter.SelectedIndex = 0;
        sourceFilter.SelectedIndexChanged += (_, _) => { if (!changingFilters) RenderItems(); };
        filterCard.Controls.Add(sourceFilter);
        toolbar.Controls.Add(filterCard, 1, 0);
        layout.Controls.Add(toolbar, 0, 1);

        status.Text = "Run keys, startup folders, services and scheduled tasks";
        status.Font = FluentTheme.CaptionFont;
        status.ForeColor = FluentTheme.Muted;
        status.Dock = DockStyle.Fill;
        status.AutoEllipsis = true;
        status.Margin = Padding.Empty;
        layout.Controls.Add(status, 0, 2);

        ConfigureGrid();
        var card = new FluentCard { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(14, 12, 14, 14) };
        var contents = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty,
            BackColor = FluentTheme.Surface };
        contents.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        contents.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        contents.Controls.Add(grid, 0, 0);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(8, 13, 8, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Controls.Add(new Label { Text = "Selected command", Font = FluentTheme.CaptionFont,
            ForeColor = FluentTheme.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        selectedCommand.Multiline = true;
        selectedCommand.ReadOnly = true;
        selectedCommand.BorderStyle = BorderStyle.None;
        selectedCommand.BackColor = FluentTheme.Surface;
        selectedCommand.Dock = DockStyle.Fill;
        selectedCommand.Font = FluentTheme.CaptionFont;
        selectedCommand.ScrollBars = ScrollBars.Vertical;
        selectedCommand.AccessibleName = "Full selected startup command";
        selectedCommand.Text = "Select an entry to inspect its full command.";
        footer.Controls.Add(selectedCommand, 0, 1);
        copyButton.Enabled = false;
        copyButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        copyButton.Click += (_, _) => CopyCommand();
        footer.Controls.Add(copyButton, 1, 1);
        contents.Controls.Add(footer, 0, 1);
        card.Controls.Add(contents);
        layout.Controls.Add(card, 0, 3);

        warningsBox.Multiline = true;
        warningsBox.ReadOnly = true;
        warningsBox.ScrollBars = ScrollBars.Vertical;
        warningsBox.BorderStyle = BorderStyle.None;
        warningsBox.BackColor = Color.FromArgb(255, 246, 230);
        warningsBox.ForeColor = Color.FromArgb(132, 92, 34);
        warningsBox.Font = FluentTheme.CaptionFont;
        warningsBox.Height = 60;
        warningsBox.Dock = DockStyle.Fill;
        warningsBox.Margin = new Padding(0, 12, 0, 0);
        warningsBox.Visible = false;
        warningsBox.AccessibleName = "Startup collection warnings";
        layout.Controls.Add(warningsBox, 0, 4);
        Controls.Add(layout);
    }

    public void StartLoading() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (loading || IsDisposed || Disposing) return;
        loading = true;
        refreshButton.Enabled = false;
        status.Text = "Reading startup locations…";
        try
        {
            var result = await Task.Run(PersistenceService.ReadDetailed);
            if (IsDisposed || Disposing) return;
            ReportItems = result.Items;
            ReportWarnings = result.Warnings;
            updatedAt = DateTime.Now;
            changingFilters = true;
            var previous = sourceFilter.SelectedItem as string;
            sourceFilter.Items.Clear();
            sourceFilter.Items.Add("All locations");
            foreach (var source in ReportItems.Select(item => item.Source).Distinct().OrderBy(source => source))
                sourceFilter.Items.Add(source);
            sourceFilter.SelectedItem = previous is not null && sourceFilter.Items.Contains(previous) ? previous : "All locations";
            changingFilters = false;
            warningsBox.Text = string.Join(Environment.NewLine, ReportWarnings);
            warningsBox.Visible = ReportWarnings.Count > 0;
            RenderItems();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                ReportWarnings = [$"Startup scan failed ({ex.GetType().Name}). Displayed entries, if any, are from the previous scan."];
                warningsBox.Text = ReportWarnings[0];
                warningsBox.Visible = true;
                status.Text = "Could not refresh startup entries. Try again.";
            }
        }
        finally
        {
            changingFilters = false;
            loading = false;
            if (!IsDisposed && !Disposing) refreshButton.Enabled = true;
        }
    }

    private void RenderItems()
    {
        var query = searchBox.Text.Trim();
        var source = sourceFilter.SelectedIndex > 0 ? sourceFilter.SelectedItem as string : null;
        var items = ReportItems.Where(item => (source is null || item.Source == source) &&
            (query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Command.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Source.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        grid.Rows.Clear();
        foreach (var item in items)
        {
            var rowIndex = grid.Rows.Add(item.Source, item.Name, item.Command, item.Status);
            grid.Rows[rowIndex].Tag = item;
        }
        status.Text = $"{items.Length} of {ReportItems.Count} entries" +
            (updatedAt is { } updated ? $"  ·  Updated {updated:HH:mm:ss}" : "") +
            (ReportWarnings.Count > 0 ? "  ·  Some locations unavailable" : "");
        ShowSelectedCommand();
    }

    private void ShowSelectedCommand()
    {
        var item = grid.CurrentRow?.Tag as PersistenceItem;
        selectedCommand.Text = item?.Command ?? "Select an entry to inspect its full command.";
        copyButton.Enabled = item is not null;
    }

    private void CopyCommand()
    {
        if (grid.CurrentRow?.Tag is not PersistenceItem item || string.IsNullOrWhiteSpace(item.Command)) return;
        try { Clipboard.SetText(item.Command); }
        catch (System.Runtime.InteropServices.ExternalException)
        { status.Text = "Clipboard is busy. Try copying again."; }
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add("Source", "Location");
        grid.Columns.Add("Name", "Name");
        grid.Columns.Add("Command", "Command");
        grid.Columns.Add("Status", "State");
        grid.Columns["Source"]!.FillWeight = 20;
        grid.Columns["Name"]!.FillWeight = 26;
        grid.Columns["Command"]!.FillWeight = 42;
        grid.Columns["Status"]!.FillWeight = 12;
        FluentTheme.StyleGrid(grid);
        grid.AccessibleName = "Startup entries";
        grid.CurrentCellChanged += (_, _) => ShowSelectedCommand();
    }
}
