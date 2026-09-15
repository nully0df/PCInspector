using System.Diagnostics;
using PCInspector.Models;
using PCInspector.Services;

internal static class MetricsChecks
{
    private const ulong MiB = 1024 * 1024;

    private static ProcessReading Reading(double time, ulong? readBytes, ulong? writeBytes,
        long start = 100) => new(123, "Example", start, time, time, (long)MiB,
            "example.exe", GpuPercent: 12, ReadTransferBytes: readBytes, WriteTransferBytes: writeBytes);

    public static void Run(Action<bool, string> check)
    {
        var history = new ProcessHistory(4);
        var first = history.Update([Reading(0, 10 * MiB, 20 * MiB)], 0).Single();
        check(first.ReadMiBPerSecond is null && first.WriteMiBPerSecond is null,
            "I/O first sample is unknown, not zero");
        var second = history.Update([Reading(2.5, 15 * MiB, 30 * MiB)], 2.5).Single();
        check(second.ReadMiBPerSecond == 2 && second.WriteMiBPerSecond == 4 &&
            second.ResourceHistory is { Count: 2 } && second.ResourceHistory[^1] is
                { TimestampSeconds: 2.5, CpuPercent: 25, GpuPercent: 12, MemoryMiB: 1, ReadMiBPerSecond: 2 },
            "I/O uses actual elapsed time and resource charts retain matching measurements");
        var idle = history.Update([Reading(5, 15 * MiB, 30 * MiB)], 5).Single();
        check(idle.ReadMiBPerSecond == 0 && idle.WriteMiBPerSecond == 0,
            "Idle I/O displays a measured zero");
        var reset = history.Update([Reading(6, 1, 2)], 6).Single();
        check(reset.ReadMiBPerSecond is null && reset.WriteMiBPerSecond is null,
            "I/O counter reset does not underflow into an enormous rate");
        history.Update([Reading(7, null, 3)], 7);
        var recovered = history.Update([Reading(8, MiB, MiB + 3)], 8).Single();
        check(recovered.ReadMiBPerSecond is null && recovered.WriteMiBPerSecond == 1 && recovered.CpuPercent == 25,
            "Missing read counter breaks only that counter's baseline");
        var absent = history.Update([], 9).Single();
        check(absent.CpuPercent is null && absent.GpuPercent is null && absent.MemoryMiB is null &&
            absent.ReadMiBPerSecond is null && absent.WriteMiBPerSecond is null &&
            absent.ResourceHistory![^1] == new ResourceSample(9, null, null, null, null, null),
            "Absent process clears current GPU/I/O and adds a chart gap");
        var returned = history.Update([Reading(10, 2 * MiB, 2 * MiB)], 10).Single();
        check(returned.ReadMiBPerSecond is null && returned.WriteMiBPerSecond is null,
            "Re-observed process starts fresh I/O baselines");
        history.BreakSampling(10.5);
        var afterFailure = history.Update([Reading(11, 3 * MiB, 3 * MiB)], 11).Single();
        check(afterFailure.ReadMiBPerSecond is null && afterFailure.WriteMiBPerSecond is null &&
            afterFailure.ResourceHistory!.Contains(new ResourceSample(10.5, null, null, null, null, null)),
            "Whole-scan failure breaks I/O baselines and all resource chart lines");
        var afterPause = history.Update([Reading(72, 4 * MiB, 4 * MiB)], 72).Single();
        check(afterPause.ReadMiBPerSecond is null && afterPause.ResourceHistory!.All(s => s.TimestampSeconds >= 12),
            "Long pause resets I/O and prunes old chart samples");
        var reused = history.Update([Reading(73, 8 * MiB, 8 * MiB, start: 200)], 73);
        check(reused.Count == 2 && reused.Single(r => r.Identity!.Value.StartTimeUtcTicks == 200) is
            { ReadMiBPerSecond: null, WriteMiBPerSecond: null, ResourceHistory.Count: 1 },
            "Reused PID receives its own I/O baseline and resource history");

        var invalidTiming = new ProcessHistory(4);
        invalidTiming.Update([Reading(0, 0, 0)], 0);
        var tooSmall = invalidTiming.Update([Reading(double.Epsilon, ulong.MaxValue, ulong.MaxValue)
            with { CpuSeconds = double.MaxValue }], double.Epsilon).Single();
        check(tooSmall.CpuPercent is null && tooSmall.ReadMiBPerSecond is null && tooSmall.WriteMiBPerSecond is null,
            "Non-finite rates from invalid timing are rejected instead of entering charts or reports");

        var gpu = GpuUsageService.Aggregate([
            new("pid_123_luid_0x00000000_0x1234_phys_0_eng_0_engtype_3D", 0, 42),
            new("pid_123_luid_0x00000000_0x1234_phys_0_eng_1_engtype_Compute", 1, 68),
            new("pid_123_luid_0x00000000_0x1234_phys_0_eng_2_engtype_Copy", 0xC0000BBA, 99),
            new("pid_124_luid_0_eng_0", 1, 0),
            new("pid_125_luid_0_eng_0", 0, double.NaN),
            new("pid_126_luid_0_eng_0", 0, double.PositiveInfinity),
            new("not-a-process", 0, 100)
        ]);
        check(gpu.Count == 2 && gpu[123] == 68 && gpu[124] == 0,
            "GPU uses the busiest valid engine and accepts new data without summing engines");
        var metadata = new ProcessMetadata("original.exe", "original.exe --flag", 1, 1000);
        check(metadata.Matches(1009) && !metadata.Matches(1010) && !metadata.Matches(null),
            "WMI identity comparison respects microsecond precision and rejects reused PIDs");
        List<ProcessReading> family = [
            Reading(0, 0, 0, 300) with { Pid = 1, Name = "reused-parent" },
            Reading(0, 0, 0, 200) with { Pid = 2, ParentPid = 1 },
            Reading(0, 0, 0, 400) with { Pid = 3, ParentPid = 1 }
        ];
        ProcessSampler.ResolveParents(family);
        check(family[1].ParentPid is null && family[1].ParentName is null &&
            family[2].ParentPid == 1 && family[2].ParentName == "reused-parent",
            "Parent relationship rejects reused PID born after child and keeps valid ancestry");
        using var disposedGpu = new GpuUsageService();
        disposedGpu.Dispose();
        check(disposedGpu.Read().Count == 0, "Disposed GPU sampler cannot reopen native counters");
    }

    public static void RunLive(Action<bool, string> check)
    {
        using var process = Process.GetCurrentProcess();
        var identity = new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks);
        var before = ProcessIoService.Read(identity);
        var buffer = new byte[MiB];
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"PCInspector-io-{Guid.NewGuid():N}.tmp");
        using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 4096, FileOptions.DeleteOnClose))
        {
            file.Write(buffer);
            file.Flush();
            file.Position = 0;
            file.ReadExactly(buffer);
        }
        var after = ProcessIoService.Read(identity);
        check(before is { } first && after is { } second &&
            second.ReadBytes - first.ReadBytes >= MiB && second.WriteBytes - first.WriteBytes >= MiB,
            "Native I/O counters attribute known file reads and writes to this process");
        check(ProcessIoService.Read(identity with { StartTimeUtcTicks = identity.StartTimeUtcTicks - 1 }) is null,
            "Native I/O reader rejects an identity mismatch before attributing bytes");
    }
}
