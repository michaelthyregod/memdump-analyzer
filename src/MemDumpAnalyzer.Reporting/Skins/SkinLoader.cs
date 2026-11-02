using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemDumpAnalyzer.Reporting.Skins;

/// <summary>
/// Loads the built-in "default" skin, a skin folder (containing <c>skin.json</c>) by path, or a skin folder by
/// name from a <c>skins</c> folder in the current directory or next to the executable.
/// Missing values come from the default skin; problems become <see cref="Skin.Warnings"/>, never failures —
/// except a skin that can't be found or a skin.json that isn't valid JSON.
/// </summary>
public static partial class SkinLoader
{
    public const string SkinFileName = "skin.json";
    private const string ResourcePrefix = "skins/";
    private const string FontResourcePrefix = "fonts/";

    /// <summary>Skins compiled into the tool. Everything else (e.g. the Northwind example) is a folder.</summary>
    public static IReadOnlyList<string> BuiltInSkins => ["default"];

    /// <summary>Where a skin given by name (<c>--skin northwind</c>) is looked for, in order.</summary>
    public static IReadOnlyList<string> SkinSearchFolders =>
        new[] { Path.Combine(Directory.GetCurrentDirectory(), "skins"), Path.Combine(AppContext.BaseDirectory, "skins") }
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Names of the skin folders found in <see cref="SkinSearchFolders"/>.</summary>
    public static IReadOnlyList<string> AvailableSkins => FindSkinFolders(SkinSearchFolders).Select(s => s.Name).ToList();

    /// <summary>
    /// Skin folders (sub-folders with a skin.json) in the given search folders, sorted by name. When two
    /// search folders have the same name, the first wins — the same one <see cref="Load"/> would use.
    /// Missing search folders are simply skipped: no skin folder is required to exist.
    /// </summary>
    public static IReadOnlyList<(string Name, string Folder)> FindSkinFolders(IEnumerable<string> searchFolders)
    {
        var found = new List<(string Name, string Folder)>();
        foreach (var root in searchFolders.Where(Directory.Exists))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (File.Exists(Path.Combine(dir, SkinFileName)) &&
                    !found.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    found.Add((name, dir));
            }
        }
        return found.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly string[] DefaultBodyFonts = ["Arial"];
    private static readonly string[] DefaultMonoFonts = ["Courier New"];

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    /// <param name="nameOrPath">Null/"default"; a skin folder or skin.json path; or the name of a folder in
    /// <see cref="SkinSearchFolders"/>.</param>
    public static Skin Load(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath) || nameOrPath.Equals("default", StringComparison.OrdinalIgnoreCase))
            return Build(ReadBuiltIn("default"), rel => ReadResource($"{ResourcePrefix}default/{rel}"), "built-in skin 'default'");

        var path = Path.GetFullPath(nameOrPath);

        // A bare name ("northwind") that isn't a path here: look in the skins folders
        bool isBareName = nameOrPath.IndexOfAny(['\\', '/', ':']) < 0;
        if (isBareName && !Directory.Exists(path) && !File.Exists(path))
        {
            var found = SkinSearchFolders
                .Select(f => Path.Combine(f, nameOrPath))
                .FirstOrDefault(d => File.Exists(Path.Combine(d, SkinFileName)));
            if (found != null)
                path = found;
        }

        string folder, jsonPath;
        if (Directory.Exists(path))
        {
            folder = path;
            jsonPath = Path.Combine(path, SkinFileName);
        }
        else if (File.Exists(path))
        {
            jsonPath = path;
            folder = Path.GetDirectoryName(path)!;
        }
        else
        {
            var names = BuiltInSkins.Concat(AvailableSkins).Distinct(StringComparer.OrdinalIgnoreCase);
            throw new FileNotFoundException(
                $"Skin '{nameOrPath}' not found. Use one of: {string.Join(", ", names)}, or a folder that contains {SkinFileName} " +
                $"(names are looked up in: {string.Join("; ", SkinSearchFolders)}).");
        }

        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"No {SkinFileName} in skin folder '{folder}'.");

        var definition = Parse(File.ReadAllText(jsonPath), jsonPath);
        return Build(definition, rel => ReadSkinFile(folder, rel), jsonPath);
    }

    private static SkinDefinition ReadBuiltIn(string name)
    {
        var bytes = ReadResource($"{ResourcePrefix}{name}/{SkinFileName}")
                    ?? throw new InvalidOperationException($"Built-in skin '{name}' is missing from the assembly.");
        return Parse(System.Text.Encoding.UTF8.GetString(bytes), $"built-in skin '{name}'");
    }

    internal static SkinDefinition Parse(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize(json, SkinJsonContext.Default.SkinDefinition)
                   ?? throw new InvalidDataException($"{source} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not valid JSON: {ex.Message}", ex);
        }
    }

    private static Skin Build(SkinDefinition d, Func<string, byte[]?> readFile, string source)
    {
        var warnings = new List<string>();

        // Colours: defaults, overridden by valid skin values
        var colors = new Dictionary<string, string>(SkinColors.Defaults);
        foreach (var (key, value) in d.Colors ?? [])
        {
            var known = SkinColors.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (known == null)
                warnings.Add($"Unknown colour '{key}' ignored (known: {string.Join(", ", SkinColors.Keys)}).");
            else if (!HexColor().IsMatch(value ?? ""))
                warnings.Add($"Colour '{key}' = '{value}' is not #RRGGBB; using the default {colors[known]}.");
            else
                colors[known] = value!.ToLowerInvariant();
        }

        // Font files: the skin's own, then the built-in fallbacks
        var faces = new List<SkinFontFace>();
        foreach (var ff in d.FontFiles ?? [])
        {
            if (string.IsNullOrWhiteSpace(ff.Family) || string.IsNullOrWhiteSpace(ff.File))
            {
                warnings.Add("A fontFiles entry needs both 'family' and 'file'; entry ignored.");
                continue;
            }
            var format = Path.GetExtension(ff.File).ToLowerInvariant() switch
            {
                ".ttf" => "truetype",
                ".otf" => "opentype",
                ".woff2" => "woff2",
                ".woff" => "woff",
                _ => null
            };
            if (format == null)
            {
                warnings.Add($"Font file '{ff.File}' is not .ttf, .otf, .woff2 or .woff; ignored.");
                continue;
            }
            var data = readFile(ff.File);
            if (data == null)
            {
                warnings.Add($"Font file '{ff.File}' for '{ff.Family}' not found in the skin folder; falling back.");
                continue;
            }
            var style = (ff.Style ?? "normal").ToLowerInvariant() == "italic" ? "italic" : "normal";
            faces.Add(new SkinFontFace(ff.Family.Trim(), data, format, ff.Weight ?? 400, style, BuiltIn: false));
        }
        faces.AddRange(BuiltInFontFaces.Value);

        // Logo
        SkinLogo? logo = null;
        if (!string.IsNullOrWhiteSpace(d.Logo))
        {
            var mediaType = Path.GetExtension(d.Logo).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => null
            };
            var data = mediaType != null ? readFile(d.Logo) : null;
            if (mediaType == null)
                warnings.Add($"Logo '{d.Logo}' must be .svg, .png or .jpg; no logo used.");
            else if (data == null)
                warnings.Add($"Logo '{d.Logo}' not found in the skin folder; no logo used.");
            else
                logo = new SkinLogo(data, mediaType, LogoAspectRatio(data, mediaType));
        }

        int headingWeight = d.HeadingWeight ?? 700;
        if (headingWeight is not (400 or 600 or 700))
        {
            warnings.Add($"headingWeight {headingWeight} is not 400, 600 or 700; using 700.");
            headingWeight = 700;
        }
        var headingCase = HeadingCase.Title;
        switch (d.HeadingCase?.Trim().ToLowerInvariant())
        {
            case null or "" or "title": break;
            case "sentence": headingCase = HeadingCase.Sentence; break;
            default:
                warnings.Add($"headingCase '{d.HeadingCase}' is not 'title' or 'sentence'; using title.");
                break;
        }

        double logoHeight = d.LogoHeightMm ?? 12;
        if (logoHeight is < 5 or > 60)
        {
            warnings.Add($"logoHeightMm {logoHeight} is outside 5–60; using 12.");
            logoHeight = 12;
        }

        static IReadOnlyList<string> Fonts(List<string>? configured, IReadOnlyList<string> fallback) =>
            configured is { Count: > 0 } ? configured.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList() : fallback;

        var body = Fonts(d.Fonts?.Body, DefaultBodyFonts);

        return new Skin
        {
            Name = string.IsNullOrWhiteSpace(d.Name) ? Path.GetFileName(Path.GetDirectoryName(source)) ?? "custom" : d.Name,
            DisplayName = d.DisplayName ?? d.Name ?? "Custom",
            ReportTitle = d.Texts?.ReportTitle ?? "Memory Dump Analysis Report",
            CompanyName = d.Texts?.CompanyName,
            PreparedBy = d.Texts?.PreparedBy,
            PreparedFor = d.Texts?.PreparedFor,
            Footer = d.Texts?.Footer,
            Colors = colors,
            BodyFonts = body,
            HeadingFonts = Fonts(d.Fonts?.Heading, body),
            MonoFonts = Fonts(d.Fonts?.Mono, DefaultMonoFonts),
            FontFaces = faces,
            Logo = logo,
            LogoHeightMm = logoHeight,
            HeadingWeight = headingWeight,
            HeadingCase = headingCase,
            FontLicense = d.FontLicense,
            Warnings = warnings
        };
    }

    /// <summary>Width ÷ height of the logo: SVG viewBox, PNG header, or JPEG frame; 1 if unknown.</summary>
    internal static double LogoAspectRatio(byte[] data, string mediaType)
    {
        try
        {
            if (mediaType == "image/svg+xml")
            {
                var vb = SvgViewBox().Match(System.Text.Encoding.UTF8.GetString(data));
                if (vb.Success)
                {
                    var parts = vb.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (parts.Length == 4 &&
                        double.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out var w) &&
                        double.TryParse(parts[3], System.Globalization.NumberStyles.Float, inv, out var h) && w > 0 && h > 0)
                        return w / h;
                }
            }
            else if (mediaType == "image/png" && data.Length > 24)
            {
                uint w = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16));
                uint h = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20));
                if (w > 0 && h > 0) return (double)w / h;
            }
            else if (mediaType == "image/jpeg")
            {
                for (int i = 2; i + 9 < data.Length;)
                {
                    if (data[i] != 0xFF) break;
                    byte marker = data[i + 1];
                    int len = (data[i + 2] << 8) | data[i + 3];
                    if (marker is >= 0xC0 and <= 0xC3)
                    {
                        int h = (data[i + 5] << 8) | data[i + 6], w = (data[i + 7] << 8) | data[i + 8];
                        if (w > 0 && h > 0) return (double)w / h;
                        break;
                    }
                    i += 2 + len;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
        {
            // Unreadable header: fall through to square
        }
        return 1;
    }

    [GeneratedRegex("viewBox\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex SvgViewBox();

    /// <summary>Reads a file inside the skin folder; refuses paths that escape it.</summary>
    private static byte[]? ReadSkinFile(string folder, string relative)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(folder, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return null;
        return File.ReadAllBytes(full);
    }

    private static byte[]? ReadResource(string name)
    {
        // %(RecursiveDir) gives '\' on Windows builds and '/' elsewhere; accept both
        var assembly = typeof(SkinLoader).Assembly;
        var normalized = name.Replace('\\', '/');
        int firstSlash = normalized.IndexOf('/');
        var windowsStyle = firstSlash < 0 ? normalized
            : normalized[..(firstSlash + 1)] + normalized[(firstSlash + 1)..].Replace('/', '\\');
        using var stream = assembly.GetManifestResourceStream(normalized)
                           ?? assembly.GetManifestResourceStream(windowsStyle);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // Liberation Sans/Mono (SIL OFL 1.1): metric-compatible with Arial / Courier New, so the default
    // look holds where those aren't installed, and every skin has a font that always exists.
    private static readonly Lazy<IReadOnlyList<SkinFontFace>> BuiltInFontFaces = new(() =>
    {
        (string File, string Family, int Weight, string Style)[] files =
        [
            ("LiberationSans-Regular.ttf", Skin.BuiltInSansFallback, 400, "normal"),
            ("LiberationSans-Bold.ttf", Skin.BuiltInSansFallback, 700, "normal"),
            ("LiberationSans-Italic.ttf", Skin.BuiltInSansFallback, 400, "italic"),
            ("LiberationSans-BoldItalic.ttf", Skin.BuiltInSansFallback, 700, "italic"),
            ("LiberationMono-Regular.ttf", Skin.BuiltInMonoFallback, 400, "normal"),
            ("LiberationMono-Bold.ttf", Skin.BuiltInMonoFallback, 700, "normal"),
        ];
        return files
            .Select(f => new SkinFontFace(f.Family,
                ReadResource(FontResourcePrefix + f.File) ?? throw new InvalidOperationException($"Built-in font {f.File} is missing."),
                "truetype", f.Weight, f.Style, BuiltIn: true))
            .ToList();
    });

    /// <summary>Names of embedded resources — used by tests to confirm the built-ins are packaged.</summary>
    internal static IEnumerable<string> ResourceNames => typeof(SkinLoader).Assembly.GetManifestResourceNames();
}
