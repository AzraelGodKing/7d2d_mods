using System.IO.Compression;

namespace SevenDaysModManager.Tests;

/// <summary>A game install and data folder in a temp folder, with mods made to order.</summary>
sealed class FakeGame : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "7dmm-test-" + Guid.NewGuid().ToString("N")[..8]);
    public GameInstall Install { get; }
    public string Backups => Path.Combine(Root, "backups");

    public FakeGame()
    {
        Install = new GameInstall(Path.Combine(Root, "game"), Path.Combine(Root, "appdata"));
        Directory.CreateDirectory(Install.GameMods);
        File.WriteAllText(Path.Combine(Install.GameFolder, SevenDays.Exe), "");
        AddMod(Install.GameMods, "0_TFP_Harmony", "0_TFP_Harmony", dll: true);
    }

    public static string ModInfoXml(string name, string? display = null, string version = "1.0") =>
        $"""<?xml version="1.0" encoding="UTF-8" ?><xml><Name value="{name}" /><DisplayName value="{display ?? name}" /><Version value="{version}" /><Author value="Someone" /><Description value="Does things" /><Website value="example.com" /></xml>""";

    public string AddMod(string modsFolder, string folder, string name, bool dll = false, string version = "1.0")
    {
        var d = Path.Combine(modsFolder, folder);
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "ModInfo.xml"), ModInfoXml(name, version: version));
        if (dll) File.WriteAllText(Path.Combine(d, name + ".dll"), "not really a dll");
        return d;
    }

    public string Disabled(ModLocation loc) => Path.Combine(loc == ModLocation.Game ? Install.GameFolder : Install.UserDataFolder, SevenDays.DisabledFolderName);

    /// <summary>Zips files given as (path in zip, content).</summary>
    public string Zip(string name, params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(Root);
        var zip = Path.Combine(Root, name);
        using var a = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (p, c) in files)
        {
            using var w = new StreamWriter(a.CreateEntry(p).Open());
            w.Write(c);
        }
        return zip;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}
