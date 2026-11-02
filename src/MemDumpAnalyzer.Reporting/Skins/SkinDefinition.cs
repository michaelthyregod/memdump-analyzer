using System.Text.Json.Serialization;

namespace MemDumpAnalyzer.Reporting.Skins;

/// <summary>
/// The contents of a <c>skin.json</c> file. Every part is optional: anything left out comes from the
/// default skin, so a skin can be as small as a few colours.
/// </summary>
public sealed class SkinDefinition
{
    /// <summary>Short identifier, e.g. "northwind".</summary>
    public string? Name { get; set; }

    /// <summary>Human-readable name shown in the CLI.</summary>
    public string? DisplayName { get; set; }

    public SkinTexts? Texts { get; set; }

    /// <summary>Logo file relative to the skin folder: .svg, .png or .jpg.</summary>
    public string? Logo { get; set; }

    /// <summary>Logo height in millimetres (width follows the logo's own proportions). Default 12.</summary>
    public double? LogoHeightMm { get; set; }

    /// <summary>Font weight for headings: 400 (regular), 600 (semibold) or 700 (bold, the default).</summary>
    public int? HeadingWeight { get; set; }

    /// <summary>Colour tokens (see <see cref="SkinColors.Keys"/>) as #RRGGBB.</summary>
    public Dictionary<string, string>? Colors { get; set; }

    public SkinFonts? Fonts { get; set; }

    /// <summary>Font files shipped in the skin folder, mapped to the family names used in <see cref="Fonts"/>.</summary>
    public List<SkinFontFile>? FontFiles { get; set; }

    /// <summary>Free text: what the skin author cleared for the fonts (reports embed them in HTML and PDF).</summary>
    public string? FontLicense { get; set; }
}

public sealed class SkinTexts
{
    /// <summary>Replaces "Memory Dump Analysis Report".</summary>
    public string? ReportTitle { get; set; }

    /// <summary>Shown under the title and in the footer.</summary>
    public string? CompanyName { get; set; }

    /// <summary>"Prepared by" line in the summary.</summary>
    public string? PreparedBy { get; set; }

    /// <summary>"Prepared for" line in the summary; usually set per report with --prepared-for.</summary>
    public string? PreparedFor { get; set; }

    /// <summary>Footer text; "memdump-analyzer" is always appended.</summary>
    public string? Footer { get; set; }
}

/// <summary>Ordered font lists per role. The first family that can be found wins.</summary>
public sealed class SkinFonts
{
    public List<string>? Body { get; set; }
    public List<string>? Heading { get; set; }
    public List<string>? Mono { get; set; }
}

public sealed class SkinFontFile
{
    /// <summary>The family name used in <see cref="SkinFonts"/>.</summary>
    public string? Family { get; set; }

    /// <summary>Path relative to the skin folder: .ttf/.otf (HTML and PDF) or .woff2/.woff (HTML only).</summary>
    public string? File { get; set; }

    /// <summary>CSS font weight, e.g. 400 or 700. Default 400.</summary>
    public int? Weight { get; set; }

    /// <summary>"normal" or "italic". Default "normal".</summary>
    public string? Style { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(SkinDefinition))]
internal partial class SkinJsonContext : JsonSerializerContext
{
}
