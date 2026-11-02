using System.Diagnostics.CodeAnalysis;
using Scriban;
using Scriban.Runtime;
using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting.Skins;

namespace MemDumpAnalyzer.Reporting;

public static class HtmlReporter
{
    private static readonly string TemplateSource = EmbeddedTemplate.Html;

    // Scriban reads the model graph and the registered delegates via reflection at render
    // time. The delegate methods are statically rooted by the Func<> constructions below,
    // and the model types (MemDumpAnalyzer.Core) are preserved via ILLink.Descriptors.xml.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Delegates reference statically-rooted methods; model assembly is preserved by ILLink.Descriptors.xml.")]
    public static string Render(AnalysisResult result, Skin? skin = null)
    {
        skin ??= Skin.Default;
        var template = Template.Parse(TemplateSource);
        if (template.HasErrors)
            throw new InvalidOperationException($"Template parse error: {string.Join("; ", template.Messages)}");

        var aliveThreads = result.Threads.Where(t => t.IsAlive).ToList();
        var scriptObj = new ScriptObject
        {
            ["r"] = result,
            ["dump_file_name"] = Path.GetFileName(result.DumpPath),
            ["generated_utc"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            ["alive_threads"] = aliveThreads,
            ["alive_thread_count"] = aliveThreads.Count,
            ["blocked_thread_count"] = aliveThreads.Count(t => t.BlockingReason != null),

            // Skin: plain strings only, so Scriban needs no reflection over the skin types
            ["skin_css"] = SkinCss.Build(skin),
            ["skin_title"] = skin.ReportTitle,
            ["skin_company"] = skin.CompanyName,
            ["skin_prepared_for"] = skin.PreparedFor,
            ["skin_prepared_by"] = skin.PreparedBy,
            ["skin_footer"] = skin.FooterLine,
            ["skin_logo"] = skin.Logo?.DataUri
        };

        var headings = new ScriptObject();
        foreach (var key in ReportHeadings.Keys)
            headings[key] = skin.Heading(key);
        scriptObj["headings"] = headings;

        scriptObj.Import("format_bytes", new Func<long, string>(FormatBytes));
        scriptObj.Import("severity_class", new Func<Severity, string>(s => s switch
        {
            Severity.Critical => "critical",
            Severity.Warning => "warning",
            _ => "info"
        }));
        scriptObj.Import("severity_label", new Func<Severity, string>(s => s.ToString().ToUpper()));

        // Real dumps can have thousands of objects/threads: disable the loop-iteration cap and
        // the 1 MiB output cap (LimitToString silently truncates the rendered HTML with "...")
        var context = new TemplateContext { LoopLimit = 0, LimitToString = 0 };
        context.PushGlobal(scriptObj);

        return template.Render(context);
    }

    public static async Task WriteAsync(AnalysisResult result, string outputPath, CancellationToken cancellationToken = default, Skin? skin = null)
        => await File.WriteAllTextAsync(outputPath, Render(result, skin), cancellationToken);

    internal static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
