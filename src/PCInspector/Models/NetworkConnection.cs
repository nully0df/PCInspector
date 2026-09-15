namespace PCInspector.Models;

public sealed record NetworkConnection(string Protocol, string LocalEndpoint,
    string RemoteEndpoint, string State);

public sealed record NetworkReadResult(IReadOnlyList<NetworkConnection> Connections,
    IReadOnlyList<string> Warnings);
