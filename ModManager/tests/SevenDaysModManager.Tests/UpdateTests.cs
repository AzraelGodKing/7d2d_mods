namespace SevenDaysModManager.Tests;

public class UpdateCheckTests
{
    static string Release(string tag, bool draft = false, bool pre = false, string body = "notes") => $$"""
        { "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{pre.ToString().ToLowerInvariant()}},
          "html_url": "https://github.com/x/{{tag}}", "body": "{{body}}",
          "assets": [
            { "name": "SevenDaysModManager-{{tag[12..]}}-win-x64.exe", "browser_download_url": "https://dl/{{tag}}/app.exe" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://dl/{{tag}}/SHA256SUMS.txt" } ] }
        """;

    [Fact]
    public void PicksTheNewestModManagerReleaseAndIgnoresTheMods()
    {
        var json = "[" + string.Join(",",
            """{ "tag_name": "AzraelSpeedometer-v9.9.9", "assets": [] }""",
            Release("ModManager-v0.2.0"),
            Release("ModManager-v0.10.0"),
            Release("ModManager-v0.11.0", draft: true),
            Release("ModManager-v0.12.0", pre: true)) + "]";
        var r = UpdateCheck.Newest(json);
        Assert.NotNull(r);
        Assert.Equal("0.10.0", r!.Version);
        Assert.Equal("https://dl/ModManager-v0.10.0/app.exe", r.ExeUrl);
        Assert.Equal("https://dl/ModManager-v0.10.0/SHA256SUMS.txt", r.ChecksumsUrl);
        Assert.Equal("notes", r.Notes);
    }

    [Fact]
    public void NoModManagerReleaseMeansNoUpdate() =>
        Assert.Null(UpdateCheck.Newest("""[{ "tag_name": "HonkDoorFix-v1.0.0", "assets": [] }]"""));

    [Theory]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    [InlineData("junk", "0.1.0", false)]
    public void ComparesVersions(string latest, string current, bool newer) => Assert.Equal(newer, UpdateCheck.IsNewer(latest, current));
}

public class SelfUpdateTests
{
    [Fact]
    public void ReadsChecksumListings()
    {
        var hash = new string('a', 64);
        var d = SelfUpdate.ParseChecksums($"{hash}  SevenDaysModManager-1.0.0-win-x64.exe\n{hash} *other.zip\nnot a line\n");
        Assert.Equal(hash, d["SevenDaysModManager-1.0.0-win-x64.exe"]);
        Assert.Equal(hash, d["other.zip"]);
        Assert.Equal(2, d.Count);
    }

    [Fact]
    public void ApplyKeepsTheOldExeAndCleanUpRemovesIt()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(g.Root);
        var target = Path.Combine(g.Root, "SevenDaysModManager.exe");
        var fresh = Path.Combine(g.Root, "new.exe");
        File.WriteAllText(target, "old");
        File.WriteAllText(fresh, "new");

        SelfUpdate.Apply(fresh, target);

        Assert.Equal("new", File.ReadAllText(target));
        Assert.Equal("old", File.ReadAllText(target + SelfUpdate.OldSuffix));
        Assert.Equal(1, SelfUpdate.CleanUp(target, attempts: 1));
        Assert.False(File.Exists(target + SelfUpdate.OldSuffix));
    }

    [Fact]
    public void ApplyPutsTheOldExeBackWhenTheCopyFails()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(g.Root);
        var target = Path.Combine(g.Root, "SevenDaysModManager.exe");
        File.WriteAllText(target, "old");
        Assert.ThrowsAny<IOException>(() => SelfUpdate.Apply(Path.Combine(g.Root, "missing.exe"), target));
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void CleanStagingKeepsOnlyNewerDownloads()
    {
        using var g = new FakeGame();
        foreach (var v in new[] { "0.1.0", "0.2.0", "0.3.0" }) Directory.CreateDirectory(SelfUpdate.StagingFor(v, g.Root));
        SelfUpdate.CleanStaging("0.2.0", g.Root);
        Assert.False(Directory.Exists(SelfUpdate.StagingFor("0.1.0", g.Root)));
        Assert.False(Directory.Exists(SelfUpdate.StagingFor("0.2.0", g.Root)));
        Assert.True(Directory.Exists(SelfUpdate.StagingFor("0.3.0", g.Root)));
    }

    [Fact]
    public void SaysWhyItCantUpdate()
    {
        var r = new ReleaseInfo("1.0.0", "page", null, "sums");
        Assert.Contains("isn't the single-file exe", SelfUpdate.CannotUpdateReason(r, null));
        Assert.Contains("no exe", SelfUpdate.CannotUpdateReason(r, "C:/x/SevenDaysModManager.exe"));
    }
}
