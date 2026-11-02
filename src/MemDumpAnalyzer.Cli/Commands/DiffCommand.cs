using System.CommandLine;
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
            Description = "Number of rows shown per table in the diff report (all types are compared)",
            DefaultValueFactory = _ => 100
        };

        var skinOptions = new SkinOptions();

        var cmd = new Command("diff", "Compare two memory dumps");
        cmd.Add(baselineArg);
        cmd.Add(problemArg);
        cmd.Add(outputOpt);
        cmd.Add(formatOpt);
        cmd.Add(topOpt);
        skinOptions.AddTo(cmd);

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
            var skin = skinOptions.Load(parseResult);
            if (skin == null) return;

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

                        await Task.Run(() => { baselineResult = AnalysisEngine.Analyze(baselineFile.FullName, int.MaxValue, cancellationToken: cancellationToken); t1.Value = 100; }, cancellationToken);
                        currentDump = problemFile.Name;
                        await Task.Run(() => { problemResult = AnalysisEngine.Analyze(problemFile.FullName, int.MaxValue, cancellationToken: cancellationToken); t2.Value = 100; }, cancellationToken);
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

                // Every type is compared — comparing only the top N would report a type that merely moved
                // across the cut-off as "new" or "disappeared".
                foreach (var (name, res) in new[] { (baselineFile.Name, baselineResult), (problemFile.Name, problemResult) })
                {
                    if (!res.HeapWalkComplete)
                        AnsiConsole.MarkupLine($"  [yellow]⚠ {Markup.Escape(name)} was captured mid-GC — its heap numbers are partial, so type deltas are unreliable.[/]");
                }

                var diff = DiffAnalyzer.Compare(
                    baselineResult.HeapTypes,
                    problemResult.HeapTypes,
                    baselineResult.GcStats,
                    problemResult.GcStats,
                    baselineResult.Threads.Count(t => t.IsAlive),
                    problemResult.Threads.Count(t => t.IsAlive),
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
                            await File.WriteAllTextAsync(htmlPath, DiffReporter.RenderHtml(diff, baselineFile.Name, problemFile.Name, topN, skin), cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] HTML diff → {Markup.Escape(htmlPath)}");
                            break;

                        case "md":
                        case "markdown":
                            var mdPath = baseName + ".md";
                            await File.WriteAllTextAsync(mdPath, DiffReporter.RenderMarkdown(diff, baselineFile.Name, problemFile.Name, topN, skin), cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] Markdown diff → {Markup.Escape(mdPath)}");
                            break;

                        default:
                            AnsiConsole.MarkupLine($"  [yellow]Format not supported for diff:[/] {Markup.Escape(fmt)} (use html, md or json)");
                            break;
                    }
                }

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"Heap delta: [yellow]{Markup.Escape(DiffReporter.FormatBytes(diff.HeapDeltaBytes))}[/]  |  Thread delta: [yellow]{diff.ThreadDelta:+#;-#;0}[/]");
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
}
