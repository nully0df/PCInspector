namespace PCInspector.Models;

public sealed record DiagnosticReport(DateTime GeneratedAtUtc, ActivitySnapshot Activity,
    SystemSnapshot? System, IReadOnlyList<PersistenceItem> Startup, IReadOnlyList<string> StartupWarnings,
    string? SelectedProcessDetails, ActivitySnapshot? Baseline, IReadOnlyList<ProcessChange>? Comparison)
{
    public string Note => "Observed measurements are not a malware verdict. CPU is normalized across all logical processors. " +
        "I/O includes file, network and device transfers. Null means unavailable, not zero. Details and startup are separately captured observations.";
}
