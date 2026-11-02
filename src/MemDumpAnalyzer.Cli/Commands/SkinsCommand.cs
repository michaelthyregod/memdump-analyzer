using System.CommandLine;
using MemDumpAnalyzer.Cli.Settings;
using MemDumpAnalyzer.Reporting.Skins;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli.Commands;

/// <summary><c>skins list</c>: the built-in default plus every skin folder found right now.</summary>
public static class SkinsCommand
{
    public static Command Build()
    {
        var skinOptions = new SkinOptions();
        var list = new Command("list", "List the skins --skin can use: the built-in default and the skin folders found in the skins folders");
        skinOptions.AddTo(list, includeReportOptions: false);

        list.SetAction(parseResult =>
        {
            var settings = Locations.LoadSettings();
            if (settings == null) return;
            var folders = skinOptions.SearchFolders(parseResult, settings);

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("Name (--skin)")
                .AddColumn("Display name")
                .AddColumn("Folder");

            var defaultMark = settings.Skin.Value is null or "default" ? " [green](used when no --skin)[/]" : "";
            table.AddRow("default", Markup.Escape(Skin.Default.DisplayName) + defaultMark, "[grey]built in[/]");

            // Whatever is there now (example skins may have been removed or replaced). The first folder with
            // a given name wins, as with --skin; later ones are shown as hidden.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in folders.Where(Directory.Exists))
            {
                foreach (var (name, folder) in SkinLoader.FindSkinFolders([root]))
                {
                    bool hidden = !seen.Add(name);
                    string display;
                    try
                    {
                        var skin = SkinLoader.Load(folder);
                        display = Markup.Escape(skin.DisplayName) +
                                  (skin.Warnings.Count > 0 ? $" [yellow]({skin.Warnings.Count} warning(s) — shown when used)[/]" : "");
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                    {
                        display = $"[red]can't load: {Markup.Escape(ex.Message)}[/]";
                    }
                    if (hidden) display = $"[grey]hidden by the {Markup.Escape(name)} above[/] — " + display;
                    else if (name.Equals(settings.Skin.Value, StringComparison.OrdinalIgnoreCase)) display += " [green](used when no --skin)[/]";
                    table.AddRow(hidden ? $"[grey]{Markup.Escape(name)}[/]" : Markup.Escape(name), display, Markup.Escape(ShortPath(folder)));
                }
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[grey]Skins folders, searched in this order:[/]");
            foreach (var f in folders)
                AnsiConsole.MarkupLine($"  {Markup.Escape(f)}{(Directory.Exists(f) ? "" : " [grey](doesn't exist)[/]")}");
            AnsiConsole.MarkupLine("[grey]Add folders with --skins-dir or 'settings add skinsDirs <folder>'. To make your own skin, " +
                                   "copy an example skin folder under a new name and edit its skin.json.[/]");
        });

        var cmd = new Command("skins", "Report skins (look and feel)");
        cmd.Add(list);
        return cmd;
    }

    /// <summary>Relative to the current folder when the folder is under it.</summary>
    private static string ShortPath(string folder)
    {
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), folder);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? folder : relative;
    }
}
