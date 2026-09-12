using PCInspector;
using PCInspector.Services;

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

if (args.Contains("--live"))
{
    var snapshot = new SystemInfoService().GetSnapshot();
    Check(!string.IsNullOrWhiteSpace(snapshot.ComputerName), "Computer name collected");
    Check(snapshot.WindowsVersion != "Unavailable", "Windows version collected");
    Check(snapshot.Cpu != "Unavailable", "CPU collected");
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

Console.WriteLine($"Finished: {failures.Count} failure(s).");
return failures.Count == 0 ? 0 : 1;
