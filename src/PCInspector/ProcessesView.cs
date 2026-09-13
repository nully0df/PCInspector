using System.ComponentModel;
using PCInspector.Models;
using PCInspector.Services;
using System.Text;

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
    private readonly TextBox investigationBox = new() { Dock = DockStyle.Fill, ReadOnly = true,
        Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label detail = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ContextMenuStrip processMenu = new();
    private readonly ToolStripMenuItem showFileItem = new("Show file in folder");
    private readonly ToolStripMenuItem showTaskManagerItem = new("Open in Task Manager");
    private readonly FluentButton exportButton = new() { Text = "Export report", Width = 140, Height = 38 };
    private readonly Func<string, Task> showFileAsync;
    private readonly Func<int, string, Task> showTaskManagerAsync;
    private string? contextPath;
    private int? contextPid;
    private string? contextName;
    private bool contextRequestedByMouse;
    private bool sampling;
    private bool rendering;
    private double lastSampleTime;
    private int investigationPid;
    private int investigationVersion;
    private IReadOnlyList<ProcessRow> latestRows = [];

    public ProcessesView(Func<string, Task>? showFileAsync = null,
        Func<int, string, Task>? showTaskManagerAsync = null)
    {
        this.showFileAsync = showFileAsync ?? FileLocationService.ShowAsync;
        this.showTaskManagerAsync = showTaskManagerAsync ?? TaskManagerService.OpenAndSelectAsync;
        Dock = DockStyle.Fill;
        Font = FluentTheme.BodyFont;
        BackColor = FluentTheme.Canvas;
        ForeColor = FluentTheme.Text;
        FluentTheme.StyleGrid(processGrid);
        FluentTheme.StyleGrid(historyGrid);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 20), ColumnCount = 1, RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        status.Text = "Starting process monitor...";
        status.Font = FluentTheme.CaptionFont;
        status.ForeColor = FluentTheme.Muted;
        status.Padding = new Padding(0, 0, 0, 14);
        AddColumn(processGrid, "Name", "Process", 180);
        AddColumn(processGrid, "Pid", "PID", 65, typeof(int));
        AddColumn(processGrid, "Cpu", "CPU %", 110, typeof(double));
        AddColumn(processGrid, "Gpu", "GPU %", 100, typeof(double));
        AddColumn(processGrid, "Average", "Avg %", 115, typeof(double));
        AddColumn(processGrid, "Peak", "Peak %", 115, typeof(double));
        AddColumn(processGrid, "Ram", "RAM MiB", 110, typeof(double));
        AddColumn(processGrid, "Status", "State", 140);
        processGrid.Columns["Average"]!.ToolTipText = "Average CPU over measured intervals in the last 60 seconds";
        processGrid.Columns["Peak"]!.ToolTipText = "Peak CPU over measured intervals in the last 60 seconds";
        AddColumn(processGrid, "Path", "Executable path", 230);
        processGrid.Columns["Path"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        processGrid.Columns["Path"]!.MinimumWidth = 180;
        processGrid.Columns["Cpu"]!.DefaultCellStyle.Format = "N1";
        processGrid.Columns["Gpu"]!.DefaultCellStyle.Format = "N1";
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
        processMenu.Items.Add(new ToolStripSeparator());
        processMenu.Items.Add(showTaskManagerItem);
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
        showTaskManagerItem.Click += async (_, _) =>
        {
            var pid = contextPid;
            var name = contextName;
            if (pid is null || name is null) return;
            try { await this.showTaskManagerAsync(pid.Value, name); }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                    MessageBox.Show(this,
                        $"Task Manager was opened, but the process could not be selected.\n{ex.Message}",
                        "PCInspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        pathBox.AccessibleName = "Selected process executable path";
        pathBox.BorderStyle = BorderStyle.None;
        pathBox.BackColor = FluentTheme.Surface;
        pathBox.ForeColor = FluentTheme.Muted;
        pathBox.Font = FluentTheme.CaptionFont;
        processMenu.Font = FluentTheme.BodyFont;
        investigationBox.AccessibleName = "Selected process investigation details";
        investigationBox.BorderStyle = BorderStyle.None;
        investigationBox.BackColor = FluentTheme.Surface;
        investigationBox.ForeColor = FluentTheme.Text;
        investigationBox.Font = FluentTheme.CaptionFont;
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
        detail.Font = FluentTheme.CaptionFont;
        detail.Padding = new Padding(0, 0, 0, 6);
        var processCard = new FluentCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        processCard.Controls.Add(processGrid);
        var historyCard = new FluentCard { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var historyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, ColumnCount = 1, RowCount = 4,
            Margin = Padding.Empty
        };
        historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        historyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        historyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        historyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        historyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        historyLayout.Controls.Add(detail, 0, 0);
        historyLayout.Controls.Add(pathBox, 0, 1);
        historyLayout.Controls.Add(investigationBox, 0, 2);
        historyLayout.Controls.Add(historyGrid, 0, 3);
        historyCard.Controls.Add(historyLayout);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.Controls.Add(new Label
        {
            Text = "Processes", Font = FluentTheme.HeadingFont, Dock = DockStyle.Fill,
            AutoSize = true, Margin = Padding.Empty
        }, 0, 0);
        exportButton.Anchor = AnchorStyles.Right;
        exportButton.AccessibleName = "Export process investigation report";
        exportButton.Click += async (_, _) => await ExportReportAsync();
        header.Controls.Add(exportButton, 1, 0);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(status, 0, 1);
        layout.Controls.Add(processCard, 0, 2);
        layout.Controls.Add(historyCard, 0, 3);
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
            sampler.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ProcessGrid_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        contextRequestedByMouse = true;
        contextPath = null;
        contextPid = null;
        contextName = null;
        if (e.RowIndex < 0) return;
        var row = processGrid.Rows[e.RowIndex];
        processGrid.CurrentCell = row.Cells[Math.Max(0, e.ColumnIndex)];
        row.Selected = true;
        if (row.Tag is ProcessRow process)
        {
            contextPath = process.Path;
            contextPid = process.Pid > 0 ? process.Pid : null;
            contextName = process.Name;
        }
    }

    private void ProcessMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (!contextRequestedByMouse)
        {
            if (processGrid.CurrentRow?.Tag is ProcessRow process)
            {
                contextPath = process.Path;
                contextPid = process.Pid > 0 ? process.Pid : null;
                contextName = process.Name;
            }
            else
            {
                contextPath = null;
                contextPid = null;
                contextName = null;
            }
        }
        contextRequestedByMouse = false;
        e.Cancel = contextPid is null;
        showFileItem.Enabled = FileLocationService.CanLocate(contextPath);
        showFileItem.ToolTipText = showFileItem.Enabled ? contextPath : "File path is unavailable or the file no longer exists.";
        showTaskManagerItem.Enabled = contextPid.HasValue;
        showTaskManagerItem.ToolTipText = showTaskManagerItem.Enabled
            ? $"Open Task Manager and select {contextName} (PID {contextPid})"
            : "Process ID is unavailable.";
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
        latestRows = rows;
        rendering = true;
        try
        {
            processGrid.Rows.Clear();
            foreach (var row in rows)
            {
                var index = processGrid.Rows.Add(row.Name, row.Pid, row.CpuPercent!, row.GpuPercent!,
                    row.AverageCpuPercent!, row.PeakCpuPercent!, row.MemoryMiB!, row.Status, row.Path);
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
            investigationBox.Text = "";
            investigationPid = 0;
            investigationVersion++;
            return;
        }
        pathBox.Text = row.Path;
        var coveredSeconds = row.History.Sum(interval => interval.End - interval.Start);
        detail.Text = $"{row.Name} (PID {row.Pid}) • {coveredSeconds:0.0} s measured in the last minute • " +
            "No access = details unavailable; Not observed = absent from the latest scan";
        foreach (var interval in row.History.Reverse())
            historyGrid.Rows.Add(Math.Max(0, lastSampleTime - interval.End),
                interval.End - interval.Start, interval.Percent);
        if (investigationPid != row.Pid)
        {
            investigationPid = row.Pid;
            var version = ++investigationVersion;
            investigationBox.Text = BasicInvestigation(row);
            _ = LoadInvestigationAsync(row, version);
        }
    }

    private string BasicInvestigation(ProcessRow row)
    {
        var started = row.Identity is { } identity
            ? new DateTime(identity.StartTimeUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "Unavailable";
        return $"Command line: {row.CommandLine ?? "Unavailable"}{Environment.NewLine}" +
            $"Process tree: {ProcessTree(row)}{Environment.NewLine}" +
            $"Started: {started}{Environment.NewLine}" +
            $"GPU: {(row.GpuPercent is { } gpu ? $"{gpu:N1}%" : "Unavailable")}{Environment.NewLine}" +
            "Signature, hash and network connections: loading...";
    }

    private string ProcessTree(ProcessRow row)
    {
        var chain = new List<string> { $"{row.Name} (PID {row.Pid})" };
        var parentPid = row.ParentPid;
        for (var level = 0; level < 6 && parentPid is { } pid; level++)
        {
            var parent = latestRows.FirstOrDefault(candidate => candidate.Pid == pid);
            if (parent is null) break;
            chain.Add($"{parent.Name} (PID {parent.Pid})");
            parentPid = parent.ParentPid;
        }
        chain.Reverse();
        return string.Join(" → ", chain);
    }

    private async Task LoadInvestigationAsync(ProcessRow row, int version)
    {
        try
        {
            var fileTask = FileAnalysisService.AnalyzeAsync(row.Path);
            var networkTask = Task.Run(() => NetworkConnectionService.ReadForProcess(row.Pid));
            await Task.WhenAll(fileTask, networkTask);
            if (IsDisposed || Disposing || version != investigationVersion || investigationPid != row.Pid) return;

            var builder = new StringBuilder(BasicInvestigation(row));
            var analysis = await fileTask;
            builder.AppendLine();
            if (analysis is null)
                builder.Append("Signature/hash: unavailable");
            else
            {
                builder.AppendLine($"Publisher: {analysis.Publisher}");
                builder.AppendLine($"Signature: {analysis.Signature}");
                builder.Append($"SHA-256: {analysis.Sha256}");
            }

            var connections = await networkTask;
            builder.AppendLine();
            builder.Append("Network: ");
            if (connections.Count == 0)
                builder.Append("no active TCP/UDP endpoints");
            else
            {
                builder.AppendLine();
                foreach (var connection in connections.Take(8))
                    builder.AppendLine($"  {connection.Protocol} {connection.LocalEndpoint} → {connection.RemoteEndpoint} ({connection.State})");
                if (connections.Count > 8) builder.Append($"  ... and {connections.Count - 8} more");
            }
            investigationBox.Text = builder.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!IsDisposed && !Disposing && version == investigationVersion)
                investigationBox.Text = $"{BasicInvestigation(row)}{Environment.NewLine}{Environment.NewLine}" +
                    $"Additional details unavailable ({ex.GetType().Name}).";
        }
    }

    private async Task ExportReportAsync()
    {
        var rows = processGrid.Rows.Cast<DataGridViewRow>()
            .Select(row => row.Tag as ProcessRow).Where(row => row is not null).Cast<ProcessRow>().ToArray();
        if (rows.Length == 0) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "JSON report (*.json)|*.json|HTML report (*.html)|*.html",
            DefaultExt = "json", AddExtension = true,
            FileName = $"pcinspector-processes-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await Task.Run(() => ExportReportService.Write(dialog.FileName, rows));
            if (!IsDisposed && !Disposing) status.Text = $"Report exported: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!IsDisposed && !Disposing)
                MessageBox.Show(this, $"Could not export the report.\n{ex.Message}",
                    "PCInspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
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
