using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using PCInspector;
using PCInspector.Models;
using PCInspector.Services;

internal static class ActivityChecks
{
    private static ProcessRow Row(int pid, long started, double? cpu = 5, double? memory = 100) =>
        new(new ProcessIdentity(pid, started), pid, "Example", cpu, cpu, cpu, memory, "Unavailable", "Running", []);

    public static void Run(Action<bool, string> check)
    {
        var before = new ActivitySnapshot(DateTime.UtcNow, [Row(1, 100), Row(2, 100), Row(3, 100), Row(5, 100) with { Identity = null }]);
        var after = new ActivitySnapshot(DateTime.UtcNow, [Row(1, 100, 15, 80), Row(2, 200), Row(3, 100) with { Status = "Not observed" }, Row(6, 100, null)]);
        var changes = SnapshotComparison.Compare(before, after);
        check(changes.Single(c => c.Pid == 1) is { CpuDelta: 10, MemoryDeltaMiB: -20 },
            "Snapshot deltas compare measurements of the same process identity");
        check(changes.Count(c => c.Pid == 2) == 2 && changes.Where(c => c.Pid == 2).All(c => c.CpuDelta is null),
            "A reused PID appears as two observations instead of an invented delta");
        check(changes.Single(c => c.Pid == 3).Change == "No longer observed" && changes.All(c => c.Pid != 5),
            "Snapshot comparison excludes unverified identities and retained absent rows");

        var folder = Path.Combine(Path.GetTempPath(), "pcinspector-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var hostile = Row(1, 100) with { Name = "<script>alert(1)</script>", CommandLine = "<&\"", ReadMiBPerSecond = 2,
                ResourceHistory = [new ResourceSample(1, 5, null, 100, 2, 0)] };
            var report = new DiagnosticReport(DateTime.UtcNow, new ActivitySnapshot(DateTime.UtcNow, [hostile]),
                new SystemSnapshot { Cpu = "Demo CPU" }, [new PersistenceItem("Run", "demo", "demo.exe", "Enabled")], [],
                "Signature: unavailable", before, changes);
            var json = Path.Combine(folder, "report.json");
            var html = Path.Combine(folder, "report.html");
            ExportReportService.Write(json, report);
            ExportReportService.Write(html, report);
            using var document = JsonDocument.Parse(File.ReadAllText(json));
            check(document.RootElement.GetProperty("Activity").GetProperty("Processes")[0].GetProperty("ResourceHistory").GetArrayLength() == 1 &&
                document.RootElement.GetProperty("Startup").GetArrayLength() == 1 && document.RootElement.GetProperty("Comparison").GetArrayLength() > 0,
                "Export retains resource history, startup observations and snapshot comparison");
            var markup = File.ReadAllText(html);
            check(!markup.Contains("<script>") && markup.Contains("&lt;script&gt;") && markup.Contains("Complete captured data"),
                "HTML report escapes process-controlled text and includes all captured data");
        }
        finally { Directory.Delete(folder, true); }
    }

    public static IReadOnlyList<string> RunUi()
    {
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                using var view = new ProcessesView();
                T Field<T>(string name) => (T)typeof(ProcessesView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
                void Set(string name, object value) => typeof(ProcessesView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);
                void Call(string name, params object[] args) => typeof(ProcessesView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, args);
                var first = new[] { Row(101, 100) with { Name = "alpha" }, Row(102, 100) with { Name = "beta", CommandLine = "--special" } };
                Call("Render", (object)first);
                Field<TextBox>("searchBox").Text = "special";
                var grid = Field<DataGridView>("processGrid");
                if (grid.Rows.Count != 1 || ((ProcessRow)grid.Rows[0].Tag!).Pid != 102) failures.Add("Command-line search did not select beta");
                Field<TextBox>("searchBox").Text = "";
                Call("TogglePause");
                Set("liveRows", new[] { Row(103, 100) with { Name = "gamma" } });
                Field<TextBox>("searchBox").Text = "gamma";
                if (grid.Rows.Count != 0) failures.Add("Pause must filter the frozen capture, not live readings");
                Call("TogglePause");
                if (grid.Rows.Count != 1 || ((ProcessRow)grid.Rows[0].Tag!).Pid != 103) failures.Add("Resume must apply the most recent live capture");
                Call("TakeSnapshot");
                if (Field<ActivitySnapshot>("baseline").Processes.Count != 1) failures.Add("Snapshot did not capture the displayed observation");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failures;
    }
}
