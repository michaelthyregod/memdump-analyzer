using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting;
using System.Text.Json;

namespace MemDumpAnalyzer.Tests;

public class ReportingTests
{
    private static AnalysisResult MakeSampleResult() => new(
        DumpPath: @"C:\dumps\test.dmp",
        CaptureTime: new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc),
        DumpFileSizeBytes: 512 * 1024 * 1024,
        ClrVersion: "4.8.4300.0",
        AppDomainName: "TestApp.exe",
        OsVersion: "Windows 10",
        Threads:
        [
            new ThreadInfo(1234, 1, "Alive", null, ["TestApp.Program.Main()  [TestApp]", "mscorlib.Thread.Start()  [mscorlib]"
            ], false, false, null)
        ],
        ThreadGroups: [],
        HeapTypes:
        [
            new HeapTypeStats("System.String", 50_000, 10 * 1024 * 1024, 10 * 1024 * 1024, 0),
            new HeapTypeStats("MyApp.DataModel", 1_000, 5 * 1024 * 1024, 5 * 1024 * 1024, 2)
        ],
        GcStats: new GcStats(
            TotalHeapBytes: 15 * 1024 * 1024,
            Gen0Bytes: 1 * 1024 * 1024,
            Gen1Bytes: 2 * 1024 * 1024,
            Gen2Bytes: 10 * 1024 * 1024,
            LohBytes: 2 * 1024 * 1024,
            PohBytes: 0,
            LohFragmentationPercent: 5.0,
            Segments:
            [
                new GcSegmentInfo(2, "Gen2", 10 * 1024 * 1024, 12 * 1024 * 1024, 0.83)
            ]),
        DuplicateStrings:
        [
            new StringDuplication("conn-string-value", 500, 5 * 1024 * 1024)
        ],
        LeakSuspects: [],
        ApplicationHotspots: [],
        KnownAssemblyFilters: [],
        Findings:
        [
            new Finding("LargeObjectHeapGrowth", Severity.Warning, "LOH is large", "The LOH grew.", "LOH: 2MB", "Use ArrayPool.")
        ],
        HealthScore: 85
    );

    [Fact]
    public void JsonReporter_ProducesValidJson()
    {
        var result = MakeSampleResult();
        var json = JsonReporter.Render(result);

        Assert.NotEmpty(json);
        using var doc = JsonDocument.Parse(json); // throws if invalid
        Assert.Equal("4.8.4300.0", doc.RootElement.GetProperty("clrVersion").GetString());
    }

    [Fact]
    public void MarkdownReporter_ContainsKeyHeaders()
    {
        var result = MakeSampleResult();
        var md = MarkdownReporter.Render(result);

        Assert.Contains("# Memory Dump Analysis Report", md);
        Assert.Contains("## 1. Executive Summary", md);
        Assert.Contains("## 3. Top Memory Consumers", md);
        Assert.Contains("## 4. Thread Analysis", md);
        Assert.Contains("Health score", md);
        Assert.Contains("85/100", md);
    }

    [Fact]
    public void MarkdownReporter_ContainsTypeNames()
    {
        var result = MakeSampleResult();
        var md = MarkdownReporter.Render(result);

        Assert.Contains("System.String", md);
        Assert.Contains("MyApp.DataModel", md);
    }

    [Fact]
    public void HtmlReporter_ProducesValidHtmlStructure()
    {
        var result = MakeSampleResult();
        var html = HtmlReporter.Render(result);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Memory Dump Analysis", html);
        Assert.Contains("85/100", html);
        Assert.Contains("System.String", html);
    }

    [Fact]
    public void HtmlReporter_ContainsFindings()
    {
        var result = MakeSampleResult();
        var html = HtmlReporter.Render(result);

        Assert.Contains("LargeObjectHeapGrowth", html);
        Assert.Contains("WARNING", html);
    }

    [Fact]
    public void HtmlReporter_DoesNotTruncateLargeReports()
    {
        // Scriban's LimitToString defaults to 1 MiB and silently truncates the rendered output
        // with "..." — a single finding's TechnicalDetails (full thread stacks) can exceed that
        // alone, cutting off every later report section.
        var hugeDetails = Enumerable.Range(0, 20_000)
            .Select(i => $"Frame_{i}: {new string('x', 80)}  [TestApp]")
            .ToList();
        var result = MakeSampleResult() with
        {
            Findings =
            [
                new Finding("ThreadPoolExhaustion", Severity.Critical, "Many threads", "Explanation.",
                    "Evidence", "Recommendation", hugeDetails)
            ]
        };

        var html = HtmlReporter.Render(result);

        Assert.True(html.Length > 1_500_000, $"Report unexpectedly small: {html.Length:N0} chars");
        Assert.Contains("MyApp.DataModel", html);   // section after Findings still rendered
        Assert.EndsWith("</html>", html.TrimEnd()); // document is complete, not cut off
    }

    [Fact]
    public void JsonReporter_IncludesAllTopLevelFields()
    {
        var result = MakeSampleResult();
        var json = JsonReporter.Render(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("threads", out _), "Missing 'threads'");
        Assert.True(root.TryGetProperty("heapTypes", out _), "Missing 'heapTypes'");
        Assert.True(root.TryGetProperty("gcStats", out _), "Missing 'gcStats'");
        Assert.True(root.TryGetProperty("findings", out _), "Missing 'findings'");
        Assert.True(root.TryGetProperty("healthScore", out _), "Missing 'healthScore'");
    }
}
