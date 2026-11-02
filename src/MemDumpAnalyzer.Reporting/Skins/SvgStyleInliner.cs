using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MemDumpAnalyzer.Reporting.Skins;

/// <summary>
/// QuestPDF's SVG renderer ignores &lt;style&gt; blocks, so a logo that colours its shapes through CSS classes
/// (the usual Illustrator export: <c>.st1{fill:#E6007E}</c>) comes out black in the PDF. This copies class
/// rules onto the elements as presentation attributes (<c>fill="#E6007E"</c>) before the logo is drawn.
/// Precedence follows CSS: a class rule beats a presentation attribute, an inline <c>style</c> beats a class
/// rule, and later rules beat earlier ones.
/// </summary>
public static partial class SvgStyleInliner
{
    // ".a{...}" or ".a,.b{...}" — class selectors are what design tools export
    [GeneratedRegex(@"(?<selectors>[^{}]+)\{(?<body>[^{}]*)\}")]
    private static partial Regex Rule();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    public static string Inline(string svg)
    {
        XDocument doc;
        try
        {
            // DTDs are ignored (no external entities can be resolved)
            using var reader = XmlReader.Create(new StringReader(svg),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return svg;   // not parseable: draw it as it is
        }

        var styles = doc.Descendants().Where(e => e.Name.LocalName == "style").ToList();
        if (styles.Count == 0)
            return svg;

        // class name → declarations, in document order (later rules override earlier ones)
        var classRules = new Dictionary<string, List<(string Property, string Value)>>(StringComparer.Ordinal);
        foreach (var style in styles)
        {
            foreach (Match rule in Rule().Matches(Comment().Replace(style.Value, "")))
            {
                var declarations = ParseDeclarations(rule.Groups["body"].Value);
                foreach (var selector in rule.Groups["selectors"].Value.Split(','))
                {
                    var s = selector.Trim();
                    if (s.Length < 2 || s[0] != '.' || s.IndexOfAny([' ', '>', ':', '[', '#', '.'], 1) >= 0)
                        continue;   // only simple ".class" selectors
                    if (!classRules.TryGetValue(s[1..], out var list))
                        classRules[s[1..]] = list = [];
                    list.AddRange(declarations);
                }
            }
        }
        if (classRules.Count == 0)
            return svg;

        foreach (var element in doc.Descendants())
        {
            if (element.Attribute("class")?.Value is not { } classAttr)
                continue;

            var fromClasses = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var cls in classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (classRules.TryGetValue(cls, out var declarations))
                    foreach (var (property, value) in declarations)
                        fromClasses[property] = value;
            }

            var inlineStyle = ParseDeclarations(element.Attribute("style")?.Value ?? "").Select(d => d.Property).ToHashSet();
            foreach (var (property, value) in fromClasses)
            {
                if (!inlineStyle.Contains(property))
                    element.SetAttributeValue(property, value);
            }
        }

        return doc.Declaration != null ? doc.Declaration + doc.ToString(SaveOptions.DisableFormatting) : doc.ToString(SaveOptions.DisableFormatting);
    }

    private static List<(string Property, string Value)> ParseDeclarations(string body) =>
        body.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Split(':', 2))
            .Where(p => p.Length == 2 && p[0].Trim().Length > 0 && p[1].Trim().Length > 0)
            .Select(p => (p[0].Trim().ToLowerInvariant(), p[1].Trim()))
            .Where(d => !d.Item1.StartsWith("enable-", StringComparison.Ordinal))   // Illustrator noise
            .ToList();
}
