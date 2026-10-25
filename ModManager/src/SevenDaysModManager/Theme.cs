using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SevenDaysModManager;

/// <summary>
/// What a control is for, which decides how the theme colours it. Unmarked controls get the default look. Header is the
/// brand bar at the top of the main window; Primary is the one button that does the main job (Upload); Rule is the thin
/// line under a card's title; Brand is the coloured half of the app name.
/// </summary>
public enum ThemeRole { None, Surface, Header, Divider, Muted, Title, Accent, Log, Keep, Primary, Rule, Brand }

/// <summary>Meaning of a status text; the theme picks a colour that reads well on its background.</summary>
public enum Tone { Normal, Muted, Ok, Warn, Error, Info }

public sealed record Palette(
    string Name, bool Dark,
    Color Back, Color Surface, Color Header, Color Input, Color Border,
    Color Text, Color Muted, Color Title,
    Color Button, Color ButtonText, Color ButtonHover, Color ButtonDown,
    Color Accent, Color AccentHover, Color AccentText,
    Color Primary, Color PrimaryHover,
    Color Track, Color ListHeader,
    Color Ok, Color Warn, Color Error, Color Info)
{
    /// <summary>The darker end of the gradient on Accent and Primary buttons.</summary>
    public Color AccentDeep { get; init; } = Color.FromArgb(18, 58, 168);
    public Color PrimaryDeep { get; init; } = Color.FromArgb(52, 92, 14);

    Color? _primaryText, _brand;

    /// <summary>Text on Primary buttons. Same as <see cref="AccentText"/> unless a theme's Primary needs its own.</summary>
    public Color PrimaryText { get => _primaryText ?? AccentText; init => _primaryText = value; }

    /// <summary>The coloured half of the app name. Same as <see cref="Info"/> unless a theme sets its own.</summary>
    public Color Brand { get => _brand ?? Info; init => _brand = value; }
}

/// <summary>
/// Dark (default), light and Windows High Contrast themes for the WinForms UI. <see cref="Apply"/> walks a control tree
/// and colours it; call it again after <see cref="Set"/> to switch live. The log keeps its console look in dark and light.
/// </summary>
public static class Theme
{
    public const string DarkName = Display.Dark, LightName = Display.Light, KatsName = Display.Kats, KonekoName = Display.Koneko,
        SerinaName = Display.Serina, WolfieName = Display.Wolfie, VickraName = Display.Vickra, KitomiName = Display.Kitomi,
        SzendooName = Display.Szendoo, TriphlxName = Display.Triphlx, TohmaName = Display.Tohma,
        SkywolfName = Display.Skywolf, HunterName = Display.Hunter, SystemName = Display.System, HighContrastName = "HighContrast";

    // The look follows Steam's own desktop client: deep slate-blue surfaces, a near-black brand bar, Steam blue for
    // links, ticks and selection, light-blue secondary buttons and a green button for the main job. Neither theme uses
    // Valve's logos or names as branding; the "not affiliated" line stays at the bottom of the window.

    public static readonly Palette DarkPalette = new(
        DarkName, true,
        Back: Color.FromArgb(27, 40, 56), Surface: Color.FromArgb(35, 52, 72), Header: Color.FromArgb(23, 26, 33),
        Input: Color.FromArgb(22, 32, 45), Border: Color.FromArgb(58, 80, 104),
        Text: Color.FromArgb(214, 224, 233), Muted: Color.FromArgb(139, 152, 166), Title: Color.White,
        Button: Color.FromArgb(42, 71, 94), ButtonText: Color.FromArgb(103, 193, 245),
        ButtonHover: Color.FromArgb(65, 122, 155), ButtonDown: Color.FromArgb(54, 103, 132),
        Accent: Color.FromArgb(26, 159, 255), AccentHover: Color.FromArgb(71, 191, 255), AccentText: Color.White,
        Primary: Color.FromArgb(100, 156, 30), PrimaryHover: Color.FromArgb(124, 186, 40),
        Track: Color.FromArgb(42, 63, 86), ListHeader: Color.FromArgb(28, 42, 58),
        Ok: Color.FromArgb(164, 208, 7), Warn: Color.FromArgb(232, 176, 74), Error: Color.FromArgb(240, 110, 100), Info: Color.FromArgb(102, 192, 244));

    public static readonly Palette LightPalette = new(
        LightName, false,
        Back: Color.FromArgb(234, 238, 243), Surface: Color.White, Header: Color.White,
        Input: Color.FromArgb(251, 252, 253), Border: Color.FromArgb(200, 208, 218),
        Text: Color.FromArgb(27, 40, 56), Muted: Color.FromArgb(88, 102, 118), Title: Color.FromArgb(23, 26, 33),
        Button: Color.FromArgb(228, 235, 243), ButtonText: Color.FromArgb(20, 72, 120),
        ButtonHover: Color.FromArgb(210, 223, 237), ButtonDown: Color.FromArgb(194, 211, 229),
        Accent: Color.FromArgb(16, 112, 204), AccentHover: Color.FromArgb(36, 132, 224), AccentText: Color.White,
        Primary: Color.FromArgb(84, 138, 24), PrimaryHover: Color.FromArgb(104, 160, 34),
        Track: Color.FromArgb(214, 221, 230), ListHeader: Color.FromArgb(243, 245, 248),
        Ok: Color.FromArgb(52, 122, 26), Warn: Color.FromArgb(168, 92, 0), Error: Color.FromArgb(192, 40, 30), Info: Color.FromArgb(16, 112, 204));

    /// <summary>
    /// Kats: gunmetal grey surfaces with a vivid orange (#FF5C00) for buttons, ticks and selection. Text on the orange is
    /// near-black, which reads far better on it than white.
    /// </summary>
    public static readonly Palette KatsPalette = new(
        KatsName, true,
        Back: Color.FromArgb(31, 38, 42), Surface: Color.FromArgb(42, 52, 57), Header: Color.FromArgb(22, 27, 30),
        Input: Color.FromArgb(26, 32, 36), Border: Color.FromArgb(72, 86, 93),
        Text: Color.FromArgb(226, 230, 232), Muted: Color.FromArgb(150, 160, 166), Title: Color.White,
        Button: Color.FromArgb(58, 70, 76), ButtonText: Color.FromArgb(255, 150, 92),
        ButtonHover: Color.FromArgb(76, 90, 97), ButtonDown: Color.FromArgb(66, 79, 85),
        Accent: Color.FromArgb(255, 92, 0), AccentHover: Color.FromArgb(255, 122, 46), AccentText: Color.FromArgb(24, 20, 18),
        Primary: Color.FromArgb(255, 92, 0), PrimaryHover: Color.FromArgb(255, 122, 46),
        Track: Color.FromArgb(56, 68, 74), ListHeader: Color.FromArgb(36, 45, 49),
        Ok: Color.FromArgb(150, 210, 60), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(240, 110, 100), Info: Color.FromArgb(255, 150, 92))
    {
        AccentDeep = Color.FromArgb(150, 46, 0),
        PrimaryDeep = Color.FromArgb(150, 46, 0),
    };

    /// <summary>
    /// Koneko: near-black surfaces with a bright purple for buttons, ticks and selection. Text on the purple is near-black,
    /// which reads better on it than white.
    /// </summary>
    public static readonly Palette KonekoPalette = new(
        KonekoName, true,
        Back: Color.FromArgb(16, 12, 20), Surface: Color.FromArgb(28, 22, 34), Header: Color.FromArgb(10, 8, 13),
        Input: Color.FromArgb(20, 16, 25), Border: Color.FromArgb(66, 52, 82),
        Text: Color.FromArgb(230, 224, 236), Muted: Color.FromArgb(156, 146, 168), Title: Color.White,
        Button: Color.FromArgb(48, 36, 62), ButtonText: Color.FromArgb(200, 160, 255),
        ButtonHover: Color.FromArgb(66, 50, 86), ButtonDown: Color.FromArgb(58, 44, 76),
        Accent: Color.FromArgb(168, 98, 255), AccentHover: Color.FromArgb(186, 128, 255), AccentText: Color.FromArgb(18, 10, 26),
        Primary: Color.FromArgb(168, 98, 255), PrimaryHover: Color.FromArgb(186, 128, 255),
        Track: Color.FromArgb(44, 34, 56), ListHeader: Color.FromArgb(22, 17, 27),
        Ok: Color.FromArgb(150, 210, 60), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(240, 110, 100), Info: Color.FromArgb(200, 160, 255))
    {
        AccentDeep = Color.FromArgb(92, 36, 168),
        PrimaryDeep = Color.FromArgb(92, 36, 168),
    };

    /// <summary>
    /// Serina: deep purple surfaces with mint green for buttons, ticks and selection, and dark text on the mint.
    /// </summary>
    public static readonly Palette SerinaPalette = new(
        SerinaName, true,
        Back: Color.FromArgb(36, 24, 52), Surface: Color.FromArgb(48, 33, 70), Header: Color.FromArgb(26, 17, 38),
        Input: Color.FromArgb(30, 20, 44), Border: Color.FromArgb(86, 64, 116),
        Text: Color.FromArgb(236, 230, 244), Muted: Color.FromArgb(176, 160, 198), Title: Color.White,
        Button: Color.FromArgb(64, 46, 92), ButtonText: Color.FromArgb(130, 232, 194),
        ButtonHover: Color.FromArgb(82, 60, 116), ButtonDown: Color.FromArgb(72, 52, 104),
        Accent: Color.FromArgb(110, 226, 182), AccentHover: Color.FromArgb(142, 238, 202), AccentText: Color.FromArgb(22, 30, 27),
        Primary: Color.FromArgb(110, 226, 182), PrimaryHover: Color.FromArgb(142, 238, 202),
        Track: Color.FromArgb(62, 44, 88), ListHeader: Color.FromArgb(40, 27, 58),
        Ok: Color.FromArgb(110, 226, 182), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(255, 120, 130), Info: Color.FromArgb(196, 160, 255))
    {
        AccentDeep = Color.FromArgb(40, 150, 112),
        PrimaryDeep = Color.FromArgb(40, 150, 112),
    };

    /// <summary>
    /// Wolfie: deep blue surfaces with a bright green for buttons, ticks and selection, light blue for the other buttons'
    /// text, and dark text on the green.
    /// </summary>
    public static readonly Palette WolfiePalette = new(
        WolfieName, true,
        Back: Color.FromArgb(14, 26, 44), Surface: Color.FromArgb(20, 36, 60), Header: Color.FromArgb(9, 17, 30),
        Input: Color.FromArgb(12, 22, 38), Border: Color.FromArgb(44, 70, 104),
        Text: Color.FromArgb(220, 230, 242), Muted: Color.FromArgb(140, 164, 192), Title: Color.White,
        Button: Color.FromArgb(28, 52, 84), ButtonText: Color.FromArgb(110, 190, 255),
        ButtonHover: Color.FromArgb(40, 70, 110), ButtonDown: Color.FromArgb(34, 62, 98),
        Accent: Color.FromArgb(46, 204, 113), AccentHover: Color.FromArgb(76, 222, 140), AccentText: Color.FromArgb(8, 24, 16),
        Primary: Color.FromArgb(46, 204, 113), PrimaryHover: Color.FromArgb(76, 222, 140),
        Track: Color.FromArgb(30, 50, 78), ListHeader: Color.FromArgb(16, 30, 50),
        Ok: Color.FromArgb(46, 204, 113), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(240, 110, 100), Info: Color.FromArgb(110, 190, 255))
    {
        AccentDeep = Color.FromArgb(18, 128, 66),
        PrimaryDeep = Color.FromArgb(18, 128, 66),
    };

    /// <summary>
    /// Vickra: jewel tones, the reverse of Wolfie. Deep emerald surfaces with a bright sapphire for buttons, ticks and
    /// selection, light sapphire for the other buttons' text, and dark text on the sapphire.
    /// </summary>
    public static readonly Palette VickraPalette = new(
        VickraName, true,
        Back: Color.FromArgb(8, 36, 29), Surface: Color.FromArgb(13, 51, 41), Header: Color.FromArgb(4, 24, 19),
        Input: Color.FromArgb(6, 30, 24), Border: Color.FromArgb(30, 92, 74),
        Text: Color.FromArgb(222, 240, 233), Muted: Color.FromArgb(144, 186, 170), Title: Color.White,
        Button: Color.FromArgb(18, 68, 55), ButtonText: Color.FromArgb(130, 178, 255),
        ButtonHover: Color.FromArgb(26, 88, 71), ButtonDown: Color.FromArgb(22, 78, 63),
        Accent: Color.FromArgb(66, 133, 255), AccentHover: Color.FromArgb(100, 158, 255), AccentText: Color.FromArgb(4, 14, 36),
        Primary: Color.FromArgb(66, 133, 255), PrimaryHover: Color.FromArgb(100, 158, 255),
        Track: Color.FromArgb(22, 64, 52), ListHeader: Color.FromArgb(10, 43, 34),
        Ok: Color.FromArgb(64, 214, 150), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(240, 110, 100), Info: Color.FromArgb(130, 178, 255))
    {
        AccentDeep = Color.FromArgb(20, 66, 170),
        PrimaryDeep = Color.FromArgb(20, 66, 170),
    };

    /// <summary>
    /// Kitomi: black surfaces and white text, a bold red for buttons, ticks and selection with white text on it, and cyan
    /// for the other buttons' text and info notes.
    /// </summary>
    public static readonly Palette KitomiPalette = new(
        KitomiName, true,
        Back: Color.FromArgb(12, 12, 14), Surface: Color.FromArgb(22, 22, 26), Header: Color.FromArgb(5, 5, 7),
        Input: Color.FromArgb(16, 16, 19), Border: Color.FromArgb(58, 58, 66),
        Text: Color.FromArgb(240, 240, 242), Muted: Color.FromArgb(160, 160, 170), Title: Color.White,
        Button: Color.FromArgb(32, 32, 38), ButtonText: Color.FromArgb(0, 214, 240),
        ButtonHover: Color.FromArgb(46, 46, 54), ButtonDown: Color.FromArgb(40, 40, 47),
        Accent: Color.FromArgb(228, 30, 54), AccentHover: Color.FromArgb(244, 62, 84), AccentText: Color.White,
        Primary: Color.FromArgb(228, 30, 54), PrimaryHover: Color.FromArgb(244, 62, 84),
        Track: Color.FromArgb(38, 38, 44), ListHeader: Color.FromArgb(17, 17, 20),
        Ok: Color.FromArgb(80, 220, 130), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(255, 110, 124), Info: Color.FromArgb(0, 214, 240))
    {
        AccentDeep = Color.FromArgb(140, 8, 26),
        PrimaryDeep = Color.FromArgb(140, 8, 26),
    };

    /// <summary>
    /// Szendoo: dim grey (#696969) cards with a hot pink (#FF70B0) for buttons, ticks and selection, and dark text on the
    /// pink. The grey is lighter than the other dark themes, so the other buttons sit darker than the cards and the status
    /// colours are pale enough to read on it.
    /// </summary>
    public static readonly Palette SzendooPalette = new(
        SzendooName, true,
        Back: Color.FromArgb(82, 82, 82), Surface: Color.FromArgb(105, 105, 105), Header: Color.FromArgb(56, 56, 56),
        Input: Color.FromArgb(70, 70, 70), Border: Color.FromArgb(140, 140, 140),
        Text: Color.FromArgb(250, 250, 250), Muted: Color.FromArgb(222, 222, 222), Title: Color.White,
        Button: Color.FromArgb(58, 58, 58), ButtonText: Color.FromArgb(255, 150, 200),
        ButtonHover: Color.FromArgb(72, 72, 72), ButtonDown: Color.FromArgb(66, 66, 66),
        Accent: Color.FromArgb(255, 112, 176), AccentHover: Color.FromArgb(255, 140, 192), AccentText: Color.FromArgb(30, 10, 20),
        Primary: Color.FromArgb(255, 112, 176), PrimaryHover: Color.FromArgb(255, 140, 192),
        Track: Color.FromArgb(80, 80, 80), ListHeader: Color.FromArgb(92, 92, 92),
        Ok: Color.FromArgb(180, 255, 190), Warn: Color.FromArgb(255, 225, 120), Error: Color.FromArgb(255, 180, 160), Info: Color.FromArgb(255, 190, 220))
    {
        AccentDeep = Color.FromArgb(200, 50, 120),
        PrimaryDeep = Color.FromArgb(200, 50, 120),
    };

    /// <summary>
    /// Triphlx: true black surfaces with an electric blue for buttons, ticks and selection, light blue for the other
    /// buttons' text, and dark text on the blue.
    /// </summary>
    public static readonly Palette TriphlxPalette = new(
        TriphlxName, true,
        Back: Color.FromArgb(8, 8, 10), Surface: Color.FromArgb(16, 18, 24), Header: Color.FromArgb(3, 3, 5),
        Input: Color.FromArgb(12, 13, 17), Border: Color.FromArgb(40, 52, 76),
        Text: Color.FromArgb(226, 234, 248), Muted: Color.FromArgb(148, 160, 184), Title: Color.White,
        Button: Color.FromArgb(20, 30, 48), ButtonText: Color.FromArgb(110, 180, 255),
        ButtonHover: Color.FromArgb(28, 42, 66), ButtonDown: Color.FromArgb(24, 36, 58),
        Accent: Color.FromArgb(30, 144, 255), AccentHover: Color.FromArgb(70, 166, 255), AccentText: Color.FromArgb(2, 10, 24),
        Primary: Color.FromArgb(30, 144, 255), PrimaryHover: Color.FromArgb(70, 166, 255),
        Track: Color.FromArgb(26, 32, 44), ListHeader: Color.FromArgb(12, 13, 18),
        Ok: Color.FromArgb(80, 220, 130), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(240, 110, 100), Info: Color.FromArgb(110, 180, 255))
    {
        AccentDeep = Color.FromArgb(10, 80, 180),
        PrimaryDeep = Color.FromArgb(10, 80, 180),
    };

    /// <summary>
    /// Tohma: deep royal blue surfaces with a crimson for buttons, ticks and selection, white text on the crimson, and
    /// light red for the other buttons' text.
    /// </summary>
    public static readonly Palette TohmaPalette = new(
        TohmaName, true,
        Back: Color.FromArgb(16, 30, 72), Surface: Color.FromArgb(24, 42, 96), Header: Color.FromArgb(10, 20, 52),
        Input: Color.FromArgb(14, 26, 62), Border: Color.FromArgb(52, 76, 140),
        Text: Color.FromArgb(232, 238, 255), Muted: Color.FromArgb(160, 176, 220), Title: Color.White,
        Button: Color.FromArgb(34, 56, 120), ButtonText: Color.FromArgb(255, 150, 150),
        ButtonHover: Color.FromArgb(46, 72, 148), ButtonDown: Color.FromArgb(40, 64, 134),
        Accent: Color.FromArgb(210, 36, 52), AccentHover: Color.FromArgb(228, 56, 70), AccentText: Color.White,
        Primary: Color.FromArgb(210, 36, 52), PrimaryHover: Color.FromArgb(228, 56, 70),
        Track: Color.FromArgb(32, 52, 112), ListHeader: Color.FromArgb(20, 36, 84),
        Ok: Color.FromArgb(90, 220, 140), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(255, 120, 110), Info: Color.FromArgb(150, 190, 255))
    {
        AccentDeep = Color.FromArgb(130, 14, 28),
        PrimaryDeep = Color.FromArgb(130, 14, 28),
    };

    /// <summary>
    /// Skywolf: black surfaces with a deep dark purple for buttons, ticks and selection, white text on the purple, and
    /// light purple for the other buttons' text. Unlike Koneko's bright lavender, the purple here is dark.
    /// </summary>
    public static readonly Palette SkywolfPalette = new(
        SkywolfName, true,
        Back: Color.FromArgb(6, 4, 8), Surface: Color.FromArgb(14, 10, 18), Header: Color.FromArgb(0, 0, 0),
        Input: Color.FromArgb(10, 7, 13), Border: Color.FromArgb(52, 30, 70),
        Text: Color.FromArgb(228, 222, 236), Muted: Color.FromArgb(150, 138, 166), Title: Color.White,
        Button: Color.FromArgb(30, 18, 42), ButtonText: Color.FromArgb(190, 140, 240),
        ButtonHover: Color.FromArgb(44, 26, 62), ButtonDown: Color.FromArgb(38, 22, 54),
        Accent: Color.FromArgb(106, 27, 154), AccentHover: Color.FromArgb(128, 44, 180), AccentText: Color.White,
        Primary: Color.FromArgb(106, 27, 154), PrimaryHover: Color.FromArgb(128, 44, 180),
        Track: Color.FromArgb(28, 18, 38), ListHeader: Color.FromArgb(10, 7, 13),
        Ok: Color.FromArgb(120, 220, 120), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(240, 110, 100), Info: Color.FromArgb(190, 140, 240))
    {
        AccentDeep = Color.FromArgb(60, 10, 92),
        PrimaryDeep = Color.FromArgb(60, 10, 92),
    };

    /// <summary>
    /// Hunter: black surfaces with silver for ticks, selection and the other buttons' text, dark text on the silver, and
    /// red kept to small touches: the one main button of each window and the coloured half of the app name.
    /// </summary>
    public static readonly Palette HunterPalette = new(
        HunterName, true,
        Back: Color.FromArgb(8, 8, 9), Surface: Color.FromArgb(17, 17, 19), Header: Color.FromArgb(0, 0, 0),
        Input: Color.FromArgb(12, 12, 14), Border: Color.FromArgb(58, 60, 66),
        Text: Color.FromArgb(226, 228, 232), Muted: Color.FromArgb(150, 153, 160), Title: Color.White,
        Button: Color.FromArgb(30, 31, 35), ButtonText: Color.FromArgb(196, 200, 208),
        ButtonHover: Color.FromArgb(42, 44, 49), ButtonDown: Color.FromArgb(36, 37, 42),
        Accent: Color.FromArgb(192, 196, 204), AccentHover: Color.FromArgb(214, 218, 226), AccentText: Color.FromArgb(12, 12, 14),
        Primary: Color.FromArgb(178, 24, 36), PrimaryHover: Color.FromArgb(204, 36, 48),
        Track: Color.FromArgb(30, 31, 35), ListHeader: Color.FromArgb(12, 12, 14),
        Ok: Color.FromArgb(90, 210, 130), Warn: Color.FromArgb(240, 200, 80), Error: Color.FromArgb(255, 110, 110), Info: Color.FromArgb(206, 212, 224))
    {
        AccentDeep = Color.FromArgb(120, 124, 134),
        PrimaryDeep = Color.FromArgb(110, 10, 20),
        PrimaryText = Color.White,
        Brand = Color.FromArgb(214, 40, 52),
    };

    /// <summary>
    /// Windows High Contrast: every colour comes from the user's contrast theme, so the app reads exactly like the rest
    /// of Windows. Status colours fall back to the text colour; the words (badges, notes) carry the meaning.
    /// </summary>
    static Palette HighContrastPalette() => new(
        HighContrastName, SystemColors.Window.GetBrightness() < 0.5f,
        Back: SystemColors.Window, Surface: SystemColors.Window, Header: SystemColors.Window,
        Input: SystemColors.Window, Border: SystemColors.WindowText,
        Text: SystemColors.WindowText, Muted: SystemColors.WindowText, Title: SystemColors.WindowText,
        Button: SystemColors.ButtonFace, ButtonText: SystemColors.ControlText, ButtonHover: SystemColors.ButtonFace, ButtonDown: SystemColors.ButtonFace,
        Accent: SystemColors.Highlight, AccentHover: SystemColors.Highlight, AccentText: SystemColors.HighlightText,
        Primary: SystemColors.Highlight, PrimaryHover: SystemColors.Highlight,
        Track: SystemColors.GrayText, ListHeader: SystemColors.ButtonFace,
        Ok: SystemColors.WindowText, Warn: SystemColors.WindowText, Error: SystemColors.WindowText, Info: SystemColors.HotTrack);

    /// <summary>Console colours for the log, the same in dark and light (High Contrast uses its own).</summary>
    public static Color LogBack => HighContrast ? SystemColors.Window : Color.FromArgb(15, 22, 31);
    public static Color LogText => HighContrast ? SystemColors.WindowText : Color.FromArgb(198, 212, 223);

    /// <summary>
    /// The brand bar keeps the dark colours in the light theme too, the way Steam's own tools keep their dark top bar,
    /// so the window looks like the same product in either theme. High Contrast uses the contrast colours throughout.
    /// </summary>
    public static Palette Banner => Current == LightPalette ? DarkPalette : Current;

    /// <summary>The palette a control is drawn with: the banner's inside the brand bar, the theme's everywhere else.</summary>
    public static Palette PaletteFor(Control? c)
    {
        for (; c != null; c = c.Parent)
            if (RoleOf(c) == ThemeRole.Header) return Banner;
        return Current;
    }

    public static Palette Current { get; private set; } = DarkPalette;

    /// <summary>What the user picked: "Dark", "Light", "Kats", "Koneko", "Serina", "Wolfie", "Vickra", "Kitomi", "Szendoo", "Triphlx", "Tohma", "Skywolf", "Hunter" or "System". <see cref="Current"/> is what that resolves to now.</summary>
    public static string Choice { get; private set; } = DarkName;

    /// <summary>True while Windows High Contrast is on; it overrides the chosen theme.</summary>
    public static bool HighContrast => Current.Name == HighContrastName;

    /// <summary>
    /// Picks a theme by name ("Dark", "Light" or "System"); anything else is dark, the default. Windows High Contrast,
    /// when on, wins over the choice. Returns true when the colours changed (the caller re-applies them).
    /// </summary>
    public static bool Set(string? name)
    {
        Choice = Display.NormalizeTheme(name);
        var before = Current;
        Current = SystemInformation.HighContrast ? HighContrastPalette()
            : Display.ResolveTheme(Choice, WindowsAppsUseLight()) switch 
            {
                LightName => LightPalette, KatsName => KatsPalette, KonekoName => KonekoPalette,
                SerinaName => SerinaPalette, WolfieName => WolfiePalette,
                VickraName => VickraPalette, KitomiName => KitomiPalette,
                SzendooName => SzendooPalette,
                TriphlxName => TriphlxPalette, TohmaName => TohmaPalette,
                SkywolfName => SkywolfPalette, HunterName => HunterPalette, _ => DarkPalette,
            };
        return Current != before;
    }

    /// <summary>Windows' "Choose your app mode" setting: true for light, false for dark, null when unknown.</summary>
    static bool? WindowsAppsUseLight()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null) is int v
                ? v != 0 : null;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------ text size and type scale

    /// <summary>Options > Appearance > Text size, as a factor (1.25 = 125%). Set before the first window is themed.</summary>
    public static float TextScale { get; set; } = 1f;

    /// <summary>The app's type scale; every window uses these four sizes, so headings and body text match everywhere.</summary>
    public static readonly Font TitleFont = new("Segoe UI Semibold", 14f),
        HeadingFont = new("Segoe UI Semibold", 10.5f),
        BodyFont = new("Segoe UI", 9.5f),
        SmallFont = new("Segoe UI", 8.5f);

    /// <summary>The second half of the app name in the brand bar, lighter than the first.</summary>
    public static readonly Font BrandFont = new("Segoe UI Semilight", 14f);

    /// <summary>Card titles, shown in capitals like Steam's section headings.</summary>
    public static readonly Font SectionFont = new("Segoe UI Semibold", 9.5f);

    /// <summary>The Upload / Options tabs in the brand bar.</summary>
    public static readonly Font NavFont = new("Segoe UI Semibold", 9.75f);

    /// <summary>Mixes <paramref name="amount"/> (0..1) of <paramref name="over"/> into <paramref name="under"/>.</summary>
    public static Color Blend(Color under, Color over, double amount) => Color.FromArgb(
        (int)(under.R + (over.R - under.R) * amount),
        (int)(under.G + (over.G - under.G) * amount),
        (int)(under.B + (over.B - under.B) * amount));

    /// <summary>The app icon (Assets/app.ico, embedded), shown on every window and on the taskbar.</summary>
    public static Icon? AppIcon { get; } = LoadIcon();

    static Icon? LoadIcon()
    {
        try
        {
            using var s = typeof(Theme).Assembly.GetManifestResourceStream("SevenDaysModManager.app.ico");
            return s == null ? null : new Icon(s);
        }
        catch { return null; }
    }

    public static Color Of(Tone t) => t switch
    {
        Tone.Muted => Current.Muted,
        Tone.Ok => Current.Ok,
        Tone.Warn => Current.Warn,
        Tone.Error => Current.Error,
        Tone.Info => Current.Info,
        _ => Current.Text,
    };

    static readonly ConditionalWeakTable<Control, object> Roles = new();

    /// <summary>Marks a control's role for <see cref="Apply"/>; returns it so it can be used inline.</summary>
    public static T Role<T>(this T c, ThemeRole role) where T : Control
    {
        Roles.AddOrUpdate(c, role);
        return c;
    }

    static ThemeRole RoleOf(Control c) => Roles.TryGetValue(c, out var r) ? (ThemeRole)r : ThemeRole.None;

    public static void Apply(Control root)
    {
        Apply(root, root.Parent?.BackColor ?? Current.Back, PaletteFor(root));
    }

    /// <summary>
    /// Every window is laid out in 96-DPI pixels. The first time it is themed, it is scaled to the screen's DPI (150%,
    /// 200%, ...), so fixed widths and heights grow with the text instead of clipping it. Only once: a theme switch
    /// themes the window again.
    /// </summary>
    static void ScaleForDpi(Form f)
    {
        if (f.AutoScaleMode == AutoScaleMode.Dpi) return;
        ScaleText(f);
        f.AutoScaleMode = AutoScaleMode.Dpi;
        // A larger text size scales the layout with it, as if the screen had a higher DPI.
        f.AutoScaleDimensions = new SizeF(96F / TextScale, 96F / TextScale);
        f.PerformAutoScale();
    }

    static readonly ConditionalWeakTable<Font, object> Scaled = new();

    /// <summary>Grows every font in the window by <see cref="TextScale"/>: the window's own and each one set on a control.</summary>
    static void ScaleText(Form f)
    {
        if (Math.Abs(TextScale - 1f) < 0.01f) return;
        // A control that inherits its font returns its parent's; only the ones set on purpose are scaled one by one.
        var own = new List<Control>();
        void Collect(Control c)
        {
            foreach (Control child in c.Controls)
            {
                if (!ReferenceEquals(child.Font, c.Font)) own.Add(child);
                Collect(child);
            }
        }
        Collect(f);
        var fonts = own.Select(c => c.Font).ToList();
        f.Font = Bigger(f.Font);
        for (var i = 0; i < own.Count; i++) own[i].Font = Bigger(fonts[i]);
    }

    /// <summary>The font at the text size; a font that is already scaled (a dialog given the main window's) is kept.</summary>
    static Font Bigger(Font font)
    {
        if (Scaled.TryGetValue(font, out _)) return font;
        var big = new Font(font.FontFamily, font.SizeInPoints * TextScale, font.Style, GraphicsUnit.Point);
        Scaled.AddOrUpdate(big, true);
        return big;
    }

    static void Apply(Control c, Color parentBack, Palette p)
    {
        var role = RoleOf(c);
        if (role == ThemeRole.Header) p = Banner;
        var back = role switch
        {
            ThemeRole.Surface => p.Surface,
            ThemeRole.Header => p.Header,
            ThemeRole.Divider => p.Border,
            ThemeRole.Rule => parentBack,
            _ => parentBack,
        };

        switch (c)
        {
            case Form f:
                ScaleForDpi(f);
                FocusCues.Install();
                back = p.Back;
                f.BackColor = back; f.ForeColor = p.Text;
                if (AppIcon != null) f.Icon = AppIcon;
                Native(f, OnFormHandle);
                break;
            case NavButton nb:
                nb.FlatStyle = FlatStyle.Flat;
                nb.FlatAppearance.BorderSize = 0;
                nb.FlatAppearance.MouseOverBackColor = Blend(back, p.Text, 0.07);
                nb.FlatAppearance.MouseDownBackColor = Blend(back, p.Text, 0.12);
                nb.BackColor = back;
                nb.ForeColor = nb.Selected ? p.Title : p.Muted;
                nb.UseVisualStyleBackColor = false;
                nb.Invalidate();
                Hook(nb, PaintFocus);
                break;
            case SideNavButton sb:
                StyleSideNav(sb, back);
                Hook(sb, PaintFocus);
                break;
            case Button b:
                StyleButton(b);
                b.EnabledChanged -= OnButtonEnabled;
                b.EnabledChanged += OnButtonEnabled;
                Hook(b, PaintButton);
                Hook(b, PaintFocus);
                break;
            case RichTextBox r:
                r.BackColor = role == ThemeRole.Log ? LogBack : p.Input;
                r.ForeColor = role == ThemeRole.Log ? LogText : p.Text;
                Native(r, role == ThemeRole.Log && !HighContrast ? OnDarkScrollHandle : OnScrollHandle);
                break;
            case TextBox t:
                t.BackColor = p.Input; t.ForeColor = p.Text;
                t.BorderStyle = BorderStyle.FixedSingle;
                if (t.Multiline) Native(t, OnScrollHandle);
                break;
            case ComboBox cb:
                // Flat style alone leaves a drop-down list's box white; draw the items ourselves.
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = p.Input; cb.ForeColor = p.Text;
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = cb.Font.Height + 4;
                cb.DrawItem -= DrawComboItem; cb.DrawItem += DrawComboItem;
                cb.Invalidate();
                break;
            case ListView lv:
                lv.BackColor = p.Input; lv.ForeColor = p.Text;
                lv.BorderStyle = BorderStyle.None;
                lv.OwnerDraw = true;
                SmoothPaint(lv);
                lv.DrawColumnHeader -= DrawHeader; lv.DrawColumnHeader += DrawHeader;
                lv.DrawItem -= DrawItemDefault; lv.DrawItem += DrawItemDefault;
                lv.DrawSubItem -= DrawSubItemDefault; lv.DrawSubItem += DrawSubItemDefault;
                Native(lv, OnScrollHandle);
                lv.Invalidate();
                break;
            case LinkLabel ll:
                ll.BackColor = back;
                if (role != ThemeRole.Keep) ll.LinkColor = p.Dark ? p.Info : p.Accent;
                ll.ActiveLinkColor = p.AccentHover;
                ll.VisitedLinkColor = ll.LinkColor;
                break;
            case Label l:
                l.BackColor = back;
                if (role != ThemeRole.Keep)
                    l.ForeColor = role switch { ThemeRole.Muted => p.Muted, ThemeRole.Title => p.Title, ThemeRole.Brand => p.Brand, _ => p.Text };
                break;
            case CheckBox or RadioButton:
                c.BackColor = back; c.ForeColor = p.Text;
                Hook(c, PaintFocus);
                break;
            case FlatProgressBar pb:
                pb.BackColor = p.Track; pb.ForeColor = p.Accent;
                break;
            default:
                c.BackColor = back; c.ForeColor = p.Text;
                if (role == ThemeRole.Surface) Hook(c, PaintCardEdge);
                if (role == ThemeRole.Rule) Hook(c, PaintRule);
                if (role == ThemeRole.Header) Hook(c, PaintBanner);
                if (c is ModDetailsPanel) Native(c, OnScrollHandle); // its scroll bar follows the theme like the list's
                c.Invalidate();
                break;
        }

        foreach (Control child in c.Controls) Apply(child, back, p);
    }

    static readonly System.Reflection.PropertyInfo? DoubleBufferedProp =
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>
    /// Draws the control off-screen first, so scrolling, resizing and picking rows don't flicker (for a list this turns
    /// on Windows' own double-buffered drawing). The property is protected, so it is set from outside this way.
    /// </summary>
    static void SmoothPaint(Control c)
    {
        try { DoubleBufferedProp?.SetValue(c, true); }
        catch { /* cosmetic only */ }
    }

    /// <summary>Adds a paint step once, however often the theme is applied.</summary>
    /// <summary>Colours an Options sidebar section: muted text when not selected, a lit-up row when selected.</summary>
    public static void StyleSideNav(SideNavButton b, Color back)
    {
        var p = PaletteFor(b);
        var lit = HighContrast ? p.Accent : Blend(back, p.Text, p.Dark ? 0.10 : 0.08);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = b.Selected ? lit : Blend(back, p.Text, 0.05);
        b.FlatAppearance.MouseDownBackColor = lit;
        b.BackColor = b.Selected ? lit : back;
        b.ForeColor = b.Selected ? (HighContrast ? p.AccentText : p.Title) : p.Muted;
        b.UseVisualStyleBackColor = false;
        b.Invalidate();
    }

    static void Hook(Control c, PaintEventHandler h)
    {
        c.Paint -= h;
        c.Paint += h;
    }

    /// <summary>A hairline around each card, so cards stay apart on the light theme and in High Contrast.</summary>
    static void PaintCardEdge(object? s, PaintEventArgs e)
    {
        if (s is not Control c) return;
        var p = Current;
        using var pen = new Pen(HighContrast ? p.Border : Blend(p.Surface, p.Border, p.Dark ? 0.55 : 0.8));
        e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
    }

    /// <summary>The line under a card's title: the accent fading out to the right, like Steam's section headings.</summary>
    static void PaintRule(object? s, PaintEventArgs e)
    {
        if (s is not Control c || c.Width <= 0) return;
        var p = PaletteFor(c);
        var back = c.BackColor;
        if (HighContrast) { using var solid = new SolidBrush(p.Border); e.Graphics.FillRectangle(solid, c.ClientRectangle); return; }
        using var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, c.Width, 1),
            Blend(back, p.Accent, p.Dark ? 0.55 : 0.45), back, 0f);
        e.Graphics.FillRectangle(b, c.ClientRectangle);
    }

    /// <summary>The brand bar's bottom edge: a Steam-blue line that fades towards the right.</summary>
    static void PaintBanner(object? s, PaintEventArgs e)
    {
        if (s is not Control c || c.Width <= 0 || c.Height <= 0 || HighContrast) return;
        var p = Banner;
        var g = e.Graphics;
        var h = Math.Max(2, c.LogicalToDeviceUnits(2));
        using var line = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, c.Width, h), p.Accent, Blend(p.Header, p.Accent, 0.25), 0f);
        g.FillRectangle(line, 0, c.Height - h, c.Width, h);
    }

    /// <summary>
    /// A clear ring around the focused button or tick box while the keyboard is in use (Tab, arrows, Alt), so it is
    /// always visible where Tab is. Clicking with the mouse hides it again, as Windows does.
    /// </summary>
    static void PaintFocus(object? s, PaintEventArgs e)
    {
        if (s is not Control c || !c.Focused || !FocusCues.Keyboard || !c.Enabled) return;
        var p = PaletteFor(c);
        var accent = c is Button and not NavButton && RoleOf(c) is ThemeRole.Accent or ThemeRole.Primary;
        var w = Math.Max(2, c.LogicalToDeviceUnits(2));
        using var pen = new Pen(accent ? (RoleOf(c) == ThemeRole.Primary ? p.PrimaryText : p.AccentText) : p.Accent, w) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset };
        var inset = accent ? w : 0;
        e.Graphics.DrawRectangle(pen, inset, inset, c.Width - 1 - inset * 2, c.Height - 1 - inset * 2);
    }

    /// <summary>The colour to outline the keyboard-focused item of a list with.</summary>
    public static Color FocusColor => Current.Accent;

    static void StyleButton(Button b)
    {
        var p = PaletteFor(b);
        var accent = b.Enabled && RoleOf(b) is ThemeRole.Accent or ThemeRole.Primary; // a disabled main button should not look pressable
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.FlatAppearance.BorderSize = 1;
        var primary = accent && RoleOf(b) == ThemeRole.Primary;
        var fill = primary ? p.Primary : p.Accent;
        b.FlatAppearance.BorderColor = accent ? fill : p.Border;
        b.FlatAppearance.MouseOverBackColor = accent ? (primary ? p.PrimaryHover : p.AccentHover) : p.ButtonHover;
        b.FlatAppearance.MouseDownBackColor = accent ? fill : p.ButtonDown;
        b.BackColor = accent ? fill : p.Button;
        b.ForeColor = primary ? p.PrimaryText : accent ? p.AccentText : p.ButtonText;
        b.Invalidate();
    }

    static void OnButtonEnabled(object? s, EventArgs e)
    {
        if (s is Button b) StyleButton(b);
    }

    /// <summary>
    /// Draws every button the way Steam's client does: a softly rounded face, light-blue text on slate for ordinary
    /// buttons, a blue gradient for the suggested next step (Accent) and a green one for the main job (Primary).
    /// Disabled buttons sit back on a flat face in the muted colour, so they read as "not now" but stay legible.
    /// In High Contrast the flat system look is kept and only the disabled text is redrawn.
    /// </summary>
    static void PaintButton(object? s, PaintEventArgs e)
    {
        if (s is not Button b) return;
        if (HighContrast) { PaintDisabled(b, e); return; }
        var p = PaletteFor(b);
        var g = e.Graphics;
        var r = b.ClientRectangle;
        if (r.Width <= 2 || r.Height <= 2) return;
        var role = RoleOf(b);
        var hot = b.Enabled && r.Contains(b.PointToClient(Cursor.Position));
        var down = hot && b.Capture && (Control.MouseButtons & MouseButtons.Left) != 0;

        g.Clear(b.Parent?.BackColor ?? p.Back);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(0, 0, r.Width - 1, r.Height - 1), Math.Max(2, b.LogicalToDeviceUnits(3)));
        Color text;
        if (!b.Enabled)
        {
            using var bg = new SolidBrush(Blend(p.Button, b.Parent?.BackColor ?? p.Back, 0.55));
            g.FillPath(bg, path);
            text = p.Muted;
        }
        else if (role is ThemeRole.Accent or ThemeRole.Primary)
        {
            var baseColor = role == ThemeRole.Primary ? (hot ? p.PrimaryHover : p.Primary) : (hot ? p.AccentHover : p.Accent);
            var deep = role == ThemeRole.Primary ? p.PrimaryDeep : p.AccentDeep;
            var top = down ? Blend(baseColor, deep, 0.35) : Blend(baseColor, Color.White, 0.08);
            var bottom = Blend(baseColor, deep, down ? 0.55 : 0.45);
            using var bg = new System.Drawing.Drawing2D.LinearGradientBrush(r, top, bottom, 90f);
            g.FillPath(bg, path);
            text = role == ThemeRole.Primary ? p.PrimaryText : p.AccentText;
        }
        else
        {
            using var bg = new SolidBrush(down ? p.ButtonDown : hot ? p.ButtonHover : p.Button);
            g.FillPath(bg, path);
            if (!p.Dark)
            {
                using var pen = new Pen(hot ? Blend(p.Border, p.Accent, 0.5) : p.Border);
                g.DrawPath(pen, path);
            }
            text = hot && p.Dark ? Color.White : p.ButtonText;
        }
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
            | (FocusCues.Keyboard ? 0 : TextFormatFlags.HidePrefix)
            | (b.TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        var inner = Rectangle.FromLTRB(r.Left + b.Padding.Left + 2, r.Top, r.Right - b.Padding.Right - 2, r.Bottom);
        if (inner.Width < 8) inner = r;
        TextRenderer.DrawText(g, b.Text, b.Font, inner, text, flags);
    }

    /// <summary>A rectangle with rounded corners of <paramref name="radius"/> pixels.</summary>
    public static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 1) { path.AddRectangle(r); return path; }
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Windows draws a disabled flat button's text in a fixed grey that almost vanishes on dark colours. Paint it
    /// again in the muted colour on a slightly sunken face, so it reads as "not now" but stays legible.
    /// </summary>
    static void PaintDisabled(Button b, PaintEventArgs e)
    {
        if (b.Enabled) return;
        var p = Current;
        var r = b.ClientRectangle;
        using (var bg = new SolidBrush(Blend(p.Button, p.Surface, 0.5))) e.Graphics.FillRectangle(bg, r);
        using (var pen = new Pen(p.Border)) e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
        TextRenderer.DrawText(e.Graphics, b.Text, b.Font, r, SystemColors.GrayText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.HidePrefix);
    }

    /// <summary>Right-click menus in the theme's colours.</summary>
    public static void StyleMenu(ToolStrip menu)
    {
        PaintMenu(menu);
    }

    static void PaintMenu(ToolStrip menu)
    {
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(Current)) { RoundedEdges = false };
        menu.BackColor = Current.Surface;
        menu.ForeColor = Current.Text;
        foreach (ToolStripItem i in menu.Items)
        {
            i.ForeColor = Current.Text;
            // Submenus are menus of their own, so they get the same colours.
            if (i is ToolStripDropDownItem { HasDropDownItems: true } sub) PaintMenu(sub.DropDown);
        }
    }

    sealed class MenuColors(Palette p) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => p.Surface;
        public override Color ImageMarginGradientBegin => p.Surface;
        public override Color ImageMarginGradientMiddle => p.Surface;
        public override Color ImageMarginGradientEnd => p.Surface;
        public override Color MenuBorder => p.Border;
        public override Color MenuItemBorder => p.ButtonHover;
        public override Color MenuItemSelected => p.ButtonHover;
        public override Color SeparatorDark => p.Border;
        public override Color SeparatorLight => p.Surface;
    }

    /// <summary>Tooltips are drawn in the theme's colours.</summary>
    public static void Style(ToolTip tip)
    {
        // Windows hides a tip after 5 seconds, too soon to read the longer ones; it now stays while the mouse rests.
        tip.AutoPopDelay = 30000;
        tip.OwnerDraw = true;
        tip.Draw -= DrawTip;
        tip.Draw += DrawTip;
    }

    static void DrawTip(object? s, DrawToolTipEventArgs e)
    {
        var p = Current;
        using (var bg = new SolidBrush(p.Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
        using (var pen = new Pen(p.Border)) e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, p.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    static void DrawComboItem(object? s, DrawItemEventArgs e)
    {
        if (s is not ComboBox cb) return;
        var p = PaletteFor(cb);
        var highlighted = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var bg = new SolidBrush(highlighted ? p.Accent : p.Input)) e.Graphics.FillRectangle(bg, e.Bounds);
        var text = e.Index >= 0 ? cb.GetItemText(cb.Items[e.Index]) : "";
        var color = highlighted ? p.AccentText : cb.Enabled ? p.Text : HighContrast ? SystemColors.GrayText : p.Muted;
        TextRenderer.DrawText(e.Graphics, text, cb.Font, Rectangle.Inflate(e.Bounds, -3, 0), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // ------------------------------------------------------------ list view

    static void DrawHeader(object? s, DrawListViewColumnHeaderEventArgs e)
    {
        var p = Current;
        var font = (s as Control)?.Font ?? SystemFonts.DefaultFont;
        using (var bg = new SolidBrush(p.ListHeader)) e.Graphics.FillRectangle(bg, e.Bounds);
        using (var pen = new Pen(p.Border))
        {
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 5, e.Bounds.Right - 1, e.Bounds.Bottom - 6);
        }
        var align = e.Header?.TextAlign switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, font, Rectangle.Inflate(e.Bounds, -6, 0), p.Muted,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    static readonly ConditionalWeakTable<ListView, Func<DrawListViewSubItemEventArgs, bool>> Painters = new();

    /// <summary>Lets a list draw its own cells; <paramref name="paint"/> returns false to leave a cell to Windows.</summary>
    public static void PaintCells(this ListView lv, Func<DrawListViewSubItemEventArgs, bool> paint) => Painters.AddOrUpdate(lv, paint);

    static void DrawItemDefault(object? s, DrawListViewItemEventArgs e) =>
        e.DrawDefault = !(s is ListView lv && Painters.TryGetValue(lv, out _));

    static void DrawSubItemDefault(object? s, DrawListViewSubItemEventArgs e) =>
        e.DrawDefault = !(s is ListView lv && Painters.TryGetValue(lv, out var paint) && paint(e));

    // ------------------------------------------------------------ native: dark title bar and scroll bars

    /// <summary>Runs a native styling step now and every time the control's window is (re)created.</summary>
    static void Native(Control c, EventHandler h)
    {
        c.HandleCreated -= h;
        c.HandleCreated += h;
        if (c.IsHandleCreated) h(c, EventArgs.Empty);
    }

    static void OnFormHandle(object? s, EventArgs e)
    {
        if (s is not Form f || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        // A window with the brand bar gets a caption in the bar's colours, so the two read as one piece.
        var p = HasBanner(f) ? Banner : Current;
        int on = p.Dark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; Windows 10 before 20H1 used 19.
        if (DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(f.Handle, 19, ref on, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Windows 11 can colour the caption itself: match the app's header bar so the window reads as one piece.
            int caption = ColorRef(p.Header), text = ColorRef(p.Text), border = ColorRef(p.Border);
            DwmSetWindowAttribute(f.Handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(f.Handle, 36, ref text, sizeof(int));    // DWMWA_TEXT_COLOR
            DwmSetWindowAttribute(f.Handle, 34, ref border, sizeof(int));  // DWMWA_BORDER_COLOR
        }
        // A window that is already on screen keeps its old caption until the frame is redrawn (a live theme switch).
        const uint NoSize = 0x1, NoMove = 0x2, NoZOrder = 0x4, NoActivate = 0x10, FrameChanged = 0x20;
        SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, NoSize | NoMove | NoZOrder | NoActivate | FrameChanged);
    }

    static bool HasBanner(Control c)
    {
        foreach (Control child in c.Controls)
            if (RoleOf(child) == ThemeRole.Header || HasBanner(child)) return true;
        return false;
    }

    static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    static void OnScrollHandle(object? s, EventArgs e) => SetScrollTheme(s as Control, Current.Dark);
    static void OnDarkScrollHandle(object? s, EventArgs e) => SetScrollTheme(s as Control, true);

    static void SetScrollTheme(Control? c, bool dark)
    {
        if (c == null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try { SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); }
        catch { /* cosmetic only */ }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
}

/// <summary>A tab-like button for the top navigation; the selected one gets an accent underline.</summary>
public sealed class NavButton : Button
{
    bool _selected;

    public NavButton()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 8, 16, 10);
        Font = Theme.NavFont;
        Margin = new Padding(0);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            ForeColor = value ? Theme.PaletteFor(this).Title : Theme.PaletteFor(this).Muted;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_selected) return;
        using var b = new SolidBrush(Theme.PaletteFor(this).Accent);
        var h = Math.Max(3, LogicalToDeviceUnits(3));
        e.Graphics.FillRectangle(b, LogicalToDeviceUnits(10), Height - h, Width - LogicalToDeviceUnits(20), h);
    }
}

/// <summary>
/// A section in the Options sidebar, like the list down the side of Steam's settings window: the selected one is
/// lit up with an accent bar on its left. Up and Down move to the section above or below.
/// </summary>
public sealed class SideNavButton : Button
{
    bool _selected;

    public SideNavButton()
    {
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(14, 0, 8, 0);
        Margin = new Padding(0, 0, 0, 2);
        Height = 32;
        TabStop = true;
        Cursor = Cursors.Hand;
        UseMnemonic = false;
        AccessibleRole = AccessibleRole.PageTab;
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            AccessibleDescription = value ? "Selected" : null;
            if (Parent != null) Theme.StyleSideNav(this, Parent.BackColor);
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Down && Parent != null)
        {
            var all = Parent.Controls.OfType<SideNavButton>().Where(b => b.Visible).ToList();
            var i = all.IndexOf(this) + (e.KeyCode == Keys.Down ? 1 : -1);
            if (i >= 0 && i < all.Count) { all[i].Focus(); all[i].PerformClick(); }
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_selected) return;
        using var b = new SolidBrush(Theme.PaletteFor(this).Accent);
        e.Graphics.FillRectangle(b, 0, 0, Math.Max(3, LogicalToDeviceUnits(3)), Height);
    }
}

/// <summary>A flat progress bar that follows the theme (the native one ignores colours).</summary>
public sealed class FlatProgressBar : Control
{
    int _value;

    public FlatProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Height = 8;
        AccessibleRole = AccessibleRole.ProgressBar;
    }

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            AccessibleDescription = $"{v * 100 / Math.Max(1, Maximum - Minimum)}%";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var span = Math.Max(1, Maximum - Minimum);
        var w = (int)((long)Width * (_value - Minimum) / span);
        if (w <= 0) return;
        using var b = new SolidBrush(ForeColor);
        e.Graphics.FillRectangle(b, 0, 0, w, Height);
    }
}

/// <summary>
/// Tracks whether the keyboard or the mouse was used last, so focus rings show while tabbing and hide after a click.
/// Windows does the same for its own controls; the flat themed ones need to be told.
/// </summary>
static class FocusCues
{
    static bool _installed;
    public static bool Keyboard { get; private set; }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Application.AddMessageFilter(new FocusCuesFilter());
    }

    sealed class FocusCuesFilter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            const int KeyDown = 0x100, SysKeyDown = 0x104, LButtonDown = 0x201, RButtonDown = 0x204, MButtonDown = 0x207;
            bool? keyboard = m.Msg switch
            {
                KeyDown or SysKeyDown when Navigates((Keys)(int)m.WParam) => true,
                LButtonDown or RButtonDown or MButtonDown => false,
                _ => null,
            };
            if (keyboard is bool k && k != Keyboard)
            {
                Keyboard = k;
                // The control that has focus now repaints with or without its ring.
                Form.ActiveForm?.ActiveControl?.Invalidate();
                if (Form.ActiveForm?.ActiveControl is ContainerControl cc) cc.ActiveControl?.Invalidate();
            }
            return false;
        }

        static bool Navigates(Keys k) => k is Keys.Tab or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Menu or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;
    }

}
