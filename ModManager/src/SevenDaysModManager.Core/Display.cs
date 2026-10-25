namespace SevenDaysModManager.Core;

/// <summary>Display choices that need no WinForms (the theme list and its names), so the tests can check them.</summary>
public static class Display
{
    public const string Dark = "Dark", Light = "Light", Kats = "Kats", Koneko = "Koneko", Serina = "Serina", Wolfie = "Wolfie", Vickra = "Vickra", Kitomi = "Kitomi", Szendoo = "Szendoo",
        Triphlx = "Triphlx", Tohma = "Tohma", Skywolf = "Skywolf", Hunter = "Hunter", System = "System";

    /// <summary>The themes in Options > Appearance, in the drop-down's order: saved name and the name shown.</summary>
    public static readonly (string Name, string Label)[] Themes =
    {
        (Dark, "Dark"), (Light, "Light"), (Kats, "Kats (orange and gunmetal)"),
        (Koneko, "Koneko (purple and black)"), (Serina, "Serina (purple and mint green)"), (Wolfie, "Wolfie (blue and green)"),
        (Vickra, "Vickra (sapphire and emerald)"), (Kitomi, "Kitomi (red, black and cyan)"),
        (Szendoo, "Szendoo (grey and pink)"),
        (Triphlx, "Triphlx (black and blue)"), (Tohma, "Tohma (blue and red)"),
        (Skywolf, "Skywolf (deep purple and black)"), (Hunter, "Hunter (black and silver)"),
        (System, "Match Windows"),
    };

    /// <summary>The saved theme choice: one of <see cref="Themes"/> ("System" follows Windows). Anything else is dark, the default.</summary>
    public static string NormalizeTheme(string? name) =>
        Themes.Select(t => t.Name).FirstOrDefault(t => string.Equals(name, t, StringComparison.OrdinalIgnoreCase)) ?? Dark;

    /// <summary>
    /// The palette to use for a choice. "System" follows Windows' app mode (<paramref name="windowsAppsUseLight"/>, null
    /// when it could not be read, which counts as dark).
    /// </summary>
    public static string ResolveTheme(string? choice, bool? windowsAppsUseLight) => NormalizeTheme(choice) switch
    {
        System => windowsAppsUseLight == true ? Light : Dark,
        var c => c,
    };

    /// <summary>Text sizes in Options > Appearance, as factors.</summary>
    public static readonly float[] TextSizes = { 1f, 1.1f, 1.25f, 1.5f };

    /// <summary>A label's text without its "&amp;" access key marks, as screen readers should say it.</summary>
    public static string StripMnemonic(string text) => text.Replace("&&", "\0").Replace("&", "").Replace("\0", "&");

    /// <summary>A byte count as people read it: "512 B", "1.4 MB".</summary>
    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }
}
