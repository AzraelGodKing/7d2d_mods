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
            if (Event.current.type != EventType.Repaint) return;
            Camera cam; EntityPlayerLocal p;
            if (!HudVisible(out cam, out p)) return;
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
                    var e = BaitData.Live(t.EntityId);
                    if (UiSettings.Nameplates && e != null && t.Active) Plate(cam, p, e, Title(t), HpFrac(e), HpColor(HpFrac(e)), t.Stay ? "hold" : null, 45f);
                    if (UiSettings.Party && t.Active)
                    {
                        float s = Sc, y = 150 * Screen.height / 1080f + row * 36 * s;
                        float f = e != null ? HpFrac(e) : 0f;
                        Shadow(new Rect(12, y, 320 * s, 20 * s), (t.Tether ? "[T] " : "") + Title(t) + (e == null ? " (away)" : ""), small);
                        Bar(new Rect(12, y + 21 * s, 220 * s, 7 * s), f, HpColor(f));
                        row++;
                    }
                }
            }
            if (UiSettings.Feed)
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

    static string Title(UiThrall t) { return (t.Level > 0 ? "Lv" + t.Level + " " : "") + t.Label; }
    static float HpFrac(EntityAlive e) { float m = e.GetMaxHealth(); return m <= 0 ? 0f : Mathf.Clamp01(e.Stats.Health.Value / m); }
    static Color HpColor(float f) { return f > 0.5f ? Color.Lerp(Color.yellow, Color.green, (f - 0.5f) * 2f) : Color.Lerp(Color.red, Color.yellow, f * 2f); }

    void Plate(Camera cam, EntityPlayerLocal p, EntityAlive e, string text, float frac, Color col, string extra, float maxDist)
    {
        if ((e.position - p.position).magnitude > maxDist) return;
        Vector3 sp = cam.WorldToScreenPoint(e.position + Vector3.up * (e.height + 0.35f));
        if (sp.z <= 0.1f) return;
        float x = sp.x, y = Screen.height - sp.y;
        float s = Sc;
        Shadow(new Rect(x - 120 * s, y - 20 * s, 240 * s, 18 * s), text + (extra != null ? " [" + extra + "]" : ""), label);
        Bar(new Rect(x - 50 * s, y - 1, 100 * s, 8 * s), frac, col);
    }

    static void Shadow(Rect r, string text, GUIStyle st)
    {
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.9f);
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, st);
        GUI.color = Color.white;
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
               "baitui key <key>             - roster window hotkey (default Backslash, the backslash key), 'none' to disable";
    }

    public override void Execute(List<string> p, CommandSenderInfo info)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        if (p.Count >= 2)
        {
            string what = p[0].ToLowerInvariant(), v = p[1].ToLowerInvariant();
            bool on = v == "on" || v == "true" || v == "1";
            if (what == "nameplates") UiSettings.Nameplates = on;
            else if (what == "party") UiSettings.Party = on;
            else if (what == "feed") UiSettings.Feed = on;
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
                else { con.Output("'" + p[1] + "' is not a key name I know. Examples: Backslash, F8, Home, K, Keypad5."); return; }
            }
            else { con.Output(getHelp()); return; }
            UiSettings.Save();
        }
        con.Output("Bait UI: name tags " + (UiSettings.Nameplates ? "on" : "off") + ", party list " + (UiSettings.Party ? "on" : "off") +
                   ", fed tags " + (UiSettings.Feed ? "on" : "off") + ", size " + UiSettings.Scale + ", roster key " + UiSettings.Key + ".");
    }
}