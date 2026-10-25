namespace SevenDaysModManager.Core;

/// <summary>Where a mod has to be installed for a multiplayer game.</summary>
public enum ModSide
{
    /// <summary>Only the server (or the host); players install nothing.</summary>
    Server,
    /// <summary>Only the players who want it; the server needs nothing.</summary>
    Client,
    /// <summary>The server and every player.</summary>
    Both,
}

/// <summary>A mod's side and how it was decided.</summary>
/// <param name="Guessed">True when it comes from the mod's files rather than its own words or the player's choice.</param>
/// <param name="Why">Shown in the details: "its description says so", "it changes the HUD (XUi)", ...</param>
public sealed record SideInfo(ModSide Side, bool Guessed, string Why)
{
    public string Label => Side switch { ModSide.Server => "Server only", ModSide.Client => "Client only", _ => "Server + clients" } + (Guessed ? " (guess)" : "");
    public string Short => Side switch { ModSide.Server => "Server", ModSide.Client => "Client", _ => "Both" } + (Guessed ? "?" : "");
}

/// <summary>
/// Tells whether a mod is for the server, the players, or both. In order: the player's own choice; the mod's
/// description, since most mods say it ("Server only: players install nothing", "Only players need it"); and last a
/// guess from its files. The server sends its XML to players when they join, so a mod of XML alone works from the
/// server; HUD windows (XUi), textures, sounds and asset bundles have to be on each player's PC; code (a DLL) can be
/// either, so it counts as both.
/// </summary>
public static class ModSides
{
    static readonly string[] BothWords =
    {
        "server and every player", "server and all players", "server and each player", "server and the players",
        "server and players", "server and clients", "both server and client", "server and client", "every player and the server",
    };

    static readonly string[] ServerWords =
    {
        "server only", "server-only", "server side only", "server-side only", "server-side", "serverside",
        "players install nothing", "players do not install", "players don't install", "players do not need to install",
        "players don't need to install", "players need nothing", "nobody installs anything", "only the server needs",
        "clients do not need", "clients don't need",
    };

    static readonly string[] ClientWords =
    {
        "client only", "client-only", "client side", "client-side", "clientside", "only players need",
        "only the players who want it", "server needs nothing", "server does not need", "server doesn't need",
        "players only", "server not required",
    };

    public static SideInfo Of(InstalledMod mod, ModSide? chosen)
    {
        if (chosen is { } c) return new SideInfo(c, false, "you set it");
        if (FromWords(mod.Info?.Description) is { } said) return said;
        return FromFiles(mod.NestedFolder ?? mod.Folder, mod.HasCode);
    }

    /// <summary>The side a mod's description names, or null when it names none.</summary>
    public static SideInfo? FromWords(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var d = description.ToLowerInvariant().Replace('’', '\'');
        if (BothWords.Any(d.Contains)) return new SideInfo(ModSide.Both, false, "its description says so");
        var server = ServerWords.Any(d.Contains);
        var client = ClientWords.Any(d.Contains);
        if (server && client) return new SideInfo(ModSide.Both, true, "its description mentions both");
        if (server) return new SideInfo(ModSide.Server, false, "its description says so");
        if (client) return new SideInfo(ModSide.Client, false, "its description says so");
        return null;
    }

    static readonly string[] ClientFileTypes = { ".unity3d", ".bundle", ".ogg", ".wav", ".mp3", ".png", ".jpg", ".dds", ".tga" };

    /// <summary>A guess from what is in the mod's folder.</summary>
    public static SideInfo FromFiles(string folder, bool hasCode)
    {
        bool ui = false, assets = false, xml = false;
        try
        {
            var root = Path.GetFullPath(folder);
            foreach (var f in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 }))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                var parts = rel.Split('/');
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (parts.Any(p => p.StartsWith("XUi", StringComparison.OrdinalIgnoreCase) || p.Equals("UIAtlases", StringComparison.OrdinalIgnoreCase))) ui = true;
                else if (ClientFileTypes.Contains(ext) && !rel.Equals("preview.png", StringComparison.OrdinalIgnoreCase)) assets = true;
                else if (parts.Length > 1 && parts[0].Equals("Config", StringComparison.OrdinalIgnoreCase) && (ext == ".xml" || ext == ".csv")) xml = true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        if (hasCode) return new SideInfo(ModSide.Both, true, "it has code, which may be needed on either side");
        if (ui || assets)
            return xml
                ? new SideInfo(ModSide.Both, true, ui ? "it changes the HUD (XUi) and game XML" : "it has textures, sounds or asset bundles, and game XML")
                : new SideInfo(ModSide.Client, true, ui ? "it changes the HUD (XUi), which each player needs" : "it has textures, sounds or asset bundles, which each player needs");
        if (xml) return new SideInfo(ModSide.Server, true, "it's game XML only, which the server sends to players");
        return new SideInfo(ModSide.Both, true, "nothing in it says");
    }
}
