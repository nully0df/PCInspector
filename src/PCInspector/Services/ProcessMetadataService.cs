using System.Management;

namespace PCInspector.Services;

internal sealed record ProcessMetadata(string Name, string? CommandLine, int? ParentPid,
    long? StartTimeUtcTicks)
{
    // WMI's DMTF timestamp has microsecond precision; Process.StartTime has 100 ns ticks.
    public bool Matches(long? startTimeUtcTicks) => StartTimeUtcTicks is { } metadataStart &&
        startTimeUtcTicks is { } processStart && metadataStart / 10 == processStart / 10;
}

internal static class ProcessMetadataService
{
    public static Dictionary<int, ProcessMetadata> Read()
    {
        var result = new Dictionary<int, ProcessMetadata>();
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope("root\\CIMV2"),
            new ObjectQuery("SELECT ProcessId, Name, CommandLine, ParentProcessId, CreationDate FROM Win32_Process"),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) });
        using var objects = searcher.Get();
        foreach (ManagementObject item in objects)
        {
            using (item)
            {
                if (item["ProcessId"] is null) continue;
                var pid = Convert.ToInt32(item["ProcessId"]);
                var name = item["Name"] as string ?? $"PID {pid}";
                var commandLine = item["CommandLine"] as string;
                int? parentPid = item["ParentProcessId"] is null ? null : Convert.ToInt32(item["ParentProcessId"]);
                result[pid] = new ProcessMetadata(name,
                    string.IsNullOrWhiteSpace(commandLine) ? null : commandLine.Trim(), parentPid,
                    ReadStartTime(item["CreationDate"] as string));
            }
        }
        return result;
    }

    private static long? ReadStartTime(string? dmtf)
    {
        if (string.IsNullOrWhiteSpace(dmtf)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(dmtf).ToUniversalTime().Ticks; }
        catch (ArgumentException) { return null; }
    }
}
