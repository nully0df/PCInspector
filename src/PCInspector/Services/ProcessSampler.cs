using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using PCInspector.Models;

namespace PCInspector.Services;

public sealed class ProcessSampler : IDisposable
{
    private readonly Dictionary<ProcessIdentity, string> paths = [];
    private readonly GpuUsageService gpuUsage = new();
    private Dictionary<int, ProcessMetadata> metadata = [];
    private double metadataReadAt;

    public static double NowSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public List<ProcessReading> Read()
    {
        var readings = new List<ProcessReading>();
        RefreshMetadata();
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
                    if (start is { } ticks)
                    {
                        var identity = new ProcessIdentity(process.Id, ticks);
                        seen.Add(identity);
                        if (!paths.TryGetValue(identity, out path))
                        {
                            path = TryRead(() => process.MainModule?.FileName);
                            if (path is not null) paths[identity] = path;
                        }
                    }
                    metadata.TryGetValue(process.Id, out var processMetadata);
                    var parentName = processMetadata?.ParentPid is { } parentPid && metadata.TryGetValue(parentPid, out var parent)
                        ? parent.Name : null;
                    readings.Add(new ProcessReading(process.Id, name, start, timestamp, cpu, ram, path,
                        processMetadata?.CommandLine, processMetadata?.ParentPid,
                        parentName,
                        gpuByPid.TryGetValue(process.Id, out var gpu) ? gpu : null));
                }
                catch (Exception ex) when (IsAccessError(ex))
                {
                    // The process may have ended between enumeration and reading its name.
                }
            }
        }
        foreach (var identity in paths.Keys.Where(key => !seen.Contains(key)).ToArray())
            paths.Remove(identity);
        return readings;
    }

    private void RefreshMetadata()
    {
        var now = NowSeconds;
        if (now - metadataReadAt < 5) return;
        metadataReadAt = now;
        try { metadata = ProcessMetadataService.Read(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException or TimeoutException)
        {
            metadata = [];
        }
    }

    public void Dispose() => gpuUsage.Dispose();

    private static T? TryRead<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception ex) when (IsAccessError(ex)) { return default; }
    }

    private static bool IsAccessError(Exception ex) => ex is Win32Exception
        or InvalidOperationException or NotSupportedException or UnauthorizedAccessException;
}
