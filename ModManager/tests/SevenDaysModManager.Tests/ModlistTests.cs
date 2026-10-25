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
}
