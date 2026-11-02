using System.CommandLine;
using MemDumpAnalyzer.Reporting.Skins;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli.Commands;

/// <summary><c>skins list</c>: the built-in default skin plus every skin folder found right now.</summary>
public static class SkinsCommand
{
    public static Command Build()
    {
        var list = new Command("list", "List the skins --skin can use: the built-in default and the skin folders found in skins\\");
        list.SetAction(_ =>
        {
            var table = new Table().Border(TableBorder.Rounded)
                .AddColumn("Name (--skin)")
                .AddColumn("Display name")
                .AddColumn("Where");

            table.AddRow("default", Markup.Escape(Skin.Default.DisplayName), "[grey]built in[/]");

            // Whatever is there now: example skins like northwind may have been removed or replaced
            foreach (var (name, folder) in SkinLoader.FindSkinFolders(SkinLoader.SkinSearchFolders))
            {
                string display;
                try
                {
                    var skin = SkinLoader.Load(folder);
                    display = Markup.Escape(skin.DisplayName) +
                              (skin.Warnings.Count > 0 ? $" [yellow]({skin.Warnings.Count} warning(s) — run analyze to see them)[/]" : "");
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    display = $"[red]can't load: {Markup.Escape(ex.Message)}[/]";
                }
                // Short path when the skin is under the current folder (the usual case: skins\<name>)
                var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), folder);
                var where = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? folder : relative;
                table.AddRow(Markup.Escape(name), display, Markup.Escape(where));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[grey]Skin names are looked up in:[/] " +
                                   Markup.Escape(string.Join("  |  ", SkinLoader.SkinSearchFolders)));
            AnsiConsole.MarkupLine("[grey]A folder anywhere else works too: --skin C:\\path\\to\\my-skin. " +
                                   "To make your own, copy an example skin folder under a new name and edit its skin.json.[/]");
        });

        var cmd = new Command("skins", "Report skins (look and feel)");
        cmd.Add(list);
        return cmd;
    }
}
