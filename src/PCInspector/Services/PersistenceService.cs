using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using PCInspector.Models;

namespace PCInspector.Services;

internal static class PersistenceService
{
    public static IReadOnlyList<PersistenceItem> Read() => ReadDetailed().Items;

    public static PersistenceReadResult ReadDetailed()
    {
        var items = new List<PersistenceItem>();
        var warnings = new List<string>();
        ReadRunKeys(items, warnings, "Current user", RegistryHive.CurrentUser, RegistryView.Default);
        ReadRunKeys(items, warnings, Environment.Is64BitOperatingSystem ? "All users (64-bit)" : "All users",
            RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        if (Environment.Is64BitOperatingSystem)
            ReadRunKeys(items, warnings, "All users (32-bit)", RegistryHive.LocalMachine, RegistryView.Registry32);
        ReadStartupFolder(items, warnings, Environment.SpecialFolder.Startup, "Current user startup folder");
        ReadStartupFolder(items, warnings, Environment.SpecialFolder.CommonStartup, "All users startup folder");
        ReadServices(items, warnings);
        ReadScheduledTasks(items, warnings);
        return new(items.OrderBy(item => item.Source).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings);
    }

    private static void ReadRunKeys(List<PersistenceItem> items, List<string> warnings, string source,
        RegistryHive hive, RegistryView view)
    {
        foreach (var keyName in new[] { "Run", "RunOnce" })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\" + keyName);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                    items.Add(new($"{source} / {keyName}", name,
                        key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "",
                        "Registered; enabled state not checked"));
            }
            catch (Exception ex) when (IsReadError(ex)) { warnings.Add($"{source} / {keyName}: {ex.Message}"); }
        }
    }

    private static void ReadStartupFolder(List<PersistenceItem> items, List<string> warnings,
        Environment.SpecialFolder folder, string source)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path))
            {
                if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new(source, Path.GetFileName(file), file,
                    Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        ? "Shortcut; target and enabled state not checked" : "Registered; enabled state not checked"));
            }
        }
        catch (Exception ex) when (IsReadError(ex)) { warnings.Add($"{source}: {ex.Message}"); }
    }

    private static void ReadServices(List<PersistenceItem> items, List<string> warnings)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope("root\\CIMV2"),
                new ObjectQuery("SELECT Name, DisplayName, PathName, State FROM Win32_Service WHERE StartMode = 'Auto'"),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) });
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    items.Add(new("Automatic service",
                        item["DisplayName"] as string ?? item["Name"] as string ?? "Unnamed service",
                        item["PathName"] as string ?? "Unavailable", item["State"] as string ?? "Unknown"));
                }
            }
        }
        catch (Exception ex) when (IsReadError(ex)) { warnings.Add($"Automatic services: {ex.Message}"); }
    }

    private static void ReadScheduledTasks(List<PersistenceItem> items, List<string> warnings)
    {
        try
        {
            var (output, error, exitCode) = QueryTasksAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(output))
            {
                var parsed = ParseScheduledTaskXml(output);
                items.AddRange(parsed.Items);
                warnings.AddRange(parsed.Warnings);
            }
            else if (exitCode == 0)
                warnings.Add("Scheduled tasks: Windows returned no readable XML.");
            if (exitCode != 0 || !string.IsNullOrWhiteSpace(error))
                warnings.Add($"Scheduled tasks: collection may be incomplete (exit {exitCode}). {error.Trim()}");
        }
        catch (Exception ex) when (IsReadError(ex) || ex is XmlException or OperationCanceledException)
        {
            warnings.Add(ex is OperationCanceledException
                ? "Scheduled tasks: collection timed out after 12 seconds."
                : $"Scheduled tasks: {ex.Message}");
        }
    }

    private static async Task<(string Output, string Error, int ExitCode)> QueryTasksAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        // XML ONE is one XML document, independent of the Windows display language.
        // Drain both pipes concurrently; waiting after a blocking ReadToEnd is not a timeout.
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            ?? Encoding.UTF8;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
            }
        };
        process.StartInfo.ArgumentList.Add("/Query");
        process.StartInfo.ArgumentList.Add("/XML");
        process.StartInfo.ArgumentList.Add("ONE");
        if (!process.Start()) throw new IOException("Could not start the scheduled-task reader.");
        var output = ReadLimitedAsync(process.StandardOutput, 16 * 1024 * 1024, timeout.Token);
        var error = ReadLimitedAsync(process.StandardError, 64 * 1024, timeout.Token);
        try
        {
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            return (await output.ConfigureAwait(false), await error.ConfigureAwait(false), process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (output.Length > limit - count) throw new InvalidDataException("Scheduled-task output exceeded its size limit.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }

    internal static PersistenceReadResult ParseScheduledTaskXml(string xml)
    {
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024
        });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName is not ("Tasks" or "Task"))
            throw new XmlException("Unexpected scheduled-task XML root.");
        var tasks = document.Root.Name.LocalName == "Task"
            ? new[] { document.Root } : document.Root.Elements().Where(element => element.Name.LocalName == "Task");
        var items = new List<PersistenceItem>();
        int missingNames = 0;
        foreach (var task in tasks)
        {
            var name = Child(Child(task, "RegistrationInfo"), "URI")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = task.Attribute("name")?.Value ?? "Task name unavailable";
                missingNames++;
            }
            var commands = Child(task, "Actions")?.Elements().Select(action => action.Name.LocalName switch
            {
                "Exec" => string.Join(" ", new[] { Child(action, "Command")?.Value, Child(action, "Arguments")?.Value }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
                "ComHandler" => $"COM handler {Child(action, "ClassId")?.Value ?? "(class unavailable)"}",
                _ => $"{action.Name.LocalName} action"
            }).ToArray() ?? [];
            var enabled = Child(Child(task, "Settings"), "Enabled")?.Value;
            items.Add(new("Scheduled task", name, commands.Length == 0 ? "No action declared" : string.Join(" | ", commands),
                enabled is "false" or "0" ? "Disabled" : "Enabled"));
        }
        return new(items, missingNames == 0 ? [] : [$"Scheduled tasks: {missingNames} definitions did not declare a task URI."]);
    }

    private static XElement? Child(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(child => child.Name.LocalName == name);

    private static bool IsReadError(Exception ex) => ex is ManagementException or UnauthorizedAccessException
        or IOException or InvalidOperationException or Win32Exception or TimeoutException or COMException or SecurityException
        or PlatformNotSupportedException;
}
