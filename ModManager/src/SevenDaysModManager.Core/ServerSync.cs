using System.IO.Compression;

namespace SevenDaysModManager.Core;

/// <summary>How the app reaches a dedicated server's Mods folder.</summary>
public enum ServerKind
{
    /// <summary>A folder: a shared folder, a mapped drive, or a dedicated server installed on this PC.</summary>
    Folder,
    Ftp,
}

/// <summary>Where the dedicated server's Mods folder is (saved in the settings; the FTP password is encrypted by the window).</summary>
public sealed class ServerTarget
{
    public ServerKind Kind { get; set; } = ServerKind.Folder;
    /// <summary>The server's Mods folder, for <see cref="ServerKind.Folder"/>.</summary>
    public string Folder { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 21;
    public string User { get; set; } = "";
    /// <summary>The FTP password, encrypted for this Windows user (DPAPI) and base64; never plain text.</summary>
    public string? ProtectedPassword { get; set; }
    /// <summary>The Mods folder on the FTP server, from the login's home: "Mods", "7daystodie/Mods".</summary>
    public string RemotePath { get; set; } = "Mods";
    /// <summary>FTPS: FTP over TLS.</summary>
    public bool Tls { get; set; }

    public bool IsSet => Kind == ServerKind.Folder ? Folder.Trim().Length > 0 : Host.Trim().Length > 0;

    public IModsFolder Open(string password) => Kind == ServerKind.Folder
        ? new LocalModsFolder(Folder.Trim())
        : new FtpModsFolder(Host, Port, User.Trim(), password, RemotePath, Tls);
}

public enum SyncAction
{
    /// <summary>Not on the server: send the whole mod.</summary>
    Upload,
    /// <summary>On the server but different: send the files that changed and remove the ones the mod no longer has.</summary>
    Update,
    /// <summary>On the server and the same.</summary>
    Same,
    /// <summary>On the server, but here it is disabled or only for players: can be removed from the server.</summary>
    Remove,
    /// <summary>On the server and not installed here: left alone.</summary>
    Leave,
}

/// <summary>One mod in a compare of this PC's mods with the server's.</summary>
/// <param name="Folder">The mod's folder name on the server (what it is, or will be, called there).</param>
/// <param name="Source">The local folder the files come from, for Upload and Update.</param>
/// <param name="Send">Files to send, as paths inside the mod.</param>
/// <param name="Delete">Files on the server the mod no longer has.</param>
public sealed record SyncItem(SyncAction Action, string Folder, string Name, string? LocalVersion, string? ServerVersion, string Why,
    string? Source, IReadOnlyList<string> Send, long SendBytes, IReadOnlyList<string> Delete)
{
    /// <summary>Whether a push does it unless the player unticks it: sending yes, removing only when asked.</summary>
    public bool OnByDefault => Action is SyncAction.Upload or SyncAction.Update;
}

/// <summary>What a push did.</summary>
public sealed record SyncResult(int Sent, int Deleted, int ModsRemoved, long Bytes);

/// <summary>
/// Keeps a dedicated server's Mods folder in step with this PC: the server gets the enabled mods that are for the
/// server (Server only, or Server + clients), sending only the files that differ. Mods that are only for players, or
/// disabled here, can be removed from it; mods the server has that this PC doesn't are left alone.
/// </summary>
public static class ServerSync
{
    /// <summary>The enabled mods the server needs: Server only, or Server + clients.</summary>
    public static List<InstalledMod> ServerMods(LibraryScan scan, Func<InstalledMod, SideInfo> side) =>
        scan.Mods.Where(m => m.Enabled && !m.Locked && m.Info != null && side(m).Side != ModSide.Client).ToList();

    /// <summary>The game's own mods (0_TFP_Harmony) are on every server and never touched.</summary>
    static bool IsGames(string folder) => folder.StartsWith("0_TFP_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Compares the mods the server needs (<paramref name="wanted"/>) with what <paramref name="server"/> has.</summary>
    public static List<SyncItem> Compare(IReadOnlyList<InstalledMod> wanted, IReadOnlyList<InstalledMod> allLocal, Func<InstalledMod, SideInfo> side,
        IModsFolder server, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Reading the server's Mods folder...");
        var remote = server.ModFolders().Where(f => !IsGames(f)).ToList();
        var infos = new Dictionary<string, ModInfo?>(StringComparer.OrdinalIgnoreCase);
        ModInfo? InfoOf(string folder)
        {
            if (infos.TryGetValue(folder, out var i)) return i;
            try { i = server.ReadText(folder, ModInfo.FileName) is { } t ? ModInfo.Parse(t) : null; }
            catch (FormatException) { i = null; }
            return infos[folder] = i;
        }

        var items = new List<SyncItem>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in wanted)
        {
            ct.ThrowIfCancellationRequested();
            var root = Root(m);
            var name = Path.GetFileName(root);
            progress?.Report($"Comparing {m.DisplayName}...");
            // The same folder name, else a server folder holding the same mod under another name.
            var there = remote.FirstOrDefault(f => f.Equals(name, StringComparison.OrdinalIgnoreCase) && !matched.Contains(f))
                ?? remote.FirstOrDefault(f => !matched.Contains(f) && !wanted.Any(w => Path.GetFileName(Root(w)).Equals(f, StringComparison.OrdinalIgnoreCase))
                    && string.Equals(InfoOf(f)?.Name, m.Key, StringComparison.OrdinalIgnoreCase));
            var local = LocalFiles(root);
            var version = Blank(m.Info?.Version);
            if (there == null)
            {
                items.Add(new SyncItem(SyncAction.Upload, name, m.DisplayName, version, null, "not on the server",
                    root, local.Select(f => f.Path).ToList(), local.Sum(f => f.Size), Array.Empty<string>()));
                continue;
            }
            matched.Add(there);
            var serverVersion = Blank(InfoOf(there)?.Version);
            var files = server.Files(there).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var newVersion = version != null && serverVersion != null && version != serverVersion;
            var send = local.Where(f => newVersion || !files.TryGetValue(f.Path, out var r) || r.Size != f.Size
                || (r.Modified is { } rm && f.Modified is { } lm && lm > rm.AddSeconds(2))).ToList();
            var localSet = local.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var delete = files.Keys.Where(p => !localSet.Contains(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            if (send.Count == 0 && delete.Count == 0)
            {
                items.Add(new SyncItem(SyncAction.Same, there, m.DisplayName, version, serverVersion, "the same", root, Array.Empty<string>(), 0, Array.Empty<string>()));
                continue;
            }
            var why = newVersion ? $"the server has {serverVersion}"
                : $"{send.Count} file{(send.Count == 1 ? "" : "s")} differ{(send.Count == 1 ? "s" : "")}{(delete.Count > 0 ? $", {delete.Count} to remove" : "")}";
            if (!there.Equals(name, StringComparison.OrdinalIgnoreCase)) why += $" (in {there} on the server)";
            items.Add(new SyncItem(SyncAction.Update, there, m.DisplayName, version, serverVersion, why, root, send.Select(f => f.Path).ToList(), send.Sum(f => f.Size), delete));
        }

        foreach (var f in remote.Where(f => !matched.Contains(f)))
        {
            ct.ThrowIfCancellationRequested();
            var info = InfoOf(f);
            var mine = allLocal.FirstOrDefault(m => !m.Locked && (info != null && string.Equals(m.Info?.Name, info.Name, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(Root(m)).Equals(f, StringComparison.OrdinalIgnoreCase)));
            var name = info?.DisplayName ?? f;
            var serverVersion = Blank(info?.Version);
            if (mine == null)
                items.Add(new SyncItem(SyncAction.Leave, f, name, null, serverVersion, "not installed here", null, Array.Empty<string>(), 0, Array.Empty<string>()));
            else
                items.Add(new SyncItem(SyncAction.Remove, f, name, Blank(mine.Info?.Version), serverVersion,
                    !mine.Enabled ? "disabled here" : side(mine).Side == ModSide.Client ? "only for players (client only)" : "not a server mod here",
                    null, Array.Empty<string>(), 0, Array.Empty<string>()));
        }
        return items.OrderBy(i => i.Action).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Does <paramref name="items"/> on <paramref name="server"/>: sends and removes files, and removes whole mods for <see cref="SyncAction.Remove"/>.</summary>
    public static SyncResult Push(IEnumerable<SyncItem> items, IModsFolder server, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        int sent = 0, deleted = 0, removed = 0;
        long bytes = 0;
        foreach (var item in items)
        {
            switch (item.Action)
            {
                case SyncAction.Upload or SyncAction.Update when item.Source != null:
                    var i = 0;
                    foreach (var path in item.Send)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report($"{item.Name}: sending {path} ({++i} of {item.Send.Count})");
                        var local = Path.Combine(item.Source, path.Replace('/', Path.DirectorySeparatorChar));
                        server.Upload(local, item.Folder, path);
                        bytes += new FileInfo(local).Length;
                        sent++;
                    }
                    foreach (var path in item.Delete)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report($"{item.Name}: removing {path}");
                        server.DeleteFile(item.Folder, path);
                        deleted++;
                    }
                    break;
                case SyncAction.Remove:
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Removing {item.Name} from the server...");
                    server.DeleteFolder(item.Folder);
                    removed++;
                    break;
            }
        }
        return new SyncResult(sent, deleted, removed, bytes);
    }

    /// <summary>The folder that holds the mod's ModInfo.xml (the inner one for a mod unpacked a folder too deep).</summary>
    internal static string Root(InstalledMod m) => m.NestedFolder ?? m.Folder;

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static List<RemoteFile> LocalFiles(string root) =>
        new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Select(f => new RemoteFile(Path.GetRelativePath(root, f.FullName).Replace('\\', '/'), f.Length, f.LastWriteTimeUtc)).ToList();
}

/// <summary>
/// A zip for players: the enabled mods they need (Client only, or Server + clients), so their mods match the server's.
/// It unzips into the game folder (or %AppData%\7DaysToDie) as Mods\..., or drops onto 7 Days Mod Manager, which
/// installs every mod in it. The profile is inside too, so the mod manager can switch to exactly those mods.
/// </summary>
public static class ClientPack
{
    /// <summary>The enabled mods players need: Client only, or Server + clients.</summary>
    public static List<InstalledMod> ClientMods(LibraryScan scan, Func<InstalledMod, SideInfo> side) =>
        scan.Mods.Where(m => m.Enabled && !m.Locked && m.Info != null && side(m).Side != ModSide.Server).ToList();

    /// <summary>Writes the pack to <paramref name="zipPath"/> (replacing it); returns how many mods are in it.</summary>
    public static int Create(IReadOnlyList<InstalledMod> mods, string zipPath, string packName)
    {
        var tmp = zipPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var m in mods)
            {
                var root = ServerSync.Root(m);
                var name = Path.GetFileName(root);
                foreach (var f in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    zip.CreateEntryFromFile(f, "Mods/" + name + "/" + Path.GetRelativePath(root, f).Replace('\\', '/'), CompressionLevel.Optimal);
            }
            var list = Modlist.From(packName, mods);
            using (var w = new StreamWriter(zip.CreateEntry(Backups.Safe(packName) + ".7dmm.json").Open())) w.Write(list.ToJson());
            using (var w = new StreamWriter(zip.CreateEntry("README.txt").Open())) w.Write(Readme(packName, mods));
        }
        File.Move(tmp, zipPath, true);
        return mods.Count;
    }

    static string Readme(string packName, IReadOnlyList<InstalledMod> mods) =>
        $"""
        {packName}: the mods you need to play on this server.

        Install, either way:
          - Drop this zip on 7 Days Mod Manager (it installs every mod in it), or
          - Unzip it into your 7 Days to Die folder, or into %AppData%\7DaysToDie, so the Mods folder in it lands there.

        Mods ({mods.Count}):
        {string.Join("\r\n", mods.Select(m => $"  {m.DisplayName}{(string.IsNullOrEmpty(m.Info?.Version) ? "" : " " + m.Info!.Version)}"))}

        Made by 7 Days Mod Manager on {DateTime.Now:d}.
        """.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
}
