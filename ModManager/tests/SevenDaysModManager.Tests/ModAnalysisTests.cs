namespace SevenDaysModManager.Tests;

public class ModAnalysisTests
{
    static string Mod(FakeGame g, string name, string? description = null, params (string File, string Xml)[] config)
    {
        var d = g.AddMod(g.Install.GameMods, name, name);
        if (description != null)
            File.WriteAllText(Path.Combine(d, "ModInfo.xml"), $"""<xml><Name value="{name}" /><DisplayName value="{name}" /><Version value="1.0" /><Description value="{description}" /></xml>""");
        foreach (var (file, xml) in config)
        {
            var path = Path.Combine(d, "Config", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
        }
        return d;
    }

    static List<ModProblem> Check(FakeGame g, GameVersion? game = null)
    {
        var scan = ModLibrary.Scan(g.Install);
        return Problems.Check(scan, scan.Mods.Where(m => m.Enabled).Select(m => m.Id).ToHashSet(), EacMode.Auto, new ModAnalysis(game));
    }

    [Fact]
    public void TwoModsSettingTheSameXPathConflictWhateverTheQuotesAndSpaces()
    {
        using var g = new FakeGame();
        Mod(g, "A_First", config: ("items.xml", """<configs><set xpath="/items/item[@name='gunPistol']/property[@name='Weight']/@value">2</set></configs>"""));
        Mod(g, "B_Second", config: ("items.xml", """<configs><set xpath = "/items/item[ @name=&quot;gunPistol&quot; ]/property[@name='Weight']/@value" >3</set></configs>"""));
        var p = Assert.Single(Check(g));
        Assert.Equal(Severity.Warning, p.Severity);
        Assert.Equal("game/B_Second", p.ModId);
        Assert.Contains("B_Second loads last", p.Text);
    }

    [Fact]
    public void AppendsToTheSamePlaceAreFineButTheSameNewItemIsNot()
    {
        using var g = new FakeGame();
        Mod(g, "A", config: ("items.xml", """<configs><append xpath="/items"><item name="mine" /></append></configs>"""));
        Mod(g, "B", config: ("items.xml", """<configs><append xpath="/items"><item name="theirs" /></append></configs>"""));
        Assert.Empty(Check(g));
        Mod(g, "C", config: ("items.xml", """<configs><append xpath="/items"><item name="mine" /></append></configs>"""));
        var p = Assert.Single(Check(g));
        Assert.Contains("item \"mine\"", p.Text);
    }

    [Fact]
    public void SetAttributeOnDifferentAttributesIsNoConflict()
    {
        using var g = new FakeGame();
        Mod(g, "A", config: ("blocks.xml", """<configs><setattribute xpath="/blocks/block[@name='x']" name="a">1</setattribute></configs>"""));
        Mod(g, "B", config: ("blocks.xml", """<configs><setattribute xpath="/blocks/block[@name='x']" name="b">1</setattribute></configs>"""));
        Assert.Empty(Check(g));
    }

    [Fact]
    public void BrokenXmlIsAnError()
    {
        using var g = new FakeGame();
        Mod(g, "Broken", config: ("XUi/windows.xml", "<configs><append xpath=\"/windows\"></configs>"));
        var p = Assert.Single(Check(g));
        Assert.Equal(Severity.Error, p.Severity);
        Assert.Contains("Config/XUi/windows.xml", p.Text);
    }

    [Fact]
    public void AModThatNeedsAnotherSaysSoWhenItIsDisabledOrMissing()
    {
        using var g = new FakeGame();
        Mod(g, "AzraelBaitUI", "Optional client side add-on. Needs the Bait mod on the server. The server does not need this.");
        Assert.Empty(Check(g)); // needed on the server, and not installed here: fine for a player.

        g.AddMod(g.Disabled(ModLocation.Game), "Bait", "AzraelBait");
        var p = Assert.Single(Check(g));
        Assert.Equal(Severity.Info, p.Severity);
        Assert.Contains("needs AzraelBait on the server", p.Text);

        Mod(g, "Gun", "Requires the SCore mod.");
        Assert.Contains(Check(g), x => x.ModId == "game/Gun" && x.Text.Contains("SCore") && x.Severity == Severity.Warning);
        Mod(g, "SCore", "Core library");
        Assert.DoesNotContain(Check(g), x => x.ModId == "game/Gun");
    }

    [Fact]
    public void RequirementsThatArentModsAreIgnored()
    {
        using var g = new FakeGame();
        Mod(g, "Code", "Requires EAC to be off. Needs a new game.");
        Assert.Empty(Check(g));
    }

    [Theory]
    [InlineData("2024-07-29T20:47:41 0.123 INF Version: V 1.4 (b8) Compatibility Version: V 1.4, Build: Windows 64 Bit", false, 1, 4)]
    [InlineData("2023-01-02T10:00:00 0.1 INF Version: Alpha 21.2 (b30) Compatibility Version: Alpha 21.2, Build: Windows 64 Bit", true, 21, 2)]
    public void ReadsTheGameVersionFromTheLog(string line, bool alpha, int major, int minor)
    {
        var v = ModAnalysis.ReadGameVersion(new[] { "other line", line });
        Assert.NotNull(v);
        Assert.Equal((alpha, major, minor), (v!.Alpha, v.Major, v.Minor));
    }

    [Fact]
    public void ModsForAnOlderGameAreFlagged()
    {
        using var g = new FakeGame();
        Mod(g, "Old", "Bigger backpack for A20.");
        Mod(g, "New", "Bigger backpack, updated for V1.0.");
        Mod(g, "Plain", "Version 1.2.3 of my mod.");
        var v1 = new GameVersion("V 1.4 (b8)", false, 1, 4);
        var p = Assert.Single(Check(g, v1));
        Assert.Equal("game/Old", p.ModId);
        Assert.Contains("Alpha 20", p.Text);
        Assert.Contains(Check(g, new GameVersion("V 2.0", false, 2, 0)), x => x.ModId == "game/New");
        Assert.Empty(Check(g, new GameVersion("Alpha 20.7", true, 20, 7)));
    }
}
