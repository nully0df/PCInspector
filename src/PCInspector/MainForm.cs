using PCInspector.Services;

namespace PCInspector;

public partial class MainForm : Form
{
    private readonly SystemInfoService systemInfoService = new();

    public MainForm()
    {
        InitializeComponent();
    }

    private async void MainForm_Shown(object? sender, EventArgs e) => await RefreshInfoAsync();

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

            summaryGrid.Rows.Clear();
            summaryGrid.Rows.Add("Computer", snapshot.ComputerName);
            summaryGrid.Rows.Add("Windows", snapshot.WindowsVersion);
            summaryGrid.Rows.Add("CPU", snapshot.Cpu);
            summaryGrid.Rows.Add("RAM (usable by Windows)", DisplayFormat.Gibibytes(snapshot.TotalMemoryBytes));
            summaryGrid.Rows.Add("RAM (available)", DisplayFormat.Gibibytes(snapshot.FreeMemoryBytes));
            summaryGrid.Rows.Add("System uptime", DisplayFormat.Uptime(snapshot.Uptime));
            summaryGrid.Rows.Add("Local IPv4", snapshot.LocalAddresses.Count == 0
                ? "No active IPv4 addresses found"
                : string.Join(Environment.NewLine, snapshot.LocalAddresses));

            disksGrid.Rows.Clear();
            foreach (var disk in snapshot.Disks)
                disksGrid.Rows.Add(disk.Name, disk.Type, DisplayFormat.Gibibytes(disk.TotalBytes),
                    DisplayFormat.Gibibytes(disk.FreeBytes), disk.Status);

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
