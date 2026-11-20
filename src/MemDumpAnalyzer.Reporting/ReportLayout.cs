using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Reporting;

/// <summary>A report section: its heading key (<see cref="Skins.ReportHeadings"/>) and link anchor.</summary>
public sealed record ReportSection(string Key, string Anchor);

/// <summary>
/// The structure every report shares — section order, anchors and cross-links — so HTML, PDF and Markdown
/// stay in step. Conclusions come first, raw data last.
/// </summary>
public static class ReportLayout
{
    public const string ContentsAnchor = "contents";

    private static readonly string[] Order =
    [
        "executive_summary",
        "findings",
        "thread_analysis",
        "application_code_hotspots",
        "memory_overview",
        "top_memory_consumers",
        "retention_paths",
        "duplicate_strings",
        "gc_health",
        "findings_detail",
    ];

    /// <summary>The sections this report has, in order (optional sections are left out when empty).</summary>
    public static IReadOnlyList<ReportSection> SectionsFor(AnalysisResult r) =>
        Order.Where(key => key switch
            {
                "application_code_hotspots" => r.ApplicationHotspots.Count > 0,
                "retention_paths" => r.Retention is { IsEmpty: false },
                "duplicate_strings" => r.DuplicateStrings.Count > 0,
                "findings_detail" => r.Findings.Count > 0,
                _ => true
            })
            .Select(key => new ReportSection(key, Anchor(key)))
            .ToList();

    public static string Anchor(string sectionKey) => sectionKey.Replace('_', '-');

    /// <summary>Anchor of a finding's short entry (index = position in <see cref="AnalysisResult.Findings"/>).</summary>
    public static string FindingAnchor(int index) => $"finding-{index + 1}";

    /// <summary>Anchor of a finding's full entry in the details section.</summary>
    public static string FindingDetailAnchor(int index) => $"finding-detail-{index + 1}";

    public static string PatternAnchor(int index) => $"pattern-{index + 1}";

    /// <summary>The section whose data backs a finding up, or null.</summary>
    public static string? RelatedSection(string findingId) => findingId switch
    {
        "HighThreadCount" or "ThreadPoolStarvation" or "LockContention" or "SqlConnectionPoolExhausted"
            or "HighCpuAtCapture" => "thread_analysis",
        "HighLiveObjectCount" => "top_memory_consumers",
        "LargeObjectHeapGrowth" or "LohFragmentationHigh" => "gc_health",
        "DuplicateStringWaste" => "duplicate_strings",
        "GcInProgressAtCapture" => "memory_overview",
        _ => null
    };

    /// <summary>The related section when the report has it.</summary>
    public static ReportSection? RelatedSection(AnalysisResult r, Finding f) =>
        RelatedSection(f.Id) is { } key ? SectionsFor(r).FirstOrDefault(s => s.Key == key) : null;

    /// <summary>The text above the hotspots table, the same in every report.</summary>
    public static string HotspotsIntro(AnalysisResult r) =>
        r.KnownAssemblyFilters.Count == 0
            ? "Every assembly found in thread stacks, ranked by the number of threads: the known-assembly filter file " +
              "has no rules, so nothing is hidden — .NET, ASP.NET and platform assemblies are included."
            : "Assemblies found in thread stacks that the known-assembly filter does not hide " +
              $"({r.KnownAssemblyFilters.Count} rule{(r.KnownAssemblyFilters.Count == 1 ? "" : "s")} active — " +
              "-prefix hides an assembly, +prefix shows it even under a broader exclude). " +
              "These are likely your own application code contributing to the problems above.";

    /// <summary>The text above the retention paths, the same in every report.</summary>
    public static string RetentionIntro(RetentionReport retention) =>
        "The largest objects on the heap and the chain of references that keeps each one alive, from a GC root down to " +
        "the object (like WinDbg !gcroot). The owner nearest the object usually explains it: a cache, a static " +
        $"collection, an event subscription. Searched: {retention.RootsSearched}, in {retention.Seconds:N1} s." +
        (retention.Note is { } note ? " " + note : "");

    /// <summary>Why objects can lack a path, given what was searched.</summary>
    public static string RetentionNotFoundText(RetentionReport retention) =>
        retention.StackRootsSearched
            ? "No path from any root was found for these objects" + (retention.Complete
                ? ": nothing references them any more, so they are garbage the GC has not collected yet."
                : " before the search stopped.")
            : "No path from static fields, handles or the finalizer queue was found for these objects. Either only a " +
              "thread stack holds them (e.g. a request still working on them; run with --gc-roots to search the stacks, " +
              "slow with many threads) or nothing references them any more (garbage the GC has not collected yet).";

    /// <summary>Indexes (0-based) of the thread patterns whose representative stack has frames from the assembly.</summary>
    public static IReadOnlyList<int> PatternsWithAssembly(AnalysisResult r, string assembly)
    {
        var marker = $"  [{assembly}]";
        return r.ThreadGroups
            .Select((g, i) => (g, i))
            .Where(x => x.g.RepresentativeStack.Any(f => f.EndsWith(marker, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.i)
            .ToList();
    }
}
