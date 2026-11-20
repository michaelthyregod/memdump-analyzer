using System.Text;
using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting.Skins;

namespace MemDumpAnalyzer.Reporting;

public static class MarkdownReporter
{
    public static string Render(AnalysisResult r, Skin? skin = null)
    {
        skin ??= Skin.Default;
        var sb = new StringBuilder();
        var sections = ReportLayout.SectionsFor(r);
        var present = sections.Select(s => s.Key).ToHashSet();
        string Contents() => $"[↑ {skin.Heading("contents")}](#{ReportLayout.ContentsAnchor})";

        // Markdown carries no colours or fonts: the skin only contributes its texts
        sb.AppendLine($"# {skin.ReportTitle}");
        sb.AppendLine();
        if (skin.CompanyName != null)
        {
            sb.AppendLine($"_{Escape(skin.CompanyName)}_");
            sb.AppendLine();
        }

        int number = 0;
        foreach (var section in sections)
        {
            number++;
            if (section.Key != "executive_summary")
            {
                sb.AppendLine("---");
                sb.AppendLine();
            }
            // Explicit anchors: heading-derived ids differ between Markdown renderers
            sb.AppendLine($"<a id=\"{section.Anchor}\"></a>");
            sb.AppendLine();
            sb.AppendLine($"## {number}. {skin.Heading(section.Key)}");
            sb.AppendLine();

            switch (section.Key)
            {
                case "executive_summary": Summary(); break;
                case "findings": Findings(); break;
                case "thread_analysis": Threads(); break;
                case "application_code_hotspots": Hotspots(); break;
                case "memory_overview": MemoryOverview(); break;
                case "top_memory_consumers": TopConsumers(); break;
                case "duplicate_strings": DuplicateStrings(); break;
                case "gc_health": GcHealth(); break;
                case "findings_detail": FindingsDetail(); break;
            }

            if (section.Key == "executive_summary")
                ContentsList();
            else
            {
                sb.AppendLine(Contents());
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"_{Escape(skin.FooterLine)}_");
        return sb.ToString();

        void Summary()
        {
            sb.AppendLine("| Field | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| Dump | `{Path.GetFileName(r.DumpPath)}` |");
            sb.AppendLine($"| Capture time | {r.CaptureTime:yyyy-MM-dd HH:mm:ss} UTC |");
            sb.AppendLine($"| Dump size | {r.DumpFileSizeBytes / (1024 * 1024):N0} MB |");
            sb.AppendLine($"| CLR version | {r.ClrVersion} |");
            sb.AppendLine($"| AppDomain | {r.AppDomainName} |");
            if (skin.PreparedFor != null) sb.AppendLine($"| Prepared for | {Escape(skin.PreparedFor)} |");
            if (skin.PreparedBy != null) sb.AppendLine($"| Prepared by | {Escape(skin.PreparedBy)} |");
            sb.AppendLine($"| Health score | **{r.HealthScore}/100** |");
            sb.AppendLine();

            if (!r.HeapWalkComplete)
            {
                sb.AppendLine("> ⚠ **Incomplete heap data:** the dump was captured while a garbage collection was running, so heap types, " +
                              "duplicate strings and fragmentation come from the part of the heap that could be read.");
                sb.AppendLine();
            }
        }

        void ContentsList()
        {
            sb.AppendLine($"<a id=\"{ReportLayout.ContentsAnchor}\"></a>");
            sb.AppendLine();
            sb.AppendLine($"**{skin.Heading("contents")}**");
            sb.AppendLine();
            int n = 0;
            foreach (var s in sections)
            {
                n++;
                sb.AppendLine($"{n}. [{skin.Heading(s.Key)}](#{s.Anchor})");
                if (s.Key == "findings")
                    for (int i = 0; i < r.Findings.Count; i++)
                        sb.AppendLine($"   - [{Badge(r.Findings[i].Severity)} {Escape(r.Findings[i].Summary)}](#{ReportLayout.FindingAnchor(i)})");
            }
            sb.AppendLine();
        }

        void Findings()
        {
            if (r.Findings.Count == 0)
            {
                sb.AppendLine("No significant findings — heap looks healthy!");
                sb.AppendLine();
                return;
            }
            for (int i = 0; i < r.Findings.Count; i++)
            {
                var f = r.Findings[i];
                sb.AppendLine($"<a id=\"{ReportLayout.FindingAnchor(i)}\"></a>");
                sb.AppendLine($"- **{Badge(f.Severity)}** — {f.Summary}");
                if (f.Recommendation != null)
                    sb.AppendLine($"  > **Recommendation:** {f.Recommendation}");
                sb.AppendLine($"  > {Links(f, i, detail: false)}");
                sb.AppendLine();
            }
        }

        void Threads()
        {
            var alive = r.Threads.Where(t => t.IsAlive).ToList();
            sb.AppendLine($"Live threads: **{alive.Count}**  ");
            sb.AppendLine($"Dead threads (not analyzed): **{r.Threads.Count - alive.Count}**  ");
            sb.AppendLine($"Waiting/blocked threads: **{alive.Count(t => t.BlockingReason != null)}**");
            if (r.ThreadPool is { } tp)
            {
                sb.AppendLine();
                sb.AppendLine($"Thread pool: active **{tp.ActiveWorkerThreads}**, idle **{tp.IdleWorkerThreads}**, min {tp.MinThreads}, max {tp.MaxThreads} | " +
                              $"CPU at capture (system-wide): **{tp.CpuUtilization}%**");
            }
            sb.AppendLine();

            if (r.ThreadGroups.Count > 0)
            {
                sb.AppendLine($"### {skin.Heading("thread_patterns")}");
                sb.AppendLine();
                sb.AppendLine("| # | Threads | % | Waiting on | Top frame |");
                sb.AppendLine("|---:|---:|---:|---|---|");
                for (int i = 0; i < r.ThreadGroups.Count; i++)
                {
                    var g = r.ThreadGroups[i];
                    var topFrame = g.RepresentativeStack.FirstOrDefault(f => !f.StartsWith('[')) ?? g.RepresentativeStack.FirstOrDefault() ?? "";
                    sb.AppendLine($"| <a id=\"{ReportLayout.PatternAnchor(i)}\"></a>{i + 1} | {g.ThreadCount:N0} | {g.Percentage:F1} | {Escape(g.WaitReason ?? "running")} | `{Escape(topFrame)}` |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("| OS TID | Managed TID | State | Exception |");
            sb.AppendLine("|---:|---:|---|---|");
            foreach (var t in alive.Take(50))
                sb.AppendLine($"| {t.OsThreadId} | {t.ManagedThreadId} | {t.State} | {Escape(t.CurrentException ?? "")} |");
            sb.AppendLine();
        }

        void Hotspots()
        {
            sb.AppendLine($"Assemblies found in thread stacks that are **not suppressed** by the known-assembly filter " +
                          $"({r.KnownAssemblyFilters.Count} rule{(r.KnownAssemblyFilters.Count == 1 ? "" : "s")} active — " +
                          "`-prefix` hides, `+prefix` forces visible even under a broader exclude). " +
                          "These are likely your own application code contributing to the issues above.");
            sb.AppendLine();
            sb.AppendLine("| Assembly | Threads | Blocked | In thread patterns |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var h in r.ApplicationHotspots.Take(30))
            {
                var patterns = string.Join(" ", ReportLayout.PatternsWithAssembly(r, h.Assembly)
                    .Select(i => $"[#{i + 1}](#{ReportLayout.PatternAnchor(i)})"));
                sb.AppendLine($"| `{Escape(h.Assembly)}` | {h.ThreadCount:N0} | {h.BlockedThreadCount:N0} | {patterns} |");
            }
            sb.AppendLine();

            foreach (var h in r.ApplicationHotspots.Take(20))
            {
                sb.AppendLine($"### `{h.Assembly}`");
                sb.AppendLine();
                sb.AppendLine($"Seen in **{h.ThreadCount:N0}** thread(s), **{h.BlockedThreadCount:N0}** blocked.");
                sb.AppendLine();
                sb.AppendLine("Top methods:");
                sb.AppendLine("```");
                foreach (var m in h.TopMethods)
                    sb.AppendLine(m);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        void MemoryOverview()
        {
            sb.AppendLine("| Heap Region | Size |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| Total managed heap | {FormatBytes(r.GcStats.TotalHeapBytes)} |");
            sb.AppendLine($"| Gen0 | {FormatBytes(r.GcStats.Gen0Bytes)} |");
            sb.AppendLine($"| Gen1 | {FormatBytes(r.GcStats.Gen1Bytes)} |");
            sb.AppendLine($"| Gen2 | {FormatBytes(r.GcStats.Gen2Bytes)} |");
            sb.AppendLine($"| LOH | {FormatBytes(r.GcStats.LohBytes)} |");
            sb.AppendLine($"| POH | {FormatBytes(r.GcStats.PohBytes)} |");
            sb.AppendLine($"| LOH fragmentation | {r.GcStats.LohFragmentationPercent:F1}% ({FormatBytes(r.GcStats.LohFreeBytes)} free) |");
            sb.AppendLine($"| Gen2 fragmentation | {r.GcStats.Gen2FragmentationPercent:F1}% ({FormatBytes(r.GcStats.Gen2FreeBytes)} free) |");
            sb.AppendLine();
        }

        void TopConsumers()
        {
            sb.AppendLine("| Type | Instances | Total Size | Gen |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var t in r.HeapTypes.Take(30))
                sb.AppendLine($"| `{Escape(t.TypeName)}` | {t.InstanceCount:N0} | {FormatBytes(t.TotalSizeBytes)} | {t.Generation} |");
            sb.AppendLine();
        }

        void DuplicateStrings()
        {
            sb.AppendLine("| Value (truncated) | Instances | Wasted |");
            sb.AppendLine("|---|---:|---:|");
            foreach (var s in r.DuplicateStrings.Take(20))
                sb.AppendLine($"| `{Escape(s.Value)}` | {s.InstanceCount:N0} | {FormatBytes(s.TotalWastedBytes)} |");
            sb.AppendLine();
        }

        void GcHealth()
        {
            sb.AppendLine("| Kind | Committed | Free objects | Fill % |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var seg in r.GcStats.Segments.Take(20))
                sb.AppendLine($"| {seg.Kind} | {FormatBytes(seg.CommittedBytes)} | {FormatBytes(seg.FreeBytes)} | {seg.FillRatio * 100:F1}% |");
            sb.AppendLine();
        }

        void FindingsDetail()
        {
            for (int i = 0; i < r.Findings.Count; i++)
            {
                var f = r.Findings[i];
                sb.AppendLine($"<a id=\"{ReportLayout.FindingDetailAnchor(i)}\"></a>");
                sb.AppendLine();
                sb.AppendLine($"### {Badge(f.Severity)} {f.Id}");
                sb.AppendLine();
                sb.AppendLine($"**{f.Summary}**");
                sb.AppendLine();
                sb.AppendLine(f.Explanation);
                if (f.Evidence != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"**Evidence:** {f.Evidence}");
                }
                if (f.Recommendation != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"**Recommendation:** {f.Recommendation}");
                }
                if (f.TechnicalDetails != null && f.TechnicalDetails.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("**Technical details:**");
                    sb.AppendLine("```");
                    foreach (var line in f.TechnicalDetails)
                        sb.AppendLine(line);
                    sb.AppendLine("```");
                }
                sb.AppendLine();
                sb.AppendLine(Links(f, i, detail: true));
                sb.AppendLine();
            }
        }

        // Short entry → its details (and back), plus the section whose data backs the finding up
        string Links(Finding f, int index, bool detail)
        {
            var links = new List<string>
            {
                detail
                    ? $"[↑ {skin.Heading("findings")}](#{ReportLayout.FindingAnchor(index)})"
                    : $"[Full explanation, evidence and technical details ↓](#{ReportLayout.FindingDetailAnchor(index)})"
            };
            if (ReportLayout.RelatedSection(f.Id) is { } key && present.Contains(key))
                links.Add($"[{skin.Heading(key)}](#{ReportLayout.Anchor(key)})");
            return string.Join(" · ", links);
        }
    }

    private static string Badge(Severity s) => s switch
    {
        Severity.Critical => "🔴 CRITICAL",
        Severity.Warning => "🟡 WARNING",
        _ => "🔵 INFO"
    };

    public static async Task WriteAsync(AnalysisResult result, string outputPath, CancellationToken cancellationToken = default, Skin? skin = null)
        => await File.WriteAllTextAsync(outputPath, Render(result, skin), cancellationToken);

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
