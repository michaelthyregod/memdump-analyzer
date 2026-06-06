using System.CommandLine;
using System.Net;
using MemDumpAnalyzer.Core;
using MemDumpAnalyzer.Core.Analysis;
using MemDumpAnalyzer.Reporting;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli.Commands;

public static class DiffCommand
{
    public static Command Build()
    {
        var baselineArg = new Argument<FileInfo>("baseline") { Description = "Baseline (earlier) .dmp file" };
        var problemArg = new Argument<FileInfo>("problem") { Description = "Problem (later) .dmp file" };

        var outputOpt = new Option<DirectoryInfo?>("--output", "-o") { Description = "Output directory (default: ./report)" };
        var formatOpt = new Option<string>("--format", "-f")
        {
            Description = "Comma-separated output formats",
            DefaultValueFactory = _ => "html,json"
        };
        var topOpt = new Option<int>("--top")
        {
            Description = "Top N types",
            DefaultValueFactory = _ => 100
        };

        var cmd = new Command("diff", "Compare two memory dumps");
        cmd.Add(baselineArg);
        cmd.Add(problemArg);
        cmd.Add(outputOpt);
        cmd.Add(formatOpt);
        cmd.Add(topOpt);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var baselineFile = parseResult.GetValue(baselineArg)!;
            var problemFile = parseResult.GetValue(problemArg)!;
            var outputDir = parseResult.GetValue(outputOpt) ?? new DirectoryInfo("./report");
            var formats = (parseResult.GetValue(formatOpt) ?? "html,json")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var topN = parseResult.GetValue(topOpt);

            foreach (var f in new[] { baselineFile, problemFile })
            {
                if (!f.Exists)
                {
                    AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(f.FullName)}");
                    Environment.ExitCode = 1;
                    return;
                }
            }

            outputDir.Create();
            AnsiConsole.MarkupLine($"[bold blue]memdump-analyzer diff[/] — [yellow]{Markup.Escape(baselineFile.Name)}[/] vs [yellow]{Markup.Escape(problemFile.Name)}[/]");

            MemDumpAnalyzer.Core.Models.AnalysisResult? baselineResult = null;
            MemDumpAnalyzer.Core.Models.AnalysisResult? problemResult = null;
            var currentDump = baselineFile.Name;

            try
            {
                try
                {
                    await AnsiConsole.Progress().StartAsync(async ctx =>
                    {
                        var t1 = ctx.AddTask("[green]Analyzing baseline...[/]");
                        var t2 = ctx.AddTask("[green]Analyzing problem dump...[/]");

                        await Task.Run(() => { baselineResult = AnalysisEngine.Analyze(baselineFile.FullName, topN, cancellationToken: cancellationToken); t1.Value = 100; }, cancellationToken);
                        currentDump = problemFile.Name;
                        await Task.Run(() => { problemResult = AnalysisEngine.Analyze(problemFile.FullName, topN, cancellationToken: cancellationToken); t2.Value = 100; }, cancellationToken);
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ErrorReporting.PrintAnalysisError(ex, currentDump);
                    Environment.ExitCode = 1;
                    return;
                }

                if (baselineResult == null || problemResult == null) return;

                var diff = DiffAnalyzer.Compare(
                    baselineResult.HeapTypes,
                    problemResult.HeapTypes,
                    baselineResult.GcStats,
                    problemResult.GcStats,
                    baselineResult.Threads.Count,
                    problemResult.Threads.Count,
                    cancellationToken
                );

                var baseName = Path.Combine(outputDir.FullName,
                    $"diff_{Path.GetFileNameWithoutExtension(baselineFile.Name)}_vs_{Path.GetFileNameWithoutExtension(problemFile.Name)}");

                foreach (var fmt in formats)
                {
                    switch (fmt.ToLowerInvariant())
                    {
                        case "json":
                            var jsonPath = baseName + ".json";
                            await JsonReporter.WriteAsync(diff, jsonPath, cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] JSON diff → {Markup.Escape(jsonPath)}");
                            break;

                        case "html":
                            var htmlPath = baseName + ".html";
                            await WriteDiffHtml(diff, baselineResult, problemResult, htmlPath, cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] HTML diff → {Markup.Escape(htmlPath)}");
                            break;

                        case "md":
                        case "markdown":
                            var mdPath = baseName + ".md";
                            await WriteDiffMarkdown(diff, baselineResult, problemResult, mdPath, cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] Markdown diff → {Markup.Escape(mdPath)}");
                            break;
                    }
                }

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"Heap delta: [yellow]{FormatBytes(diff.HeapDeltaBytes)}[/]  |  Thread delta: [yellow]{diff.ThreadDelta:+#;-#;0}[/]");
                AnsiConsole.MarkupLine($"Growing types: [red]{diff.GrowingTypes.Count}[/]  |  New types: [yellow]{diff.NewTypes.Count}[/]  |  Disappeared: [green]{diff.DisappearedTypes.Count}[/]");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("\n[yellow]Canceled.[/]");
                Environment.ExitCode = 130; // conventional exit code for interrupted (Ctrl+C)
            }
        });

        return cmd;
    }

    private static async Task WriteDiffHtml(
        MemDumpAnalyzer.Core.Models.DiffResult diff,
        MemDumpAnalyzer.Core.Models.AnalysisResult baseline,
        MemDumpAnalyzer.Core.Models.AnalysisResult problem,
        string path,
        CancellationToken cancellationToken)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=UTF-8><title>Diff Report</title>");
        sb.AppendLine("<style>body{font-family:sans-serif;max-width:1100px;margin:2rem auto;color:#222}h1,h2{color:#1b4f72}table{border-collapse:collapse;width:100%}th{background:#1b4f72;color:#fff;padding:.5rem}td{padding:.4rem .6rem;border-bottom:1px solid #eee}tr:nth-child(even){background:#f9f9f9}.grow{color:#c0392b}.shrink{color:#27ae60}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>Diff Report</h1><p><strong>Baseline:</strong> {WebUtility.HtmlEncode(Path.GetFileName(baseline.DumpPath))} &nbsp;→&nbsp; <strong>Problem:</strong> {WebUtility.HtmlEncode(Path.GetFileName(problem.DumpPath))}</p>");
        sb.AppendLine($"<p>Heap delta: <strong class=grow>{FormatBytes(diff.HeapDeltaBytes)}</strong> | Thread delta: <strong>{diff.ThreadDelta:+#;-#;0}</strong></p>");

        sb.AppendLine("<h2>Findings</h2>");
        foreach (var f in diff.Findings)
            sb.AppendLine($"<p><strong>[{f.Severity}]</strong> {WebUtility.HtmlEncode(f.Summary)}: {WebUtility.HtmlEncode(f.Explanation)}</p>");

        sb.AppendLine("<h2>Growing Types (Top 50)</h2>");
        sb.AppendLine("<table><tr><th>Type</th><th>Baseline Count</th><th>Problem Count</th><th>Delta</th><th>Size Delta</th></tr>");
        foreach (var t in diff.GrowingTypes.Take(50))
            sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(t.TypeName)}</td><td>{t.BaselineCount:N0}</td><td>{t.ProblemCount:N0}</td><td class=grow>+{t.CountDelta:N0}</td><td class=grow>+{FormatBytes(t.SizeDeltaBytes)}</td></tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>New Types</h2><ul>");
        foreach (var t in diff.NewTypes.Take(30))
            sb.AppendLine($"<li><code>{WebUtility.HtmlEncode(t)}</code></li>");
        sb.AppendLine("</ul></body></html>");

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static async Task WriteDiffMarkdown(
        MemDumpAnalyzer.Core.Models.DiffResult diff,
        MemDumpAnalyzer.Core.Models.AnalysisResult baseline,
        MemDumpAnalyzer.Core.Models.AnalysisResult problem,
        string path,
        CancellationToken cancellationToken)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Diff Report");
        sb.AppendLine();
        sb.AppendLine($"**Baseline:** `{Path.GetFileName(baseline.DumpPath)}` → **Problem:** `{Path.GetFileName(problem.DumpPath)}`");
        sb.AppendLine();
        sb.AppendLine($"Heap delta: **{FormatBytes(diff.HeapDeltaBytes)}** | Thread delta: **{diff.ThreadDelta:+#;-#;0}**");
        sb.AppendLine();
        sb.AppendLine("## Growing Types");
        sb.AppendLine();
        sb.AppendLine("| Type | Baseline | Problem | Δ Count | Δ Size |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var t in diff.GrowingTypes.Take(50))
            sb.AppendLine($"| `{t.TypeName}` | {t.BaselineCount:N0} | {t.ProblemCount:N0} | +{t.CountDelta:N0} | +{FormatBytes(t.SizeDeltaBytes)} |");
        sb.AppendLine();
        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-" + FormatBytes(-bytes);
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
