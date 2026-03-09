using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Heuristics;

public static class FindingEngine
{
    private const long LohSizeThreshold = 85 * 1024 * 1024;
    private const double Gen2FillThreshold = 0.85;
    private const double LohFragThreshold = 30.0;
    private const int ThreadPoolExhaustionThreshold = 200;
    private const long DuplicateStringThreshold = 50 * 1024 * 1024;

    public static (IReadOnlyList<Finding> Findings, int HealthScore) Evaluate(
        IReadOnlyList<ThreadInfo> threads,
        IReadOnlyList<ThreadGroup> threadGroups,
        IReadOnlyList<HeapTypeStats> heapTypes,
        GcStats gc,
        IReadOnlyList<StringDuplication> strings,
        IReadOnlyList<LeakSuspect> leaks)
    {
        var findings = new List<Finding>();

        // ── Thread findings ──────────────────────────────────────────────────

        int managedThreadCount = threads.Count(t => t.ManagedThreadId > 0);
        if (managedThreadCount > ThreadPoolExhaustionThreshold)
        {
            findings.Add(new Finding(
                "ThreadPoolExhaustion",
                Severity.Critical,
                $"Thread-pool exhaustion: {managedThreadCount:N0} managed threads active",
                "The CLR thread pool is saturated. Work items are queuing faster than they complete, " +
                "typically caused by synchronous blocking inside async code (.Result/.Wait), " +
                "slow I/O, or database latency. This leads to cascading latency, request timeouts, " +
                "and eventual OutOfMemoryException as each thread allocates ~1 MB of stack.",
                Evidence: $"{managedThreadCount:N0} managed threads at time of capture",
                Recommendation:
                    "1. Replace every .Result / .Wait / .GetAwaiter().GetResult() call with 'await'. " +
                    "2. Ensure database calls use async ADO.NET/EF methods. " +
                    "3. Set ThreadPool.SetMinThreads to a higher value as a short-term relief. " +
                    "4. Add Application Insights or ETW thread-pool queue-depth monitoring to catch this early.",
                TechnicalDetails: BuildThreadGroupDetails(threads, threadGroups)
            ));
        }

        // Blocked threads → deadlock / lock contention
        var blocked = threads.Where(t => t.BlockingReason != null).ToList();
        if (blocked.Count >= 2)
        {
            findings.Add(new Finding(
                "SuspectedDeadlock",
                Severity.Critical,
                $"{blocked.Count} threads are blocked — lock contention or deadlock",
                "Multiple threads are simultaneously blocked on locks. If the same threads hold locks " +
                "that each other needs (circular dependency), they will never make progress. Even without " +
                "a true deadlock, heavy lock contention serialises work and destroys throughput.",
                Evidence: $"{blocked.Count} blocked threads; top frame: " +
                          (blocked.First().StackFrames.FirstOrDefault() ?? "unknown"),
                Recommendation:
                    "1. Identify which locks each thread holds vs. waits for (see stacks below). " +
                    "2. Ensure locks are always acquired in the same order across all code paths. " +
                    "3. Replace lock() with SemaphoreSlim for async code, or use ConcurrentDictionary/Interlocked. " +
                    "4. Consider lock-free data structures if the contended resource allows it.",
                TechnicalDetails: BuildBlockedThreadDetails(blocked)
            ));
        }

        int gcThreads = threads.Count(t => t.IsGcThread);
        if (gcThreads > 2)
        {
            findings.Add(new Finding(
                "GcPressureHigh",
                Severity.Warning,
                $"{gcThreads} GC threads active — application under heavy memory pressure",
                "Server GC spawns one GC thread per logical core. Seeing many active GC threads " +
                "means the application is allocating and collecting continuously, spending CPU on " +
                "garbage collection instead of useful work (GC tax).",
                Evidence: $"{gcThreads} threads flagged IsGc=true",
                Recommendation:
                    "1. Profile allocation hot-paths with dotMemory or PerfView. " +
                    "2. Pool frequently-allocated objects with ObjectPool<T> or ArrayPool<T>. " +
                    "3. Avoid allocating in tight loops (LINQ chains, string concatenation, boxing). " +
                    "4. Check LOH usage — objects >85 KB go to LOH and are expensive to collect."
            ));
        }

        // ── Heap findings ────────────────────────────────────────────────────

        var topType = heapTypes.FirstOrDefault();
        if (topType != null && topType.TotalSizeBytes > 200 * 1024 * 1024)
        {
            var relatedLeak = leaks.FirstOrDefault(l => l.TypeName == topType.TypeName);
            findings.Add(new Finding(
                "HighLiveObjectCount",
                Severity.Warning,
                $"'{topType.TypeName}' dominates the heap: {topType.TotalSizeBytes / (1024 * 1024):N0} MB",
                "One type holds an outsized share of the managed heap. This is a strong indicator of " +
                "an unbounded cache, a static collection that never shrinks, event handler leak " +
                "(subscribers not unsubscribed), or a finalizer queue backup.",
                Evidence: $"{topType.InstanceCount:N0} instances × avg {(topType.InstanceCount > 0 ? topType.TotalSizeBytes / topType.InstanceCount : 0):N0} B = {topType.TotalSizeBytes / (1024 * 1024):N0} MB",
                Recommendation:
                    "1. Search the codebase for static List<T>/Dictionary<T> or event += that never calls event -=. " +
                    "2. Use a memory profiler (dotMemory, VS Diagnostic Tools) to capture a GC root path to a live instance. " +
                    "3. If this is a cache, add a size cap and eviction policy (e.g. MemoryCache with SizeLimit).",
                TechnicalDetails: BuildLeakDetails(topType, relatedLeak, heapTypes)
            ));
        }

        if (gc.LohBytes > LohSizeThreshold)
        {
            findings.Add(new Finding(
                "LargeObjectHeapGrowth",
                Severity.Warning,
                $"Large Object Heap: {gc.LohBytes / (1024 * 1024):N0} MB ({gc.LohFragmentationPercent:F1}% fragmented)",
                "Objects ≥ 85 KB are allocated on the LOH. The LOH is not compacted by default, " +
                "so free gaps left by collected objects accumulate as fragmentation. " +
                "Fragmented LOH memory is committed to the process but cannot be used for new allocations, " +
                "causing effective memory waste and eventual OutOfMemoryException.",
                Evidence: $"LOH committed: {gc.LohBytes / (1024 * 1024):N0} MB; fragmentation: {gc.LohFragmentationPercent:F1}%",
                Recommendation:
                    "1. Use ArrayPool<byte>.Shared for large byte buffers (HttpContent, stream reads). " +
                    "2. Enable one-shot LOH compaction: GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce; GC.Collect(); " +
                    "3. Check for repeated allocation of large arrays in tight loops. " +
                    "4. Consider enabling Server GC if not already (better LOH management).",
                TechnicalDetails: BuildLohDetails(gc, heapTypes)
            ));
        }

        // ── GC findings ──────────────────────────────────────────────────────

        if (gc.LohFragmentationPercent > LohFragThreshold)
        {
            findings.Add(new Finding(
                "LohFragmentationHigh",
                Severity.Warning,
                $"LOH fragmentation: {gc.LohFragmentationPercent:F1}% of committed LOH is dead space",
                "More than a third of the committed LOH cannot be reused. .NET allocates large objects " +
                "into the first fitting free gap; when gaps are smaller than the next allocation, " +
                "memory usage climbs even though objects are being collected.",
                Evidence: $"{gc.LohFragmentationPercent:F1}% fragmentation across {gc.LohBytes / (1024 * 1024):N0} MB LOH",
                Recommendation:
                    "Set GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce " +
                    "before calling GC.Collect(2, GCCollectionMode.Forced, true, true) during a low-traffic window. " +
                    "Long-term: stop allocating transient large arrays; use ArrayPool<T>."
            ));
        }

        var gen2Segs = gc.Segments.Where(s => s.Kind == "Gen2").ToList();
        double avgGen2Fill = gen2Segs.Count > 0 ? gen2Segs.Average(s => s.FillRatio) : 0;
        if (avgGen2Fill > Gen2FillThreshold)
        {
            findings.Add(new Finding(
                "Gen2Saturated",
                Severity.Warning,
                $"Gen2 heap is {avgGen2Fill * 100:F0}% full — full GC pressure",
                "A saturated Gen2 forces the GC to perform full Stop-The-World collections frequently. " +
                "During a full GC, all application threads are suspended. With high Gen2 fill, " +
                "these pauses can exceed 100–500 ms and are visible as request timeouts.",
                Evidence: $"Avg fill {avgGen2Fill * 100:F0}% across {gen2Segs.Count} Gen2 segment(s); " +
                          $"Gen2 size: {gc.Gen2Bytes / (1024 * 1024):N0} MB",
                Recommendation:
                    "1. Find what is promoting objects to Gen2 (long-lived caches, static collections). " +
                    "2. Shorten object lifetimes so objects are collected in Gen0/1 (cheaper). " +
                    "3. Increase machine RAM / process memory limit if the working set is legitimately large."
            ));
        }

        // ── String findings ──────────────────────────────────────────────────

        long totalStringWaste = strings.Sum(s => s.TotalWastedBytes);
        if (totalStringWaste > DuplicateStringThreshold)
        {
            var top = strings.First();
            findings.Add(new Finding(
                "DuplicateStringWaste",
                Severity.Warning,
                $"Duplicate strings: {totalStringWaste / (1024 * 1024):N0} MB wasted across {strings.Count} groups",
                "The same string values are stored as thousands of separate heap objects. " +
                "Interning or deduplicating them would immediately reclaim this memory.",
                Evidence: $"Worst offender: \"{top.Value}\" — {top.InstanceCount:N0} copies = {top.TotalWastedBytes / 1024:N0} KB wasted",
                Recommendation:
                    "1. For constant strings (config keys, enum names), use string.Intern(). " +
                    "2. For dynamic strings loaded from DB/config, deduplicate via a static Dictionary<string,string>. " +
                    "3. Consider switching to string symbols (enum, const, ReadOnlySpan) where the string is a fixed identifier.",
                TechnicalDetails: BuildStringDetails(strings)
            ));
        }

        // ── Leak findings ────────────────────────────────────────────────────

        foreach (var leak in leaks.Take(5))
        {
            findings.Add(new Finding(
                "SuspectedMemoryLeak",
                Severity.Warning,
                $"Suspected leak: {leak.TypeName} — {leak.TotalSizeBytes / (1024 * 1024):N0} MB in {leak.InstanceCount:N0} instances",
                "This type occupies significant heap space and roots were found keeping it alive. " +
                "Memory that cannot be collected accumulates over time until the process runs out.",
                Evidence: leak.RootPaths.Count > 0
                    ? $"GC roots found: {string.Join(" | ", leak.RootPaths.Take(2))}"
                    : "No direct root found in sample — may be retained transitively via a large object graph.",
                Recommendation:
                    "1. Capture two heap snapshots minutes apart and compare growth with the 'diff' command. " +
                    "2. Use dotMemory / VS Memory Profiler to get the full retention path. " +
                    "3. Look for event subscriptions (+=) with no corresponding unsubscription (-=). " +
                    "4. Check for static fields holding collections of this type.",
                TechnicalDetails: leak.RootPaths.Count > 0
                    ? new[] { "GC root paths:" }.Concat(leak.RootPaths.Select(r => "  " + r)).ToList()
                    : null
            ));
        }

        // ── Health score ─────────────────────────────────────────────────────

        int score = 100;
        foreach (var f in findings)
            score -= f.Severity switch { Severity.Critical => 30, Severity.Warning => 10, _ => 2 };
        score = Math.Max(0, score);

        findings.Sort((a, b) =>
        {
            int c = b.Severity.CompareTo(a.Severity);
            return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });

        return (findings, score);
    }

    // ── Technical detail builders ─────────────────────────────────────────────

    private static IReadOnlyList<string> BuildThreadGroupDetails(
        IReadOnlyList<ThreadInfo> threads,
        IReadOnlyList<ThreadGroup> groups)
    {
        var lines = new List<string>();
        lines.Add($"THREAD PATTERN ANALYSIS  ({threads.Count:N0} managed threads total)");
        lines.Add(new string('─', 60));

        int patternNum = 0;
        foreach (var g in groups.Take(10))
        {
            patternNum++;
            string waitDesc = g.WaitReason != null ? $"  ⚠ {g.WaitReason}" : "";
            lines.Add($"");
            lines.Add($"Pattern {patternNum}: {g.ThreadCount:N0} threads ({g.Percentage:F1}%){waitDesc}");
            lines.Add($"Sample OS thread IDs: {string.Join(", ", g.SampleOsThreadIds)}");
            lines.Add("Stack (representative):");
            foreach (var frame in g.RepresentativeStack)
                lines.Add($"    {frame}");
        }

        if (groups.Count > 10)
            lines.Add($"... ({groups.Count - 10} more patterns not shown)");

        return lines;
    }

    private static IReadOnlyList<string> BuildBlockedThreadDetails(IReadOnlyList<ThreadInfo> blocked)
    {
        var lines = new List<string>();
        lines.Add($"BLOCKED THREAD DETAILS  ({blocked.Count} threads)");
        lines.Add(new string('─', 60));

        foreach (var t in blocked)
        {
            lines.Add("");
            lines.Add($"Thread OS:{t.OsThreadId}  Managed:{t.ManagedThreadId}  {t.State}");
            if (t.BlockingReason != null) lines.Add($"  Blocking reason: {t.BlockingReason}");
            if (t.CurrentException != null) lines.Add($"  Current exception: {t.CurrentException}");
            lines.Add("  Stack:");
            foreach (var frame in t.StackFrames)
                lines.Add($"    {frame}");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildLeakDetails(
        HeapTypeStats top,
        LeakSuspect? leak,
        IReadOnlyList<HeapTypeStats> allTypes)
    {
        var lines = new List<string>();
        lines.Add($"TOP HEAP TYPE BREAKDOWN");
        lines.Add(new string('─', 60));
        lines.Add($"  {top.TypeName}");
        lines.Add($"  Instances : {top.InstanceCount:N0}");
        lines.Add($"  Total size: {top.TotalSizeBytes / (1024 * 1024):N0} MB");
        lines.Add($"  Avg size  : {(top.InstanceCount > 0 ? top.TotalSizeBytes / top.InstanceCount : 0):N0} B/instance");
        lines.Add($"  Generation: {top.Generation}");

        if (leak?.RootPaths.Count > 0)
        {
            lines.Add("");
            lines.Add("GC root paths (sample):");
            foreach (var rp in leak.RootPaths)
                lines.Add($"  {rp}");
        }

        lines.Add("");
        lines.Add("Next 9 largest types:");
        foreach (var t in allTypes.Skip(1).Take(9))
            lines.Add($"  {t.TotalSizeBytes / (1024 * 1024),6:N0} MB  {t.InstanceCount,10:N0} instances  {t.TypeName}");

        return lines;
    }

    private static IReadOnlyList<string> BuildLohDetails(GcStats gc, IReadOnlyList<HeapTypeStats> types)
    {
        var lines = new List<string>();
        lines.Add("LARGE OBJECT HEAP SEGMENTS");
        lines.Add(new string('─', 60));
        foreach (var seg in gc.Segments.Where(s => s.Kind == "LOH"))
            lines.Add($"  Committed: {seg.CommittedBytes / (1024 * 1024):N0} MB   Fill: {seg.FillRatio * 100:F1}%");

        // Largest types likely on LOH (heuristic: avg instance size > 80 KB)
        var lohCandidates = types
            .Where(t => t.InstanceCount > 0 && t.TotalSizeBytes / t.InstanceCount > 80 * 1024)
            .Take(8)
            .ToList();

        if (lohCandidates.Count > 0)
        {
            lines.Add("");
            lines.Add("Types likely on LOH (avg instance >80 KB):");
            foreach (var t in lohCandidates)
                lines.Add($"  {t.TotalSizeBytes / (1024 * 1024),6:N0} MB  avg {t.TotalSizeBytes / t.InstanceCount / 1024:N0} KB/inst  {t.TypeName}");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildStringDetails(IReadOnlyList<StringDuplication> strings)
    {
        var lines = new List<string>
        {
            "TOP DUPLICATE STRING GROUPS",
            new string('─', 60)
        };
        foreach (var s in strings.Take(15))
        {
            lines.Add($"  {s.TotalWastedBytes / 1024,8:N0} KB wasted   {s.InstanceCount,8:N0} copies");
            var display = s.Value.Length > 80 ? s.Value[..80] + "…" : s.Value;
            lines.Add($"  Value: \"{display}\"");
            lines.Add("");
        }
        return lines;
    }
}
