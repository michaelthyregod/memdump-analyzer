namespace MemDumpAnalyzer.Reporting.Skins;

/// <summary>Colour tokens every skin provides. Values are the default skin (the original report look).</summary>
public static class SkinColors
{
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["background"] = "#f5f7fa",          // page background
        ["surface"] = "#ffffff",             // cards, tables
        ["text"] = "#1a1a2e",                // body text
        ["heading"] = "#0d1b2a",             // h1, values
        ["primary"] = "#1b4f72",             // section headings, table headers, bars
        ["onPrimary"] = "#ffffff",           // text on primary / severity badges
        ["accent"] = "#1a5276",              // recommendations, links
        ["border"] = "#d6e4f0",              // heading underline, card edges
        ["divider"] = "#eef1f5",             // table rows, bar tracks
        ["stripe"] = "#f9fbfc",              // alternating table rows
        ["muted"] = "#555555",               // secondary text
        ["subtle"] = "#888888",              // footer, hints
        ["critical"] = "#e74c3c",
        ["criticalBackground"] = "#fff5f5",
        ["warning"] = "#f39c12",
        ["warningBackground"] = "#fffbf0",
        ["info"] = "#3498db",
        ["infoBackground"] = "#f0f7ff",
        ["success"] = "#27ae60",
        ["codeBackground"] = "#0d1b2a",      // technical details blocks
        ["codeHeader"] = "#1a2a3a",
        ["codeText"] = "#c8d8e8",
        ["codeAccent"] = "#7fb3d3",
        ["stackBackground"] = "#f4f4f8",     // inline stacks
    };

    public static IEnumerable<string> Keys => Defaults.Keys;
}

/// <summary>A font file ready to use: bytes plus how it is declared.</summary>
public sealed record SkinFontFace(string Family, byte[] Data, string Format, int Weight, string Style, bool BuiltIn)
{
    /// <summary>True for formats QuestPDF can load (TrueType / OpenType).</summary>
    public bool UsableInPdf => Format is "truetype" or "opentype";
}

public sealed record SkinLogo(byte[] Data, string MediaType, double AspectRatio)
{
    public bool IsSvg => MediaType == "image/svg+xml";
    public string DataUri => $"data:{MediaType};base64,{Convert.ToBase64String(Data)}";
}

/// <summary>A loaded, validated skin with every value resolved (defaults filled in).</summary>
public sealed class Skin
{
    public const string BuiltInSansFallback = "Liberation Sans";
    public const string BuiltInMonoFallback = "Liberation Mono";

    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string ReportTitle { get; init; }
    public string? CompanyName { get; init; }
    public string? PreparedBy { get; init; }
    public string? PreparedFor { get; init; }
    public string? Footer { get; init; }
    public required IReadOnlyDictionary<string, string> Colors { get; init; }

    /// <summary>Font families as configured, without the built-in fallback.</summary>
    public required IReadOnlyList<string> BodyFonts { get; init; }
    public required IReadOnlyList<string> HeadingFonts { get; init; }
    public required IReadOnlyList<string> MonoFonts { get; init; }

    /// <summary>The skin's own font files plus the built-in fallbacks (marked <see cref="SkinFontFace.BuiltIn"/>).</summary>
    public required IReadOnlyList<SkinFontFace> FontFaces { get; init; }
    public SkinLogo? Logo { get; init; }

    /// <summary>Logo height in millimetres; the width follows <see cref="SkinLogo.AspectRatio"/>, so it's never distorted.</summary>
    public double LogoHeightMm { get; init; } = 12;

    /// <summary>400, 600 or 700.</summary>
    public int HeadingWeight { get; init; } = 700;

    public HeadingCase HeadingCase { get; init; } = HeadingCase.Title;

    /// <summary>A report section heading in this skin's case (see <see cref="ReportHeadings"/>).</summary>
    public string Heading(string key) => ReportHeadings.Get(key, HeadingCase);
    public string? FontLicense { get; init; }

    /// <summary>Problems found while loading (missing files, bad colours…). The skin still works.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string Color(string key) => Colors.TryGetValue(key, out var c) ? c : SkinColors.Defaults[key];

    public Skin WithPreparedFor(string? preparedFor) =>
        string.IsNullOrWhiteSpace(preparedFor) ? this : With(preparedFor, PreparedBy);

    public Skin WithPreparedBy(string? preparedBy) =>
        string.IsNullOrWhiteSpace(preparedBy) ? this : With(PreparedFor, preparedBy);

    private Skin With(string? preparedFor, string? preparedBy) =>
        new()
        {
            Name = Name, DisplayName = DisplayName, ReportTitle = ReportTitle, CompanyName = CompanyName,
            PreparedBy = preparedBy, PreparedFor = preparedFor, Footer = Footer, Colors = Colors,
            BodyFonts = BodyFonts, HeadingFonts = HeadingFonts, MonoFonts = MonoFonts, FontFaces = FontFaces,
            Logo = Logo, LogoHeightMm = LogoHeightMm, HeadingWeight = HeadingWeight, HeadingCase = HeadingCase, FontLicense = FontLicense, Warnings = Warnings
        };

    /// <summary>Footer line used by every report.</summary>
    public string FooterLine =>
        string.Join(" · ", new[] { Footer, CompanyName is not null && Footer is null ? CompanyName : null, "memdump-analyzer" }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    private static readonly Lazy<Skin> DefaultSkin = new(() => SkinLoader.Load(null));

    /// <summary>The built-in default skin — the original look of the reports.</summary>
    public static Skin Default => DefaultSkin.Value;
}
