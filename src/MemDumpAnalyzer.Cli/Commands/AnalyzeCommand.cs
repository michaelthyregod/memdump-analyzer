using System.CommandLine;
using MemDumpAnalyzer.Core;
using MemDumpAnalyzer.Reporting;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli.Commands;

public static class AnalyzeCommand
{
    public static Command Build()
    {
        var dumpArg = new Argument<FileInfo>("dump") { Description = "Path to the .dmp file to analyze" };

        var outputOpt = new Option<DirectoryInfo?>("--output", "-o") { Description = "Output directory (default: ./report)" };
        var formatOpt = new Option<string>("--format", "-f")
        {
            Description = "Comma-separated list of output formats: html, md, json, pdf",
            DefaultValueFactory = _ => "html,json"
        };
        var topOpt = new Option<int>("--top")
        {
            Description = "Top N types to include in heap analysis",
            DefaultValueFactory = _ => 100
        };
        var knownAssembliesOpt = new Option<FileInfo?>("--known-assemblies", "-k")
        {
            Description = "Path to a text file listing known/trusted assembly prefixes (one per line). " +
                          "Assemblies not matching any prefix are reported as application hotspots. " +
                          "Also auto-detected as 'known-assemblies.txt' in the current directory."
        };

        var cmd = new Command("analyze", "Analyze a single memory dump");
        cmd.Add(dumpArg);
        cmd.Add(outputOpt);
        cmd.Add(formatOpt);
        cmd.Add(topOpt);
        cmd.Add(knownAssembliesOpt);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var dumpFile   = parseResult.GetValue(dumpArg)!;
            var outputDir  = parseResult.GetValue(outputOpt) ?? new DirectoryInfo("./report");
            var formats    = (parseResult.GetValue(formatOpt) ?? "html,json")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var topN       = parseResult.GetValue(topOpt);

            // Known-assemblies filter: explicit flag, then auto-detect in cwd
            var knownFile  = parseResult.GetValue(knownAssembliesOpt)?.FullName
                ?? (File.Exists("known-assemblies.txt") ? Path.GetFullPath("known-assemblies.txt") : null);

            if (!dumpFile.Exists)
            {
                AnsiConsole.MarkupLine($"[red]Dump file not found:[/] {Markup.Escape(dumpFile.FullName)}");
                Environment.ExitCode = 1;
                return;
            }

            outputDir.Create();

            AnsiConsole.MarkupLine($"[bold blue]memdump-analyzer[/] — analyzing [yellow]{Markup.Escape(dumpFile.Name)}[/]");
            if (knownFile != null)
                AnsiConsole.MarkupLine($"  [grey]Assembly filter:[/] {Markup.Escape(Path.GetFileName(knownFile))}");

            MemDumpAnalyzer.Core.Models.AnalysisResult? result = null;

            try
            {
                try
                {
                    await AnsiConsole.Progress()
                        .StartAsync(async ctx =>
                        {
                            var task = ctx.AddTask("[green]Loading and analyzing dump...[/]", maxValue: 100);
                            task.Value = 5;

                            await Task.Run(() =>
                            {
                                result = AnalysisEngine.Analyze(dumpFile.FullName, topN, knownFile, cancellationToken);
                                task.Value = 100;
                            }, cancellationToken);
                        });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ErrorReporting.PrintAnalysisError(ex, dumpFile.Name);
                    Environment.ExitCode = 1;
                    return;
                }

                if (result == null) return;

                var baseName = Path.Combine(outputDir.FullName, Path.GetFileNameWithoutExtension(dumpFile.Name));

                foreach (var fmt in formats)
                {
                    switch (fmt.ToLowerInvariant())
                    {
                        case "json":
                            var jsonPath = baseName + ".json";
                            await JsonReporter.WriteAsync(result, jsonPath, cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] JSON → {Markup.Escape(jsonPath)}");
                            break;
                        case "md":
                        case "markdown":
                            var mdPath = baseName + ".md";
                            await MarkdownReporter.WriteAsync(result, mdPath, cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] Markdown → {Markup.Escape(mdPath)}");
                            break;
                        case "html":
                            var htmlPath = baseName + ".html";
                            await HtmlReporter.WriteAsync(result, htmlPath, cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] HTML → {Markup.Escape(htmlPath)}");
                            break;
                        case "pdf":
                            var pdfPath = baseName + ".pdf";
                            PdfReporter.Write(result, pdfPath, cancellationToken);
                            AnsiConsole.MarkupLine($"  [green]✓[/] PDF → {Markup.Escape(pdfPath)}");
                            break;
                        default:
                            AnsiConsole.MarkupLine($"  [yellow]Unknown format:[/] {Markup.Escape(fmt)}");
                            break;
                    }
                }

                PrintSummary(result);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("\n[yellow]Canceled.[/]");
                Environment.ExitCode = 130; // conventional exit code for interrupted (Ctrl+C)
            }
        });

        return cmd;
    }

    private static void PrintSummary(MemDumpAnalyzer.Core.Models.AnalysisResult r)
    {
        AnsiConsole.WriteLine();
        var table = new Spectre.Console.Table()
            .AddColumn("Finding")
            .AddColumn("Severity")
            .AddColumn("Summary")
            .Border(Spectre.Console.TableBorder.Rounded);

        foreach (var f in r.Findings.Take(10))
        {
            string severity = f.Severity switch
            {
                MemDumpAnalyzer.Core.Models.Severity.Critical => "[red bold]CRITICAL[/]",
                MemDumpAnalyzer.Core.Models.Severity.Warning => "[yellow]WARNING[/]",
                _ => "[blue]INFO[/]"
            };
            string summary = f.Summary.Length > 70 ? f.Summary[..67] + "..." : f.Summary;
            table.AddRow(Markup.Escape(f.Id), severity, Markup.Escape(summary));
        }

        if (r.Findings.Count == 0)
            AnsiConsole.MarkupLine("[green bold]No findings — heap looks healthy![/]");
        else
            AnsiConsole.Write(table);

        string scoreColor = r.HealthScore >= 70 ? "green" : r.HealthScore >= 40 ? "yellow" : "red";
        AnsiConsole.MarkupLine($"\nHealth score: [{scoreColor} bold]{r.HealthScore}/100[/]");
    }
}
