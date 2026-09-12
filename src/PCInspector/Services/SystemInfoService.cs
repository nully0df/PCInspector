using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using PCInspector.Models;

namespace PCInspector.Services;

public sealed class SystemInfoService
{
    public SystemSnapshot GetSnapshot()
    {
        var snapshot = new SystemSnapshot();
        // Each section can fail independently without losing the other results.
        ReadSection(snapshot, "Windows / RAM / uptime", () => ReadOperatingSystem(snapshot));
        ReadSection(snapshot, "CPU", () => ReadCpu(snapshot));
        ReadSection(snapshot, "Disks", () => ReadDisks(snapshot));
        ReadSection(snapshot, "Network", () => ReadNetwork(snapshot));
        return snapshot;
    }

    private static void ReadOperatingSystem(SystemSnapshot snapshot)
    {
        using var searcher = CreateSearcher(
            "SELECT Caption, Version, BuildNumber, OSArchitecture, " +
            "TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime FROM Win32_OperatingSystem");
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                snapshot.WindowsVersion = $"{item["Caption"]} ({item["OSArchitecture"]}) " +
                    $"— version {item["Version"]}, build {item["BuildNumber"]}";
                // WMI reports these RAM values in KiB. Convert them to bytes.
                if (item["TotalVisibleMemorySize"] is not null)
                    snapshot.TotalMemoryBytes = Convert.ToUInt64(item["TotalVisibleMemorySize"]) * 1024;
                if (item["FreePhysicalMemory"] is not null)
                    snapshot.FreeMemoryBytes = Convert.ToUInt64(item["FreePhysicalMemory"]) * 1024;
                if (item["LastBootUpTime"] is string bootTime)
                {
                    var bootUtc = ManagementDateTimeConverter.ToDateTime(bootTime).ToUniversalTime();
                    var uptime = DateTime.UtcNow - bootUtc;
                    snapshot.Uptime = uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime;
                }
            }
        }
    }

    private static void ReadCpu(SystemSnapshot snapshot)
    {
        using var searcher = CreateSearcher("SELECT Name FROM Win32_Processor");
        using var results = searcher.Get();
        var names = new List<string>();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                if (item["Name"] is string name && !string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }
        }
        if (names.Count > 0)
            snapshot.Cpu = string.Join("; ", names);
    }

    private static void ReadDisks(SystemSnapshot snapshot)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            // Remote shares can wait for a disconnected server. This tool shows local volumes.
            if (drive.DriveType == DriveType.Network)
                continue;
            try
            {
                snapshot.Disks.Add(drive.IsReady
                    ? new DiskInfo(drive.Name, drive.DriveType.ToString(), drive.TotalSize,
                        drive.TotalFreeSpace, "Ready")
                    : new DiskInfo(drive.Name, drive.DriveType.ToString(), null, null, "Not ready"));
            }
            catch (Exception ex) when (IsReadError(ex))
            {
                snapshot.Disks.Add(new DiskInfo(drive.Name, drive.DriveType.ToString(),
                    null, null, "Unavailable"));
                snapshot.Warnings.Add($"Cannot read drive {drive.Name} ({ex.GetType().Name}).");
            }
        }
    }

    private static void ReadNetwork(SystemSnapshot snapshot)
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            try
            {
                foreach (var address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        snapshot.LocalAddresses.Add($"{adapter.Name}: {address.Address}");
                }
            }
            catch (Exception ex) when (IsReadError(ex))
            {
                snapshot.Warnings.Add($"Cannot read adapter {adapter.Name} ({ex.GetType().Name}).");
            }
        }
    }

    private static ManagementObjectSearcher CreateSearcher(string query) =>
        new("root\\CIMV2", query, new System.Management.EnumerationOptions
        {
            Timeout = TimeSpan.FromSeconds(10)
        });

    private static void ReadSection(SystemSnapshot snapshot, string section, Action read)
    {
        try
        {
            read();
        }
        catch (Exception ex) when (IsReadError(ex))
        {
            snapshot.Warnings.Add($"{section} is unavailable ({ex.GetType().Name}).");
        }
    }

    private static bool IsReadError(Exception ex) => ex is ManagementException
        or UnauthorizedAccessException or SecurityException or COMException
        or IOException or NetworkInformationException or TimeoutException
        or InvalidOperationException or ArgumentException;
}
