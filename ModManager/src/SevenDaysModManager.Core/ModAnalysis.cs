using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SevenDaysModManager.Core;

/// <summary>One XPath patch in a mod's Config XML: &lt;set xpath="..."&gt;, &lt;append&gt;, &lt;remove&gt; and the rest.</summary>
/// <param name="File">The config file it patches, from the mod's Config folder ("items.xml", "xui/windows.xml"), lower case.</param>
/// <param name="Target">What it changes: the xpath, plus "@name" for setattribute and removeattribute.</param>
/// <param name="Adds">For append and insert: the names of the things it adds ("item:gunPistol"), to find two mods adding the same one.</param>
public sealed record XmlPatch(string File, string Op, string Target, IReadOnlyList<string> Adds);

/// <summary>A game version as the log states it ("V 1.4 (b8)", "Alpha 21.2 (b30)"): Alpha numbers or the 1.0-and-later major.minor.</summary>
public sealed record GameVersion(string Text, bool Alpha, int Major, int Minor)
{
    public override string ToString() => Text;
}

/// <summary>
/// The slower checks of the enabled mods, read once per scan and kept: two mods changing the same XML, a mod that
/// needs another one, and mods made for an older game version. <see cref="Problems.Check"/> adds what they find.
/// </summary>
public sealed partial class ModAnalysis
{
    readonly Dictionary<string, (List<XmlPatch> Patches, List<string> Broken)> _xml = new();

    /// <summary>The game version from its newest log, or null when it hasn't run yet.</summary>
    public GameVersion? Game { get; }

    public ModAnalysis(GameVersion? game) => Game = game;

    /// <summary>An analysis with the game version read from the newest log of <paramref name="install"/>.</summary>
    public static ModAnalysis For(GameInstall install)
    {
        GameVersion? game = null;
        try { if (SevenDays.LatestLog(install) is { } log) game = ReadGameVersion(FirstLines(log, 400)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new ModAnalysis(game);
    }

    static IEnumerable<string> FirstLines(string path, int count)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs);
        var lines = new List<string>();
        while (lines.Count < count && r.ReadLine() is { } l) lines.Add(l);
        return lines;
    }

    // ------------------------------------------------------------ game version

    [GeneratedRegex(@"\bVersion:\s*((Alpha|V)\s*(\d+)(?:\.(\d+))?[^,\r\n]*?)(?:\s+Compatibility|,|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LogVersionRx();

    /// <summary>The game version from the start of a log ("INF Version: V 1.4 (b8) Compatibility Version: V 1.4, Build: ...").</summary>
    public static GameVersion? ReadGameVersion(IEnumerable<string> lines)
    {
        foreach (var l in lines)
        {
            var m = LogVersionRx().Match(l);
            if (!m.Success) continue;
            var alpha = m.Groups[2].Value.Equals("Alpha", StringComparison.OrdinalIgnoreCase);
            return new GameVersion(m.Groups[1].Value.Trim(), alpha, int.Parse(m.Groups[3].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0);
        }
        return null;
    }

    [GeneratedRegex(@"\b(?:A|Alpha\s?)(1[5-9]|2[0-1])(?:\.\d+)?\b")]
    private static partial Regex ModAlphaRx();

    [GeneratedRegex(@"\b(?:for|made for|built for|updated for|works (?:on|with)|tested on|game|7DTD|7D2D)\s+(?:version\s+)?V\s?(\d)\.(\d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ModReleaseRx();

    /// <summary>
    /// The game version a mod says it is for, from its display name and description ("A21", "Alpha 20", "for V1.0"),
    /// or null when it says none. Its own Version field is the mod's version, so it isn't read.
    /// </summary>
    public static GameVersion? MadeFor(ModInfo info)
    {
        var text = info.DisplayName + " " + info.Description;
        var m = ModAlphaRx().Match(text);
        if (m.Success) return new GameVersion("Alpha " + m.Groups[1].Value, true, int.Parse(m.Groups[1].Value), 0);
        m = ModReleaseRx().Match(text);
        if (m.Success) return new GameVersion($"V{m.Groups[1].Value}.{m.Groups[2].Value}", false, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        return null;
    }

    /// <summary>True when a mod made for <paramref name="mod"/> is for an older game than <paramref name="game"/> (null: 1.0 or later).</summary>
    public static bool IsOlder(GameVersion mod, GameVersion? game)
    {
        if (game == null) return mod.Alpha;
        if (mod.Alpha != game.Alpha) return mod.Alpha;
        return mod.Major < game.Major;
    }

    // ------------------------------------------------------------ XML patches

    static readonly string[] Ops = { "set", "setattribute", "append", "remove", "removeattribute", "insertafter", "insertbefore", "csv", "prepend" };
    /// <summary>Ops that replace or take away what is there: two mods doing them to the same target fight.</summary>
    static readonly string[] Changing = { "set", "setattribute", "remove", "removeattribute", "csv" };

    /// <summary>The patches in a mod's Config XML, and the files that aren't valid XML. Read once per mod.</summary>
    public (List<XmlPatch> Patches, List<string> Broken) XmlOf(InstalledMod mod)
    {
        if (_xml.TryGetValue(mod.Id, out var cached)) return cached;
        var result = (new List<XmlPatch>(), new List<string>());
        var config = Path.Combine(mod.NestedFolder ?? mod.Folder, "Config");
        if (Directory.Exists(config))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(config, "*.xml", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive }).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { files = Array.Empty<string>(); }
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(config, f).Replace('\\', '/').ToLowerInvariant();
                try { result.Item1.AddRange(ReadPatches(rel, File.ReadAllText(f))); }
                catch (XmlException e) { result.Item2.Add($"Config/{Path.GetRelativePath(config, f).Replace('\\', '/')} (line {e.LineNumber}: {e.Message.Split(" Line ")[0].TrimEnd('.')})"); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return _xml[mod.Id] = result;
    }

    /// <summary>Reads the XPath patches in one config file. Throws <see cref="XmlException"/> when it isn't valid XML.</summary>
    public static List<XmlPatch> ReadPatches(string file, string xml)
    {
        var doc = XDocument.Parse(xml.TrimStart('﻿', ' ', '\t', '\r', '\n'), LoadOptions.SetLineInfo);
        var list = new List<XmlPatch>();
        foreach (var e in doc.Descendants())
        {
            var op = e.Name.LocalName.ToLowerInvariant();
            if (!Ops.Contains(op) || e.Attribute("xpath") is not { } xp) continue;
            var target = NormalizeXPath(xp.Value);
            if (op is "setattribute" or "removeattribute" && (string?)e.Attribute("name") is { Length: > 0 } attr) target += "@" + attr;
            var adds = op is "append" or "insertafter" or "insertbefore" or "prepend"
                ? e.Elements().Where(c => (string?)c.Attribute("name") is { Length: > 0 }).Select(c => c.Name.LocalName + ":" + (string)c.Attribute("name")!).ToList()
                : new List<string>();
            list.Add(new XmlPatch(file, op, target, adds));
        }
        return list;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRx();

    /// <summary>An xpath written the same way whatever its spacing and quotes: "/items/item[@name='x']".</summary>
    public static string NormalizeXPath(string xpath)
    {
        var s = SpaceRx().Replace(xpath.Trim(), " ").Replace('"', '\'');
        s = Regex.Replace(s, @"\s*([\[\]=/,()])\s*", "$1");
        return s.TrimEnd('/');
    }

    /// <summary>
    /// Mods that change the same XML: two mods replacing or removing the same thing (the one loaded last wins), and two
    /// adding something with the same name (the game gets it twice). Mods are in load order.
    /// </summary>
    public List<ModProblem> Conflicts(IReadOnlyList<InstalledMod> loadOrder)
    {
        var list = new List<ModProblem>();
        var mods = loadOrder.Where(m => !m.Locked && m.Info != null).ToList();
        var patches = mods.SelectMany(m => XmlOf(m).Patches.Select(p => (Mod: m, Patch: p))).ToList();

        foreach (var g in patches.Where(x => Changing.Contains(x.Patch.Op)).GroupBy(x => (x.Patch.File, x.Patch.Target)))
        {
            var who = g.Select(x => x.Mod).Distinct().ToList();
            if (who.Count < 2) continue;
            var last = who[^1];
            list.Add(new ModProblem(Severity.Warning, last.Id,
                $"{Names(who)} all change {Short(g.Key.Target)} in {g.Key.File}. {last.DisplayName} loads last, so its change wins."));
        }

        foreach (var g in patches.SelectMany(x => x.Patch.Adds.Select(a => (x.Mod, x.Patch.File, Add: a))).GroupBy(x => (x.File, x.Add), x => x.Mod))
        {
            var who = g.Distinct().ToList();
            if (who.Count < 2) continue;
            var (kind, name) = (g.Key.Add.Split(':')[0], g.Key.Add[(g.Key.Add.IndexOf(':') + 1)..]);
            list.Add(new ModProblem(Severity.Warning, who[^1].Id, $"{Names(who)} all add the {kind} \"{name}\" in {g.Key.File}, so the game gets it more than once."));
        }

        foreach (var m in mods)
            foreach (var broken in XmlOf(m).Broken)
                list.Add(new ModProblem(Severity.Error, m.Id, $"{m.DisplayName}: {broken} is not valid XML, so the game can't load it."));
        return list;
    }

    static string Names(IReadOnlyList<InstalledMod> mods) =>
        mods.Count == 2 ? $"{mods[0].DisplayName} and {mods[1].DisplayName}" : string.Join(", ", mods.Take(mods.Count - 1).Select(m => m.DisplayName)) + " and " + mods[^1].DisplayName;

    static string Short(string xpath) => xpath.Length <= 90 ? xpath : xpath[..87] + "...";

    // ------------------------------------------------------------ requirements

    [GeneratedRegex(@"\b(?:needs|requires|require|depends on|requirement:|required:)\s+(?:the\s+)?(.{2,60}?)(?=\s+mod\b|\s+installed\b|\s+on the server\b|[.,;:()!]|\s+(?:to|and|or|for|v?\d)\b|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NeedsRx();

    static readonly string[] NotMods = { "eac", "easyanticheat", "easy anti cheat", "harmony", "0_tfp_harmony", "a new game", "a new world", "new game", "a restart", "restart", "nothing", "no", "it", "this", "players", "the server", "server", "client", "steam" };

    /// <summary>
    /// A mod that says it needs another ("Needs the Bait mod on the server", "Requires SCore") when that one is
    /// installed but disabled, or not installed at all. One needed on the server only counts when it is installed here.
    /// </summary>
    public static List<ModProblem> Requirements(IReadOnlyList<InstalledMod> all, IReadOnlySet<string> enabledIds)
    {
        var list = new List<ModProblem>();
        foreach (var m in all.Where(m => enabledIds.Contains(m.Id) && !m.Locked && m.Info != null))
        {
            foreach (Match match in NeedsRx().Matches(m.Info!.Description))
            {
                var phrase = match.Groups[1].Value.Trim().Trim('"', '\'');
                if (phrase.Length < 2 || NotMods.Contains(phrase.ToLowerInvariant())) continue;
                var after = m.Info.Description[(match.Index + match.Length)..].TrimStart().ToLowerInvariant();
                var saysMod = after.StartsWith("mod");
                var onServer = after.StartsWith("on the server") || after.StartsWith("mod on the server");
                var found = all.Where(o => o.Id != m.Id && !o.Locked && o.Info != null && Names(o, phrase)).ToList();
                if (found.Any(o => enabledIds.Contains(o.Id))) continue;
                if (found.Count > 0)
                    list.Add(new ModProblem(onServer ? Severity.Info : Severity.Warning, m.Id,
                        $"{m.DisplayName} needs {found[0].DisplayName}{(onServer ? " on the server" : "")}, which is disabled here."));
                else if (saysMod && !onServer && phrase.Split(' ').Length <= 4)
                    list.Add(new ModProblem(Severity.Warning, m.Id, $"{m.DisplayName} says it needs the {phrase} mod, which isn't installed."));
            }
        }
        return list;
    }

    /// <summary>Whether <paramref name="phrase"/> names mod <paramref name="m"/>: its Name, folder or display name, or the end of its Name ("Bait" for "AzraelBait").</summary>
    static bool Names(InstalledMod m, string phrase)
    {
        var p = Squash(phrase);
        if (p.Length < 3) return false;
        var name = Squash(m.Info!.Name);
        var display = Squash(m.Info.DisplayName);
        return name == p || Squash(m.FolderName) == p || display == p || name.EndsWith(p) && name.Length - p.Length <= 8
            || Regex.IsMatch(m.Info.DisplayName, $@"(^|\W){Regex.Escape(phrase)}($|\W)", RegexOptions.IgnoreCase) && display.Length - p.Length <= 20;
    }

    static string Squash(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // ------------------------------------------------------------ all of it

    /// <summary>Everything this analysis finds for the mods that would be enabled.</summary>
    public List<ModProblem> Check(LibraryScan scan, IReadOnlySet<string> enabledIds)
    {
        var order = scan.LoadOrder(enabledIds);
        var list = Conflicts(order);
        list.AddRange(Requirements(scan.Mods, enabledIds));
        foreach (var m in order.Where(m => !m.Locked && m.Info is { Format: 2 }))
            if (MadeFor(m.Info!) is { } made && IsOlder(made, Game))
                list.Add(new ModProblem(Severity.Warning, m.Id,
                    $"{m.DisplayName} says it is for {made}, and the game is {(Game?.Text ?? "1.0 or later")}. It may not work; look for an update."));
        return list;
    }
}
