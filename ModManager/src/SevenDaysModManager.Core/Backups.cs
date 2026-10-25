using System.IO.Compression;

namespace SevenDaysModManager.Core;

/// <summary>Zips folders before they are replaced or removed, so nothing is lost by a click.</summary>
public static class Backups
{
    /// <summary>Zips <paramref name="folder"/> into <paramref name="backupFolder"/> as name-yyyyMMdd-HHmmss.zip and returns the zip's path.</summary>
    public static string ZipFolder(string folder, string backupFolder, string name)
    {
        Directory.CreateDirectory(backupFolder);
        var zip = Path.Combine(backupFolder, $"{Safe(name)}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        for (var i = 2; File.Exists(zip); i++) zip = Path.Combine(backupFolder, $"{Safe(name)}-{DateTime.Now:yyyyMMdd-HHmmss}-{i}.zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        return zip;
    }

    /// <summary>Deletes all but the newest <paramref name="keep"/> zips that start with <paramref name="name"/>.</summary>
    public static void Prune(string backupFolder, string name, int keep)
    {
        if (!Directory.Exists(backupFolder)) return;
        foreach (var old in new DirectoryInfo(backupFolder).GetFiles(Safe(name) + "-*.zip").OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
        {
            try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>A name made safe for a file name.</summary>
    public static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToHashSet();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return s.Length == 0 ? "mod" : s;
    }

    /// <summary>Copies a folder and everything in it.</summary>
    public static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
    }

    /// <summary>Moves a folder, copying when it goes to another drive.</summary>
    public static void MoveFolder(string from, string to)
    {
        try { Directory.Move(from, to); }
        catch (IOException) when (!string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase))
        {
            CopyFolder(from, to);
            Directory.Delete(from, true);
        }
    }
}
