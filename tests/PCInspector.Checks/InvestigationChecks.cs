using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Xml;
using PCInspector.Services;

internal static class InvestigationChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(NetworkConnectionService.TcpState(6) == "FIN_WAIT_1" &&
            NetworkConnectionService.TcpState(11) == "TIME_WAIT", "TCP close states distinguish FIN_WAIT_1 from TIME_WAIT");
        check(NetworkConnectionService.Endpoint(0x0100007f, 0xbb01) == "127.0.0.1:443",
            "Native IPv4 socket address and port decode correctly");
        check(NetworkConnectionService.Endpoint(IPAddress.IPv6Loopback.GetAddressBytes(), 0, 0xbb01) == "[::1]:443",
            "IPv6 socket endpoint has brackets and the correct port");
        check(NetworkConnectionService.ReadDetailed(0).Warnings.Count != 0,
            "Unavailable socket ownership is distinct from zero connections");
        check(FileAnalysisService.DescribeSignature(0).State == FileSignatureState.Verified &&
            FileAnalysisService.DescribeSignature(0x800B0100).State == FileSignatureState.NoEmbeddedSignature &&
            FileAnalysisService.DescribeSignature(0x80096010).State == FileSignatureState.Invalid &&
            FileAnalysisService.DescribeSignature(0x800B0109).State == FileSignatureState.Untrusted &&
            FileAnalysisService.DescribeSignature(5).State == FileSignatureState.Unavailable,
            "Signature results distinguish trust failure, changed bytes, absent embedded signature and read failure");
        var tasks = PersistenceService.ParseScheduledTaskXml("""
            <?xml version="1.0" encoding="utf-16"?>
            <Tasks>
              <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                <RegistrationInfo><URI>\Работа\Проверка</URI></RegistrationInfo>
                <Settings><Enabled>false</Enabled></Settings>
                <Actions><Exec><Command>C:\Program Files\пример.exe</Command><Arguments>--name &quot;тест&quot;</Arguments></Exec>
                  <ComHandler><ClassId>{00112233-4455-6677-8899-AABBCCDDEEFF}</ClassId></ComHandler></Actions>
              </Task>
              <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                <RegistrationInfo><URI>\Second</URI></RegistrationInfo><Actions><Exec><Command>second.exe</Command></Exec></Actions>
              </Task>
            </Tasks>
            """);
        check(tasks.Items.Count == 2 && tasks.Warnings.Count == 0 &&
            tasks.Items[0].Name == @"\Работа\Проверка" && tasks.Items[0].Status == "Disabled" &&
            tasks.Items[0].Command.Contains("--name \"тест\"") && tasks.Items[0].Command.Contains("COM handler") &&
            tasks.Items[1].Status == "Enabled", "Scheduled-task XML preserves Unicode, multiple actions and disabled state");
        check(PersistenceService.ParseScheduledTaskXml("<Tasks />").Items.Count == 0,
            "A valid empty task document represents no readable tasks");
        var missing = PersistenceService.ParseScheduledTaskXml("<Task><Actions /></Task>");
        check(missing.Items.Count == 1 && missing.Warnings.Count == 1,
            "Missing scheduled-task names produce a coverage warning");
        var rejectsDtd = false;
        try { PersistenceService.ParseScheduledTaskXml("<!DOCTYPE Tasks [<!ENTITY e SYSTEM 'file:///missing'>]><Tasks>&e;</Tasks>"); }
        catch (XmlException) { rejectsDtd = true; }
        check(rejectsDtd, "Scheduled-task XML cannot load external entities");
    }

    public static async Task RunLiveAsync(Action<bool, string> check)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        using var server = await listener.AcceptTcpClientAsync();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var udpEndpoint = (IPEndPoint)udp.Client.LocalEndPoint!;
        var sockets = NetworkConnectionService.ReadDetailed(Environment.ProcessId);
        check(sockets.Warnings.Count == 0, "Real socket tables are readable for IPv4 and IPv6");
        check(sockets.Connections.Any(connection => connection.Protocol == "TCP" &&
            connection.LocalEndpoint == endpoint.ToString() && connection.State == "LISTENING"),
            "Native TCP table attributes the real loopback listener to this process");
        check(sockets.Connections.Any(connection => connection.Protocol == "TCP" &&
            connection.LocalEndpoint == endpoint.ToString() && connection.State == "ESTABLISHED"),
            "Native TCP table captures the real established loopback connection");
        check(sockets.Connections.Any(connection => connection.Protocol == "UDP" &&
            connection.LocalEndpoint == udpEndpoint.ToString() && connection.State == "BOUND"),
            "Native UDP table attributes the real bound socket to this process");
        if (Socket.OSSupportsIPv6)
        {
            using var listener6 = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener6.Start();
            using var udp6 = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            var sockets6 = NetworkConnectionService.ReadDetailed(Environment.ProcessId);
            check(sockets6.Connections.Any(connection => connection.Protocol == "TCPv6" &&
                connection.LocalEndpoint == listener6.LocalEndpoint.ToString() && connection.State == "LISTENING"),
                "Native IPv6 TCP structure decodes the real loopback listener");
            check(sockets6.Connections.Any(connection => connection.Protocol == "UDPv6" &&
                connection.LocalEndpoint == udp6.Client.LocalEndPoint!.ToString()),
                "Native IPv6 UDP structure decodes the real bound socket");
        }

        var unsignedPath = typeof(InvestigationChecks).Assembly.Location;
        var unsigned = await FileAnalysisService.AnalyzeAsync(unsignedPath);
        using (var input = File.OpenRead(unsignedPath))
            check(unsigned?.Sha256 == Convert.ToHexString(SHA256.HashData(input)), "File analysis SHA-256 matches the executable bytes");
        check(unsigned?.SignatureState == FileSignatureState.NoEmbeddedSignature,
            "A real unsigned PE is reported as no embedded signature, not a trust failure");
        check((await FileAnalysisService.AnalyzeAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")))?.SignatureState
            == FileSignatureState.Unavailable, "A missing file is unavailable, not unsigned");

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var signedPath = Path.Combine(system, "wintrust.dll");
        var signed = await FileAnalysisService.AnalyzeAsync(signedPath);
        if (signed?.SignatureState == FileSignatureState.Verified)
        {
            check(signed.Publisher != "Unknown publisher" && !string.IsNullOrWhiteSpace(signed.Publisher),
                "WinVerifyTrust validates a real Windows binary and extracts its publisher");
            var copy = Path.Combine(Path.GetTempPath(), $"pcinspector-signature-check-{Guid.NewGuid():N}.exe");
            try
            {
                var bytes = await File.ReadAllBytesAsync(signedPath);
                // A byte in the executable body is covered by Authenticode; never modify the Windows original.
                bytes[4096] ^= 1;
                await File.WriteAllBytesAsync(copy, bytes);
                check((await FileAnalysisService.AnalyzeAsync(copy))?.SignatureState == FileSignatureState.Invalid,
                    "Changing signed binary bytes causes a real Authenticode digest failure");
            }
            finally { File.Delete(copy); }
        }
        else Console.WriteLine($"SKIP: Windows binary trust fixture unavailable: {signed?.Signature}");
    }
}
