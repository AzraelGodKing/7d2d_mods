using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Bait UI 1.9.0 HUD extras: damage numbers, target lines, the tame banner, the downed panel, roaming Alpha tags,
// per-thrall hotkeys and the go-to ping.
public partial class BaitHud
{
    class DmgNum { public Vector3 At; public int Amount; public float Time; }
    readonly List<DmgNum> dmgNums = new List<DmgNum>();
    readonly Dictionary<int, float> lastHp = new Dictionary<int, float>();   // what our thralls fight -> its HP last frame

    // U44: a number floats up over what your thralls hit (from the HP drop of each thrall's current target).
    void TrackDamage(EntityPlayerLocal p)
    {
        if (!UiSettings.DamageNumbers || BaitData.Targets.Count == 0) { lastHp.Clear(); return; }
        var seen = new HashSet<int>();
        foreach (int id in BaitData.Targets.Values)
        {
            if (!seen.Add(id)) continue;
            var e = BaitData.Live(id);
            if (e == null) continue;
            float hp = e.Stats.Health.Value, was;
            if (lastHp.TryGetValue(id, out was) && hp < was - 0.5f && (e.position - p.position).magnitude < 60f)
                dmgNums.Add(new DmgNum { At = e.position + Vector3.up * (e.height + 0.2f), Amount = Mathf.RoundToInt(was - hp), Time = Time.time });
            lastHp[id] = hp;
        }
        if (lastHp.Count > seen.Count) foreach (int id in new List<int>(lastHp.Keys)) if (!seen.Contains(id)) lastHp.Remove(id);
        if (dmgNums.Count > 40) dmgNums.RemoveRange(0, dmgNums.Count - 40);
    }

    void DrawExtras(Camera cam, EntityPlayerLocal p)
    {
        float s = Sc;
        // U45: a faint line from each thrall to what it fights
        if (UiSettings.TargetLines)
        {
            foreach (var kv in BaitData.Targets)
            {
                var a = BaitData.Live(kv.Key);
                var b = BaitData.Live(kv.Value);
                if (a == null || b == null || (a.position - p.position).magnitude > 45f) continue;
                Vector3 sa = cam.WorldToScreenPoint(a.position - Origin.position + Vector3.up * (a.height * 0.6f));
                Vector3 sb = cam.WorldToScreenPoint(b.position - Origin.position + Vector3.up * (b.height * 0.6f));
                if (sa.z <= 0.1f || sb.z <= 0.1f) continue;
                Line(new Vector2(sa.x, Screen.height - sa.y), new Vector2(sb.x, Screen.height - sb.y), 2f, new Color(1f, 0.35f, 0.25f, 0.45f));
            }
        }
        // U44: damage numbers drift up and fade over a second
        if (UiSettings.DamageNumbers && dmgNums.Count > 0)
        {
            dmgNums.RemoveAll(d => Time.time - d.Time > 1.2f);
            foreach (var d in dmgNums)
            {
                float age = Time.time - d.Time;
                Vector3 sp = cam.WorldToScreenPoint(d.At - Origin.position + Vector3.up * age * 0.8f);
                if (sp.z <= 0.1f) continue;
                Shadow(new Rect(sp.x - 40 * s, Screen.height - sp.y - 10 * s, 80 * s, 20 * s), d.Amount.ToString(), label, new Color(1f, 0.9f, 0.3f, 1.2f - age));
            }
        }
        // S67: roaming Alphas the server told us about
        foreach (var kv in BaitData.Alphas)
        {
            var e = BaitData.Live(kv.Key);
            if (e == null || e.IsDead()) continue;
            Plate(cam, p, e, "Alpha " + kv.Value, HpFrac(e), new Color(1f, 0.55f, 0.1f), "tame with Feast bait", 60f, Gold);
        }
        // U53: where the go-to ping landed
        if (Time.time - pingAt < 2f)
        {
            Vector3 sp = cam.WorldToScreenPoint(pingPos - Origin.position + Vector3.up * 0.3f);
            if (sp.z > 0.1f) Shadow(new Rect(sp.x - 60 * s, Screen.height - sp.y - 10 * s, 120 * s, 20 * s), "v  go here  v", label, new Color(0.5f, 0.9f, 1f, 2f - (Time.time - pingAt)));
        }
        // U48: the tame banner
        float age2 = Time.time - Extras.BannerAt;
        if (Extras.Banner != null && age2 < 4f)
        {
            float a = age2 < 0.3f ? age2 / 0.3f : age2 > 3.3f ? (4f - age2) / 0.7f : 1f;
            var r = new Rect(Screen.width / 2f - 320 * s, Screen.height * 0.2f, 640 * s, 44 * s);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f * a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, a);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 3 * s, r.width, 3 * s), Texture2D.whiteTexture);
            GUI.color = old;
            if (bannerStyle == null || bannerStyle.fontSize != Mathf.RoundToInt(22 * s)) bannerStyle = new GUIStyle(label) { fontSize = Mathf.RoundToInt(22 * s) };
            Shadow(r, Extras.Banner, bannerStyle, new Color(1f, 0.92f, 0.6f, a));
        }
    }
    GUIStyle bannerStyle;

    static void Line(Vector2 a, Vector2 b, float width, Color col)
    {
        var m = GUI.matrix;
        var old = GUI.color;
        float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, len = (b - a).magnitude;
        GUI.color = col;
        GUIUtility.RotateAroundPivot(ang, a);
        GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, len, width), Texture2D.whiteTexture);
        GUI.matrix = m;
        GUI.color = old;
    }

    // U46: downed thralls in the bottom-left corner, with a Revive button each while a window (and the cursor) is up.
    readonly List<KeyValuePair<Rect, int>> reviveButtons = new List<KeyValuePair<Rect, int>>();

    void DrawDownedPanel(bool edit)
    {
        reviveButtons.Clear();
        var down = BaitData.Thralls.FindAll(t => t.DownLeft > 0);
        if (down.Count == 0) return;
        float s = Sc, rh = 22 * s, w = 330 * s, x = 12 * s, y0 = Screen.height - 120 * s - down.Count * rh;
        var old = GUI.color;
        GUI.color = UiSettings.HudBack;
        GUI.DrawTexture(new Rect(x - 4 * s, y0 - 24 * s, w + 8 * s, down.Count * rh + 28 * s), Texture2D.whiteTexture);
        GUI.color = old;
        Shadow(new Rect(x, y0 - 22 * s, w, 20 * s), L.T("Downed") + (edit ? "" : "  (open a window to revive)"), small, new Color(1f, 0.6f, 0.5f));
        for (int i = 0; i < down.Count; i++)
        {
            var t = down[i];
            float y = y0 + i * rh;
            Shadow(new Rect(x, y, w - 90 * s, rh), t.Idx + ". " + t.Label + "  " + Clock(t.DownLeft) + (t.AutoRevive ? " (auto)" : ""), tiny, new Color(0.8f, 0.8f, 0.8f));
            if (!edit) continue;
            var br = new Rect(x + w - 84 * s, y + 1, 84 * s, rh - 2);
            reviveButtons.Add(new KeyValuePair<Rect, int>(br, t.Idx));
            bool hover = br.Contains(Event.current.mousePosition);
            GUI.color = hover ? new Color(0.55f, 0.3f, 0.2f, 0.95f) : new Color(0.3f, 0.18f, 0.12f, 0.9f);
            GUI.DrawTexture(br, Texture2D.whiteTexture);
            GUI.color = old;
            Shadow(br, L.T("Revive"), label, Color.white);
        }
    }

    void DownedPanelClick()
    {
        var ev = Event.current;
        if (ev == null || ev.type != EventType.MouseDown || ev.button != 0) return;
        foreach (var kv in reviveButtons)
            if (kv.Key.Contains(ev.mousePosition)) { BaitData.Cmd("/thrall revive " + kv.Value); ev.Use(); return; }
    }

    // U54: hold the modifier and press 1-9 to call or stow roster number 1 to 9.
    void ThrallKeys()
    {
        bool held;
        switch (UiSettings.ThrallMod)
        {
            case 0: held = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt); break;
            case 1: held = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl); break;
            case 2: held = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); break;
            default: return;
        }
        if (!held) return;
        for (int n = 1; n <= 9; n++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha0 + n)) continue;
            var t = BaitData.Thralls.Find(x => x.Idx == n);
            if (t == null) { BaitData.SetStatus("No thrall " + n + "."); return; }
            BaitData.Cmd((t.Active ? "/thrall stow " : "/thrall call ") + n);
            return;
        }
    }

    // U53: aim at a spot and press the go-to key; the thrall picked in the roster (or every one with you) walks there.
    void GotoKey(EntityPlayerLocal p)
    {
        var k = UiSettings.GotoKey;
        if (k == KeyCode.None || !Input.GetKeyDown(k)) return;
        if (!BaitData.ServerHasBatch3) { BaitData.SetStatus("Go-to needs Bait " + BaitData.Batch3Need + " or newer on the server."); return; }
        var cam = p.playerCamera;
        if (cam == null) return;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit best = default(RaycastHit);
        bool found = false;
        foreach (var h in Physics.RaycastAll(ray, 120f))
        {
            if (h.collider == null || h.collider.isTrigger) continue;
            var ent = h.collider.GetComponentInParent<Entity>();
            if (ent != null) continue;   // players, thralls and zombies: aim at the ground
            if (!found || h.distance < best.distance) { best = h; found = true; }
        }
        if (!found) { BaitData.SetStatus("Aim at the ground (within 120 m)."); return; }
        Vector3 w = best.point + Origin.position;
        string who = BaitData.SelectedIdx >= 0 && BaitData.Thralls.Exists(t => t.Idx == BaitData.SelectedIdx) ? BaitData.SelectedIdx.ToString() : "all";
        var ci = CultureInfo.InvariantCulture;
        BaitData.Cmd("/thrall goto " + who + " " + w.x.ToString("0.#", ci) + " " + w.y.ToString("0.#", ci) + " " + w.z.ToString("0.#", ci));
        pingAt = Time.time; pingPos = w;
    }
    float pingAt = -10f;
    Vector3 pingPos;
}
