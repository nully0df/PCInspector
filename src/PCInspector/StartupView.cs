using PCInspector.Models;
using PCInspector.Services;

namespace PCInspector;

public sealed class StartupView : UserControl
{
    private readonly DataGridView grid = new();
    private readonly Label status = new();
    private readonly FluentButton refreshButton = new() { Text = "Refresh", Width = 124, Height = 38 };
    private bool loading;

    public StartupView()
    {
        Dock = DockStyle.Fill;
        BackColor = FluentTheme.Canvas;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 20), ColumnCount = 1, RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        header.Controls.Add(new Label
        {
            Text = "Startup & persistence", Font = FluentTheme.HeadingFont, AutoSize = true,
            Dock = DockStyle.Fill, Margin = Padding.Empty
        }, 0, 0);
        refreshButton.AccessibleName = "Refresh startup items";
        refreshButton.Click += async (_, _) => await RefreshAsync();
        header.Controls.Add(refreshButton, 1, 0);
        layout.Controls.Add(header, 0, 0);

        status.Text = "Run keys, automatic services and scheduled tasks";
        status.Font = FluentTheme.CaptionFont;
        status.ForeColor = FluentTheme.Muted;
        status.AutoSize = true;
        status.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(status, 0, 1);

        ConfigureGrid();
        var card = new FluentCard { Dock = DockStyle.Fill, Margin = Padding.Empty };
        card.Controls.Add(grid);
        layout.Controls.Add(card, 0, 2);
        Controls.Add(layout);
    }

    public void StartLoading() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (loading || IsDisposed || Disposing) return;
        loading = true;
        refreshButton.Enabled = false;
        status.Text = "Reading startup locations...";
        try
        {
            var items = await Task.Run(PersistenceService.Read);
            if (IsDisposed || Disposing) return;
            grid.Rows.Clear();
            foreach (var item in items) grid.Rows.Add(item.Source, item.Name, item.Command, item.Status);
            status.Text = $"{items.Count} startup entries found • review paths and publishers before disabling anything";
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing) status.Text = $"Startup scan unavailable ({ex.GetType().Name}).";
        }
        finally
        {
            loading = false;
            if (!IsDisposed && !Disposing) refreshButton.Enabled = true;
        }
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.Columns.Add("Source", "Source");
        grid.Columns.Add("Name", "Name");
        grid.Columns.Add("Command", "Command");
        grid.Columns.Add("Status", "Status");
        grid.Columns["Source"]!.FillWeight = 18;
        grid.Columns["Name"]!.FillWeight = 20;
        grid.Columns["Command"]!.FillWeight = 47;
        grid.Columns["Status"]!.FillWeight = 15;
        FluentTheme.StyleGrid(grid);
        grid.AccessibleName = "Startup persistence entries";
    }
}
