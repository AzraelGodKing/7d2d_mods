using System.Text.Json;

namespace SevenDaysModManager.Core;

/// <summary>
/// The command line, for scripts and desktop shortcuts:
/// <code>
/// SevenDaysModManager list [--json]        the installed mods (* = enabled)
/// SevenDaysModManager problems [--json]    problems with the enabled mods (exit code 1 on errors)
/// SevenDaysModManager lists                the saved modlists
/// SevenDaysModManager switch &lt;modlist&gt;     enable exactly the mods of a modlist
/// SevenDaysModManager play [&lt;modlist&gt;]     start the game, after switching to the modlist
/// SevenDaysModManager install &lt;zip|folder&gt; install a mod
/// </code>
/// </summary>
public static class Cli
{
    public static readonly string[] Commands = { "list", "problems", "lists", "switch", "play", "install", "help", "--help", "-h", "/?" };

    public static bool IsCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0].ToLowerInvariant());

    public static int Run(string[] args, TextWriter o, AppSettings s)
    {
        var cmd = args[0].ToLowerInvariant();
        var json = args.Contains("--json");
        var rest = args.Skip(1).Where(a => a != "--json").ToList();
        if (cmd is "help" or "--help" or "-h" or "/?") { o.WriteLine(Help); return 0; }

        var steam = SteamLibraries.Find();
        var install = SevenDays.Detect(steam, s.GameFolder, s.UserDataFolder);
        if (install == null) { o.WriteLine("7 Days to Die was not found. Open the app and pick the game folder."); return 2; }
        var scan = ModLibrary.Scan(install);
        var store = new ModlistStore(AppSettings.ModlistFolder);
        try
        {
            switch (cmd)
            {
                case "list":
                    SideInfo SideOf(InstalledMod m) => ModSides.Of(m, s.Sides.TryGetValue(m.Key, out var c) ? c : null);
                    if (json) o.WriteLine(JsonSerializer.Serialize(scan.Mods.Select(m => new { m.Key, m.DisplayName, version = m.Info?.Version, m.Enabled, m.HasCode, side = SideOf(m).Side.ToString(), sideGuessed = SideOf(m).Guessed, location = m.Location.ToString(), m.Folder }), new JsonSerializerOptions { WriteIndented = true }));
                    else foreach (var m in scan.Mods) o.WriteLine($"{(m.Enabled ? "*" : " ")} {m.DisplayName}{(m.Info?.Version is { Length: > 0 } v ? " " + v : "")}{(m.HasCode ? "  [code]" : "")}{(m.Locked ? "" : "  [" + SideOf(m).Short + "]")}  ({m.FolderName})");
                    return 0;
                case "problems":
                {
                    var found = Problems.Check(scan, scan.Mods.Where(m => m.Enabled).Select(m => m.Id).ToHashSet(), s.Eac, ModAnalysis.For(install));
                    if (json) o.WriteLine(JsonSerializer.Serialize(found.Select(p => new { severity = p.Severity.ToString(), mod = p.ModId, p.Text }), new JsonSerializerOptions { WriteIndented = true }));
                    else if (found.Count == 0) o.WriteLine("No problems.");
                    else foreach (var p in found) o.WriteLine($"{p.Severity}: {p.Text}");
                    return found.Any(p => p.Severity == Severity.Error) ? 1 : 0;
                }
                case "lists":
                    foreach (var l in store.All()) o.WriteLine($"{(string.Equals(l.Name, s.Modlist, StringComparison.OrdinalIgnoreCase) ? "*" : " ")} {l.Name} ({l.Mods.Count} mods)");
                    return 0;
                case "switch":
                    if (rest.Count == 0) { o.WriteLine("Usage: switch <modlist>"); return 2; }
                    return Switch(rest[0], scan, store, s, o) ? 0 : 1;
                case "play":
                    if (rest.Count > 0)
                    {
                        if (!Switch(rest[0], scan, store, s, o)) return 1;
                        scan = ModLibrary.Scan(install);
                    }
                    var r = GameRunner.Play(scan, s, steam.SteamFolder);
                    o.WriteLine("Starting 7 Days to Die. " + r.Why + ".");
                    return 0;
                case "install":
                    if (rest.Count == 0) { o.WriteLine("Usage: install <zip or folder>"); return 2; }
                    foreach (var done in ModInstaller.Install(rest[0], scan, s.InstallToUserData ? install.UserMods : install.GameMods, AppSettings.BackupFolder))
                    {
                        o.WriteLine((done.Replaced != null ? "Updated " : "Installed ") + done.Name + " in " + done.Folder);
                        var kept = new ModSettingsStore(AppSettings.ModSettingsFolder).Restore(done.Key, done.Folder);
                        if (kept.Count > 0) o.WriteLine("  your settings put back: " + string.Join(", ", kept));
                    }
                    return 0;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            o.WriteLine(e.Message);
            return 1;
        }
        o.WriteLine(Help);
        return 2;
    }

    static bool Switch(string name, LibraryScan scan, ModlistStore store, AppSettings s, TextWriter o)
    {
        var list = store.Get(name);
        if (list == null) { o.WriteLine($"No modlist called \"{name}\". Saved lists: {string.Join(", ", store.All().Select(l => l.Name))}"); return false; }
        if (GameRunner.IsRunning()) { o.WriteLine("7 Days to Die is running; close it first."); return false; }
        var (_, missing) = list.Match(scan);
        var moved = ModLibrary.Apply(scan, list.EnabledIds(scan));
        s.Modlist = list.Name;
        s.Save();
        o.WriteLine($"Switched to \"{list.Name}\" ({moved} mod{(moved == 1 ? "" : "s")} moved).");
        foreach (var m in missing) o.WriteLine($"  not installed: {m.DisplayName ?? m.Name}");
        return true;
    }

    public const string Help = """
        7 Days Mod Manager

          SevenDaysModManager                       open the window
          SevenDaysModManager list [--json]         the installed mods (* = enabled)
          SevenDaysModManager problems [--json]     problems with the enabled mods (exit code 1 on errors)
          SevenDaysModManager lists                 the saved modlists (* = last used)
          SevenDaysModManager switch <modlist>      enable exactly the mods of a modlist
          SevenDaysModManager play [<modlist>]      start the game, after switching to the modlist
          SevenDaysModManager install <zip|folder>  install a mod (an installed copy is replaced, after a backup)
        """;
}
