using PCInspector.Services;
using PCInspector.Models;

namespace PCInspector;

public partial class MainForm : Form
{
    private readonly SystemInfoService systemInfoService = new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public SystemSnapshot? LatestSystemSnapshot { get; private set; }

    public MainForm()
    {
        InitializeComponent();
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        // Respect the current monitor after Windows has applied its display scale.
        if (StartPosition != FormStartPosition.Manual)
        {
            var area = Screen.FromControl(this).WorkingArea;
            if (Width > area.Width - 32 || Height > area.Height - 32)
            {
                Size = new Size(Math.Min(Width, area.Width - 32), Math.Min(Height, area.Height - 32));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            }
        }
        processesView.StartMonitoring();
        await RefreshInfoAsync();
    }

    private async void RefreshButton_Click(object? sender, EventArgs e) => await RefreshInfoAsync();

    private async Task RefreshInfoAsync()
    {
        refreshButton.Enabled = false;
        statusLabel.Text = "Reading system information...";
        try
        {
            // WMI can be slow. Read on a worker thread, then update controls on the UI thread.
            var snapshot = await Task.Run(systemInfoService.GetSnapshot);
            if (IsDisposed || Disposing)
                return;

            LatestSystemSnapshot = snapshot;
            processesView.ReportSystemSnapshot = snapshot;
            deviceName.Text = snapshot.ComputerName;
            deviceDescription.Text = snapshot.WindowsVersion;
            memoryValue.Text = DisplayFormat.Gibibytes(snapshot.TotalMemoryBytes);
            memoryCaption.Text = snapshot.FreeMemoryBytes.HasValue
                ? $"{DisplayFormat.Gibibytes(snapshot.FreeMemoryBytes)} available" : "Available memory not reported";
            memoryBar.Fraction = snapshot.TotalMemoryBytes is > 0 && snapshot.FreeMemoryBytes.HasValue
                ? 1 - (double)snapshot.FreeMemoryBytes.Value / snapshot.TotalMemoryBytes.Value : null;
            uptimeValue.Text = snapshot.Uptime is { } uptime ? $"{uptime.Days}d {uptime.Hours:00}h {uptime.Minutes:00}m" : "Unavailable";
            volumeValue.Text = snapshot.Disks.Count(disk => disk.TotalBytes.HasValue).ToString();
            volumeCaption.Text = "Volumes with readable capacity";
            graphicsName.Text = snapshot.GraphicsAdapters.Count == 0 ? "Not available"
                : string.Join(Environment.NewLine, snapshot.GraphicsAdapters.Select(adapter => adapter.Name));
            graphicsDriver.Text = snapshot.GraphicsAdapters.Count == 0
                ? "Windows did not report graphics information."
                : string.Join(Environment.NewLine, snapshot.GraphicsAdapters.Select(adapter => $"Driver {adapter.DriverVersion}"));

            summaryGrid.Rows.Clear();
            summaryGrid.Rows.Add("Windows", snapshot.WindowsVersion);
            summaryGrid.Rows.Add("Processor", snapshot.Cpu);
            summaryGrid.Rows.Add("Memory", DisplayFormat.Gibibytes(snapshot.TotalMemoryBytes));
            summaryGrid.Rows.Add("Local IPv4", snapshot.LocalAddresses.Count == 0
                ? "No active IPv4 addresses found"
                : string.Join(Environment.NewLine, snapshot.LocalAddresses));

            disksGrid.Rows.Clear();
            foreach (var disk in snapshot.Disks)
                disksGrid.Rows.Add(disk.Name, disk.Type, DisplayFormat.Gibibytes(disk.TotalBytes),
                    DisplayFormat.Gibibytes(disk.FreeBytes),
                    disk.TotalBytes is > 0 && disk.FreeBytes.HasValue
                        ? $"{100d * (1 - (double)disk.FreeBytes.Value / disk.TotalBytes.Value):N0}%" : "—", disk.Status);
            summaryGrid.ClearSelection();
            disksGrid.ClearSelection();

            warningsBox.Text = string.Join(Environment.NewLine, snapshot.Warnings);
            warningsBox.Visible = snapshot.Warnings.Count > 0;
            statusLabel.Text = $"Updated {DateTime.Now:HH:mm:ss}" +
                (snapshot.Warnings.Count > 0 ? " — some information is unavailable" : "");
        }
        catch (Exception ex)
        {
            // Last line of defense for an event handler: show a recoverable error in the window.
            if (!IsDisposed && !Disposing)
            {
                statusLabel.Text = "Unable to refresh. Try again.";
                warningsBox.Text = $"Unexpected error: {ex.GetType().Name}";
                warningsBox.Visible = true;
            }
        }
        finally
        {
            if (!IsDisposed && !Disposing)
                refreshButton.Enabled = true;
        }
    }
}
