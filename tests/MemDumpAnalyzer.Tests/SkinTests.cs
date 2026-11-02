using MemDumpAnalyzer.Core.Models;
using MemDumpAnalyzer.Reporting;
using MemDumpAnalyzer.Reporting.Skins;

namespace MemDumpAnalyzer.Tests;

public class SkinTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"memdump-analyzer-skin-{Guid.NewGuid():N}");

    public SkinTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteSkin(string json, params (string Name, byte[] Data)[] files)
    {
        File.WriteAllText(Path.Combine(_dir, "skin.json"), json);
        foreach (var (name, data) in files)
        {
            var path = Path.Combine(_dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }
        return _dir;
    }

    private static byte[] BuiltInFont(string family, int weight = 400) =>
        SkinLoader.Load("northwind").FontFaces.Concat(Skin.Default.FontFaces)
            .First(f => f.Family == family && f.Weight == weight).Data;

    private static AnalysisResult Result() => new(
        DumpPath: @"C:\dumps\w3wp.dmp",
        CaptureTime: new DateTime(2026, 3, 1, 12, 23, 6, DateTimeKind.Utc),
        DumpFileSizeBytes: 1024,
        ClrVersion: "4.8.9315.0",
        AppDomainName: "DefaultDomain",
        OsVersion: "Windows",
        Threads: [],
        ThreadGroups: [],
        HeapTypes: [new HeapTypeStats("System.String", 10, 1024, 1024, 0)],
        GcStats: new GcStats(0, 0, 0, 0, 0, 0, 0, []),
        DuplicateStrings: [],
        LeakSuspects: [],
        ApplicationHotspots: [],
        KnownAssemblyFilters: [],
        Findings: [new Finding("LockContention", Severity.Critical, "Many threads wait", "Explanation", "Evidence", "Fix it")],
        HealthScore: 70);

    // ── Loading ──────────────────────────────────────────────────────────────

    [Fact]
    public void Default_IsTheOriginalLook()
    {
        var skin = Skin.Default;

        Assert.Equal("Memory Dump Analysis Report", skin.ReportTitle);
        Assert.Equal(SkinColors.Defaults, skin.Colors);
        Assert.Equal(["Arial"], skin.BodyFonts);
        Assert.Equal(["Courier New"], skin.MonoFonts);
        Assert.Null(skin.Logo);
        Assert.Empty(skin.Warnings);
        Assert.Equal(6, skin.FontFaces.Count(f => f.BuiltIn));   // Liberation Sans x4, Liberation Mono x2
    }

    [Fact]
    public void Northwind_LoadsTextsLogoColoursAndItsOwnFont()
    {
        var skin = SkinLoader.Load("Northwind");

        Assert.Empty(skin.Warnings);
        Assert.Equal("Memory Dump Health Report", skin.ReportTitle);
        Assert.Equal("#1f4e5f", skin.Color("primary"));
        Assert.True(skin.Logo!.IsSvg);
        Assert.Contains(skin.FontFaces, f => !f.BuiltIn && f.Family == "Liberation Serif" && f.Weight == 700);
    }

    [Fact]
    public void FolderSkin_FillsGapsFromDefaultAndWarnsAboutProblems()
    {
        var dir = WriteSkin("""
            {
              // comments and trailing commas are allowed
              "name": "acme",
              "texts": { "reportTitle": "ACME Report", },
              "colors": { "primary": "#AA0000", "nope": "#000000", "accent": "red" },
              "fonts": { "body": [ "ACME Sans", "Lato" ] },
              "fontFiles": [
                { "family": "ACME Sans", "file": "fonts/missing.ttf" },
                { "family": "Escape", "file": "../outside.ttf" }
              ],
              "logo": "logo.gif"
            }
            """);

        var skin = SkinLoader.Load(dir);

        Assert.Equal("acme", skin.Name);
        Assert.Equal("ACME Report", skin.ReportTitle);
        Assert.Equal("#aa0000", skin.Color("primary"));
        Assert.Equal(SkinColors.Defaults["accent"], skin.Color("accent"));
        Assert.Equal(SkinColors.Defaults["background"], skin.Color("background"));
        Assert.Equal(["ACME Sans", "Lato"], skin.BodyFonts);
        Assert.Equal(skin.BodyFonts, skin.HeadingFonts);          // heading defaults to body
        Assert.Contains(skin.Warnings, w => w.Contains("'nope'"));
        Assert.Contains(skin.Warnings, w => w.Contains("'accent'"));
        Assert.Contains(skin.Warnings, w => w.Contains("missing.ttf"));
        Assert.Contains(skin.Warnings, w => w.Contains("outside.ttf"));   // never read outside the skin folder
        Assert.Contains(skin.Warnings, w => w.Contains("logo.gif"));
        Assert.DoesNotContain(skin.FontFaces, f => !f.BuiltIn);
    }

    [Fact]
    public void HeadingCase_DefaultsToTitleAndWarnsOnUnknownValue()
    {
        Assert.Equal(HeadingCase.Title, Skin.Default.HeadingCase);

        var skin = SkinLoader.Load(WriteSkin("""{ "headingCase": "shouting" }"""));

        Assert.Equal(HeadingCase.Title, skin.HeadingCase);
        Assert.Contains(skin.Warnings, w => w.Contains("shouting"));
    }

    [Fact]
    public void HeadingCase_Sentence_AppliesToEveryReport()
    {
        var skin = SkinLoader.Load(WriteSkin("""{ "headingCase": "sentence" }"""));
        var result = Result();

        var html = HtmlReporter.Render(result, skin);
        var md = MarkdownReporter.Render(result, skin);
        var diff = new DiffResult(0, 0, 0, 0, 0, 0, [], [], [], [], []);
        var diffHtml = DiffReporter.RenderHtml(diff, "a.dmp", "b.dmp", 10, skin);
        var diffMd = DiffReporter.RenderMarkdown(diff, "a.dmp", "b.dmp", 10, skin);

        Assert.Contains("<h2>Top memory consumers</h2>", html);
        Assert.Contains("<h2>GC health</h2>", html);                // acronym keeps its capitals
        Assert.DoesNotContain(">Top Memory Consumers<", html);   // (the template's HTML comments may keep title case)
        Assert.Contains("## 3. Top memory consumers", md);
        Assert.Contains("## 1. Executive summary", md);
        Assert.Contains("<h2>Growing types (top 10)</h2>", diffHtml);
        Assert.Contains("## New types", diffMd);
    }

    [Fact]
    public void HeadingCase_EveryHeadingHasBothForms()
    {
        foreach (var key in ReportHeadings.Keys)
        {
            var title = ReportHeadings.Get(key, HeadingCase.Title);
            var sentence = ReportHeadings.Get(key, HeadingCase.Sentence);
            Assert.Equal(title, sentence, StringComparer.OrdinalIgnoreCase);   // same words, only the case differs
            Assert.True(char.IsUpper(sentence[0]), key);
        }
    }

    [Fact]
    public void Load_UnknownSkin_Throws()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => SkinLoader.Load(Path.Combine(_dir, "nope")));
        Assert.Contains("northwind", ex.Message);
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        var dir = WriteSkin("{ not json");
        Assert.Throws<InvalidDataException>(() => SkinLoader.Load(dir));
    }

    // ── Fonts ────────────────────────────────────────────────────────────────

    [Fact]
    public void PdfFonts_UseTheFamilyNameStoredInTheSkinsFontFile()
    {
        var dir = WriteSkin("""
            {
              "fonts": { "heading": [ "Brand Serif" ], "body": [ "Font That Does Not Exist" ] },
              "fontFiles": [ { "family": "Brand Serif", "file": "fonts/brand.ttf", "weight": 700 } ]
            }
            """, ("fonts/brand.ttf", BuiltInFont("Liberation Serif", 700)));

        var fonts = SkinFontResolver.ForPdf(SkinLoader.Load(dir));

        Assert.Equal(["Liberation Serif", Skin.BuiltInSansFallback], fonts.Heading);   // the file's own family name
        Assert.Equal([Skin.BuiltInSansFallback], fonts.Body);                           // missing → fallback only
        Assert.Contains(fonts.Warnings, w => w.Contains("Font That Does Not Exist"));
    }

    [Fact]
    public void CssStack_EndsWithBuiltInFallbackAndGenericFamily()
    {
        Assert.Equal("'Brand Sans', 'Liberation Sans', sans-serif", SkinFontResolver.CssStack(["Brand Sans"], mono: false));
        Assert.Equal("'Liberation Mono', monospace", SkinFontResolver.CssStack(["Liberation Mono"], mono: true));
    }

    [Fact]
    public void SkinCss_HasKebabCaseVariablesAndEmbedsOnlyTheSkinsOwnFonts()
    {
        var css = SkinCss.Build(SkinLoader.Load("northwind"));

        Assert.Contains("--primary:#1f4e5f;", css);
        Assert.Contains("--critical-background:#fbf0ee;", css);
        Assert.Contains("@font-face{font-family:'Liberation Serif'", css);
        Assert.DoesNotContain("font-family:'Liberation Sans'", css);   // built-in fallbacks are not embedded
    }

    // ── Reports ──────────────────────────────────────────────────────────────

    [Fact]
    public void Html_AppliesSkinTextsLogoAndEscapesPreparedFor()
    {
        var skin = SkinLoader.Load("northwind").WithPreparedFor("<Customer & Co>");

        var html = HtmlReporter.Render(Result(), skin);

        Assert.Contains("Memory Dump Health Report", html);
        Assert.Contains("--primary:#1f4e5f", html);
        Assert.Contains("data:image/svg+xml;base64,", html);
        Assert.Contains("&lt;Customer &amp; Co&gt;", html);
        Assert.Contains("<footer>Northwind — reference skin &#183; memdump-analyzer</footer>", html);
        Assert.DoesNotContain("#1b4f72", html);   // no default colour leaks into a skinned report
    }

    [Fact]
    public void Markdown_UsesSkinTexts()
    {
        var md = MarkdownReporter.Render(Result(), SkinLoader.Load("northwind").WithPreparedFor("ACME"));

        Assert.StartsWith("# Memory Dump Health Report", md);
        Assert.Contains("| Prepared for | ACME |", md);
        Assert.Contains("| Prepared by | Northwind Performance Team |", md);
    }

    [Fact]
    public void Pdf_RendersWithSkinFontAndLogo()
    {
        var path = Path.Combine(_dir, "report.pdf");

        var warnings = PdfReporter.Write(Result(), path, TestContext.Current.CancellationToken, SkinLoader.Load("northwind"));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
        Assert.Contains("LiberationSerif-Bold", System.Text.Encoding.ASCII.GetString(bytes));   // the skin's heading font is embedded
        Assert.DoesNotContain(warnings, w => w.Contains("Liberation Serif"));
    }

    [Fact]
    public void DiffHtml_IsSkinnedAndEscaped()
    {
        var diff = new DiffResult(1024, 0, 0, 0, 0, 1,
            [new TypeDelta("List<MyApp.Item>", 1, 2, 1, 10, 20, 10)], [], ["Queue<MyApp.Work>"], [], []);

        var html = DiffReporter.RenderHtml(diff, "a.dmp", "b.dmp", 10, SkinLoader.Load("northwind"));

        Assert.Contains("--primary:#1f4e5f", html);
        Assert.Contains("List&lt;MyApp.Item&gt;", html);
        Assert.Contains("Queue&lt;MyApp.Work&gt;", html);
        Assert.DoesNotContain("<MyApp.", html);
    }
}
