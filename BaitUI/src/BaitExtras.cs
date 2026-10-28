using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Bait UI 1.9.0: the session summary, tracking, the tame banner, the new thrall setup popup, the bestiary and the bait crafting shortcut.

// What happened since you joined (U57). Written to AzraelBaitUI-sessions.txt (in the game's user data folder) when you leave.
public static class Session
{
    public static int Kills, LevelUps, Deaths, Tamed;
    public static int UsedPlain, UsedRich, UsedFeast;
    public static float Start = -1f;
    static int lastPlain = -1, lastRich = -1, lastFeast = -1;

    static bool Empty { get { return Kills + LevelUps + Deaths + Tamed + UsedPlain + UsedRich + UsedFeast == 0; } }

    // the server's bait counts: a drop since the last copy is bait used (picking some up only raises them)
    public static void Bait(int plain, int rich, int feast, bool counts)
    {
        if (!counts) return;
        if (Start < 0f) Start = Time.time;
        if (lastPlain >= 0) { UsedPlain += Math.Max(0, lastPlain - plain); UsedRich += Math.Max(0, lastRich - rich); UsedFeast += Math.Max(0, lastFeast - feast); }
        lastPlain = plain; lastRich = rich; lastFeast = feast;
    }

    public static string Length()
    {
        if (Start < 0f) Start = Time.time;
        var span = TimeSpan.FromSeconds(Time.time - Start);
        return span.TotalHours >= 1 ? (int)span.TotalHours + "h " + span.Minutes + "m" : Math.Max(0, span.Minutes) + "m";
    }

    public static string Text()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Playing for ").Append(Length()).Append("\n\n");
        sb.Append(L.T("Kills")).Append(": ").Append(Kills).Append('\n');
        sb.Append("Tamed: ").Append(Tamed).Append('\n');
        sb.Append("Level ups: ").Append(LevelUps).Append('\n');
        sb.Append("Deaths: ").Append(Deaths).Append('\n');
        sb.Append("Bait used: ").Append(UsedPlain).Append(" plain, ").Append(UsedRich).Append(" rich, ").Append(UsedFeast).Append(" feast");
        if (!BaitData.BaitCounts) sb.Append("  (the server doesn't send bait counts)");
        sb.Append("\n\n");
        UiThrall best = null;
        foreach (var t in BaitData.Thralls) if (best == null || t.Level > best.Level) best = t;
        if (best != null) sb.Append("Strongest: ").Append(best.Label).Append(", level ").Append(best.Level).Append(", ").Append(best.Kills).Append(" kills\n");
        sb.Append("Roster: ").Append(BaitData.Thralls.Count).Append(" thralls, ").Append(BaitData.ActiveCount).Append(" out");
        return sb.ToString();
    }

    // Writes the session to the log file (when there is anything to write), then starts a new one.
    public static void Clear()
    {
        if (!Empty)
        {
            try
            {
                string p = Path.Combine(GameIO.GetUserGameDataDir(), "AzraelBaitUI-sessions.txt");
                File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "  " + Length() + "  kills " + Kills + ", tamed " + Tamed + ", level ups " + LevelUps
                                     + ", deaths " + Deaths + ", bait " + UsedPlain + "/" + UsedRich + "/" + UsedFeast + "\n");
            }
            catch (Exception e) { Log.Warning("[BaitUI] session log: " + e.Message); }
        }
        Kills = LevelUps = Deaths = Tamed = UsedPlain = UsedRich = UsedFeast = 0;
        lastPlain = lastRich = lastFeast = -1;
        Start = -1f;
    }
}

public static class Extras
{
    public static int TrackIdx = -1;   // the thrall with a compass marker at any distance (U52)
    public static string Banner;       // U48
    public static float BannerAt = -100f;
    public static int SetupIdx = -1;   // a thrall waiting for the setup popup (U42)
    static float craftUntil;

    public static void ToggleTrack(UiThrall t)
    {
        TrackIdx = TrackIdx == t.Idx ? -1 : t.Idx;
        BaitData.SetStatus(TrackIdx < 0 ? "Not tracking." : "Tracking " + t.Label + " on the compass.");
    }

    public static void OnTamed(UiThrall n)
    {
        if (UiSettings.TameBanner)
        {
            Banner = L.T("Tamed") + ": " + n.Pretty + (n.Alpha ? " (Alpha!)" : "") + (n.Level > 0 ? "   Lv " + n.Level : "");
            BannerAt = Time.time;
            try { Audio.Manager.PlayInsidePlayerHead("ui_skill_purchase"); } catch { }
        }
        if (UiSettings.SetupPopup) SetupIdx = n.Idx;
    }

    // Called every frame from the HUD: opens the setup popup when nothing else is on screen, and finishes the crafting shortcut.
    public static void Tick(LocalPlayerUI ui)
    {
        var wm = ui.windowManager;
        if (SetupIdx >= 0 && !wm.IsCursorWindowOpen() && !wm.IsModalWindowOpen() && !wm.IsInputActive())
        {
            if (BaitData.Thralls.Exists(t => t.Idx == SetupIdx)) wm.Open("azraelBaitSetup", true);
            else SetupIdx = -1;
        }
        if (craftUntil > 0f)
        {
            if (Time.time > craftUntil) { craftUntil = 0f; return; }
            var list = ui.xui.GetChildByType<XUiC_RecipeList>();
            if (list == null || list.txtInput == null || !list.txtInput.ViewComponent.IsVisible) return;
            list.txtInput.Text = "bait";
            list.RefreshRecipes();
            craftUntil = 0f;
        }
    }

    // U58: crafting, filtered to the bait recipes.
    public static void OpenBaitCrafting(XUi xui)
    {
        var p = BaitData.Me;
        if (p == null) return;
        XUiC_WindowSelector.OpenSelectorAndWindow(p, "crafting");
        craftUntil = Time.time + 2f;
    }

    // How many of each bait the inventory has the ingredients for, e.g. "8 / 2 / 0".
    static float craftAt = -10f;
    static string craftText = "";
    public static string CraftableText()
    {
        if (Time.time - craftAt < 2f) return craftText;
        craftAt = Time.time;
        var p = BaitData.Me;
        if (p == null) return craftText = "?";
        var parts = new List<string>();
        foreach (string r in new[] { "azraelBait", "azraelBaitRich", "azraelBaitFeast" }) parts.Add(Craftable(p, r).ToString());
        return craftText = string.Join(" / ", parts.ToArray());
    }

    static int Craftable(EntityPlayerLocal p, string name)
    {
        try
        {
            var rec = CraftingManager.GetRecipe(name);
            if (rec == null || rec.ingredients == null || rec.ingredients.Count == 0) return 0;
            int best = int.MaxValue;
            foreach (var ing in rec.ingredients)
            {
                if (ing == null || ing.count <= 0) continue;
                int have = p.bag.GetItemCount(ing.itemValue) + p.inventory.GetItemCount(ing.itemValue);
                best = Math.Min(best, have / ing.count);
            }
            return best == int.MaxValue ? 0 : best * Math.Max(1, rec.count);
        }
        catch { return 0; }
    }

    // U39: the roster and thrall page windows scale with the Window size setting.
    public static void ApplyScale(XUiView v)
    {
        try { if (v != null && v.UiTransform != null) v.UiTransform.localScale = Vector3.one * Mathf.Clamp(UiSettings.WinScale, 0.6f, 1.4f); }
        catch (Exception e) { Log.Warning("[BaitUI] window size: " + e.Message); }
    }

    public static int Cfg(string key, int def)
    {
        foreach (var c in BaitData.Config) { int v; if (c[0] == key && int.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v; }
        return def;
    }

    // Meals a level 0 creature of this class needs, from the server's settings (the same sums the server does).
    public static int MealsFor(string cls)
    {
        int need;
        if (cls.StartsWith("animal", StringComparison.OrdinalIgnoreCase))
        {
            if (!BaitData.AnimalFeeds.TryGetValue(cls, out need)) need = Cfg("AnimalFeedsToTame", 3);
        }
        else
        {
            string c = cls.ToLowerInvariant();
            int bonus = c.EndsWith("infernal") || c.EndsWith("charged") ? Cfg("ChargedBonus", 6) : c.EndsWith("radiated") ? Cfg("RadiatedBonus", 4) : c.EndsWith("feral") ? Cfg("FeralBonus", 2) : 0;
            need = Cfg("FeedsToTame", 4) + bonus;
        }
        int max = Cfg("MaxFeeds", 27);
        return max > 0 ? Math.Min(need, max) : need;
    }
}

// U42: a new thrall's name, mode and auto boxes in one go. Referenced as controller="AzraelBaitSetup, AzraelBaitUI".
public class XUiC_AzraelBaitSetup : XUiController
{
    int mode;
    bool heal, rev;
    XUiC_TextInput name;

    static UiThrall T { get { return BaitData.Thralls.Find(t => t.Idx == Extras.SetupIdx); } }

    void Hook(string id, Action a)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] setup window: no button " + id); return; }
        c.OnPress += (s, b) => { try { a(); RefreshBindings(); } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    void Done() { Extras.SetupIdx = -1; xui.playerUI.windowManager.Close("azraelBaitSetup"); }

    void Ok()
    {
        var t = T;
        if (t != null)
        {
            string n = (name != null ? name.Text ?? "" : "").Replace("|", " ").Replace("~", " ").Trim();
            if (n.Length > 0) BaitData.Cmd("/thrall name " + t.Idx + " " + n);
            if (mode != t.Mode) BaitData.Cmd("/thrall mode " + t.Idx + " " + BaitData.ModeWord(mode));
            if (heal != t.AutoHeal && BaitData.ServerHasQol) BaitData.Cmd("/thrall autoheal " + t.Idx + (heal ? " on" : " off"));
            if (rev != t.AutoRevive) BaitData.Cmd("/thrall autorevive " + t.Idx + (rev ? " on" : " off"));
        }
        Done();
    }

    public override void Init()
    {
        base.Init();
        name = GetChildById("suname") as XUiC_TextInput;
        if (name != null) name.OnSubmitHandler += (s, t) => Ok();
        Hook("sumode", () => mode = (mode + 1) % 4);
        Hook("suheal", () => heal = !heal);
        Hook("surev", () => rev = !rev);
        Hook("suskip", Done);
        Hook("sunever", () => { UiSettings.SetupPopup = false; UiSettings.Save(); Done(); });
        Hook("suok", Ok);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        var t = T;
        mode = t != null ? t.Mode : 0; heal = t != null && t.AutoHeal; rev = t != null && t.AutoRevive;
        if (name != null) name.Text = "";
        RefreshBindings();
    }

    public override bool GetBindingValueInternal(ref string value, string n)
    {
        var t = T;
        switch (n)
        {
            case "bgcol": value = UiSettings.BgCol; return true;
            case "textcol": value = UiSettings.TextCol; return true;
            case "sutitle": value = t == null ? L.T("New thrall") : L.T("New thrall") + ": " + t.Idx + ". " + t.Pretty + (t.Alpha ? " (Alpha)" : ""); return true;
            case "sunamelbl": value = L.T("Name"); return true;
            case "sumodetxt": value = L.T("Mode") + ": " + BaitData.ModeWord(mode); return true;
            case "suhealtxt": value = BaitData.ServerHasQol ? (heal ? "[X] " : "[  ] ") + L.T("Auto-heal") : ""; return true;
            case "surevtxt": value = (rev ? "[X] " : "[  ] ") + L.T("Auto-revive"); return true;
            case "suskiptxt": value = L.T("Skip"); return true;
            case "sunevertxt": value = L.T("Don't ask again"); return true;
            case "suoktxt": value = L.T("Done"); return true;
        }
        return base.GetBindingValueInternal(ref value, n);
    }
}

// U43: every creature you can tame, the meals it needs, and how many you have. Referenced as controller="AzraelBaitBestiary, AzraelBaitUI".
public class XUiC_AzraelBaitBestiary : XUiController
{
    const int Rows = 12;
    static readonly string[] Kinds = { "All", "Zombies", "Animals" };
    int page, kind, seen = -1;
    bool onlyTamed;
    string search = "";
    List<string> view = new List<string>();

    void Hook(string id, Action a)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] bestiary: no button " + id); return; }
        c.OnPress += (s, b) => { try { a(); seen = -1; } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    public override void Init()
    {
        base.Init();
        Hook("prev", () => { if (page > 0) page--; });
        Hook("next", () => { if ((page + 1) * Rows < view.Count) page++; });
        Hook("kind", () => { kind = (kind + 1) % Kinds.Length; page = 0; });
        Hook("onlytamed", () => { onlyTamed = !onlyTamed; page = 0; });
        Hook("back", () => { var wm = xui.playerUI.windowManager; wm.Close("azraelBaitBestiary"); BaitData.TryOpenRoster(); });
        Hook("close", () => xui.playerUI.windowManager.Close("azraelBaitBestiary"));
        var s = GetChildById("bsearch") as XUiC_TextInput;
        if (s != null) s.OnChangeHandler += (x, text, fromCode) => { search = (text ?? "").Trim(); page = 0; seen = -1; };
    }

    static int Owned(string cls) { int n = 0; foreach (var t in BaitData.Thralls) if (string.Equals(t.Cls, cls, StringComparison.OrdinalIgnoreCase)) n++; return n; }

    void Build()
    {
        view.Clear();
        foreach (string c in XUiC_AzraelBaitAdmin.Classes())
        {
            bool animal = c.StartsWith("animal", StringComparison.OrdinalIgnoreCase);
            if (kind == 1 && animal || kind == 2 && !animal) continue;
            if (onlyTamed && Owned(c) == 0) continue;
            if (search.Length > 0 && c.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && XUiC_AzraelBaitAdmin.Pretty(c).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            view.Add(c);
        }
        int pages = Math.Max(1, (view.Count + Rows - 1) / Rows);
        if (page >= pages) page = pages - 1;
    }

    public override void OnOpen() { base.OnOpen(); page = 0; seen = -1; BaitData.Cmd("/thrall ui", false); Build(); RefreshBindings(); }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (seen != BaitData.Version) { seen = BaitData.Version; Build(); RefreshBindings(); }
    }

    string At(int row) { int i = page * Rows + row - 1; return i >= 0 && i < view.Count ? view[i] : null; }

    string RowText(string c)
    {
        int owned = Owned(c), cap; BaitData.Caps.TryGetValue(c, out cap);
        bool ex = BaitData.Exclude.Contains(c);
        string meals = ex ? "can't be tamed here" : Extras.MealsFor(c) + " meals" + (Extras.Cfg("FeedsPerLevelStep", 0) > 0 ? " (+1 per " + Extras.Cfg("FeedsPerLevelStep", 0) + " levels)" : "");
        return XUiC_AzraelBaitAdmin.Pretty(c) + "   " + meals + (owned > 0 ? "   you have " + owned : "") + (cap > 0 ? "   (cap " + cap + ")" : "");
    }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        switch (name)
        {
            case "bgcol": value = UiSettings.BgCol; return true;
            case "textcol": value = UiSettings.TextCol; return true;
            case "btitle":
            {
                var tamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in BaitData.Thralls) tamed.Add(t.Cls);
                value = L.T("Bestiary") + "   " + tamed.Count + " of " + XUiC_AzraelBaitAdmin.Classes().Count + " kinds tamed";
                return true;
            }
            case "pagetext": value = (page + 1) + "/" + Math.Max(1, (view.Count + Rows - 1) / Rows); return true;
            case "kindtxt": value = L.T("Show") + ": " + L.T(Kinds[kind]); return true;
            case "onlytxt": value = (onlyTamed ? "[X] " : "[  ] ") + L.T("Only ones I have"); return true;
            case "bfoot": value = "Meals count plain bait as 1, rich as " + Extras.Cfg("RichBaitMeals", 2) + " and feast as " + Extras.Cfg("FeastBaitMeals", 4) + ". Green: you have one. Grey: not allowed on this server."; return true;
            case "rostertxt": value = L.T("Roster"); return true;
            case "closetxt": value = L.T("Close"); return true;
        }
        if (name.Length > 2 && name[0] == 'b' && char.IsDigit(name[1]))
        {
            int k = 1;
            while (k < name.Length && char.IsDigit(name[k])) k++;
            string c = At(int.Parse(name.Substring(1, k - 1)));
            switch (name.Substring(k))
            {
                case "vis": value = c != null ? "true" : "false"; return true;
                case "text": value = c == null ? "" : RowText(c); return true;
                case "col": value = c == null ? "[darkGrey]" : Owned(c) > 0 ? "40,70,40,220" : "[darkGrey]"; return true;
                case "tcol": value = c != null && BaitData.Exclude.Contains(c) ? "150,150,150,255" : Owned(c ?? "") > 0 ? "170,255,170,255" : UiSettings.TextCol; return true;
            }
        }
        return base.GetBindingValueInternal(ref value, name);
    }
}

// U57: the session summary window. Referenced as controller="AzraelBaitSummary, AzraelBaitUI".
public class XUiC_AzraelBaitSummary : XUiController
{
    float next;

    void Hook(string id, Action a)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] summary: no button " + id); return; }
        c.OnPress += (s, b) => { try { a(); RefreshBindings(); } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    public override void Init()
    {
        base.Init();
        Hook("mreset", () => { Session.Clear(); BaitData.SetStatus("Session saved to the log and started again."); });
        Hook("back", () => { var wm = xui.playerUI.windowManager; wm.Close("azraelBaitSummary"); BaitData.TryOpenRoster(); });
        Hook("close", () => xui.playerUI.windowManager.Close("azraelBaitSummary"));
    }

    public override void OnOpen() { base.OnOpen(); RefreshBindings(); }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (Time.time > next) { next = Time.time + 1f; RefreshBindings(); }
    }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        switch (name)
        {
            case "bgcol": value = UiSettings.BgCol; return true;
            case "textcol": value = UiSettings.TextCol; return true;
            case "mtitle": value = L.T("Session summary"); return true;
            case "mbody": value = Session.Text(); return true;
            case "mresettxt": value = L.T("Start again"); return true;
            case "rostertxt": value = L.T("Roster"); return true;
            case "closetxt": value = L.T("Close"); return true;
        }
        return base.GetBindingValueInternal(ref value, name);
    }
}
