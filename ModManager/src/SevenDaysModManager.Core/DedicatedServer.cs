using System.Xml.Linq;

namespace SevenDaysModManager.Core;

/// <summary>A dedicated server install on this PC: its folder, the program that runs it, and its Mods folder.</summary>
public sealed record ServerInstall(string Folder, string Exe)
{
    public string Mods => Path.Combine(Folder, SevenDays.ModsFolderName);
    public string ConfigPath(string? configFile) => Path.Combine(Folder, string.IsNullOrWhiteSpace(configFile) ? DedicatedServer.DefaultConfig : configFile.Trim());
}

/// <summary>
/// Running a 7 Days to Die dedicated server on this PC, the way the game's own startdedicated.bat does, and keeping
/// backups of its serverconfig.xml. The server is Steam's "7 Days to Die Dedicated Server" (app 294420), or the game
/// itself, which can run as one.
/// </summary>
public static class DedicatedServer
{
    public const string AppId = "294420";
    public const string InstallDirName = "7 Days to Die Dedicated Server";
    public const string ServerExe = "7DaysToDieServer.exe";
    public const string DefaultConfig = "serverconfig.xml";

    /// <summary>
    /// The server: <paramref name="folder"/> when set, else the Dedicated Server app from Steam, else the game folder
    /// when it has a serverconfig.xml. Null when there is none.
    /// </summary>
    public static ServerInstall? Find(SteamLibraries steam, string? folder, GameInstall? game)
    {
        var dir = folder is { Length: > 0 } && Directory.Exists(folder) ? folder
            : steam.FindApp(AppId, InstallDirName)
            ?? (game != null && File.Exists(Path.Combine(game.GameFolder, DefaultConfig)) ? game.GameFolder : null);
        return dir == null ? null : At(dir);
    }

    /// <summary>The server in <paramref name="folder"/>: 7DaysToDieServer.exe when it is there, else the game's exe run as a server.</summary>
    public static ServerInstall At(string folder)
    {
        var server = Path.Combine(folder, ServerExe);
        return new ServerInstall(folder, File.Exists(server) ? server : Path.Combine(folder, SevenDays.Exe));
    }

    /// <summary>How to start it: the arguments startdedicated.bat uses, a log file in the server's folder, then the player's own.</summary>
    public static GameLaunch Launch(ServerInstall server, string? configFile, string? extraArgs, DateTime now)
    {
        var dataFolder = Path.GetFileNameWithoutExtension(server.Exe) + "_Data";
        var log = Path.Combine(server.Folder, dataFolder, $"output_log_dedi__{now:yyyy-MM-dd__HH-mm-ss}.txt");
        var args = new List<string>
        {
            "-logfile", log, "-quit", "-batchmode", "-nographics",
            "-configfile=" + (string.IsNullOrWhiteSpace(configFile) ? DefaultConfig : configFile.Trim()), "-dedicated",
        };
        args.AddRange(SevenDays.SplitArgs(extraArgs));
        return new GameLaunch(server.Exe, args, server.Folder, false);
    }

    /// <summary>The &lt;property name value&gt; settings of a serverconfig.xml, in file order. Throws <see cref="FormatException"/> when it isn't valid XML.</summary>
    public static List<(string Name, string Value)> ReadConfig(string path)
    {
        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch (System.Xml.XmlException e) { throw new FormatException($"{Path.GetFileName(path)} is not valid XML: {e.Message}", e); }
        return doc.Descendants("property")
            .Where(p => (string?)p.Attribute("name") is { Length: > 0 })
            .Select(p => ((string)p.Attribute("name")!, (string?)p.Attribute("value") ?? "")).ToList();
    }

    /// <summary>Copies a serverconfig.xml into <paramref name="backupFolder"/> as serverconfig-yyyyMMdd-HHmmss.xml, keeping the newest <paramref name="keep"/>. Returns the copy.</summary>
    public static string BackupConfig(string path, string backupFolder, int keep = 20)
    {
        Directory.CreateDirectory(backupFolder);
        var stem = Path.GetFileNameWithoutExtension(path);
        var dest = Path.Combine(backupFolder, $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}.xml");
        for (var i = 2; File.Exists(dest); i++) dest = Path.Combine(backupFolder, $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}-{i}.xml");
        File.Copy(path, dest);
        foreach (var old in ConfigBackups(backupFolder, stem).Skip(keep))
        {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return dest;
    }

    /// <summary>The backups of a config (by its file name without .xml), newest first.</summary>
    public static List<string> ConfigBackups(string backupFolder, string stem = "serverconfig") =>
        Directory.Exists(backupFolder)
            ? new DirectoryInfo(backupFolder).GetFiles(stem + "-*.xml").OrderByDescending(f => f.Name, StringComparer.Ordinal).Select(f => f.FullName).ToList()
            : new List<string>();

    /// <summary>Puts a backup back over <paramref name="path"/>, after backing up the file it replaces. Returns that backup, or null when there was no file.</summary>
    public static string? RestoreConfig(string backup, string path, string backupFolder)
    {
        // A file that isn't a config can't go back.
        ReadConfig(backup);
        string? before = File.Exists(path) ? BackupConfig(path, backupFolder) : null;
        File.Copy(backup, path, true);
        return before;
    }
}
