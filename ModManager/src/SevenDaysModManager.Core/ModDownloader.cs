using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SevenDaysModManager.Core;

/// <summary>
/// Downloads a mod's zip from a link so <see cref="ModInstaller"/> can install it: a direct link to a .zip, or a GitHub
/// repository or release page, whose release zip is found through GitHub's public API.
/// </summary>
public static partial class ModDownloader
{
    /// <summary>The biggest download accepted: no 7 Days to Die mod is anywhere near it.</summary>
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>For downloads: names this program, and allows the time a big mod takes.</summary>
    public static readonly HttpClient Http = CreateDownloadClient();

    /// <summary>For GitHub's API (releases lists).</summary>
    public static readonly HttpClient Api = UpdateCheck.CreateClient(TimeSpan.FromSeconds(30));

    static HttpClient CreateDownloadClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SevenDaysModManager", UpdateCheck.CurrentVersion));
        return c;
    }

    /// <summary>Resolves, downloads and installs a mod from a link; the download is removed after. Returns what was installed.</summary>
    public static async Task<List<InstalledResult>> InstallAsync(string url, LibraryScan scan, string modsFolder, string backupFolder,
        IProgress<(long Done, long? Total)>? progress = null, CancellationToken ct = default)
    {
        var file = await DownloadAsync(await ResolveAsync(url, Api, ct), Http, progress, ct);
        try { return await Task.Run(() => ModInstaller.Install(file, scan, modsFolder, backupFolder), ct); }
        finally { CleanUp(file); }
    }

    /// <summary>A GitHub link: a repository, its latest release, or one release by tag.</summary>
    public sealed record GitHubLink(string Owner, string Repo, string? Tag);

    [GeneratedRegex(@"^/([\w.\-]+)/([\w.\-]+?)(?:\.git)?(?:/releases(?:/tag/([^/?#]+)|/latest)?)?/?$")]
    private static partial Regex GitHubPathRx();

    /// <summary>Whether <paramref name="text"/> is an http(s) link (what can be dropped or pasted).</summary>
    public static bool IsLink(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    /// <summary>The repository and tag of a github.com page link; null for any other link (a direct download is used as it is).</summary>
    public static GitHubLink? ParseGitHub(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || !u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
        var m = GitHubPathRx().Match(u.AbsolutePath);
        if (!m.Success) return null;
        return new GitHubLink(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Success ? Uri.UnescapeDataString(m.Groups[3].Value) : null);
    }

    /// <summary>The .zip to download for a release in GitHub's API JSON: the one naming <paramref name="prefer"/>, else the only one.</summary>
    public static string? ZipAsset(JsonElement release, string? prefer = null)
    {
        if (!release.TryGetProperty("assets", out var a) || a.ValueKind != JsonValueKind.Array) return null;
        var zips = a.EnumerateArray()
            .Select(x => (Name: x.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", Url: x.TryGetProperty("browser_download_url", out var b) ? b.GetString() : null))
            .Where(x => x.Url != null && x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (prefer != null && zips.FirstOrDefault(z => z.Name.Contains(prefer, StringComparison.OrdinalIgnoreCase)) is { Url: not null } p) return p.Url;
        return zips.Count == 1 ? zips[0].Url : null;
    }

    /// <summary>
    /// The direct download for <paramref name="url"/>: itself, or for a GitHub repository or release page, its release's
    /// zip. Throws <see cref="InvalidDataException"/> when a GitHub release has no single zip to pick.
    /// </summary>
    public static async Task<string> ResolveAsync(string url, HttpClient http, CancellationToken ct = default)
    {
        if (ParseGitHub(url) is not { } gh) return url.Trim();
        var api = $"https://api.github.com/repos/{gh.Owner}/{gh.Repo}/releases/" + (gh.Tag == null ? "latest" : "tags/" + Uri.EscapeDataString(gh.Tag));
        string json;
        try { json = await http.GetStringAsync(api, ct); }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidDataException($"{gh.Owner}/{gh.Repo} has no {(gh.Tag == null ? "published release" : "release " + gh.Tag)} on GitHub.");
        }
        using var doc = JsonDocument.Parse(json);
        return ZipAsset(doc.RootElement)
            ?? throw new InvalidDataException($"The GitHub release {(gh.Tag ?? "latest")} of {gh.Repo} has no single .zip to install. Open the release page and drop the zip you want.");
    }

    /// <summary>
    /// Downloads <paramref name="url"/> into a new temp folder and returns the file, named as the server names it.
    /// <paramref name="progress"/> gets the bytes so far and the total when known. Throws
    /// <see cref="InvalidDataException"/> when the link is a web page rather than a file.
    /// </summary>
    public static async Task<string> DownloadAsync(string url, HttpClient http, IProgress<(long Done, long? Total)>? progress = null, CancellationToken ct = default)
    {
        if (!IsLink(url)) throw new InvalidDataException($"{url} is not an http or https link.");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        if (total > MaxBytes) throw new InvalidDataException($"The download is {Display.Size(total.Value)}; that is too big for a mod.");
        var type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("That link opens a web page, not a file. Use the link of the download itself (right-click the download button > Copy link).");

        var name = FileName(resp.Content.Headers.ContentDisposition, resp.RequestMessage?.RequestUri ?? new Uri(url));
        var folder = Path.Combine(Path.GetTempPath(), "7dmm-download-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(path))
        {
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                done += n;
                if (done > MaxBytes) throw new InvalidDataException("The download is too big for a mod.");
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                progress?.Report((done, total));
            }
        }
        // A zip served without its extension (a "download?id=..." link): it starts with "PK".
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) && StartsWithPk(path))
        {
            var zip = path + ".zip";
            File.Move(path, zip);
            path = zip;
        }
        return path;
    }

    /// <summary>The file name for a download: the server's Content-Disposition name, else the link's last part, made safe.</summary>
    public static string FileName(ContentDispositionHeaderValue? disposition, Uri uri)
    {
        var name = (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? "");
        name = Backups.Safe(Path.GetFileName(name.Replace('\\', '/')));
        return name == "mod" ? "download" : name;
    }

    static bool StartsWithPk(string path)
    {
        using var f = File.OpenRead(path);
        return f.ReadByte() == 'P' && f.ReadByte() == 'K';
    }

    /// <summary>Removes a download's temp folder; never throws.</summary>
    public static void CleanUp(string downloaded)
    {
        try
        {
            var dir = Path.GetDirectoryName(downloaded);
            if (dir != null && Path.GetFileName(dir).StartsWith("7dmm-download-", StringComparison.Ordinal)) Directory.Delete(dir, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A newer release of an installed mod on GitHub.</summary>
public sealed record ModUpdate(string ModId, string Name, string Current, string Latest, string PageUrl, string? ZipUrl);

/// <summary>
/// Finds newer versions of installed mods whose Website is a GitHub repository, from its public releases. A repository
/// of several mods (like this one) tags each release "&lt;ModName&gt;-v&lt;version&gt;"; one of a single mod may tag
/// "v&lt;version&gt;". Only GitHub's public release lists are read.
/// </summary>
public static class ModUpdates
{
    /// <summary>The GitHub repository a Website names ("https://github.com/AzraelGodKing/7d2d_mods"), or null.</summary>
    public static (string Owner, string Repo)? RepoOf(string? website)
    {
        if (string.IsNullOrWhiteSpace(website)) return null;
        var w = website.Trim();
        if (!w.Contains("://")) w = "https://" + w;
        return ModDownloader.ParseGitHub(w) is { } gh ? (gh.Owner, gh.Repo) : null;
    }

    /// <summary>Checks every mod that names a GitHub repository; <paramref name="getJson"/> fetches a URL (one call per repository).</summary>
    public static async Task<List<ModUpdate>> CheckAsync(IEnumerable<InstalledMod> mods, Func<string, CancellationToken, Task<string>> getJson, CancellationToken ct = default)
    {
        var list = mods.Where(m => !m.Locked && m.Info != null && RepoOf(m.Info.Website) != null).ToList();
        var updates = new List<ModUpdate>();
        foreach (var repo in list.GroupBy(m => RepoOf(m.Info!.Website)!.Value, m => m))
        {
            string json;
            try { json = await getJson($"https://api.github.com/repos/{repo.Key.Owner}/{repo.Key.Repo}/releases?per_page=100", ct); }
            catch (HttpRequestException) { continue; }
            var single = repo.Select(m => m.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
            foreach (var m in repo)
                if (Newer(json, m, single) is { } u) updates.Add(u);
        }
        return updates.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The newest release for <paramref name="mod"/> in GitHub's releases JSON when it is newer than the installed one.
    /// <paramref name="onlyModOfRepo"/> lets plain "v1.2" tags count (no other installed mod points at the repository).
    /// </summary>
    public static ModUpdate? Newer(string json, InstalledMod mod, bool onlyModOfRepo)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || mod.Info == null) return null;
        var prefixes = new[] { mod.Info.Name, mod.FolderName }.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => p + "-v").ToList();
        (string Version, JsonElement Release)? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (Flag(r, "draft") || Flag(r, "prerelease") || !r.TryGetProperty("tag_name", out var t) || t.GetString() is not { } tag) continue;
            var version = prefixes.FirstOrDefault(p => tag.StartsWith(p, StringComparison.OrdinalIgnoreCase)) is { } pre ? tag[pre.Length..]
                : onlyModOfRepo && !tag.Contains('-') ? tag.TrimStart('v', 'V') : null;
            if (version == null || !UpdateCheck.IsNewer(version, "0")) continue;
            if (best == null || UpdateCheck.IsNewer(version, best.Value.Version)) best = (version, r.Clone());
        }
        if (best is not { } b || !UpdateCheck.IsNewer(b.Version, mod.Info.Version)) return null;
        var page = b.Release.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return new ModUpdate(mod.Id, mod.DisplayName, mod.Info.Version, b.Version, page, ModDownloader.ZipAsset(b.Release, mod.Info.Name));
    }

    static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
