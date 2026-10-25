namespace SevenDaysModManager.Core;

/// <summary>One saved game: Saves\&lt;World&gt;\&lt;Save&gt;.</summary>
public sealed record SaveGame(string World, string Name, string Folder, DateTime LastPlayed, long Size);

public static class Saves
{
    /// <summary>Every save, the most recently played first.</summary>
    public static List<SaveGame> List(GameInstall install)
    {
        var list = new List<SaveGame>();
        if (!Directory.Exists(install.Saves)) return list;
        try
        {
            foreach (var world in Directory.GetDirectories(install.Saves))
                foreach (var save in Directory.GetDirectories(world))
                {
                    var files = new DirectoryInfo(save).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
                    if (files.Count == 0) continue;
                    list.Add(new SaveGame(Path.GetFileName(world), Path.GetFileName(save), save, files.Max(f => f.LastWriteTime), files.Sum(f => f.Length)));
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return list.OrderByDescending(s => s.LastPlayed).ToList();
    }

    /// <summary>Zips a save into <paramref name="backupFolder"/>, keeping the newest <paramref name="keep"/> backups of it. Returns the zip.</summary>
    public static string Backup(SaveGame save, string backupFolder, int keep = 10)
    {
        var name = save.World + "-" + save.Name;
        var zip = Backups.ZipFolder(save.Folder, backupFolder, name);
        Backups.Prune(backupFolder, name, keep);
        return zip;
    }
}
