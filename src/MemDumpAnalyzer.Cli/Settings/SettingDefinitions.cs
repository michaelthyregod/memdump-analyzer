namespace MemDumpAnalyzer.Cli.Settings;

/// <summary>
/// Every setting the settings command knows: how to read, change and validate it. Values equal to the
/// built-in default are not stored (the file holds only what differs).
/// </summary>
public sealed record SettingDefinition(
    string Name,
    bool IsList,
    bool IsPath,
    string DefaultText,
    string Description,
    Func<AppSettings, List<string>?> Get,
    Action<AppSettings, List<string>?> Set,
    Func<List<string>, string?> Validate,
    Func<List<string>, bool> IsDefault);

public static class SettingDefinitions
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new("skinsDirs", IsList: true, IsPath: true, "(none; skins\\ next to the exe is always searched last)",
            "Folders containing skin folders, searched before skins\\ next to the exe",
            s => s.SkinsDirs, (s, v) => s.SkinsDirs = v, _ => null, v => v.Count == 0),

        new("skin", IsList: false, IsPath: false, "default", "Skin used when --skin isn't given",
            s => One(s.Skin), (s, v) => s.Skin = v?[0],
            v => v[0].IndexOfAny(['\\', '/', ':']) >= 0 ? "A skin is set by name; add its parent folder to skinsDirs." : null,
            v => v[0].Equals("default", StringComparison.OrdinalIgnoreCase)),

        new("preparedBy", IsList: false, IsPath: false, "(from the skin)", "\"Prepared by\" line on reports",
            s => One(s.PreparedBy), (s, v) => s.PreparedBy = v?[0], _ => null, _ => false),

        new("formats", IsList: true, IsPath: false, Locations.DefaultFormats, "Report formats when --format isn't given",
            s => s.Formats, (s, v) => s.Formats = v,
            v => v.FirstOrDefault(f => !Locations.KnownFormats.Contains(f, StringComparer.OrdinalIgnoreCase)) is { } bad
                ? $"Unknown format '{bad}' (use {string.Join(", ", Locations.KnownFormats)})." : null,
            v => string.Join(',', v).Equals(Locations.DefaultFormats, StringComparison.OrdinalIgnoreCase) || v.Count == 0),

        new("outputDir", IsList: false, IsPath: true, "report (in the current folder)", "Output folder when --output isn't given",
            s => One(s.OutputDir), (s, v) => s.OutputDir = v?[0], _ => null, _ => false),

        new("knownAssemblies", IsList: false, IsPath: true,
            "known-assemblies.txt in the current folder, else next to the exe", "Assembly filter file when --known-assemblies isn't given",
            s => One(s.KnownAssemblies), (s, v) => s.KnownAssemblies = v?[0], _ => null, _ => false),

        new("top", IsList: false, IsPath: false, Locations.DefaultTop.ToString(), "Heap types listed (analyze) / rows per table (diff)",
            s => s.Top is { } t ? [t.ToString()] : null,
            (s, v) => s.Top = v == null ? null : int.Parse(v[0]),
            v => int.TryParse(v[0], out var n) && n > 0 ? null : "top must be a whole number above 0.",
            v => v[0] == Locations.DefaultTop.ToString()),

        new("dumpsDirs", IsList: true, IsPath: true, "(none; mem-dumps\\ next to the exe is always searched last)",
            "Folders searched for a dump given by name, before mem-dumps\\ next to the exe",
            s => s.DumpsDirs, (s, v) => s.DumpsDirs = v, _ => null, v => v.Count == 0),
    ];

    public static SettingDefinition? Find(string name) =>
        All.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static List<string>? One(string? value) => value == null ? null : [value];
}
