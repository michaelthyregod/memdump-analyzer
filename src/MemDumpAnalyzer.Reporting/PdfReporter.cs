using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Reporting;

public static class PdfReporter
{
    public static void Write(AnalysisResult r, string outputPath)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Text("Memory Dump Analysis Report")
                        .FontSize(20).Bold().FontColor(Colors.Blue.Darken3);
                    col.Item().Text($"Dump: {Path.GetFileName(r.DumpPath)}  |  CLR: {r.ClrVersion}  |  Health: {r.HealthScore}/100")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    // Executive summary
                    col.Item().Text("1. Executive Summary").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        AddRow(t, "Capture time", r.CaptureTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
                        AddRow(t, "Dump size", FormatBytes(r.DumpFileSizeBytes));
                        AddRow(t, "AppDomain", r.AppDomainName);
                        AddRow(t, "OS", r.OsVersion);
                        AddRow(t, "Health score", $"{r.HealthScore}/100");
                    });

                    // Findings
                    col.Item().Text("2. Findings").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                    foreach (var f in r.Findings.Take(15))
                    {
                        col.Item().Background(SeverityBackground(f.Severity)).Padding(6).Column(fc =>
                        {
                            fc.Item().Text($"[{f.Severity.ToString().ToUpper()}] {f.Summary}").Bold();
                            fc.Item().Text(f.Explanation).FontSize(9).FontColor(Colors.Grey.Darken2);
                            if (f.Evidence != null)
                                fc.Item().Text($"Evidence: {f.Evidence}").FontSize(8).FontColor(Colors.Grey.Darken1);
                            if (f.Recommendation != null)
                                fc.Item().PaddingTop(3).Text($"Recommendation: {f.Recommendation}").FontSize(9).Italic().FontColor(Colors.Blue.Darken2);
                            if (f.TechnicalDetails != null && f.TechnicalDetails.Count > 0)
                            {
                                fc.Item().PaddingTop(4)
                                    .Background(Colors.Grey.Darken4).Padding(5)
                                    .Text(string.Join("\n", f.TechnicalDetails))
                                    .FontSize(7).FontColor(Colors.Grey.Lighten3)
                                    .FontFamily("Courier New");
                            }
                        });
                    }

                    // Memory overview
                    col.Item().Text("3. Memory Overview").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        AddRow(t, "Total heap", FormatBytes(r.GcStats.TotalHeapBytes));
                        AddRow(t, "Gen0", FormatBytes(r.GcStats.Gen0Bytes));
                        AddRow(t, "Gen1", FormatBytes(r.GcStats.Gen1Bytes));
                        AddRow(t, "Gen2", FormatBytes(r.GcStats.Gen2Bytes));
                        AddRow(t, "LOH", FormatBytes(r.GcStats.LohBytes));
                        AddRow(t, "LOH fragmentation", $"{r.GcStats.LohFragmentationPercent:F1}%");
                    });

                    // Top consumers
                    col.Item().Text("4. Top Memory Consumers").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });
                        t.Header(h =>
                        {
                            h.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Type").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Instances").FontColor(Colors.White).Bold();
                            h.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Size").FontColor(Colors.White).Bold();
                        });
                        foreach (var ht in r.HeapTypes.Take(25))
                        {
                            t.Cell().Padding(3).Text(ht.TypeName).FontSize(8);
                            t.Cell().Padding(3).Text($"{ht.InstanceCount:N0}").FontSize(8);
                            t.Cell().Padding(3).Text(FormatBytes(ht.TotalSizeBytes)).FontSize(8);
                        }
                    });

                    // Application hotspots
                    if (r.ApplicationHotspots.Count > 0)
                    {
                        col.Item().Text("5. Application Code Hotspots").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                        col.Item().Text(
                            $"Assemblies in thread stacks not matching any of the {r.KnownAssemblyFilters.Count} known-assembly filter prefix(es). " +
                            "These are likely your own code contributing to the problems above.")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(5);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Assembly").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Threads").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Blocked").FontColor(Colors.White).Bold();
                                h.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Top Method").FontColor(Colors.White).Bold();
                            });
                            foreach (var h in r.ApplicationHotspots.Take(20))
                            {
                                t.Cell().Padding(3).Text(h.Assembly).FontSize(8).FontFamily("Courier New");
                                t.Cell().Padding(3).Text($"{h.ThreadCount:N0}").FontSize(8);
                                var blockedCell = t.Cell().Padding(3);
                                if (h.BlockedThreadCount > 0)
                                    blockedCell.Background(Colors.Red.Lighten4).Text($"{h.BlockedThreadCount:N0}").FontSize(8).Bold().FontColor(Colors.Red.Darken2);
                                else
                                    blockedCell.Text("0").FontSize(8);
                                t.Cell().Padding(3).Text(h.TopMethods.Count > 0 ? h.TopMethods[0] : "").FontSize(7).FontFamily("Courier New").FontColor(Colors.Grey.Darken2);
                            }
                        });
                    }

                    // Threads
                    col.Item().Text("6. Thread Summary").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Total: {r.Threads.Count}  |  Blocked: {r.Threads.Count(t => t.BlockingReason != null)}");
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("memdump-analyzer  |  page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static void AddRow(TableDescriptor t, string label, string value)
    {
        t.Cell().Padding(3).Background(Colors.Grey.Lighten4).Text(label).Bold().FontSize(9);
        t.Cell().Padding(3).Text(value).FontSize(9);
    }

    private static string SeverityBackground(Severity s) => s switch
    {
        Severity.Critical => Colors.Red.Lighten4,
        Severity.Warning => Colors.Yellow.Lighten4,
        _ => Colors.Blue.Lighten5
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
