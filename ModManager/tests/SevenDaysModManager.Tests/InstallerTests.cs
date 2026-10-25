namespace SevenDaysModManager.Tests;

public class InstallerTests
{
    [Fact]
    public void InstallsAModFromAZipWithExtraFolders()
    {
        using var g = new FakeGame();
        var zip = g.Zip("cool.zip", ("CoolMod-v2/CoolMod/ModInfo.xml", FakeGame.ModInfoXml("CoolMod")), ("CoolMod-v2/CoolMod/Config/items.xml", "<configs/>"), ("CoolMod-v2/README.txt", "read me"));
        var done = ModInstaller.Install(zip, ModLibrary.Scan(g.Install), g.Install.GameMods, g.Backups);

        var r = Assert.Single(done);
        Assert.Null(r.Replaced);
        Assert.True(File.Exists(Path.Combine(g.Install.GameMods, "CoolMod", "Config", "items.xml")));
    }

    [Fact]
    public void AZipWithModInfoAtTheTopIsNamedAfterTheMod()
    {
        using var g = new FakeGame();
        var zip = g.Zip("whatever.zip", ("ModInfo.xml", FakeGame.ModInfoXml("TopMod")));
        ModInstaller.Install(zip, ModLibrary.Scan(g.Install), g.Install.UserMods, g.Backups);
        Assert.True(File.Exists(Path.Combine(g.Install.UserMods, "TopMod", "ModInfo.xml")));
    }

    [Fact]
    public void InstallsEveryModInAPack()
    {
        using var g = new FakeGame();
        var zip = g.Zip("pack.zip", ("A/ModInfo.xml", FakeGame.ModInfoXml("A")), ("B/ModInfo.xml", FakeGame.ModInfoXml("B")), ("B/Sub/ModInfo.xml", FakeGame.ModInfoXml("NotSeparate")));
        var done = ModInstaller.Install(zip, ModLibrary.Scan(g.Install), g.Install.GameMods, g.Backups);
        Assert.Equal(new[] { "A", "B" }, done.Select(d => d.Name).OrderBy(n => n));
    }

    [Fact]
    public void ReplacesAnInstalledCopyInPlaceAfterABackup()
    {
        using var g = new FakeGame();
        g.AddMod(g.Disabled(ModLocation.Game), "Cool_Old", "CoolMod", version: "1.0");
        var zip = g.Zip("cool.zip", ("CoolMod/ModInfo.xml", FakeGame.ModInfoXml("CoolMod", version: "2.0")));

        var r = Assert.Single(ModInstaller.Install(zip, ModLibrary.Scan(g.Install), g.Install.GameMods, g.Backups));

        Assert.NotNull(r.Replaced);
        Assert.False(Directory.Exists(Path.Combine(g.Disabled(ModLocation.Game), "Cool_Old")));
        var now = ModLibrary.Scan(g.Install).Mods.Single(m => m.Key == "CoolMod");
        Assert.Equal("2.0", now.Info!.Version);
        Assert.False(now.Enabled); // stays disabled, as it was
        Assert.Single(Directory.GetFiles(g.Backups, "Cool_Old-*.zip"));
    }

    [Fact]
    public void RefusesAZipThatWritesOutsideTheModFolder()
    {
        using var g = new FakeGame();
        var zip = g.Zip("evil.zip", ("Evil/ModInfo.xml", FakeGame.ModInfoXml("Evil")), ("../../escape.dll", "x"));
        Assert.Throws<InvalidDataException>(() => ModInstaller.Install(zip, ModLibrary.Scan(g.Install), g.Install.GameMods, g.Backups));
        Assert.False(Directory.Exists(Path.Combine(g.Install.GameMods, "Evil")));
    }

    [Fact]
    public void RefusesInstallingAModOntoItself()
    {
        using var g = new FakeGame();
        var folder = g.AddMod(g.Install.GameMods, "Mine", "Mine");
        Assert.Throws<InvalidDataException>(() => ModInstaller.Install(folder, ModLibrary.Scan(g.Install), g.Install.GameMods, g.Backups));
        Assert.True(File.Exists(Path.Combine(folder, "ModInfo.xml")));
    }

    [Fact]
    public void SaysWhyItCantInstall()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(g.Root);
        var rar = Path.Combine(g.Root, "mod.rar");
        File.WriteAllText(rar, "");
        Assert.Contains("Unpack it first", ModInstaller.CantInstall(rar));
        var empty = g.Zip("empty.zip", ("readme.txt", "no mod here"));
        Assert.Throws<InvalidDataException>(() => ModInstaller.Install(empty, ModLibrary.Scan(g.Install), g.Install.GameMods, g.Backups));
    }
}
