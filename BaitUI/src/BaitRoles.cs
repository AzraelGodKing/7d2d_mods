using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

// Bait UI 1.8.0: hover tooltips, the thrall page (stats and role buttons) and map markers.

// Tooltips for every roster button. Costs are read from the server's settings, so they stay right when an admin changes them.
public static class Tips
{
    static string Cfg(string key, string def)
    {
        foreach (var c in BaitData.Config) if (c[0] == key) return c[1];
        return def;
    }

    static readonly Dictionary<string, string> Fixed = new Dictionary<string, string>
    {
        { "dragbar", "Drag the title bar to move the window. Re-center puts it back." },
        { "prev", "Previous page" }, { "next", "Next page" },
        { "filter", "Show all thralls, only zombies or only animals" },
        { "sort", "Sort by roster number, level or lowest HP first" },
        { "pagesize", "8, 12 or 16 rows per page" },
        { "admin", "Server settings and the cannot-be-tamed list (admins)" },
        { "close", "Close the window" },
        { "stowall", "Put every thrall away, tethered ones too" },
        { "stowactive", "Put away the ones following you" },
        { "stowtethered", "Put away the ones in a daycare, guard post or patrol" },
        { "followall", "Every thrall out with you follows you" },
        { "stayall", "Every thrall out with you holds position" },
        { "deaths", "Your last 10 thrall deaths and what killed them (in chat)" },
        { "resetpos", "Move this window back to the middle of the screen" },
        { "tgltags", "Name tags and HP bars over your thralls" },
        { "tglparty", "The list of your active thralls on the left of the screen" },
        { "tglfed", "'Fed x/y' tags over creatures you are taming" },
        { "tglpbars", "Name tags over thralls that belong to your party members" },
        { "cyclecolor", "Colour of the name tag (for all thralls, or the one picked with 'Colors for')" },
        { "cyclebar", "Colour of the HP bar (Auto goes green to red)" },
        { "cycletarget", "Pick which thrall the colour buttons change, or all of them" },
        { "sizedown", "Smaller name tags and party list" }, { "sizeup", "Bigger name tags and party list" },
        { "tglkills", "Your thralls' kills listed on the right of the screen" },
        { "tglglow", "A gold name tag for a few seconds when a thrall levels up" },
        { "tgllowhp", "Flash a thrall's tag red under 25% HP" },
        { "tgllowsnd", "A short sound when a thrall drops under 25% HP" },
        { "tgldowned", "A grey 'back in m:ss' tag where a downed thrall fell" },
        { "pickkey", "Click, then press the key that opens this window" },
        { "pickgroup", "Click, then press the keys for Follow all, Stay all and Bring all here in turn (Del = none)" },
        { "pickwheel", "Click, then press the key you hold for the quick-actions wheel" },
        { "teamcycle", "Pick a saved team" },
        { "teamcall", "Stow everyone else and bring out this team" },
        { "teamsave", "Save the thralls that are out now as this team" },
        { "teamnew", "Save the thralls that are out now as a new team" },
        { "thrallpage", "Stats, perks, guard post, patrol, scout and evolve for the selected thrall (or the first one)" },
        { "top", "The server's top 10 thralls by level (in chat)" },
        { "tglmap", "Your thralls and their daycare, guard and patrol spots on the map and compass" },
        { "tglstrip", "Small HP bars in the bottom-right corner instead of the party list" },
        { "tglcompact", "Party list with the name and bar on one line, so more thralls fit" },
        { "cycletheme", "Window colours: dark, light or high contrast" },
        { "tglhelp", "While you hold bait: meals needed for what you aim at, and whether it can be tamed" },
        { "search", "Type part of a name (or a number) to find a thrall" },
    };

    public static void Apply(XUiController w, int rows)
    {
        foreach (var kv in Fixed) Set(w, kv.Key, kv.Value);
        Set(w, "allhere", "Bring every thrall that follows you to your side (once every " + Cfg("HereCooldownSeconds", "30") + "s)");
        string heal = "Heal: " + Cfg("HealBaitCost", "1") + " bait, restores " + Cfg("HealPct", "40") + "% HP.  Revive (when downed): "
                    + Cfg("ReviveCostPlain", "3") + " Plain, " + Cfg("ReviveCostRich", "2") + " Rich or " + Cfg("ReviveCostFeast", "1") + " Feast bait";
        string auto = "Auto-revive: when it goes down, " + Cfg("AutoReviveCost", "3") + " plain bait from your inventory brings it straight back";
        string hbox = "Auto-heal: under " + Cfg("AutoHealBelowPct", "30") + "% HP, " + Cfg("HealBaitCost", "1") + " plain bait heals it (at most every " + Cfg("AutoHealCooldownSeconds", "20") + "s)";
        for (int i = 1; i <= rows; i++)
        {
            string r = "r" + i;
            Set(w, r + "sel", "Click for this thrall's details below (click again to hide)");
            Set(w, r + "call", "Call: bring it out (uses an active slot).  Stow: put it away");
            Set(w, r + "stay", "Stay: hold position.  Follow: follow you again");
            Set(w, r + "mode", "Aggr attacks hostiles it sees, Def only what comes close, Pass never fights, Inv can't be hurt and never attacks");
            Set(w, r + "teth", "Tether: it stays in a circle where you stand now (a daycare), even while you are away");
            Set(w, r + "heal", heal);
            Set(w, r + "name", "Give it a nickname. A name works anywhere a number does");
            Set(w, r + "rel", "Let it go for good (click twice)");
            Set(w, r + "auto", auto);
            Set(w, r + "hbox", hbox);
            Set(w, r + "up", "Move it up the roster (its number changes)");
            Set(w, r + "down", "Move it down the roster (its number changes)");
        }
    }

    static void Set(XUiController w, string id, string text)
    {
        var c = w.GetChildById(id);
        if (c != null && c.ViewComponent != null && c.ViewComponent.ToolTip != text) c.ViewComponent.ToolTip = text;
    }
}

// The thrall page: one thrall's stats, and buttons for perks, guard post, patrol, scout, evolve and blood moon mode.
// Referenced as controller="AzraelBaitStats, AzraelBaitUI". Needs Bait 1.10.0 on the server.
public class XUiC_AzraelBaitStats : XUiController
{
    int seen = -1;
    float nextRefresh;

    static UiThrall T { get { return BaitData.Thralls.Find(t => t.Idx == BaitData.StatsIdx); } }

    void Hook(string id, Action a, string tip = null)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] thrall page: no button " + id); return; }
        if (tip != null && c.ViewComponent != null) c.ViewComponent.ToolTip = tip;
        c.OnPress += (s, b) => { try { a(); } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    void On(string verb) { var t = T; if (t != null) BaitData.Cmd("/thrall " + verb.Replace("#", t.Idx.ToString())); }

    static int Cfg(string key, int def)
    {
        foreach (var c in BaitData.Config) { int v; if (c[0] == key && int.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v; }
        return def;
    }

    public override void Init()
    {
        base.Init();
        Hook("ptough", () => On("perk # tough"), "Tough: takes less damage");
        Hook("phit", () => On("perk # hit"), "Hit: deals more damage");
        Hook("pregen", () => On("perk # regen"), "Regen: heals faster, even while fighting");
        Hook("ptaunt", () => On("perk # taunt"), "Taunt: pulls zombies off you onto itself (animals too, wider than normal)");
        Hook("preset", () => On("perk # reset"), "Clear its perks to pick again (costs Feast bait, see the server's PerkResetFeast)");
        Hook("guard", () => { var t = T; if (t != null) BaitData.Cmd(t.Guard || t.Patrol ? "/thrall untether " + t.Idx : "/thrall guard " + t.Idx); },
             "Guard post: it stays where you stand now and attacks anything hostile that comes into its circle. You get a message when it fights while you are away");
        Hook("padd", () => On("patrol # add"), "Add the spot you stand on to its patrol route (up to 8)");
        Hook("pstart", () => { var t = T; if (t != null) BaitData.Cmd("/thrall patrol " + t.Idx + (t.Patrol ? " stop" : " start")); }, "Start or stop walking the route (needs 2 points)");
        Hook("pclear", () => On("patrol # clear"), "Forget the patrol points");
        Hook("scout", () => On("scout #"), "It runs ahead the way you look and reports the hostiles it finds, then comes back");
        Hook("evolve", () => On("evolve #"), "Turn a high level zombie into its feral, radiated or charged version (keeps its level and name)");
        Hook("bloodmoon", () =>
        {
            string[] modes = { "off", "call", "fight", "both" };
            int i = Array.IndexOf(modes, string.IsNullOrEmpty(BaitData.BmMode) ? "off" : BaitData.BmMode);
            BaitData.Cmd("/thrall bloodmoon " + modes[(i + 1) % modes.Length]);
        }, "Your blood moon mode. Call: daycare thralls come to you for the horde. Fight: thralls with you turn aggressive. Both. Back to normal at dawn");
        Hook("prevthrall", () => Step(-1));
        Hook("nextthrall", () => Step(1));
        Hook("back", () => { var wm = xui.playerUI.windowManager; wm.Close("azraelBaitStats"); BaitData.TryOpenRoster(); });
        Hook("close", () => xui.playerUI.windowManager.Close("azraelBaitStats"));
    }

    void Step(int d)
    {
        var l = BaitData.Thralls;
        if (l.Count == 0) return;
        int i = l.FindIndex(t => t.Idx == BaitData.StatsIdx);
        i = ((i < 0 ? 0 : i + d) % l.Count + l.Count) % l.Count;
        BaitData.StatsIdx = l[i].Idx; seen = -1;
    }

    public override void OnOpen() { base.OnOpen(); seen = -1; BaitData.Cmd("/thrall ui", false); RefreshBindings(); }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (seen != BaitData.Version || Time.time > nextRefresh) { seen = BaitData.Version; nextRefresh = Time.time + 0.5f; RefreshBindings(); }
    }

    static string PerkWords(string p)
    {
        if (string.IsNullOrEmpty(p)) return "none yet";
        var l = new List<string>();
        foreach (char c in p) l.Add(c == 'T' ? "tough" : c == 'H' ? "hit" : c == 'R' ? "regen" : c == 'A' ? "taunt" : "?");
        return string.Join(", ", l.ToArray());
    }

    string Body(UiThrall t)
    {
        if (t == null) return BaitData.ServerHasRoles ? "That thrall is gone. Use Prev / Next." : "The thrall page needs Bait " + BaitData.RolesNeed + " or newer on the server.";
        int per = Cfg("KillsPerLevel", 8);
        var sb = new System.Text.StringBuilder();
        sb.Append("Level ").Append(t.Level).Append("     kills ").Append(t.Kills);
        if (per > 0) sb.Append(" (").Append(per - t.Kills % per).Append(" to the next level)");
        sb.Append("     deaths ").Append(t.Deaths).Append('\n');
        if (t.TamedAt > 0)
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(t.TamedAt).ToLocalTime();
            var age = DateTimeOffset.Now - when;
            sb.Append("Tamed ").Append(when.ToString("yyyy-MM-dd")).Append(", yours for ")
              .Append(age.TotalDays >= 1 ? Math.Floor(age.TotalDays) + " days" : age.TotalHours >= 1 ? Math.Floor(age.TotalHours) + " hours" : Math.Max(1, (int)age.TotalMinutes) + " minutes").Append('\n');
        }
        else sb.Append("Tamed before Bait 1.10.0, so there is no date.\n");
        string where = t.DownLeft > 0 ? "downed, back in " + BaitHud.Clock(t.DownLeft)
                     : t.Patrol ? "on patrol (" + t.PatrolPts + " points)"
                     : t.Guard ? "guarding a post (radius " + t.Radius.ToString("0.#", CultureInfo.InvariantCulture) + ")"
                     : t.Tether ? "tethered (radius " + t.Radius.ToString("0.#", CultureInfo.InvariantCulture) + ")"
                     : t.Active ? (t.Stay ? "out, holding position" : "out, following you") : "stowed";
        sb.Append("Now: ").Append(where);
        var p = BaitData.Me;
        if (t.HasPos && p != null) sb.Append(", ").Append(Mathf.RoundToInt((t.Pos - p.position).magnitude)).Append("m away");
        sb.Append('\n');
        var e = BaitData.LiveThrall(t);
        if (e != null) sb.Append("HP ").Append(Mathf.CeilToInt(e.Stats.Health.Value)).Append('/').Append(e.GetMaxHealth()).Append("     ");
        sb.Append("mode ").Append(BaitData.ModeWord(t.Mode)).Append("     auto-revive ").Append(t.AutoRevive ? "on" : "off").Append("     auto-heal ").Append(t.AutoHeal ? "on" : "off").Append('\n');
        int l1 = Cfg("PerkLevel1", 10), l2 = Cfg("PerkLevel2", 25), l3 = Cfg("PerkLevel3", 50);
        sb.Append("Perks: ").Append(PerkWords(t.Perks)).Append("   (").Append(t.Perks.Length).Append(" of ").Append(t.PerkSlots).Append(" slots used");
        int nextSlot = t.Level < l1 ? l1 : t.Level < l2 ? l2 : t.Level < l3 ? l3 : 0;
        if (nextSlot > 0) sb.Append(", next slot at level ").Append(nextSlot);
        sb.Append(")\n");
        int ev = Cfg("EvolveLevel", 50);
        if (t.Cls != null && t.Cls.StartsWith("animal", StringComparison.OrdinalIgnoreCase)) sb.Append("Evolution: animals don't evolve.");
        else if (string.IsNullOrEmpty(t.EvolveTo)) sb.Append("Evolution: no stronger version of this one.");
        else
        {
            string to = Localization.Get(t.EvolveTo); if (string.IsNullOrEmpty(to)) to = t.EvolveTo;
            sb.Append("Evolution: becomes ").Append(to).Append(t.Level >= ev ? ", ready now (" : " at level " + ev + " (").Append(Cfg("EvolveFeastCost", 3)).Append(" Feast bait)");
        }
        return sb.ToString();
    }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        var t = T;
        switch (name)
        {
            case "bgcol": value = UiSettings.BgCol; return true;
            case "textcol": value = UiSettings.TextCol; return true;
            case "stitle": value = t == null ? "Thrall page" : t.Idx + ". " + t.Label + (string.IsNullOrEmpty(t.Name) ? "" : " (" + t.Pretty + ")") + "   Lv " + t.Level; return true;
            case "sbody": value = Body(t); return true;
            case "perkslots": value = t == null ? "" : t.Perks.Length + "/" + t.PerkSlots; return true;
            case "resettxt": value = t != null && t.Perks.Length > 0 ? "Reset perks" : ""; return true;
            case "guardtxt": value = t == null ? "" : t.Guard ? "Stop guarding" : t.Patrol ? "Stop (untether)" : "Guard here"; return true;
            case "patroltxt": value = t == null ? "" : t.Patrol ? "Stop patrol" : t.PatrolPts >= 2 ? "Start patrol" : "Start (" + t.PatrolPts + "/2 pts)"; return true;
            case "evolvetxt": value = t == null || string.IsNullOrEmpty(t.EvolveTo) ? "Evolve (not available)" : "Evolve"; return true;
            case "bmtxt": value = "Blood moon: " + (string.IsNullOrEmpty(BaitData.BmMode) ? "off" : BaitData.BmMode); return true;
            case "status": value = BaitData.StatusText; return true;
        }
        return base.GetBindingValueInternal(ref value, name);
    }
}

// Map and compass markers for your own thralls (green) and the spots their tethers, guard posts and patrols are at (blue).
// The marker classes are made here rather than in nav_objects.xml, because a dedicated server sends its own copy of that
// file to every client and a client-only mod's patch would be lost.
public static class MapMarks
{
    const string ThrallClass = "azrael_thrall", PostClass = "azrael_thrall_post";
    static readonly Dictionary<string, NavObject> marks = new Dictionary<string, NavObject>();
    static float next;

    static void EnsureClass(string name, string sprite, string color)
    {
        if (NavObjectClass.GetNavObjectClass(name) != null) return;
        var x = XElement.Parse("<nav_object_class name='" + name + "'>"
            + "<map_settings><property name='sprite_name' value='" + sprite + "'/><property name='color' value='" + color + "'/><property name='layer' value='5'/><property name='icon_scale' value='0.8'/></map_settings>"
            + "<compass_settings><property name='sprite_name' value='" + sprite + "'/><property name='color' value='" + color + "'/><property name='max_distance' value='150'/>"
            + "<property name='min_icon_scale' value='0.35'/><property name='max_icon_scale' value='0.6'/><property name='max_scale_distance' value='60'/></compass_settings>"
            + "</nav_object_class>");
        NavObjectClassesFromXml.ParseNavObjectClass(x);
    }

    public static bool IsOurs(NavObject n) { return n != null && n.NavObjectClass != null && n.NavObjectClass.NavObjectClassName.StartsWith("azrael_thrall", StringComparison.Ordinal); }

    public static void Clear()
    {
        if (NavObjectManager.HasInstance) foreach (var n in marks.Values) NavObjectManager.Instance.UnRegisterNavObject(n);
        marks.Clear();
    }

    public static void Update()
    {
        if (Time.time < next || !NavObjectManager.HasInstance) return;
        next = Time.time + 1f;
        if (!UiSettings.MapMarkers || !BaitData.HasData) { if (marks.Count > 0) Clear(); return; }
        try
        {
            EnsureClass(ThrallClass, "ui_game_symbol_allies", "120,230,120,255");
            EnsureClass(PostClass, "ui_game_symbol_map_campsite", "110,190,255,255");
        }
        catch (Exception e) { Log.Warning("[BaitUI] map markers: " + e.Message); UiSettings.MapMarkers = false; return; }
        var want = new HashSet<string>();
        foreach (var t in BaitData.Thralls)
        {
            var e = BaitData.LiveThrall(t);
            if (e != null)
            {
                string k = "e" + e.entityId;
                want.Add(k);
                NavObject n;
                if (!marks.TryGetValue(k, out n) || !NavObjectManager.Instance.NavObjectList.Contains(n)) { n = NavObjectManager.Instance.RegisterNavObject(ThrallClass, e); marks[k] = n; }
                if (n != null) n.name = t.Label;
            }
            if (t.HasPos && (t.Tether || t.Guard || t.Patrol))
            {
                string k = "p" + t.Idx + "|" + t.Pos.x.ToString("0") + "," + t.Pos.z.ToString("0");
                want.Add(k);
                NavObject n;
                if (!marks.TryGetValue(k, out n) || !NavObjectManager.Instance.NavObjectList.Contains(n)) { n = NavObjectManager.Instance.RegisterNavObject(PostClass, t.Pos); marks[k] = n; }
                if (n != null) n.name = t.Label + (t.Patrol ? " (patrol)" : t.Guard ? " (guard post)" : " (daycare)");
            }
        }
        foreach (var k in new List<string>(marks.Keys))
        {
            if (want.Contains(k)) continue;
            NavObjectManager.Instance.UnRegisterNavObject(marks[k]);
            marks.Remove(k);
        }
    }
}
