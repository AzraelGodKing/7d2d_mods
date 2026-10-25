namespace SevenDaysModManager.Tests;

public class BisectTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void FindsTheBrokenModByHalving(int broken)
    {
        var mods = Enumerable.Range(0, 7).Select(i => "m" + i).ToList();
        var b = new Bisect(mods, mods);
        var rounds = 0;
        while (!b.Done && rounds++ < 10) b.Answer(b.Testing.Contains("m" + broken));
        Assert.Equal("m" + broken, b.Culprit);
        Assert.True(rounds <= 3);
        Assert.Equal(mods, b.Original);
    }

    [Fact]
    public void EachRoundKeepsTheGamesOwnModsOn()
    {
        var b = new Bisect(new[] { "a", "b" }, new[] { "a", "b" });
        Assert.Equal(new HashSet<string> { "a", "harmony" }, b.EnabledThisRound(new[] { "harmony" }));
    }

    [Fact]
    public void NeedsTwoMods() => Assert.Throws<InvalidOperationException>(() => new Bisect(new[] { "a" }, new[] { "a" }));
}

public class SevenDaysTests
{
    [Fact]
    public void SplitsLaunchOptions() =>
        Assert.Equal(new[] { "-skipintro", "-logfile", @"C:\my logs\out.txt" }, SevenDays.SplitArgs(@"-skipintro  -logfile ""C:\my logs\out.txt"""));

    [Theory]
    [InlineData(EacMode.Auto, true, false)]
    [InlineData(EacMode.Auto, false, true)]
    [InlineData(EacMode.On, true, true)]
    [InlineData(EacMode.Off, false, false)]
    public void PicksEac(EacMode mode, bool code, bool expected) => Assert.Equal(expected, SevenDays.UsesEac(mode, code));

    [Fact]
    public void WithoutEacStartsTheExeDirectlyElseGoesThroughSteam()
    {
        var install = new GameInstall(@"C:\Games\7DTD", @"C:\Users\me\AppData\Roaming\7DaysToDie");
        var direct = SevenDays.Launch(install, eac: false, "-skipintro", null);
        Assert.EndsWith(SevenDays.Exe, direct.Exe);
        Assert.Equal(new[] { "-skipintro" }, direct.Args);
        var steam = SevenDays.Launch(install, eac: true, null, null);
        Assert.True(steam.ThroughShell);
        Assert.Equal("steam://rungameid/251570", steam.Exe);
    }

    [Fact]
    public void FindsTheNewestLog()
    {
        using var g = new FakeGame();
        var logs = Path.Combine(g.Install.UserDataFolder, "logs");
        Directory.CreateDirectory(logs);
        var old = Path.Combine(logs, "output_log_client__2024-01-01.txt");
        var recent = Path.Combine(logs, "output_log_client__2024-02-01.txt");
        File.WriteAllText(old, "");
        File.WriteAllText(recent, "");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
        Assert.Equal(recent, SevenDays.LatestLog(g.Install));
    }

    [Fact]
    public void FindsTheGameThroughSteamLibraries()
    {
        using var g = new FakeGame();
        var steam = Path.Combine(g.Root, "Steam");
        var lib = Path.Combine(g.Root, "Lib");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", "7 Days To Die"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{steam.Replace("\\", "\\\\")}\" }} \"1\" {{ \"path\" \"{lib.Replace("\\", "\\\\")}\" }} }}");
        File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_251570.acf"), "\"AppState\" { \"appid\" \"251570\" \"installdir\" \"7 Days To Die\" }");
        var found = SevenDays.Detect(SteamLibraries.FromSteamFolder(steam), null, "data");
        Assert.Equal(Path.Combine(lib, "steamapps", "common", "7 Days To Die"), found?.GameFolder);
        Assert.Equal("data", found?.UserDataFolder);
    }
}

public class DisplayTests
{
    [Fact]
    public void HasEveryModWrightTheme() =>
        Assert.Equal(new[] { "Dark", "Light", "Kats", "Koneko", "Serina", "Wolfie", "Vickra", "Kitomi", "Szendoo", "Triphlx", "Tohma", "Skywolf", "Hunter", "System" }, Display.Themes.Select(t => t.Name));

    [Theory]
    [InlineData("wolfie", "Wolfie")]
    [InlineData("nonsense", "Dark")]
    [InlineData(null, "Dark")]
    public void NormalizesThemeNames(string? name, string expected) => Assert.Equal(expected, Display.NormalizeTheme(name));

    [Fact]
    public void SystemFollowsWindows()
    {
        Assert.Equal("Light", Display.ResolveTheme("System", true));
        Assert.Equal("Dark", Display.ResolveTheme("System", null));
    }
}

public class EditHistoryTests
{
    [Fact]
    public void UndoesAndRedoes()
    {
        var h = new EditHistory();
        var a = new HashSet<string> { "a" };
        var ab = new HashSet<string> { "a", "b" };
        h.Push(a);
        Assert.True(h.Undo(ab)!.SetEquals(a));
        Assert.True(h.Redo(a)!.SetEquals(ab));
        Assert.Null(h.Redo(ab));
    }
}

public class SavesTests
{
    [Fact]
    public void ListsAndBacksUpSaves()
    {
        using var g = new FakeGame();
        var save = Path.Combine(g.Install.Saves, "Navezgane", "MySave");
        Directory.CreateDirectory(save);
        File.WriteAllText(Path.Combine(save, "main.ttw"), "world");
        var s = Assert.Single(Saves.List(g.Install));
        Assert.Equal("Navezgane", s.World);
        Assert.Equal("MySave", s.Name);
        var zip = Saves.Backup(s, g.Backups, keep: 1);
        Assert.True(File.Exists(zip));
    }
}
