using MemDumpAnalyzer.Core.Analysis;
using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting;

namespace MemDumpAnalyzer.Tests;

/// <summary>
/// The known-assembly filter: no file means no hotspots; a file without rules hides nothing. An empty file used to
/// behave like a missing one and dropped the hotspots section.
/// </summary>
public class HotspotFilterTests
{
    private static readonly ThreadInfo[] Threads =
    [
        new(1, 1, "Alive", null,
            ["System.Threading.Monitor.Enter(System.Object)  [mscorlib]", "Acme.Web.Checkout.Pay()  [Acme.Web]"],
            false, false, null, IsAlive: true),
    ];

    private static IReadOnlyList<string>? LoadFrom(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"memdump-analyzer-known-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        try { return ApplicationHotspotAnalyzer.LoadFilter(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFilter_MissingFileIsNull_EmptyOrCommentOnlyFileHasNoRules()
    {
        Assert.Null(ApplicationHotspotAnalyzer.LoadFilter(null));
        Assert.Null(ApplicationHotspotAnalyzer.LoadFilter(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt")));
        Assert.Empty(LoadFrom("")!);
        Assert.Empty(LoadFrom("# only a comment\n\n// another\n")!);
    }

    [Fact]
    public void NoFilter_NoHotspots()
    {
        Assert.Empty(ApplicationHotspotAnalyzer.Analyze(Threads, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FilterWithoutRules_ShowsEveryAssembly()
    {
        var hotspots = ApplicationHotspotAnalyzer.Analyze(Threads, [], TestContext.Current.CancellationToken);

        Assert.Equal(["mscorlib", "Acme.Web"], hotspots.Select(h => h.Assembly).Order().Reverse());
    }

    [Fact]
    public void FilterWithRules_HidesMatchingAssemblies()
    {
        var hotspots = ApplicationHotspotAnalyzer.Analyze(Threads, ["-mscorlib"], TestContext.Current.CancellationToken);

        Assert.Equal("Acme.Web", Assert.Single(hotspots).Assembly);
    }

    [Fact]
    public void Intro_SaysNothingIsHiddenWhenTheFilterHasNoRules()
    {
        var result = new AnalysisResult(
            DumpPath: @"C:\dumps\w3wp.dmp",
            CaptureTime: new DateTime(2026, 3, 1, 12, 23, 6, DateTimeKind.Utc),
            DumpFileSizeBytes: 1024,
            ClrVersion: "4.8.9315.0",
            AppDomainName: "DefaultDomain",
            OsVersion: "Windows",
            Threads: Threads,
            ThreadGroups: [],
            HeapTypes: [],
            GcStats: new GcStats(0, 0, 0, 0, 0, 0, 0, []),
            DuplicateStrings: [],
            LeakSuspects: [],
            ApplicationHotspots: [new AssemblyHotspot("mscorlib", 1, 1, [])],
            KnownAssemblyFilters: [],
            Findings: [],
            HealthScore: 100);

        Assert.Contains("nothing is hidden", ReportLayout.HotspotsIntro(result));
        Assert.Contains("nothing is hidden", HtmlReporter.Render(result));
        Assert.Contains("nothing is hidden", MarkdownReporter.Render(result));
        Assert.Contains("1 rule active", ReportLayout.HotspotsIntro(result with { KnownAssemblyFilters = ["-System"] }));
    }
}
