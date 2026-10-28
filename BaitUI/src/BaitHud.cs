using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Draws the name tags, party list and fed-progress tags (IMGUI), handles the roster hotkey and the "hello" to the server.
public partial class BaitHud : MonoBehaviour
{
    float nextHello;
    int helloTries;
    GUIStyle label, small, tiny;
    float styleScale;
    static float Sc { get { return UiSettings.Scale * Screen.height / 1080f; } }

    float nextNav;

    // Thralls are not hostile to their players: take the enemy marker (compass and map) off every thrall the server told us about.
    // Our own thrall markers (MapMarks) stay.
    void ClearThrallMarks()
    {
        if (Time.time < nextNav || BaitData.All.Count == 0 || !NavObjectManager.HasInstance) return;
        nextNav = Time.time + 1f;
        List<NavObject> drop = null;
        foreach (var n in NavObjectManager.Instance.NavObjectList)
        {
            if (n == null || MapMarks.IsOurs(n) || n.TrackedEntity == null || !BaitData.All.Contains(n.TrackedEntity.entityId)) continue;
            if (drop == null) drop = new List<NavObject>();
            drop.Add(n);
        }
        if (drop == null) return;
        foreach (var n in drop)
        {
            var e = n.TrackedEntity as EntityAlive;
            NavObjectManager.Instance.UnRegisterNavObject(n);
            if (e != null && e.NavObject == n) e.NavObject = null;
        }
    }

    void Update()
    {
        try
        {
            var p = BaitData.Me;
            if (p == null)
            {
                if (BaitData.HasData || helloTries > 0) { BaitData.Reset(); MapMarks.Clear(); helloTries = 0; }
                return;
            }

            ClearThrallMarks();
            MapMarks.Update();

            // Tell the server mod we are here (only on servers that run Bait, and only a few tries).
            if (!BaitData.HasData && BaitData.ServerHasBait())
            {
                if (helloTries < 3 && Time.time > nextHello)
                {
                    helloTries++;
                    nextHello = Time.time + 15f;
                    BaitData.Cmd("/thrall hello", false);
                }
                else if (helloTries >= 3 && Time.time > nextHello) BaitData.HelloGaveUp = true;   // stop asking: a server without Bait would show every ask as a chat line
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
            Extras.Tick(ui);
            TrackDamage(p);
            if (wm.IsInputActive()) { wheelOpen = false; return; }
            if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
            if (p.IsDead()) { wheelOpen = false; return; }
            bool free = !wm.IsCursorWindowOpen() && !wm.IsModalWindowOpen();   // playing, no window up
            if (free && BaitData.HasData && !UiSettings.CaptureKey && UiSettings.CaptureGroup == 0 && !UiSettings.CaptureWheel && !UiSettings.CaptureGoto)
            {
                ThrallKeys();
                GotoKey(p);
                // group hotkeys: follow all, stay all, bring all here
                if (UiSettings.FollowKey != KeyCode.None && Input.GetKeyDown(UiSettings.FollowKey)) BaitData.Cmd("/thrall follow all");
                if (UiSettings.StayKey != KeyCode.None && Input.GetKeyDown(UiSettings.StayKey)) BaitData.Cmd("/thrall stay all");
                if (UiSettings.HereKey != KeyCode.None && Input.GetKeyDown(UiSettings.HereKey) && BaitData.ServerHasQol) BaitData.Cmd("/thrall here");
                WheelUpdate();
            }
            else wheelOpen = false;
            if (UiSettings.Key == KeyCode.None || !Input.GetKeyDown(UiSettings.Key)) return;
            BaitData.TryOpenRoster();
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
            if (UiSettings.CaptureGoto && Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
            {
                var pressed = Event.current.keyCode;
                UiSettings.CaptureGoto = false;
                if (pressed != KeyCode.Escape) { UiSettings.GotoKey = pressed == KeyCode.Delete || pressed == KeyCode.Backspace ? KeyCode.None : pressed; UiSettings.Save(); BaitData.SetStatus("Key saved."); }
                Event.current.Use();
                return;
            }
            if ((UiSettings.CaptureGroup > 0 || UiSettings.CaptureWheel) && Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
            {
                var pressed = Event.current.keyCode;
                var k = pressed == KeyCode.Delete || pressed == KeyCode.Backspace ? KeyCode.None : pressed;
                if (UiSettings.CaptureWheel) { UiSettings.CaptureWheel = false; if (pressed != KeyCode.Escape) UiSettings.WheelKey = k; }
                else
                {
                    // one click sets all three in turn: follow, stay, here
                    if (pressed != KeyCode.Escape) UiSettings.SetGroupKey(UiSettings.CaptureGroup, k);
                    UiSettings.CaptureGroup = pressed == KeyCode.Escape || UiSettings.CaptureGroup >= 3 ? 0 : UiSettings.CaptureGroup + 1;
                }
                UiSettings.Save(); BaitData.SetStatus("Key saved.");
                Event.current.Use();
                return;
            }
            if (UiSettings.CaptureKey && Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
            {
                var pressed = Event.current.keyCode;
                UiSettings.CaptureKey = false;
                if (pressed != KeyCode.Escape) { UiSettings.Key = pressed; UiSettings.Save(); }
                Event.current.Use();
                return;
            }
            if (Event.current.type != EventType.Repaint)
            {
                if (edit && UiSettings.DownedPanel) DownedPanelClick();
                if (edit && UiSettings.Party && !UiSettings.HpStrip) DragParty();
                return;
            }
            if (!hud && !(edit && UiSettings.Party)) return;
            if (label == null || Mathf.Abs(styleScale - Sc) > 0.01f)
            {
                styleScale = Sc;
                label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13 * Sc), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
                small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(14 * Sc), alignment = TextAnchor.MiddleLeft };
                tiny = new GUIStyle(label) { fontSize = Mathf.RoundToInt(11 * Sc), alignment = TextAnchor.MiddleLeft };
            }

            bool partyList = UiSettings.Party && !UiSettings.HpStrip;   // the HP strip replaces the party list
            if (hud && UiSettings.HpStrip) DrawStrip();
            if (hud && UiSettings.TamingHelp) DrawTamingHelp();
            if (UiSettings.Nameplates || partyList)
            {
                int row = 0;
                if (partyList && hud && !edit && UiSettings.Theme != 0)
                {
                    var old = GUI.color; GUI.color = UiSettings.HudBack; GUI.DrawTexture(PartyRect, Texture2D.whiteTexture); GUI.color = old;
                }
                foreach (var t in BaitData.Thralls)
                {
                    var e = BaitData.LiveThrall(t);
                    string tk = UiSettings.ThrallKey(t);
                    if (e != null) lastPos[tk] = e.position + Vector3.up * (e.height + 0.35f);
                    if (hud && UiSettings.Nameplates && e != null && t.Active)
                    {
                        float hf = HpFrac(e);
                        Color nameCol = UiSettings.NameColorFor(t);
                        string title = Title(t);
                        float lv;
                        if (UiSettings.LevelGlow && BaitData.LevelUpAt.TryGetValue(tk, out lv) && Time.time - lv < 4f)
                        { nameCol = Color.Lerp(Gold, Color.white, Mathf.PingPong(Time.time * 3f, 1f) * 0.5f); title = "Lv " + t.Level + "!  " + t.Label; }
                        bool low = UiSettings.LowHpAlert && t.Mode != 3 && hf < 0.25f;
                        if (low && Mathf.Repeat(Time.time, 0.6f) < 0.3f) nameCol = Color.red;
                        LowHpSound(tk, hf, t.Mode == 3);
                        Plate(cam, p, e, title, hf, t.Mode == 3 ? InvColor : low && Mathf.Repeat(Time.time, 0.6f) < 0.3f ? Color.red : UiSettings.BarColorFor(t, HpColor(hf)), t.Mode == 3 ? (t.Stay ? "invulnerable, hold" : "invulnerable") : (t.Stay ? "hold" : null), 45f, nameCol);
                    }
                    else if (hud && UiSettings.Nameplates && UiSettings.DownedTags && t.DownLeft > 0) DownedTag(cam, p, t, tk);
                    if (partyList && t.Active)
                    {
                        float s = Sc, y = PartyRect.y + 22 * s + row * RowH * s;
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
                        string pn = (t.Patrol ? "[P] " : t.Guard ? "[G] " : t.Tether ? "[T] " : "") + (t.Mode == 3 ? "[Inv] " : "") + Title(t) + (e == null ? " (away)" : "");
                        Color pc = t.Mode == 3 ? InvColor : UiSettings.BarColorFor(t, HpColor(f));
                        if (UiSettings.CompactParty)
                        {
                            // one line: name, then the bar beside it
                            Shadow(new Rect(PartyRect.x + 4, y, 200 * s, 18 * s), pn, tiny, UiSettings.NameColorFor(t));
                            Bar(new Rect(PartyRect.x + 210 * s, y + 6 * s, 120 * s, 7 * s), f, pc);
                        }
                        else
                        {
                            Shadow(new Rect(PartyRect.x + 4, y, 320 * s, 20 * s), pn, small, UiSettings.NameColorFor(t));
                            Bar(new Rect(PartyRect.x + 4, y + 21 * s, 220 * s, 7 * s), f, pc);
                        }
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
            if (hud && UiSettings.KillFeed) DrawKillFeed();
            if (hud) DrawExtras(cam, p);
            if ((hud || edit) && UiSettings.DownedPanel) DrawDownedPanel(edit);
            if (hud && wheelOpen) DrawWheel();
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
            return new Rect(UiSettings.PartyX * Screen.width, UiSettings.PartyY * Screen.height, 340 * s, 22 * s + Mathf.Max(1, n) * RowH * s);
        }
    }
    static float RowH { get { return UiSettings.CompactParty ? 20f : 36f; } }

    // HP strip: a small bar per active thrall in the bottom-right corner (instead of the party list).
    void DrawStrip()
    {
        var list = new List<UiThrall>();
        foreach (var t in BaitData.Thralls) if (t.Active) list.Add(t);
        if (list.Count == 0) return;
        float s = Sc, w = 230 * s, rh = 16 * s, x = Screen.width - w - 12 * s, y0 = Screen.height - 14 * s - list.Count * rh;
        var old = GUI.color;
        GUI.color = UiSettings.HudBack;
        GUI.DrawTexture(new Rect(x - 4 * s, y0 - 4 * s, w + 8 * s, list.Count * rh + 8 * s), Texture2D.whiteTexture);
        GUI.color = old;
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            var e = BaitData.LiveThrall(t);
            float f = e != null ? HpFrac(e) : 0f, y = y0 + i * rh;
            string n = t.DownLeft > 0 ? t.Label + " " + Clock(t.DownLeft) : e == null ? t.Label + " (away)" : t.Label;
            Shadow(new Rect(x, y, 150 * s, rh), n, tiny, t.DownLeft > 0 ? new Color(0.65f, 0.65f, 0.65f) : UiSettings.NameColorFor(t));
            Bar(new Rect(x + 155 * s, y + 5 * s, 72 * s, 6 * s), f, t.Mode == 3 ? InvColor : UiSettings.BarColorFor(t, HpColor(f)));
        }
    }

    // Taming helper: while you hold bait, what the creature under the crosshair needs (the server works it out).
    void DrawTamingHelp()
    {
        var l = BaitData.Look;
        if (l == null || Time.time - l.At > 3f) return;
        var e = BaitData.Live(l.EntityId);
        if (e == null) return;
        string cn = Localization.Get(EntityClass.list[e.entityClass].entityClassName);
        string what = cn + (l.Level > 0 ? " Lv" + l.Level : "");
        string st; Color c;
        switch (l.Status)
        {
            case "mine": st = "already a thrall"; c = new Color(0.5f, 1f, 0.5f); break;
            case "excluded": st = "can't be tamed on this server"; c = new Color(1f, 0.45f, 0.45f); break;
            case "full": st = "your roster is full: release one first"; c = new Color(1f, 0.45f, 0.45f); break;
            case "other": st = "someone else is feeding it"; c = new Color(1f, 0.6f, 0.3f); break;
            default: st = l.Cur > 0 ? "fed " + l.Cur + "/" + l.Need + " meals" : l.Need + " meals to tame"; c = new Color(1f, 0.85f, 0.35f); break;
        }
        float s = Sc, cx = Screen.width / 2f, y = Screen.height * 0.56f;
        Shadow(new Rect(cx - 220 * s, y, 440 * s, 20 * s), what + ":  " + st, label, c);
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

    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    readonly Dictionary<string, Vector3> lastPos = new Dictionary<string, Vector3>();   // where each thrall's tag was last drawn
    readonly HashSet<string> lowWarned = new HashSet<string>();

    public static string Clock(long secs) { return (secs / 60) + ":" + (secs % 60).ToString("00"); }

    // a short sound once when a thrall drops under 25%; again only after it was back above 40%
    void LowHpSound(string key, float frac, bool invulnerable)
    {
        if (invulnerable || frac >= 0.4f) { lowWarned.Remove(key); return; }
        if (frac >= 0.25f || !lowWarned.Add(key) || !UiSettings.LowHpAlert || !UiSettings.LowHpSound) return;
        try { Audio.Manager.PlayInsidePlayerHead("ui_denied"); } catch { }
    }

    // A downed thrall has no entity, so its tag stays where it was last seen, grey, with the time until it comes back.
    void DownedTag(Camera cam, EntityPlayerLocal p, UiThrall t, string key)
    {
        Vector3 at;
        if (!lastPos.TryGetValue(key, out at) || (at - p.position).magnitude > 60f) return;
        Vector3 sp = cam.WorldToScreenPoint(at - Origin.position);
        if (sp.z <= 0.1f) return;
        float s = Sc, x = sp.x, y = Screen.height - sp.y;
        Shadow(new Rect(x - 140 * s, y - 20 * s, 280 * s, 18 * s), Title(t) + "  back in " + Clock(t.DownLeft) + (t.AutoRevive ? " (auto-revive)" : ""), label, new Color(0.65f, 0.65f, 0.65f));
    }

    // The last few kills of your thralls, on the right, fading out after 6 s.
    void DrawKillFeed()
    {
        var kf = BaitData.Kills;
        kf.RemoveAll(k => Time.time - k.At > 6f);
        float s = Sc, w = 420 * s, y0 = Screen.height * 0.32f;
        for (int i = 0; i < kf.Count; i++)
        {
            float age = Time.time - kf[i].At;
            var c = kf[i].Shared ? new Color(0.8f, 0.8f, 0.8f) : new Color(1f, 0.85f, 0.55f);
            c.a = age < 5f ? 1f : 6f - age;
            Shadow(new Rect(Screen.width - w - 12 * s, y0 + i * 20 * s, w, 20 * s), kf[i].Text, small, c);
        }
    }

    // ---- quick-actions wheel: hold the wheel key, move the mouse (or scroll, or press 1-5) towards an action, let go.
    static readonly string[] WheelNames = { "Follow all", "Stay all", "Bring all here", "Stow active", "Call team" };
    bool wheelOpen;
    int wheelSel = -1;
    Vector2 wheelAim;
    static bool axisBroken;

    void WheelUpdate()
    {
        var k = UiSettings.WheelKey;
        if (k == KeyCode.None) { wheelOpen = false; return; }
        if (Input.GetKeyDown(k)) { wheelOpen = true; wheelSel = -1; wheelAim = Vector2.zero; }
        if (!wheelOpen) return;
        if (Input.GetKey(k))
        {
            if (!axisBroken)
            {
                try { wheelAim += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")); }
                catch { axisBroken = true; }
                if (wheelAim.magnitude > 6f) wheelAim = wheelAim.normalized * 6f;
                if (wheelAim.magnitude > 1.5f)
                {
                    float ang = Mathf.Atan2(wheelAim.x, wheelAim.y) * Mathf.Rad2Deg;   // 0 = up, clockwise
                    if (ang < 0f) ang += 360f;
                    wheelSel = Mathf.RoundToInt(ang / 72f) % 5;
                }
            }
            float sc = Input.mouseScrollDelta.y;
            if (sc > 0.1f) wheelSel = (wheelSel + 4) % 5; else if (sc < -0.1f) wheelSel = (wheelSel + 1) % 5;
            for (int i = 0; i < 5; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) wheelSel = i;
            return;
        }
        wheelOpen = false;   // released: run the chosen action
        switch (wheelSel)
        {
            case 0: BaitData.Cmd("/thrall follow all"); break;
            case 1: BaitData.Cmd("/thrall stay all"); break;
            case 2: if (BaitData.ServerHasQol) BaitData.Cmd("/thrall here"); break;
            case 3: BaitData.Cmd("/thrall stow active"); break;
            case 4: if (BaitData.ServerHasQol && BaitData.Teams.Count > 0) BaitData.Cmd("/thrall team call " + BaitData.Teams[Mathf.Clamp(BaitData.TeamSel, 0, BaitData.Teams.Count - 1)].Key); break;
        }
    }

    void DrawWheel()
    {
        float s = Sc, cx = Screen.width / 2f, cy = Screen.height / 2f, r = 110 * s;
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.45f);
        GUI.DrawTexture(new Rect(cx - r - 90 * s, cy - r - 30 * s, 2 * r + 180 * s, 2 * r + 60 * s), Texture2D.whiteTexture);
        GUI.color = old;
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f * Mathf.Deg2Rad;
            float x = cx + Mathf.Sin(a) * r, y = cy - Mathf.Cos(a) * r;
            string n = (i + 1) + ". " + WheelNames[i];
            if (i == 4) n = "5. Call team" + (BaitData.Teams.Count > 0 ? " " + BaitData.Teams[Mathf.Clamp(BaitData.TeamSel, 0, BaitData.Teams.Count - 1)].Key : " (none)");
            if ((i == 2 || i == 4) && !BaitData.ServerHasQol) n = (i + 1) + ". (server too old)";
            Shadow(new Rect(x - 90 * s, y - 11 * s, 180 * s, 22 * s), n, label, i == wheelSel ? Gold : Color.white);
        }
        Shadow(new Rect(cx - 90 * s, cy - 11 * s, 180 * s, 22 * s), wheelSel < 0 ? "move the mouse, scroll or 1-5" : "let go to run", label, new Color(0.8f, 0.8f, 0.8f));
    }

    static string Title(UiThrall t) { return (t.Level > 0 ? "Lv" + t.Level + " " : "") + (t.Alpha ? "[Alpha] " : "") + t.Label; }
    static float HpFrac(EntityAlive e) { float m = e.GetMaxHealth(); return m <= 0 ? 0f : Mathf.Clamp01(e.Stats.Health.Value / m); }
    static readonly Color InvColor = new Color(0.45f, 0.85f, 1f);
    static readonly Color CbFull = new Color(0.25f, 0.55f, 1f), CbMid = new Color(0.85f, 0.85f, 0.85f), CbEmpty = new Color(1f, 0.55f, 0.1f);
    // colour-blind bars (U50) go blue to orange, which reads the same with red-green colour blindness
    static Color HpColor(float f)
    {
        if (UiSettings.ColorBlind) return f > 0.5f ? Color.Lerp(CbMid, CbFull, (f - 0.5f) * 2f) : Color.Lerp(CbEmpty, CbMid, f * 2f);
        return f > 0.5f ? Color.Lerp(Color.yellow, Color.green, (f - 0.5f) * 2f) : Color.Lerp(Color.red, Color.yellow, f * 2f);
    }

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
    public override string getDescription() { return "Bait UI: turn name tags, the party list and fed tags on or off, change the roster hotkey, open the roster"; }
    public override string getHelp()
    {
        return "baitui                       - show settings\n" +
               "baitui nameplates on|off     - name tags and HP bars over your thralls\n" +
               "baitui party on|off          - the HP list on the left\n" +
               "baitui feed on|off           - 'Fed x/y' tags over creatures you are taming\n" +
               "baitui scale <number>       - size of the tags and party list (default 1.6)\n" +
               "baitui reset                - party list back to the default spot and size\n" +
               "baitui key <key>             - roster window hotkey (default Home), 'none' to disable\n" +
               "baitui open                  - open or close the roster window\n" +
               "baitui followkey|staykey|herekey <key>  - group hotkeys (default F6, F7, F8), 'none' to disable\n" +
               "baitui wheelkey <key>        - hold for the quick-actions wheel (default Mouse2, the middle button)\n" +
               "baitui version               - this Bait UI's version and the server's Bait version";
    }

    public override void Execute(List<string> p, CommandSenderInfo info)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        if (p.Count == 1 && p[0].ToLowerInvariant() == "open")
        {
            if (!BaitData.TryOpenRoster()) con.Output("Could not open the roster (not in a world, or the window XML did not load).");
            return;
        }
        if (p.Count == 1 && p[0].ToLowerInvariant() == "version")
        {
            string server = !string.IsNullOrEmpty(BaitData.ServerVersion) ? BaitData.ServerVersion
                          : BaitData.HasData ? "older than 1.7.6 (it does not say)"
                          : BaitData.HelloGaveUp ? "no answer (not installed, or older than 1.3.0)"
                          : BaitData.Me == null ? "not in a world" : "waiting for the server";
            con.Output("Bait UI " + AzraelBaitUIMod.Version + ", server Bait " + server + ".");
            return;
        }
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
            else if (what == "followkey" || what == "staykey" || what == "herekey" || what == "wheelkey")
            {
                KeyCode k = KeyCode.None;
                if (v != "none" && !UiSettings.TryParseKey(p[1], out k)) { con.Output("'" + p[1] + "' is not a key name I know. Examples: F6, G, Keypad5, Mouse2."); return; }
                if (what == "followkey") UiSettings.FollowKey = k; else if (what == "staykey") UiSettings.StayKey = k; else if (what == "herekey") UiSettings.HereKey = k; else UiSettings.WheelKey = k;
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