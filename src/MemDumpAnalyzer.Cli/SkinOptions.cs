using System.CommandLine;
using MemDumpAnalyzer.Reporting.Skins;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli;

/// <summary>The --skin and --prepared-for options shared by analyze and diff.</summary>
internal sealed class SkinOptions
{
    public Option<string?> Skin { get; } = new("--skin", "-s")
    {
        Description = $"Report look and feel: a built-in skin ({string.Join(", ", SkinLoader.BuiltInSkins)}) " +
                      "or a folder containing skin.json. Default: default."
    };

    public Option<string?> PreparedFor { get; } = new("--prepared-for")
    {
        Description = "\"Prepared for\" line in the report (e.g. the customer), overriding the skin's value."
    };

    public void AddTo(Command cmd)
    {
        cmd.Add(Skin);
        cmd.Add(PreparedFor);
    }

    /// <summary>Loads the chosen skin, printing its warnings; null (and exit code 1) when it can't be loaded.</summary>
    public Skin? Load(ParseResult parseResult)
    {
        var name = parseResult.GetValue(Skin);
        Skin skin;
        try
        {
            skin = SkinLoader.Load(name);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[red]Skin could not be loaded:[/] {Markup.Escape(ex.Message)}");
            Environment.ExitCode = 1;
            return null;
        }

        if (!string.IsNullOrWhiteSpace(name))
            AnsiConsole.MarkupLine($"  [grey]Skin:[/] {Markup.Escape(skin.DisplayName)}");
        PrintWarnings(skin.Warnings);
        return skin.WithPreparedFor(parseResult.GetValue(PreparedFor));
    }

    public static void PrintWarnings(IEnumerable<string> warnings)
    {
        foreach (var w in warnings)
            AnsiConsole.MarkupLine($"  [yellow]⚠ {Markup.Escape(w)}[/]");
    }
}
