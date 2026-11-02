using MemDumpAnalyzer.Core.Analysis;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Core.Heuristics;

/// <summary>
/// Turns analysis data into findings. Rules of thumb follow Nikolay Mitikov's
/// https://makingitfaster.tips/ where it gives one; the remaining thresholds are practical
/// experience. Each finding's explanation says which applies.
/// </summary>
public static class FindingEngine
{
    private const long LohSizeThreshold = 85 * 1024 * 1024;
    private const double LohFragThreshold = 30.0;
    private const long DuplicateStringThreshold = 50 * 1024 * 1024;

    // makingitfaster.tips: "Over 100 threads & low CPU likely flags a bottleneck in dependent resource"
    private const int HighThreadCountThreshold = 100;
    private const int CriticalThreadCountThreshold = 200;
    private const int LowCpuThreshold = 50;
    // makingitfaster.tips: "81 % CPU flags garbage collection is running"
    private const int HighCpuThreshold = 80;
    private const int GcMagicCpu = 81;

    private const int LockContentionWarningThreshold = 10;
    private const int LockContentionCriticalThreshold = 50;

    private const string SecondDumpAdvice =
        "A single dump is one moment in time: capture 2–3 dumps about a minute apart " +
        "(e.g. procdump64 -ma -n 3 -s 60 <pid>) and compare them.";

    public static (IReadOnlyList<Finding> Findings, int HealthScore) Evaluate(
        IReadOnlyList<ThreadInfo> threads,
        IReadOnlyList<ThreadGroup> threadGroups,
        IReadOnlyList<HeapTypeStats> heapTypes,
        GcStats gc,
        IReadOnlyList<StringDuplication> strings,
        IReadOnlyList<LeakSuspect> leaks,
        CancellationToken cancellationToken = default)
        => Evaluate(threads, threadGroups, heapTypes, gc, strings, leaks,
            threadPool: null, heapWalkComplete: true, cancellationToken);

    public static (IReadOnlyList<Finding> Findings, int HealthScore) Evaluate(
        IReadOnlyList<ThreadInfo> threads,
        IReadOnlyList<ThreadGroup> threadGroups,
        IReadOnlyList<HeapTypeStats> heapTypes,
        GcStats gc,
        IReadOnlyList<StringDuplication> strings,
        IReadOnlyList<LeakSuspect> leaks,
        ThreadPoolStats? threadPool,
        bool heapWalkComplete,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<Finding>();
        string partialNote = heapWalkComplete ? "" : " (partial heap walk — dump captured mid-GC)";

        // ── Dump quality ─────────────────────────────────────────────────────

        if (!heapWalkComplete)
        {
            findings.Add(new Finding(
                "GcInProgressAtCapture",
                Severity.Info,   // about the report's data quality; HighCpuAtCapture carries the health signal
                "The dump was captured while a garbage collection was running — heap numbers are incomplete",
                "The runtime reports the heap as not walkable, which happens when the snapshot catches a GC " +
                "in progress: objects may be half-moved, so the heap walk stops early. Heap types, strings and " +
                "fragmentation in this report come from the part that could be read. On makingitfaster.tips, " +
                "almost every snapshot attempt catching GC running was the sign of a process that collects " +
                "nearly all the time because it allocates heavily.",
                Evidence: "ClrMD CanWalkHeap = false" +
                          (threadPool != null ? $"; CPU at capture {threadPool.CpuUtilization}%" : ""),
                Recommendation:
                    "1. Capture another dump for complete heap statistics. " +
                    "2. If repeated captures keep catching GC, the process is under heavy allocation pressure: " +
                    "collect a PerfView / ETW allocation trace to find what allocates. " +
                    "3. " + SecondDumpAdvice
            ));
        }

        // ── Thread findings ──────────────────────────────────────────────────

        var aliveUser = threads.Where(t => t.IsAlive && !t.IsGcThread && !t.IsFinalizer).ToList();
        int deadCount = threads.Count(t => !t.IsAlive);

        if (threadPool != null &&
            threadPool.ActiveWorkerThreads > 0 &&
            threadPool.IdleWorkerThreads == 0 &&
            threadPool.ActiveWorkerThreads >= threadPool.MinThreads)
        {
            findings.Add(new Finding(
                "ThreadPoolStarvation",
                Severity.Critical,
                $"Thread pool starved: {threadPool.ActiveWorkerThreads:N0} busy workers, none idle (min {threadPool.MinThreads:N0})",
                "Every worker thread is busy and the pool is at or above its minimum size. Above the minimum the " +
                "CLR adds new threads slowly (roughly one or two per second), so queued requests wait — typically " +
                "seen as timeouts. The cause is almost always threads blocked on something: see the wait breakdown.",
                Evidence: $"Active {threadPool.ActiveWorkerThreads:N0}, idle {threadPool.IdleWorkerThreads:N0}, " +
                          $"min {threadPool.MinThreads:N0}, max {threadPool.MaxThreads:N0}, CPU {threadPool.CpuUtilization}%",
                Recommendation:
                    "1. Fix what the threads wait on (largest groups below): slow SQL/Redis/HTTP calls, contended locks, .Result/.Wait on tasks. " +
                    "2. Replace .Result / .Wait / .GetAwaiter().GetResult() with await. " +
                    "3. Raising minWorkerThreads is short-term relief only.",
                TechnicalDetails: BuildThreadGroupDetails(aliveUser, threadGroups, deadCount)
            ));
        }

        if (aliveUser.Count > HighThreadCountThreshold)
        {
            int? cpu = threadPool?.CpuUtilization;
            string cpuReading = cpu switch
            {
                null => "CPU usage at capture is unknown, so whether the threads wait on a dependency or compete for CPU can't be told from CPU alone.",
                < LowCpuThreshold => $"CPU was only {cpu}% at capture: over 100 threads with low CPU usually means they are waiting on a dependency such as SQL, Redis or web API calls (makingitfaster.tips).",
                GcMagicCpu => $"CPU was {cpu}% at capture — the value makingitfaster.tips calls a magic number for garbage collection running.",
                >= HighCpuThreshold => $"CPU was {cpu}% at capture: the threads are competing for CPU.",
                _ => $"CPU was {cpu}% at capture."
            };

            findings.Add(new Finding(
                "HighThreadCount",
                aliveUser.Count > CriticalThreadCountThreshold ? Severity.Critical : Severity.Warning,
                $"{aliveUser.Count:N0} live threads" + (deadCount > 0 ? $" (plus {deadCount:N0} dead, not counted)" : ""),
                "Many threads are alive at once. Each one reserves stack memory and most of them are usually " +
                "waiting rather than working. " + cpuReading,
                Evidence: "Wait breakdown: " + BuildWaitBreakdown(aliveUser),
                Recommendation:
                    "1. Look at the largest thread groups below — they show what the threads wait on. " +
                    "2. Replace blocking calls (.Result / .Wait, synchronous SQL/HTTP) with async/await. " +
                    "3. Check the speed of the dependencies the threads wait on (SQL, Redis, Solr, web APIs). " +
                    "4. " + SecondDumpAdvice,
                TechnicalDetails: BuildThreadGroupDetails(aliveUser, threadGroups, deadCount)
            ));
        }

        if (threadPool != null && threadPool.CpuUtilization >= HighCpuThreshold)
        {
            findings.Add(new Finding(
                "HighCpuAtCapture",
                Severity.Warning,
                $"CPU was {threadPool.CpuUtilization}% when the dump was captured",
                "The thread pool's CPU reading is system-wide, so other processes can contribute. " +
                (threadPool.CpuUtilization == GcMagicCpu
                    ? "81% is the value makingitfaster.tips calls a magic number that likely indicates garbage collection is running. "
                    : "") +
                "A single dump can't show which threads use the CPU — that needs per-thread CPU times compared between two dumps.",
                Evidence: $"Thread pool CPU utilization {threadPool.CpuUtilization}%",
                Recommendation:
                    "Capture dumps on high CPU and compare per-thread CPU time between them (WinDbg !runaway, after " +
                    "excluding GC and finalizer threads), e.g. procdump64 -c 80 -s 2 -ma -n 3 w3wp.exe. " +
                    "If GC is the consumer, reduce allocations (PerfView allocation trace)."
            ));
        }

        var lockWaiters = threads.Where(t => t.IsAlive && t.WaitKind == WaitKind.LockContention).ToList();
        if (lockWaiters.Count >= LockContentionWarningThreshold)
        {
            var sites = lockWaiters
                .GroupBy(t => ThreadAnalyzer.GetLockSite(t.StackFrames) ?? "(unknown lock site)")
                .OrderByDescending(g => g.Count())
                .ToList();
            var top = sites[0];

            findings.Add(new Finding(
                "LockContention",
                lockWaiters.Count >= LockContentionCriticalThreshold ? Severity.Critical : Severity.Warning,
                $"{lockWaiters.Count:N0} threads wait to enter a lock or semaphore — {top.Count():N0} of them at {StripAssembly(top.Key)}",
                "These threads are blocked in Monitor.Enter (a C# lock statement) or a synchronous SemaphoreSlim.Wait, " +
                "waiting for another thread to release it, so the work behind it is serialised. A semaphore that guards " +
                "a cache refresh makes every request wait while one thread reloads the value. One dump cannot tell heavy " +
                "contention from a deadlock: if a second dump a minute later shows the same threads waiting at " +
                "the same place, nothing is moving.",
                Evidence: string.Join("; ", sites.Take(3).Select(g => $"{g.Count():N0} × {StripAssembly(g.Key)}")),
                Recommendation:
                    "1. Look at the code at the top lock site: keep the locked section short and never do I/O inside it. " +
                    "2. Lock per key instead of one global lock (e.g. a lock set keyed by the cache key), or use " +
                    "ConcurrentDictionary / Interlocked / lock-free reads. " +
                    "3. Use SemaphoreSlim.WaitAsync in async code. " +
                    "4. " + SecondDumpAdvice,
                TechnicalDetails: BuildLockContentionDetails(sites, threads)
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
                $"'{topType.TypeName}' dominates the heap: {topType.TotalSizeBytes / (1024 * 1024):N0} MB{partialNote}",
                "One type holds an outsized share of the managed heap. This often points to an unbounded cache, " +
                "a static collection that never shrinks, or event handlers that are never unsubscribed — but a " +
                "single dump can't tell a leak from a large, stable working set.",
                Evidence: $"{topType.InstanceCount:N0} instances × avg {(topType.InstanceCount > 0 ? topType.TotalSizeBytes / topType.InstanceCount : 0):N0} B = {topType.TotalSizeBytes / (1024 * 1024):N0} MB",
                Recommendation:
                    "1. Capture a second dump a minute or so later and run the 'diff' command: types that keep growing are the leak candidates. " +
                    "2. Find what keeps instances alive (WinDbg !gcroot, dotMemory or the VS memory profiler). " +
                    "3. Search for static List<T>/Dictionary<T>, and event += without a matching -=. " +
                    "4. If this is a cache, give it a size cap and an eviction policy.",
                TechnicalDetails: BuildLeakDetails(topType, relatedLeak, heapTypes)
            ));
        }

        if (gc.LohBytes > LohSizeThreshold)
        {
            findings.Add(new Finding(
                "LargeObjectHeapGrowth",
                Severity.Warning,
                $"Large Object Heap: {gc.LohBytes / (1024 * 1024):N0} MB ({gc.LohFragmentationPercent:F1}% free space){partialNote}",
                "Objects of 85,000 bytes or more are allocated on the LOH, which is collected only with gen2 and " +
                "not compacted by default. A large LOH usually means big arrays or strings are allocated repeatedly " +
                "(buffers, serialised payloads, large collections).",
                Evidence: $"LOH committed: {gc.LohBytes / (1024 * 1024):N0} MB; free objects: {gc.LohFreeBytes / (1024 * 1024):N0} MB ({gc.LohFragmentationPercent:F1}%)",
                Recommendation:
                    "1. Find the large objects (Top Memory Consumers; types with average size above 85 KB are listed below). " +
                    "2. Reuse large buffers with ArrayPool<T>.Shared instead of allocating new ones. " +
                    "3. Stream large payloads instead of building them as one string or array. " +
                    "Forcing LOH compaction is a performance anti-pattern (makingitfaster.tips) — remove the allocations instead.",
                TechnicalDetails: BuildLohDetails(gc, heapTypes)
            ));
        }

        // ── GC findings ──────────────────────────────────────────────────────

        if (gc.LohFragmentationPercent > LohFragThreshold)
        {
            findings.Add(new Finding(
                "LohFragmentationHigh",
                Severity.Warning,
                $"LOH fragmentation: {gc.LohFragmentationPercent:F1}% of the LOH is free space{partialNote}",
                "Free gaps between large objects can only be reused by allocations that fit into them. When the " +
                "gaps are smaller than the next allocation, the LOH keeps growing even though objects are being " +
                "collected. Free ranges show that large objects are being created and dropped (makingitfaster.tips).",
                Evidence: $"{gc.LohFreeBytes / (1024 * 1024):N0} MB free out of {gc.LohBytes / (1024 * 1024):N0} MB committed LOH",
                Recommendation:
                    "Stop allocating transient large arrays and strings: pool buffers with ArrayPool<T>, stream large " +
                    "payloads, and cap collection sizes. Forcing LOH compaction (GCSettings.LargeObjectHeapCompactionMode) " +
                    "is a performance anti-pattern (makingitfaster.tips) — treat it as a last resort, not a fix."
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
                $"Duplicate strings: {totalStringWaste / (1024 * 1024):N0} MB wasted across {strings.Count} groups{partialNote}",
                "The same string values are stored as many separate heap objects. Deduplicating the most repeated " +
                "values reclaims this memory (makingitfaster.tips found nearly a million copies of \"true\"/\"false\" in one Sitecore heap).",
                Evidence: $"Worst offender: \"{OneLine(top.Value)}\" — {top.InstanceCount:N0} copies = {top.TotalWastedBytes / 1024:N0} KB wasted",
                Recommendation:
                    "1. Find where the top values are created (often parsing: XML, JSON, database rows, field values). " +
                    "2. Deduplicate selectively: only fields with few unique values compared to their instance count, and skip rare or big values. " +
                    "3. Use a dedicated lookup (Dictionary<string,string> or a string pool) rather than string.Intern, whose entries are never released.",
                TechnicalDetails: BuildStringDetails(strings)
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

    private static string BuildWaitBreakdown(IReadOnlyList<ThreadInfo> threads) =>
        string.Join(", ", threads
            .GroupBy(t => t.WaitKind)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count():N0} {DescribeWaitKind(g.Key)}"));

    private static string DescribeWaitKind(WaitKind kind) => kind switch
    {
        WaitKind.None => "running or no managed stack",
        WaitKind.LockContention => "waiting to enter a lock or semaphore",
        WaitKind.SyncOverAsync => "blocking on a Task (.Result/.Wait)",
        WaitKind.Dependency => "waiting on a dependency (SQL/Redis/HTTP/Solr/network)",
        WaitKind.FileIo => "waiting on file I/O",
        WaitKind.Signal => "waiting on an event/semaphore",
        WaitKind.Sleep => "sleeping",
        WaitKind.IdleWorker => "idle workers",
        _ => kind.ToString()
    };

    private static IReadOnlyList<string> BuildThreadGroupDetails(
        IReadOnlyList<ThreadInfo> aliveThreads,
        IReadOnlyList<ThreadGroup> groups,
        int deadCount)
    {
        var lines = new List<string>();
        lines.Add($"THREAD PATTERN ANALYSIS  ({aliveThreads.Count:N0} live threads; {deadCount:N0} dead threads excluded)");
        lines.Add(new string('─', 60));
        lines.Add("By wait kind: " + BuildWaitBreakdown(aliveThreads));

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

    private static IReadOnlyList<string> BuildLockContentionDetails(
        IReadOnlyList<IGrouping<string, ThreadInfo>> sites,
        IReadOnlyList<ThreadInfo> allThreads)
    {
        var lines = new List<string>();
        lines.Add($"LOCK CONTENTION BY SITE  ({sites.Sum(s => s.Count()):N0} waiting threads, {sites.Count} site(s))");
        lines.Add(new string('─', 60));

        foreach (var site in sites.Take(10))
        {
            lines.Add("");
            lines.Add($"{site.Count():N0} threads waiting at: {site.Key}");
            lines.Add($"Sample OS thread IDs: {string.Join(", ", site.Take(10).Select(t => t.OsThreadId))}");
            lines.Add("Stack (representative):");
            foreach (var frame in site.First().StackFrames)
                lines.Add($"    {frame}");
        }
        if (sites.Count > 10)
            lines.Add($"... ({sites.Count - 10} more lock sites not shown)");

        // Threads that hold locks are the candidates for blocking the waiters
        var holders = allThreads.Where(t => t.IsAlive && t.State.Contains("Locks:", StringComparison.Ordinal)).ToList();
        if (holders.Count > 0)
        {
            lines.Add("");
            lines.Add($"THREADS HOLDING LOCKS  ({holders.Count:N0})");
            lines.Add(new string('─', 60));
            foreach (var t in holders.Take(20))
            {
                lines.Add("");
                lines.Add($"Thread OS:{t.OsThreadId}  Managed:{t.ManagedThreadId}  {t.State}" +
                          (t.BlockingReason != null ? $"  — {t.BlockingReason}" : "  — running"));
                foreach (var frame in t.StackFrames.Take(25))
                    lines.Add($"    {frame}");
            }
        }

        return lines;
    }

    /// <summary>Collapses runs of whitespace (including line breaks) so a value fits on one line.</summary>
    private static string OneLine(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string StripAssembly(string frame)
    {
        int i = frame.LastIndexOf("  [", StringComparison.Ordinal);
        return i > 0 ? frame[..i] : frame;
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
            lines.Add($"  Committed: {seg.CommittedBytes / (1024 * 1024):N0} MB   Free: {seg.FreeBytes / (1024 * 1024):N0} MB   Fill: {seg.FillRatio * 100:F1}%");

        // Largest types likely on LOH (heuristic: avg instance size ≥ 85,000 bytes)
        var lohCandidates = types
            .Where(t => t.InstanceCount > 0 && t.TotalSizeBytes / t.InstanceCount >= 85_000)
            .Take(8)
            .ToList();

        if (lohCandidates.Count > 0)
        {
            lines.Add("");
            lines.Add("Types likely on LOH (avg instance ≥ 85,000 bytes):");
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
            var oneLine = OneLine(s.Value);
            var display = oneLine.Length > 80 ? oneLine[..80] + "…" : oneLine;
            lines.Add($"  Value: \"{display}\"");
            lines.Add("");
        }
        return lines;
    }
}
