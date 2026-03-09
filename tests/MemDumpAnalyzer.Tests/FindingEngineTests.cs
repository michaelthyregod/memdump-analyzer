using MemDumpAnalyzer.Core.Heuristics;
using MemDumpAnalyzer.Core.Models;
using Xunit;

namespace MemDumpAnalyzer.Tests;

public class FindingEngineTests
{
    private static GcStats EmptyGc() => new GcStats(0, 0, 0, 0, 0, 0, 0.0, Array.Empty<GcSegmentInfo>());
    private static IReadOnlyList<ThreadGroup> NoGroups() => Array.Empty<ThreadGroup>();

    private static (IReadOnlyList<Finding> Findings, int Score) Eval(
        IReadOnlyList<ThreadInfo>? threads = null,
        IReadOnlyList<ThreadGroup>? groups = null,
        IReadOnlyList<HeapTypeStats>? heap = null,
        GcStats? gc = null,
        IReadOnlyList<StringDuplication>? strings = null,
        IReadOnlyList<LeakSuspect>? leaks = null) =>
        FindingEngine.Evaluate(
            threads ?? Array.Empty<ThreadInfo>(),
            groups ?? NoGroups(),
            heap ?? Array.Empty<HeapTypeStats>(),
            gc ?? EmptyGc(),
            strings ?? Array.Empty<StringDuplication>(),
            leaks ?? Array.Empty<LeakSuspect>());

    [Fact]
    public void NoFindings_WhenHeapIsSmall()
    {
        var threads = new[] { new ThreadInfo(1, 1, "Alive", null, Array.Empty<string>(), false, false, null) };
        var heap = new[] { new HeapTypeStats("System.String", 10, 1024, 1024, 0) };

        var (findings, score) = Eval(threads: threads, heap: heap);

        Assert.Empty(findings);
        Assert.Equal(100, score);
    }

    [Fact]
    public void ThreadPoolExhaustion_WhenHighThreadCount()
    {
        var threads = Enumerable.Range(1, 250)
            .Select(i => new ThreadInfo((uint)i, i, "Alive", null, Array.Empty<string>(), false, false, null))
            .ToList();

        var (findings, score) = Eval(threads: threads);

        Assert.Contains(findings, f => f.Id == "ThreadPoolExhaustion");
        Assert.True(score < 100);
    }

    [Fact]
    public void ThreadPoolExhaustion_HasTechnicalDetails()
    {
        var frames = new[] { "System.Net.HttpWebRequest.GetResponse()  [System]", "MyApp.Client.Fetch()  [MyApp]" };
        var threads = Enumerable.Range(1, 250)
            .Select(i => new ThreadInfo((uint)i, i, "Alive", null, frames, false, false, null))
            .ToList();
        var groups = new[]
        {
            new ThreadGroup(250, 100.0, "outbound HTTP I/O", frames, new uint[] { 1, 2, 3 })
        };

        var (findings, _) = Eval(threads: threads, groups: groups);

        var f = Assert.Single(findings, f => f.Id == "ThreadPoolExhaustion");
        Assert.NotNull(f.TechnicalDetails);
        Assert.True(f.TechnicalDetails!.Count > 0);
    }

    [Fact]
    public void SuspectedDeadlock_WhenMultipleBlockedThreads()
    {
        var threads = new[]
        {
            new ThreadInfo(1, 1, "Blocked", null, Array.Empty<string>(), false, false, "Monitor on 0x1234"),
            new ThreadInfo(2, 2, "Blocked", null, Array.Empty<string>(), false, false, "Monitor on 0x5678"),
        };

        var (findings, _) = Eval(threads: threads);

        Assert.Contains(findings, f => f.Id == "SuspectedDeadlock");
    }

    [Fact]
    public void SuspectedDeadlock_HasTechnicalDetailsWithStacks()
    {
        var stack1 = new[] { "System.Threading.Monitor.Enter(Object)  [mscorlib]", "MyApp.Cache.Get(String)  [MyApp]" };
        var stack2 = new[] { "System.Threading.Monitor.Wait(Object)  [mscorlib]", "MyApp.Db.Query(String)  [MyApp.Data]" };
        var threads = new[]
        {
            new ThreadInfo(100, 1, "Blocked", null, stack1, false, false, "Holding 1 lock(s)"),
            new ThreadInfo(200, 2, "Blocked", null, stack2, false, false, "Holding 2 lock(s)"),
        };

        var (findings, _) = Eval(threads: threads);

        var f = Assert.Single(findings, f => f.Id == "SuspectedDeadlock");
        Assert.NotNull(f.TechnicalDetails);
        Assert.Contains(f.TechnicalDetails!, line => line.Contains("MyApp"));
    }

    [Fact]
    public void LargeObjectHeapGrowth_WhenLohExceedsThreshold()
    {
        var gc = new GcStats(200 * 1024 * 1024, 10 * 1024 * 1024, 10 * 1024 * 1024,
            90 * 1024 * 1024, 90 * 1024 * 1024, 0, 5.0, Array.Empty<GcSegmentInfo>());

        var (findings, _) = Eval(gc: gc);

        Assert.Contains(findings, f => f.Id == "LargeObjectHeapGrowth");
    }

    [Fact]
    public void LohFragmentationHigh_WhenFragOver30Percent()
    {
        var (findings, _) = Eval(gc: new GcStats(0, 0, 0, 0, 0, 0, 45.0, Array.Empty<GcSegmentInfo>()));
        Assert.Contains(findings, f => f.Id == "LohFragmentationHigh");
    }

    [Fact]
    public void Gen2Saturated_WhenAverageFillHigh()
    {
        var segments = new[]
        {
            new GcSegmentInfo(2, "Gen2", 100 * 1024 * 1024, 100 * 1024 * 1024, 0.92),
            new GcSegmentInfo(2, "Gen2", 100 * 1024 * 1024, 100 * 1024 * 1024, 0.90),
        };
        var (findings, _) = Eval(gc: new GcStats(200 * 1024 * 1024, 0, 0, 200 * 1024 * 1024, 0, 0, 0, segments));
        Assert.Contains(findings, f => f.Id == "Gen2Saturated");
    }

    [Fact]
    public void DuplicateStringWaste_WhenLargeWaste()
    {
        var strings = new[] { new StringDuplication("some-connection-string", 10_000, 60 * 1024 * 1024) };
        var (findings, _) = Eval(strings: strings);
        Assert.Contains(findings, f => f.Id == "DuplicateStringWaste");
    }

    [Fact]
    public void HealthScore_DecreasesWithEachFinding()
    {
        var threads = Enumerable.Range(1, 250)
            .Select(i => new ThreadInfo((uint)i, i, "Alive", null, Array.Empty<string>(), false, false, null))
            .Concat(new[]
            {
                new ThreadInfo(300, 300, "Blocked", null, Array.Empty<string>(), false, false, "lock"),
                new ThreadInfo(301, 301, "Blocked", null, Array.Empty<string>(), false, false, "lock"),
            }).ToList();

        var (_, score) = Eval(threads: threads);

        Assert.True(score < 100);
        Assert.True(score >= 0);
    }

    [Fact]
    public void Findings_OrderedBySeverityDescending()
    {
        var threads = Enumerable.Range(1, 250)
            .Select(i => new ThreadInfo((uint)i, i, "Alive", null, Array.Empty<string>(), false, false, null))
            .ToList();
        var gc = new GcStats(0, 0, 0, 0, 0, 0, 45.0, Array.Empty<GcSegmentInfo>());

        var (findings, _) = Eval(threads: threads, gc: gc);

        for (int i = 1; i < findings.Count; i++)
            Assert.True(findings[i - 1].Severity >= findings[i].Severity,
                "Findings should be ordered by severity descending");
    }
}
