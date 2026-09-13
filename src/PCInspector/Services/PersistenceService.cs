using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using PCInspector.Models;

namespace PCInspector.Services;

internal static class PersistenceService
{
    public static IReadOnlyList<PersistenceItem> Read()
    {
        var items = new List<PersistenceItem>();
        ReadRunKey(items, "Current user", RegistryHive.CurrentUser);
        ReadRunKey(items, "All users", RegistryHive.LocalMachine);
        ReadServices(items);
        ReadScheduledTasks(items);
        return items.OrderBy(item => item.Source).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ReadRunKey(List<PersistenceItem> items, string source, RegistryHive hive)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key is null) return;
            foreach (var name in key.GetValueNames())
                items.Add(new PersistenceItem($"{source} startup", name, key.GetValue(name)?.ToString() ?? "", "Run key"));
        }
        catch (Exception ex) when (IsReadError(ex)) { }
    }

    private static void ReadServices(List<PersistenceItem> items)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope("root\\CIMV2"),
                new ObjectQuery("SELECT Name, DisplayName, PathName, StartMode, State FROM Win32_Service WHERE StartMode = 'Auto'"),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) });
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var name = item["DisplayName"] as string ?? item["Name"] as string ?? "Unnamed service";
                    var command = item["PathName"] as string ?? "Unavailable";
                    var state = item["State"] as string ?? "Unknown";
                    items.Add(new PersistenceItem("Automatic service", name, command, state));
                }
            }
        }
        catch (Exception ex) when (IsReadError(ex)) { }
    }

    private static void ReadScheduledTasks(List<PersistenceItem> items)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/Query /FO CSV /V",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            var lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return;
            var header = ParseCsv(lines[0]);
            var nameIndex = FindColumn(header, "taskname", "task name");
            var commandIndex = FindColumn(header, "tasktorun", "task to run");
            var statusIndex = FindColumn(header, "status");
            foreach (var line in lines.Skip(1).Take(300))
            {
                var columns = ParseCsv(line);
                if (columns.Count == 0) continue;
                var name = GetColumn(columns, nameIndex) ?? columns[0];
                if (string.IsNullOrWhiteSpace(name) || name.Equals("INFO:", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new PersistenceItem("Scheduled task", name,
                    GetColumn(columns, commandIndex) ?? "Command unavailable",
                    GetColumn(columns, statusIndex) ?? "Registered"));
            }
        }
        catch (Exception ex) when (IsReadError(ex)) { }
    }

    private static int FindColumn(IReadOnlyList<string> columns, params string[] names)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            var normalized = columns[index].Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
            if (names.Any(name => normalized.Contains(name, StringComparison.OrdinalIgnoreCase))) return index;
        }
        return -1;
    }

    private static string? GetColumn(IReadOnlyList<string> columns, int index) =>
        index >= 0 && index < columns.Count ? columns[index] : null;

    private static List<string> ParseCsv(string line)
    {
        var values = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
            else value.Append(character);
        }
        values.Add(value.ToString());
        return values;
    }

    private static bool IsReadError(Exception ex) => ex is ManagementException or UnauthorizedAccessException
        or IOException or InvalidOperationException or Win32Exception or TimeoutException;
}
