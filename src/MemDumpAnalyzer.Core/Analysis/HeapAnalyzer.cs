using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class HeapAnalyzer
{
    /// <summary>Per-type totals from the heap walk, largest first. Free objects are excluded.</summary>
    public static IReadOnlyList<HeapTypeStats> Analyze(HeapWalkResult walk, int topN = 100)
    {
        return walk.Types
            .Select(kvp => new HeapTypeStats(
                TypeName: kvp.Key,
                InstanceCount: kvp.Value.Count,
                TotalSizeBytes: kvp.Value.TotalSize,
                OwnSizeBytes: kvp.Value.TotalSize,
                Generation: DominantGeneration(kvp.Value.BytesByGeneration)
            ))
            .OrderByDescending(s => s.TotalSizeBytes)
            .Take(topN)
            .ToList();
    }

    private static int DominantGeneration(long[] bytesByGen)
    {
        int best = -1;
        long bestBytes = 0;
        for (int g = 0; g < bytesByGen.Length; g++)
        {
            if (bytesByGen[g] > bestBytes) { best = g; bestBytes = bytesByGen[g]; }
        }
        // Index 4 (POH/frozen) is reported as 3 like LOH, matching the "3 = LOH/POH/frozen" convention
        return best == HeapWalker.GenPohOrFrozen ? HeapWalker.GenLoh : best;
    }
}
