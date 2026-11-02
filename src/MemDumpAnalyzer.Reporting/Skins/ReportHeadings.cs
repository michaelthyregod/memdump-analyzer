namespace MemDumpAnalyzer.Reporting.Skins;

/// <summary>How report section headings are capitalised.</summary>
public enum HeadingCase
{
    /// <summary>"Top Memory Consumers" — the original look.</summary>
    Title,
    /// <summary>"Top memory consumers" — only the first word and names/acronyms capitalised.</summary>
    Sentence
}

/// <summary>
/// Every section heading in every report, in both cases. Written out by hand rather than lower-cased
/// automatically, so acronyms (GC) and names stay right. Add new headings here, never inline in a renderer.
/// </summary>
public static class ReportHeadings
{
    private static readonly Dictionary<string, (string Title, string Sentence)> All = new()
    {
        ["executive_summary"] = ("Executive Summary", "Executive summary"),
        ["top_findings"] = ("Top Findings", "Top findings"),
        ["findings"] = ("Findings", "Findings"),
        ["memory_overview"] = ("Memory Overview", "Memory overview"),
        ["top_memory_consumers"] = ("Top Memory Consumers", "Top memory consumers"),
        ["thread_analysis"] = ("Thread Analysis", "Thread analysis"),
        ["thread_patterns"] = ("Thread Patterns", "Thread patterns"),
        ["thread_summary"] = ("Thread Summary", "Thread summary"),
        ["gc_health"] = ("GC Health", "GC health"),
        ["duplicate_strings"] = ("Duplicate Strings", "Duplicate strings"),
        ["application_code_hotspots"] = ("Application Code Hotspots", "Application code hotspots"),
        ["findings_detail"] = ("Findings Detail", "Findings detail"),
        ["growing_types"] = ("Growing Types", "Growing types"),
        ["new_types"] = ("New Types", "New types"),
    };

    public static IEnumerable<string> Keys => All.Keys;

    public static string Get(string key, HeadingCase headingCase) =>
        All.TryGetValue(key, out var h)
            ? headingCase == HeadingCase.Sentence ? h.Sentence : h.Title
            : throw new ArgumentException($"Unknown report heading '{key}'.", nameof(key));
}
