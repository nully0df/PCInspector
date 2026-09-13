using System.ComponentModel;
using System.Windows.Forms;
using PCInspector;
using PCInspector.Models;

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
                using var view = new ProcessesView();
                var grid = view.Controls[0].Controls.OfType<DataGridView>()
                    .Single(control => control.AccessibleName == "Processes sorted by resource usage");
                foreach (var (pid, value) in new (int, double?)[] { (1, null), (2, 60), (3, 0), (4, 9.5), (5, null) })
                {
                    var index = grid.Rows.Add("Example", pid, value!, value!, value!, value!, "Limited access", "Unavailable");
                    grid.Rows[index].Tag = new ProcessRow(null, pid, "Example", value, value, value, value,
                        "Unavailable", "Limited access", []);
                }

                foreach (var name in new[] { "Cpu", "Average", "Peak", "Ram" })
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
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failures;
    }
}
