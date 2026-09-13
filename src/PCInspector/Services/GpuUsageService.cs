using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PCInspector.Services;

// GPU Engine is a Windows performance counter. It reports engine utilization per
// process and works without a vendor-specific NVIDIA/AMD SDK.
internal sealed class GpuUsageService : IDisposable
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhOk = 0;
    private readonly object gate = new();
    private IntPtr query;
    private IntPtr counter;
    private bool initialized;
    private static readonly Regex PidPattern = new("pid_(?<pid>\\d+)_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Dictionary<int, double> Read()
    {
        lock (gate)
        {
            if (!EnsureInitialized() || PdhCollectQueryData(query) != PdhOk)
                return [];
            var bufferSize = 0;
            var itemCount = 0;
            var status = PdhGetFormattedCounterArray(counter, PdhFmtDouble,
                ref bufferSize, ref itemCount, IntPtr.Zero);
            if (status != PdhMoreData || bufferSize <= 0 || itemCount <= 0)
                return [];
            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                status = PdhGetFormattedCounterArray(counter, PdhFmtDouble,
                    ref bufferSize, ref itemCount, buffer);
                if (status != PdhOk) return [];
                var result = new Dictionary<int, double>();
                var itemSize = Marshal.SizeOf<PdhItem>();
                for (var index = 0; index < itemCount; index++)
                {
                    var item = Marshal.PtrToStructure<PdhItem>(buffer + index * itemSize);
                    var instance = Marshal.PtrToStringUni(item.Name);
                    if (instance is null) continue;
                    var match = PidPattern.Match(instance);
                    if (!match.Success || !int.TryParse(match.Groups["pid"].Value, out var pid)) continue;
                    if (item.Value.Status != PdhOk || double.IsNaN(item.Value.DoubleValue)) continue;
                    result[pid] = Math.Clamp(result.GetValueOrDefault(pid) + item.Value.DoubleValue, 0, 100);
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = IntPtr.Zero;
            counter = IntPtr.Zero;
            initialized = false;
        }
    }

    private bool EnsureInitialized()
    {
        if (initialized) return true;
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != PdhOk) return false;
        if (PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out counter) != PdhOk)
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

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath,
        IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format,
        ref int bufferSize, ref int itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
