namespace PCInspector.Models;

// PID alone is not an identity: Windows can reuse it after a process exits.
public readonly record struct ProcessIdentity(int Pid, long StartTimeUtcTicks);

public sealed record ProcessReading(int Pid, string Name, long? StartTimeUtcTicks,
    double TimestampSeconds, double? CpuSeconds, long? WorkingSetBytes, string? Path)
{
    public ProcessIdentity? Identity => StartTimeUtcTicks is { } start
        ? new ProcessIdentity(Pid, start) : null;
}

public sealed record CpuInterval(double Start, double End, double Percent);

public sealed record ProcessRow(ProcessIdentity? Identity, int Pid, string Name,
    double? CpuPercent, double? AverageCpuPercent, double? PeakCpuPercent,
    double? MemoryMiB, string Path, string Status, IReadOnlyList<CpuInterval> History);
