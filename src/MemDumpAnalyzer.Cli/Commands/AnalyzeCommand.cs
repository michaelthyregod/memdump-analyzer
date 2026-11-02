using System.CommandLine;
using MemDumpAnalyzer.Cli.Settings;
using MemDumpAnalyzer.Core;
using MemDumpAnalyzer.Reporting;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli.Commands;

public static class AnalyzeCommand
{
    public static Command Build()
    {
        var dumpArg = new Argument<string>("dump")
        {
            Description = "The .dmp file: a path, or a name found in the dumpsDirs setting or mem-dumps\\ next to the exe"
        };

        var outputOpt = new Option<DirectoryInfo?>("--output", "-o") { Description = "Output folder. Default: the outputDir setting, else .\\report" };
        var formatOpt = new Option<string?>("--format", "-f")
        {
            Description = $"Comma-separated output formats: html, md, json, pdf. Default: the formats setting, else {Locations.DefaultFormats}"
        };
        var topOpt = new Option<int?>("--top")
        {
            Description = $"Number of heap types in the reports. Default: the top setting, else {Locations.DefaultTop}"
        };
        var knownAssembliesOpt = new Option<FileInfo?>("--known-assemblies", "-k")
        {
            Description = "Assembly filter file (which assemblies count as framework). Default: the knownAssemblies setting, " +
                          "else known-assemblies.txt in the current folder, else next to the exe."
        };

        var skinOptions = new SkinOptions();

        var cmd = new Command("analyze", "Analyze a single memory dump");
        cmd.Add(dumpArg);
        cmd.Add(outputOpt);
        cmd.Add(formatOpt);
        cmd.Add(topOpt);
        cmd.Add(knownAssembliesOpt);
        skinOptions.AddTo(cmd);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var settings = Locations.LoadSettings();
            if (settings == null) return;

            var dumpGiven  = parseResult.GetValue(dumpArg)!;
            var outputDir  = Locations.OutputDir(parseResult.GetValue(outputOpt), settings);
            var formats    = Locations.Formats(parseResult.GetValue(formatOpt), settings);
            var topN       = Locations.Top(parseResult.GetValue(topOpt), settings);
            var knownFile  = Locations.FindKnownAssemblies(parseResult.GetValue(knownAssembliesOpt)?.FullName, settings);

            var dumpFile = Locations.FindDump(dumpGiven, settings);
            if (dumpFile == null)
            {
                AnsiConsole.MarkupLine($"[red]Dump file not found:[/] {Markup.Escape(dumpGiven)}");
                if (!Path.IsPathRooted(dumpGiven))
                    AnsiConsole.MarkupLine("  [grey]Also looked in:[/] " + Markup.Escape(string.Join("; ", Locations.DumpSearchFolders(settings))));
                Environment.ExitCode = 1;
                return;
            }

            outputDir.Create();

            AnsiConsole.MarkupLine($"[bold blue]memdump-analyzer[/] — analyzing [yellow]{Markup.Escape(dumpFile.Name)}[/]");
            var skin = skinOptions.Load(parseResult, settings);
            if (skin == null) return;
            if (knownFile != null)
                AnsiConsole.MarkupLine($"  [grey]Assembly filter:[/] {Markup.Escape(knownFile)}");
            else
                AnsiConsole.MarkupLine("  [yellow]⚠ No known-assemblies.txt found: the application hotspots section will be empty.[/]");

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
                    try
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
                                await MarkdownReporter.WriteAsync(result, mdPath, cancellationToken, skin);
                                AnsiConsole.MarkupLine($"  [green]✓[/] Markdown → {Markup.Escape(mdPath)}");
                                break;
                            case "html":
                                var htmlPath = baseName + ".html";
                                await HtmlReporter.WriteAsync(result, htmlPath, cancellationToken, skin);
                                AnsiConsole.MarkupLine($"  [green]✓[/] HTML → {Markup.Escape(htmlPath)}");
                                break;
                            case "pdf":
                                var pdfPath = baseName + ".pdf";
                                var fontWarnings = PdfReporter.Write(result, pdfPath, cancellationToken, skin);
                                AnsiConsole.MarkupLine($"  [green]✓[/] PDF → {Markup.Escape(pdfPath)}");
                                SkinOptions.PrintWarnings(fontWarnings);
                                break;
                            default:
                                AnsiConsole.MarkupLine($"  [yellow]Unknown format:[/] {Markup.Escape(fmt)}");
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // One failing format must not lose the others (or the summary)
                        AnsiConsole.MarkupLine($"  [red]✗ {Markup.Escape(fmt)} report failed:[/] {Markup.Escape(ex.Message)}");
                        Environment.ExitCode = 1;
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

        if (!r.HeapWalkComplete)
            AnsiConsole.MarkupLine("[yellow]⚠ Dump captured mid-GC: heap statistics are partial.[/]");
        if (r.ThreadPool is { } tp)
            AnsiConsole.MarkupLine($"Threads: {r.Threads.Count(t => t.IsAlive)} live  |  Thread pool: {tp.ActiveWorkerThreads} active, {tp.IdleWorkerThreads} idle  |  CPU {tp.CpuUtilization}%");

        string scoreColor = r.HealthScore >= 70 ? "green" : r.HealthScore >= 40 ? "yellow" : "red";
        AnsiConsole.MarkupLine($"\nHealth score: [{scoreColor} bold]{r.HealthScore}/100[/]");
    }
}
