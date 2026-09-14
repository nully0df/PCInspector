using System.ComponentModel;
using System.Windows.Forms;
using PCInspector;
using PCInspector.Models;
using PCInspector.Services;
using System.Reflection;

internal static class ProcessUiChecks
{
    public static IReadOnlyList<string> Run()
    {
        var failures = new List<string>();
        // WinForms controls must be created and inspected on the same STA thread.
        var thread = new Thread(() =>
        {
            try
            {
                string? openedPath = null;
                (int Pid, string Name)? taskManagerTarget = null;
                using var view = new ProcessesView(
                    path => { openedPath = path; return Task.CompletedTask; },
                    (pid, name) => { taskManagerTarget = (pid, name); return Task.CompletedTask; });
                var grid = Descendants(view).OfType<DataGridView>()
                    .Single(control => control.AccessibleName == "Processes sorted by resource usage");
                foreach (var (pid, value) in new (int, double?)[] { (1, null), (2, 60), (3, 0), (4, 9.5), (5, null) })
                {
                    var index = grid.Rows.Add("Example", pid, value!, value!, value!, value!, value!, "Limited access", "Unavailable");
                    grid.Rows[index].Tag = new ProcessRow(null, pid, "Example", value, value, value, value,
                        "Unavailable", "Limited access", []);
                }

                foreach (var name in new[] { "Cpu", "Gpu", "Average", "Peak", "Ram" })
                    foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
                    {
                        grid.Sort(grid.Columns[name]!, direction);
                        var actual = grid.Rows.Cast<DataGridViewRow>().Select(row => (double?)row.Cells[name].Value).ToArray();
                        double?[] expected = direction == ListSortDirection.Ascending
                            ? [0, 9.5, 60, null, null] : [60, 9.5, 0, null, null];
                        if (!actual.SequenceEqual(expected)) failures.Add($"{name} {direction}: unavailable values must follow numbers");
                    }

                var restricted = grid.Rows.Cast<DataGridViewRow>().First(row => row.Cells["Cpu"].Value is null);
                if (!Equals(restricted.Cells["Cpu"].FormattedValue, "No access"))
                    failures.Add("Unavailable CPU must explain the access limitation");
                if (!Equals(restricted.Cells["Average"].FormattedValue, "No samples"))
                    failures.Add("Empty history must be labelled No samples");

                var menu = grid.ContextMenuStrip!;
                var open = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Show file in folder");
                var openTaskManager = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Open in Task Manager");
                var opening = typeof(ContextMenuStrip).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var mouseDown = typeof(DataGridView).GetMethod("OnCellMouseDown", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [typeof(DataGridViewCellMouseEventArgs)], null)!;
                var tempFolder = Path.Combine(Path.GetTempPath(), "PCInspector-checks-" + Guid.NewGuid());
                Directory.CreateDirectory(tempFolder);
                var samplePath = Path.Combine(tempFolder, "sample, файл & spaces.exe");
                try
                {
                    File.WriteAllText(samplePath, "Test data, not an executable");
                    var targetRow = grid.Rows[0];
                    targetRow.Tag = ((ProcessRow)targetRow.Tag!) with { Path = samplePath };
                    var expectedTaskManagerTarget = (ProcessRow)targetRow.Tag;
                    grid.CurrentCell = grid.Rows[1].Cells[0];
                    mouseDown.Invoke(grid, [new DataGridViewCellMouseEventArgs(0, targetRow.Index, 5, 5,
                        new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0))]);
                    var args = new CancelEventArgs();
                    opening.Invoke(menu, [args]);
                    if (args.Cancel || !open.Enabled || !openTaskManager.Enabled || grid.CurrentRow != targetRow)
                        failures.Add("Right-click must select its row and enable the file action");
                    openTaskManager.PerformClick();
                    if (taskManagerTarget is not { } taskTarget ||
                        taskTarget.Pid != expectedTaskManagerTarget.Pid || taskTarget.Name != expectedTaskManagerTarget.Name)
                        failures.Add("Task Manager action must keep the clicked process PID");
                    // A refresh or selection change while the menu is open cannot redirect the action.
                    grid.CurrentCell = grid.Rows[1].Cells[0];
                    open.PerformClick();
                    if (openedPath != samplePath) failures.Add("File menu target changed while open");
                    opening.Invoke(menu, [new CancelEventArgs()]);
                    if (open.Enabled) failures.Add("Unavailable path must disable the menu item");
                    File.Delete(samplePath);
                    if (FileLocationService.CanLocate(samplePath)) failures.Add("Deleted file is still offered");
                    try
                    {
                        FileLocationService.ShowAsync(samplePath).GetAwaiter().GetResult();
                        failures.Add("A missing file must be rejected before invoking Explorer");
                    }
                    catch (FileNotFoundException) { }
                    mouseDown.Invoke(grid, [new DataGridViewCellMouseEventArgs(0, -1, 5, 5,
                        new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0))]);
                    var headerArgs = new CancelEventArgs();
                    opening.Invoke(menu, [headerArgs]);
                    if (!headerArgs.Cancel) failures.Add("Right-click on a header must not target the old selection");
                    // Also cover the general MouseDown path for empty grid space.
                    mouseDown.Invoke(grid, [new DataGridViewCellMouseEventArgs(0, 0, 5, 5,
                        new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0))]);
                    var generalMouseDown = typeof(DataGridView).GetMethod("OnMouseDown",
                        BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(MouseEventArgs)], null)!;
                    generalMouseDown.Invoke(grid, [new MouseEventArgs(MouseButtons.Right, 1, -10, -10, 0)]);
                    var emptyArgs = new CancelEventArgs();
                    opening.Invoke(menu, [emptyArgs]);
                    if (!emptyArgs.Cancel) failures.Add("Empty grid space must clear the previous Task Manager target");
                }
                finally
                {
                    if (File.Exists(samplePath)) File.Delete(samplePath);
                    Directory.Delete(tempFolder);
                }
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failures;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
