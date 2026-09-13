using System.ComponentModel;
using PCInspector.Models;
using PCInspector.Services;

namespace PCInspector;

public sealed class ProcessesView : UserControl
{
    private readonly ProcessSampler sampler = new();
    private readonly ProcessHistory history = new(Environment.ProcessorCount);
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly DataGridView processGrid = CreateGrid();
    private readonly DataGridView historyGrid = CreateGrid();
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly TextBox pathBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Label detail = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ContextMenuStrip processMenu = new();
    private readonly ToolStripMenuItem showFileItem = new("Show file in folder");
    private readonly Func<string, Task> showFileAsync;
    private string? contextPath;
    private bool contextRequestedByMouse;
    private bool sampling;
    private bool rendering;
    private double lastSampleTime;

    public ProcessesView(Func<string, Task>? showFileAsync = null)
    {
        this.showFileAsync = showFileAsync ?? FileLocationService.ShowAsync;
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        status.Text = "Starting process monitor...";
        status.Padding = new Padding(0, 0, 0, 8);
        AddColumn(processGrid, "Name", "Process", 150);
        AddColumn(processGrid, "Pid", "PID", 65, typeof(int));
        AddColumn(processGrid, "Cpu", "CPU %", 110, typeof(double));
        AddColumn(processGrid, "Average", "Avg / 60 s %", 100, typeof(double));
        AddColumn(processGrid, "Peak", "Peak / 60 s %", 105, typeof(double));
        AddColumn(processGrid, "Ram", "RAM MiB", 110, typeof(double));
        AddColumn(processGrid, "Status", "State", 115);
        AddColumn(processGrid, "Path", "Executable path", 230);
        processGrid.Columns["Path"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        processGrid.Columns["Path"]!.MinimumWidth = 180;
        processGrid.Columns["Cpu"]!.DefaultCellStyle.Format = "N1";
        processGrid.Columns["Average"]!.DefaultCellStyle.Format = "N1";
        processGrid.Columns["Peak"]!.DefaultCellStyle.Format = "N1";
        processGrid.Columns["Ram"]!.DefaultCellStyle.Format = "N1";
        // SelectionChanged can fire before CurrentRow points to the newly selected row.
        processGrid.CurrentCellChanged += (_, _) => { if (!rendering) ShowHistory(); };
        processGrid.SortCompare += CompareMissingValues;
        processGrid.CellFormatting += FormatMissingValue;
        processGrid.Sorted += (_, _) =>
        {
            // A user-initiated sort should show the start of the new order.
            if (!rendering && processGrid.Rows.Count > 0 && processGrid.DisplayedRowCount(false) > 0)
                processGrid.FirstDisplayedScrollingRowIndex = 0;
        };
        processGrid.AccessibleName = "Processes sorted by resource usage";
        processMenu.Items.Add(showFileItem);
        processGrid.ContextMenuStrip = processMenu;
        processGrid.CellMouseDown += ProcessGrid_CellMouseDown;
        processGrid.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && processGrid.HitTest(e.X, e.Y).RowIndex < 0)
            {
                contextRequestedByMouse = true;
                contextPath = null;
            }
        };
        processMenu.Opening += ProcessMenu_Opening;
        showFileItem.Click += async (_, _) =>
        {
            // Keep the menu's target even if a refresh changes the selected row.
            var path = contextPath;
            if (path is null) return;
            try { await this.showFileAsync(path); }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                    MessageBox.Show(this, $"Could not show the file. It may have been moved, deleted or become inaccessible.\n{ex.Message}",
                        "PCInspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        pathBox.AccessibleName = "Selected process executable path";
        AddColumn(historyGrid, "Time", "Seconds ago (interval end)", 210, typeof(double));
        AddColumn(historyGrid, "Duration", "Sample duration, s", 165, typeof(double));
        AddColumn(historyGrid, "Cpu", "CPU %", 95, typeof(double));
        foreach (DataGridViewColumn column in historyGrid.Columns)
        {
            column.DefaultCellStyle.Format = "N1";
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        historyGrid.AccessibleName = "Selected process CPU history for the last minute";
        detail.Text = "Select a process to see its CPU samples. Missing values show why they are unavailable.";
        detail.Padding = new Padding(0, 8, 0, 4);
        layout.Controls.Add(status, 0, 0);
        layout.Controls.Add(processGrid, 0, 1);
        layout.Controls.Add(detail, 0, 2);
        layout.Controls.Add(pathBox, 0, 3);
        layout.Controls.Add(historyGrid, 0, 4);
        Controls.Add(layout);
        timer.Tick += async (_, _) => await SampleAsync();
    }

    public void StartMonitoring()
    {
        timer.Start();
        _ = SampleAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            processMenu.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ProcessGrid_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        contextRequestedByMouse = true;
        contextPath = null;
        if (e.RowIndex < 0) return;
        var row = processGrid.Rows[e.RowIndex];
        processGrid.CurrentCell = row.Cells[Math.Max(0, e.ColumnIndex)];
        row.Selected = true;
        contextPath = (row.Tag as ProcessRow)?.Path;
    }

    private void ProcessMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (!contextRequestedByMouse)
            contextPath = (processGrid.CurrentRow?.Tag as ProcessRow)?.Path;
        contextRequestedByMouse = false;
        e.Cancel = contextPath is null;
        showFileItem.Enabled = FileLocationService.CanLocate(contextPath);
        showFileItem.ToolTipText = showFileItem.Enabled ? contextPath : "File path is unavailable or the file no longer exists.";
    }

    private async Task SampleAsync()
    {
        if (sampling || IsDisposed || Disposing) return;
        sampling = true;
        var started = ProcessSampler.NowSeconds;
        try
        {
            var readings = await Task.Run(sampler.Read);
            if (IsDisposed || Disposing) return;
            lastSampleTime = ProcessSampler.NowSeconds;
            var rows = history.Update(readings, lastSampleTime);
            Render(rows);
            var measured = rows.Count(row => row.CpuPercent.HasValue);
            var readable = readings.Count(reading => reading.Identity.HasValue && reading.CpuSeconds.HasValue);
            status.Text = $"Updated {DateTime.Now:HH:mm:ss} • Read took {lastSampleTime - started:0.0} s • " +
                $"CPU measured: {measured}/{readings.Count}; counters readable: {readable} • " +
                (measured == 0 && readable > 0 ? "Waiting for the next sample" : "CPU: share of all logical processors");
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                history.BreakSampling();
                status.Text = $"Refresh failed ({ex.GetType().Name}). Showing previous readings; retrying...";
            }
        }
        finally { sampling = false; }
    }

    private void Render(IReadOnlyList<ProcessRow> rows)
    {
        var selected = processGrid.CurrentRow?.Tag as ProcessRow;
        var sortColumn = processGrid.SortedColumn?.Name ?? "Cpu";
        var direction = processGrid.SortOrder == SortOrder.Ascending
            ? ListSortDirection.Ascending : ListSortDirection.Descending;
        var scroll = processGrid.FirstDisplayedScrollingRowIndex;
        rendering = true;
        try
        {
            processGrid.Rows.Clear();
            foreach (var row in rows)
            {
                var index = processGrid.Rows.Add(row.Name, row.Pid, row.CpuPercent!, row.AverageCpuPercent!,
                    row.PeakCpuPercent!, row.MemoryMiB!, row.Status, row.Path);
                processGrid.Rows[index].Tag = row;
            }
            processGrid.Sort(processGrid.Columns[sortColumn]!, direction);
            processGrid.ClearSelection();
            DataGridViewRow? match = null;
            if (selected is not null)
                match = processGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(gridRow =>
                    gridRow.Tag is ProcessRow row && row.Identity == selected.Identity && row.Pid == selected.Pid);
            match ??= processGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            if (match is not null)
            {
                processGrid.CurrentCell = match.Cells[0];
                match.Selected = true;
            }
            if (scroll >= 0 && scroll < processGrid.Rows.Count && processGrid.DisplayedRowCount(false) > 0)
                processGrid.FirstDisplayedScrollingRowIndex = scroll;
        }
        finally { rendering = false; }
        ShowHistory();
    }

    private void ShowHistory()
    {
        historyGrid.Rows.Clear();
        if (processGrid.CurrentRow?.Tag is not ProcessRow row)
        {
            pathBox.Text = "";
            detail.Text = "Select a process to see its CPU samples.";
            return;
        }
        pathBox.Text = row.Path;
        var coveredSeconds = row.History.Sum(interval => interval.End - interval.Start);
        detail.Text = $"{row.Name} (PID {row.Pid}) • {coveredSeconds:0.0} s measured in the last minute • " +
            "No access = details unavailable; Not observed = absent from the latest scan";
        foreach (var interval in row.History.Reverse())
            historyGrid.Rows.Add(Math.Max(0, lastSampleTime - interval.End),
                interval.End - interval.Start, interval.Percent);
    }

    private void CompareMissingValues(object? sender, DataGridViewSortCompareEventArgs e)
    {
        if (e.Column.ValueType != typeof(double) || (e.CellValue1 is not null && e.CellValue2 is not null))
            return;

        // WinForms reverses this result for descending sorts. Compensate so that
        // unavailable measurements always follow numbers, in either direction.
        var result = e.CellValue1 is null ? (e.CellValue2 is null ? 0 : 1) : -1;
        e.SortResult = processGrid.SortOrder == SortOrder.Descending ? -result : result;
        e.Handled = true;
    }

    private void FormatMissingValue(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Value is not null ||
            processGrid.Columns[e.ColumnIndex].ValueType != typeof(double) ||
            processGrid.Rows[e.RowIndex].Tag is not ProcessRow row)
            return;

        var column = processGrid.Columns[e.ColumnIndex].Name;
        e.Value = column is "Average" or "Peak" ? "No samples" : row.Status switch
        {
            "Limited access" => "No access",
            "Not observed" => "Not seen",
            "Measuring..." when column == "Cpu" => "Waiting",
            _ => "Unavailable"
        };
        e.FormattingApplied = true;
    }

    private static void AddColumn(DataGridView grid, string name, string title, int width, Type? type = null)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = title, Width = width, ValueType = type ?? typeof(string),
            SortMode = DataGridViewColumnSortMode.Automatic
        });
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle,
        DefaultCellStyle = new DataGridViewCellStyle { NullValue = "—", Padding = new Padding(3) }
    };
}
