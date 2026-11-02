using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting.Skins;

namespace MemDumpAnalyzer.Reporting;

public static class PdfReporter
{
    /// <returns>Warnings about fonts that weren't available (the PDF then uses the next font in the list).</returns>
    public static IReadOnlyList<string> Write(AnalysisResult r, string outputPath, CancellationToken cancellationToken = default, Skin? skin = null)
    {
        // QuestPDF generation itself is not cancellable; honor cancellation before starting
        cancellationToken.ThrowIfCancellationRequested();
        skin ??= Skin.Default;

        QuestPDF.Settings.License = LicenseType.Community;
        // Registers the skin's font files and keeps only families that exist (skin folder, installed,
        // or the built-in Liberation fallback); see SkinFontResolver.
        var fonts = SkinFontResolver.ForPdf(skin);

        string primary = skin.Color("primary"), accent = skin.Color("accent"), muted = skin.Color("muted"),
               onPrimary = skin.Color("onPrimary"), critical = skin.Color("critical");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(fonts.Body).FontColor(skin.Color("text")));
                page.PageColor(skin.Color("surface"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        if (skin.Logo is { } logo)
                        {
                            // Width from the logo's own proportions — never distorted; half the height as clear space
                            float h = (float)skin.LogoHeightMm * MmToPt;
                            var logoBox = row.ConstantItem(h * (float)logo.AspectRatio + h / 2).PaddingRight(h / 2).Height(h);
                            if (logo.IsSvg) logoBox.Svg(System.Text.Encoding.UTF8.GetString(logo.Data));
                            else logoBox.Image(logo.Data);
                        }
                        row.RelativeItem().Column(title =>
                        {
                            title.Item().Text(skin.ReportTitle)
                                .FontSize(20).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                            if (skin.CompanyName != null)
                                title.Item().Text(skin.CompanyName).FontSize(9).FontColor(muted);
                        });
                    });
                    col.Item().Text($"Dump: {Path.GetFileName(r.DumpPath)}  |  CLR: {r.ClrVersion}  |  Health: {r.HealthScore}/100")
                        .FontSize(9).FontColor(muted);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(skin.Color("border"));
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    // Executive summary
                    col.Item().Text($"1. {skin.Heading("executive_summary")}").FontSize(14).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        AddRow(t, "Capture time", r.CaptureTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
                        AddRow(t, "Dump size", FormatBytes(r.DumpFileSizeBytes));
                        AddRow(t, "AppDomain", r.AppDomainName);
                        AddRow(t, "OS", r.OsVersion);
                        AddRow(t, "Health score", $"{r.HealthScore}/100");
                        if (skin.PreparedFor != null) AddRow(t, "Prepared for", skin.PreparedFor);
                        if (skin.PreparedBy != null) AddRow(t, "Prepared by", skin.PreparedBy);
                        if (!r.HeapWalkComplete)
                            AddRow(t, "Heap data", "Incomplete — dump captured while a GC was running");
                    });

                    // Findings
                    col.Item().Text($"2. {skin.Heading("findings")}").FontSize(14).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                    foreach (var f in r.Findings.Take(15))
                    {
                        col.Item().Background(SeverityBackground(skin, f.Severity)).BorderLeft(3).BorderColor(SeverityColor(skin, f.Severity)).Padding(6).Column(fc =>
                        {
                            fc.Item().Text($"[{f.Severity.ToString().ToUpper()}] {f.Summary}").Bold();
                            fc.Item().Text(f.Explanation).FontSize(9).FontColor(muted);
                            if (f.Evidence != null)
                                fc.Item().Text($"Evidence: {f.Evidence}").FontSize(8).FontColor(muted);
                            if (f.Recommendation != null)
                                fc.Item().PaddingTop(3).Text($"Recommendation: {f.Recommendation}").FontSize(9).Italic().FontColor(accent);
                            if (f.TechnicalDetails != null && f.TechnicalDetails.Count > 0)
                            {
                                fc.Item().PaddingTop(4)
                                    .Background(skin.Color("codeBackground")).Padding(5)
                                    .Text(string.Join("\n", f.TechnicalDetails))
                                    .FontSize(7).FontColor(skin.Color("codeText"))
                                    .FontFamily(fonts.Mono);
                            }
                        });
                    }

                    // Memory overview
                    col.Item().Text($"3. {skin.Heading("memory_overview")}").FontSize(14).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        AddRow(t, "Total heap", FormatBytes(r.GcStats.TotalHeapBytes));
                        AddRow(t, "Gen0", FormatBytes(r.GcStats.Gen0Bytes));
                        AddRow(t, "Gen1", FormatBytes(r.GcStats.Gen1Bytes));
                        AddRow(t, "Gen2", FormatBytes(r.GcStats.Gen2Bytes));
                        AddRow(t, "LOH", FormatBytes(r.GcStats.LohBytes));
                        AddRow(t, "LOH fragmentation", $"{r.GcStats.LohFragmentationPercent:F1}% ({FormatBytes(r.GcStats.LohFreeBytes)} free)");
                        AddRow(t, "Gen2 fragmentation", $"{r.GcStats.Gen2FragmentationPercent:F1}% ({FormatBytes(r.GcStats.Gen2FreeBytes)} free)");
                    });

                    // Top consumers
                    col.Item().Text($"4. {skin.Heading("top_memory_consumers")}").FontSize(14).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
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
                            h.Cell().Background(primary).Padding(4).Text("Type").FontColor(onPrimary).Bold();
                            h.Cell().Background(primary).Padding(4).Text("Instances").FontColor(onPrimary).Bold();
                            h.Cell().Background(primary).Padding(4).Text("Size").FontColor(onPrimary).Bold();
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
                        col.Item().Text($"5. {skin.Heading("application_code_hotspots")}").FontSize(14).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                        col.Item().Text(
                            $"Assemblies in thread stacks not matching any of the {r.KnownAssemblyFilters.Count} known-assembly filter prefix(es). " +
                            "These are likely your own code contributing to the problems above.")
                            .FontSize(9).FontColor(muted);
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
                                h.Cell().Background(primary).Padding(4).Text("Assembly").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Threads").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Blocked").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Top Method").FontColor(onPrimary).Bold();
                            });
                            foreach (var h in r.ApplicationHotspots.Take(20))
                            {
                                t.Cell().Padding(3).Text(h.Assembly).FontSize(8).FontFamily(fonts.Mono);
                                t.Cell().Padding(3).Text($"{h.ThreadCount:N0}").FontSize(8);
                                var blockedCell = t.Cell().Padding(3);
                                if (h.BlockedThreadCount > 0)
                                    blockedCell.Background(skin.Color("criticalBackground")).Text($"{h.BlockedThreadCount:N0}").FontSize(8).Bold().FontColor(critical);
                                else
                                    blockedCell.Text("0").FontSize(8);
                                t.Cell().Padding(3).Text(h.TopMethods.Count > 0 ? h.TopMethods[0] : "").FontSize(7).FontFamily(fonts.Mono).FontColor(muted);
                            }
                        });
                    }

                    // Threads
                    col.Item().Text($"6. {skin.Heading("thread_summary")}").FontSize(14).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                    var alive = r.Threads.Where(t => t.IsAlive).ToList();
                    col.Item().Text($"Live: {alive.Count}  |  Dead (not analyzed): {r.Threads.Count - alive.Count}  |  Waiting/blocked: {alive.Count(t => t.BlockingReason != null)}");
                    if (r.ThreadPool is { } tp)
                        col.Item().Text($"Thread pool: active {tp.ActiveWorkerThreads}, idle {tp.IdleWorkerThreads}, min {tp.MinThreads}, max {tp.MaxThreads}  |  CPU at capture (system-wide): {tp.CpuUtilization}%");
                    foreach (var g in r.ThreadGroups.Take(10))
                    {
                        var topFrame = g.RepresentativeStack.FirstOrDefault(f => !f.StartsWith('[')) ?? g.RepresentativeStack.FirstOrDefault() ?? "";
                        col.Item().Text($"{g.ThreadCount,5:N0} threads ({g.Percentage:F1}%) — {g.WaitReason ?? "running"} — {topFrame}").FontSize(8);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(skin.Color("subtle")));
                    t.Span($"{skin.FooterLine}  |  page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(outputPath);

        return fonts.Warnings;

        void AddRow(TableDescriptor t, string label, string value)
        {
            t.Cell().Padding(3).Background(skin.Color("stripe")).Text(label).Bold().FontSize(9);
            t.Cell().Padding(3).Text(value).FontSize(9);
        }
    }

    private const float MmToPt = 72f / 25.4f;

    private static TextBlockDescriptor HeadingWeight(this TextBlockDescriptor text, Skin skin) => skin.HeadingWeight switch
    {
        400 => text.NormalWeight(),
        600 => text.SemiBold(),
        _ => text.Bold()
    };

    private static string SeverityBackground(Skin skin, Severity s) => s switch
    {
        Severity.Critical => skin.Color("criticalBackground"),
        Severity.Warning => skin.Color("warningBackground"),
        _ => skin.Color("infoBackground")
    };

    private static string SeverityColor(Skin skin, Severity s) => s switch
    {
        Severity.Critical => skin.Color("critical"),
        Severity.Warning => skin.Color("warning"),
        _ => skin.Color("info")
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
