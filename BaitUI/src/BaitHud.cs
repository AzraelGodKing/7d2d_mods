using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Draws the name tags, party list and fed-progress tags (IMGUI), handles the roster hotkey and the "hello" to the server.
public class BaitHud : MonoBehaviour
{
    float nextHello;
    int helloTries;
    GUIStyle label, small;
    float styleScale;
    static float Sc { get { return UiSettings.Scale * Screen.height / 1080f; } }

    float nextNav;

    // Thralls are not hostile to their players: take the enemy marker (compass and map) off every thrall the server told us about.
    void ClearThrallMarks()
    {
        if (Time.time < nextNav || BaitData.All.Count == 0 || !NavObjectManager.HasInstance) return;
        nextNav = Time.time + 1f;
        foreach (int id in BaitData.All)
        {
            var e = BaitData.Live(id);
            if (e == null || e.NavObject == null) continue;
            NavObjectManager.Instance.UnRegisterNavObjectByEntityID(id);
            e.NavObject = null;
        }
    }

    void Update()
    {
        try
        {
            var p = BaitData.Me;
            if (p == null)
            {
                if (BaitData.HasData || helloTries > 0) { BaitData.Reset(); helloTries = 0; }
                return;
            }

            ClearThrallMarks();

            // Tell the server mod we are here (only on servers that run Bait, and only a few tries).
            if (!BaitData.HasData && helloTries < 3 && Time.time > nextHello && BaitData.ServerHasBait())
            {
                helloTries++;
                nextHello = Time.time + 12f;
                BaitData.Cmd("/thrall hello", false);
            }

            // old fed tags
            if (BaitData.Feeds.Count > 0)
            {
                List<int> gone = null;
                foreach (var kv in BaitData.Feeds)
                    if (Time.time - kv.Value.At > 90f || BaitData.Live(kv.Key) == null) { if (gone == null) gone = new List<int>(); gone.Add(kv.Key); }
                if (gone != null) foreach (int id in gone) BaitData.Feeds.Remove(id);
            }

            var ui = LocalPlayerUI.GetUIForPrimaryPlayer();
            if (ui == null || ui.windowManager == null) return;
            var wm = ui.windowManager;
            if (UiSettings.Key == KeyCode.None || !Input.GetKeyDown(UiSettings.Key)) return;
            if (wm.IsInputActive() || wm.IsModalWindowOpen() || wm.IsCursorWindowOpen()) return;
            if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
            if (p.IsDead()) return;
            wm.Open("azraelBaitRoster", true);
        }
        catch (Exception e) { Log.Warning("[BaitUI] update: " + e.Message); }
    }

    bool HudVisible(out Camera cam, out EntityPlayerLocal p)
    {
        cam = null; p = BaitData.Me;
        if (p == null || p.IsDead()) return false;
        var ui = LocalPlayerUI.GetUIForPrimaryPlayer();
        if (ui == null || ui.windowManager == null) return false;
        var wm = ui.windowManager;
        if (!wm.IsHUDEnabled() || wm.IsFullHUDDisabled()) return false;
        cam = p.playerCamera;
        return cam != null;
    }

    void OnGUI()
    {
        try
        {
            Camera cam; EntityPlayerLocal p;
            bool hud = HudVisible(out cam, out p);
            bool edit = false;   // a cursor window (inventory, roster...) is open: the party list can be dragged
            if (p != null && !p.IsDead())
            {
                var ui = LocalPlayerUI.GetUIForPrimaryPlayer();
                if (ui != null && ui.windowManager != null && !ui.windowManager.IsFullHUDDisabled())
                    edit = ui.windowManager.IsCursorWindowOpen() || ui.windowManager.IsModalWindowOpen();
            }
            if (Event.current.type != EventType.Repaint) { if (edit && UiSettings.Party) DragParty(); return; }
            if (!hud && !(edit && UiSettings.Party)) return;
            if (label == null || Mathf.Abs(styleScale - Sc) > 0.01f)
            {
                styleScale = Sc;
                label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13 * Sc), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
                small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(14 * Sc), alignment = TextAnchor.MiddleLeft };
            }

            if (UiSettings.Nameplates || UiSettings.Party)
            {
                int row = 0;
                foreach (var t in BaitData.Thralls)
                {
                    var e = BaitData.LiveThrall(t);
                    if (hud && UiSettings.Nameplates && e != null && t.Active) Plate(cam, p, e, Title(t), HpFrac(e), t.Mode == 3 ? InvColor : UiSettings.BarColorFor(t, HpColor(HpFrac(e))), t.Mode == 3 ? (t.Stay ? "invulnerable, hold" : "invulnerable") : (t.Stay ? "hold" : null), 45f, UiSettings.NameColorFor(t));
                    if (UiSettings.Party && t.Active)
                    {
                        float s = Sc, y = PartyRect.y + 22 * s + row * 36 * s;
                        if (row == 0 && edit)
                        {
                            var pr = PartyRect;
                            var old = GUI.color;
                            GUI.color = new Color(0, 0, 0, dragging ? 0.55f : 0.35f);
                            GUI.DrawTexture(pr, Texture2D.whiteTexture);
                            GUI.color = old;
                            Shadow(new Rect(pr.x + 4, pr.y + 1, pr.width, 20 * s), "drag to move", small);
                        }
                        float f = e != null ? HpFrac(e) : 0f;
                        Shadow(new Rect(PartyRect.x + 4, y, 320 * s, 20 * s), (t.Tether ? "[T] " : "") + (t.Mode == 3 ? "[Inv] " : "") + Title(t) + (e == null ? " (away)" : ""), small, UiSettings.NameColorFor(t));
                        Bar(new Rect(PartyRect.x + 4, y + 21 * s, 220 * s, 7 * s), f, t.Mode == 3 ? InvColor : UiSettings.BarColorFor(t, HpColor(f)));
                        row++;
                    }
                }
            }
            if (hud && UiSettings.Nameplates && UiSettings.PartyBars && p.Party != null)
            {
                foreach (var kv in BaitData.Owner)
                {
                    if (kv.Value == p.entityId || !p.Party.ContainsMember(kv.Value)) continue;
                    bool mine = false;
                    foreach (var t in BaitData.Thralls) if (t.EntityId == kv.Key) { mine = true; break; }
                    if (mine) continue;
                    var e = BaitData.Live(kv.Key);
                    if (e == null) continue;
                    string cn = e.EntityClass != null ? Localization.Get(EntityClass.list[e.entityClass].entityClassName) : "Thrall";
                    var ow = e.world != null ? e.world.GetEntity(kv.Value) as EntityPlayer : null;
                    string label = (ow != null ? ow.PlayerDisplayName + "'s " : "") + cn;
                    Plate(cam, p, e, label, HpFrac(e), UiSettings.BarColor(HpFrac(e), HpColor(HpFrac(e))), null, 45f);
                }
            }
            if (hud && UiSettings.Feed)
            {
                foreach (var kv in BaitData.Feeds)
                {
                    var e = BaitData.Live(kv.Key);
                    if (e == null) continue;
                    bool mine = false;
                    foreach (var t in BaitData.Thralls) if (t.EntityId == kv.Key) { mine = true; break; }
                    if (mine) continue;
                    float f = kv.Value.Need > 0 ? Mathf.Clamp01((float)kv.Value.Cur / kv.Value.Need) : 0f;
                    Plate(cam, p, e, "Fed " + kv.Value.Cur + "/" + kv.Value.Need, f, new Color(0.95f, 0.75f, 0.2f), null, 30f);
                }
            }
        }
        catch (Exception e) { Log.Warning("[BaitUI] draw: " + e.Message); }
    }

    // The party list box: top-left corner is saved as a fraction of the screen so it survives resolution changes.
    static Rect PartyRect
    {
        get
        {
            int n = 0;
            foreach (var t in BaitData.Thralls) if (t.Active) n++;
            float s = Sc;
            return new Rect(UiSettings.PartyX * Screen.width, UiSettings.PartyY * Screen.height, 340 * s, 22 * s + Mathf.Max(1, n) * 36 * s);
        }
    }

    bool dragging;
    Vector2 dragOff;

    void DragParty()
    {
        var ev = Event.current;
        if (ev == null) return;
        Vector2 m = new Vector2(ev.mousePosition.x, ev.mousePosition.y);
        var r = PartyRect;
        if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(m)) { dragging = true; dragOff = m - new Vector2(r.x, r.y); ev.Use(); }
        else if (dragging && ev.type == EventType.MouseDrag)
        {
            float x = Mathf.Clamp(m.x - dragOff.x, 0, Screen.width - r.width), y = Mathf.Clamp(m.y - dragOff.y, 0, Screen.height - r.height);
            UiSettings.PartyX = x / Screen.width; UiSettings.PartyY = y / Screen.height;
            ev.Use();
        }
        else if (dragging && ev.type == EventType.MouseUp) { dragging = false; UiSettings.Save(); ev.Use(); }
    }

    static string Title(UiThrall t) { return (t.Level > 0 ? "Lv" + t.Level + " " : "") + t.Label; }
    static float HpFrac(EntityAlive e) { float m = e.GetMaxHealth(); return m <= 0 ? 0f : Mathf.Clamp01(e.Stats.Health.Value / m); }
    static readonly Color InvColor = new Color(0.45f, 0.85f, 1f);
    static Color HpColor(float f) { return f > 0.5f ? Color.Lerp(Color.yellow, Color.green, (f - 0.5f) * 2f) : Color.Lerp(Color.red, Color.yellow, f * 2f); }

    void Plate(Camera cam, EntityPlayerLocal p, EntityAlive e, string text, float frac, Color col, string extra, float maxDist) { Plate(cam, p, e, text, frac, col, extra, maxDist, UiSettings.NameColor); }

    void Plate(Camera cam, EntityPlayerLocal p, EntityAlive e, string text, float frac, Color col, string extra, float maxDist, Color nameCol)
    {
        if ((e.position - p.position).magnitude > maxDist) return;
        Vector3 sp = cam.WorldToScreenPoint(e.position - Origin.position + Vector3.up * (e.height + 0.35f));   // entity.position is world space; the camera lives in floating-origin space
        if (sp.z <= 0.1f) return;
        float x = sp.x, y = Screen.height - sp.y;
        float s = Sc;
        Shadow(new Rect(x - 120 * s, y - 20 * s, 240 * s, 18 * s), text + (extra != null ? " [" + extra + "]" : ""), label, nameCol);
        Bar(new Rect(x - 50 * s, y - 1, 100 * s, 8 * s), frac, col);
    }

    static void Shadow(Rect r, string text, GUIStyle st) { Shadow(r, text, st, Color.white); }

    static void Shadow(Rect r, string text, GUIStyle st, Color tint)
    {
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.9f);
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, st);
        GUI.color = tint;
        GUI.Label(r, text, st);
        GUI.color = old;
    }

    static void Bar(Rect r, float frac, Color col)
    {
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.75f);
        GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), Texture2D.whiteTexture);
        GUI.color = col;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), Texture2D.whiteTexture);
        GUI.color = old;
    }
}

// On a multiplayer client the game sends EVERY F1 command to the server; this makes "baitui" run locally instead.
[HarmonyPatch(typeof(GUIWindowConsole), nameof(GUIWindowConsole.EnterCommand))]
public static class BaitUIConsolePatch
{
    static bool Prefix(GUIWindowConsole __instance, string _command)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_command)) return true;
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsClient) return true;
            if (_command.Trim().Split(' ')[0].ToLowerInvariant() != "baitui") return true;
            __instance.scrollRect.verticalNormalizedPosition = 0f;
            __instance.internalAddLine(new GUIWindowConsole.ConsoleLine("> " + _command, string.Empty, LogType.Log));
            GUIWindowConsole.AddLines(SingletonMonoBehaviour<SdtdConsole>.Instance.ExecuteSync(_command, null));
            var hist = __instance.lastCommands;
            if (hist.Count == 0 || !hist[hist.Count - 1].Equals(_command)) { hist.Remove(_command); hist.Add(_command); }
            __instance.lastCommandsIdx = hist.Count;
            __instance.commandField.text = "";
            __instance.commandField.Select();
            __instance.commandField.ActivateInputField();
            return false;
        }
        catch (Exception e) { Log.Warning("[BaitUI] console patch: " + e.Message); return true; }
    }
}

// Console command (F1): baitui [nameplates|party|feed on|off] [key <key>]
public class ConsoleCmdBaitUI : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient { get { return true; } }
    public override bool AllowedInMainMenu { get { return true; } }
    public override int DefaultPermissionLevel { get { return 1000; } }
    public override string[] getCommands() { return new[] { "baitui" }; }
    public override string getDescription() { return "Bait UI: turn name tags, the party list and fed tags on or off, change the roster hotkey"; }
    public override string getHelp()
    {
        return "baitui                       - show settings\n" +
               "baitui nameplates on|off     - name tags and HP bars over your thralls\n" +
               "baitui party on|off          - the HP list on the left\n" +
               "baitui feed on|off           - 'Fed x/y' tags over creatures you are taming\n" +
               "baitui scale <number>       - size of the tags and party list (default 1.6)\n" +
               "baitui reset                - party list back to the default spot and size\n" +
               "baitui key <key>             - roster window hotkey (default Home), 'none' to disable";
    }

    public override void Execute(List<string> p, CommandSenderInfo info)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        if (p.Count == 1 && p[0].ToLowerInvariant() == "reset") { UiSettings.PartyX = 0.006f; UiSettings.PartyY = 0.14f; UiSettings.Scale = 1.6f; UiSettings.Save(); }
        else if (p.Count >= 2)
        {
            string what = p[0].ToLowerInvariant(), v = p[1].ToLowerInvariant();
            bool on = v == "on" || v == "true" || v == "1";
            if (what == "nameplates") UiSettings.Nameplates = on;
            else if (what == "party") UiSettings.Party = on;
            else if (what == "feed") UiSettings.Feed = on;
            else if (what == "reset") { UiSettings.PartyX = 0.006f; UiSettings.PartyY = 0.14f; UiSettings.Scale = 1.6f; }
            else if (what == "scale")
            {
                float sc;
                if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sc)) { con.Output("baitui scale <number>, e.g. 1.6 (default), 2.2 for bigger"); return; }
                UiSettings.Scale = Mathf.Clamp(sc, 0.5f, 4f);
            }
            else if (what == "key")
            {
                KeyCode k;
                if (v == "none") UiSettings.Key = KeyCode.None;
                else if (UiSettings.TryParseKey(p[1], out k)) UiSettings.Key = k;
                else { con.Output("'" + p[1] + "' is not a key name I know. Examples: Home, Backslash, End, K, Keypad5."); return; }
            }
            else { con.Output(getHelp()); return; }
            UiSettings.Save();
        }
        con.Output("Bait UI: name tags " + (UiSettings.Nameplates ? "on" : "off") + ", party list " + (UiSettings.Party ? "on" : "off") +
                   ", fed tags " + (UiSettings.Feed ? "on" : "off") + ", size " + UiSettings.Scale + ", roster key " + UiSettings.Key + ".");
    }
}