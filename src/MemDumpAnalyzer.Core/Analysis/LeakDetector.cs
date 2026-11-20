using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

/// <summary>
/// The largest types (over 1 MB), with the retention paths <see cref="RetentionAnalyzer"/> found for them. A single
/// dump cannot prove a leak — that takes two dumps a minute or so apart and the <c>diff</c> command — so the results
/// are evidence attached to other findings, not findings of their own.
/// </summary>
public static class LeakDetector
{
    private const int MaxSuspects = 20;

    public static IReadOnlyList<LeakSuspect> Analyze(IReadOnlyList<HeapTypeStats> heapStats, RetentionReport retention) =>
        heapStats
            .Where(t => t.TotalSizeBytes > 1024 * 1024) // >1 MB
            .Take(MaxSuspects)
            .Select(t => new LeakSuspect(
                TypeName: t.TypeName,
                InstanceCount: t.InstanceCount,
                TotalSizeBytes: t.TotalSizeBytes,
                RootPaths: retention.Paths.Where(p => p.TypeName == t.TypeName).Select(p => p.Summary).ToList()))
            .ToList();
}
