using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SevenDaysModManager.Core;

/// <summary>One mod in a modlist: its ModInfo Name, and what it was called and which version it was when the list was made.</summary>
public sealed record ModlistEntry(string Name, string? DisplayName = null, string? Version = null);

/// <summary>
/// A named set of mods the player keeps (a profile): switch to it, share it, or import it on another PC. It can also
/// carry how Play starts the game with it (EasyAntiCheat and launch options), which win over the ones in Options.
/// </summary>
public sealed class Modlist
{
    public const int FormatVersion = 1;
    public const string SharePrefix = "7DMM1:";
    /// <summary>The built-in profile with no mods but the game's own; never saved as a file.</summary>
    public const string VanillaName = "Vanilla (no mods)";
    public int Format { get; set; } = FormatVersion;
    public string Name { get; set; } = "";
    public List<ModlistEntry> Mods { get; set; } = new();
    public DateTime Saved { get; set; }
    /// <summary>EasyAntiCheat for Play with this profile, or null to use the one in Options.</summary>
    public EacMode? Eac { get; set; }
    /// <summary>Launch options for Play with this profile, or null to use the ones in Options.</summary>
    public string? LaunchOptions { get; set; }

    public static Modlist Vanilla() => new() { Name = VanillaName };

    public static bool IsVanilla(string? name) =>
        string.Equals(name, VanillaName, StringComparison.OrdinalIgnoreCase) || string.Equals(name, "vanilla", StringComparison.OrdinalIgnoreCase);

    /// <summary>The EasyAntiCheat setting and launch options Play uses: this profile's where it has them, else the app's.</summary>
    public (EacMode Eac, string LaunchOptions) PlaySettings(AppSettings s) => (Eac ?? s.Eac, LaunchOptions ?? s.LaunchOptions);

    /// <summary>A list of the enabled mods, without the game's own (every install has those).</summary>
    public static Modlist From(string name, IEnumerable<InstalledMod> enabled)
    {
        var list = new Modlist { Name = name, Saved = DateTime.UtcNow };
        foreach (var m in enabled)
        {
            if (m.Locked || list.Mods.Any(e => string.Equals(e.Name, m.Key, StringComparison.OrdinalIgnoreCase))) continue;
            list.Mods.Add(new ModlistEntry(m.Key, m.DisplayName, string.IsNullOrEmpty(m.Info?.Version) ? null : m.Info!.Version));
        }
        return list;
    }

    /// <summary>
    /// The installed mods this list turns on (their ids, one per entry: the enabled copy when there are two) and the
    /// entries that aren't installed.
    /// </summary>
    public (List<string> Installed, List<ModlistEntry> Missing) Match(LibraryScan scan)
    {
        var installed = new List<string>();
        var missing = new List<ModlistEntry>();
        foreach (var e in Mods)
        {
            var m = scan.Mods.Where(x => string.Equals(x.Key, e.Name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Enabled).FirstOrDefault();
            if (m != null) { if (!installed.Contains(m.Id)) installed.Add(m.Id); }
            else missing.Add(e);
        }
        return (installed, missing);
    }

    /// <summary>The ids to enable to switch to this list: its mods, plus the game's own.</summary>
    public HashSet<string> EnabledIds(LibraryScan scan)
    {
        var ids = new HashSet<string>(Match(scan).Installed);
        foreach (var m in scan.Mods) if (m.Locked) ids.Add(m.Id);
        return ids;
    }

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    /// <summary>Reads a modlist file. Throws <see cref="FormatException"/> when it isn't one.</summary>
    public static Modlist Parse(string text, string fallbackName = "Imported")
    {
        var t = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (t.StartsWith(SharePrefix, StringComparison.Ordinal)) return FromShareCode(t);
        Modlist? m;
        try { m = JsonSerializer.Deserialize<Modlist>(t, Opts); }
        catch (JsonException e) { throw new FormatException("The modlist file is not valid: " + e.Message, e); }
        if (m == null) throw new FormatException("The modlist file is empty.");
        if (m.Format > FormatVersion) throw new FormatException($"The modlist was made by a newer version of the app (format {m.Format}).");
        m.Mods = m.Mods.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name)).ToList();
        if (m.Name.Length == 0) m.Name = fallbackName;
        return m;
    }

    /// <summary>The list as one line of text to paste in a chat: "7DMM1:" and the compressed list.</summary>
    public string ToShareCode()
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, new JsonSerializerOptions(Opts) { WriteIndented = false }));
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize)) z.Write(bytes);
        return SharePrefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static Modlist FromShareCode(string code)
    {
        var t = code.Trim();
        if (!t.StartsWith(SharePrefix, StringComparison.Ordinal)) throw new FormatException("Not a modlist share code: it should start with " + SharePrefix);
        var b64 = t[SharePrefix.Length..].Trim().Replace('-', '+').Replace('_', '/');
        b64 += new string('=', (4 - b64.Length % 4) % 4);
        try
        {
            using var z = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            using var r = new StreamReader(z, Encoding.UTF8);
            var json = r.ReadToEnd();
            return Parse(json, "Shared list");
        }
        catch (Exception e) when (e is FormatException or InvalidDataException) { throw new FormatException("The share code is damaged or cut off.", e); }
    }
}

/// <summary>The saved modlists: one JSON file each in a folder.</summary>
public sealed class ModlistStore
{
    public string Folder { get; }
    public ModlistStore(string folder) => Folder = folder;

    public List<Modlist> All()
    {
        var lists = new List<Modlist>();
        if (!Directory.Exists(Folder)) return lists;
        foreach (var f in Directory.GetFiles(Folder, "*.json"))
        {
            try { lists.Add(Modlist.Parse(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f))); }
            catch (Exception e) when (e is FormatException or IOException) { }
        }
        return lists.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A saved list by name, or the built-in Vanilla profile for its name (or "vanilla").</summary>
    public Modlist? Get(string name) =>
        Modlist.IsVanilla(name) ? Modlist.Vanilla() : All().FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    string PathFor(string name) => Path.Combine(Folder, Backups.Safe(name) + ".json");

    public void Save(Modlist list)
    {
        if (Modlist.IsVanilla(list.Name)) throw new IOException($"\"{list.Name}\" is the built-in profile with no mods; pick another name.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathFor(list.Name), list.ToJson());
    }

    public void Delete(string name)
    {
        var p = PathFor(name);
        if (File.Exists(p)) File.Delete(p);
    }
}
