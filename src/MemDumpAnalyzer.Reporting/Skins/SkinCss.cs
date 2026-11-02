using System.Text;

namespace MemDumpAnalyzer.Reporting.Skins;

/// <summary>The skin as CSS: custom properties for colours and fonts, plus @font-face rules for the skin's own fonts.</summary>
public static class SkinCss
{
    public static string Build(Skin skin)
    {
        var sb = new StringBuilder();
        sb.Append(SkinFontResolver.FontFaceCss(skin));
        sb.Append(":root{");
        foreach (var (key, value) in skin.Colors)
            sb.Append("--").Append(ToKebab(key)).Append(':').Append(value).Append(';');
        sb.Append("--font-body:").Append(SkinFontResolver.CssStack(skin.BodyFonts, mono: false)).Append(';');
        sb.Append("--font-heading:").Append(SkinFontResolver.CssStack(skin.HeadingFonts, mono: false)).Append(';');
        sb.Append("--font-mono:").Append(SkinFontResolver.CssStack(skin.MonoFonts, mono: true)).Append(';');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        sb.Append("--heading-weight:").Append(skin.HeadingWeight).Append(';');
        sb.Append("--logo-height:").Append(skin.LogoHeightMm.ToString("0.##", inv)).Append("mm;");
        sb.Append("--logo-clearance:").Append((skin.LogoHeightMm / 2).ToString("0.##", inv)).Append("mm;");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>"criticalBackground" → "critical-background".</summary>
    public static string ToKebab(string key)
    {
        var sb = new StringBuilder(key.Length + 4);
        foreach (var c in key)
        {
            if (char.IsUpper(c)) sb.Append('-').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
