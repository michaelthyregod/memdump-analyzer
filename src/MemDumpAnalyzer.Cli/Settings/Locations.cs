using Spectre.Console;

namespace MemDumpAnalyzer.Cli.Settings;

/// <summary>
/// Where the CLI looks for things, combining command-line options, the settings files and the folder the
/// exe is in. Every lookup checks the most specific place first.
/// </summary>
public static class Locations
{
    public const string DefaultFormats = "html,json";
    public const int DefaultTop = 100;
    public static readonly string[] KnownFormats = ["html", "md", "markdown", "json", "pdf"];

    /// <summary>The folder MemDumpAnalyzer.Cli.exe is in (skins\, mem-dumps\ and known-assemblies.txt ship there).</summary>
    public static string ExeDir => AppContext.BaseDirectory;

    /// <summary>Loads both settings files; prints the problem and returns null when one is broken.</summary>
    public static EffectiveSettings? LoadSettings(SettingsStore? store = null)
    {
        try
        {
            return (store ?? SettingsStore.ForCurrentUser()).Load();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[red]Settings could not be read:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("  Fix the file, or run [aqua]settings reset[/] (add [aqua]--local[/] for the file in this folder).");
            Environment.ExitCode = 1;
            return null;
        }
    }

    /// <summary>Skin folders to search, in order: --skins-dir, the skinsDirs setting, skins\ next to the exe.</summary>
    public static IReadOnlyList<string> SkinSearchFolders(IEnumerable<string>? commandLineDirs, EffectiveSettings settings) =>
        (commandLineDirs ?? [])
            .Select(Path.GetFullPath)
            .Concat(settings.SkinsDirs.Select(d => d.Path))
            .Append(Path.Combine(ExeDir, "skins"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Folders searched for a dump that isn't found where it's given: dumpsDirs, then mem-dumps\ next to the exe.</summary>
    public static IReadOnlyList<string> DumpSearchFolders(EffectiveSettings settings) =>
        settings.DumpsDirs.Select(d => d.Path)
            .Append(Path.Combine(ExeDir, "mem-dumps"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The dump as given (relative to the current folder), else by its relative path in the dump folders.</summary>
    public static FileInfo? FindDump(string given, EffectiveSettings settings)
    {
        if (File.Exists(given))
            return new FileInfo(given);
        if (Path.IsPathRooted(given))
            return null;
        return DumpSearchFolders(settings)
            .Select(dir => new FileInfo(Path.Combine(dir, given)))
            .FirstOrDefault(f => f.Exists);
    }

    /// <summary>--known-assemblies, then the setting, then known-assemblies.txt in the current folder, then next to the exe.</summary>
    public static string? FindKnownAssemblies(string? commandLine, EffectiveSettings settings)
    {
        if (commandLine != null) return Path.GetFullPath(commandLine);
        if (settings.KnownAssemblies.Value != null) return settings.KnownAssemblies.Value;
        return new[] { Path.GetFullPath("known-assemblies.txt"), Path.Combine(ExeDir, "known-assemblies.txt") }
            .FirstOrDefault(File.Exists);
    }

    public static string[] Formats(string? commandLine, EffectiveSettings settings) =>
        (commandLine ?? (settings.Formats.Value is { } f ? string.Join(',', f) : DefaultFormats))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static DirectoryInfo OutputDir(DirectoryInfo? commandLine, EffectiveSettings settings) =>
        commandLine ?? new DirectoryInfo(settings.OutputDir.Value ?? Path.GetFullPath("report"));

    public static int Top(int? commandLine, EffectiveSettings settings) =>
        commandLine ?? settings.Top.Value ?? DefaultTop;
}
