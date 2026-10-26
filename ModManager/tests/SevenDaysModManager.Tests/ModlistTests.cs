namespace SevenDaysModManager.Tests;

public class ModlistTests
{
    [Fact]
    public void ShareCodeRoundTrips()
    {
        var list = new Modlist { Name = "Horde night", Mods = { new ModlistEntry("Speedometer", "Speedometer", "1.1.1"), new ModlistEntry("KeepBackpacks") } };
        var code = list.ToShareCode();
        Assert.StartsWith(Modlist.SharePrefix, code);
        var back = Modlist.Parse(code);
        Assert.Equal("Horde night", back.Name);
        Assert.Equal(list.Mods, back.Mods);
    }

    [Fact]
    public void ADamagedShareCodeSaysSo() =>
        Assert.Throws<FormatException>(() => Modlist.FromShareCode(Modlist.SharePrefix + "not-a-real-code"));

    [Fact]
    public void LeavesOutTheGamesOwnModsAndMatchesInstalledOnes()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "A", "A");
        g.AddMod(g.Disabled(ModLocation.Game), "B", "B");
        var scan = ModLibrary.Scan(g.Install);
        var list = Modlist.From("mine", scan.LoadOrder());
        Assert.Equal(new[] { "A" }, list.Mods.Select(m => m.Name));

        list.Mods.Add(new ModlistEntry("B"));
        list.Mods.Add(new ModlistEntry("Gone"));
        var (installed, missing) = list.Match(scan);
        Assert.Equal(new[] { "game/A", "game/B" }, installed);
        Assert.Equal("Gone", Assert.Single(missing).Name);
        Assert.Contains("game/0_TFP_Harmony", list.EnabledIds(scan));
    }

    [Fact]
    public void StoreSavesAndReadsLists()
    {
        using var g = new FakeGame();
        var store = new ModlistStore(Path.Combine(g.Root, "lists"));
        store.Save(new Modlist { Name = "A/B list", Mods = { new ModlistEntry("X") } });
        Assert.Equal("A/B list", Assert.Single(store.All()).Name);
        store.Delete("A/B list");
        Assert.Empty(store.All());
    }

    [Fact]
    public void AProfileKeepsItsPlaySettingsThroughAFileAndAShareCode()
    {
        var list = new Modlist { Name = "Server", Eac = EacMode.Off, LaunchOptions = "-skipintro", Mods = { new ModlistEntry("A") } };
        foreach (var back in new[] { Modlist.Parse(list.ToJson()), Modlist.Parse(list.ToShareCode()) })
        {
            Assert.Equal(EacMode.Off, back.Eac);
            Assert.Equal("-skipintro", back.LaunchOptions);
        }
        Assert.Contains("\"Off\"", list.ToJson());
    }

    [Fact]
    public void AProfileWithoutPlaySettingsUsesTheAppsOnes()
    {
        var s = new AppSettings { Eac = EacMode.On, LaunchOptions = "-a" };
        var plain = Modlist.Parse("""{ "name": "Old", "mods": [ { "name": "A" } ] }""");
        Assert.Null(plain.Eac);
        Assert.Equal((EacMode.On, "-a"), plain.PlaySettings(s));
        Assert.Equal((EacMode.Off, "-a"), new Modlist { Eac = EacMode.Off }.PlaySettings(s));
        Assert.Equal((EacMode.On, ""), new Modlist { LaunchOptions = "" }.PlaySettings(s));
    }

    [Fact]
    public void VanillaTurnsEveryModOffButTheGamesOwn()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "A", "A");
        var scan = ModLibrary.Scan(g.Install);
        var store = new ModlistStore(Path.Combine(g.Root, "lists"));
        var vanilla = store.Get("vanilla");
        Assert.NotNull(vanilla);
        Assert.Equal(new[] { "game/0_TFP_Harmony" }, vanilla!.EnabledIds(scan));
        Assert.Throws<IOException>(() => store.Save(new Modlist { Name = Modlist.VanillaName }));
        Assert.Empty(store.All());
    }
}
