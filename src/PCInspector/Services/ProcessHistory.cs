using PCInspector.Models;

namespace PCInspector.Services;

// Pure calculations: no Windows calls and no controls. Tests supply their own readings.
public sealed class ProcessHistory(int logicalProcessorCount)
{
    private sealed class Entry(ProcessReading reading)
    {
        public ProcessReading Last = reading;
        public readonly List<CpuInterval> Intervals = [];
    }

    private readonly Dictionary<ProcessIdentity, Entry> entries = [];
    private readonly int processorCount = logicalProcessorCount > 0
        ? logicalProcessorCount : throw new ArgumentOutOfRangeException(nameof(logicalProcessorCount));
    public const double WindowSeconds = 60;

    public IReadOnlyList<ProcessRow> Update(IReadOnlyList<ProcessReading> readings, double now)
    {
        var seen = new HashSet<ProcessIdentity>();
        var rows = new List<ProcessRow>();
        foreach (var reading in readings)
        {
            if (reading.Identity is not { } identity)
            {
                rows.Add(new ProcessRow(null, reading.Pid, reading.Name, null, null, null,
                    reading.WorkingSetBytes / (1024d * 1024), reading.Path ?? "Unavailable",
                    "Limited access", []));
                continue;
            }

            seen.Add(identity);
            double? current = null;
            if (!entries.TryGetValue(identity, out var entry))
                entries[identity] = entry = new Entry(reading);
            else
            {
                var elapsed = reading.TimestampSeconds - entry.Last.TimestampSeconds;
                if (elapsed > 0 && elapsed <= 3 && reading.CpuSeconds is { } cpu &&
                    entry.Last.CpuSeconds is { } previous && cpu >= previous)
                {
                    // CPU seconds accumulate across cores. Normalize to the whole computer.
                    current = Math.Clamp((cpu - previous) / elapsed / processorCount * 100, 0, 100);
                    entry.Intervals.Add(new CpuInterval(entry.Last.TimestampSeconds,
                        reading.TimestampSeconds, current.Value));
                }
                entry.Last = reading;
            }
            Prune(entry, now);
            rows.Add(ToRow(entry, current, reading.CpuSeconds is null ? "Limited access"
                : current is null ? "Measuring..." : "Running", now));
        }

        foreach (var (identity, entry) in entries.ToArray())
        {
            if (seen.Contains(identity)) continue;
            if (now - entry.Last.TimestampSeconds >= WindowSeconds)
            {
                entries.Remove(identity);
                continue;
            }
            Prune(entry, now);
            entry.Last = entry.Last with { CpuSeconds = null };
            rows.Add(ToRow(entry, null, "Not observed", now));
        }
        return rows;
    }

    // A failed whole scan must not turn the next CPU delta into a fabricated continuous sample.
    public void BreakSampling()
    {
        foreach (var entry in entries.Values)
            entry.Last = entry.Last with { CpuSeconds = null };
    }

    private static void Prune(Entry entry, double now) =>
        entry.Intervals.RemoveAll(interval => interval.End <= now - WindowSeconds);

    private static ProcessRow ToRow(Entry entry, double? current, string status, double now)
    {
        var history = entry.Intervals.Select(interval => interval with
        {
            Start = Math.Max(interval.Start, now - WindowSeconds)
        }).ToArray();
        var seconds = history.Sum(interval => interval.End - interval.Start);
        double? average = seconds > 0
            ? history.Sum(interval => interval.Percent * (interval.End - interval.Start)) / seconds : null;
        double? peak = history.Length > 0 ? history.Max(interval => interval.Percent) : null;
        return new ProcessRow(entry.Last.Identity, entry.Last.Pid, entry.Last.Name, current, average, peak,
            status == "Not observed" ? null : entry.Last.WorkingSetBytes / (1024d * 1024),
            entry.Last.Path ?? "Unavailable", status, history);
    }
}
