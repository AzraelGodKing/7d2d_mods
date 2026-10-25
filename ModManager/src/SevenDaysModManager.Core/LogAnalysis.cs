using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace SevenDaysModManager.Core;

/// <summary>Errors or warnings in the log that are the same message, with the mod most likely behind them.</summary>
/// <param name="Detail">The first occurrence with its stack trace.</param>
/// <param name="ModId">The mod it points at (an <see cref="InstalledMod.Id"/>), or null when nothing does.</param>
/// <param name="Why">How the mod was picked ("its code is in the stack trace"), or null.</param>
public sealed record LogGroup(Severity Severity, string Message, int Count, string Detail, int FirstLine, string? ModId, string? Why);

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
        // XML patch errors name the mod: "XML loader: Patching 'items.xml' from mod 'SomeMod' failed".
        foreach (var c in clues)
            foreach (var name in new[] { c.Mod.Key, c.Mod.FolderName }.Distinct().Where(n => n.Length >= 3))
                if (text.Contains($"'{name}'", StringComparison.OrdinalIgnoreCase) || text.Contains($"\"{name}\"", StringComparison.OrdinalIgnoreCase) || text.Contains($"mod {name}", StringComparison.OrdinalIgnoreCase))
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
