using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PCInspector.Models;

namespace PCInspector.Services;

internal readonly record struct ProcessIoCounters(ulong ReadBytes, ulong WriteBytes);

// These are process I/O bytes (files, network and devices), not physical disk throughput.
// Keep the handle open across identity verification and measurement so PID reuse is safe.
internal static class ProcessIoService
{
    public static ProcessIoCounters? Read(ProcessIdentity identity)
    {
        using var handle = OpenProcess(0x1000, false, identity.Pid); // QUERY_LIMITED_INFORMATION
        if (handle.IsInvalid || !GetProcessTimes(handle, out var created, out _, out _, out _))
            return null;
        if (DateTime.FromFileTimeUtc(created).Ticks != identity.StartTimeUtcTicks ||
            !GetProcessIoCounters(handle, out var counters))
            return null;
        return new ProcessIoCounters(counters.ReadTransferCount, counters.WriteTransferCount);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process,
        out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(SafeProcessHandle process, out IoCounters counters);
}
