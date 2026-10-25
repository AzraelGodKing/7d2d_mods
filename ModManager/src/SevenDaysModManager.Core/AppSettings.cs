using System.Text.Json;

namespace SevenDaysModManager.Core;

/// <summary>The app's settings, in %LocalAppData%\SevenDaysModManager\settings.json.</summary>
public sealed class AppSettings
{
    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SevenDaysModManager");
    static string FilePath => Path.Combine(Folder, "settings.json");

    public static string BackupFolder => Path.Combine(Folder, "mod-backups");
    public static string SaveBackupFolder => Path.Combine(Folder, "save-backups");
    public static string ModlistFolder => Path.Combine(Folder, "modlists");

    /// <summary>The game's install folder when picked by hand, else null (found through Steam).</summary>
    public string? GameFolder { get; set; }
    /// <summary>The player's data folder when not %AppData%\7DaysToDie.</summary>
    public string? UserDataFolder { get; set; }
    public string Theme { get; set; } = Display.Dark;
    public float TextScale { get; set; } = 1f;
    public string LaunchOptions { get; set; } = "";
    public EacMode Eac { get; set; } = EacMode.Auto;
    public bool BackupSaveBeforePlay { get; set; }
    /// <summary>Where new mods go: the game's Mods folder (false) or %AppData%\7DaysToDie\Mods (true).</summary>
    public bool InstallToUserData { get; set; }
    /// <summary>The modlist last picked.</summary>
    public string? Modlist { get; set; }
    /// <summary>The player's notes, by mod Name.</summary>
    public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The colour tag on a mod's row, by mod Name.</summary>
    public Dictionary<string, string> Colours { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
