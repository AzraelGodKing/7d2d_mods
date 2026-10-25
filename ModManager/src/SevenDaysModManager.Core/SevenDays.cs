namespace SevenDaysModManager.Core;

/// <summary>Where 7 Days to Die is installed and where it keeps the player's own files.</summary>
/// <param name="GameFolder">The game's install folder (holds 7DaysToDie.exe and the built-in Mods folder).</param>
/// <param name="UserDataFolder">The player's data folder, normally %AppData%\7DaysToDie (saves, logs, and a second Mods folder).</param>
public sealed record GameInstall(string GameFolder, string UserDataFolder)
{
    public string GameMods => Path.Combine(GameFolder, SevenDays.ModsFolderName);
    public string UserMods => Path.Combine(UserDataFolder, SevenDays.ModsFolderName);
    public string Saves => Path.Combine(UserDataFolder, "Saves");
}

/// <summary>How to start the game: a program (or a steam:// link opened by Windows) and its arguments.</summary>
public sealed record GameLaunch(string Exe, IReadOnlyList<string> Args, string? WorkingDirectory, bool ThroughShell);

/// <summary>When Play starts the game with EasyAntiCheat.</summary>
public enum EacMode
{
    /// <summary>Off while any enabled mod has code (a DLL), on otherwise.</summary>
    Auto,
    Off,
    On,
}

/// <summary>What the manager knows about 7 Days to Die itself.</summary>
public static class SevenDays
{
    public const string AppId = "251570";
    public const string Name = "7 Days to Die";
    public const string ModsFolderName = "Mods";
    /// <summary>Disabled mods are moved here, next to the Mods folder they came from; the game never reads it.</summary>
    public const string DisabledFolderName = "Mods-disabled";
    const string InstallDirName = "7 Days To Die";

    /// <summary>The game's process names (no ".exe"), to tell whether it is running before its files are changed.</summary>
    public static readonly IReadOnlyList<string> ProcessNames = new[] { "7DaysToDie", "7DaysToDie_EAC" };

    /// <summary>The game without EasyAntiCheat: starting it directly skips the launcher and EAC.</summary>
    public const string Exe = "7DaysToDie.exe";

    /// <summary>The player's data folder on this PC: %AppData%\7DaysToDie.</summary>
    public static string DefaultUserDataFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7DaysToDie");

    /// <summary>True when <paramref name="folder"/> looks like a game install (has the game's exe or its data folder).</summary>
    public static bool LooksLikeGame(string folder) =>
        File.Exists(Path.Combine(folder, Exe)) || Directory.Exists(Path.Combine(folder, "7DaysToDie_Data"));

    /// <summary>
    /// Finds the install: <paramref name="gameFolder"/> when given and real, else through Steam's libraries. Null when
    /// the game is not found.
    /// </summary>
    public static GameInstall? Detect(SteamLibraries steam, string? gameFolder, string? userDataFolder)
    {
        var game = gameFolder is { Length: > 0 } && Directory.Exists(gameFolder) ? gameFolder : steam.FindApp(AppId, InstallDirName);
        if (game == null) return null;
        return new GameInstall(game, userDataFolder is { Length: > 0 } ? userDataFolder : DefaultUserDataFolder());
    }

    /// <summary>
    /// How to start the game. With EAC it goes through Steam (the game's own launcher, which starts EAC); without, the
    /// game's exe is started directly, which is how the launcher itself starts it with EAC unticked.
    /// <paramref name="steamFolder"/> is used to pass launch options through Steam; without it the steam:// link is used.
    /// </summary>
    public static GameLaunch Launch(GameInstall install, bool eac, string? launchOptions, string? steamFolder)
    {
        var args = SplitArgs(launchOptions);
        if (!eac)
            return new GameLaunch(Path.Combine(install.GameFolder, Exe), args, install.GameFolder, false);
        var steamExe = steamFolder == null ? null : Path.Combine(steamFolder, "steam.exe");
        if (steamExe != null && File.Exists(steamExe))
            return new GameLaunch(steamExe, new[] { "-applaunch", AppId }.Concat(args).ToList(), null, false);
        return new GameLaunch("steam://rungameid/" + AppId, Array.Empty<string>(), null, true);
    }

    /// <summary>Whether Play uses EAC: the setting, or for Auto, only when no enabled mod has code.</summary>
    public static bool UsesEac(EacMode mode, bool anyCodeModEnabled) => mode switch
    {
        EacMode.On => true,
        EacMode.Off => false,
        _ => !anyCodeModEnabled,
    };

    /// <summary>Splits launch options the way a command line does: spaces separate, double quotes group.</summary>
    public static List<string> SplitArgs(string? s)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        var cur = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (var c in s)
        {
            if (c == '"') { quoted = !quoted; any = true; }
            else if (char.IsWhiteSpace(c) && !quoted) { if (any) list.Add(cur.ToString()); cur.Clear(); any = false; }
            else { cur.Append(c); any = true; }
        }
        if (any) list.Add(cur.ToString());
        return list;
    }

    /// <summary>The newest log the game wrote: %AppData%\7DaysToDie\logs (1.0 and later), else the old place in the game's data folder.</summary>
    public static string? LatestLog(GameInstall install)
    {
        var candidates = new List<FileInfo>();
        void Add(string dir, string pattern)
        {
            try { if (Directory.Exists(dir)) candidates.AddRange(new DirectoryInfo(dir).GetFiles(pattern)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Add(Path.Combine(install.UserDataFolder, "logs"), "output_log*.txt");
        Add(Path.Combine(install.GameFolder, "7DaysToDie_Data"), "output_log*.txt");
        return candidates.OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }
}
