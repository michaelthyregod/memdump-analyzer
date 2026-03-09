namespace MemDumpAnalyzer.Core.Models;

/// <summary>
/// A cluster of threads sharing the same top-of-stack pattern.
/// Grouping reveals what the bulk of threads are waiting on.
/// </summary>
public record ThreadGroup(
    int ThreadCount,
    double Percentage,
    string? WaitReason,             // human-readable: "HTTP I/O", "SQL database I/O", "CLR Monitor lock", etc.
    IReadOnlyList<string> RepresentativeStack,  // top frames (with [Assembly] annotations)
    IReadOnlyList<uint> SampleOsThreadIds       // a small sample of OS thread IDs in this group
);
