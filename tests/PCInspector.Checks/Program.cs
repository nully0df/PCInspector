using PCInspector;
using PCInspector.Services;
using PCInspector.Models;
using System.Diagnostics;

// Child modes exercise the real process boundary without operating Task Manager.
if (args is ["--helper-test", var mode])
{
    if (mode == "hang") Thread.Sleep(30000);
    return mode == "fail" ? 23 : 0;
}

var failures = new List<string>();
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {name}");
    if (!condition) failures.Add(name);
}

Check(DisplayFormat.Gibibytes(null) == "Unavailable", "Unknown capacity is distinct from zero");
Check(DisplayFormat.Gibibytes(0) == "0.0 GiB", "Zero free space is displayed correctly");
Check(DisplayFormat.Gibibytes(1073741824) == "1.0 GiB", "Binary capacity unit");
Check(DisplayFormat.Gibibytes(5d * 1024 * 1024 * 1024 * 1024) == "5120.0 GiB", "Multi-terabyte capacity");
Check(DisplayFormat.Uptime(null) == "Unavailable", "Missing boot time");
Check(DisplayFormat.Uptime(new TimeSpan(42, 3, 4, 5)) == "42d 03h 04m 05s", "Uptime longer than one day");

ProcessReading Reading(double time, double? cpu, long start = 100) =>
    new(123, "Example", start, time, cpu, 1024 * 1024, "example.exe");
var history = new ProcessHistory(8);
Check(history.Update([Reading(0, 0)], 0).Single().CpuPercent is null,
    "First CPU sample is unknown, not zero");
Check(history.Update([Reading(1, 2)], 1).Single().CpuPercent == 25,
    "Two CPU seconds in one second on eight processors is 25 percent");
var weighted = new ProcessHistory(8);
weighted.Update([Reading(0, 0)], 0);
weighted.Update([Reading(2, 8)], 2);
var weightedRow = weighted.Update([Reading(3, 16)], 3).Single();
Check(Math.Abs(weightedRow.AverageCpuPercent!.Value - 200d / 3) < 0.001,
    "Average is weighted by measured duration");
var clipped = weighted.Update([], 61).Single();
Check(clipped.AverageCpuPercent == 75 && clipped.PeakCpuPercent == 100 &&
    clipped.History[0].Start == 1 && clipped.MemoryMiB is null && clipped.CpuPercent is null,
    "Rolling window clips intervals and does not invent current metrics for absent processes");
Check(weighted.Update([], 63).Count == 0, "Absent process expires after sixty seconds");
var reused = history.Update([Reading(2, 0, start: 200)], 2);
Check(reused.Count == 2 && reused.Single(r => r.Identity!.Value.StartTimeUtcTicks == 200).CpuPercent is null,
    "Reused PID starts a separate history");
var gaps = new ProcessHistory(8);
gaps.Update([Reading(0, 0)], 0);
gaps.Update([Reading(1, null)], 1);
Check(gaps.Update([Reading(2, 8)], 2).Single().CpuPercent is null,
    "Inaccessible CPU reading creates a gap");
Check(gaps.Update([Reading(7, 16)], 7).Single().CpuPercent == 20,
    "A slow five-second refresh still computes CPU using the actual elapsed time");
Check(gaps.Update([Reading(8, 1)], 8).Single().CpuPercent is null,
    "Counter reset is not negative CPU usage");
gaps.BreakSampling();
Check(gaps.Update([Reading(9, 2)], 9).Single().CpuPercent is null,
    "Failed scan breaks the next CPU delta");
gaps.Update([], 10);
Check(gaps.Update([Reading(11, 3)], 11).Single().CpuPercent is null,
    "A missing process observation breaks the next CPU delta");
var limited = gaps.Update([Reading(12, 4) with { StartTimeUtcTicks = null }], 12)
    .Single(r => r.Identity is null);
Check(limited.Status == "Limited access" && limited.CpuPercent is null && limited.MemoryMiB == 1,
    "Unknown process identity preserves accessible RAM without fabricated history");

var slowRefresh = new ProcessHistory(8);
slowRefresh.Update([Reading(0, 0)], 0);
var slowRow = slowRefresh.Update([Reading(5, 10)], 5).Single();
Check(slowRow.CpuPercent == 25 && slowRow.History.Single().End - slowRow.History.Single().Start == 5,
    "Slow sample retains its real duration in history");
Check(slowRefresh.Update([Reading(10, 10)], 10).Single().CpuPercent == 0,
    "An idle process displays zero after a slow refresh, not a dash");
Check(slowRefresh.Update([Reading(71, 12)], 71).Single().CpuPercent is null,
    "A pause longer than the history window starts a new CPU baseline");
Check(slowRefresh.Update([Reading(76, 22)], 76).Single().CpuPercent == 25,
    "CPU recovers on the next sample after a long pause");

MetricsChecks.Run(Check);
InvestigationChecks.Run(Check);
ActivityChecks.Run(Check);

if (args.Contains("--investigation-live")) await InvestigationChecks.RunLiveAsync(Check);

if (args.Contains("--process-live"))
{
    MetricsChecks.RunLive(Check);
    using var sampler = new ProcessSampler();
    var liveHistory = new ProcessHistory(Environment.ProcessorCount);
    liveHistory.Update(sampler.Read(), ProcessSampler.NowSeconds);
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < 300) Thread.SpinWait(10000);
    var liveRows = liveHistory.Update(sampler.Read(), ProcessSampler.NowSeconds);
    var own = liveRows.Single(r => r.Pid == Environment.ProcessId);
    Check(own.CpuPercent > 0 && own.CpuPercent <= 100, "Real CPU work is attributed to this test process");
    Check(own.MemoryMiB > 0 && own.Path != "Unavailable", "Own process memory and executable path are readable");
}

if (args.Contains("--live"))
{
    var snapshot = new SystemInfoService().GetSnapshot();
    Check(!string.IsNullOrWhiteSpace(snapshot.ComputerName), "Computer name collected");
    Check(snapshot.WindowsVersion != "Unavailable", "Windows version collected");
    Check(snapshot.Cpu != "Unavailable", "CPU collected");
    Check(snapshot.GraphicsAdapters.Count > 0 && snapshot.GraphicsAdapters.All(adapter =>
        !string.IsNullOrWhiteSpace(adapter.Name) && !string.IsNullOrWhiteSpace(adapter.DriverVersion)),
        "Graphics adapters and driver fields collected");
    Check(snapshot.TotalMemoryBytes > 0, "Usable RAM collected");
    Check(snapshot.FreeMemoryBytes.HasValue && snapshot.FreeMemoryBytes <= snapshot.TotalMemoryBytes,
        "Available RAM within usable RAM");
    Check(snapshot.Uptime is { } uptime && uptime >= TimeSpan.Zero, "Boot time collected");
    Check(snapshot.Disks.Any(d => d.TotalBytes > 0), "At least one ready local drive");
    Check(snapshot.Disks.All(d => d.TotalBytes is null || (d.FreeBytes >= 0 && d.FreeBytes <= d.TotalBytes)),
        "Drive capacities are consistent");
    Check(snapshot.LocalAddresses.All(a => System.Net.IPAddress.TryParse(a[(a.LastIndexOf(": ") + 2)..], out _)),
        "Collected network addresses are valid (offline is allowed)");
    Check(snapshot.Warnings.Count == 0, "No collection warnings on this computer");
}

if (args.Contains("--ui"))
{
    var uiFailures = ProcessUiChecks.Run();
    foreach (var failure in uiFailures) Console.WriteLine(failure);
    Check(uiFailures.Count == 0, "UI: numeric sorting, missing-data labels and file context-menu targeting");
    var activityFailures = ActivityChecks.RunUi();
    foreach (var failure in activityFailures) Console.WriteLine(failure);
    Check(activityFailures.Count == 0, "UI: command-line search, frozen-view filtering, resume and snapshot capture");
}

if (args.Contains("--task-manager-checks"))
{
    try
    {
        using var own = Process.GetCurrentProcess();
        await TaskManagerService.OpenAndSelectAsync(own.Id, own.ProcessName, 1);
        Check(false, "Task Manager action rejects a stale displayed identity");
    }
    catch (InvalidOperationException ex)
    {
        Check(ex.Message.Contains("PID was reused"), "Task Manager action rejects stale paused-view identity before starting helper");
    }
    ProcessStartInfo Child(string mode)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!);
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(ProcessUiChecks).Assembly.Location);
        start.ArgumentList.Add("--helper-test");
        start.ArgumentList.Add(mode);
        return start;
    }
    await TaskManagerService.RunHelperAsync(Child("ok"), TimeSpan.FromSeconds(5));
    Check(true, "Task Manager helper: successful exit completes normally");
    try
    {
        await TaskManagerService.RunHelperAsync(Child("fail"), TimeSpan.FromSeconds(5));
        Check(false, "Task Manager helper: abnormal exit is reported");
    }
    catch (InvalidOperationException ex)
    {
        Check(ex.Message.Contains("23"), "Task Manager helper: abnormal exit is reported and parent survives");
    }
    var elapsed = Stopwatch.StartNew();
    try
    {
        await TaskManagerService.RunHelperAsync(Child("hang"), TimeSpan.FromMilliseconds(500));
        Check(false, "Task Manager helper: hung worker times out");
    }
    catch (TimeoutException)
    {
        Check(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Task Manager helper: hung worker is stopped without blocking parent");
    }
    // Calls the actual worker entry and its UI Automation dependency, but rejects
    // a stale identity before opening any external window.
    Check(TaskManagerService.RunWorker(["--task-manager", Environment.ProcessId.ToString(), "1", "checks"]) == 1,
        "Task Manager worker rejects a reused PID without touching Task Manager");
    var automation = typeof(System.Windows.Automation.AutomationElement).Assembly;
    Check(automation.GetName().Version!.Major >= 10,
        "UI Automation loads from the modern desktop runtime, not .NET Framework 4");
    var worker = new ProcessStartInfo(Path.ChangeExtension(typeof(TaskManagerService).Assembly.Location, ".exe"));
    foreach (var argument in new[] { "--task-manager", Environment.ProcessId.ToString(), "1", "checks" })
        worker.ArgumentList.Add(argument);
    try
    {
        await TaskManagerService.RunHelperAsync(worker, TimeSpan.FromSeconds(5));
        Check(false, "Actual PCInspector helper entry reports stale identity");
    }
    catch (InvalidOperationException ex)
    {
        Check(ex.Message.Contains("PID was reused"),
            "Actual PCInspector helper starts without MainForm and reports stale identity to parent");
    }
}

Console.WriteLine($"Finished: {failures.Count} failure(s).");
return failures.Count == 0 ? 0 : 1;
