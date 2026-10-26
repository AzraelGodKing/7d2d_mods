using System.Net;
using System.Net.Http.Headers;

namespace SevenDaysModManager.Tests;

public class DownloadTests
{
    /// <summary>Answers every request from a table of URL to (content type, body).</summary>
    sealed class FakeWeb : HttpMessageHandler
    {
        readonly Dictionary<string, (string Type, byte[] Body)> _pages;
        public FakeWeb(Dictionary<string, (string Type, byte[] Body)> pages) => _pages = pages;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!_pages.TryGetValue(request.RequestUri!.ToString(), out var page))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            var content = new ByteArrayContent(page.Body);
            content.Headers.ContentType = new MediaTypeHeaderValue(page.Type);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    static byte[] Text(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Theory]
    [InlineData("https://github.com/AzraelGodKing/7d2d_mods", "AzraelGodKing", "7d2d_mods", null)]
    [InlineData("https://github.com/AzraelGodKing/7d2d_mods/releases/latest", "AzraelGodKing", "7d2d_mods", null)]
    [InlineData("https://github.com/AzraelGodKing/7d2d_mods/releases/tag/AzraelBait-v1.7.8", "AzraelGodKing", "7d2d_mods", "AzraelBait-v1.7.8")]
    [InlineData("https://github.com/someone/Mod.git", "someone", "Mod", null)]
    public void ReadsGitHubLinks(string url, string owner, string repo, string? tag) =>
        Assert.Equal(new ModDownloader.GitHubLink(owner, repo, tag), ModDownloader.ParseGitHub(url));

    [Theory]
    [InlineData("https://github.com/AzraelGodKing/7d2d_mods/releases/download/AzraelBait-v1.7.8/AzraelBait-1.7.8.zip")]
    [InlineData("https://example.com/mod.zip")]
    [InlineData("not a link")]
    public void OtherLinksAreUsedAsTheyAre(string url) => Assert.Null(ModDownloader.ParseGitHub(url));

    const string Releases = """
        [
          { "tag_name": "AzraelBait-v1.8.0-beta", "prerelease": true, "html_url": "p0", "assets": [] },
          { "tag_name": "AzraelBait-v1.7.8", "html_url": "https://github.com/x/y/releases/tag/AzraelBait-v1.7.8",
            "assets": [ { "name": "AzraelBait-1.7.8.zip", "browser_download_url": "https://dl/AzraelBait-1.7.8.zip" } ] },
          { "tag_name": "AzraelBaitUI-v1.6.5", "html_url": "p2", "assets": [ { "name": "AzraelBaitUI-1.6.5.zip", "browser_download_url": "https://dl/ui.zip" } ] },
          { "tag_name": "AzraelBait-v1.7.7", "html_url": "p3", "assets": [] },
          { "tag_name": "v9.0", "html_url": "p4", "assets": [] }
        ]
        """;

    [Fact]
    public void FindsTheNewestReleaseTaggedForTheMod()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Bait", "AzraelBait", version: "1.7.7");
        g.AddMod(g.Install.GameMods, "BaitUI", "AzraelBaitUI", version: "1.6.5");
        var scan = ModLibrary.Scan(g.Install);
        var bait = ModUpdates.Newer(Releases, scan.Mods.First(m => m.Key == "AzraelBait"), onlyModOfRepo: false);
        Assert.NotNull(bait);
        Assert.Equal(("1.7.7", "1.7.8", "https://dl/AzraelBait-1.7.8.zip"), (bait!.Current, bait.Latest, bait.ZipUrl));
        Assert.Null(ModUpdates.Newer(Releases, scan.Mods.First(m => m.Key == "AzraelBaitUI"), onlyModOfRepo: false));
    }

    [Fact]
    public void PlainVersionTagsCountOnlyForARepositoryOfOneMod()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "Other", "Other", version: "1.0");
        var mod = ModLibrary.Scan(g.Install).Mods.First(m => m.Key == "Other");
        Assert.Null(ModUpdates.Newer(Releases, mod, onlyModOfRepo: false));
        Assert.Equal("9.0", ModUpdates.Newer(Releases, mod, onlyModOfRepo: true)?.Latest);
    }

    [Fact]
    public async Task ChecksEachRepositoryOnce()
    {
        using var g = new FakeGame();
        foreach (var (folder, name) in new[] { ("Bait", "AzraelBait"), ("BaitUI", "AzraelBaitUI"), ("NoSite", "NoSite") })
        {
            var d = g.AddMod(g.Install.GameMods, folder, name, version: "1.0");
            var site = name == "NoSite" ? "" : "https://github.com/AzraelGodKing/7d2d_mods";
            File.WriteAllText(Path.Combine(d, "ModInfo.xml"), $"""<xml><Name value="{name}" /><Version value="1.0" /><Website value="{site}" /></xml>""");
        }
        var calls = new List<string>();
        var found = await ModUpdates.CheckAsync(ModLibrary.Scan(g.Install).Mods, (u, _) => { calls.Add(u); return Task.FromResult(Releases); });
        Assert.Equal(new[] { "https://api.github.com/repos/AzraelGodKing/7d2d_mods/releases?per_page=100" }, calls);
        Assert.Equal(new[] { "AzraelBait", "AzraelBaitUI" }, found.Select(u => u.Name));
    }

    [Fact]
    public async Task AGitHubPageResolvesToItsReleaseZip()
    {
        var http = new HttpClient(new FakeWeb(new()
        {
            ["https://api.github.com/repos/o/r/releases/latest"] = ("application/json", Text("""{ "assets": [ { "name": "Mod-1.0.zip", "browser_download_url": "https://dl/Mod-1.0.zip" } ] }""")),
            ["https://api.github.com/repos/o/r/releases/tags/v2"] = ("application/json", Text("""{ "assets": [ { "name": "a.zip", "browser_download_url": "https://dl/a.zip" }, { "name": "b.zip", "browser_download_url": "https://dl/b.zip" } ] }""")),
        }));
        Assert.Equal("https://dl/Mod-1.0.zip", await ModDownloader.ResolveAsync("https://github.com/o/r", http));
        Assert.Equal("https://example.com/x.zip", await ModDownloader.ResolveAsync("https://example.com/x.zip", http));
        await Assert.ThrowsAsync<InvalidDataException>(() => ModDownloader.ResolveAsync("https://github.com/o/r/releases/tag/v2", http));
        await Assert.ThrowsAsync<InvalidDataException>(() => ModDownloader.ResolveAsync("https://github.com/o/none", http));
    }

    [Fact]
    public async Task DownloadsAZipServedWithoutItsNameAndInstallsIt()
    {
        using var g = new FakeGame();
        var zip = File.ReadAllBytes(g.Zip("src.zip", ("Wrapper/Speedometer/ModInfo.xml", FakeGame.ModInfoXml("Speedometer"))));
        var http = new HttpClient(new FakeWeb(new() { ["https://example.com/download?id=7"] = ("application/octet-stream", zip) }));
        var file = await ModDownloader.DownloadAsync("https://example.com/download?id=7", http);
        try
        {
            Assert.EndsWith(".zip", file);
            var done = ModInstaller.Install(file, ModLibrary.Scan(g.Install), g.Install.GameMods, g.Backups);
            Assert.Equal(Path.Combine(g.Install.GameMods, "Speedometer"), Assert.Single(done).Folder);
        }
        finally { ModDownloader.CleanUp(file); }
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }

    [Fact]
    public async Task AWebPageIsNotAMod()
    {
        var http = new HttpClient(new FakeWeb(new() { ["https://www.nexusmods.com/7daystodie/mods/1"] = ("text/html", Text("<html></html>")) }));
        var e = await Assert.ThrowsAsync<InvalidDataException>(() => ModDownloader.DownloadAsync("https://www.nexusmods.com/7daystodie/mods/1", http));
        Assert.Contains("web page", e.Message);
    }

    [Fact]
    public void NamesTheFileFromTheServerOrTheLink()
    {
        Assert.Equal("Mod 1.0.zip", ModDownloader.FileName(new ContentDispositionHeaderValue("attachment") { FileName = "\"Mod 1.0.zip\"" }, new Uri("https://x/dl")));
        Assert.Equal("Bait.zip", ModDownloader.FileName(null, new Uri("https://x/files/Bait.zip?token=1")));
        Assert.Equal("evil.zip", ModDownloader.FileName(new ContentDispositionHeaderValue("attachment") { FileName = "..\\..\\evil.zip" }, new Uri("https://x/")));
    }
}
