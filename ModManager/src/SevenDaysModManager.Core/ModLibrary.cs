namespace SevenDaysModManager.Core;

/// <summary>Which Mods folder a mod is in: the game's own, or the one in the player's data folder (1.0 and later).</summary>
public enum ModLocation { Game, UserData }

/// <summary>One installed mod folder, enabled (in Mods) or disabled (in Mods-disabled).</summary>
/// <param name="Folder">The mod's folder as it is now.</param>
/// <param name="Info">Its ModInfo.xml, or null when it has none or it can't be read (<paramref name="Error"/> says why).</param>
/// <param name="NestedFolder">When the folder has no ModInfo.xml but one a level or two down does (a zip unpacked into a folder of its own name), that inner folder.</param>
public sealed record InstalledMod(string Folder, ModLocation Location, bool Enabled, ModInfo? Info, string? Error, string? NestedFolder, IReadOnlyList<string> Dlls, long Size)
{
    public string FolderName => Path.GetFileName(Folder);

    /// <summary>Unique among installed mods: where it is and its folder name.</summary>
    public string Id => (Location == ModLocation.Game ? "game/" : "user/") + FolderName;

    /// <summary>What modlists and the game call the mod: its ModInfo Name, else its folder name.</summary>
    public string Key => Info?.Name ?? FolderName;

    public string DisplayName => Info?.DisplayName ?? FolderName;

    /// <summary>A mod with code: the game runs its DLLs, so EasyAntiCheat must be off for it to load.</summary>
    public bool HasCode => Dlls.Count > 0;

    /// <summary>The game's own mods (0_TFP_Harmony): needed by every code mod and never turned off here.</summary>
    public bool Locked => Location == ModLocation.Game && FolderName.StartsWith("0_TFP_", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Every mod the game can see, and every disabled one, after one read of the Mods folders.</summary>
public sealed class LibraryScan
{
    public GameInstall Install { get; }
    public IReadOnlyList<InstalledMod> Mods { get; }

    public LibraryScan(GameInstall install, IReadOnlyList<InstalledMod> mods)
    {
        Install = install;
        Mods = mods;
    }

    public InstalledMod? ById(string id) => Mods.FirstOrDefault(m => m.Id == id);

    /// <summary>
    /// The enabled mods in the order the game loads them: by folder name, A to Z (which is why the game's own folder is
    /// "0_TFP_Harmony"), the game's Mods folder before the data folder's.
    /// </summary>
    public IReadOnlyList<InstalledMod> LoadOrder(IEnumerable<string>? enabledIds = null)
    {
        var on = enabledIds == null ? null : new HashSet<string>(enabledIds);
        return Mods.Where(m => on == null ? m.Enabled : on.Contains(m.Id))
            .OrderBy(m => m.Location).ThenBy(m => m.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>Reads the Mods folders and moves mods between Mods and Mods-disabled.</summary>
public static class ModLibrary
{
    /// <summary>Reads every mod folder of both Mods folders and both Mods-disabled folders.</summary>
    public static LibraryScan Scan(GameInstall install)
    {
        var mods = new List<InstalledMod>();
        foreach (var (root, loc) in Roots(install))
        {
            mods.AddRange(ReadFolder(Path.Combine(root, SevenDays.ModsFolderName), loc, true));
            mods.AddRange(ReadFolder(Path.Combine(root, SevenDays.DisabledFolderName), loc, false));
        }
        return new LibraryScan(install, mods.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    static IEnumerable<(string Root, ModLocation Location)> Roots(GameInstall install)
    {
        yield return (install.GameFolder, ModLocation.Game);
        // Some players point the data folder at the game folder; read it once.
        if (!PathsEqual(install.GameFolder, install.UserDataFolder)) yield return (install.UserDataFolder, ModLocation.UserData);
    }

    static IEnumerable<InstalledMod> ReadFolder(string folder, ModLocation loc, bool enabled)
    {
        string[] dirs;
        try { dirs = Directory.Exists(folder) ? Directory.GetDirectories(folder) : Array.Empty<string>(); }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }
        foreach (var d in dirs)
        {
            if (Path.GetFileName(d).StartsWith('.')) continue;
            yield return ReadMod(d, loc, enabled);
        }
    }

    /// <summary>Reads one mod folder; never throws.</summary>
    public static InstalledMod ReadMod(string folder, ModLocation loc, bool enabled)
    {
        ModInfo? info = null;
        string? error = null, nested = null;
        var file = ModInfo.FindIn(folder);
        if (file != null)
        {
            try { info = ModInfo.Load(file); }
            catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { error = e.Message; }
        }
        else
        {
            nested = FindNested(folder);
            error = nested != null ? "ModInfo.xml is in a folder inside this one, so the game won't load it" : "No ModInfo.xml: the game doesn't see this folder as a mod";
            if (nested != null && ModInfo.FindIn(nested) is { } inner)
            {
                try { info = ModInfo.Load(inner); } catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { }
            }
        }
        var (dlls, size) = Contents(folder);
        return new InstalledMod(folder, loc, enabled, info, error, nested, dlls, size);
    }

    /// <summary>The folder up to three levels down that holds a ModInfo.xml, when there is exactly one.</summary>
    internal static string? FindNested(string folder)
    {
        try
        {
            var hits = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive })
                .Where(f => Path.GetFileName(f).Equals(ModInfo.FileName, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetDirectoryName).Distinct().Take(2).ToList();
            return hits.Count == 1 ? hits[0] : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static (List<string> Dlls, long Size) Contents(string folder)
    {
        var dlls = new List<string>();
        long size = 0;
        try
        {
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                size += f.Length;
                if (f.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)) dlls.Add(f.FullName);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return (dlls, size);
    }

    /// <summary>What <see cref="Apply"/> would move: each mod whose enabled state differs from <paramref name="enabledIds"/>.</summary>
    public static List<InstalledMod> Changes(LibraryScan scan, IReadOnlySet<string> enabledIds) =>
        scan.Mods.Where(m => !m.Locked && m.Enabled != enabledIds.Contains(m.Id)).ToList();

    /// <summary>
    /// Moves mods so that exactly <paramref name="enabledIds"/> are in a Mods folder and the rest in Mods-disabled. The
    /// game's own mods always stay. Stops at the first move that fails, leaving the mods already moved where they are;
    /// returns how many moved.
    /// </summary>
    public static int Apply(LibraryScan scan, IReadOnlySet<string> enabledIds)
    {
        var moved = 0;
        foreach (var m in Changes(scan, enabledIds))
        {
            var root = Directory.GetParent(Path.GetDirectoryName(m.Folder)!)!.FullName;
            var dest = Path.Combine(root, m.Enabled ? SevenDays.DisabledFolderName : SevenDays.ModsFolderName, m.FolderName);
            if (Directory.Exists(dest)) throw new IOException($"Can't move {m.FolderName}: {dest} already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Directory.Move(m.Folder, dest);
            moved++;
        }
        return moved;
    }

    /// <summary>
    /// Fixes a mod unpacked one folder too deep: the folder that holds ModInfo.xml takes the outer folder's place.
    /// Anything else in the outer folder (readmes, other versions) is zipped into <paramref name="backupFolder"/> first.
    /// Returns the mod's folder now.
    /// </summary>
    public static string FixNested(InstalledMod mod, string backupFolder)
    {
        if (mod.NestedFolder == null) return mod.Folder;
        var parent = Path.GetDirectoryName(mod.Folder)!;
        var holding = Path.Combine(parent, "." + mod.FolderName + ".fixing");
        Directory.Move(mod.Folder, holding);
        var inner = Path.Combine(holding, Path.GetRelativePath(mod.Folder, mod.NestedFolder));
        var name = Path.GetFileName(mod.NestedFolder);
        var dest = Path.Combine(parent, name);
        if (Directory.Exists(dest))
        {
            Directory.Move(holding, mod.Folder);
            throw new IOException($"Can't fix {mod.FolderName}: {dest} already exists.");
        }
        Directory.Move(inner, dest);
        if (Directory.EnumerateFiles(holding, "*", SearchOption.AllDirectories).Any())
            Backups.ZipFolder(holding, backupFolder, mod.FolderName + "-leftovers");
        Directory.Delete(holding, true);
        return dest;
    }

    public static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
