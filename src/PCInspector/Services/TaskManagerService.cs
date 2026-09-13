using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PCInspector.Services;

internal static class TaskManagerService
{
    private const int ShowNormal = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    public static Task OpenAndSelectAsync(int pid, string name) =>
        Task.Run(() => OpenAndSelect(pid, name));

    private static void OpenAndSelect(int pid, string name)
    {
        using var taskManager = FindOrStartTaskManager();
        var window = WaitForWindow(taskManager, TimeSpan.FromSeconds(5));
        if (window is null)
            throw new InvalidOperationException("Task Manager window was not ready.");

        if (taskManager.MainWindowHandle != IntPtr.Zero)
        {
            ShowWindow(taskManager.MainWindowHandle, ShowNormal);
            SetForegroundWindow(taskManager.MainWindowHandle);
        }

        // Task Manager remembers its last page. Select Details when the localized
        // UI exposes it, then look for the exact PID in the accessibility tree.
        var details = FindByName(window, "Details", "Подробности", "Сведения");
        if (details is not null)
        {
            Activate(details);
            Thread.Sleep(250);
        }

        if (TrySelectProcess(window, pid, name)) return;
        throw new InvalidOperationException(
            $"Task Manager opened, but process {name} (PID {pid}) was not exposed for selection.");
    }

    private static Process FindOrStartTaskManager()
    {
        var existing = Process.GetProcessesByName("Taskmgr")
            .FirstOrDefault(process =>
            {
                try { return process.MainWindowHandle != IntPtr.Zero; }
                catch { return false; }
            });
        if (existing is not null) return existing;

        var started = Process.Start(new ProcessStartInfo
        {
            FileName = "taskmgr.exe",
            UseShellExecute = true
        });
        return started ?? throw new InvalidOperationException("Could not start Task Manager.");
    }

    private static AutomationElement? WaitForWindow(Process taskManager, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            try
            {
                taskManager.Refresh();
                if (taskManager.MainWindowHandle != IntPtr.Zero)
                    return AutomationElement.FromHandle(taskManager.MainWindowHandle);

                var condition = new PropertyCondition(
                    AutomationElement.ProcessIdProperty, taskManager.Id);
                var window = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
                if (window is not null) return window;
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            Thread.Sleep(100);
        }
        return null;
    }

    private static AutomationElement? FindByName(AutomationElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var element = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, name,
                    PropertyConditionFlags.IgnoreCase));
            if (element is not null) return element;
        }
        return null;
    }

    private static bool TrySelectProcess(AutomationElement root, int pid, string name)
    {
        var pidText = pid.ToString();
        var exactPid = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, pidText));
        if (exactPid is not null && SelectNearestItem(exactPid)) return true;

        var rows = root.FindAll(TreeScope.Descendants,
            new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)));
        foreach (AutomationElement row in rows)
        {
            string rowName;
            try { rowName = row.Current.Name; }
            catch (ElementNotAvailableException) { continue; }
            if (!rowName.Contains(name, StringComparison.OrdinalIgnoreCase) &&
                !rowName.Contains(pidText, StringComparison.Ordinal))
                continue;
            if (SelectNearestItem(row)) return true;
        }
        return false;
    }

    private static bool SelectNearestItem(AutomationElement element)
    {
        var current = element;
        for (var level = 0; level < 6 && current is not null; level++)
        {
            if (Activate(current)) return true;
            try { current = TreeWalker.RawViewWalker.GetParent(current); }
            catch (ElementNotAvailableException) { return false; }
        }
        return false;
    }

    private static bool Activate(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection))
            {
                ((SelectionItemPattern)selection).Select();
                return true;
            }
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            {
                ((InvokePattern)invoke).Invoke();
                return true;
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        return false;
    }
}
