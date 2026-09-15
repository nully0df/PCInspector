namespace PCInspector.Models;

public sealed record PersistenceItem(string Source, string Name, string Command, string Status);

public sealed record PersistenceReadResult(IReadOnlyList<PersistenceItem> Items,
    IReadOnlyList<string> Warnings);
