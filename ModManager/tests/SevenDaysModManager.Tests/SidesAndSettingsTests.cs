namespace SevenDaysModManager.Tests;

public class ModSidesTests
{
    [Theory]
    [InlineData("A craftable throwable bait. Server only: players install nothing.", ModSide.Server)]
    [InlineData("Lets players craft picture signs. Server-side: players install nothing.", ModSide.Server)]
    [InlineData("Server-side fixes for ghost players. Players do not need to install anything.", ModSide.Server)]
    [InlineData("Servers: only the server needs this mod.", ModSide.Server)]
    [InlineData("Replaces the ding with a horn. Server only: the server sends the sound setting to players, so nobody installs anything.", ModSide.Server)]
    [InlineData("Optional client side add-on for Bait. Needs the Bait mod on the server. The server does not need this.", ModSide.Client)]
    [InlineData("Shows your speed on the HUD. Only players need it; the server does not.", ModSide.Client)]
    [InlineData("Crafting pulls from chests. Only the players who want it install it - the server needs nothing.", ModSide.Client)]
    [InlineData("Adds an Upgrade Bench. Install on the server AND every player's game.", ModSide.Both)]
    public void ReadsTheSideFromTheDescription(string description, ModSide side)
    {
        var s = ModSides.FromWords(description);
        Assert.NotNull(s);
        Assert.Equal(side, s!.Side);
        Assert.False(s.Guessed);
    }

    [Fact]
    public void NoWordsMeansNoAnswer() => Assert.Null(ModSides.FromWords("Gyrocopters no longer damage blocks."));

    [Fact]
    public void GuessesFromFiles()
    {
        using var g = new FakeGame();
        var xml = g.AddMod(g.Install.GameMods, "Xml", "Xml");
        Directory.CreateDirectory(Path.Combine(xml, "Config"));
        File.WriteAllText(Path.Combine(xml, "Config", "items.xml"), "<configs/>");
        var hud = g.AddMod(g.Install.GameMods, "Hud", "Hud");
        Directory.CreateDirectory(Path.Combine(hud, "Config", "XUi_InGame"));
        File.WriteAllText(Path.Combine(hud, "Config", "XUi_InGame", "windows.xml"), "<configs/>");
        var code = g.AddMod(g.Install.GameMods, "Code", "Code", dll: true);

        Assert.Equal(ModSide.Server, ModSides.FromFiles(xml, false).Side);
        Assert.Equal(ModSide.Client, ModSides.FromFiles(hud, false).Side);
        Assert.Equal(ModSide.Both, ModSides.FromFiles(code, true).Side);
        Assert.True(ModSides.FromFiles(xml, false).Guessed);
    }

    [Fact]
    public void ThePlayersChoiceWins()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "A", "A");
        var mod = ModLibrary.Scan(g.Install).Mods.Single(m => m.Key == "A");
        var s = ModSides.Of(mod, ModSide.Client);
        Assert.Equal(ModSide.Client, s.Side);
        Assert.Equal("you set it", s.Why);
    }
}

public class ModSettingsTests
{
    const string Settings = "# Craft From Chests settings.\r\n#\r\n# on / off for each line.\r\n\r\n# Turn the whole mod on or off.\r\nEnabled=on\r\n\r\n# Bag time.\r\n# A plain number is seconds.\r\nDroppedItems = 24h\r\n# Prefix=/dai\r\nAllowAllies=True\r\n";

    [Fact]
    public void ReadsSettingsWithTheirComments()
    {
        var doc = SettingsDoc.Parse(Settings);
        Assert.Equal(new[] { "Enabled", "DroppedItems", "AllowAllies" }, doc.Entries.Select(e => e.Key));
        Assert.Equal("Turn the whole mod on or off.", doc.Entries[0].Description);
        Assert.Equal(new[] { "on", "off" }, doc.Entries[0].Choices);
        Assert.Equal("Bag time.\nA plain number is seconds.", doc.Entries[1].Description);
        Assert.Null(doc.Entries[1].Choices);
        Assert.Equal(new[] { "True", "False" }, doc.Entries[2].Choices);
    }

    [Fact]
    public void ChangingAValueKeepsEverythingElse()
    {
        var doc = SettingsDoc.Parse(Settings);
        Assert.True(doc.Set("droppeditems", "90m"));
        Assert.False(doc.Set("Missing", "x"));
        Assert.Equal(Settings.Replace("DroppedItems = 24h", "DroppedItems = 90m"), doc.ToText());
        Assert.Equal(Settings, SettingsDoc.Parse(Settings).ToText());
    }

    [Fact]
    public void FindsSettingsFilesButNotReadmesOrExamples()
    {
        using var g = new FakeGame();
        var mod = g.AddMod(g.Install.GameMods, "M", "M");
        foreach (var f in new[] { "settings.txt", "Mine.cfg", "discord.example.txt", "README.txt", "PUT_YOUR_SOUND_HERE.txt", "notes.md" })
            File.WriteAllText(Path.Combine(mod, f), "x");
        Assert.Equal(new[] { "Mine.cfg", "settings.txt" }, ModSettingsStore.FilesOf(mod).Select(Path.GetFileName));
    }

    [Fact]
    public void PutsYourSettingsBackAfterAnUpdate()
    {
        using var g = new FakeGame();
        var store = new ModSettingsStore(Path.Combine(g.Root, "kept"));
        var mod = g.AddMod(g.Install.GameMods, "M", "M");
        var settings = Path.Combine(mod, "settings.txt");
        var list = Path.Combine(mod, "entities.txt");
        File.WriteAllText(settings, "A=on\nB=1\n");
        File.WriteAllText(list, "#zombieDog\n");
        store.Save("M", settings, "A=off\nB=1\n");
        store.Save("M", list, "zombieDog\n");

        // The update brings a new setting and a changed comment, and resets the old ones.
        File.WriteAllText(settings, "# new comment\nA=on\nB=2\nC=new\n");
        File.WriteAllText(list, "#zombieDog\n#zombieCat\n");
        var changed = store.Restore("M", mod);

        Assert.Equal(new[] { "entities.txt", "settings.txt" }, changed.OrderBy(x => x));
        Assert.Equal("# new comment\nA=off\nB=1\nC=new\n", File.ReadAllText(settings));
        Assert.Equal("zombieDog\n", File.ReadAllText(list));
        Assert.True(store.HasSaved("M"));
        store.Forget("M");
        Assert.False(store.HasSaved("M"));
    }
}
