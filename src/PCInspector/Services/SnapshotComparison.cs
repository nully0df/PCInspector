using PCInspector.Models;

namespace PCInspector.Services;

public static class SnapshotComparison
{
    // Unknown identities cannot be matched safely: an old PID may belong to a new process.
    public static IReadOnlyList<ProcessChange> Compare(ActivitySnapshot before, ActivitySnapshot after)
    {
        var oldRows = Current(before);
        var newRows = Current(after);
        var changes = new List<ProcessChange>();
        foreach (var (identity, row) in newRows)
        {
            if (oldRows.TryGetValue(identity, out var old))
                changes.Add(new("Present in both", row.Name, row.Pid, row.CpuPercent - old.CpuPercent,
                    row.MemoryMiB - old.MemoryMiB, row.ReadMiBPerSecond - old.ReadMiBPerSecond,
                    row.WriteMiBPerSecond - old.WriteMiBPerSecond));
            else changes.Add(new("Newly observed", row.Name, row.Pid, null, null, null, null));
        }
        foreach (var (identity, row) in oldRows)
            if (!newRows.ContainsKey(identity))
                changes.Add(new("No longer observed", row.Name, row.Pid, null, null, null, null));
        return changes.OrderBy(change => change.Change == "Present in both" ? 1 : 0)
            .ThenByDescending(change => Math.Abs(change.CpuDelta ?? 0))
            .ThenBy(change => change.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<ProcessIdentity, ProcessRow> Current(ActivitySnapshot snapshot) =>
        snapshot.Processes.Where(row => row.Identity.HasValue && row.Status != "Not observed")
            .GroupBy(row => row.Identity!.Value).ToDictionary(group => group.Key, group => group.Last());
}
