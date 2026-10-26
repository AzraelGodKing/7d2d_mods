using System.IO.Compression;

namespace SevenDaysModManager.Tests;

public class ServerSyncTests
{
    /// <summary>Sides by mod Name; anything not listed is Server + clients.</summary>
    static Func<InstalledMod, SideInfo> Sides(params (string Name, ModSide Side)[] sides) =>
        m => new SideInfo(sides.FirstOrDefault(s => s.Name == m.Key) is { Name: not null } s ? s.Side : ModSide.Both, false, "test");

    static (LibraryScan Scan, List<SyncItem> Items) Compare(FakeGame g, IModsFolder server, Func<InstalledMod, SideInfo> side)
    {
        var scan = ModLibrary.Scan(g.Install);
        return (scan, ServerSync.Compare(ServerSync.ServerMods(scan, side), scan.Mods, side, server));
    }

    [Fact]
    public void SendsTheServerModsAndThenFindsThemTheSame()
    {
        using var g = new FakeGame();
        var a = g.AddMod(g.Install.GameMods, "ServerMod", "ServerMod");
        Directory.CreateDirectory(Path.Combine(a, "Config"));
        File.WriteAllText(Path.Combine(a, "Config", "items.xml"), "<configs/>");
        g.AddMod(g.Install.GameMods, "HudMod", "HudMod");
        g.AddMod(g.Disabled(ModLocation.Game), "Off", "Off");
        var side = Sides(("HudMod", ModSide.Client));
        var server = new LocalModsFolder(Path.Combine(g.Root, "server", "Mods"));

        var (_, items) = Compare(g, server, side);
        var item = Assert.Single(items);
        Assert.Equal(SyncAction.Upload, item.Action);
        Assert.Equal("ServerMod", item.Folder);
        Assert.Equal(new[] { "Config/items.xml", "ModInfo.xml" }, item.Send.OrderBy(p => p));

        var r = ServerSync.Push(items.Where(i => i.OnByDefault), server);
        Assert.Equal(2, r.Sent);
        Assert.True(File.Exists(Path.Combine(g.Root, "server", "Mods", "ServerMod", "Config", "items.xml")));
        Assert.Equal(SyncAction.Same, Assert.Single(Compare(g, server, side).Items).Action);
    }

    [Fact]
    public void SendsOnlyWhatChangedAndRemovesFilesTheModNoLongerHas()
    {
        using var g = new FakeGame();
        var a = g.AddMod(g.Install.GameMods, "A", "A");
        File.WriteAllText(Path.Combine(a, "keep.txt"), "same");
        File.WriteAllText(Path.Combine(a, "change.txt"), "old");
        var serverRoot = Path.Combine(g.Root, "server");
        var server = new LocalModsFolder(serverRoot);
        ServerSync.Push(Compare(g, server, Sides()).Items, server);

        File.WriteAllText(Path.Combine(a, "change.txt"), "a longer new text");
        File.WriteAllText(Path.Combine(serverRoot, "A", "stale.txt"), "left from an old version");
        var item = Assert.Single(Compare(g, server, Sides()).Items);
        Assert.Equal(SyncAction.Update, item.Action);
        Assert.Equal(new[] { "change.txt" }, item.Send);
        Assert.Equal(new[] { "stale.txt" }, item.Delete);

        ServerSync.Push(new[] { item }, server);
        Assert.Equal("a longer new text", File.ReadAllText(Path.Combine(serverRoot, "A", "change.txt")));
        Assert.False(File.Exists(Path.Combine(serverRoot, "A", "stale.txt")));
    }

    [Fact]
    public void ANewVersionSendsEveryFile()
    {
        using var g = new FakeGame();
        var a = g.AddMod(g.Install.GameMods, "A", "A", version: "1.1");
        File.WriteAllText(Path.Combine(a, "same.txt"), "x");
        var serverRoot = Path.Combine(g.Root, "server");
        var old = g.AddMod(serverRoot, "A", "A", version: "1.0");
        File.WriteAllText(Path.Combine(old, "same.txt"), "x");
        File.SetLastWriteTimeUtc(Path.Combine(old, "same.txt"), File.GetLastWriteTimeUtc(Path.Combine(a, "same.txt")));

        var item = Assert.Single(Compare(g, new LocalModsFolder(serverRoot), Sides()).Items);
        Assert.Equal(SyncAction.Update, item.Action);
        Assert.Equal("1.0", item.ServerVersion);
        Assert.Equal(2, item.Send.Count);
    }

    [Fact]
    public void ClientOnlyAndDisabledModsCanBeRemovedAndUnknownOnesAreLeft()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Hud", "Hud");
        g.AddMod(g.Disabled(ModLocation.Game), "Off", "Off");
        var serverRoot = Path.Combine(g.Root, "server");
        g.AddMod(serverRoot, "HudOnServer", "Hud");
        g.AddMod(serverRoot, "Off", "Off");
        g.AddMod(serverRoot, "TheirMod", "TheirMod");
        g.AddMod(serverRoot, "0_TFP_Harmony", "0_TFP_Harmony");
        var server = new LocalModsFolder(serverRoot);

        var items = Compare(g, server, Sides(("Hud", ModSide.Client))).Items;
        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i is { Action: SyncAction.Remove, Folder: "HudOnServer" } && i.Why.Contains("client"));
        Assert.Contains(items, i => i is { Action: SyncAction.Remove, Folder: "Off" } && i.Why.Contains("disabled"));
        Assert.Contains(items, i => i is { Action: SyncAction.Leave, Folder: "TheirMod" });
        Assert.All(items, i => Assert.False(i.OnByDefault));

        var r = ServerSync.Push(items.Where(i => i.Action == SyncAction.Remove), server);
        Assert.Equal(2, r.ModsRemoved);
        Assert.Equal(new[] { "0_TFP_Harmony", "TheirMod" }, Directory.GetDirectories(serverRoot).Select(Path.GetFileName).OrderBy(n => n));
    }

    [Fact]
    public void AModRenamedOnTheServerIsUpdatedWhereItIs()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Bait", "AzraelBait", version: "1.7.8");
        var serverRoot = Path.Combine(g.Root, "server");
        g.AddMod(serverRoot, "Bait-1.7.7", "AzraelBait", version: "1.7.7");
        var item = Assert.Single(Compare(g, new LocalModsFolder(serverRoot), Sides()).Items);
        Assert.Equal(SyncAction.Update, item.Action);
        Assert.Equal("Bait-1.7.7", item.Folder);
    }

    [Theory]
    [InlineData("drwxr-xr-x    2 ftp      ftp          4096 Oct 25 13:01 AzraelBait", "AzraelBait", true, 0)]
    [InlineData("-rw-r--r--    1 1000     1000          512 Jan  3  2025 ModInfo.xml", "ModInfo.xml", false, 512)]
    [InlineData("-rw-r--r-- 1 user group 77 Mar 1 09:00 name with spaces.txt", "name with spaces.txt", false, 77)]
    [InlineData("10-25-26  01:15PM       <DIR>          Speedometer", "Speedometer", true, 0)]
    [InlineData("10-25-26  01:15PM                 1234 items.xml", "items.xml", false, 1234)]
    public void ReadsBothStylesOfFtpListing(string line, string name, bool folder, long size)
    {
        var e = FtpListing.Parse(line);
        Assert.NotNull(e);
        Assert.Equal(new FtpEntry(name, folder, size), e);
    }

    [Fact]
    public void FtpListingSkipsTheTotalLine() => Assert.Null(FtpListing.Parse("total 12"));

    [Fact]
    public void AClientPackHasThePlayersModsAndInstallsFromTheManager()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Hud", "Hud");
        g.AddMod(g.Install.GameMods, "Both", "Both");
        g.AddMod(g.Install.GameMods, "ServerOnly", "ServerOnly");
        var scan = ModLibrary.Scan(g.Install);
        var mods = ClientPack.ClientMods(scan, Sides(("Hud", ModSide.Client), ("ServerOnly", ModSide.Server)));
        var zip = Path.Combine(g.Root, "pack.zip");
        Assert.Equal(2, ClientPack.Create(mods, zip, "My server"));

        using (var a = ZipFile.OpenRead(zip))
        {
            var names = a.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("Mods/Hud/ModInfo.xml", names);
            Assert.Contains("Mods/Both/ModInfo.xml", names);
            Assert.DoesNotContain(names, n => n.Contains("ServerOnly"));
            Assert.Contains("README.txt", names);
            Assert.Contains("My server.7dmm.json", names);
        }

        using var player = new FakeGame();
        var done = ModInstaller.Install(zip, ModLibrary.Scan(player.Install), player.Install.GameMods, player.Backups);
        Assert.Equal(new[] { "Both", "Hud" }, done.Select(d => d.Key).OrderBy(k => k));
    }
}
