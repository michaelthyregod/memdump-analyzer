using System.CommandLine;
using MemDumpAnalyzer.Cli.Commands;
using MemDumpAnalyzer.Cli.Settings;
using MemDumpAnalyzer.Reporting.Skins;

namespace MemDumpAnalyzer.Tests;

/// <summary>Settings files (user + local), the settings command, and the lookups that use them.</summary>
public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"memdump-analyzer-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "user"));
        Directory.CreateDirectory(Path.Combine(_dir, "local"));
        _store = new SettingsStore(Path.Combine(_dir, "user", "settings.json"), Path.Combine(_dir, "local", SettingsStore.LocalFileName));
        Environment.ExitCode = 0;
    }

    public void Dispose()
    {
        Environment.ExitCode = 0;
        Directory.Delete(_dir, recursive: true);
    }

    private int Run(params string[] args)
    {
        Environment.ExitCode = 0;
        var root = new RootCommand { SettingsCommand.Build(() => _store) };
        int code = root.Parse(["settings", .. args]).Invoke();
        return code != 0 ? code : Environment.ExitCode;
    }

    // ── Store ────────────────────────────────────────────────────────────────

    [Fact]
    public void Write_AllDefaults_CreatesNoFile_AndRemovesAnExistingOne()
    {
        _store.Write(local: false, new AppSettings { Skin = "idoedge" });
        Assert.True(File.Exists(_store.UserPath));

        _store.Write(local: false, new AppSettings());

        Assert.False(File.Exists(_store.UserPath));
    }

    [Fact]
    public void Write_StoresOnlyNonDefaultValues()
    {
        _store.Write(local: false, new AppSettings { Skin = "idoedge" });

        var json = File.ReadAllText(_store.UserPath);
        Assert.Contains("\"skin\": \"idoedge\"", json);
        Assert.DoesNotContain("formats", json);
        Assert.DoesNotContain("top", json);
        Assert.DoesNotContain("isEmpty", json);
    }

    [Fact]
    public void Load_LocalWinsPerSetting_ListsLocalFirst_PathsRelativeToTheirFile()
    {
        _store.Write(local: false, new AppSettings { Skin = "user-skin", Top = 50, SkinsDirs = ["user-skins"] });
        _store.Write(local: true, new AppSettings { Skin = "local-skin", SkinsDirs = ["local-skins"] });

        var s = _store.Load();

        Assert.Equal(("local-skin", SettingSource.Local), s.Skin);
        Assert.Equal((50, SettingSource.User), s.Top);                     // not in the local file → user value
        Assert.Equal(
            [Path.Combine(_dir, "local", "local-skins"), Path.Combine(_dir, "user", "user-skins")],
            s.SkinsDirs.Select(d => d.Path));
    }

    [Fact]
    public void Load_NoFiles_IsAllDefaults()
    {
        var s = _store.Load();

        Assert.Null(s.Skin.Value);
        Assert.Empty(s.SkinsDirs);
        Assert.Equal(Locations.DefaultTop, Locations.Top(null, s));
        Assert.Equal(["html", "json"], Locations.Formats(null, s));
    }

    [Fact]
    public void Load_BrokenFile_Throws()
    {
        File.WriteAllText(_store.UserPath, "{ nope");
        Assert.Throws<InvalidDataException>(() => _store.Load());
    }

    // ── settings command ─────────────────────────────────────────────────────

    [Fact]
    public void Set_WritesUserFileByDefault_AndSettingTheDefaultRemovesIt()
    {
        Assert.Equal(0, Run("set", "skin", "idoedge"));
        Assert.Equal("idoedge", _store.Read(local: false)!.Skin);
        Assert.False(File.Exists(_store.LocalPath));

        Assert.Equal(0, Run("set", "skin", "default"));
        Assert.False(File.Exists(_store.UserPath));                      // nothing differs → no file
    }

    [Fact]
    public void Local_WritesTheCurrentFolderFile()
    {
        Assert.Equal(0, Run("add", "skinsDirs", "team-skins", "--local"));

        Assert.Equal(["team-skins"], _store.Read(local: true)!.SkinsDirs);   // kept relative in the local file
        Assert.False(File.Exists(_store.UserPath));
    }

    [Fact]
    public void AddRemove_ListSetting_AndUserPathsBecomeAbsolute()
    {
        var a = Path.Combine(_dir, "a");
        var b = Path.Combine(_dir, "b");
        Run("add", "dumpsDirs", a, b);
        Run("add", "dumpsDirs", a);                                         // no duplicate
        Assert.Equal([a, b], _store.Read(local: false)!.DumpsDirs);

        Run("remove", "dumpsDirs", a, b);
        Assert.False(File.Exists(_store.UserPath));
    }

    [Fact]
    public void Set_Formats_AcceptsCommaList_AndRejectsUnknown()
    {
        Assert.Equal(0, Run("set", "formats", "html,pdf"));
        Assert.Equal(["html", "pdf"], _store.Read(local: false)!.Formats);

        Assert.Equal(1, Run("set", "formats", "docx"));
        Assert.Equal(["html", "pdf"], _store.Read(local: false)!.Formats);  // unchanged
    }

    [Theory]
    [InlineData("set", "top", "0")]
    [InlineData("set", "skin", @"C:\skins\acme")]
    [InlineData("set", "nosuchsetting", "x")]
    [InlineData("add", "skin", "x")]
    public void InvalidChanges_FailAndWriteNothing(string verb, string name, string value)
    {
        Assert.Equal(1, Run(verb, name, value));
        Assert.False(File.Exists(_store.UserPath));
    }

    [Fact]
    public void Reset_OneOrAll()
    {
        Run("set", "skin", "idoedge");
        Run("set", "top", "300");

        Run("reset", "skin");
        Assert.Null(_store.Read(local: false)!.Skin);
        Assert.Equal(300, _store.Read(local: false)!.Top);

        Run("reset");
        Assert.False(File.Exists(_store.UserPath));
    }

    // ── Lookups ──────────────────────────────────────────────────────────────

    [Fact]
    public void FindDump_UsesDumpsDirsForANameThatIsNotFoundAsGiven()
    {
        var dumps = Path.Combine(_dir, "dumps");
        Directory.CreateDirectory(dumps);
        File.WriteAllText(Path.Combine(dumps, "w3wp-test.dmp"), "x");
        _store.Write(local: false, new AppSettings { DumpsDirs = [dumps] });

        var found = Locations.FindDump("w3wp-test.dmp", _store.Load());

        Assert.Equal(Path.Combine(dumps, "w3wp-test.dmp"), found!.FullName);
        Assert.Null(Locations.FindDump("missing.dmp", _store.Load()));
    }

    [Fact]
    public void FindKnownAssemblies_CommandLineThenSetting()
    {
        _store.Write(local: false, new AppSettings { KnownAssemblies = Path.Combine(_dir, "filter.txt") });
        var s = _store.Load();

        Assert.Equal(Path.Combine(_dir, "filter.txt"), Locations.FindKnownAssemblies(null, s));
        Assert.Equal(Path.GetFullPath("cli.txt"), Locations.FindKnownAssemblies("cli.txt", s));
    }

    [Fact]
    public void SkinSearchFolders_CommandLineThenSettingsThenExeFolder()
    {
        _store.Write(local: false, new AppSettings { SkinsDirs = [Path.Combine(_dir, "from-settings")] });

        var folders = Locations.SkinSearchFolders([Path.Combine(_dir, "from-cli")], _store.Load());

        Assert.Equal(
            [Path.Combine(_dir, "from-cli"), Path.Combine(_dir, "from-settings"), Path.Combine(Locations.ExeDir, "skins")],
            folders);
    }

    [Fact]
    public void LoadByName_FirstFolderWins_PathsAreRejected()
    {
        foreach (var (folder, display) in new[] { ("first", "First"), ("second", "Second") })
        {
            var skin = Path.Combine(_dir, folder, "acme");
            Directory.CreateDirectory(skin);
            File.WriteAllText(Path.Combine(skin, "skin.json"), $$"""{ "displayName": "{{display}}" }""");
        }
        string[] folders = [Path.Combine(_dir, "first"), Path.Combine(_dir, "second")];

        Assert.Equal("First", SkinLoader.LoadByName("acme", folders).DisplayName);
        Assert.Equal("Default", SkinLoader.LoadByName(null, folders).DisplayName);
        Assert.Throws<ArgumentException>(() => SkinLoader.LoadByName(Path.Combine(_dir, "first", "acme"), folders));
        Assert.Throws<FileNotFoundException>(() => SkinLoader.LoadByName("nope", folders));
    }
}
