using System.Net;
using System.Text;
using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting.Skins;

namespace MemDumpAnalyzer.Reporting;

/// <summary>HTML and Markdown reports for the <c>diff</c> command, skinned like the single-dump reports.</summary>
public static class DiffReporter
{
    public static string RenderHtml(DiffResult diff, string baselineName, string problemName, int rows, Skin? skin = null)
    {
        skin ??= Skin.Default;
        static string E(string s) => WebUtility.HtmlEncode(s);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine($"<title>{E(skin.ReportTitle)} — diff</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(SkinCss.Build(skin));
        sb.AppendLine("body{font-family:var(--font-body);background:var(--background);color:var(--text);max-width:1100px;margin:2rem auto;padding:0 1rem;line-height:1.5}" +
                      "h1,h2{font-family:var(--font-heading);font-weight:var(--heading-weight);color:var(--primary)}h1{color:var(--heading)}" +
                      ".header{display:flex;align-items:center;gap:var(--logo-clearance)}.header img{height:var(--logo-height);width:auto}" +
                      "table{border-collapse:collapse;width:100%;background:var(--surface)}th{background:var(--primary);color:var(--on-primary);padding:.5rem;text-align:left}" +
                      "td{padding:.4rem .6rem;border-bottom:1px solid var(--divider)}tr:nth-child(even) td{background:var(--stripe)}" +
                      ".grow{color:var(--critical)}.shrink{color:var(--success)}code{font-family:var(--font-mono)}" +
                      ".finding{background:var(--surface);border-left:4px solid var(--warning);padding:.6rem 1rem;margin:.5rem 0}" +
                      "footer{margin-top:3rem;text-align:center;font-size:.8rem;color:var(--subtle)}");
        sb.AppendLine("</style></head><body>");

        sb.Append("<div class=\"header\">");
        if (skin.Logo != null) sb.Append($"<img src=\"{skin.Logo.DataUri}\" alt=\"\">");
        sb.Append($"<div><h1>{E(skin.ReportTitle)} — diff</h1>");
        if (skin.CompanyName != null) sb.Append($"<div>{E(skin.CompanyName)}</div>");
        sb.AppendLine("</div></div>");

        sb.AppendLine($"<p><strong>Baseline:</strong> {E(baselineName)} &nbsp;→&nbsp; <strong>Problem:</strong> {E(problemName)}</p>");
        if (skin.PreparedFor != null) sb.AppendLine($"<p><strong>Prepared for:</strong> {E(skin.PreparedFor)}</p>");
        if (skin.PreparedBy != null) sb.AppendLine($"<p><strong>Prepared by:</strong> {E(skin.PreparedBy)}</p>");
        sb.AppendLine($"<p>Heap delta: <strong class=grow>{FormatBytes(diff.HeapDeltaBytes)}</strong> | Thread delta: <strong>{diff.ThreadDelta:+#;-#;0}</strong></p>");

        sb.AppendLine("<h2>Findings</h2>");
        foreach (var f in diff.Findings)
            sb.AppendLine($"<div class=\"finding\"><strong>[{f.Severity}]</strong> {E(f.Summary)}: {E(f.Explanation)}</div>");
        if (diff.Findings.Count == 0) sb.AppendLine("<p>No findings.</p>");

        sb.AppendLine($"<h2>Growing Types (Top {rows})</h2>");
        sb.AppendLine("<table><tr><th>Type</th><th>Baseline Count</th><th>Problem Count</th><th>Delta</th><th>Size Delta</th></tr>");
        foreach (var t in diff.GrowingTypes.Take(rows))
            sb.AppendLine($"<tr><td><code>{E(t.TypeName)}</code></td><td>{t.BaselineCount:N0}</td><td>{t.ProblemCount:N0}</td><td class=grow>{t.CountDelta:+#,0;-#,0;0}</td><td class=grow>{(t.SizeDeltaBytes >= 0 ? "+" : "")}{FormatBytes(t.SizeDeltaBytes)}</td></tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>New Types</h2><ul>");
        foreach (var t in diff.NewTypes.Take(rows))
            sb.AppendLine($"<li><code>{E(t)}</code></li>");
        sb.AppendLine("</ul>");
        sb.AppendLine($"<footer>{E(skin.FooterLine)}</footer></body></html>");
        return sb.ToString();
    }

    public static string RenderMarkdown(DiffResult diff, string baselineName, string problemName, int rows, Skin? skin = null)
    {
        skin ??= Skin.Default;
        static string Md(string s) => s.Replace("|", "\\|");

        var sb = new StringBuilder();
        sb.AppendLine($"# {skin.ReportTitle} — diff");
        sb.AppendLine();
        if (skin.CompanyName != null) { sb.AppendLine($"_{skin.CompanyName}_"); sb.AppendLine(); }
        sb.AppendLine($"**Baseline:** `{baselineName}` → **Problem:** `{problemName}`");
        sb.AppendLine();
        if (skin.PreparedFor != null) { sb.AppendLine($"**Prepared for:** {skin.PreparedFor}  "); }
        if (skin.PreparedBy != null) { sb.AppendLine($"**Prepared by:** {skin.PreparedBy}  "); }
        sb.AppendLine($"Heap delta: **{FormatBytes(diff.HeapDeltaBytes)}** | Thread delta: **{diff.ThreadDelta:+#;-#;0}**");
        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();
        foreach (var f in diff.Findings)
            sb.AppendLine($"- **[{f.Severity}]** {f.Summary}: {f.Explanation}");
        if (diff.Findings.Count == 0)
            sb.AppendLine("No findings.");
        sb.AppendLine();
        sb.AppendLine("## Growing Types");
        sb.AppendLine();
        sb.AppendLine("| Type | Baseline | Problem | Δ Count | Δ Size |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var t in diff.GrowingTypes.Take(rows))
            sb.AppendLine($"| `{Md(t.TypeName)}` | {t.BaselineCount:N0} | {t.ProblemCount:N0} | {t.CountDelta:+#,0;-#,0;0} | {(t.SizeDeltaBytes >= 0 ? "+" : "")}{FormatBytes(t.SizeDeltaBytes)} |");
        sb.AppendLine();
        sb.AppendLine("## New Types");
        sb.AppendLine();
        foreach (var t in diff.NewTypes.Take(rows))
            sb.AppendLine($"- `{t}`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"_{skin.FooterLine}_");
        return sb.ToString();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-" + FormatBytes(-bytes);
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
