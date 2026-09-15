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
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly TextBox pathBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox investigationBox = new() { Dock = DockStyle.Fill, ReadOnly = true,
        Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label detail = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ContextMenuStrip processMenu = new();
    private readonly ToolStripMenuItem showFileItem = new("Show file in folder");
    private readonly ToolStripMenuItem showTaskManagerItem = new("Open in Task Manager");
    private readonly FluentButton exportButton = new() { Text = "Export report", Width = 140, Height = 38 };
    private readonly FluentButton pauseButton = new() { Text = "Pause view", Width = 112, Height = 34, Secondary = true };
    private readonly FluentButton snapshotButton = new() { Text = "Take snapshot", Width = 130, Height = 34, Secondary = true };
    private readonly FluentButton compareButton = new() { Text = "Compare", Width = 100, Height = 34, Secondary = true, Enabled = false };
    private readonly TextBox searchBox = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        PlaceholderText = "Search name, PID, path or command…", AccessibleName = "Search processes" };
    private readonly ComboBox filterBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox metricBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ResourceChart chart = new();
    private readonly Label snapshotStatus = new() { AutoSize = true, ForeColor = FluentTheme.Muted, Font = FluentTheme.CaptionFont };
    private readonly FluentButton refreshDetails = new() { Text = "Refresh details", Width = 124, Height = 30, Secondary = true };
    private readonly Func<string, Task> showFileAsync;
    private readonly Func<int, string, Task> showTaskManagerAsync;
    private string? contextPath;
    private int? contextPid;
    private string? contextName;
    private ProcessIdentity? contextIdentity;
    private bool contextRequestedByMouse;
    private bool sampling;
    private bool openingTaskManager;
    private bool rendering;
    private double lastSampleTime;
    private int investigationPid;
    private int investigationVersion;
    private IReadOnlyList<ProcessRow> latestRows = [];
    private IReadOnlyList<ProcessRow> liveRows = [];
    private ProcessIdentity? investigationIdentity;
    private string? investigationPath;
    private string extraDetails = "";
    private bool paused;
    private double displayedAt;
    private DateTime displayedUtc;
    private ActivitySnapshot? baseline;
    private bool detailsLoading;
    private Task investigationTask = Task.CompletedTask;
    private CancellationTokenSource? detailsCancellation;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SystemSnapshot? ReportSystemSnapshot { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<(IReadOnlyList<PersistenceItem> Items, IReadOnlyList<string> Warnings)>? ReportStartupProvider { get; set; }

    public ProcessesView(Func<string, Task>? showFileAsync = null,
        Func<int, string, Task>? showTaskManagerAsync = null)
    {
        this.showFileAsync = showFileAsync ?? FileLocationService.ShowAsync;
        this.showTaskManagerAsync = showTaskManagerAsync ?? ((pid, name) =>
            TaskManagerService.OpenAndSelectAsync(pid, name, contextIdentity?.StartTimeUtcTicks));
        Dock = DockStyle.Fill;
        Font = FluentTheme.BodyFont;
        BackColor = FluentTheme.Canvas;
        ForeColor = FluentTheme.Text;
        FluentTheme.StyleGrid(processGrid);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(30, 24, 30, 22), ColumnCount = 1, RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        status.Text = "Starting process monitor...";
        status.Font = FluentTheme.CaptionFont;
        status.ForeColor = FluentTheme.Muted;
        status.Padding = new Padding(2, 3, 0, 0);
        status.AutoSize = false;
        status.AutoEllipsis = true;
        AddColumn(processGrid, "Name", "Process", 180);
        AddColumn(processGrid, "Pid", "PID", 65, typeof(int));
        AddColumn(processGrid, "Cpu", "CPU %", 105, typeof(double));
        AddColumn(processGrid, "Gpu", "GPU %", 100, typeof(double));
        AddColumn(processGrid, "Average", "Avg %", 95, typeof(double));
        AddColumn(processGrid, "Peak", "Peak %", 95, typeof(double));
        AddColumn(processGrid, "Ram", "Memory MiB", 130, typeof(double));
        AddColumn(processGrid, "Status", "State", 130);
        processGrid.Columns["Average"]!.ToolTipText = "Average CPU over measured intervals in the last 60 seconds";
        processGrid.Columns["Peak"]!.ToolTipText = "Peak CPU over measured intervals in the last 60 seconds";
        AddColumn(processGrid, "Path", "Executable path", 230);
        processGrid.Columns["Path"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        processGrid.Columns["Path"]!.MinimumWidth = 180;
        processGrid.Columns["Path"]!.Visible = false;
        processGrid.Columns["Average"]!.Visible = false;
        processGrid.Columns["Peak"]!.Visible = false;
        AddColumn(processGrid, "Read", "Read MiB/s", 120, typeof(double));
        AddColumn(processGrid, "Write", "Write MiB/s", 125, typeof(double));
        processGrid.Columns["Status"]!.DisplayIndex = processGrid.Columns.Count - 1;
        processGrid.Columns["Name"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        processGrid.Columns["Name"]!.MinimumWidth = 150;
        foreach (var io in new[] { "Read", "Write" })
        {
            processGrid.Columns[io]!.DefaultCellStyle.Format = "N2";
            processGrid.Columns[io]!.ToolTipText = "Process I/O: file, network and device transfers; not physical disk throughput.";
        }
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
        processMenu.Items.Add(new ToolStripSeparator());
        processMenu.Items.Add("Copy PID", null, (_, _) => CopyText(contextPid?.ToString()));
        processMenu.Items.Add("Copy file path", null, (_, _) => CopyText(contextPath));
        processGrid.ContextMenuStrip = processMenu;
        processGrid.CellMouseDown += ProcessGrid_CellMouseDown;
        processGrid.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && processGrid.HitTest(e.X, e.Y).RowIndex < 0)
            {
                contextRequestedByMouse = true;
                contextPath = null;
                contextPid = null;
                contextName = null;
                contextIdentity = null;
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
            if (pid is null || name is null || openingTaskManager) return;
            openingTaskManager = true;
            showTaskManagerItem.Enabled = false;
            try { await this.showTaskManagerAsync(pid.Value, name); }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                    MessageBox.Show(this,
                        $"Could not complete the Task Manager action.\n{ex.Message}",
                        "PCInspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                openingTaskManager = false;
                if (!IsDisposed && !Disposing) showTaskManagerItem.Enabled = contextPid.HasValue;
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
        detail.Text = "Select a process to see its CPU samples. Missing values show why they are unavailable.";
        detail.Font = FluentTheme.CaptionFont;
        detail.Padding = new Padding(0, 0, 0, 6);
        var processCard = new FluentCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        processCard.Controls.Add(processGrid);
        var historyCard = new FluentCard { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var historyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty
        };
        historyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        var chartLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = FluentTheme.Surface, Margin = new Padding(0, 0, 22, 0) };
        chartLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        chartLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        metricBox.Items.AddRange(["CPU", "GPU", "Memory", "Read I/O", "Write I/O"]);
        metricBox.SelectedIndex = 0;
        metricBox.Font = FluentTheme.CaptionFont;
        metricBox.AccessibleName = "Resource shown in history chart";
        metricBox.SelectedIndexChanged += (_, _) => UpdateChart();
        chartLayout.Controls.Add(metricBox, 0, 0);
        chartLayout.Controls.Add(chart, 0, 1);
        var infoLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            BackColor = FluentTheme.Surface, Margin = Padding.Empty };
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        detail.AutoSize = false;
        detail.AutoEllipsis = true;
        detail.Font = FluentTheme.SectionFont;
        infoLayout.Controls.Add(detail, 0, 0);
        infoLayout.Controls.Add(pathBox, 0, 1);
        infoLayout.Controls.Add(investigationBox, 0, 2);
        refreshDetails.Click += (_, _) =>
        {
            if (processGrid.CurrentRow?.Tag is ProcessRow row && !detailsLoading)
                investigationTask = LoadInvestigationAsync(row, ++investigationVersion);
        };
        infoLayout.Controls.Add(refreshDetails, 0, 3);
        historyLayout.Controls.Add(chartLayout, 0, 0);
        historyLayout.Controls.Add(infoLayout, 1, 0);
        historyCard.Controls.Add(historyLayout);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 475));
        header.Controls.Add(new Label
        {
            Text = "Activity", Font = FluentTheme.HeadingFont, Dock = DockStyle.Fill,
            AutoSize = true, Margin = Padding.Empty
        }, 0, 0);
        exportButton.Width = 108;
        exportButton.Height = 34;
        exportButton.Text = "Export…";
        exportButton.AccessibleName = "Export process investigation report";
        exportButton.Click += async (_, _) => await ExportReportAsync();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty };
        actions.Controls.AddRange([exportButton, compareButton, snapshotButton, pauseButton]);
        pauseButton.Click += (_, _) => TogglePause();
        snapshotButton.Click += (_, _) => TakeSnapshot();
        compareButton.Click += (_, _) => CompareSnapshot();
        header.Controls.Add(actions, 1, 0);
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        var searchCard = new FluentCard { Dock = DockStyle.Fill, Padding = new Padding(12, 9, 10, 5), Margin = new Padding(0, 0, 12, 10) };
        searchBox.Font = FluentTheme.BodyFont;
        searchCard.Controls.Add(searchBox);
        searchCard.Controls.Add(new Label { Text = "Search", Dock = DockStyle.Left, Width = 60,
            Font = FluentTheme.CaptionFont, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft,
            BackColor = FluentTheme.Surface });
        searchBox.TextChanged += (_, _) => Render(latestRows);
        filterBox.Items.AddRange(["All processes", "CPU above 10%", "GPU above 1%", "Started in last 5 min", "Limited access"]);
        filterBox.SelectedIndex = 0;
        filterBox.Font = FluentTheme.BodyFont;
        filterBox.FlatStyle = FlatStyle.Flat;
        filterBox.BackColor = FluentTheme.Surface;
        filterBox.AccessibleName = "Filter processes";
        filterBox.Margin = new Padding(0, 5, 0, 0);
        filterBox.SelectedIndexChanged += (_, _) => Render(latestRows);
        snapshotStatus.Text = "No snapshot yet";
        snapshotStatus.Margin = new Padding(6, 8, 0, 0);
        toolbar.Controls.Add(searchCard, 0, 0);
        toolbar.Controls.Add(filterBox, 1, 0);
        toolbar.Controls.Add(snapshotStatus, 2, 0);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(toolbar, 0, 1);
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(processCard, 0, 3);
        layout.Controls.Add(historyCard, 0, 4);
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
            detailsCancellation?.Cancel();
            detailsCancellation?.Dispose();
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
        contextIdentity = null;
        if (e.RowIndex < 0) return;
        var row = processGrid.Rows[e.RowIndex];
        processGrid.CurrentCell = row.Cells[Math.Max(0, e.ColumnIndex)];
        row.Selected = true;
        if (row.Tag is ProcessRow process)
        {
            contextPath = process.Path;
            contextPid = process.Pid > 0 ? process.Pid : null;
            contextName = process.Name;
            contextIdentity = process.Identity;
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
                contextIdentity = process.Identity;
            }
            else
            {
                contextPath = null;
                contextPid = null;
                contextName = null;
                contextIdentity = null;
            }
        }
        contextRequestedByMouse = false;
        e.Cancel = contextPid is null;
        showFileItem.Enabled = FileLocationService.CanLocate(contextPath);
        showFileItem.ToolTipText = showFileItem.Enabled ? contextPath : "File path is unavailable or the file no longer exists.";
        showTaskManagerItem.Enabled = contextPid.HasValue && !openingTaskManager;
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
            liveRows = history.Update(readings, lastSampleTime);
            if (!paused)
            {
                displayedAt = lastSampleTime;
                displayedUtc = DateTime.UtcNow;
                Render(liveRows);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                history.BreakSampling(ProcessSampler.NowSeconds);
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
            foreach (var row in rows.Where(MatchesFilter))
            {
                var index = processGrid.Rows.Add(row.Name, row.Pid, row.CpuPercent!, row.GpuPercent!,
                    row.AverageCpuPercent!, row.PeakCpuPercent!, row.MemoryMiB!, row.Status, row.Path,
                    row.ReadMiBPerSecond!, row.WriteMiBPerSecond!);
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
        var measured = rows.Count(row => row.CpuPercent.HasValue);
        status.Text = $"{(paused ? "Paused view" : "Live")}  ·  {processGrid.Rows.Count} of {rows.Count} processes  ·  " +
            $"CPU readable {measured}/{rows.Count}  ·  {(paused ? "Sampling continues" : "I/O includes file, network and device transfers")}";
    }

    private void ShowHistory()
    {
        if (processGrid.CurrentRow?.Tag is not ProcessRow row)
        {
            pathBox.Text = "";
            detail.Text = "Select a process to see its CPU samples.";
            investigationBox.Text = "";
            investigationPid = 0;
            investigationIdentity = null;
            investigationPath = null;
            extraDetails = "";
            investigationVersion++;
            detailsCancellation?.Cancel();
            UpdateChart();
            return;
        }
        pathBox.Text = row.Path;
        detail.Text = $"{row.Name}   ·   PID {row.Pid}";
        UpdateChart();
        if (investigationPid != row.Pid || investigationIdentity != row.Identity || investigationPath != row.Path)
        {
            investigationPid = row.Pid;
            investigationIdentity = row.Identity;
            investigationPath = row.Path;
            extraDetails = "Loading file and connection details…";
            var version = ++investigationVersion;
            investigationBox.Text = BasicInvestigation(row);
            investigationTask = LoadInvestigationAsync(row, version);
        }
        investigationBox.Text = BasicInvestigation(row) + Environment.NewLine + extraDetails;
    }

    private string BasicInvestigation(ProcessRow row)
    {
        var started = row.Identity is { } identity
            ? new DateTime(identity.StartTimeUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "Unavailable";
        return $"Command line: {row.CommandLine ?? "Unavailable"}{Environment.NewLine}" +
            $"Process tree: {ProcessTree(row)}{Environment.NewLine}" +
            $"Started: {started}{Environment.NewLine}" +
            $"CPU average / peak (60s): {Metric(row.AverageCpuPercent)} / {Metric(row.PeakCpuPercent)}{Environment.NewLine}";
    }

    private string ProcessTree(ProcessRow row)
    {
        var chain = new List<string> { $"{row.Name} (PID {row.Pid})" };
        var parentPid = row.ParentPid;
        var visited = new HashSet<int> { row.Pid };
        for (var level = 0; level < 6 && parentPid is { } pid; level++)
        {
            if (!visited.Add(pid)) break;
            var parent = latestRows.FirstOrDefault(candidate => candidate.Pid == pid && candidate.Status != "Not observed");
            if (parent is null) break;
            if (row.Identity is { } childId && parent.Identity is { } parentId && parentId.StartTimeUtcTicks > childId.StartTimeUtcTicks) break;
            chain.Add($"{parent.Name} (PID {parent.Pid})");
            parentPid = parent.ParentPid;
        }
        chain.Reverse();
        return string.Join(" → ", chain);
    }

    private async Task LoadInvestigationAsync(ProcessRow row, int version)
    {
        detailsCancellation?.Cancel();
        detailsCancellation?.Dispose();
        detailsCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var cancellation = detailsCancellation.Token;
        detailsLoading = true;
        refreshDetails.Enabled = false;
        async Task<string> FileDetails()
        {
            try
            {
                var analysis = await FileAnalysisService.AnalyzeAsync(row.Path, cancellation);
                return analysis is null ? "File analysis unavailable."
                    : $"Publisher: {analysis.Publisher}\r\nSignature: {analysis.Signature}\r\nSHA-256: {analysis.Sha256}\r\n{analysis.Warning}";
            }
            catch (Exception ex) { return $"File analysis unavailable ({ex.GetType().Name})."; }
        }
        Task<string> NetworkDetails() => Task.Run(() =>
        {
            try
            {
                if (row.Identity is not { } identity) return "Connections: process identity unavailable.";
                using var process = System.Diagnostics.Process.GetProcessById(row.Pid);
                if (process.StartTime.ToUniversalTime().Ticks != identity.StartTimeUtcTicks)
                    return "Connections: the selected process has exited.";
                var result = NetworkConnectionService.ReadDetailed(row.Pid);
                process.Refresh();
                if (process.HasExited) return "Connections: the selected process has exited.";
                var lines = result.Connections.Select(connection =>
                    $"{connection.Protocol}  {connection.LocalEndpoint} → {connection.RemoteEndpoint}  {connection.State}");
                return (result.Connections.Count == 0 ? "No endpoints observed." : string.Join(Environment.NewLine, lines))
                    + (result.Warnings.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, result.Warnings) : "");
            }
            catch (Exception ex) { return $"Connections unavailable ({ex.GetType().Name})."; }
        });
        try
        {
            var file = FileDetails();
            var network = NetworkDetails();
            await Task.WhenAll(file, network);
            if (IsDisposed || Disposing || version != investigationVersion) return;
            extraDetails = $"Details captured {DateTime.Now:HH:mm:ss}\r\n{await file}\r\n\r\nCONNECTIONS\r\n{await network}";
            if (processGrid.CurrentRow?.Tag is ProcessRow current)
                investigationBox.Text = BasicInvestigation(current) + Environment.NewLine + extraDetails;
        }
        finally
        {
            if (!IsDisposed && !Disposing && version == investigationVersion)
            {
                detailsLoading = false;
                refreshDetails.Enabled = true;
            }
        }
    }

    private void UpdateChart() => chart.Update(processGrid.CurrentRow?.Tag as ProcessRow,
        metricBox.SelectedItem?.ToString() ?? "CPU", displayedAt > 0 ? displayedAt : lastSampleTime);

    private static string Metric(double? value) => value is { } number ? $"{number:N1}%" : "Unavailable";

    private bool MatchesFilter(ProcessRow row)
    {
        var query = searchBox.Text.Trim();
        if (query.Length > 0 && !new[] { row.Name, row.Pid.ToString(), row.Path, row.CommandLine ?? "" }
            .Any(text => text.Contains(query, StringComparison.OrdinalIgnoreCase))) return false;
        return filterBox.SelectedIndex switch
        {
            1 => row.CpuPercent >= 10,
            2 => row.GpuPercent >= 1,
            3 => row.Identity is { } identity && row.Status != "Not observed" &&
                identity.StartTimeUtcTicks >= (displayedUtc == default ? DateTime.UtcNow : displayedUtc).AddMinutes(-5).Ticks,
            4 => row.Status == "Limited access",
            _ => true
        };
    }

    private void TogglePause()
    {
        paused = !paused;
        pauseButton.Text = paused ? "Resume view" : "Pause view";
        if (!paused && liveRows.Count > 0)
        {
            displayedAt = lastSampleTime;
            displayedUtc = DateTime.UtcNow;
            Render(liveRows);
        }
        else Render(latestRows);
    }

    private void TakeSnapshot()
    {
        if (latestRows.Count == 0) return;
        baseline = new ActivitySnapshot(displayedUtc == default ? DateTime.UtcNow : displayedUtc, latestRows.ToArray());
        snapshotStatus.Text = $"Snapshot {baseline.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
        compareButton.Enabled = true;
    }

    private void CompareSnapshot()
    {
        if (baseline is null) return;
        var current = new ActivitySnapshot(displayedUtc == default ? DateTime.UtcNow : displayedUtc, latestRows.ToArray());
        var changes = SnapshotComparison.Compare(baseline, current);
        using var dialog = new Form
        {
            Text = "PCInspector · Snapshot comparison", Size = new Size(1000, 640), MinimumSize = new Size(800, 480),
            StartPosition = FormStartPosition.CenterParent, BackColor = FluentTheme.Canvas, Font = FluentTheme.BodyFont
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "What changed", Font = FluentTheme.HeadingFont, AutoSize = true }, 0, 0);
        var skipped = baseline.Processes.Concat(current.Processes).Count(row => row.Identity is null);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill,
            Text = $"{baseline.CapturedAtUtc.ToLocalTime():HH:mm:ss} → {current.CapturedAtUtc.ToLocalTime():HH:mm:ss}  ·  {changes.Count} known identities\r\n" +
                $"Deltas are current minus snapshot. CPU is in percentage points. {skipped} readings without identity excluded.",
            ForeColor = FluentTheme.Muted, Font = FluentTheme.CaptionFont }, 0, 1);
        var grid = CreateGrid();
        FluentTheme.StyleGrid(grid);
        AddColumn(grid, "Change", "Observation", 175);
        AddColumn(grid, "Name", "Process", 180);
        AddColumn(grid, "Pid", "PID", 70, typeof(int));
        AddColumn(grid, "Cpu", "Δ CPU pp", 100, typeof(double));
        AddColumn(grid, "Ram", "Δ RAM MiB", 120, typeof(double));
        AddColumn(grid, "Read", "Δ Read MiB/s", 135, typeof(double));
        AddColumn(grid, "Write", "Δ Write MiB/s", 135, typeof(double));
        foreach (DataGridViewColumn column in grid.Columns)
            if (column.ValueType == typeof(double)) column.DefaultCellStyle.Format = "+0.00;-0.00;0.00";
        foreach (var change in changes)
            grid.Rows.Add(change.Change, change.Name, change.Pid, change.CpuDelta!, change.MemoryDeltaMiB!,
                change.ReadDeltaMiBPerSecond!, change.WriteDeltaMiBPerSecond!);
        var card = new FluentCard { Dock = DockStyle.Fill };
        card.Controls.Add(grid);
        layout.Controls.Add(card, 0, 2);
        dialog.Controls.Add(layout);
        dialog.ShowDialog(this);
    }

    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Clipboard is busy. Try again."; }
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
            var activity = new ActivitySnapshot(displayedUtc == default ? DateTime.UtcNow : displayedUtc, rows);
            var startup = ReportStartupProvider?.Invoke() ?? (Array.Empty<PersistenceItem>(), new[] { "Startup has not been scanned." });
            var report = new DiagnosticReport(DateTime.UtcNow, activity, ReportSystemSnapshot, startup.Item1.ToArray(),
                startup.Item2.ToArray(), investigationBox.Text, baseline,
                baseline is null ? null : SnapshotComparison.Compare(baseline, new ActivitySnapshot(activity.CapturedAtUtc, latestRows)));
            exportButton.Enabled = false;
            await Task.Run(() => ExportReportService.Write(dialog.FileName, report));
            if (!IsDisposed && !Disposing) status.Text = $"Report exported: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!IsDisposed && !Disposing)
                MessageBox.Show(this, $"Could not export the report.\n{ex.Message}",
                    "PCInspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { if (!IsDisposed && !Disposing) exportButton.Enabled = true; }
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
        e.Value = column is "Average" or "Peak" ? "No samples" : column is "Gpu" or "Read" or "Write" ? "No data" : row.Status switch
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
