namespace SevenDaysModManager.Core;

/// <summary>
/// Steam's library folders, read from steamapps/libraryfolders.vdf (with the Steam folder itself as the first library),
/// and where an app is installed in them (steamapps/appmanifest_&lt;appid&gt;.acf).
/// </summary>
public sealed class SteamLibraries
{
    public string? SteamFolder { get; }
    public IReadOnlyList<string> Libraries { get; }

    public SteamLibraries(string? steamFolder, IReadOnlyList<string> libraries)
    {
        SteamFolder = steamFolder;
        Libraries = libraries;
    }

    /// <summary>Libraries of the Steam client on this PC, or none when Steam is not installed.</summary>
    public static SteamLibraries Find()
    {
        var steam = SteamFolderGuess();
        return steam == null ? new SteamLibraries(null, Array.Empty<string>()) : FromSteamFolder(steam);
    }

    public static SteamLibraries FromSteamFolder(string steamFolder)
    {
        var libs = new List<string> { steamFolder };
        try
        {
            var vdf = Path.Combine(steamFolder, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) libs.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new SteamLibraries(steamFolder, libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// The library paths in libraryfolders.vdf: "libraryfolders" { "0" { "path" "D:\\SteamLibrary" ... } }. Older files
    /// give the path as the value itself ("1" "D:\\SteamLibrary").
    /// </summary>
    public static List<string> ParseLibraryFolders(string vdf)
    {
        var list = new List<string>();
        var root = KeyValues.Parse(vdf);
        var folders = root.Block("libraryfolders") ?? root.Block("LibraryFolders");
        if (folders == null) return list;
        foreach (var (key, value) in folders)
        {
            if (!int.TryParse(key, out _)) continue;
            var path = value is KeyValues b ? b.Value("path") : value as string;
            if (!string.IsNullOrWhiteSpace(path)) list.Add(path);
        }
        return list;
    }

    /// <summary>The install folder of <paramref name="appId"/> (from its appmanifest's installdir), or null when no library has it.</summary>
    public string? FindApp(string appId, string fallbackDirName)
    {
        foreach (var lib in Libraries)
        {
            var apps = Path.Combine(lib, "steamapps");
            var manifest = Path.Combine(apps, $"appmanifest_{appId}.acf");
            string dir = fallbackDirName;
            try
            {
                if (File.Exists(manifest))
                {
                    var kv = KeyValues.Parse(File.ReadAllText(manifest));
                    if ((kv.Block("AppState")?.Value("installdir")) is { Length: > 0 } d) dir = d;
                }
                else continue;
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            var full = Path.Combine(apps, "common", dir);
            if (Directory.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>The Workshop content folder for <paramref name="appId"/> in the library holding <paramref name="gameFolder"/> (or any library).</summary>
    public string? WorkshopFolder(string appId, string? gameFolder)
    {
        IEnumerable<string> order = Libraries;
        if (gameFolder != null)
        {
            // steamapps/common/<game> -> the library is three levels up.
            var lib = Directory.GetParent(gameFolder)?.Parent?.Parent?.FullName;
            if (lib != null) order = new[] { lib }.Concat(Libraries);
        }
        return order.Select(l => Path.Combine(l, "steamapps", "workshop", "content", appId)).FirstOrDefault(Directory.Exists);
    }

    static string? SteamFolderGuess()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string p && Directory.Exists(p)) return Path.GetFullPath(p);
            }
            catch { }
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var guess = Path.Combine(pf, "Steam");
            return Directory.Exists(guess) ? guess : null;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".local", "share", "Steam") }.FirstOrDefault(Directory.Exists);
    }
}
