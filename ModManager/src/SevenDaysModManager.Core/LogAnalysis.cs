using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace SevenDaysModManager.Core;

/// <summary>Errors or warnings in the log that are the same message, with the mod most likely behind them.</summary>
/// <param name="Detail">The first occurrence with its stack trace.</param>
/// <param name="ModId">The mod it points at (an <see cref="InstalledMod.Id"/>), or null when nothing does.</param>
/// <param name="Why">How the mod was picked ("its code is in the stack trace"), or null.</param>
public sealed record LogGroup(Severity Severity, string Message, int Count, string Detail, int FirstLine, string? ModId, string? Why);

/// <summary>One line of the log as the line view shows it.</summary>
/// <param name="Number">Its line number, from 1.</param>
/// <param name="Level">The level of the entry it belongs to (a stack trace line has its entry's level).</param>
/// <param name="ModId">The mod its entry points at, or null.</param>
/// <param name="XmlPatch">The entry is about the game's XML patching (a mod's xpath patch that failed, or the like).</param>
/// <param name="Start">The line starts an entry (the others are its stack trace or text that runs on).</param>
public sealed record LogLine(int Number, LogLevel Level, string Text, string? ModId, bool XmlPatch, bool Start);

/// <summary>A log entry's level: INF, WRN, ERR/EXC.</summary>
public enum LogLevel { Info, Warning, Error }

/// <summary>
/// Reads the game's output log. Each entry starts "2024-07-29T20:47:41 21.590 ERR ..." (INF, WRN, ERR or EXC); the
/// lines after it up to the next entry are its stack trace. Errors and warnings are grouped by message, and each group
/// names the mod most likely at fault: a mod named in it (XML patch errors name the mod), a path into its folder, or
/// its code in the stack trace (namespaces read from the mod's DLLs, never run).
/// </summary>
public static partial class LogAnalysis
{
    [GeneratedRegex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\s+[\d.]+\s+(INF|WRN|ERR|EXC|LOG)\s?(.*)$")]
    private static partial Regex EntryRx();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRx();

    [GeneratedRegex(@"XML\s*(loader|patch)|xpath|Patching '[^']*' from mod|XML\s+file|\.xml'?\s+(failed|error)", RegexOptions.IgnoreCase)]
    private static partial Regex XmlPatchRx();

    /// <summary>
    /// Every line of the log with its entry's level, the mod its entry points at (the same way the groups pick one), and
    /// whether it is about XML patching, for the line view and its filters.
    /// </summary>
    public static List<LogLine> Lines(IReadOnlyList<string> lines, IReadOnlyList<InstalledMod> mods)
    {
        var clues = mods.Where(m => !m.Locked).Select(m => new Clues(m, m.HasCode ? Namespaces(m.Dlls) : Array.Empty<string>())).ToList();
        var result = new List<LogLine>(lines.Count);
        var i = 0;
        while (i < lines.Count)
        {
            var m = EntryRx().Match(lines[i]);
            if (!m.Success)
            {
                result.Add(new LogLine(i + 1, LogLevel.Info, lines[i], null, false, true));
                i++;
                continue;
            }
            var level = m.Groups[1].Value switch { "ERR" or "EXC" => LogLevel.Error, "WRN" => LogLevel.Warning, _ => LogLevel.Info };
            var end = i + 1;
            while (end < lines.Count && !EntryRx().IsMatch(lines[end])) end++;
            var text = end - i == 1 ? lines[i] : string.Join("\n", lines.Skip(i).Take(end - i));
            // Every entry is blamed, not only errors: "Loaded mod 'X'" lines belong to X too.
            var (mod, _) = Blame(text, clues);
            var xml = level != LogLevel.Info && XmlPatchRx().IsMatch(text);
            for (var j = i; j < end; j++) result.Add(new LogLine(j + 1, level, lines[j], mod?.Id, xml, j == i));
            i = end;
        }
        return result;
    }

    /// <summary>The lines that pass the line view's filters: a mod (null: any), the lowest level, and text to find.</summary>
    public static List<LogLine> Filter(IReadOnlyList<LogLine> lines, string? modId, LogLevel atLeast, string? find, bool xmlOnly = false)
    {
        var f = string.IsNullOrWhiteSpace(find) ? null : find.Trim();
        return lines.Where(l => (modId == null || l.ModId == modId) && l.Level >= atLeast && (!xmlOnly || l.XmlPatch)
            && (f == null || l.Text.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>How a mod is recognised in log text.</summary>
    sealed record Clues(InstalledMod Mod, string[] Namespaces);

    public static List<LogGroup> Analyze(IReadOnlyList<string> lines, IReadOnlyList<InstalledMod> mods)
    {
        var clues = mods.Where(m => !m.Locked).Select(m => new Clues(m, m.HasCode ? Namespaces(m.Dlls) : Array.Empty<string>())).ToList();
        var groups = new Dictionary<string, (Severity Sev, string Msg, int Count, string Detail, int Line)>();
        var order = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var m = EntryRx().Match(lines[i]);
            if (!m.Success) continue;
            var sev = m.Groups[1].Value switch { "ERR" or "EXC" => Severity.Error, "WRN" => Severity.Warning, _ => Severity.Info };
            if (sev == Severity.Info) continue;
            var msg = m.Groups[2].Value.Trim();
            var detail = new List<string> { msg };
            for (var j = i + 1; j < lines.Count && !EntryRx().IsMatch(lines[j]); j++) detail.Add(lines[j]);
            var key = sev + "|" + NumberRx().Replace(msg, "#");
            if (groups.TryGetValue(key, out var g)) groups[key] = (g.Sev, g.Msg, g.Count + 1, g.Detail, g.Line);
            else
            {
                groups[key] = (sev, msg, 1, string.Join("\n", detail).TrimEnd(), i + 1);
                order.Add(key);
            }
        }

        return order.Select(k =>
        {
            var g = groups[k];
            var (mod, why) = Blame(g.Detail, clues);
            return new LogGroup(g.Sev, g.Msg, g.Count, g.Detail, g.Line, mod?.Id, why);
        }).OrderByDescending(g => g.Severity).ThenBy(g => g.FirstLine).ToList();
    }

    static (InstalledMod?, string?) Blame(string text, List<Clues> clues)
    {
        // XML patch errors name the mod: "XML loader: Patching 'items.xml' from mod 'SomeMod' failed"; so does "[MODS] Loaded Mod: SomeMod (1.0)".
        foreach (var c in clues)
            foreach (var name in new[] { c.Mod.Key, c.Mod.FolderName }.Distinct().Where(n => n.Length >= 3))
                if (text.Contains($"'{name}'", StringComparison.OrdinalIgnoreCase) || text.Contains($"\"{name}\"", StringComparison.OrdinalIgnoreCase) || text.Contains($"mod {name}", StringComparison.OrdinalIgnoreCase) || text.Contains($"Mod: {name} ", StringComparison.OrdinalIgnoreCase))
                    return (c.Mod, "the message names it");
        foreach (var c in clues)
            if (text.Contains("Mods/" + c.Mod.FolderName + "/", StringComparison.OrdinalIgnoreCase) || text.Contains("Mods\\" + c.Mod.FolderName + "\\", StringComparison.OrdinalIgnoreCase))
                return (c.Mod, "a file in its folder is named");
        foreach (var c in clues)
            foreach (var ns in c.Namespaces)
                if (text.Contains("at " + ns + ".", StringComparison.Ordinal) || text.Contains("[" + ns + "]", StringComparison.Ordinal))
                    return (c.Mod, "its code is in the stack trace");
        return (null, null);
    }

    static readonly string[] CommonNamespaces = { "System", "Microsoft", "UnityEngine", "Unity", "HarmonyLib", "Mono", "Newtonsoft", "MonoMod", "Steamworks" };

    /// <summary>The top-level namespaces of the types in a mod's DLLs (game and library namespaces left out).</summary>
    internal static string[] Namespaces(IEnumerable<string> dlls)
    {
        var set = new HashSet<string>();
        foreach (var dll in dlls)
        {
            try
            {
                using var fs = File.OpenRead(dll);
                using var pe = new PEReader(fs);
                if (!pe.HasMetadata) continue;
                var md = pe.GetMetadataReader();
                foreach (var h in md.TypeDefinitions)
                {
                    var t = md.GetTypeDefinition(h);
                    var ns = md.GetString(t.Namespace);
                    if (ns.Length == 0) { var n = md.GetString(t.Name); if (n != "<Module>" && !n.StartsWith('<')) set.Add(n); continue; }
                    var top = ns.Split('.')[0];
                    if (!CommonNamespaces.Contains(top)) set.Add(ns);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { }
        }
        return set.OrderByDescending(s => s.Length).ToArray();
    }

    /// <summary>Reads a log the game may still be writing.</summary>
    public static List<string> ReadLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs);
        var list = new List<string>();
        while (r.ReadLine() is { } l) list.Add(l);
        return list;
    }
}
