using System.IO.Compression;

namespace SevenDaysModManager.Core;

/// <summary>One mod an install put in place.</summary>
/// <param name="Replaced">The folder of the older copy it replaced (zipped into the backups first), or null for a new mod.</param>
public sealed record InstalledResult(string Name, string Folder, string? Replaced);

/// <summary>
/// Installs mods from a .zip or a folder: every folder inside that holds a ModInfo.xml is a mod, however deep the zip
/// put it, so a zip with a folder in a folder still lands as Mods\TheMod. A mod already installed (the same ModInfo
/// Name) is replaced in place, enabled or not, after its old copy is zipped into the backups.
/// </summary>
public static class ModInstaller
{
    public static readonly string[] ArchiveTypes = { ".zip" };
    static readonly string[] Unsupported = { ".rar", ".7z", ".tar", ".gz" };

    /// <summary>Why <paramref name="path"/> can't be installed, or null when it can.</summary>
    public static string? CantInstall(string path)
    {
        if (Directory.Exists(path)) return null;
        if (!File.Exists(path)) return $"{path} doesn't exist.";
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ArchiveTypes.Contains(ext)) return null;
        if (Unsupported.Contains(ext)) return $"{Path.GetFileName(path)} is a {ext} archive. Unpack it first, then install the folder (drop it on the window).";
        return $"{Path.GetFileName(path)} is not a .zip or a folder.";
    }

    /// <summary>
    /// Installs every mod in <paramref name="source"/> (a .zip or a folder) into <paramref name="modsFolder"/>, or over
    /// the copy already in <paramref name="scan"/>. Throws <see cref="InvalidDataException"/> when there is no mod in it.
    /// </summary>
    public static List<InstalledResult> Install(string source, LibraryScan scan, string modsFolder, string backupFolder)
    {
        if (CantInstall(source) is { } why) throw new InvalidDataException(why);
        var temp = Path.Combine(Path.GetTempPath(), "7dmm-install-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string root;
            if (Directory.Exists(source)) root = source;
            else
            {
                ExtractSafely(source, temp);
                root = temp;
            }
            var mods = FindMods(root);
            if (mods.Count == 0) throw new InvalidDataException($"There is no mod in {Path.GetFileName(source)}: no folder in it has a ModInfo.xml.");
            var results = new List<InstalledResult>();
            foreach (var folder in mods)
            {
                var info = ModInfo.Load(ModInfo.FindIn(folder)!);
                // A zip with ModInfo.xml at its top has no folder name of its own: use the mod's Name.
                var name = Backups.Safe(ModLibrary.PathsEqual(folder, root) ? (Directory.Exists(source) ? Path.GetFileName(source.TrimEnd('\\', '/')) : info.Name) : Path.GetFileName(folder));
                var existing = scan.Mods.FirstOrDefault(m => !m.Locked && m.Info != null && string.Equals(m.Info.Name, info.Name, StringComparison.OrdinalIgnoreCase));
                string dest;
                string? replaced = null;
                if (existing != null && (ModLibrary.PathsEqual(existing.Folder, folder) || Path.GetFullPath(folder).StartsWith(Path.GetFullPath(existing.Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"{info.DisplayName} is already installed there ({existing.Folder}).");
                if (existing != null)
                {
                    Backups.ZipFolder(existing.Folder, backupFolder, existing.FolderName);
                    Directory.Delete(existing.Folder, true);
                    dest = Path.Combine(Path.GetDirectoryName(existing.Folder)!, name);
                    replaced = existing.Folder;
                }
                else dest = Path.Combine(modsFolder, name);
                if (Directory.Exists(dest)) throw new IOException($"Can't install {info.DisplayName}: {dest} already exists and is a different mod.");
                Backups.CopyFolder(folder, dest);
                results.Add(new InstalledResult(info.DisplayName, dest, replaced));
            }
            return results;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The folders under <paramref name="root"/> (itself included) that hold a ModInfo.xml, without those inside another mod.</summary>
    public static List<string> FindMods(string root)
    {
        var all = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => Path.GetFileName(f).Equals(ModInfo.FileName, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetDirectoryName(f)!)
            .Where(d => !d.Split(Path.DirectorySeparatorChar).Any(p => p == "__MACOSX"))
            .OrderBy(d => d.Length).ToList();
        var mods = new List<string>();
        foreach (var d in all)
            if (!mods.Any(m => d.StartsWith(m + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) mods.Add(d);
        return mods;
    }

    /// <summary>Unzips, refusing entries that would land outside <paramref name="dest"/> ("..\..\evil.dll").</summary>
    internal static void ExtractSafely(string zip, string dest)
    {
        var full = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zip);
        foreach (var e in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('\\', '/')));
            if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{Path.GetFileName(zip)} has a file that would go outside the mod folder ({e.FullName}); not installed.");
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, true);
        }
    }
}
