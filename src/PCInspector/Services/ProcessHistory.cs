using PCInspector.Models;

namespace PCInspector.Services;

// Pure calculations: no Windows calls and no controls. Tests supply their own readings.
public sealed class ProcessHistory(int logicalProcessorCount)
{
    private sealed class Entry(ProcessReading reading)
    {
        public ProcessReading Last = reading;
        public readonly List<CpuInterval> Intervals = [];
        public readonly List<ResourceSample> Samples = [];
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
                    "Limited access", [], reading.CommandLine, reading.ParentPid,
                    reading.ParentName, reading.GpuPercent));
                continue;
            }

            seen.Add(identity);
            double? current = null;
            double? readRate = null;
            double? writeRate = null;
            if (!entries.TryGetValue(identity, out var entry))
                entries[identity] = entry = new Entry(reading);
            else
            {
                var elapsed = reading.TimestampSeconds - entry.Last.TimestampSeconds;
                // Slow refreshes are valid measurements too. Only discard a gap longer
                // than the history window, whose activity we cannot place within that window.
                if (elapsed > 0 && elapsed <= WindowSeconds)
                {
                    if (reading.CpuSeconds is { } cpu && entry.Last.CpuSeconds is { } previous &&
                        double.IsFinite(cpu) && double.IsFinite(previous) && previous >= 0 && cpu >= previous)
                    {
                        // CPU seconds accumulate across cores. Normalize to the whole computer.
                        var percent = (cpu - previous) / elapsed / processorCount * 100;
                        if (double.IsFinite(percent))
                        {
                            current = Math.Clamp(percent, 0, 100);
                            entry.Intervals.Add(new CpuInterval(entry.Last.TimestampSeconds,
                                reading.TimestampSeconds, current.Value));
                        }
                    }
                    // I/O counters are cumulative bytes, independent of CPU accessibility.
                    readRate = Rate(reading.ReadTransferBytes, entry.Last.ReadTransferBytes, elapsed);
                    writeRate = Rate(reading.WriteTransferBytes, entry.Last.WriteTransferBytes, elapsed);
                }
                entry.Last = reading;
            }
            entry.Samples.Add(new ResourceSample(reading.TimestampSeconds, current,
                reading.GpuPercent, reading.WorkingSetBytes / (1024d * 1024), readRate, writeRate));
            Prune(entry, now);
            rows.Add(ToRow(entry, current, reading.CpuSeconds is null ? "Limited access"
                : current is null ? "Measuring..." : "Running", now, readRate, writeRate));
        }

        foreach (var (identity, entry) in entries.ToArray())
        {
            if (seen.Contains(identity)) continue;
            if (now - entry.Last.TimestampSeconds >= WindowSeconds)
            {
                entries.Remove(identity);
                continue;
            }
            AddGap(entry, now);
            Prune(entry, now);
            entry.Last = ClearCounters(entry.Last);
            rows.Add(ToRow(entry, null, "Not observed", now));
        }
        return rows;
    }

    // A failed whole scan must not turn the next CPU delta into a fabricated continuous sample.
    public void BreakSampling(double? timestampSeconds = null)
    {
        foreach (var entry in entries.Values)
        {
            // A null point also breaks the instantaneous RAM/GPU chart lines during a failed scan.
            var gapAt = timestampSeconds ?? entry.Last.TimestampSeconds;
            AddGap(entry, gapAt);
            Prune(entry, gapAt);
            entry.Last = ClearCounters(entry.Last);
        }
    }

    private static void AddGap(Entry entry, double timestamp)
    {
        if (entry.Samples.Count == 0 || entry.Samples[^1] is
            { CpuPercent: not null } or { GpuPercent: not null } or { MemoryMiB: not null }
                or { ReadMiBPerSecond: not null } or { WriteMiBPerSecond: not null })
            entry.Samples.Add(new ResourceSample(timestamp, null, null, null, null, null));
    }

    private static ProcessReading ClearCounters(ProcessReading reading) => reading with
    {
        CpuSeconds = null, GpuPercent = null, ReadTransferBytes = null, WriteTransferBytes = null
    };

    private static double? Rate(ulong? current, ulong? previous, double elapsed)
    {
        if (current is not { } bytes || previous is not { } before || bytes < before) return null;
        var rate = (bytes - before) / elapsed / (1024d * 1024);
        return double.IsFinite(rate) ? rate : null;
    }

    private static void Prune(Entry entry, double now)
    {
        entry.Intervals.RemoveAll(interval => interval.End <= now - WindowSeconds);
        entry.Samples.RemoveAll(sample => sample.TimestampSeconds < now - WindowSeconds);
    }

    private static ProcessRow ToRow(Entry entry, double? current, string status, double now,
        double? readRate = null, double? writeRate = null)
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
            entry.Last.Path ?? "Unavailable", status, history,
            entry.Last.CommandLine, entry.Last.ParentPid, entry.Last.ParentName,
            status == "Not observed" ? null : entry.Last.GpuPercent,
            readRate, writeRate, entry.Samples.ToArray());
    }
}
