using System.CommandLine;
using MemDumpAnalyzer.Cli.Settings;
using MemDumpAnalyzer.Reporting.Skins;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli;

/// <summary>--skin, --skins-dir and --prepared-for, shared by analyze, diff and skins list.</summary>
internal sealed class SkinOptions
{
    public Option<string?> Skin { get; } = new("--skin", "-s")
    {
        Description = "Skin (look and feel) by name: 'default' (built in) or the name of a skin folder in the " +
                      "skins folders. Default: the 'skin' setting, else default."
    };

    public Option<string[]> SkinsDirs { get; } = new("--skins-dir")
    {
        Description = "A folder containing skin folders; repeat to add more. Searched before the skinsDirs " +
                      "setting and skins\\ next to the exe."
    };

    public Option<string?> PreparedFor { get; } = new("--prepared-for")
    {
        Description = "\"Prepared for\" line in the report (e.g. the customer)."
    };

    public void AddTo(Command cmd, bool includeReportOptions = true)
    {
        if (includeReportOptions) cmd.Add(Skin);
        cmd.Add(SkinsDirs);
        if (includeReportOptions) cmd.Add(PreparedFor);
    }

    public IReadOnlyList<string> SearchFolders(ParseResult parseResult, EffectiveSettings settings) =>
        Locations.SkinSearchFolders(parseResult.GetValue(SkinsDirs), settings);

    /// <summary>Loads the chosen skin, printing its warnings; null (and exit code 1) when it can't be loaded.</summary>
    public Skin? Load(ParseResult parseResult, EffectiveSettings settings)
    {
        var name = parseResult.GetValue(Skin) ?? settings.Skin.Value;
        Skin skin;
        try
        {
            skin = SkinLoader.LoadByName(name, SearchFolders(parseResult, settings));
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            AnsiConsole.MarkupLine($"[red]Skin could not be loaded:[/] {Markup.Escape(ex.Message)}");
            Environment.ExitCode = 1;
            return null;
        }

        if (!string.IsNullOrWhiteSpace(name))
            AnsiConsole.MarkupLine($"  [grey]Skin:[/] {Markup.Escape(skin.DisplayName)}");
        PrintWarnings(skin.Warnings);
        return skin.WithPreparedBy(settings.PreparedBy.Value).WithPreparedFor(parseResult.GetValue(PreparedFor));
    }

    public static void PrintWarnings(IEnumerable<string> warnings)
    {
        foreach (var w in warnings)
            AnsiConsole.MarkupLine($"  [yellow]⚠ {Markup.Escape(w)}[/]");
    }
}
