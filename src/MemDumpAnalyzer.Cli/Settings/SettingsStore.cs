using System.Text.Json;

namespace MemDumpAnalyzer.Cli.Settings;

/// <summary>Where a setting's value came from.</summary>
public enum SettingSource { Default, User, Local, CommandLine }

/// <summary>
/// The two settings files: a local one in the current folder (wins, so someone can keep a project-specific
/// setup) and a per-user one in %APPDATA%. Files are merged per setting; for list settings the local
/// entries come first. Relative paths in a file are relative to that file's folder.
/// </summary>
public sealed class SettingsStore(string userPath, string localPath)
{
    public const string LocalFileName = "memdump-analyzer.settings.json";

    public string UserPath { get; } = userPath;
    public string LocalPath { get; } = localPath;

    public static SettingsStore ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "memdump-analyzer", "settings.json"),
        Path.Combine(Directory.GetCurrentDirectory(), LocalFileName));

    public string PathFor(bool local) => local ? LocalPath : UserPath;

    /// <summary>The file's settings, or null when the file doesn't exist.</summary>
    public AppSettings? Read(bool local)
    {
        var path = PathFor(local);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Settings file {path} is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Writes only non-default values; deletes the file when nothing is left.</summary>
    public void Write(bool local, AppSettings settings)
    {
        var path = PathFor(local);
        if (settings.IsEmpty)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Both files merged on top of the built-in defaults (command-line options are applied by the caller).</summary>
    public EffectiveSettings Load()
    {
        var local = Read(local: true);
        var user = Read(local: false);
        var localDir = Path.GetDirectoryName(LocalPath)!;
        var userDir = Path.GetDirectoryName(UserPath)!;

        (T? Value, SettingSource Source) Pick<T>(Func<AppSettings, T?> get) where T : class =>
            local != null && get(local) is { } l ? (l, SettingSource.Local)
            : user != null && get(user) is { } u ? (u, SettingSource.User)
            : (null, SettingSource.Default);

        string? ResolvePath(string? path, SettingSource source) =>
            path == null ? null : Path.GetFullPath(Path.Combine(source == SettingSource.Local ? localDir : userDir, path));

        List<(string Path, SettingSource Source)> MergeDirs(Func<AppSettings, List<string>?> get) =>
            (local != null ? get(local) ?? [] : []).Select(p => (ResolvePath(p, SettingSource.Local)!, SettingSource.Local))
            .Concat((user != null ? get(user) ?? [] : []).Select(p => (ResolvePath(p, SettingSource.User)!, SettingSource.User)))
            .DistinctBy(p => p.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skin = Pick(s => s.Skin);
        var preparedBy = Pick(s => s.PreparedBy);
        var formats = Pick(s => s.Formats is { Count: > 0 } f ? f : null);
        var outputDir = Pick(s => s.OutputDir);
        var known = Pick(s => s.KnownAssemblies);
        (int? Top, SettingSource Source) top =
            local?.Top is { } lt ? (lt, SettingSource.Local)
            : user?.Top is { } ut ? (ut, SettingSource.User)
            : (null, SettingSource.Default);

        return new EffectiveSettings
        {
            SkinsDirs = MergeDirs(s => s.SkinsDirs),
            DumpsDirs = MergeDirs(s => s.DumpsDirs),
            Skin = skin,
            PreparedBy = preparedBy,
            Formats = formats,
            OutputDir = (ResolvePath(outputDir.Value, outputDir.Source), outputDir.Source),
            KnownAssemblies = (ResolvePath(known.Value, known.Source), known.Source),
            Top = top
        };
    }
}

/// <summary>Settings after merging the files: each value with where it came from; paths are absolute.</summary>
public sealed class EffectiveSettings
{
    public required List<(string Path, SettingSource Source)> SkinsDirs { get; init; }
    public required List<(string Path, SettingSource Source)> DumpsDirs { get; init; }
    public required (string? Value, SettingSource Source) Skin { get; init; }
    public required (string? Value, SettingSource Source) PreparedBy { get; init; }
    public required (List<string>? Value, SettingSource Source) Formats { get; init; }
    public required (string? Value, SettingSource Source) OutputDir { get; init; }
    public required (string? Value, SettingSource Source) KnownAssemblies { get; init; }
    public required (int? Value, SettingSource Source) Top { get; init; }

    /// <summary>The settings files couldn't be read: the tool still runs on its defaults.</summary>
    public static EffectiveSettings Defaults => new()
    {
        SkinsDirs = [], DumpsDirs = [],
        Skin = (null, SettingSource.Default), PreparedBy = (null, SettingSource.Default),
        Formats = (null, SettingSource.Default), OutputDir = (null, SettingSource.Default),
        KnownAssemblies = (null, SettingSource.Default), Top = (null, SettingSource.Default)
    };
}
