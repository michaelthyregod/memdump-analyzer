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
               onPrimary = skin.Color("onPrimary"), critical = skin.Color("critical"), subtle = skin.Color("subtle");

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
                            if (logo.IsSvg) logoBox.Svg(SvgStyleInliner.Inline(System.Text.Encoding.UTF8.GetString(logo.Data)));   // CSS-class colours → attributes (QuestPDF ignores <style>)
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
                    var sections = ReportLayout.SectionsFor(r);
                    var present = sections.Select(s => s.Key).ToHashSet();
                    int number = 0;

                    foreach (var section in sections)
                    {
                        number++;
                        // Heading = link target; a small link back to the contents on the right
                        col.Item().Section(section.Anchor).PaddingTop(number == 1 ? 0 : 6).Row(row =>
                        {
                            row.RelativeItem().Text($"{number}. {skin.Heading(section.Key)}")
                                .FontSize(14).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                            if (section.Key != "executive_summary")
                                row.AutoItem().AlignBottom().SectionLink(ReportLayout.ContentsAnchor)
                                    .Text($"↑ {skin.Heading("contents")}").FontSize(8).FontColor(subtle);
                        });

                        switch (section.Key)
                        {
                            case "executive_summary": Summary(col); Contents(col); break;
                            case "findings": Findings(col); break;
                            case "thread_analysis": Threads(col); break;
                            case "application_code_hotspots": Hotspots(col); break;
                            case "memory_overview": MemoryOverview(col); break;
                            case "top_memory_consumers": TopConsumers(col); break;
                            case "retention_paths": RetentionPaths(col, r.Retention!); break;
                            case "duplicate_strings": DuplicateStrings(col); break;
                            case "gc_health": GcHealth(col); break;
                            case "findings_detail": FindingsDetail(col); break;
                        }
                    }

                    void Summary(ColumnDescriptor c)
                    {
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); });
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
                    }

                    // Clickable contents with page numbers (QuestPDF sections)
                    void Contents(ColumnDescriptor c)
                    {
                        c.Item().Section(ReportLayout.ContentsAnchor).PaddingTop(6).Background(skin.Color("stripe")).Padding(8).Column(toc =>
                        {
                            toc.Spacing(2);
                            toc.Item().Text(skin.Heading("contents")).FontSize(11).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                            int n = 0;
                            foreach (var s in sections)
                            {
                                n++;
                                TocLine(toc, $"{n}. {skin.Heading(s.Key)}", s.Anchor, indent: 0);
                                if (s.Key == "findings")
                                    for (int i = 0; i < r.Findings.Count; i++)
                                        TocLine(toc, $"[{r.Findings[i].Severity.ToString().ToUpper()}] {r.Findings[i].Summary}", ReportLayout.FindingAnchor(i), indent: 14);
                            }
                        });
                    }

                    void TocLine(ColumnDescriptor toc, string text, string anchor, float indent)
                    {
                        toc.Item().PaddingLeft(indent).SectionLink(anchor).Row(row =>
                        {
                            row.RelativeItem().Text(text).FontSize(indent > 0 ? 8 : 9).FontColor(accent);
                            row.ConstantItem(30).AlignRight().Text(t => t.BeginPageNumberOfSection(anchor).FontSize(8).FontColor(subtle));
                        });
                    }

                    void Findings(ColumnDescriptor c)
                    {
                        if (r.Findings.Count == 0)
                        {
                            c.Item().Text("No significant findings — heap looks healthy!").FontColor(skin.Color("success")).Bold();
                            return;
                        }
                        for (int i = 0; i < r.Findings.Count; i++)
                        {
                            var f = r.Findings[i];
                            var index = i;
                            c.Item().Section(ReportLayout.FindingAnchor(i)).Background(SeverityBackground(skin, f.Severity))
                                .BorderLeft(3).BorderColor(SeverityColor(skin, f.Severity)).Padding(6).Column(fc =>
                            {
                                fc.Item().Text($"[{f.Severity.ToString().ToUpper()}] {f.Summary}").Bold();
                                if (f.Recommendation != null)
                                    fc.Item().PaddingTop(3).Text($"Recommendation: {f.Recommendation}").FontSize(9).Italic().FontColor(accent);
                                Links(fc, f, index, detail: false);
                            });
                        }
                    }

                    void Threads(ColumnDescriptor c)
                    {
                        var alive = r.Threads.Where(t => t.IsAlive).ToList();
                        c.Item().Text($"Live: {alive.Count}  |  Dead (not analyzed): {r.Threads.Count - alive.Count}  |  Waiting/blocked: {alive.Count(t => t.BlockingReason != null)}");
                        if (r.ThreadPool is { } tp)
                            c.Item().Text($"Thread pool: active {tp.ActiveWorkerThreads}, idle {tp.IdleWorkerThreads}, min {tp.MinThreads}, max {tp.MaxThreads}  |  CPU at capture (system-wide): {tp.CpuUtilization}%");
                        c.Item().PaddingTop(4).Text(skin.Heading("thread_patterns")).FontSize(11).FontFamily(fonts.Heading).HeadingWeight(skin).FontColor(primary);
                        for (int i = 0; i < Math.Min(r.ThreadGroups.Count, 20); i++)
                        {
                            var g = r.ThreadGroups[i];
                            var topFrame = g.RepresentativeStack.FirstOrDefault(f => !f.StartsWith('[')) ?? g.RepresentativeStack.FirstOrDefault() ?? "";
                            c.Item().Section(ReportLayout.PatternAnchor(i))
                                .Text($"#{i + 1}  {g.ThreadCount,5:N0} threads ({g.Percentage:F1}%) — {g.WaitReason ?? "running"} — {topFrame}").FontSize(8);
                        }
                    }

                    void Hotspots(ColumnDescriptor c)
                    {
                        c.Item().Text(
                            $"Assemblies in thread stacks not matching any of the {r.KnownAssemblyFilters.Count} known-assembly filter prefix(es). " +
                            "These are likely your own code contributing to the problems above.")
                            .FontSize(9).FontColor(muted);
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(3);
                                cd.RelativeColumn(1);
                                cd.RelativeColumn(1);
                                cd.RelativeColumn(4);
                                cd.RelativeColumn(2);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Background(primary).Padding(4).Text("Assembly").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Threads").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Blocked").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Top Method").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Patterns").FontColor(onPrimary).Bold();
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
                                // Only patterns shown in the PDF (the first 20) can be linked
                                var patterns = ReportLayout.PatternsWithAssembly(r, h.Assembly).Where(i => i < 20).ToList();
                                t.Cell().Padding(3).Row(pr =>
                                {
                                    foreach (var p in patterns.Take(6))
                                        pr.AutoItem().PaddingRight(4).SectionLink(ReportLayout.PatternAnchor(p)).Text($"#{p + 1}").FontSize(8).FontColor(accent);
                                });
                            }
                        });
                    }

                    void MemoryOverview(ColumnDescriptor c)
                    {
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); });
                            AddRow(t, "Total heap", FormatBytes(r.GcStats.TotalHeapBytes));
                            AddRow(t, "Gen0", FormatBytes(r.GcStats.Gen0Bytes));
                            AddRow(t, "Gen1", FormatBytes(r.GcStats.Gen1Bytes));
                            AddRow(t, "Gen2", FormatBytes(r.GcStats.Gen2Bytes));
                            AddRow(t, "LOH", FormatBytes(r.GcStats.LohBytes));
                            AddRow(t, "LOH fragmentation", $"{r.GcStats.LohFragmentationPercent:F1}% ({FormatBytes(r.GcStats.LohFreeBytes)} free)");
                            AddRow(t, "Gen2 fragmentation", $"{r.GcStats.Gen2FragmentationPercent:F1}% ({FormatBytes(r.GcStats.Gen2FreeBytes)} free)");
                        });
                    }

                    void RetentionPaths(ColumnDescriptor c, RetentionReport retention)
                    {
                        c.Item().Text(ReportLayout.RetentionIntro(retention)).FontSize(9).FontColor(muted);
                        foreach (var p in retention.Paths)
                        {
                            c.Item().PaddingTop(4).Text(t =>
                            {
                                t.Span(p.TypeName).FontFamily(fonts.Mono).FontSize(8).Bold();
                                t.Span($" — {FormatBytes(p.SizeBytes)} — held by ").FontSize(8);
                                t.Span(p.Root).FontFamily(fonts.Mono).FontSize(8);
                            });
                            c.Item().Background(skin.Color("codeBackground")).Padding(5)
                                .Text(string.Join('\n', p.Describe()))
                                .FontSize(7).FontColor(skin.Color("codeText")).FontFamily(fonts.Mono);
                        }
                        if (retention.NotFound.Count > 0)
                        {
                            c.Item().PaddingTop(6).Text(ReportLayout.RetentionNotFoundText(retention)).FontSize(9);
                            c.Item().Table(t =>
                            {
                                t.ColumnsDefinition(cd =>
                                {
                                    cd.RelativeColumn(5);
                                    cd.RelativeColumn(1.5f);
                                    cd.RelativeColumn(2);
                                });
                                t.Header(h =>
                                {
                                    h.Cell().Background(primary).Padding(4).Text("Type").FontColor(onPrimary).Bold();
                                    h.Cell().Background(primary).Padding(4).Text("Size").FontColor(onPrimary).Bold();
                                    h.Cell().Background(primary).Padding(4).Text("Address").FontColor(onPrimary).Bold();
                                });
                                foreach (var nf in retention.NotFound)
                                {
                                    t.Cell().Padding(3).Text(nf.TypeName).FontSize(8).FontFamily(fonts.Mono);
                                    t.Cell().Padding(3).Text(FormatBytes(nf.SizeBytes)).FontSize(8);
                                    t.Cell().Padding(3).Text(nf.Address.ToString("x16")).FontSize(8).FontFamily(fonts.Mono);
                                }
                            });
                        }
                    }

                    void TopConsumers(ColumnDescriptor c)
                    {
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(4);
                                cd.RelativeColumn(2);
                                cd.RelativeColumn(2);
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
                    }

                    void DuplicateStrings(ColumnDescriptor c)
                    {
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(6);
                                cd.RelativeColumn(1.5f);
                                cd.RelativeColumn(1.5f);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Background(primary).Padding(4).Text("Value (truncated)").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Instances").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Wasted").FontColor(onPrimary).Bold();
                            });
                            foreach (var s in r.DuplicateStrings.Take(20))
                            {
                                t.Cell().Padding(3).Text(s.Value).FontSize(7).FontFamily(fonts.Mono);
                                t.Cell().Padding(3).Text($"{s.InstanceCount:N0}").FontSize(8);
                                t.Cell().Padding(3).Text(FormatBytes(s.TotalWastedBytes)).FontSize(8);
                            }
                        });
                    }

                    void GcHealth(ColumnDescriptor c)
                    {
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(2);
                                cd.RelativeColumn(2);
                                cd.RelativeColumn(2);
                                cd.RelativeColumn(1.5f);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Background(primary).Padding(4).Text("Kind").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Committed").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Free objects").FontColor(onPrimary).Bold();
                                h.Cell().Background(primary).Padding(4).Text("Fill %").FontColor(onPrimary).Bold();
                            });
                            foreach (var seg in r.GcStats.Segments.Take(40))
                            {
                                t.Cell().Padding(3).Text(seg.Kind).FontSize(8);
                                t.Cell().Padding(3).Text(FormatBytes(seg.CommittedBytes)).FontSize(8);
                                t.Cell().Padding(3).Text(FormatBytes(seg.FreeBytes)).FontSize(8);
                                t.Cell().Padding(3).Text($"{seg.FillRatio * 100:F1}%").FontSize(8);
                            }
                        });
                    }

                    void FindingsDetail(ColumnDescriptor c)
                    {
                        for (int i = 0; i < r.Findings.Count; i++)
                        {
                            var f = r.Findings[i];
                            var index = i;
                            c.Item().Section(ReportLayout.FindingDetailAnchor(i)).Background(SeverityBackground(skin, f.Severity))
                                .BorderLeft(3).BorderColor(SeverityColor(skin, f.Severity)).Padding(6).Column(fc =>
                            {
                                fc.Item().Text($"[{f.Severity.ToString().ToUpper()}] {f.Id}").FontSize(9).FontColor(muted);
                                fc.Item().Text(f.Summary).Bold();
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
                                Links(fc, f, index, detail: true);
                            });
                        }
                    }

                    // Short entry → its details (and back), plus the section whose data backs the finding up
                    void Links(ColumnDescriptor fc, Finding f, int index, bool detail)
                    {
                        fc.Item().PaddingTop(3).Row(row =>
                        {
                            if (detail)
                                row.AutoItem().SectionLink(ReportLayout.FindingAnchor(index)).Text($"↑ {skin.Heading("findings")}").FontSize(8).FontColor(accent);
                            else
                                row.AutoItem().SectionLink(ReportLayout.FindingDetailAnchor(index)).Text("Full explanation, evidence and technical details ↓").FontSize(8).FontColor(accent);
                            if (ReportLayout.RelatedSection(f.Id) is { } key && present.Contains(key))
                            {
                                row.AutoItem().PaddingHorizontal(6).Text("·").FontSize(8).FontColor(subtle);
                                row.AutoItem().SectionLink(ReportLayout.Anchor(key)).Text(skin.Heading(key)).FontSize(8).FontColor(accent);
                            }
                        });
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
