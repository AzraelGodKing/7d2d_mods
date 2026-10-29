using System;
using UnityEngine;

// Player side, on screen: the blood moon countdown line (U01), the warning banner (U02),
// the red edge pulse at horde start (U03) and the settings window (U12). Drawn with Unity's
// immediate-mode GUI, so it needs no XUi files and works next to any UI mod.
public class BmsHud : MonoBehaviour
{
    public static BmsHud Instance;

    // Countdown line, worked out twice a second rather than every frame.
    string hudText; Color hudColour;
    float hudTimer;

    // Banner
    string bannerTitle, bannerDetail;
    float bannerAt = -999f;
    const float BannerSeconds = 6f;

    // Pulse
    float pulseAt = -999f;
    const float PulseSeconds = 3f;
    Texture2D vignette;

    // Settings window
    bool windowOpen, openSoon;
    Rect windowRect = new Rect(0, 0, 420, 560);
    Vector2 scroll;
    string saveNote = "";
    KeyCode key = KeyCode.None;
    string keyFor;

    GUIStyle hudStyle, shadowStyle, titleStyle, titleShadow, detailStyle, detailShadow;

    void Awake()
    {
        Instance = this;
        BmsPlayer.OnBanner += ShowBanner;
        BmsPlayer.OnHordeStart += StartPulse;
    }

    void OnDestroy()
    {
        BmsPlayer.OnBanner -= ShowBanner;
        BmsPlayer.OnHordeStart -= StartPulse;
        if (windowOpen) CloseWindow();
        if (Instance == this) Instance = null;
    }

    void ShowBanner(string title, string detail)
    {
        if (!BmsSettings.Banner) return;
        bannerTitle = title; bannerDetail = detail; bannerAt = Time.realtimeSinceStartup;
    }

    void StartPulse()
    {
        if (BmsSettings.Pulse) pulseAt = Time.realtimeSinceStartup;
    }

    public void TestBanner()
    {
        bannerTitle = "BLOOD MOON IN 1 HOUR"; bannerDetail = "This is a test of the Blood Moon Sound banner.";
        bannerAt = Time.realtimeSinceStartup;
        StartPulse();
    }

    // From the console: the console window is still open on this frame, so wait for it to close.
    public void OpenSettingsSoon() => openSoon = true;

    static LocalPlayerUI UI => LocalPlayerUI.GetUIForPrimaryPlayer();

    static bool InWorld(out EntityPlayerLocal player)
    {
        var world = GameManager.Instance != null ? GameManager.Instance.World : null;
        player = world != null ? world.GetPrimaryPlayer() : null;
        return player != null;
    }

    void Update()
    {
        try
        {
            if (!InWorld(out _)) { if (windowOpen) CloseWindow(); hudText = null; return; }

            // Settings key (not while typing in chat or the console).
            if (keyFor != BmsSettings.SettingsKey)
            {
                keyFor = BmsSettings.SettingsKey;
                key = Enum.TryParse<KeyCode>(keyFor ?? "", true, out var k) ? k : KeyCode.None;
            }
            var ui = UI;
            bool typing = ui != null && ui.windowManager != null && ui.windowManager.IsInputActive();
            if (key != KeyCode.None && !typing && Input.GetKeyDown(key))
            {
                if (windowOpen) CloseWindow();
                else if (!GameManager.Instance.isAnyCursorWindowOpen()) OpenWindow();
            }
            if (openSoon && !GameManager.Instance.isAnyCursorWindowOpen()) { openSoon = false; OpenWindow(); }
            if (windowOpen && Input.GetKeyDown(KeyCode.Escape)) CloseWindow();

            hudTimer += Time.unscaledDeltaTime;
            if (hudTimer >= 0.5f) { hudTimer = 0f; UpdateHudText(); }
        }
        catch (Exception e) { Log.Warning("[BloodMoonSound] hud: " + e.Message); }
    }

    void UpdateHudText()
    {
        hudText = null;
        if (!BmsSettings.Hud) return;
        int bmDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
        if (bmDay <= 0) return;
        ulong t = BmdTime.Now();
        var dd = BmdTime.DuskDawn();
        int day = GameUtils.WorldTimeToDays(t);
        if (GameUtils.IsBloodMoonTime(t, dd, bmDay))
        {
            hudText = "BLOOD MOON · dawn in " + Short(BmdTime.Until(BmdTime.HordeEnd(bmDay), t));
            hudColour = new Color(1f, 0.25f, 0.25f);
            return;
        }
        ulong start = BmdTime.HordeStart(bmDay);
        if (t >= start) return;
        if (BmsSettings.HudFromDays > 0 && bmDay - day > BmsSettings.HudFromDays) return;
        ulong left = start - t;
        hudText = "Blood Moon in " + Short(left);
        hudColour = left <= 1000UL ? new Color(1f, 0.25f, 0.25f)            // last in-game hour: red
                  : day == bmDay ? new Color(1f, 0.6f, 0.2f)                 // blood moon day: orange
                  : new Color(0.95f, 0.88f, 0.85f);
    }

    // "1d 6h", "6h 30m", "45m"
    static string Short(ulong span)
    {
        long minutes = (long)(span * 60UL / 1000UL);
        long d = minutes / 1440, h = minutes % 1440 / 60, m = minutes % 60;
        if (d > 0) return h > 0 ? $"{d}d {h}h" : $"{d}d";
        if (h > 0) return m > 0 ? $"{h}h {m}m" : $"{h}h";
        return Math.Max(1, m) + "m";
    }

    void Styles()
    {
        if (hudStyle != null) return;
        hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        shadowStyle = new GUIStyle(hudStyle);
        titleStyle = new GUIStyle(hudStyle);
        titleShadow = new GUIStyle(hudStyle);
        detailStyle = new GUIStyle(hudStyle) { fontStyle = FontStyle.Normal };
        detailShadow = new GUIStyle(detailStyle);
        shadowStyle.normal.textColor = titleShadow.normal.textColor = detailShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
    }

    static void Shadowed(Rect r, string text, GUIStyle style, GUIStyle shadow, Color colour)
    {
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, shadow);
        style.normal.textColor = colour;
        GUI.Label(r, text, style);
    }

    void OnGUI()
    {
        try
        {
            if (!InWorld(out _)) return;
            Styles();
            var ui = UI;
            bool hudVisible = ui == null || ui.windowManager == null || ui.windowManager.IsHUDEnabled();
            bool menuOpen = GameManager.Instance.isAnyCursorWindowOpen();
            float scale = Screen.height / 1080f;

            // Red pulse: behind everything else we draw.
            float since = Time.realtimeSinceStartup - pulseAt;
            if (since < PulseSeconds)
            {
                if (vignette == null) vignette = MakeVignette();
                // Quick rise, slow fade, with a second smaller beat.
                float a = since < 0.25f ? since / 0.25f : Mathf.Exp(-(since - 0.25f) * 1.6f);
                a += 0.5f * Mathf.Max(0f, Mathf.Sin((since - 0.45f) * Mathf.PI / 0.35f)) * (since > 0.45f && since < 0.8f ? 1f : 0f);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a) * BmsSettings.PulseStrength);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);
                GUI.color = old;
            }

            // Countdown line.
            if (hudText != null && hudVisible && !menuOpen && !windowOpen)
            {
                int size = Mathf.RoundToInt(BmsSettings.HudSize * scale);
                hudStyle.fontSize = shadowStyle.fontSize = Mathf.Max(8, size);
                float w = 600 * scale, h = size * 2f;
                var r = new Rect(BmsSettings.HudX * Screen.width - w / 2f, BmsSettings.HudY * Screen.height - h / 2f, w, h);
                Shadowed(r, hudText, hudStyle, shadowStyle, hudColour);
            }

            // Banner: fades in and out over a few seconds.
            float b = Time.realtimeSinceStartup - bannerAt;
            if (b < BannerSeconds && bannerTitle != null && !windowOpen)
            {
                float alpha = Mathf.Clamp01(Mathf.Min(b / 0.4f, (BannerSeconds - b) / 1.2f));
                int ts = Mathf.RoundToInt(56 * scale), ds = Mathf.RoundToInt(24 * scale);
                titleStyle.fontSize = titleShadow.fontSize = Mathf.Max(12, ts);
                detailStyle.fontSize = detailShadow.fontSize = Mathf.Max(10, ds);
                float y = Screen.height * 0.24f;
                var oldColour = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Shadowed(new Rect(0, y, Screen.width, ts * 1.6f), bannerTitle, titleStyle, titleShadow, new Color(0.9f, 0.1f, 0.1f));
                if (!string.IsNullOrEmpty(bannerDetail))
                    Shadowed(new Rect(0, y + ts * 1.5f, Screen.width, ds * 1.8f), bannerDetail, detailStyle, detailShadow, new Color(1f, 0.85f, 0.8f));
                GUI.color = oldColour;
            }

            if (windowOpen)
                windowRect = GUI.Window(0x42_4D_53, windowRect, DrawWindow, "Blood Moon Sound settings");
        }
        catch (Exception e) { Log.Warning("[BloodMoonSound] hud draw: " + e.Message); }
    }

    static Texture2D MakeVignette()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;     // 0 centre, 1 corner
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.95f, d));
                px[y * n + x] = new Color(0.75f, 0f, 0f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ---- settings window -------------------------------------------------------------------

    void OpenWindow()
    {
        windowOpen = true;
        saveNote = "";
        float w = Mathf.Min(460f, Screen.width - 40f), h = Mathf.Min(620f, Screen.height - 40f);
        windowRect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GameManager.Instance.SetCursorEnabledOverride(true, true);
        if (InWorld(out var p)) p.SetControllable(false);
    }

    void CloseWindow()
    {
        windowOpen = false;
        try
        {
            GameManager.Instance?.SetCursorEnabledOverride(false, false);
            if (InWorld(out var p)) p.SetControllable(true);
        }
        catch (Exception e) { Log.Warning("[BloodMoonSound] closing settings: " + e.Message); }
    }

    void DrawWindow(int id)
    {
        var bm = BmsPlayer.Instance;
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("<b>When the blood moon clip plays</b>");
        GUILayout.BeginHorizontal();
        foreach (BmsSettings.Trigger tr in Enum.GetValues(typeof(BmsSettings.Trigger)))
            if (GUILayout.Toggle(BmsSettings.PlayAt == tr, tr == BmsSettings.Trigger.Horde ? " Horde" : tr == BmsSettings.Trigger.Warning ? " Warning" : " Both", "Button"))
                BmsSettings.PlayAt = tr;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Warning hour: {BmsSettings.WarningHour:00}:00", GUILayout.Width(170));
        if (GUILayout.Button("-", GUILayout.Width(36))) BmsSettings.WarningHour = (BmsSettings.WarningHour + 23) % 24;
        if (GUILayout.Button("+", GUILayout.Width(36))) BmsSettings.WarningHour = (BmsSettings.WarningHour + 1) % 24;
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("<b>Volume</b>");
        BmsSettings.Volume = Slider("Overall", BmsSettings.Volume);
        BmsSettings.BloodMoonVolume = Clip("Blood moon", null, BmsSettings.BloodMoonVolume, bm?.BloodMoon, "blood moon");
        bool on;
        on = BmsSettings.Spawn; BmsSettings.SpawnVolume = Clip("Spawn", v => on = v, BmsSettings.SpawnVolume, bm?.Spawn, "spawn", on); BmsSettings.Spawn = on;
        on = BmsSettings.Morning; BmsSettings.MorningVolume = Clip("Morning", v => on = v, BmsSettings.MorningVolume, bm?.Morning, "morning", on); BmsSettings.Morning = on;
        on = BmsSettings.Night; BmsSettings.NightVolume = Clip("Night", v => on = v, BmsSettings.NightVolume, bm?.Night, "night", on); BmsSettings.Night = on;
        on = BmsSettings.Survived; BmsSettings.SurvivedVolume = Clip("Survived", v => on = v, BmsSettings.SurvivedVolume, bm?.Survived, "survived", on); BmsSettings.Survived = on;

        GUILayout.Space(8);
        GUILayout.Label("<b>On screen</b>");
        BmsSettings.Hud = GUILayout.Toggle(BmsSettings.Hud, " Blood moon countdown");
        BmsSettings.Banner = GUILayout.Toggle(BmsSettings.Banner, " Warning banner");
        GUILayout.BeginHorizontal();
        BmsSettings.Pulse = GUILayout.Toggle(BmsSettings.Pulse, " Red pulse at horde start", GUILayout.Width(220));
        if (GUILayout.Button("Test banner")) TestBanner();
        GUILayout.EndHorizontal();
        GUILayout.Label("<size=11>Position, size and the key for this window: settings.txt (HudX, HudY, HudSize, SettingsKey).</size>");

        GUILayout.EndScrollView();

        if (saveNote.Length > 0) GUILayout.Label(saveNote);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
        {
            saveNote = BmsSettings.Save(out var err) ? "Saved to settings.txt." : "Couldn't save: " + err;
            hudTimer = 1f; // redraw the countdown with the new setting
        }
        if (GUILayout.Button("Undo changes")) { BmsSettings.Load(); saveNote = "Back to what settings.txt says."; }
        if (GUILayout.Button("Close")) CloseWindow();
        GUILayout.EndHorizontal();
        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }

    static float Slider(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(110));
        value = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.ExpandWidth(true));
        GUILayout.Label(Mathf.RoundToInt(value * 100) + "%", GUILayout.Width(44));
        GUILayout.EndHorizontal();
        return Mathf.Round(value * 100f) / 100f;
    }

    // One clip row: on/off (when it can be turned off), volume and a Test button.
    float Clip(string label, Action<bool> setOn, float volume, BmsPlayer.LoadedClip slot, string playLabel, bool on = true)
    {
        GUILayout.BeginHorizontal();
        if (setOn != null) setOn(GUILayout.Toggle(on, "", GUILayout.Width(18)));
        else GUILayout.Space(22);
        GUILayout.Label(label, GUILayout.Width(88));
        volume = GUILayout.HorizontalSlider(volume, 0f, 1f, GUILayout.ExpandWidth(true));
        GUILayout.Label(Mathf.RoundToInt(volume * 100) + "%", GUILayout.Width(44));
        GUI.enabled = slot != null && slot.Ready;
        if (GUILayout.Button(slot != null && slot.Ready ? "Test" : "No file", GUILayout.Width(64))) BmsPlayer.Instance?.Play(slot, playLabel);
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        return Mathf.Round(volume * 100f) / 100f;
    }
}
