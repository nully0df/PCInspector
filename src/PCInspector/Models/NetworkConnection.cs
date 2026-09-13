namespace PCInspector.Models;

public sealed record NetworkConnection(string Protocol, string LocalEndpoint,
    string RemoteEndpoint, string State);
