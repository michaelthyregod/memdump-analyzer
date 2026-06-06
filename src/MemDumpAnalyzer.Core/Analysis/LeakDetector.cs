using Microsoft.Diagnostics.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

public static class LeakDetector
{
    private const int MaxSuspects = 20;
    private const int MaxRootPaths = 5;

    public static IReadOnlyList<LeakSuspect> Analyze(
        ClrRuntime runtime,
        IReadOnlyList<HeapTypeStats> heapStats,
        CancellationToken cancellationToken = default)
    {
        var heap = runtime.Heap;
        if (!heap.CanWalkHeap)
            return [];

        // Focus on large types
        var suspects = heapStats
            .Where(t => t.TotalSizeBytes > 1024 * 1024) // >1 MB
            .Take(MaxSuspects)
            .ToList();

        if (suspects.Count == 0)
            return [];

        var suspectTypeNames = suspects.Select(s => s.TypeName).ToHashSet();

        // Sample addresses per type
        var sampleAddresses = new Dictionary<string, List<ulong>>();
        foreach (var obj in heap.EnumerateObjects())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (obj.IsNull || !obj.IsValid || obj.Type == null) continue;
            var name = obj.Type.Name ?? string.Empty;
            if (!suspectTypeNames.Contains(name)) continue;

            if (!sampleAddresses.TryGetValue(name, out var list))
                sampleAddresses[name] = list = [];

            if (list.Count < 3)
                list.Add(obj.Address);
        }

        // Build root lookup once — expensive on large heaps, limit to small set
        var rootsByObject = new Dictionary<ulong, string>();
        try
        {
            foreach (var root in heap.EnumerateRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var addr = root.Object.Address;
                if (!rootsByObject.ContainsKey(addr))
                    rootsByObject[addr] = $"{root.RootKind}: {root.Address:x16}";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Root enumeration can fail on corrupt dumps; proceed without roots
        }

        var results = new List<LeakSuspect>();

        foreach (var suspect in suspects)
        {
            var rootPaths = new List<string>();

            if (sampleAddresses.TryGetValue(suspect.TypeName, out var addresses))
            {
                foreach (var addr in addresses)
                {
                    if (rootsByObject.TryGetValue(addr, out var rootPath))
                        rootPaths.Add($"{rootPath} → {addr:x16}");

                    if (rootPaths.Count >= MaxRootPaths) break;
                }
            }

            results.Add(new LeakSuspect(
                TypeName: suspect.TypeName,
                InstanceCount: suspect.InstanceCount,
                TotalSizeBytes: suspect.TotalSizeBytes,
                RootPaths: rootPaths
            ));
        }

        return results;
    }
}
