using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemDumpAnalyzer.Cli.Settings;

/// <summary>
/// One settings file. Every value is optional: a file holds only what differs from the defaults, so a
/// later version can change a default without old files pinning it.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Extra folders with skin folders, searched before the skins\ folder next to the exe.</summary>
    public List<string>? SkinsDirs { get; set; }

    /// <summary>Skin used when --skin isn't given.</summary>
    public string? Skin { get; set; }

    /// <summary>"Prepared by" line on reports, overriding the skin's.</summary>
    public string? PreparedBy { get; set; }

    /// <summary>Report formats when --format isn't given.</summary>
    public List<string>? Formats { get; set; }

    /// <summary>Output folder when --output isn't given.</summary>
    public string? OutputDir { get; set; }

    /// <summary>Assembly filter file when --known-assemblies isn't given.</summary>
    public string? KnownAssemblies { get; set; }

    /// <summary>Heap types in the reports (analyze) / rows per table (diff) when --top isn't given.</summary>
    public int? Top { get; set; }

    /// <summary>Folders searched for a dump given by name only, before mem-dumps\ next to the exe.</summary>
    public List<string>? DumpsDirs { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        SkinsDirs is not { Count: > 0 } && Skin is null && PreparedBy is null && Formats is not { Count: > 0 } &&
        OutputDir is null && KnownAssemblies is null && Top is null && DumpsDirs is not { Count: > 0 };
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
