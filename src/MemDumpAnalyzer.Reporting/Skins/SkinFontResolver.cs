using System.Collections.Concurrent;
using System.Text;
using QuestPDF.Drawing;

namespace MemDumpAnalyzer.Reporting.Skins;

/// <summary>Font stacks per role, resolved for one output.</summary>
public sealed record ResolvedFonts(string[] Body, string[] Heading, string[] Mono, IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a skin's font lists into what each output can use. Order of lookup for every family:
/// the skin's own font files, then fonts installed on the machine, then the built-in Liberation fallback.
/// </summary>
public static class SkinFontResolver
{
    // QuestPDF's font registry is process-wide: register each face once. Value = the family name QuestPDF
    // knows the face by (from the font file), or null if it couldn't be registered.
    private static readonly ConcurrentDictionary<string, Lazy<string?>> RegisteredFaces = new();
    private static readonly Lazy<HashSet<string>> SystemFamilies = new(() =>
    {
        try
        {
            return FontManager.GetSystemFonts().Select(f => f.FamilyName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    });

    /// <summary>Registers the skin's fonts with QuestPDF and returns the families that are actually available.</summary>
    public static ResolvedFonts ForPdf(Skin skin)
    {
        // QuestPDF 2026.9+ ignores installed fonts by default; skins may name installed fonts, so allow them.
        // Families are filtered below, so a missing one never reaches QuestPDF; fall back rather than throw anyway.
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;

        var warnings = new List<string>();

        // Skin family label → family name stored in the font file (what QuestPDF matches on)
        var skinFamilies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var face in skin.FontFaces.Where(f => f.UsableInPdf))
        {
            var registered = RegisteredFaces
                .GetOrAdd(ComputeHash(face.Data), _ => new Lazy<string?>(() => Register(face.Data)))
                .Value;
            if (registered == null)
                warnings.Add($"Font file for '{face.Family}' could not be loaded for PDF; using the next font in the list.");
            else
                skinFamilies.TryAdd(face.Family, registered);
        }
        string[] Stack(IReadOnlyList<string> families, string builtIn)
        {
            var available = new List<string>();
            foreach (var family in families)
            {
                if (skinFamilies.TryGetValue(family, out var fileFamily))
                    available.Add(fileFamily);
                else if (SystemFamilies.Value.Contains(family))
                    available.Add(family);
            }
            available.Add(builtIn);

            // Later entries are fallbacks by design; only a missing first choice is worth a warning
            var preferred = families.FirstOrDefault();
            if (preferred != null && !skinFamilies.ContainsKey(preferred) && !SystemFamilies.Value.Contains(preferred))
                warnings.Add($"Font '{preferred}' is neither in the skin folder nor installed; the PDF uses '{available[0]}' instead.");

            return available.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var body = Stack(skin.BodyFonts, Skin.BuiltInSansFallback);
        var heading = Stack(skin.HeadingFonts, Skin.BuiltInSansFallback);
        var mono = Stack(skin.MonoFonts, Skin.BuiltInMonoFallback);
        return new ResolvedFonts(body, heading, mono, warnings.Distinct().ToList());
    }

    /// <summary>
    /// CSS font-family value: the skin's families, the built-in fallback name and a generic family.
    /// The reader's browser picks the first it has; the skin's own font files are embedded via <see cref="FontFaceCss"/>.
    /// </summary>
    public static string CssStack(IReadOnlyList<string> families, bool mono) =>
        string.Join(", ", families
            .Append(mono ? Skin.BuiltInMonoFallback : Skin.BuiltInSansFallback)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => "'" + f.Replace("\\", "").Replace("'", "") + "'")
            .Append(mono ? "monospace" : "sans-serif"));

    /// <summary>@font-face rules embedding the skin's own font files (built-in fallbacks are not embedded, to keep HTML small).</summary>
    public static string FontFaceCss(Skin skin)
    {
        var sb = new StringBuilder();
        foreach (var face in skin.FontFaces.Where(f => !f.BuiltIn))
        {
            var mime = face.Format switch
            {
                "truetype" => "font/ttf",
                "opentype" => "font/otf",
                "woff2" => "font/woff2",
                _ => "font/woff"
            };
            sb.Append("@font-face{font-family:'").Append(face.Family.Replace("'", ""))
              .Append("';src:url(data:").Append(mime).Append(";base64,").Append(Convert.ToBase64String(face.Data))
              .Append(") format('").Append(face.Format).Append("');font-weight:").Append(face.Weight)
              .Append(";font-style:").Append(face.Style).Append(";font-display:swap}\n");
        }
        return sb.ToString();
    }

    private static string? Register(byte[] data)
    {
        var postScriptName = FontNameReader.ReadPostScriptName(data);
        if (postScriptName == null) return null;
        try
        {
            FontManager.RegisterFontFromStream(new MemoryStream(data, writable: false));
        }
        catch (Exception)
        {
            return null;
        }
        return FontManager.GetRegisteredFonts()
            .FirstOrDefault(f => string.Equals(f.PostScriptName, postScriptName, StringComparison.Ordinal))
            ?.FamilyName;
    }

    private static string ComputeHash(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))[..16];
}
