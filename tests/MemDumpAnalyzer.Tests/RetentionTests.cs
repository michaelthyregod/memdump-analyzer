using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting;

namespace MemDumpAnalyzer.Tests;

/// <summary>Retention paths ("who keeps the largest objects alive"): the model's text forms and every report.</summary>
public class RetentionTests
{
    private const string DictionaryNodes = "System.Collections.Concurrent.ConcurrentDictionary<System.Int32, Acme.Node>+Node[]";

    private static readonly RetentionPath CachePath = new(
        DictionaryNodes, 0x1000, 640_024, "static Acme.Caching.CacheManager.s_caches",
        [
            new RetentionLink("Acme.Caching.CacheManager", 0x10, null),
            new RetentionLink("System.Collections.Concurrent.ConcurrentDictionary<System.Int32, Acme.Node>", 0x20, "._items"),
            new RetentionLink(DictionaryNodes, 0x1000, ".m_buckets"),
        ]);

    private static RetentionReport Report(bool stackRootsSearched = false, IReadOnlyList<RetentionTarget>? notFound = null) =>
        new([CachePath], notFound ?? [new RetentionTarget("System.Byte[]", 0xABC0, 1_048_600)], Complete: true,
            stackRootsSearched ? "static fields, handles, finalizer queue, thread stacks" : "static fields, handles, finalizer queue (not thread stacks)",
            stackRootsSearched, Seconds: 2.5);

    private static AnalysisResult Result(RetentionReport? retention) => new(
        DumpPath: @"C:\dumps\w3wp.dmp",
        CaptureTime: new DateTime(2026, 3, 1, 12, 23, 6, DateTimeKind.Utc),
        DumpFileSizeBytes: 1024,
        ClrVersion: "4.8.9315.0",
        AppDomainName: "DefaultDomain",
        OsVersion: "Windows",
        Threads: [],
        ThreadGroups: [],
        HeapTypes: [new HeapTypeStats(DictionaryNodes, 3, 2_000_000, 2_000_000, 3)],
        GcStats: new GcStats(0, 0, 0, 0, 0, 0, 0, []),
        DuplicateStrings: [],
        LeakSuspects: [],
        ApplicationHotspots: [],
        KnownAssemblyFilters: [],
        Findings: [],
        HealthScore: 100,
        Retention: retention);

    [Fact]
    public void Describe_ShowsRootFieldsAndTheObject()
    {
        var lines = CachePath.Describe();

        Assert.Equal($"{DictionaryNodes}  {640_024:N0} bytes  @ 0000000000001000", lines[0]);   // N0 follows the culture, as in every report
        Assert.Equal("  held by static Acme.Caching.CacheManager.s_caches", lines[1]);
        Assert.Equal("    Acme.Caching.CacheManager", lines[2]);
        Assert.Equal($"    .m_buckets → {DictionaryNodes}   ← this object", lines[^1]);
        Assert.Equal($"static Acme.Caching.CacheManager.s_caches → Acme.Caching.CacheManager → " +
                     $"System.Collections.Concurrent.ConcurrentDictionary<System.Int32, Acme.Node> → {DictionaryNodes}", CachePath.Summary);
    }

    [Fact]
    public void LongChain_KeepsHeadAndTail_AndSaysHowManyLinksAreLeftOut()
    {
        var kept = Enumerable.Range(0, RetentionPath.KeptHeadLinks + RetentionPath.KeptTailLinks)
            .Select(i => new RetentionLink($"T{i}", (ulong)i, i == 0 ? null : ".f")).ToList();
        var path = new RetentionPath("T29", 29, 100_000, "Strong handle", kept, OmittedLinks: 1_234);

        var lines = path.Describe();

        Assert.Equal($"      … {1_234:N0} links omitted …", lines[2 + RetentionPath.KeptHeadLinks]);
        Assert.Equal("    .f → T5", lines[3 + RetentionPath.KeptHeadLinks]);
        Assert.Contains($"T4 → … {1_234:N0} links … → T5", path.Summary);
    }

    [Fact]
    public void Section_FollowsTopMemoryConsumers_AndIsLeftOutWhenEmpty()
    {
        var keys = ReportLayout.SectionsFor(Result(Report())).Select(s => s.Key).ToList();
        Assert.Equal(keys.IndexOf("top_memory_consumers") + 1, keys.IndexOf("retention_paths"));

        Assert.DoesNotContain(ReportLayout.SectionsFor(Result(null)), s => s.Key == "retention_paths");
        Assert.DoesNotContain(ReportLayout.SectionsFor(Result(RetentionReport.None)), s => s.Key == "retention_paths");
    }

    [Fact]
    public void NotFoundText_DependsOnWhetherStacksWereSearched()
    {
        Assert.Contains("--gc-roots", ReportLayout.RetentionNotFoundText(Report()));
        var withStacks = ReportLayout.RetentionNotFoundText(Report(stackRootsSearched: true));
        Assert.Contains("garbage", withStacks);
        Assert.DoesNotContain("--gc-roots", withStacks);
    }

    [Fact]
    public void Html_ShowsPathsEscaped_AndTheObjectsWithoutAPath()
    {
        var html = HtmlReporter.Render(Result(Report()));

        Assert.Contains("id=\"retention-paths\"", html);
        Assert.Contains("<h2>Who Keeps the Largest Objects Alive</h2>", html);
        Assert.Contains("ConcurrentDictionary&lt;System.Int32, Acme.Node&gt;+Node[]", html);
        Assert.DoesNotContain("<System.Int32, Acme.Node>", html);
        Assert.Contains("static Acme.Caching.CacheManager.s_caches", html);
        Assert.Contains(".m_buckets", html);
        Assert.Contains("000000000000abc0", html);
        Assert.Contains("--gc-roots", html);
        Assert.Contains("href=\"#retention-paths\"", html);   // in the contents
    }

    [Fact]
    public void Markdown_ShowsEachChainAsACodeBlock()
    {
        var md = MarkdownReporter.Render(Result(Report()));

        Assert.Contains("## 6. Who Keeps the Largest Objects Alive", md);   // after memory overview (4) and top consumers (5)
        Assert.Contains("held by `static Acme.Caching.CacheManager.s_caches`", md);
        Assert.Contains($"```\n{DictionaryNodes}  {640_024:N0} bytes", md.ReplaceLineEndings("\n"));
        Assert.Contains("| `System.Byte[]` |", md);
    }

    [Fact]
    public void Json_CarriesTheStructuredPaths()
    {
        var json = JsonReporter.Render(Result(Report()));

        Assert.Contains("\"retention\"", json);
        Assert.Contains("\"via\": \".m_buckets\"", json);
        Assert.Contains("\"stackRootsSearched\": false", json);
    }

    [Fact]
    public void Pdf_WritesWithRetentionPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"memdump-analyzer-test-{Guid.NewGuid():N}.pdf");
        try
        {
            PdfReporter.Write(Result(Report()), path, TestContext.Current.CancellationToken);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
