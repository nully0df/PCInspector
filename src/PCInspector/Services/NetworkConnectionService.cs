using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using PCInspector.Models;

namespace PCInspector.Services;

internal static class NetworkConnectionService
{
    private const uint ErrorInsufficientBuffer = 122;
    private delegate uint ReadTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

    public static IReadOnlyList<NetworkConnection> ReadForProcess(int pid) => ReadDetailed(pid).Connections;

    public static NetworkReadResult ReadDetailed(int pid)
    {
        var connections = new List<NetworkConnection>();
        var warnings = new List<string>();
        if (pid <= 0) return new(connections, ["Socket ownership is unavailable for this process."]);
        Read<TcpRow>(GetExtendedTcpTable, 2, 5, "TCP/IPv4", warnings, row =>
        {
            if (row.OwningPid == pid) connections.Add(new("TCP", Endpoint(row.LocalAddress, row.LocalPort),
                row.State == 2 ? "—" : Endpoint(row.RemoteAddress, row.RemotePort), TcpState(row.State)));
        });
        Read<Tcp6Row>(GetExtendedTcpTable, 23, 5, "TCP/IPv6", warnings, row =>
        {
            if (row.OwningPid == pid) connections.Add(new("TCPv6", Endpoint(row.LocalAddress, row.LocalScope, row.LocalPort),
                row.State == 2 ? "—" : Endpoint(row.RemoteAddress, row.RemoteScope, row.RemotePort), TcpState(row.State)));
        });
        Read<UdpRow>(GetExtendedUdpTable, 2, 1, "UDP/IPv4", warnings, row =>
        {
            if (row.OwningPid == pid) connections.Add(new("UDP", Endpoint(row.LocalAddress, row.LocalPort),
                "Not provided by Windows", "BOUND"));
        });
        Read<Udp6Row>(GetExtendedUdpTable, 23, 1, "UDP/IPv6", warnings, row =>
        {
            if (row.OwningPid == pid) connections.Add(new("UDPv6", Endpoint(row.LocalAddress, row.LocalScope, row.LocalPort),
                "Not provided by Windows", "BOUND"));
        });
        return new(connections, warnings);
    }

    private static void Read<TRow>(ReadTable getTable, int family, int tableClass, string source,
        List<string> warnings, Action<TRow> add) where TRow : struct
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            int size = 0;
            uint status = getTable(IntPtr.Zero, ref size, true, family, tableClass, 0);
            for (int attempt = 0; attempt < 4 && status == ErrorInsufficientBuffer; attempt++)
            {
                if (size < sizeof(int) || size > 64 * 1024 * 1024)
                    throw new InvalidDataException("The socket table has an invalid size.");
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                int capacity = size;
                buffer = Marshal.AllocHGlobal(capacity);
                status = getTable(buffer, ref size, true, family, tableClass, 0);
                if (status != 0) continue;
                int count = Marshal.ReadInt32(buffer);
                int rowSize = Marshal.SizeOf<TRow>();
                if (count < 0 || count > (capacity - sizeof(int)) / rowSize)
                    throw new InvalidDataException("The socket table has an invalid row count.");
                for (int index = 0; index < count; index++)
                    add(Marshal.PtrToStructure<TRow>(IntPtr.Add(buffer, sizeof(int) + index * rowSize)));
                return;
            }
            if (status != 0) warnings.Add($"{source}: {new Win32Exception((int)status).Message}");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
            or ExternalException or InvalidDataException)
        {
            warnings.Add($"{source}: {ex.Message}");
        }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }

    internal static string Endpoint(uint address, uint port) =>
        $"{new IPAddress(address)}:{HostPort(port)}";

    internal static string Endpoint(byte[] address, uint scope, uint port) =>
        $"[{new IPAddress(address, scope)}]:{HostPort(port)}";

    private static ushort HostPort(uint port) =>
        unchecked((ushort)IPAddress.NetworkToHostOrder((short)(port & 0xffff)));

    internal static string TcpState(uint state) => state switch
    {
        1 => "CLOSED", 2 => "LISTENING", 3 => "SYN_SENT", 4 => "SYN_RECEIVED",
        5 => "ESTABLISHED", 6 => "FIN_WAIT_1", 7 => "FIN_WAIT_2", 8 => "CLOSE_WAIT",
        9 => "CLOSING", 10 => "LAST_ACK", 11 => "TIME_WAIT", 12 => "DELETE_TCB",
        _ => $"Unknown ({state})"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UdpRow { public uint LocalAddress, LocalPort, OwningPid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScope, LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
        public uint RemoteScope, RemotePort, State, OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Udp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScope, LocalPort, OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order,
        int addressFamily, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order,
        int addressFamily, int tableClass, uint reserved);
}
