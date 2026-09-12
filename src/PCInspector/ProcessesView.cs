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
    private bool sampling;
    private bool rendering;
    private double lastSampleTime;

    public ProcessesView()
    {
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
        AddColumn(processGrid, "Cpu", "CPU %", 75, typeof(double));
        AddColumn(processGrid, "Average", "Avg / 60 s %", 100, typeof(double));
        AddColumn(processGrid, "Peak", "Peak / 60 s %", 105, typeof(double));
        AddColumn(processGrid, "Ram", "RAM MiB", 90, typeof(double));
        AddColumn(processGrid, "Status", "State", 115);
        AddColumn(processGrid, "Path", "Executable path", 230);
        processGrid.Columns["Path"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        processGrid.Columns["Path"]!.MinimumWidth = 180;
        processGrid.Columns["Cpu"]!.DefaultCellStyle.Format = "N1";
        processGrid.Columns["Average"]!.DefaultCellStyle.Format = "N1";
        processGrid.Columns["Peak"]!.DefaultCellStyle.Format = "N1";
        processGrid.Columns["Ram"]!.DefaultCellStyle.Format = "N1";
        processGrid.SelectionChanged += (_, _) => { if (!rendering) ShowHistory(); };
        processGrid.AccessibleName = "Processes sorted by resource usage";
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
        detail.Text = "Select a process to see its CPU samples. — means no measurement.";
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
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    private async Task SampleAsync()
    {
        if (sampling || IsDisposed || Disposing) return;
        sampling = true;
        try
        {
            var readings = await Task.Run(sampler.Read);
            if (IsDisposed || Disposing) return;
            lastSampleTime = ProcessSampler.NowSeconds;
            Render(history.Update(readings, lastSampleTime));
            status.Text = $"Updated {DateTime.Now:HH:mm:ss} • {readings.Count} processes • " +
                "CPU: share of all logical processors • RAM: working set";
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
            if (scroll >= 0 && scroll < processGrid.Rows.Count)
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
            "— = unavailable / waiting; Not observed = absent from the latest scan";
        foreach (var interval in row.History.Reverse())
            historyGrid.Rows.Add(Math.Max(0, lastSampleTime - interval.End),
                interval.End - interval.Start, interval.Percent);
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
