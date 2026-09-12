using System.Globalization;

namespace PCInspector;

public static class DisplayFormat
{
    public static string Gibibytes(double? bytes) => bytes is null
        ? "Unavailable"
        : (bytes.Value / (1024 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GiB";

    public static string Uptime(TimeSpan? uptime) => uptime is not { } value
        ? "Unavailable"
        : $"{value.Days}d {value.Hours:00}h {value.Minutes:00}m {value.Seconds:00}s";
}
