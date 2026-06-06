using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class DiffAnalyzer
{
    public static DiffResult Compare(
        IReadOnlyList<HeapTypeStats> baseline,
        IReadOnlyList<HeapTypeStats> problem,
        GcStats baselineGc,
        GcStats problemGc,
        int baselineThreadCount,
        int problemThreadCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var baselineDict = baseline.ToDictionary(t => t.TypeName);
        var problemDict = problem.ToDictionary(t => t.TypeName);

        var growing = new List<TypeDelta>();
        var shrinking = new List<TypeDelta>();
        var newTypes = new List<string>();
        var disappeared = new List<string>();

        foreach (var (name, pStat) in problemDict)
        {
            if (baselineDict.TryGetValue(name, out var bStat))
            {
                long countDelta = pStat.InstanceCount - bStat.InstanceCount;
                long sizeDelta = pStat.TotalSizeBytes - bStat.TotalSizeBytes;

                if (sizeDelta > 0 || countDelta > 0)
                    growing.Add(new TypeDelta(name, bStat.InstanceCount, pStat.InstanceCount, countDelta,
                        bStat.TotalSizeBytes, pStat.TotalSizeBytes, sizeDelta));
                else if (sizeDelta < 0 || countDelta < 0)
                    shrinking.Add(new TypeDelta(name, bStat.InstanceCount, pStat.InstanceCount, countDelta,
                        bStat.TotalSizeBytes, pStat.TotalSizeBytes, sizeDelta));
            }
            else
            {
                newTypes.Add(name);
            }
        }

        foreach (var name in baselineDict.Keys)
        {
            if (!problemDict.ContainsKey(name))
                disappeared.Add(name);
        }

        var findings = new List<Finding>();

        long heapDelta = problemGc.TotalHeapBytes - baselineGc.TotalHeapBytes;
        if (heapDelta > 50 * 1024 * 1024)
        {
            findings.Add(new Finding(
                "HeapGrowthBetweenSnapshots",
                Severity.Warning,
                $"Heap grew by {heapDelta / (1024 * 1024):N0} MB between snapshots",
                "A significant increase in managed heap between two captures indicates an active memory leak or sustained allocation pressure.",
                Evidence: $"Baseline: {baselineGc.TotalHeapBytes / (1024 * 1024):N0} MB → Problem: {problemGc.TotalHeapBytes / (1024 * 1024):N0} MB",
                Recommendation: "Inspect the growing types table to identify which objects accumulated."
            ));
        }

        var topGrowing = growing.OrderByDescending(t => t.SizeDeltaBytes).FirstOrDefault();
        if (topGrowing != null && topGrowing.SizeDeltaBytes > 10 * 1024 * 1024)
        {
            findings.Add(new Finding(
                "TypeGrowthDetected",
                Severity.Warning,
                $"{topGrowing.TypeName} grew by {topGrowing.SizeDeltaBytes / (1024 * 1024):N0} MB",
                "One or more types show substantial growth between snapshots, suggesting a leak or unbounded cache.",
                Evidence: $"{topGrowing.CountDelta:+#;-#;0} instances, {topGrowing.SizeDeltaBytes / 1024:N0} KB delta",
                Recommendation: "Review retention paths for this type using a memory profiler or LeakDetector findings."
            ));
        }

        return new DiffResult(
            HeapDeltaBytes: heapDelta,
            Gen0DeltaBytes: problemGc.Gen0Bytes - baselineGc.Gen0Bytes,
            Gen1DeltaBytes: problemGc.Gen1Bytes - baselineGc.Gen1Bytes,
            Gen2DeltaBytes: problemGc.Gen2Bytes - baselineGc.Gen2Bytes,
            LohDeltaBytes: problemGc.LohBytes - baselineGc.LohBytes,
            ThreadDelta: problemThreadCount - baselineThreadCount,
            GrowingTypes: growing.OrderByDescending(t => t.SizeDeltaBytes).ToList(),
            ShrinkingTypes: shrinking.OrderBy(t => t.SizeDeltaBytes).ToList(),
            NewTypes: newTypes,
            DisappearedTypes: disappeared,
            Findings: findings
        );
    }
}
