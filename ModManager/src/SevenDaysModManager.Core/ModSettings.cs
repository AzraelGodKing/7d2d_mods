using System.Text.RegularExpressions;

namespace SevenDaysModManager.Core;

/// <summary>One "Key=value" line of a settings file, with the comment lines just above it.</summary>
/// <param name="Line">Its index in <see cref="SettingsDoc.Lines"/>.</param>
/// <param name="Choices">The values to pick from when the value is a switch (on/off, true/false, yes/no), else null.</param>
public sealed record SettingEntry(int Line, string Key, string Value, string Description, string[]? Choices);

/// <summary>
/// A mod's settings file read for editing: every "Key=value" line with the comments above it as its description.
/// Changing a value rewrites only that line, so comments, order and blank lines stay exactly as the author wrote them.
/// Files without such lines (a list like entities.txt) are edited as text.
/// </summary>
public sealed partial class SettingsDoc
{
    [GeneratedRegex(@"^(\s*)([A-Za-z_][A-Za-z0-9_.\-]*)(\s*=\s*)(.*?)(\s*)$")]
    private static partial Regex EntryRx();

    public List<string> Lines { get; }
    public List<SettingEntry> Entries { get; } = new();
    readonly string _newline;
    readonly bool _endsWithNewline;

    public bool HasEntries => Entries.Count > 0;

    SettingsDoc(string text)
    {
        _newline = text.Contains("\r\n") ? "\r\n" : "\n";
        _endsWithNewline = text.EndsWith('\n');
        Lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (_endsWithNewline) Lines.RemoveAt(Lines.Count - 1);
        for (var i = 0; i < Lines.Count; i++)
        {
            var m = EntryRx().Match(Lines[i]);
            if (!m.Success) continue;
            var value = m.Groups[4].Value;
            Entries.Add(new SettingEntry(i, m.Groups[2].Value, value, Comment(i), ChoicesFor(value)));
        }
    }

    public static SettingsDoc Parse(string text) => new(text);

    /// <summary>The comment lines right above line <paramref name="i"/> (up to a blank line or another setting), without their "#".</summary>
    string Comment(int i)
    {
        var lines = new List<string>();
        for (var j = i - 1; j >= 0; j--)
        {
            var t = Lines[j].Trim();
            if (t.Length == 0 || !(t.StartsWith('#') || t.StartsWith(';') || t.StartsWith("//"))) break;
            lines.Insert(0, t.TrimStart('#', ';', '/').Trim());
        }
        return string.Join("\n", lines).Trim();
    }

    static readonly string[][] Switches = { new[] { "on", "off" }, new[] { "true", "false" }, new[] { "yes", "no" } };

    static string[]? ChoicesFor(string value)
    {
        foreach (var pair in Switches)
            if (pair.Contains(value.Trim().ToLowerInvariant()))
                // Keep the file's own capitals ("True" stays "True").
                return char.IsUpper(value.Trim().FirstOrDefault()) ? pair.Select(p => char.ToUpperInvariant(p[0]) + p[1..]).ToArray() : pair;
        return null;
    }

    public string? Get(string key) => Entries.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>Sets a value, rewriting only its line. False when the file has no such key.</summary>
    public bool Set(string key, string value)
    {
        var i = Entries.FindIndex(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        var e = Entries[i];
        var m = EntryRx().Match(Lines[e.Line]);
        var clean = value.Replace("\r", "").Replace("\n", " ");
        Lines[e.Line] = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + clean + m.Groups[5].Value;
        Entries[i] = e with { Value = clean };
        return true;
    }

    public string ToText() => string.Join(_newline, Lines) + (_endsWithNewline ? _newline : "");
}

/// <summary>
/// Finds a mod's settings files, and keeps the player's own copy of each one they save in the editor so it can be put
/// back when the mod is updated or reinstalled.
/// </summary>
public sealed class ModSettingsStore
{
    static readonly string[] Types = { ".txt", ".cfg", ".ini", ".conf", ".json", ".properties", ".yaml", ".yml", ".toml" };
    static readonly string[] NotSettings = { "readme", "license", "licence", "changelog", "credits", "put_", "install" };

    public string Folder { get; }
    public ModSettingsStore(string folder) => Folder = folder;

    /// <summary>The settings files at the top of a mod's folder (not its game XML, ModInfo.xml, readmes or examples).</summary>
    public static List<string> FilesOf(string modFolder)
    {
        try
        {
            return Directory.EnumerateFiles(modFolder)
                .Where(f => Types.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f =>
                {
                    var n = Path.GetFileName(f).ToLowerInvariant();
                    return !NotSettings.Any(n.StartsWith) && !n.Contains(".example.") && !n.Contains(".sample.") && !n.Contains("example.");
                })
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(); }
    }

    string Dir(string modKey) => Path.Combine(Folder, Backups.Safe(modKey));

    /// <summary>Writes the file into the mod and keeps the player's copy of it.</summary>
    public void Save(string modKey, string file, string text)
    {
        File.WriteAllText(file, text);
        var dir = Dir(modKey);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Path.GetFileName(file)), text);
    }

    public bool HasSaved(string modKey) => Directory.Exists(Dir(modKey)) && Directory.EnumerateFiles(Dir(modKey)).Any();

    /// <summary>Forgets the player's copies for a mod (the mod's files stay as they are).</summary>
    public void Forget(string modKey)
    {
        if (Directory.Exists(Dir(modKey))) Directory.Delete(Dir(modKey), true);
    }

    /// <summary>
    /// Puts the player's saved settings back into a freshly installed or updated mod. For "Key=value" files each saved
    /// value goes into the new file where the key still exists, so the new version's added settings and comments stay;
    /// other files are copied back whole. Returns the names of the files changed.
    /// </summary>
    public List<string> Restore(string modKey, string modFolder)
    {
        var changed = new List<string>();
        var dir = Dir(modKey);
        if (!Directory.Exists(dir)) return changed;
        foreach (var saved in Directory.GetFiles(dir))
        {
            var target = Path.Combine(modFolder, Path.GetFileName(saved));
            var mine = File.ReadAllText(saved);
            if (!File.Exists(target)) { File.WriteAllText(target, mine); changed.Add(Path.GetFileName(target)); continue; }
            var current = File.ReadAllText(target);
            var doc = SettingsDoc.Parse(current);
            var old = SettingsDoc.Parse(mine);
            string result;
            if (doc.HasEntries && old.HasEntries)
            {
                foreach (var e in old.Entries) doc.Set(e.Key, e.Value);
                result = doc.ToText();
            }
            else result = mine;
            if (result != current) { File.WriteAllText(target, result); changed.Add(Path.GetFileName(target)); }
        }
        return changed;
    }
}
