using System.CommandLine;
using MemDumpAnalyzer.Cli.Settings;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli.Commands;

/// <summary>
/// settings show | set | add | remove | reset. Writes the per-user file, or with --local the file in the
/// current folder. Only values that differ from the defaults are stored; an empty file is deleted.
/// </summary>
public static class SettingsCommand
{
    public static Command Build(Func<SettingsStore>? storeFactory = null)
    {
        storeFactory ??= SettingsStore.ForCurrentUser;
        var names = string.Join(", ", SettingDefinitions.All.Select(d => d.Name));

        Option<bool> LocalOption() => new("--local")
        {
            Description = $"Change {SettingsStore.LocalFileName} in the current folder (it wins over the per-user file)"
        };
        Argument<string> NameArg() => new("setting") { Description = $"One of: {names}" };
        Argument<string[]> ValuesArg() => new("values") { Description = "One or more values", Arity = ArgumentArity.OneOrMore };

        var cmd = new Command("settings", "Show or change settings (per user, or per folder with --local)");

        var show = new Command("show", "Every setting, its value and where it comes from");
        show.SetAction(_ => Show(storeFactory()));
        cmd.Add(show);

        foreach (var (verb, help) in new[]
                 {
                     ("set", "Set a setting (lists: replace all values)"),
                     ("add", "Add values to a list setting"),
                     ("remove", "Remove values from a list setting"),
                 })
        {
            var nameArg = NameArg();
            var valuesArg = ValuesArg();
            var local = LocalOption();
            var sub = new Command(verb, help);
            sub.Add(nameArg);
            sub.Add(valuesArg);
            sub.Add(local);
            sub.SetAction(parseResult => Change(storeFactory(), verb, parseResult.GetValue(nameArg)!,
                parseResult.GetValue(valuesArg)!, parseResult.GetValue(local)));
            cmd.Add(sub);
        }

        var resetName = new Argument<string?>("setting") { Description = $"One of: {names}. Leave out to reset all.", Arity = ArgumentArity.ZeroOrOne };
        var resetLocal = LocalOption();
        var reset = new Command("reset", "Back to the default (one setting, or all)");
        reset.Add(resetName);
        reset.Add(resetLocal);
        reset.SetAction(parseResult => Reset(storeFactory(), parseResult.GetValue(resetName), parseResult.GetValue(resetLocal)));
        cmd.Add(reset);

        return cmd;
    }

    private static void Show(SettingsStore store)
    {
        var settings = Locations.LoadSettings(store);
        if (settings == null) return;
        AppSettings? user, local;
        try
        {
            user = store.Read(local: false);
            local = store.Read(local: true);
        }
        catch (InvalidDataException) { return; }   // already reported by LoadSettings

        var table = new Table().Border(TableBorder.Rounded).AddColumn("Setting").AddColumn("Value").AddColumn("From");
        foreach (var d in SettingDefinitions.All)
        {
            var l = local != null ? d.Get(local) : null;
            var u = user != null ? d.Get(user) : null;
            string value, from;
            if (d.IsList && (l is { Count: > 0 } || u is { Count: > 0 }))
            {
                var rows = (l ?? []).Select(v => (v, "local")).Concat((u ?? []).Select(v => (v, "user"))).ToList();
                value = string.Join("\n", rows.Select(r => Markup.Escape(r.v)));
                from = string.Join("\n", rows.Select(r => r.Item2));
            }
            else if (l is { Count: > 0 }) { value = Markup.Escape(string.Join(", ", l)); from = "local"; }
            else if (u is { Count: > 0 }) { value = Markup.Escape(string.Join(", ", u)); from = "user"; }
            else { value = $"[grey]{Markup.Escape(d.DefaultText)}[/]"; from = "[grey]default[/]"; }
            table.AddRow(new Markup(Markup.Escape(d.Name)), new Markup(value), new Markup(from));
        }
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine($"[grey]user:[/]  {Markup.Escape(store.UserPath)}{(File.Exists(store.UserPath) ? "" : " [grey](not created — all defaults)[/]")}");
        AnsiConsole.MarkupLine($"[grey]local:[/] {Markup.Escape(store.LocalPath)}{(File.Exists(store.LocalPath) ? "" : " [grey](none)[/]")}");
        AnsiConsole.MarkupLine("[grey]Command-line options win over local, local over user, user over the defaults. " +
                               "Lists combine: local entries first, then user.[/]");
    }

    private static void Change(SettingsStore store, string verb, string name, string[] values, bool local)
    {
        var def = SettingDefinitions.Find(name);
        if (def == null) { Fail($"Unknown setting '{name}'. Settings: {string.Join(", ", SettingDefinitions.All.Select(d => d.Name))}."); return; }
        if (verb != "set" && !def.IsList) { Fail($"'{def.Name}' isn't a list; use 'settings set {def.Name} <value>'."); return; }
        if (!def.IsList && values.Length != 1) { Fail($"'{def.Name}' takes one value."); return; }

        // formats may be given as "html,pdf" as well as "html pdf"
        var given = values.SelectMany(v => def.Name == "formats" ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [v.Trim()])
            .Where(v => v.Length > 0).ToList();
        // A path in the per-user file is stored absolute; in the local file as given (relative to this folder)
        if (def.IsPath && !local) given = given.Select(Path.GetFullPath).ToList();

        if (given.Count > 0 && def.Validate(given) is { } error) { Fail(error); return; }

        AppSettings settings;
        try { settings = store.Read(local) ?? new AppSettings(); }
        catch (InvalidDataException ex) { Fail(ex.Message); return; }

        var current = def.Get(settings) ?? [];
        var updated = verb switch
        {
            "add" => current.Concat(given.Where(g => !current.Contains(g, StringComparer.OrdinalIgnoreCase))).ToList(),
            "remove" => current.Where(c => !given.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList(),
            _ => given
        };
        if (verb == "remove" && updated.Count == current.Count)
            AnsiConsole.MarkupLine($"[yellow]Nothing removed: {Markup.Escape(string.Join(", ", given))} isn't in {def.Name}.[/]");

        def.Set(settings, updated.Count == 0 || def.IsDefault(updated) ? null : updated);
        Save(store, local, settings, def.Name);
    }

    private static void Reset(SettingsStore store, string? name, bool local)
    {
        AppSettings settings;
        if (name == null)
        {
            settings = new AppSettings();   // all defaults → the file is deleted
        }
        else
        {
            var def = SettingDefinitions.Find(name);
            if (def == null) { Fail($"Unknown setting '{name}'."); return; }
            try { settings = store.Read(local) ?? new AppSettings(); }
            catch (InvalidDataException) { settings = new AppSettings(); }   // a broken file can only be reset as a whole
            def.Set(settings, null);
            name = def.Name;
        }
        Save(store, local, settings, name ?? "all settings");
    }

    private static void Save(SettingsStore store, bool local, AppSettings settings, string what)
    {
        try
        {
            store.Write(local, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail($"Could not write {store.PathFor(local)}: {ex.Message}");
            return;
        }
        var path = store.PathFor(local);
        AnsiConsole.MarkupLine(File.Exists(path)
            ? $"[green]✓[/] {Markup.Escape(what)} saved in {Markup.Escape(path)}"
            : $"[green]✓[/] {Markup.Escape(what)} back to default — {(local ? "local" : "user")} settings file removed (nothing differs from the defaults)");

        if (local && File.Exists(path))
        {
            var inToolFolder = Path.GetFullPath(Path.GetDirectoryName(path)!).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(Locations.ExeDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            AnsiConsole.MarkupLine("[yellow]⚠ This settings file lives in the current folder" +
                                   (inToolFolder ? ", which is the tool's own folder" : "") +
                                   ". Unpacking a new version of the tool over this folder can overwrite or remove it — " +
                                   "keep a copy, or use the per-user settings (leave out --local).[/]");
        }
    }

    private static void Fail(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        Environment.ExitCode = 1;
    }
}
