namespace SevenDaysModManager.Tests;

public class ModInfoTests
{
    [Fact]
    public void ReadsFormat2()
    {
        var i = ModInfo.Parse(FakeGame.ModInfoXml("Speedo", "Speedometer", "1.1.1"));
        Assert.Equal("Speedo", i.Name);
        Assert.Equal("Speedometer", i.DisplayName);
        Assert.Equal("1.1.1", i.Version);
        Assert.Equal("Someone", i.Author);
        Assert.Equal(2, i.Format);
    }

    [Fact]
    public void ReadsFormat1AndUsesNameAsDisplayName()
    {
        var i = ModInfo.Parse("""<xml><ModInfo><Name value="OldMod" /><Version value="0.3" /></ModInfo></xml>""");
        Assert.Equal("OldMod", i.Name);
        Assert.Equal("OldMod", i.DisplayName);
        Assert.Equal(1, i.Format);
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<xml><Version value=\"1\" /></xml>")]
    public void RejectsBadFiles(string text) => Assert.Throws<FormatException>(() => ModInfo.Parse(text));
}

public class ModLibraryTests
{
    [Fact]
    public void ScansBothModsFoldersAndDisabledOnes()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Speedometer", "Speedometer", dll: true);
        g.AddMod(g.Install.UserMods, "BetterLoot", "BetterLoot");
        g.AddMod(g.Disabled(ModLocation.Game), "OldThing", "OldThing");
        Directory.CreateDirectory(Path.Combine(g.Install.GameMods, "NotAMod"));

        var scan = ModLibrary.Scan(g.Install);

        Assert.Equal(5, scan.Mods.Count);
        var speedo = scan.Mods.Single(m => m.Key == "Speedometer");
        Assert.True(speedo.Enabled);
        Assert.True(speedo.HasCode);
        Assert.Equal("game/Speedometer", speedo.Id);
        Assert.Equal(ModLocation.UserData, scan.Mods.Single(m => m.Key == "BetterLoot").Location);
        Assert.False(scan.Mods.Single(m => m.Key == "OldThing").Enabled);
        Assert.Null(scan.Mods.Single(m => m.FolderName == "NotAMod").Info);
        Assert.True(scan.Mods.Single(m => m.FolderName == "0_TFP_Harmony").Locked);
    }

    [Fact]
    public void LoadOrderIsByFolderNameGameFolderFirst()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Zed", "Zed");
        g.AddMod(g.Install.GameMods, "alpha", "alpha");
        g.AddMod(g.Install.UserMods, "Aardvark", "Aardvark");
        var order = ModLibrary.Scan(g.Install).LoadOrder().Select(m => m.FolderName).ToList();
        Assert.Equal(new[] { "0_TFP_Harmony", "alpha", "Zed", "Aardvark" }, order);
    }

    [Fact]
    public void ApplyMovesFoldersBetweenModsAndModsDisabled()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "On", "On");
        g.AddMod(g.Disabled(ModLocation.Game), "Off", "Off");
        var scan = ModLibrary.Scan(g.Install);
        // Asking for Harmony to be off is ignored: it is locked.
        var moved = ModLibrary.Apply(scan, new HashSet<string> { "game/Off" });

        Assert.Equal(2, moved);
        Assert.True(Directory.Exists(Path.Combine(g.Install.GameMods, "Off")));
        Assert.True(Directory.Exists(Path.Combine(g.Disabled(ModLocation.Game), "On")));
        Assert.True(Directory.Exists(Path.Combine(g.Install.GameMods, "0_TFP_Harmony")));
    }

    [Fact]
    public void FindsAndFixesAModUnpackedOneFolderTooDeep()
    {
        using var g = new FakeGame();
        var outer = Path.Combine(g.Install.GameMods, "CoolMod-1.2");
        g.AddMod(outer, "CoolMod", "CoolMod");
        File.WriteAllText(Path.Combine(outer, "readme.txt"), "hi");

        var mod = ModLibrary.Scan(g.Install).Mods.Single(m => m.FolderName == "CoolMod-1.2");
        Assert.NotNull(mod.NestedFolder);
        Assert.Equal("CoolMod", mod.Key); // read from the inner ModInfo.xml

        var now = ModLibrary.FixNested(mod, g.Backups);

        Assert.Equal(Path.Combine(g.Install.GameMods, "CoolMod"), now);
        Assert.False(Directory.Exists(outer));
        Assert.True(File.Exists(Path.Combine(now, "ModInfo.xml")));
        Assert.Single(Directory.GetFiles(g.Backups, "CoolMod-1.2-leftovers-*.zip"));
        Assert.Null(ModLibrary.Scan(g.Install).Mods.Single(m => m.Key == "CoolMod").NestedFolder);
    }

    [Fact]
    public void FixNestedWhenTheInnerFolderHasTheSameName()
    {
        using var g = new FakeGame();
        var outer = Path.Combine(g.Install.GameMods, "Same");
        g.AddMod(outer, "Same", "Same");
        var mod = ModLibrary.Scan(g.Install).Mods.Single(m => m.FolderName == "Same");
        var now = ModLibrary.FixNested(mod, g.Backups);
        Assert.True(File.Exists(Path.Combine(now, "ModInfo.xml")));
        Assert.False(Directory.Exists(g.Backups));
    }
}
