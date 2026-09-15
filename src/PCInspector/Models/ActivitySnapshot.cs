namespace PCInspector.Models;

public sealed record ActivitySnapshot(DateTime CapturedAtUtc, IReadOnlyList<ProcessRow> Processes);

public sealed record ProcessChange(string Change, string Name, int Pid, double? CpuDelta,
    double? MemoryDeltaMiB, double? ReadDeltaMiBPerSecond, double? WriteDeltaMiBPerSecond);
