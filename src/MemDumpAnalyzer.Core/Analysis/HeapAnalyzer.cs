using Microsoft.Diagnostics.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class HeapAnalyzer
{
    public static IReadOnlyList<HeapTypeStats> Analyze(
        ClrRuntime runtime, int topN = 100, CancellationToken cancellationToken = default)
    {
        var heap = runtime.Heap;
        if (!heap.CanWalkHeap)
            return [];

        // Group by type name — track count, size, and last seen generation
        var stats = new Dictionary<string, (long Count, long TotalSize, int LastGen)>();

        foreach (var obj in heap.EnumerateObjects())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (obj.IsNull || !obj.IsValid) continue;
            var typeName = obj.Type?.Name ?? "<unknown>";
            var size = (long)obj.Size;

            // Determine generation via segment lookup
            int gen = GetGeneration(heap, obj.Address);

            if (!stats.TryGetValue(typeName, out var existing))
                stats[typeName] = (1, size, gen);
            else
                stats[typeName] = (existing.Count + 1, existing.TotalSize + size, gen);
        }

        return stats
            .Select(kvp => new HeapTypeStats(
                TypeName: kvp.Key,
                InstanceCount: kvp.Value.Count,
                TotalSizeBytes: kvp.Value.TotalSize,
                OwnSizeBytes: kvp.Value.TotalSize,
                Generation: kvp.Value.LastGen
            ))
            .OrderByDescending(s => s.TotalSizeBytes)
            .Take(topN)
            .ToList();
    }

    internal static int GetGeneration(ClrHeap heap, ulong address)
    {
        var seg = heap.GetSegmentByAddress(address);
        if (seg == null) return -1;

        return seg.Kind switch
        {
            GCSegmentKind.Generation0 => 0,
            GCSegmentKind.Generation1 => 1,
            GCSegmentKind.Generation2 => 2,
            GCSegmentKind.Ephemeral => GetEphemeralGeneration(seg, address),
            GCSegmentKind.Large => 3,
            GCSegmentKind.Pinned => 3,
            GCSegmentKind.Frozen => 3,
            _ => -1
        };
    }

    private static int GetEphemeralGeneration(ClrSegment seg, ulong address)
    {
        // Ephemeral segment contains Gen0, Gen1, and possibly Gen2 sub-ranges
        if (seg.Generation0.Contains(address)) return 0;
        if (seg.Generation1.Contains(address)) return 1;
        if (seg.Generation2.Contains(address)) return 2;
        return 0; // default to gen0 if unclear
    }
}
