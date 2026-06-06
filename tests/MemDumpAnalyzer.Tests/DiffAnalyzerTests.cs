using MemDumpAnalyzer.Core.Analysis;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Tests;

public class DiffAnalyzerTests
{
    private static GcStats MakeGc(long total, long gen2 = 0, long loh = 0) =>
        new GcStats(total, 0, 0, gen2, loh, 0, 0, []);

    [Fact]
    public void GrowingType_DetectedWhenSizeIncreases()
    {
        var baseline = new[] { new HeapTypeStats("MyApp.Cache", 100, 10 * 1024 * 1024, 10 * 1024 * 1024, 2) };
        var problem = new[] { new HeapTypeStats("MyApp.Cache", 500, 50 * 1024 * 1024, 50 * 1024 * 1024, 2) };

        var diff = DiffAnalyzer.Compare(baseline, problem, MakeGc(20_000_000), MakeGc(60_000_000), 10, 10,
            TestContext.Current.CancellationToken);

        Assert.Single(diff.GrowingTypes);
        Assert.Equal("MyApp.Cache", diff.GrowingTypes[0].TypeName);
        Assert.Equal(400, diff.GrowingTypes[0].CountDelta);
        Assert.True(diff.GrowingTypes[0].SizeDeltaBytes > 0);
    }

    [Fact]
    public void NewType_DetectedWhenOnlyInProblem()
    {
        var baseline = new[] { new HeapTypeStats("System.String", 1000, 1024 * 1024, 1024 * 1024, 0) };
        var problem = new[]
        {
            new HeapTypeStats("System.String", 1000, 1024 * 1024, 1024 * 1024, 0),
            new HeapTypeStats("MyApp.NewThing", 50, 500_000, 500_000, 2)
        };

        var diff = DiffAnalyzer.Compare(baseline, problem, MakeGc(1024 * 1024), MakeGc(2 * 1024 * 1024), 5, 5,
            TestContext.Current.CancellationToken);

        Assert.Contains("MyApp.NewThing", diff.NewTypes);
    }

    [Fact]
    public void DisappearedType_DetectedWhenOnlyInBaseline()
    {
        var baseline = new[]
        {
            new HeapTypeStats("System.String", 1000, 1024 * 1024, 1024 * 1024, 0),
            new HeapTypeStats("MyApp.OldThing", 10, 10_000, 10_000, 1)
        };
        var problem = new[] { new HeapTypeStats("System.String", 1000, 1024 * 1024, 1024 * 1024, 0) };

        var diff = DiffAnalyzer.Compare(baseline, problem, MakeGc(1024 * 1024 + 10_000), MakeGc(1024 * 1024), 5, 5,
            TestContext.Current.CancellationToken);

        Assert.Contains("MyApp.OldThing", diff.DisappearedTypes);
    }

    [Fact]
    public void HeapGrowthFinding_WhenDeltaExceeds50MB()
    {
        var baseline = Array.Empty<HeapTypeStats>();
        var problem = Array.Empty<HeapTypeStats>();

        var diff = DiffAnalyzer.Compare(baseline, problem,
            MakeGc(100 * 1024 * 1024),
            MakeGc(200 * 1024 * 1024),
            5, 5,
            TestContext.Current.CancellationToken);

        Assert.Contains(diff.Findings, f => f.Id == "HeapGrowthBetweenSnapshots");
    }

    [Fact]
    public void ThreadDelta_IsCalculatedCorrectly()
    {
        var diff = DiffAnalyzer.Compare(
            [], [],
            MakeGc(0), MakeGc(0),
            10, 25,
            TestContext.Current.CancellationToken);

        Assert.Equal(15, diff.ThreadDelta);
    }
}
