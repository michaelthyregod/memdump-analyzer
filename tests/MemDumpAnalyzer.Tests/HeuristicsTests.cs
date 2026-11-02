using MemDumpAnalyzer.Core;
using MemDumpAnalyzer.Core.Analysis;
using MemDumpAnalyzer.Core.Heuristics;
using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting;

namespace MemDumpAnalyzer.Tests;

/// <summary>
/// Wait classification, thread/lock/GC rules and report safety — the behaviour derived from
/// makingitfaster.tips and the fixes found while testing against a real mid-GC dump.
/// </summary>
public class HeuristicsTests
{
    private static GcStats EmptyGc() => new(0, 0, 0, 0, 0, 0, 0.0, []);

    private static readonly string[] LockStack =
    [
        "[GCFrame]",
        "[GCFrame]",
        "System.Threading.Monitor.Enter(System.Object)  [mscorlib]",
        "Sitecore.Data.ItemInformation.get_ChildIDs()  [Sitecore.Kernel]",
        "Sitecore.Data.DataSource.GetChildIDs(Sitecore.Data.ID)  [Sitecore.Kernel]",
    ];

    private static ThreadInfo Thread(uint id, IReadOnlyList<string> frames, bool alive = true)
    {
        var (kind, description) = ThreadAnalyzer.ClassifyWait(frames);
        return new ThreadInfo(id, (int)id, alive ? "Alive" : "Dead", null, frames, false, false,
            kind is WaitKind.None or WaitKind.IdleWorker ? null : $"Waiting on {description}",
            IsAlive: alive, WaitKind: kind);
    }

    private static (IReadOnlyList<Finding> Findings, int Score) Eval(
        IReadOnlyList<ThreadInfo>? threads = null,
        GcStats? gc = null,
        ThreadPoolStats? pool = null,
        bool heapWalkComplete = true,
        IReadOnlyList<HeapTypeStats>? heap = null,
        IReadOnlyList<LeakSuspect>? leaks = null) =>
        FindingEngine.Evaluate(threads ?? [], [], heap ?? [], gc ?? EmptyGc(), [], leaks ?? [],
            pool, heapWalkComplete, TestContext.Current.CancellationToken);

    // ── Wait classification ──────────────────────────────────────────────────

    [Fact]
    public void ClassifyWait_MonitorEnter_IsLockContentionWithCallerAsSite()
    {
        var (kind, description) = ThreadAnalyzer.ClassifyWait(LockStack);

        Assert.Equal(WaitKind.LockContention, kind);
        Assert.Contains("Sitecore.Data.ItemInformation.get_ChildIDs()", description);
        Assert.Equal("Sitecore.Data.ItemInformation.get_ChildIDs()  [Sitecore.Kernel]", ThreadAnalyzer.GetLockSite(LockStack));
    }

    [Fact]
    public void ClassifyWait_TaskResult_WinsOverTheMonitorWaitThatImplementsIt()
    {
        string[] frames =
        [
            "System.Threading.Monitor.ObjWait(Boolean, Int32, System.Object)  [mscorlib]",
            "System.Threading.Monitor.Wait(System.Object, Int32, Boolean)  [mscorlib]",
            "System.Threading.ManualResetEventSlim.Wait(Int32, System.Threading.CancellationToken)  [mscorlib]",
            "System.Threading.Tasks.Task.SpinThenBlockingWait(Int32, System.Threading.CancellationToken)  [mscorlib]",
            "System.Threading.Tasks.Task.InternalWait(Int32, System.Threading.CancellationToken)  [mscorlib]",
            "System.Threading.Tasks.Task`1[[System.__Canon, mscorlib]].GetResultCore(Boolean)  [mscorlib]",
            "MyApp.Client.Fetch()  [MyApp]",
        ];

        Assert.Equal(WaitKind.SyncOverAsync, ThreadAnalyzer.ClassifyWait(frames).Kind);
    }

    [Fact]
    public void ClassifyWait_SynchronousSemaphoreSlimWait_IsContentionAtTheCaller()
    {
        // Seen in production: hundreds of requests queued on a semaphore guarding a cache reload
        string[] frames =
        [
            "[GCFrame]",
            "System.Threading.Monitor.ObjWait(Boolean, Int32, System.Object)  [mscorlib]",
            "System.Threading.SemaphoreSlim.WaitUntilCountOrTimeout(Int32, UInt32, System.Threading.CancellationToken)  [mscorlib]",
            "System.Threading.SemaphoreSlim.Wait(Int32, System.Threading.CancellationToken)  [mscorlib]",
            "System.Threading.SemaphoreSlim.Wait(System.TimeSpan)  [mscorlib]",
            "MyApp.Caching.CacheExtensions.Get(System.String)  [MyApp]",
            "MyApp.Api.Countries()  [MyApp]",
        ];

        var (kind, description) = ThreadAnalyzer.ClassifyWait(frames);

        Assert.Equal(WaitKind.LockContention, kind);
        Assert.Equal("MyApp.Caching.CacheExtensions.Get(System.String)  [MyApp]", ThreadAnalyzer.GetLockSite(frames));
        Assert.Contains("SemaphoreSlim", description);
    }

    [Fact]
    public void ClassifyWait_RedisSyncCall_IsDependency()
    {
        string[] frames =
        [
            "[GCFrame]",
            "System.Threading.Monitor.ObjWait(Boolean, Int32, System.Object)  [mscorlib]",
            "StackExchange.Redis.ConnectionMultiplexer.ExecuteSyncImpl(StackExchange.Redis.Message)  [StackExchange.Redis]",
            "MyApp.Session.Load()  [MyApp]",
        ];

        var (kind, description) = ThreadAnalyzer.ClassifyWait(frames);

        Assert.Equal(WaitKind.Dependency, kind);
        Assert.Contains("Redis", description);
    }

    [Fact]
    public void ClassifyWait_IdleSitecoreWorker_IsIdleAndNotBlocked()
    {
        string[] frames =
        [
            "[GCFrame]",
            "System.Threading.Monitor.ObjWait(Boolean, Int32, System.Object)  [mscorlib]",
            "Sitecore.Threading.Semaphore.P()  [Sitecore.Kernel]",
            "Sitecore.Threading.ManagedThreadPool.ProcessQueuedItems()  [Sitecore.Kernel]",
        ];

        var t = Thread(1, frames);

        Assert.Equal(WaitKind.IdleWorker, t.WaitKind);
        Assert.Null(t.BlockingReason);
    }

    [Fact]
    public void ClassifyWait_RunningThread_IsNotWaitingEvenWithPipelineAndIoFramesBelow()
    {
        // Busy request thread: top frames are application code; ASP.NET pipeline and a StreamReader are deeper
        string[] frames =
        [
            "MyApp.Pricing.Calculate(System.Decimal)  [MyApp]",
            "MyApp.Pricing.Render()  [MyApp]",
            "System.IO.StreamReader.ReadToEnd()  [mscorlib]",
            "System.Web.HttpApplication.ExecuteStep(IExecutionStep, Boolean ByRef)  [System.Web]",
        ];

        Assert.Equal(WaitKind.None, ThreadAnalyzer.ClassifyWait(frames).Kind);
    }

    [Fact]
    public void ClassifyWait_MonitorEnterDeepInStack_IsNotContention()
    {
        // A primitive far below the top is a caller frame, not what the thread is doing now
        var frames = Enumerable.Range(0, 12).Select(i => $"MyApp.Work.Step{i}()  [MyApp]")
            .Append("System.Threading.Monitor.Enter(System.Object)  [mscorlib]")
            .ToList();

        Assert.Equal(WaitKind.None, ThreadAnalyzer.ClassifyWait(frames).Kind);
    }

    // ── Thread findings ──────────────────────────────────────────────────────

    [Fact]
    public void LockContention_GroupsWaitersBySite()
    {
        var threads = Enumerable.Range(1, 12).Select(i => Thread((uint)i, LockStack)).ToList();

        var (findings, _) = Eval(threads);

        var f = Assert.Single(findings, f => f.Id == "LockContention");
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("12 threads", f.Summary);
        Assert.Contains("Sitecore.Data.ItemInformation.get_ChildIDs()", f.Summary);
        Assert.DoesNotContain("deadlock", f.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(f.TechnicalDetails!, l => l.Contains("12 threads waiting at"));
    }

    [Fact]
    public void LockContention_CriticalAtFiftyWaiters()
    {
        var threads = Enumerable.Range(1, 50).Select(i => Thread((uint)i, LockStack)).ToList();

        var (findings, _) = Eval(threads);

        Assert.Equal(Severity.Critical, Assert.Single(findings, f => f.Id == "LockContention").Severity);
    }

    [Fact]
    public void LockContention_NotRaisedForAFewWaiters()
    {
        var threads = Enumerable.Range(1, 9).Select(i => Thread((uint)i, LockStack)).ToList();

        var (findings, _) = Eval(threads);

        Assert.DoesNotContain(findings, f => f.Id == "LockContention");
    }

    [Fact]
    public void HighThreadCount_IgnoresDeadThreads()
    {
        var threads = Enumerable.Range(1, 90).Select(i => Thread((uint)i, []))
            .Concat(Enumerable.Range(1000, 3000).Select(i => Thread((uint)i, [], alive: false)))
            .ToList();

        var (findings, _) = Eval(threads);

        Assert.DoesNotContain(findings, f => f.Id == "HighThreadCount");
    }

    [Theory]
    [InlineData(150, Severity.Warning)]
    [InlineData(250, Severity.Critical)]
    public void HighThreadCount_SeverityByLiveThreads(int live, Severity expected)
    {
        var threads = Enumerable.Range(1, live).Select(i => Thread((uint)i, [])).ToList();

        var (findings, _) = Eval(threads);

        Assert.Equal(expected, Assert.Single(findings, f => f.Id == "HighThreadCount").Severity);
    }

    [Fact]
    public void HighThreadCount_WithLowCpu_PointsAtDependencies()
    {
        var threads = Enumerable.Range(1, 150).Select(i => Thread((uint)i, [])).ToList();
        var pool = new ThreadPoolStats(CpuUtilization: 12, MinThreads: 8, MaxThreads: 32767,
            ActiveWorkerThreads: 150, IdleWorkerThreads: 10, RetiredWorkerThreads: 0);

        var (findings, _) = Eval(threads, pool: pool);

        var f = Assert.Single(findings, f => f.Id == "HighThreadCount");
        Assert.Contains("dependency", f.Explanation);
    }

    [Fact]
    public void ThreadPoolStarvation_WhenNoIdleWorkersAtOrAboveMin()
    {
        var pool = new ThreadPoolStats(30, MinThreads: 50, MaxThreads: 32767,
            ActiveWorkerThreads: 60, IdleWorkerThreads: 0, RetiredWorkerThreads: 0);

        var (findings, _) = Eval(pool: pool);

        Assert.Equal(Severity.Critical, Assert.Single(findings, f => f.Id == "ThreadPoolStarvation").Severity);
    }

    [Fact]
    public void ThreadPoolStarvation_NotRaisedWhileWorkersAreIdle()
    {
        // The real dump: 296 active, 333 idle, min 591
        var pool = new ThreadPoolStats(81, 591, 32767, 296, 333, 20);

        var (findings, _) = Eval(pool: pool);

        Assert.DoesNotContain(findings, f => f.Id == "ThreadPoolStarvation");
    }

    [Fact]
    public void HighCpuAtCapture_81Percent_MentionsGarbageCollection()
    {
        var pool = new ThreadPoolStats(81, 591, 32767, 296, 333, 20);

        var (findings, _) = Eval(pool: pool);

        var f = Assert.Single(findings, f => f.Id == "HighCpuAtCapture");
        Assert.Contains("garbage collection", f.Explanation);
    }

    [Fact]
    public void HighCpuAtCapture_NotRaisedBelowThreshold()
    {
        var pool = new ThreadPoolStats(40, 8, 32767, 10, 5, 0);

        var (findings, _) = Eval(pool: pool);

        Assert.DoesNotContain(findings, f => f.Id == "HighCpuAtCapture");
    }

    // ── Heap / GC findings ───────────────────────────────────────────────────

    [Fact]
    public void GcInProgressAtCapture_WhenHeapWalkIncomplete()
    {
        var (findings, _) = Eval(heapWalkComplete: false);

        var f = Assert.Single(findings, f => f.Id == "GcInProgressAtCapture");
        Assert.Equal(Severity.Info, f.Severity);
    }

    [Fact]
    public void LohRecommendations_DoNotAdviseForcedCompactionAsAFix()
    {
        var gc = new GcStats(500L * 1024 * 1024, 0, 0, 0, 400L * 1024 * 1024, 0, 45.0, [],
            LohFreeBytes: 180L * 1024 * 1024);

        var (findings, _) = Eval(gc: gc);

        foreach (var f in findings.Where(f => f.Id is "LargeObjectHeapGrowth" or "LohFragmentationHigh"))
        {
            Assert.Contains("anti-pattern", f.Recommendation);
            Assert.DoesNotContain("GC.Collect", f.Recommendation);
        }
        Assert.Equal(2, findings.Count(f => f.Id is "LargeObjectHeapGrowth" or "LohFragmentationHigh"));
    }

    [Fact]
    public void LargeTypes_AreNotReportedAsLeaksFromASingleDump()
    {
        var heap = new[] { new HeapTypeStats("MyApp.Thing", 1000, 50L * 1024 * 1024, 50L * 1024 * 1024, 2) };
        var leaks = new[] { new LeakSuspect("MyApp.Thing", 1000, 50L * 1024 * 1024, ["StrongHandle: 0000000000001234 → 0000000000005678"]) };

        var (findings, score) = Eval(heap: heap, leaks: leaks);

        Assert.DoesNotContain(findings, f => f.Id == "SuspectedMemoryLeak");
        Assert.Equal(100, score);
    }

    [Fact]
    public void HighLiveObjectCount_RecommendsDiffingASecondDump()
    {
        var heap = new[] { new HeapTypeStats("System.String", 2_000_000, 1200L * 1024 * 1024, 1200L * 1024 * 1024, 2) };

        var (findings, _) = Eval(heap: heap, heapWalkComplete: false);

        var f = Assert.Single(findings, f => f.Id == "HighLiveObjectCount");
        Assert.Contains("'diff'", f.Recommendation);
        Assert.Contains("partial heap walk", f.Summary);
    }

    // ── Dump metadata ────────────────────────────────────────────────────────

    [Fact]
    public void ReadMinidumpTimestamp_ReadsHeaderTime()
    {
        var path = Path.Combine(Path.GetTempPath(), $"memdump-analyzer-test-{Guid.NewGuid():N}.dmp");
        try
        {
            var header = new byte[32];
            "MDMP"u8.CopyTo(header);
            var expected = new DateTime(2026, 3, 1, 12, 23, 6, DateTimeKind.Utc);
            BitConverter.GetBytes((uint)new DateTimeOffset(expected).ToUnixTimeSeconds()).CopyTo(header, 20);
            File.WriteAllBytes(path, header);

            Assert.Equal(expected, DumpLoader.ReadMinidumpTimestamp(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadMinidumpTimestamp_NullForNonMinidump()
    {
        var path = Path.Combine(Path.GetTempPath(), $"memdump-analyzer-test-{Guid.NewGuid():N}.dmp");
        try
        {
            File.WriteAllBytes(path, new byte[32]);
            Assert.Null(DumpLoader.ReadMinidumpTimestamp(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Reports ──────────────────────────────────────────────────────────────

    private static AnalysisResult MinimalResult(IReadOnlyList<HeapTypeStats> heap, IReadOnlyList<ThreadInfo> threads,
        bool heapWalkComplete = true) => new(
        DumpPath: @"C:\dumps\w3wp.dmp",
        CaptureTime: new DateTime(2026, 3, 1, 12, 23, 6, DateTimeKind.Utc),
        DumpFileSizeBytes: 1024,
        ClrVersion: "4.8.9315.0",
        AppDomainName: "DefaultDomain",
        OsVersion: "Windows",
        Threads: threads,
        ThreadGroups: [],
        HeapTypes: heap,
        GcStats: EmptyGc(),
        DuplicateStrings: [],
        LeakSuspects: [],
        ApplicationHotspots: [],
        KnownAssemblyFilters: [],
        Findings: [],
        HealthScore: 100,
        HeapWalkComplete: heapWalkComplete);

    [Fact]
    public void HtmlReporter_EscapesTypeNamesAndHeapStrings()
    {
        var heap = new[] { new HeapTypeStats("System.Collections.Generic.Queue<MyApp.WorkItem>", 1, 1024, 1024, 0) };
        var result = MinimalResult(heap, []) with
        {
            DuplicateStrings = [new StringDuplication("<script>alert(1)</script>", 2, 20_000)]
        };

        var html = HtmlReporter.Render(result);

        Assert.Contains("Queue&lt;MyApp.WorkItem&gt;", html);
        Assert.DoesNotContain("<MyApp.WorkItem>", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public void Reports_FlagIncompleteHeapAndExcludeDeadThreads()
    {
        var threads = new[]
        {
            new ThreadInfo(1, 1, "Alive", null, ["MyApp.Main()  [MyApp]"], false, false, null),
            new ThreadInfo(2, 2, "Dead", null, [], false, false, null, IsAlive: false),
        };
        var result = MinimalResult([], threads, heapWalkComplete: false);

        var md = MarkdownReporter.Render(result);
        var html = HtmlReporter.Render(result);

        Assert.Contains("Incomplete heap data", md);
        Assert.Contains("Live threads: **1**", md);
        Assert.Contains("Incomplete heap data", html);
        Assert.Contains("12:23:06 UTC", html);
    }
}
