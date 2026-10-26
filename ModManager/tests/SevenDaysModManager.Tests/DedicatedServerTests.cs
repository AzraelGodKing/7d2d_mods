namespace SevenDaysModManager.Tests;

public class DedicatedServerTests
{
    const string Config = """
        <?xml version="1.0"?>
        <ServerSettings>
          <property name="ServerName" value="Azrael's horde" />
          <property name="ServerPort" value="26900" />
          <!-- <property name="Commented" value="out" /> -->
          <property name="EACEnabled" value="false" />
        </ServerSettings>
        """;

    static string Server(FakeGame g, bool serverExe)
    {
        var dir = Path.Combine(g.Root, "server");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, serverExe ? DedicatedServer.ServerExe : SevenDays.Exe), "");
        File.WriteAllText(Path.Combine(dir, DedicatedServer.DefaultConfig), Config);
        return dir;
    }

    [Fact]
    public void FindsTheServerAndTheProgramThatRunsIt()
    {
        using var g = new FakeGame();
        var noSteam = new SteamLibraries(null, Array.Empty<string>());
        Assert.Null(DedicatedServer.Find(noSteam, null, g.Install));
        // The game folder counts once it has a serverconfig.xml.
        File.WriteAllText(Path.Combine(g.Install.GameFolder, DedicatedServer.DefaultConfig), Config);
        Assert.Equal(Path.Combine(g.Install.GameFolder, SevenDays.Exe), DedicatedServer.Find(noSteam, null, g.Install)!.Exe);

        var dir = Server(g, serverExe: true);
        var s = DedicatedServer.Find(noSteam, dir, g.Install)!;
        Assert.Equal(Path.Combine(dir, DedicatedServer.ServerExe), s.Exe);
        Assert.Equal(Path.Combine(dir, "Mods"), s.Mods);
    }

    [Fact]
    public void StartsLikeStartDedicatedBat()
    {
        using var g = new FakeGame();
        var s = DedicatedServer.At(Server(g, serverExe: true));
        var l = DedicatedServer.Launch(s, "myconfig.xml", "-port=26901 \"-name=two words\"", new DateTime(2026, 10, 26, 9, 5, 7));
        Assert.Equal(s.Exe, l.Exe);
        Assert.Equal(s.Folder, l.WorkingDirectory);
        Assert.Equal(Path.Combine(s.Folder, "7DaysToDieServer_Data", "output_log_dedi__2026-10-26__09-05-07.txt"), l.Args[1]);
        Assert.Equal(new[] { "-quit", "-batchmode", "-nographics", "-configfile=myconfig.xml", "-dedicated", "-port=26901", "-name=two words" }, l.Args.Skip(2));
        Assert.Contains("-configfile=serverconfig.xml", DedicatedServer.Launch(s, null, null, DateTime.Now).Args);
    }

    [Fact]
    public void ReadsTheConfigsProperties()
    {
        using var g = new FakeGame();
        var p = DedicatedServer.ReadConfig(Path.Combine(Server(g, true), DedicatedServer.DefaultConfig));
        Assert.Equal(new[] { ("ServerName", "Azrael's horde"), ("ServerPort", "26900"), ("EACEnabled", "false") }, p);
        var bad = Path.Combine(g.Root, "bad.xml");
        File.WriteAllText(bad, "<ServerSettings>");
        Assert.Throws<FormatException>(() => DedicatedServer.ReadConfig(bad));
    }

    [Fact]
    public void BacksUpTheConfigKeepingTheNewestAndRestoresIt()
    {
        using var g = new FakeGame();
        var config = Path.Combine(Server(g, true), DedicatedServer.DefaultConfig);
        var backups = Path.Combine(g.Root, "cfg-backups");
        var first = DedicatedServer.BackupConfig(config, backups, keep: 2);
        Assert.Equal(Config, File.ReadAllText(first));
        DedicatedServer.BackupConfig(config, backups, keep: 2);
        DedicatedServer.BackupConfig(config, backups, keep: 2);
        Assert.Equal(2, DedicatedServer.ConfigBackups(backups).Count);

        File.WriteAllText(config, "<ServerSettings><property name=\"ServerName\" value=\"changed\" /></ServerSettings>");
        var before = DedicatedServer.RestoreConfig(DedicatedServer.ConfigBackups(backups)[0], config, backups);
        Assert.Equal(Config, File.ReadAllText(config));
        Assert.Contains("changed", File.ReadAllText(before!));

        var notConfig = Path.Combine(g.Root, "serverconfig-x.xml");
        File.WriteAllText(notConfig, "not xml");
        Assert.Throws<FormatException>(() => DedicatedServer.RestoreConfig(notConfig, config, backups));
        Assert.Equal(Config, File.ReadAllText(config));
    }

    [Fact]
    public void BacksUpTheNewestSave()
    {
        using var g = new FakeGame();
        var backups = Path.Combine(g.Root, "save-backups");
        Assert.Null(Saves.BackupNewest(g.Install, backups));
        var old = Path.Combine(g.Install.Saves, "Navezgane", "Old");
        var recent = Path.Combine(g.Install.Saves, "Navezgane", "Recent");
        foreach (var d in new[] { old, recent }) { Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "main.ttw"), "x"); }
        File.SetLastWriteTime(Path.Combine(old, "main.ttw"), DateTime.Now.AddDays(-2));
        var zip = Saves.BackupNewest(g.Install, backups);
        Assert.StartsWith("Navezgane-Recent-", Path.GetFileName(zip));
    }
}
