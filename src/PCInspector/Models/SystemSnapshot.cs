namespace PCInspector.Models;

// A snapshot contains data only; it does not know anything about the window.
public sealed class SystemSnapshot
{
    public string ComputerName { get; init; } = Environment.MachineName;
    public string WindowsVersion { get; set; } = "Unavailable";
    public string Cpu { get; set; } = "Unavailable";
    public List<GraphicsAdapterInfo> GraphicsAdapters { get; } = [];
    public ulong? TotalMemoryBytes { get; set; }
    public ulong? FreeMemoryBytes { get; set; }
    public TimeSpan? Uptime { get; set; }
    public List<DiskInfo> Disks { get; } = [];
    public List<string> LocalAddresses { get; } = [];
    public List<string> Warnings { get; } = [];
}

public sealed record DiskInfo(string Name, string Type, long? TotalBytes,
    long? FreeBytes, string Status);

public sealed record GraphicsAdapterInfo(string Name, string DriverVersion);
