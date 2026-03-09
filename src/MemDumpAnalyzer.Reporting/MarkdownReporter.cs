using System.Text;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Reporting;

public static class MarkdownReporter
{
    public static string Render(AnalysisResult r)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Memory Dump Analysis Report");
        sb.AppendLine();
        sb.AppendLine("## 1. Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"| Field | Value |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Dump | `{Path.GetFileName(r.DumpPath)}` |");
        sb.AppendLine($"| Capture time | {r.CaptureTime:yyyy-MM-dd HH:mm:ss} UTC |");
        sb.AppendLine($"| Dump size | {r.DumpFileSizeBytes / (1024 * 1024):N0} MB |");
        sb.AppendLine($"| CLR version | {r.ClrVersion} |");
        sb.AppendLine($"| AppDomain | {r.AppDomainName} |");
        sb.AppendLine($"| Health score | **{r.HealthScore}/100** |");
        sb.AppendLine();

        sb.AppendLine("### Top Findings");
        sb.AppendLine();
        foreach (var f in r.Findings.Take(10))
        {
            string badge = f.Severity switch
            {
                Severity.Critical => "🔴 CRITICAL",
                Severity.Warning => "🟡 WARNING",
                _ => "🔵 INFO"
            };
            sb.AppendLine($"- **{badge}** — {f.Summary}");
            sb.AppendLine($"  > {f.Explanation}");
            if (f.Recommendation != null)
                sb.AppendLine($"  > **Recommendation:** {f.Recommendation}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 2. Memory Overview");
        sb.AppendLine();
        sb.AppendLine($"| Heap Region | Size |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Total managed heap | {FormatBytes(r.GcStats.TotalHeapBytes)} |");
        sb.AppendLine($"| Gen0 | {FormatBytes(r.GcStats.Gen0Bytes)} |");
        sb.AppendLine($"| Gen1 | {FormatBytes(r.GcStats.Gen1Bytes)} |");
        sb.AppendLine($"| Gen2 | {FormatBytes(r.GcStats.Gen2Bytes)} |");
        sb.AppendLine($"| LOH | {FormatBytes(r.GcStats.LohBytes)} |");
        sb.AppendLine($"| POH | {FormatBytes(r.GcStats.PohBytes)} |");
        sb.AppendLine($"| LOH fragmentation | {r.GcStats.LohFragmentationPercent:F1}% |");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 3. Top Memory Consumers");
        sb.AppendLine();
        sb.AppendLine("| Type | Instances | Total Size | Gen |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var t in r.HeapTypes.Take(30))
        {
            sb.AppendLine($"| `{Escape(t.TypeName)}` | {t.InstanceCount:N0} | {FormatBytes(t.TotalSizeBytes)} | {t.Generation} |");
        }
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 4. Thread Analysis");
        sb.AppendLine();
        sb.AppendLine($"Total threads: **{r.Threads.Count}**  ");
        sb.AppendLine($"Blocked threads: **{r.Threads.Count(t => t.BlockingReason != null)}**");
        sb.AppendLine();
        sb.AppendLine("| OS TID | Managed TID | State | Exception |");
        sb.AppendLine("|---:|---:|---|---|");
        foreach (var t in r.Threads.Take(50))
        {
            sb.AppendLine($"| {t.OsThreadId} | {t.ManagedThreadId} | {t.State} | {Escape(t.CurrentException ?? "")} |");
        }
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 5. GC Health");
        sb.AppendLine();
        sb.AppendLine("| Segment | Kind | Committed | Fill % |");
        sb.AppendLine("|---:|---|---:|---:|");
        foreach (var seg in r.GcStats.Segments.Take(20))
        {
            sb.AppendLine($"| Gen{seg.Generation} | {seg.Kind} | {FormatBytes(seg.CommittedBytes)} | {seg.FillRatio * 100:F1}% |");
        }
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 6. Findings Detail");
        sb.AppendLine();
        foreach (var f in r.Findings)
        {
            string badge = f.Severity switch
            {
                Severity.Critical => "🔴",
                Severity.Warning => "🟡",
                _ => "🔵"
            };
            sb.AppendLine($"### {badge} {f.Id}");
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
        }

        if (r.DuplicateStrings.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 7. Duplicate Strings");
            sb.AppendLine();
            sb.AppendLine("| Value (truncated) | Instances | Wasted |");
            sb.AppendLine("|---|---:|---:|");
            foreach (var s in r.DuplicateStrings.Take(20))
            {
                sb.AppendLine($"| `{Escape(s.Value)}` | {s.InstanceCount:N0} | {FormatBytes(s.TotalWastedBytes)} |");
            }
            sb.AppendLine();
        }

        if (r.ApplicationHotspots.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 8. Application Code Hotspots");
            sb.AppendLine();
            sb.AppendLine($"Assemblies found in thread stacks that are **not suppressed** by the known-assembly filter " +
                          $"({r.KnownAssemblyFilters.Count} rule{(r.KnownAssemblyFilters.Count == 1 ? "" : "s")} active — " +
                          "`-prefix` hides, `+prefix` forces visible even under a broader exclude). " +
                          "These are likely your own application code contributing to the issues above.");
            sb.AppendLine();
            sb.AppendLine("| Assembly | Threads | Blocked |");
            sb.AppendLine("|---|---:|---:|");
            foreach (var h in r.ApplicationHotspots.Take(30))
                sb.AppendLine($"| `{Escape(h.Assembly)}` | {h.ThreadCount:N0} | {h.BlockedThreadCount:N0} |");
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

        return sb.ToString();
    }

    public static async Task WriteAsync(AnalysisResult result, string outputPath)
        => await File.WriteAllTextAsync(outputPath, Render(result));

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
