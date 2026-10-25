using System.Xml.Linq;

namespace SevenDaysModManager.Core;

/// <summary>
/// What a mod's ModInfo.xml says about it. Since Alpha 21 the file is
/// <c>&lt;xml&gt;&lt;Name value="..."/&gt;&lt;DisplayName value="..."/&gt;...&lt;/xml&gt;</c> (format 2); older mods wrap the same
/// fields in <c>&lt;ModInfo&gt;</c> (format 1), which current game versions still read but warn about.
/// </summary>
/// <param name="Name">The mod's id: the game refuses a second mod with the same Name.</param>
public sealed record ModInfo(string Name, string DisplayName, string Version, string Author, string Description, string Website, int Format)
{
    public const string FileName = "ModInfo.xml";

    /// <summary>Reads a ModInfo.xml file. Throws <see cref="FormatException"/> when it is not one.</summary>
    public static ModInfo Load(string path) => Parse(File.ReadAllText(path));

    public static ModInfo Parse(string text)
    {
        XDocument doc;
        try { doc = XDocument.Parse(text.TrimStart('﻿', ' ', '\t', '\r', '\n')); }
        catch (System.Xml.XmlException e) { throw new FormatException("ModInfo.xml is not valid XML: " + e.Message, e); }
        var root = doc.Root ?? throw new FormatException("ModInfo.xml is empty.");
        var v1 = root.Name.LocalName.Equals("ModInfo", StringComparison.OrdinalIgnoreCase) ? root
            : root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("ModInfo", StringComparison.OrdinalIgnoreCase));
        var fields = v1 ?? root;

        string Field(string name) =>
            fields.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } e
                ? ((string?)e.Attribute("value") ?? e.Value).Trim() : "";

        var id = Field("Name");
        if (id.Length == 0) throw new FormatException("ModInfo.xml has no Name.");
        var display = Field("DisplayName");
        return new ModInfo(id, display.Length > 0 ? display : id, Field("Version"), Field("Author"), Field("Description"), Field("Website"), v1 != null ? 1 : 2);
    }

    /// <summary>The ModInfo.xml in <paramref name="folder"/> whatever its letter case, or null.</summary>
    public static string? FindIn(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(FileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
