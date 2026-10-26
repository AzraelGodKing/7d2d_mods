using System.Text.Json;

namespace SevenDaysModManager.Core;

/// <summary>The app's settings, in %LocalAppData%\SevenDaysModManager\settings.json.</summary>
public sealed class AppSettings
{
    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SevenDaysModManager");
    static string FilePath => Path.Combine(Folder, "settings.json");

    public static string BackupFolder => Path.Combine(Folder, "mod-backups");
    public static string SaveBackupFolder => Path.Combine(Folder, "save-backups");
    public static string ServerConfigBackupFolder => Path.Combine(Folder, "serverconfig-backups");
    public static string ModlistFolder => Path.Combine(Folder, "modlists");
    /// <summary>The player's own copies of mod settings files, put back when a mod is updated.</summary>
    public static string ModSettingsFolder => Path.Combine(Folder, "mod-settings");

    /// <summary>The game's install folder when picked by hand, else null (found through Steam).</summary>
    public string? GameFolder { get; set; }
    /// <summary>The player's data folder when not %AppData%\7DaysToDie.</summary>
    public string? UserDataFolder { get; set; }
    public string Theme { get; set; } = Display.Dark;
    public float TextScale { get; set; } = 1f;
    public string LaunchOptions { get; set; } = "";
    public EacMode Eac { get; set; } = EacMode.Auto;
    public bool BackupSaveBeforePlay { get; set; }
    /// <summary>Back up the newest save before mods are enabled, disabled, switched or updated.</summary>
    public bool BackupSaveBeforeModChange { get; set; }
    /// <summary>The dedicated server's folder when picked by hand, else null (the Dedicated Server app from Steam, or the game).</summary>
    public string? ServerFolder { get; set; }
    /// <summary>The server's config file, in its folder.</summary>
    public string ServerConfigFile { get; set; } = DedicatedServer.DefaultConfig;
    /// <summary>Arguments added after the ones the server always gets.</summary>
    public string ServerLaunchOptions { get; set; } = "";
    /// <summary>Back up the server's config file each time the server is started from here.</summary>
    public bool BackupConfigBeforeServer { get; set; } = true;
    /// <summary>Where new mods go: the game's Mods folder (false) or %AppData%\7DaysToDie\Mods (true).</summary>
    public bool InstallToUserData { get; set; }
    /// <summary>The modlist last picked.</summary>
    public string? Modlist { get; set; }
    /// <summary>Sides the player set by hand, by mod Name (overrides what the mod says or its files suggest).</summary>
    public Dictionary<string, ModSide> Sides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The player's notes, by mod Name.</summary>
    public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The colour tag on a mod's row, by mod Name.</summary>
    public Dictionary<string, string> Colours { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Look for a newer version of the app at start-up.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>A version the player said not to be reminded about again.</summary>
    public string? SkippedVersion { get; set; }
    public int[]? WindowBounds { get; set; }
    public bool WindowMaximized { get; set; }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) is { } s)
            {
                s.Notes = new Dictionary<string, string>(s.Notes, StringComparer.OrdinalIgnoreCase);
                s.Colours = new Dictionary<string, string>(s.Colours, StringComparer.OrdinalIgnoreCase);
                s.Sides = new Dictionary<string, ModSide>(s.Sides, StringComparer.OrdinalIgnoreCase);
                return s;
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
        File.Move(tmp, FilePath, true);
    }
}
