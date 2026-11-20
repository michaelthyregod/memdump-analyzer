using Microsoft.Diagnostics.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Analysis;

/// <summary>
/// The largest types (over 1 MB), optionally with GC-root samples. A single dump cannot prove a leak —
/// that takes two dumps a minute or so apart and the <c>diff</c> command — so the results are evidence
/// attached to other findings, not findings of their own.
/// </summary>
/// <remarks>
/// Root sampling is opt-in (<c>--gc-roots</c>): it enumerates every root including each thread's stack
/// roots, which ClrMD does at ~0.6 s per thread — over an hour on a 7,500-thread dump — and it only
/// reports a sampled object when that object is itself a root, which is rarely the case.
/// </remarks>
public static class LeakDetector
{
    private const int MaxSuspects = 20;
    private const int MaxRootPaths = 5;

    public static IReadOnlyList<LeakSuspect> Analyze(
        ClrRuntime runtime,
        HeapWalkResult walk,
        IReadOnlyList<HeapTypeStats> heapStats,
        bool includeGcRoots = false,
        CancellationToken cancellationToken = default)
    {
        var suspects = heapStats
            .Where(t => t.TotalSizeBytes > 1024 * 1024) // >1 MB
            .Take(MaxSuspects)
            .ToList();

        if (suspects.Count == 0)
            return [];

        // Map directly-rooted objects to their root kind (an object is only matched when it is itself a root)
        var rootsByObject = new Dictionary<ulong, string>();
        if (includeGcRoots)
        {
            try
            {
                foreach (var root in runtime.Heap.EnumerateRoots())
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
                // Root enumeration can fail on corrupt or mid-GC dumps; proceed without roots
            }
        }

        var results = new List<LeakSuspect>();

        foreach (var suspect in suspects)
        {
            var rootPaths = new List<string>();

            if (walk.Types.TryGetValue(suspect.TypeName, out var acc))
            {
                foreach (var addr in acc.Samples)
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
