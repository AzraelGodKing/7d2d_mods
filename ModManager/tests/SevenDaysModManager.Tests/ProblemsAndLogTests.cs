namespace SevenDaysModManager.Tests;

public class ProblemsTests
{
    static HashSet<string> On(LibraryScan s) => s.Mods.Where(m => m.Enabled).Select(m => m.Id).ToHashSet();

    [Fact]
    public void NoProblemsForAPlainSetup()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "A", "A");
        var scan = ModLibrary.Scan(g.Install);
        Assert.Empty(Problems.Check(scan, On(scan), EacMode.Auto));
    }

    [Fact]
    public void TwoModsWithTheSameNameIsAnError()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Copy1", "Same");
        g.AddMod(g.Install.UserMods, "Copy2", "Same");
        var scan = ModLibrary.Scan(g.Install);
        var p = Assert.Single(Problems.Check(scan, On(scan), EacMode.Auto));
        Assert.Equal(Severity.Error, p.Severity);
        Assert.Equal("user/Copy2", p.ModId);
    }

    [Fact]
    public void CodeModsWithEacForcedOnAndNoHarmony()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Code", "Code", dll: true);
        Directory.Delete(Path.Combine(g.Install.GameMods, "0_TFP_Harmony"), true);
        var scan = ModLibrary.Scan(g.Install);
        var found = Problems.Check(scan, On(scan), EacMode.On);
        Assert.Contains(found, p => p.Text.Contains("EasyAntiCheat"));
        Assert.Contains(found, p => p.Fix == ProblemFix.VerifyGameFiles);
        Assert.DoesNotContain(Problems.Check(scan, On(scan), EacMode.Auto), p => p.Text.Contains("EasyAntiCheat"));
    }

    [Fact]
    public void NestedAndOldFormatMods()
    {
        using var g = new FakeGame();
        g.AddMod(Path.Combine(g.Install.GameMods, "Outer"), "Inner", "Inner");
        var old = Path.Combine(g.Install.GameMods, "Old");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "ModInfo.xml"), """<xml><ModInfo><Name value="Old" /></ModInfo></xml>""");
        var scan = ModLibrary.Scan(g.Install);
        var found = Problems.Check(scan, On(scan), EacMode.Auto);
        Assert.Contains(found, p => p.ModId == "game/Outer" && p.Fix == ProblemFix.MoveNestedUp);
        Assert.Contains(found, p => p.ModId == "game/Old" && p.Severity == Severity.Warning);
    }

    [Fact]
    public void DisabledModsDontCount()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Copy1", "Same");
        g.AddMod(g.Disabled(ModLocation.Game), "Copy2", "Same");
        var scan = ModLibrary.Scan(g.Install);
        Assert.Empty(Problems.Check(scan, On(scan), EacMode.Auto));
    }
}

public class LogAnalysisTests
{
    [Fact]
    public void GroupsErrorsAndBlamesTheModNamedOrWhoseFolderIsMentioned()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "BetterLoot", "BetterLoot");
        g.AddMod(g.Install.GameMods, "Speedometer", "Speedometer");
        var mods = ModLibrary.Scan(g.Install).Mods;
        var lines = new[]
        {
            "2024-07-29T20:47:41 21.590 INF Loading mods",
            "2024-07-29T20:47:42 21.600 ERR XML loader: Patching 'loot.xml' from mod 'BetterLoot' failed:",
            "2024-07-29T20:47:42 21.601 WRN Couldn't load texture Mods/Speedometer/UI/needle.png",
            "2024-07-29T20:47:43 22.000 EXC NullReferenceException: Object reference not set to an instance of an object",
            "  at EntityAlive.Update () [0x00000] in <abc>:0",
            "2024-07-29T20:47:44 23.000 EXC NullReferenceException: Object reference not set to an instance of an object",
            "  at EntityAlive.Update () [0x00000] in <abc>:0",
        };
        var groups = LogAnalysis.Analyze(lines, mods);

        Assert.Equal(3, groups.Count);
        Assert.Equal("game/BetterLoot", groups.Single(x => x.Message.Contains("Patching")).ModId);
        Assert.Equal("game/Speedometer", groups.Single(x => x.Severity == Severity.Warning).ModId);
        var nre = groups.Single(x => x.Message.StartsWith("NullReference"));
        Assert.Equal(2, nre.Count);
        Assert.Null(nre.ModId);
        Assert.Contains("EntityAlive.Update", nre.Detail);
    }

    [Fact]
    public void BlamesCodeByTheNamespacesInTheModsDll()
    {
        using var g = new FakeGame();
        var folder = g.AddMod(g.Install.GameMods, "Real", "Real");
        // This test assembly's own namespace stands in for a mod's code.
        File.Copy(typeof(LogAnalysisTests).Assembly.Location, Path.Combine(folder, "Real.dll"));
        var mods = ModLibrary.Scan(g.Install).Mods;
        var lines = new[]
        {
            "2024-07-29T20:47:43 22.000 EXC InvalidOperationException: nope",
            "  at SevenDaysModManager.Tests.Something.Prefix () [0x00000] in <abc>:0",
        };
        var group = Assert.Single(LogAnalysis.Analyze(lines, mods));
        Assert.Equal("game/Real", group.ModId);
        Assert.Equal("its code is in the stack trace", group.Why);
    }
}

public class LogLinesTests
{
    static readonly string[] Log =
    {
        "Mono path[0] = 'C:/Games/7 Days To Die/7DaysToDie_Data/Managed'",
        "2024-07-29T20:47:41 21.590 INF [MODS] Loaded Mod: BetterLoot (1.0)",
        "2024-07-29T20:47:42 21.600 ERR XML loader: Patching 'loot.xml' from mod 'BetterLoot' failed:",
        "System.Xml.XPath.XPathException: bad xpath",
        "  at XmlPatcher.Patch () [0x00000] in <abc>:0",
        "2024-07-29T20:47:42 21.601 WRN Couldn't load texture Mods/Speedometer/UI/needle.png",
        "2024-07-29T20:47:43 22.000 INF Done",
    };

    static List<LogLine> Lines(FakeGame g)
    {
        g.AddMod(g.Install.GameMods, "BetterLoot", "BetterLoot");
        g.AddMod(g.Install.GameMods, "Speedometer", "Speedometer");
        return LogAnalysis.Lines(Log, ModLibrary.Scan(g.Install).Mods);
    }

    [Fact]
    public void EveryLineGetsItsEntrysLevelAndMod()
    {
        using var g = new FakeGame();
        var lines = Lines(g);
        Assert.Equal(Log.Length, lines.Count);
        Assert.Equal(Enumerable.Range(1, Log.Length), lines.Select(l => l.Number));
        // The stack trace belongs to the XML error before it.
        Assert.All(lines.Skip(2).Take(3), l => Assert.Equal((LogLevel.Error, "game/BetterLoot", true), (l.Level, l.ModId, l.XmlPatch)));
        Assert.True(lines[2].Start);
        Assert.False(lines[3].Start);
        Assert.Equal("game/BetterLoot", lines[1].ModId);
        Assert.False(lines[1].XmlPatch);
        Assert.Equal(("game/Speedometer", LogLevel.Warning), (lines[5].ModId, lines[5].Level));
        Assert.Null(lines[0].ModId);
    }

    [Fact]
    public void FiltersByModLevelTextAndXml()
    {
        using var g = new FakeGame();
        var lines = Lines(g);
        Assert.Equal(new[] { 2, 3, 4, 5 }, LogAnalysis.Filter(lines, "game/BetterLoot", LogLevel.Info, null).Select(l => l.Number));
        Assert.Equal(new[] { 3, 4, 5, 6 }, LogAnalysis.Filter(lines, null, LogLevel.Warning, null).Select(l => l.Number));
        Assert.Equal(new[] { 3, 4, 5 }, LogAnalysis.Filter(lines, null, LogLevel.Error, null).Select(l => l.Number));
        Assert.Equal(new[] { 6 }, LogAnalysis.Filter(lines, null, LogLevel.Info, "needle").Select(l => l.Number));
        Assert.Equal(new[] { 3, 4, 5 }, LogAnalysis.Filter(lines, null, LogLevel.Info, null, xmlOnly: true).Select(l => l.Number));
    }
}
