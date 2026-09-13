using System.Net;
using System.Runtime.InteropServices;
using PCInspector.Models;

namespace PCInspector.Services;

internal static class NetworkConnectionService
{
    private const int AfInet = 2;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint NoError = 0;
    private const int TcpTableOwnerPid = 5;
    private const int UdpTableOwnerPid = 1;

    public static IReadOnlyList<NetworkConnection> ReadForProcess(int pid)
    {
        var result = new List<NetworkConnection>();
        ReadTcp(pid, result);
        ReadUdp(pid, result);
        return result;
    }

    private static void ReadTcp(int pid, List<NetworkConnection> result)
    {
        var buffer = IntPtr.Zero;
        try
        {
            var size = 0;
            var status = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AfInet, TcpTableOwnerPid, 0);
            if (status != ErrorInsufficientBuffer || size <= 0) return;
            buffer = Marshal.AllocHGlobal(size);
            status = GetExtendedTcpTable(buffer, ref size, true, AfInet, TcpTableOwnerPid, 0);
            if (status != NoError) return;
            var count = Marshal.ReadInt32(buffer);
            var offset = sizeof(int);
            var rowSize = Marshal.SizeOf<TcpRow>();
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<TcpRow>(IntPtr.Add(buffer, offset + index * rowSize));
                if (row.OwningPid != pid) continue;
                result.Add(new NetworkConnection("TCP",
                    Endpoint(row.LocalAddress, row.LocalPort), Endpoint(row.RemoteAddress, row.RemotePort),
                    TcpState(row.State)));
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (ExternalException) { }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }

    private static void ReadUdp(int pid, List<NetworkConnection> result)
    {
        var buffer = IntPtr.Zero;
        try
        {
            var size = 0;
            var status = GetExtendedUdpTable(IntPtr.Zero, ref size, true, AfInet, UdpTableOwnerPid, 0);
            if (status != ErrorInsufficientBuffer || size <= 0) return;
            buffer = Marshal.AllocHGlobal(size);
            status = GetExtendedUdpTable(buffer, ref size, true, AfInet, UdpTableOwnerPid, 0);
            if (status != NoError) return;
            var count = Marshal.ReadInt32(buffer);
            var offset = sizeof(int);
            var rowSize = Marshal.SizeOf<UdpRow>();
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<UdpRow>(IntPtr.Add(buffer, offset + index * rowSize));
                if (row.OwningPid != pid) continue;
                result.Add(new NetworkConnection("UDP",
                    Endpoint(row.LocalAddress, row.LocalPort), "*:*", "Listening / active"));
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (ExternalException) { }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }

    private static string Endpoint(uint address, uint port)
    {
        var host = new IPAddress(address).ToString();
        var networkPort = (ushort)(port & 0xffff);
        var localPort = (ushort)((networkPort >> 8) | (networkPort << 8));
        return $"{host}:{localPort}";
    }

    private static string TcpState(uint state) => state switch
    {
        2 => "LISTENING",
        5 => "ESTABLISHED",
        6 => "TIME_WAIT",
        1 => "CLOSED",
        _ => $"State {state}"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UdpRow
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order,
        int addressFamily, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order,
        int addressFamily, int tableClass, uint reserved);
}
