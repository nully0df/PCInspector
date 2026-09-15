using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using PCInspector.Models;

namespace PCInspector.Services;

public sealed class ProcessSampler : IDisposable
{
    private readonly Dictionary<ProcessIdentity, string> paths = [];
    private readonly object lifetimeGate = new();
    private readonly GpuUsageService gpuUsage = new();
    private HashSet<ProcessIdentity> previousIdentities = [];
    private Dictionary<int, ProcessMetadata> metadata = [];
    private Task<Dictionary<int, ProcessMetadata>>? metadataRefresh;
    private double metadataReadAt;
    private bool disposed;

    public static double NowSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public List<ProcessReading> Read()
    {
        var readings = new List<ProcessReading>();
        lock (lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RefreshMetadata();
        }
        var gpuCollectedAt = DateTime.UtcNow.Ticks;
        var gpuByPid = gpuUsage.Read();
        var seen = new HashSet<ProcessIdentity>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == 0) continue; // Idle is unused CPU, not a workload.
                try
                {
                    var name = process.ProcessName;
                    var start = TryRead<long?>(() => process.StartTime.ToUniversalTime().Ticks);
                    var cpu = TryRead<double?>(() => process.TotalProcessorTime.TotalSeconds);
                    var timestamp = NowSeconds;
                    var ram = TryRead<long?>(() => process.WorkingSet64);
                    string? path = null;
                    ProcessIoCounters? io = null;
                    double? gpu = null;
                    if (start is { } ticks)
                    {
                        var identity = new ProcessIdentity(process.Id, ticks);
                        seen.Add(identity);
                        io = ProcessIoService.Read(identity);
                        // GPU counters identify only a PID. Require the same identity in both
                        // scans, so a newly reused PID cannot inherit its predecessor's load.
                        if (previousIdentities.Contains(identity) && ticks <= gpuCollectedAt &&
                            gpuByPid.TryGetValue(process.Id, out var usage)) gpu = usage;
                        if (!paths.TryGetValue(identity, out path))
                        {
                            path = TryRead(() => process.MainModule?.FileName);
                            if (path is not null) paths[identity] = path;
                        }
                    }
                    metadata.TryGetValue(process.Id, out var processMetadata);
                    if (processMetadata?.Matches(start) != true) processMetadata = null;
                    readings.Add(new ProcessReading(process.Id, name, start, timestamp, cpu, ram, path,
                        processMetadata?.CommandLine, processMetadata?.ParentPid,
                        null, gpu, io?.ReadBytes, io?.WriteBytes));
                }
                catch (Exception ex) when (IsAccessError(ex))
                {
                    // The process may have ended between enumeration and reading its name.
                }
            }
        }
        foreach (var identity in paths.Keys.Where(key => !seen.Contains(key)).ToArray())
            paths.Remove(identity);
        previousIdentities = seen;
        ResolveParents(readings);
        return readings;
    }

    internal static void ResolveParents(List<ProcessReading> readings)
    {
        var byPid = readings.ToDictionary(reading => reading.Pid);
        for (var index = 0; index < readings.Count; index++)
        {
            var child = readings[index];
            if (child.ParentPid is not { } parentPid || !byPid.TryGetValue(parentPid, out var parent))
                continue; // Preserve the historical parent PID when the parent has already exited.
            // A process born after its child is a reused PID, never its actual parent.
            var validParent = parent.StartTimeUtcTicks is { } parentStart &&
                child.StartTimeUtcTicks is { } childStart && parentStart <= childStart && parentPid != child.Pid;
            readings[index] = child with
            {
                ParentPid = validParent ? parentPid : null,
                ParentName = validParent ? parent.Name : null
            };
        }
    }

    private void RefreshMetadata()
    {
        var now = NowSeconds;
        if (metadataRefresh is { IsCompleted: true })
        {
            metadata = metadataRefresh.GetAwaiter().GetResult();
            metadataRefresh = null;
        }
        if (metadataRefresh is not null || now - metadataReadAt < 5) return;
        metadataReadAt = now;
        // WMI may take seconds or become unavailable. It never blocks the CPU/I/O loop.
        metadataRefresh = Task.Run(ReadMetadata);
    }

    private static Dictionary<int, ProcessMetadata> ReadMetadata()
    {
        try { return ProcessMetadataService.Read(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException or TimeoutException)
        {
            return [];
        }
    }

    public void Dispose()
    {
        lock (lifetimeGate) disposed = true;
        gpuUsage.Dispose();
    }

    private static T? TryRead<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception ex) when (IsAccessError(ex)) { return default; }
    }

    private static bool IsAccessError(Exception ex) => ex is Win32Exception
        or InvalidOperationException or NotSupportedException or UnauthorizedAccessException;
}
