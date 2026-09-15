using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PCInspector.Services;

internal static class TaskManagerService
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    public static async Task OpenAndSelectAsync(int pid, string name, long? expectedStartTimeUtcTicks = null)
    {
        using var target = Process.GetProcessById(pid);
        var started = target.StartTime.ToUniversalTime().Ticks;
        if (expectedStartTimeUtcTicks is { } expected && started != expected)
            throw new InvalidOperationException("The displayed process has exited and its PID was reused. Resume or refresh the list.");
        if (!string.Equals(target.ProcessName, name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected process has changed. Refresh the process list.");

        var assembly = typeof(TaskManagerService).Assembly.Location;
        var appHost = Path.ChangeExtension(assembly, ".exe");
        var start = new ProcessStartInfo { FileName = appHost };
        if (!File.Exists(appHost))
        {
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "dotnet.exe");
            start.ArgumentList.Add(assembly);
        }
        start.ArgumentList.Add("--task-manager");
        start.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(started.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(name);
        await RunHelperAsync(start, TimeSpan.FromSeconds(15));
    }

    internal static async Task RunHelperAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardError = true;
        using var helper = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the Task Manager helper.");
        using var deadline = new CancellationTokenSource(timeout);
        var errors = helper.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await helper.WaitForExitAsync(deadline.Token);
            var detail = (await errors).Trim();
            if (helper.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrEmpty(detail)
                    ? $"The Task Manager helper stopped (code {helper.ExitCode}). PCInspector is still running."
                    : detail);
        }
        catch (OperationCanceledException)
        {
            // Kill only our helper; Task Manager belongs to the user.
            try { helper.Kill(); }
            catch (InvalidOperationException) { }
            try { await errors; }
            catch (OperationCanceledException) { }
            throw new TimeoutException("Task Manager did not respond in time. Select the process manually by PID.");
        }
    }

    internal static int RunWorker(string[] args)
    {
        try
        {
            if (args.Length != 4 || !int.TryParse(args[1], out var pid) || pid <= 0 ||
                !long.TryParse(args[2], out var started))
                throw new ArgumentException("Invalid Task Manager helper arguments.");
            // UI Automation runs on an MTA thread outside the application's UI process.
            Task.Run(() => OpenAndSelect(pid, started, args[3])).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void VerifyTarget(int pid, long started)
    {
        using var target = Process.GetProcessById(pid);
        if (target.StartTime.ToUniversalTime().Ticks != started)
            throw new InvalidOperationException("The selected process exited and its PID was reused. Refresh the list.");
    }

    private static void OpenAndSelect(int pid, long started, string name)
    {
        VerifyTarget(pid, started);
        var handle = FindTaskManagerWindow();
        if (handle == IntPtr.Zero)
        {
            using var launched = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Taskmgr.exe"),
                UseShellExecute = true
            });
        }

        var watch = Stopwatch.StartNew();
        while (handle == IntPtr.Zero && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(100);
            // Task Manager can hand off to a different process during startup.
            handle = FindTaskManagerWindow();
        }
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Task Manager did not open a window.");

        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
        var window = AutomationElement.FromHandle(handle);
        foreach (var caption in new[] { "Details", "Подробности", "Сведения" })
        {
            var details = window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, caption));
            if (details is null) continue;
            if (details.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection))
                ((SelectionItemPattern)selection).Select();
            else if (details.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
                ((InvokePattern)invoke).Invoke();
            break;
        }

        watch.Restart();
        do
        {
            VerifyTarget(pid, started);
            if (TrySelectProcess(window, pid, name)) return;
            Thread.Sleep(200);
        } while (watch.Elapsed < TimeSpan.FromSeconds(3));
        throw new InvalidOperationException(
            $"Task Manager opened, but could not select {name} (PID {pid}). " +
            "Open Details and locate this PID manually. Selection may be unavailable because of access restrictions or the Task Manager version.");
    }

    private static IntPtr FindTaskManagerWindow()
    {
        var processes = Process.GetProcessesByName("Taskmgr");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
                catch (InvalidOperationException) { }
            }
            return IntPtr.Zero;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static bool TrySelectProcess(AutomationElement root, int pid, string name)
    {
        var rows = root.FindAll(TreeScope.Descendants, new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)));
        foreach (AutomationElement row in rows)
        {
            try
            {
                // Require exact PID and executable name in the SAME row. Never invoke
                // an ancestor control: Invoke can activate buttons rather than select a row.
                var pidCell = row.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, pid.ToString(CultureInfo.InvariantCulture)));
                if (pidCell is null) continue;
                var nameCondition = new OrCondition(
                    new PropertyCondition(AutomationElement.NameProperty, name, PropertyConditionFlags.IgnoreCase),
                    new PropertyCondition(AutomationElement.NameProperty, name + ".exe", PropertyConditionFlags.IgnoreCase));
                if (row.FindFirst(TreeScope.Element | TreeScope.Descendants, nameCondition) is null) continue;
                if (!row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection)) continue;
                ((SelectionItemPattern)selection).Select();
                if (row.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll))
                    ((ScrollItemPattern)scroll).ScrollIntoView();
                return ((SelectionItemPattern)selection).Current.IsSelected;
            }
            catch (ElementNotAvailableException) { }
        }
        return false;
    }
}
