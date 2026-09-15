using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PCInspector.Services;

internal sealed record GpuCounterSample(string Instance, uint Status, double Percent);

// GPU Engine is a Windows performance counter. It reports engine utilization per
// process and works without a vendor-specific NVIDIA/AMD SDK.
internal sealed class GpuUsageService : IDisposable
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhOk = 0;
    private const uint PdhNewData = 1;
    private readonly object gate = new();
    private IntPtr query;
    private IntPtr counter;
    private bool initialized;
    private bool collectedBaseline;
    private bool disposed;
    private long nextInitializeAt;
    private static readonly Regex PidPattern = new("^pid_(?<pid>\\d+)_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Dictionary<int, double> Read()
    {
        lock (gate)
        {
            if (!EnsureInitialized()) return [];
            if (PdhCollectQueryData(query) != PdhOk)
            {
                collectedBaseline = false;
                return [];
            }
            if (!collectedBaseline)
            {
                collectedBaseline = true;
                return [];
            }

            // The wildcard instance list can change between sizing and reading the array.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var bufferSize = 0;
                var itemCount = 0;
                var status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble,
                    ref bufferSize, ref itemCount, IntPtr.Zero);
                if (status != PdhMoreData || bufferSize <= 0 || bufferSize > 64 * 1024 * 1024)
                    return [];
                var allocationSize = bufferSize;
                var buffer = Marshal.AllocHGlobal(allocationSize);
                try
                {
                    status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble,
                        ref bufferSize, ref itemCount, buffer);
                    if (status == PdhMoreData) continue;
                    if (status != PdhOk) return [];
                    var samples = new List<GpuCounterSample>();
                    var itemSize = Marshal.SizeOf<PdhItem>();
                    if (itemCount < 0 || itemCount > allocationSize / itemSize) return [];
                    for (var index = 0; index < itemCount; index++)
                    {
                        var item = Marshal.PtrToStructure<PdhItem>(buffer + index * itemSize);
                        var instance = Marshal.PtrToStringUni(item.Name);
                        if (instance is not null)
                            samples.Add(new GpuCounterSample(instance, item.Value.Status, item.Value.DoubleValue));
                    }
                    return Aggregate(samples);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return [];
        }
    }

    internal static Dictionary<int, double> Aggregate(IEnumerable<GpuCounterSample> samples)
    {
        var result = new Dictionary<int, double>();
        foreach (var sample in samples)
        {
            if (sample.Status is not (PdhOk or PdhNewData) ||
                !double.IsFinite(sample.Percent) || sample.Percent < 0) continue;
            var match = PidPattern.Match(sample.Instance);
            if (!match.Success || !int.TryParse(match.Groups["pid"].Value, out var pid) || pid <= 0) continue;
            // Match Task Manager's definition: the busiest engine across this process's GPUs.
            // Summing independent engines can wrongly make a partially loaded GPU look full.
            result[pid] = Math.Max(result.GetValueOrDefault(pid), Math.Min(sample.Percent, 100));
        }
        return result;
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = IntPtr.Zero;
            counter = IntPtr.Zero;
            initialized = false;
            collectedBaseline = false;
        }
    }

    private bool EnsureInitialized()
    {
        if (disposed) return false;
        if (initialized) return true;
        if (Environment.TickCount64 < nextInitializeAt) return false;
        nextInitializeAt = Environment.TickCount64 + 30000;
        if (PdhOpenQueryW(null, IntPtr.Zero, out query) != PdhOk) return false;
        if (PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out counter) != PdhOk)
        {
            PdhCloseQuery(query);
            query = IntPtr.Zero;
            return false;
        }
        initialized = true;
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PdhItem
    {
        public IntPtr Name;
        public PdhValue Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string counterPath,
        IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format,
        ref int bufferSize, ref int itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
