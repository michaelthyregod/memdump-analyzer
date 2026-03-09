using Scriban;
using Scriban.Runtime;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Reporting;

public static class HtmlReporter
{
    private static readonly string TemplateSource = EmbeddedTemplate.Html;

    public static string Render(AnalysisResult result)
    {
        var template = Template.Parse(TemplateSource);
        if (template.HasErrors)
            throw new InvalidOperationException($"Template parse error: {string.Join("; ", template.Messages)}");

        var scriptObj = new ScriptObject();
        scriptObj.Import(new
        {
            r = result,
            blocked_thread_count = result.Threads.Count(t => t.BlockingReason != null)
        });

        scriptObj.Import("format_bytes", new Func<long, string>(FormatBytes));
        scriptObj.Import("severity_class", new Func<Severity, string>(s => s switch
        {
            Severity.Critical => "critical",
            Severity.Warning => "warning",
            _ => "info"
        }));
        scriptObj.Import("severity_label", new Func<Severity, string>(s => s.ToString().ToUpper()));

        var context = new TemplateContext { LoopLimit = 0 }; // real dumps can have thousands of objects/threads
        context.PushGlobal(scriptObj);

        return template.Render(context);
    }

    public static async Task WriteAsync(AnalysisResult result, string outputPath)
        => await File.WriteAllTextAsync(outputPath, Render(result));

    internal static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
