using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SevenDaysModManager.Core;

/// <summary>A published release of the mod manager that is newer than the running one.</summary>
/// <param name="ExeUrl">The single-file exe ("SevenDaysModManager-1.2.0-win-x64.exe").</param>
/// <param name="ChecksumsUrl">SHA256SUMS.txt, which every update is checked against.</param>
/// <param name="Notes">The release's description (its change notes), or "".</param>
public sealed record ReleaseInfo(string Version, string PageUrl, string? ExeUrl, string? ChecksumsUrl, string Notes = "");

/// <summary>
/// Looks up the newest release of the mod manager on GitHub. It shares the 7d2d_mods repository with the mods, whose
/// releases are tagged "&lt;ModName&gt;-v&lt;version&gt;", so only releases tagged "ModManager-v&lt;version&gt;" count. Only
/// the public releases list is read: nothing about the user or their mods is sent. Installing is
/// <see cref="SelfUpdate"/>'s job.
/// </summary>
public static class UpdateCheck
{
    public const string Repo = "AzraelGodKing/7d2d_mods";
    public const string TagPrefix = "ModManager-v";
    public const string ReleasesUrl = "https://api.github.com/repos/" + Repo + "/releases?per_page=100";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";

    static readonly HttpClient Http = CreateClient(TimeSpan.FromSeconds(10));

    /// <summary>A client that names this program to GitHub (its API refuses requests without a User-Agent).</summary>
    public static HttpClient CreateClient(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SevenDaysModManager", CurrentVersion));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>The running program's version, e.g. "0.1.0".</summary>
    public static string CurrentVersion =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? "0.0.0").Split('+')[0];

    /// <summary>The newest release when it is newer than <paramref name="current"/>; null when up to date or GitHub can't be read.</summary>
    public static async Task<ReleaseInfo?> FindNewerAsync(string current, CancellationToken ct = default)
    {
        try
        {
            var latest = Newest(await Http.GetStringAsync(ReleasesUrl, ct));
            return latest != null && IsNewer(latest.Version, current) ? latest : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { return null; }
    }

    /// <summary>
    /// The newest mod manager release in GitHub's list of releases: tagged "ModManager-v&lt;version&gt;", not a draft or
    /// pre-release. Null when there is none.
    /// </summary>
    public static ReleaseInfo? Newest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        ReleaseInfo? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
            if (Read(r) is { } info && (best == null || IsNewer(info.Version, best.Version))) best = info;
        return best;
    }

    static ReleaseInfo? Read(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object || Flag(r, "draft") || Flag(r, "prerelease")) return null;
        var tag = Str(r, "tag_name");
        if (tag == null || !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var version = tag[TagPrefix.Length..];
        if (Numbers(version) == null) return null;
        var assets = r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => (Name: Str(x, "name") ?? "", Url: Str(x, "browser_download_url"))).Where(x => x.Url != null).ToList()
            : new();
        var exe = assets.FirstOrDefault(x => x.Name.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase)).Url;
        var sums = assets.FirstOrDefault(x => x.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)).Url;
        return new ReleaseInfo(version, Str(r, "html_url") ?? ReleasesPage, exe, sums, Str(r, "body") ?? "");
    }

    /// <summary>True when <paramref name="latest"/> is a higher version than <paramref name="current"/> (1.10.0 &gt; 1.9.0).</summary>
    public static bool IsNewer(string latest, string current)
    {
        var a = Numbers(latest);
        var b = Numbers(current);
        if (a == null || b == null) return false;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>The numeric part of a version ("1.2.0-beta" = 1, 2, 0), or null when it doesn't start with one.</summary>
    static int[]? Numbers(string v)
    {
        var core = v.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.');
        if (parts.Length == 0 || parts.Length > 4) return null;
        var n = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out n[i])) return null;
        return n;
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
