using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// The roster window: one row per thrall with the buttons the /thrall commands give you.
// Referenced from XUi as controller="AzraelBaitRoster, AzraelBaitUI". There is one window per page size (8, 12, 16 rows);
// the controller counts its rows.
public class XUiC_AzraelBaitRoster : XUiController
{
    int Rows = 8;
    int page, seen = -1, relRow = -1, selIdx = -1;
    float relAt, nextRefresh;
    Vector2i homePos;
    Vector2 dragAcc;
    List<UiThrall> view = new List<UiThrall>();

    void Hook(string id, Action a)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] roster window: no button " + id); return; }
        c.OnPress += (s, b) => { try { a(); } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    // buttons that need Bait 1.9.0 on the server do nothing (and show blank) on an older one
    void HookQol(string id, Action a) { Hook(id, () => { if (BaitData.ServerHasQol) a(); }); }

    static void CloseAll(XUiController c) { var wm = c.xui.playerUI.windowManager; foreach (string g in BaitData.RosterGroups) wm.Close(g); }

    public override void Init()
    {
        base.Init();
        Rows = 0;
        while (GetChildById("row" + (Rows + 1)) != null) Rows++;
        if (Rows == 0) Rows = 8;
        homePos = ViewComponent.Position;
        for (int i = 1; i <= Rows; i++)
        {
            int row = i;
            Hook("r" + i + "sel", () => { var t = At(row); if (t != null) { selIdx = selIdx == t.Idx ? -1 : t.Idx; seen = -1; } });
            Hook("r" + i + "call", () => { var t = At(row); if (t != null) BaitData.Cmd(t.Active ? "/thrall stow " + t.Idx : "/thrall call " + t.Idx); });
            Hook("r" + i + "stay", () => { var t = At(row); if (t != null) BaitData.Cmd((t.Stay ? "/thrall follow " : "/thrall stay ") + t.Idx); });
            Hook("r" + i + "mode", () => { var t = At(row); if (t != null) BaitData.Cmd("/thrall mode " + t.Idx + " " + BaitData.ModeWord((t.Mode + 1) % 4)); });
            Hook("r" + i + "teth", () => { var t = At(row); if (t != null) BaitData.Cmd((t.Tether ? "/thrall untether " : "/thrall tether ") + t.Idx); });
            Hook("r" + i + "heal", () => { var t = At(row); if (t != null) BaitData.Cmd((t.DownLeft > 0 ? "/thrall revive " : "/thrall heal ") + t.Idx); });
            Hook("r" + i + "auto", () => { var t = At(row); if (t != null && BaitData.ServerHasAutoRevive) BaitData.Cmd("/thrall autorevive " + t.Idx + (t.AutoRevive ? " off" : " on")); });
            HookQol("r" + i + "hbox", () => { var t = At(row); if (t != null) BaitData.Cmd("/thrall autoheal " + t.Idx + (t.AutoHeal ? " off" : " on")); });
            HookQol("r" + i + "up", () => { var t = At(row); if (t != null && t.Idx > 1) BaitData.Cmd("/thrall move " + t.Idx + " up"); });
            HookQol("r" + i + "down", () => { var t = At(row); if (t != null && t.Idx < BaitData.Thralls.Count) BaitData.Cmd("/thrall move " + t.Idx + " down"); });
            Hook("r" + i + "name", () =>
            {
                var t = At(row);
                if (t == null) return;
                BaitData.RenameIdx = t.Idx; BaitData.RenameFrom = t.Label;
                CloseAll(this); xui.playerUI.windowManager.Open("azraelBaitName", true);
            });
            Hook("r" + i + "rel", () =>
            {
                var t = At(row);
                if (t == null) return;
                if (relRow == row && Time.time - relAt < 4f) { relRow = -1; BaitData.Cmd("/thrall release " + t.Idx); }
                else { relRow = row; relAt = Time.time; }
                seen = -1;
            });
        }
        Hook("prev", () => { if (page > 0) page--; seen = -1; });
        Hook("next", () => { if ((page + 1) * Rows < view.Count) page++; seen = -1; });
        Hook("filter", () => { UiSettings.Filter = (UiSettings.Filter + 1) % UiSettings.FilterNames.Length; page = 0; UiSettings.Save(); seen = -1; });
        Hook("sort", () => { UiSettings.Sort = (UiSettings.Sort + 1) % UiSettings.SortNames.Length; UiSettings.Save(); seen = -1; });
        Hook("pagesize", () =>
        {
            UiSettings.RowsPerPage = UiSettings.RowsPerPage >= 16 ? 8 : UiSettings.RowsPerPage >= 12 ? 16 : 12;
            UiSettings.Save();
            CloseAll(this);
            BaitData.TryOpenRoster();
        });
        Hook("stowall", () => BaitData.Cmd("/thrall stow all"));
        Hook("stowactive", () => BaitData.Cmd("/thrall stow active"));
        Hook("stowtethered", () => BaitData.Cmd("/thrall stow tethered"));
        Hook("followall", () => BaitData.Cmd("/thrall follow all"));
        Hook("stayall", () => BaitData.Cmd("/thrall stay all"));
        HookQol("allhere", () => BaitData.Cmd("/thrall here"));
        HookQol("deaths", () => { BaitData.Cmd("/thrall deaths", false); BaitData.SetStatus("The last deaths are listed in chat."); });
        Hook("resetpos", () => { UiSettings.RosterDX = 0; UiSettings.RosterDY = 0; UiSettings.Save(); ApplyPos(); });
        Hook("admin", () => { CloseAll(this); xui.playerUI.windowManager.Open("azraelBaitAdmin", true); });
        Hook("close", () => CloseAll(this));
        Hook("tgltags", () => { UiSettings.Nameplates = !UiSettings.Nameplates; UiSettings.Save(); seen = -1; });
        Hook("tglparty", () => { UiSettings.Party = !UiSettings.Party; UiSettings.Save(); seen = -1; });
        Hook("tglfed", () => { UiSettings.Feed = !UiSettings.Feed; UiSettings.Save(); seen = -1; });
        Hook("tglpbars", () => { UiSettings.PartyBars = !UiSettings.PartyBars; UiSettings.Save(); seen = -1; });
        Hook("tglkills", () => { UiSettings.KillFeed = !UiSettings.KillFeed; UiSettings.Save(); seen = -1; });
        Hook("tglglow", () => { UiSettings.LevelGlow = !UiSettings.LevelGlow; UiSettings.Save(); seen = -1; });
        Hook("tgllowhp", () => { UiSettings.LowHpAlert = !UiSettings.LowHpAlert; UiSettings.Save(); seen = -1; });
        Hook("tgllowsnd", () => { UiSettings.LowHpSound = !UiSettings.LowHpSound; UiSettings.Save(); seen = -1; });
        Hook("tgldowned", () => { UiSettings.DownedTags = !UiSettings.DownedTags; UiSettings.Save(); seen = -1; });
        Hook("cyclecolor", () => { UiSettings.CycleName(); seen = -1; });
        Hook("pickkey", () => { UiSettings.CaptureGroup = 0; UiSettings.CaptureWheel = false; UiSettings.CaptureKey = !UiSettings.CaptureKey; seen = -1; });
        Hook("pickgroup", () => { UiSettings.CaptureKey = false; UiSettings.CaptureWheel = false; UiSettings.CaptureGroup = UiSettings.CaptureGroup > 0 ? 0 : 1; seen = -1; });
        Hook("pickwheel", () => { UiSettings.CaptureKey = false; UiSettings.CaptureGroup = 0; UiSettings.CaptureWheel = !UiSettings.CaptureWheel; seen = -1; });
        Hook("cycletarget", () => { UiSettings.NextTarget(); seen = -1; });
        Hook("cyclebar", () => { UiSettings.CycleBar(); seen = -1; });
        Hook("sizedown", () => { UiSettings.Scale = Mathf.Clamp(Mathf.Round((UiSettings.Scale - 0.2f) * 10f) / 10f, 0.5f, 4f); UiSettings.Save(); seen = -1; });
        Hook("sizeup", () => { UiSettings.Scale = Mathf.Clamp(Mathf.Round((UiSettings.Scale + 0.2f) * 10f) / 10f, 0.5f, 4f); UiSettings.Save(); seen = -1; });
        // teams
        Hook("teamcycle", () => { if (BaitData.Teams.Count > 0) BaitData.TeamSel = (BaitData.TeamSel + 1) % BaitData.Teams.Count; seen = -1; });
        HookQol("teamcall", () => { var tn = TeamName(); if (tn != null) BaitData.Cmd("/thrall team call " + tn); });
        HookQol("teamsave", () => { BaitData.Cmd("/thrall team save " + (TeamName() ?? NewTeamName()) + " active"); });
        HookQol("teamnew", () => { string n = NewTeamName(); BaitData.Cmd("/thrall team save " + n + " active"); BaitData.TeamSel = BaitData.Teams.Count; });
        // drag the title bar to move the window; the spot is remembered
        var drag = GetChildById("dragbar");
        if (drag != null) drag.OnDrag += (s, type, delta) =>
        {
            try
            {
                if (type == EDragType.DragStart) dragAcc = Vector2.zero;
                float k = 1080f / Mathf.Max(1, Screen.height);   // screen pixels to XUi units
                dragAcc += delta * k;
                int dx = Mathf.RoundToInt(dragAcc.x), dy = Mathf.RoundToInt(dragAcc.y);
                if (dx == 0 && dy == 0 && type != EDragType.DragEnd) return;
                dragAcc -= new Vector2(dx, dy);
                UiSettings.RosterDX = Mathf.Clamp(UiSettings.RosterDX + dx, -900, 900);
                UiSettings.RosterDY = Mathf.Clamp(UiSettings.RosterDY + dy, -500, 500);
                ApplyPos();
                if (type == EDragType.DragEnd) UiSettings.Save();
            }
            catch (Exception e) { Log.Warning("[BaitUI] drag: " + e.Message); }
        };
    }

    void ApplyPos() { ViewComponent.Position = new Vector2i(homePos.x + UiSettings.RosterDX, homePos.y + UiSettings.RosterDY); }

    string TeamName()
    {
        if (BaitData.Teams.Count == 0) return null;
        if (BaitData.TeamSel >= BaitData.Teams.Count) BaitData.TeamSel = 0;
        return BaitData.Teams[BaitData.TeamSel].Key;
    }
    static string NewTeamName()
    {
        for (int n = 1; ; n++)
        {
            string s = "team" + n;
            if (!BaitData.Teams.Exists(t => string.Equals(t.Key, s, StringComparison.OrdinalIgnoreCase))) return s;
        }
    }

    UiThrall At(int row)
    {
        int i = page * Rows + row - 1;
        return i >= 0 && i < view.Count ? view[i] : null;
    }

    UiThrall SelectedThrall { get { return selIdx < 0 ? null : BaitData.Thralls.Find(t => t.Idx == selIdx); } }

    public override void OnClose()
    {
        base.OnClose();
        UiSettings.CaptureKey = false; UiSettings.CaptureGroup = 0; UiSettings.CaptureWheel = false;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        UiSettings.CaptureKey = false; UiSettings.CaptureGroup = 0; UiSettings.CaptureWheel = false;
        page = 0; relRow = -1; seen = -1;
        ApplyPos();
        BaitData.Cmd("/thrall ui", false);   // ask for a fresh copy of everything
        view = BaitData.View();
        RefreshBindings();
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (relRow >= 0 && Time.time - relAt >= 4f) { relRow = -1; seen = -1; }
        if (seen != BaitData.Version || Time.time > nextRefresh)
        {
            seen = BaitData.Version;
            nextRefresh = Time.time + 0.4f;
            view = BaitData.View();
            int pages = Math.Max(1, (view.Count + Rows - 1) / Rows);
            if (page >= pages) page = pages - 1;
            RefreshBindings();
        }
    }

    static string Hp(UiThrall t)
    {
        if (t.DownLeft > 0) return "downed " + t.DownLeft + "s";
        var e = BaitData.LiveThrall(t);
        if (e == null) return t.Active ? "away" : "";
        return Mathf.CeilToInt(e.Stats.Health.Value) + "/" + e.GetMaxHealth();
    }

    static string OnOff(bool b) { return b ? "on" : "off"; }
    static string Box(bool b) { return b ? "[X]" : "[  ]"; }

    static string Details(UiThrall t)
    {
        if (t == null) return "";
        int per = 0;
        foreach (var c in BaitData.Config) if (c[0] == "KillsPerLevel") int.TryParse(c[1], out per);
        string next = per > 0 ? "   " + (per - t.Kills % per) + " to the next level" : "";
        string where = t.DownLeft > 0 ? "downed, back in " + BaitHud.Clock(t.DownLeft) : t.Tether ? "tethered (radius " + t.Radius.ToString("0.#", CultureInfo.InvariantCulture) + ")" : t.Active ? (t.Stay ? "out, holding position" : "out, following you") : "stowed";
        var e = BaitData.LiveThrall(t);
        string hp = e != null ? "   HP " + Mathf.CeilToInt(e.Stats.Health.Value) + "/" + e.GetMaxHealth() : "";
        return t.Idx + ". " + t.Label + (string.IsNullOrEmpty(t.Name) ? "" : " (" + t.Pretty + ")") + "   level " + t.Level + "   kills " + t.Kills + next + hp + "\n"
             + where + "   mode " + BaitData.ModeWord(t.Mode) + "   auto-revive " + OnOff(t.AutoRevive) + (BaitData.ServerHasQol ? "   auto-heal " + OnOff(t.AutoHeal) : "") + "   (click the row again to close)";
    }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        switch (name)
        {
            case "title":
                value = BaitData.HasData ? (BaitData.ServerTooOld ? "Thralls   (server Bait " + BaitData.ServerVersion + " is too old: this UI needs " + BaitData.NeedsServer + " or newer)   " : "Thralls   ") + BaitData.ActiveCount + "/" + BaitData.MaxActive + " active   " + BaitData.Thralls.Count + "/" + BaitData.MaxOwned + " held   power " + BaitData.Power
                          + (BaitData.BaitCounts ? "   bait " + BaitData.Plain + " / " + BaitData.Rich + " rich / " + BaitData.Feast + " feast" : "")
                          + (string.IsNullOrEmpty(BaitData.ServerVersion) ? "" : "   Bait " + BaitData.ServerVersion)
                                         : BaitData.HelloGaveUp ? "Thralls   (no answer from the server: it needs the Zombie and Animal Bait mod, 1.3 or newer)" : "Thralls   (waiting for the server...)";
                return true;
            case "status": value = BaitData.StatusText; return true;
            case "details": value = Details(SelectedThrall); return true;
            case "pagetext": value = "Page " + (page + 1) + "/" + Math.Max(1, (view.Count + Rows - 1) / Rows); return true;
            case "filtertxt": value = "Show: " + UiSettings.FilterNames[UiSettings.Filter]; return true;
            case "sorttxt": value = "Sort: " + UiSettings.SortNames[UiSettings.Sort]; return true;
            case "pagesizetxt": value = Rows + " per page"; return true;
            case "allheretxt": value = BaitData.ServerHasQol ? "Bring all here" : ""; return true;
            case "deathstxt": value = BaitData.ServerHasQol ? "Deaths" : ""; return true;
            case "pbarstxt": value = BaitData.HasData && !BaitData.ServerHasPartyBars ? "Party bars: server needs " + BaitData.PartyBarsNeed + "+" : "Party members' bars: " + OnOff(UiSettings.PartyBars); return true;
            case "colortxt": value = "Name color: " + UiSettings.NameLabel(); return true;
            case "bartxt": value = "Bar color: " + UiSettings.BarLabel(); return true;
            case "targettxt": { var tt = UiSettings.TargetThrall; value = "Colors for: " + (tt == null ? "all thralls" : tt.Idx + ". " + tt.Label) + " (click)"; return true; }
            case "keytxt": value = UiSettings.CaptureKey ? "Press a key..." : "Hotkey: " + UiSettings.KeyText(UiSettings.Key); return true;
            case "grouptxt":
                value = UiSettings.CaptureGroup > 0 ? "Press a key for " + UiSettings.GroupNames[UiSettings.CaptureGroup] + " (Del = none)"
                      : "Keys: follow " + UiSettings.KeyText(UiSettings.FollowKey) + ", stay " + UiSettings.KeyText(UiSettings.StayKey) + ", here " + UiSettings.KeyText(UiSettings.HereKey);
                return true;
            case "wheeltxt": value = UiSettings.CaptureWheel ? "Press a key..." : "Wheel: " + UiSettings.KeyText(UiSettings.WheelKey); return true;
            case "tagstxt": value = "Name tags: " + OnOff(UiSettings.Nameplates); return true;
            case "partytxt": value = "Party list: " + OnOff(UiSettings.Party); return true;
            case "fedtxt": value = "Fed tags: " + OnOff(UiSettings.Feed); return true;
            case "killstxt": value = "Kill feed: " + OnOff(UiSettings.KillFeed); return true;
            case "glowtxt": value = "Level glow: " + OnOff(UiSettings.LevelGlow); return true;
            case "lowhptxt": value = "Low HP flash: " + OnOff(UiSettings.LowHpAlert); return true;
            case "lowsndtxt": value = "Low HP sound: " + OnOff(UiSettings.LowHpSound); return true;
            case "downedtxt": value = "Downed tags: " + OnOff(UiSettings.DownedTags); return true;
            case "teamtxt":
            {
                if (!BaitData.ServerHasQol) { value = BaitData.HasData ? "Teams need Bait " + BaitData.QolNeed + "+" : ""; return true; }
                var tn = TeamName();
                value = tn == null ? "Team: none yet" : "Team: " + tn + " (" + BaitData.Teams[BaitData.TeamSel].Value + ")" + (BaitData.Teams.Count > 1 ? "  >" : "");
                return true;
            }
            case "teamcalltxt": value = BaitData.ServerHasQol && TeamName() != null ? "Call team" : ""; return true;
            case "teamsavetxt": value = BaitData.ServerHasQol ? (TeamName() != null ? "Save active" : "") : ""; return true;
            case "teamnewtxt": value = BaitData.ServerHasQol ? "New team" : ""; return true;
            case "sizetxt": value = "Size " + UiSettings.Scale.ToString("0.0", CultureInfo.InvariantCulture); return true;
            case "adminvis": value = BaitData.Admin ? "true" : "false"; return true;
            case "emptytext":
                value = BaitData.HasData ? (BaitData.Thralls.Count == 0 ? "You have no thralls yet. Throw bait at a zombie or animal and let it eat."
                                          : view.Count == 0 ? "No " + UiSettings.FilterNames[UiSettings.Filter].ToLowerInvariant() + " on your roster. Click Show to see the rest." : "")
                                         : "No data from the server yet. This needs the Zombie and Animal Bait mod on the server.";
                return true;
        }
        // r<N><field>
        if (name.Length > 2 && name[0] == 'r' && char.IsDigit(name[1]))
        {
            int k = 1;
            while (k < name.Length && char.IsDigit(name[k])) k++;
            int row = int.Parse(name.Substring(1, k - 1));
            string field = name.Substring(k);
            var t = At(row);
            switch (field)
            {
                case "vis": value = t != null ? "true" : "false"; return true;
                case "text":
                    if (t == null) { value = ""; return true; }
                    string st = t.DownLeft > 0 ? "downed" : t.Tether ? "tethered r" + t.Radius.ToString("0.#", CultureInfo.InvariantCulture) : t.Active ? "active" : "inactive";
                    string hp = Hp(t);
                    value = (t.Idx == selIdx ? "> " : "") + t.Idx + ". " + (t.Level > 0 ? "Lv" + t.Level + " " : "") + t.Label + (string.IsNullOrEmpty(t.Name) ? "" : " (" + t.Pretty + ")") + "\n" + st + (t.Stay ? ", holding" : "") + (hp.Length > 0 ? "   HP " + hp : "") + "   kills " + t.Kills;
                    return true;
                case "calltxt": value = t == null ? "" : t.Active ? "Stow" : "Call"; return true;
                case "staytxt": value = t == null ? "" : t.Stay ? "Follow" : "Stay"; return true;
                case "modetxt": value = t == null ? "" : BaitData.ModeName(t.Mode); return true;
                case "tethtxt": value = t == null ? "" : t.Tether ? "Untether" : "Tether"; return true;
                case "autotxt": value = t == null || !BaitData.ServerHasAutoRevive ? "" : Box(t.AutoRevive) + " Auto"; return true;
                case "hboxtxt": value = t == null || !BaitData.ServerHasQol ? "" : Box(t.AutoHeal) + " Heal"; return true;
                case "uptxt": value = t == null || !BaitData.ServerHasQol || t.Idx <= 1 ? "" : "^"; return true;
                case "downtxt": value = t == null || !BaitData.ServerHasQol || t.Idx >= BaitData.Thralls.Count ? "" : "v"; return true;
                case "healtxt": value = t == null ? "" : t.DownLeft > 0 ? "Revive" : "Heal"; return true;
                case "nametxt": value = t == null ? "" : "Rename"; return true;
                case "reltxt": value = t == null ? "" : (relRow == row && Time.time - relAt < 4f) ? "Sure?" : "Release"; return true;
            }
        }
        return base.GetBindingValueInternal(ref value, name);
    }
}

// Small popup with a text box for renaming one thrall. Referenced as controller="AzraelBaitName, AzraelBaitUI".
public class XUiC_AzraelBaitName : XUiController
{
    XUiC_TextInput txt;

    void Back()
    {
        var wm = xui.playerUI.windowManager;
        wm.Close("azraelBaitName");
        wm.Open(BaitData.RosterGroup, true);
    }

    void Ok()
    {
        string name = txt != null ? (txt.Text ?? "") : "";
        name = name.Replace("|", " ").Replace("~", " ").Trim();
        BaitData.Cmd("/thrall name " + BaitData.RenameIdx + " " + (name.Length == 0 ? "clear" : name));
        Back();
    }

    public override void Init()
    {
        base.Init();
        txt = GetChildById("txtName") as XUiC_TextInput;
        if (txt != null) txt.OnSubmitHandler += (s, t) => Ok();
        var ok = GetChildById("btnOk") as XUiC_SimpleButton;
        if (ok != null) { ok.Text = "Rename"; ok.OnPressed += (s, b) => Ok(); }
        var cancel = GetChildById("btnCancel") as XUiC_SimpleButton;
        if (cancel != null) { cancel.Text = "Cancel"; cancel.OnPressed += (s, b) => Back(); }
    }

    public override void OnOpen() { base.OnOpen(); RefreshBindings(); }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        if (name == "nametitle") { value = "Rename " + BaitData.RenameFrom + "  (empty clears the name)"; return true; }
        return base.GetBindingValueInternal(ref value, name);
    }
}

// The admin window: settings with - / + buttons and the "cannot be tamed" list. The server checks admin rights.
// Referenced from XUi as controller="AzraelBaitAdmin, AzraelBaitUI".
public class XUiC_AzraelBaitAdmin : XUiController
{
    public const int Rows = 10, ExRows = 4;
    int page, exPage, seen = -1;
    float nextRefresh;

    void Hook(string id, Action a)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] admin window: no button " + id); return; }
        c.OnPress += (s, b) => { try { a(); } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    public override void Init()
    {
        base.Init();
        for (int i = 1; i <= Rows; i++)
        {
            int row = i;
            Hook("c" + i + "minus", () => Step(row, -1));
            Hook("c" + i + "plus", () => Step(row, +1));
        }
        for (int i = 1; i <= ExRows; i++)
        {
            int row = i;
            Hook("x" + i + "del", () => { var x = ExAt(row); if (x != null) BaitData.Cmd("/thrall exclude remove " + x); });
        }
        Hook("prev", () => { if (page > 0) page--; seen = -1; });
        Hook("next", () => { if ((page + 1) * Rows < BaitData.Config.Count) page++; seen = -1; });
        Hook("exprev", () => { if (exPage > 0) exPage--; seen = -1; });
        Hook("exnext", () => { if ((exPage + 1) * ExRows < BaitData.Exclude.Count) exPage++; seen = -1; });
        Hook("addlook", AddLooked);
        Hook("clearex", () => BaitData.Cmd("/thrall exclude clear"));
        Hook("back", () => { var wm = xui.playerUI.windowManager; wm.Close("azraelBaitAdmin"); wm.Open(BaitData.RosterGroup, true); });
        Hook("close", () => xui.playerUI.windowManager.Close("azraelBaitAdmin"));
    }

    string[] CfgAt(int row)
    {
        int i = page * Rows + row - 1;
        return i >= 0 && i < BaitData.Config.Count ? BaitData.Config[i] : null;
    }

    // Settings with a friendlier name in the window. The name sent to the server is still the setting's own.
    static string Label(string key) { return key == "KatsSpecial" ? "Kats Special (27 thralls each)" : key; }

    string ExAt(int row)
    {
        int i = exPage * ExRows + row - 1;
        return i >= 0 && i < BaitData.Exclude.Count ? BaitData.Exclude[i] : null;
    }

    void Step(int row, int dir)
    {
        var c = CfgAt(row);
        if (c == null) return;
        string v;
        if (c[2] == "b") v = c[1].Equals("true", StringComparison.OrdinalIgnoreCase) ? "false" : "true";
        else if (c[2] == "i")
        {
            int n; int.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            int step = Math.Abs(n) < 20 ? 1 : Math.Abs(n) < 100 ? 5 : 10;
            v = (n + dir * step).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            float f; float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f);
            float step = Math.Abs(f) < 2f ? 0.1f : Math.Abs(f) < 10f ? 0.5f : 1f;
            v = Math.Round(f + dir * step, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }
        BaitData.Cmd("/thrall config " + c[0] + " " + v);
    }

    // The creature under the crosshair (closest to the middle of the screen, within 30 m).
    void AddLooked()
    {
        var p = BaitData.Me;
        var w = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (p == null || w == null || p.playerCamera == null) return;
        Vector3 o = p.playerCamera.transform.position, d = p.playerCamera.transform.forward;
        EntityAlive best = null; float bestAng = 12f;
        foreach (var e in w.Entities.list)
        {
            var a = e as EntityAlive;
            if (a == null || a is EntityPlayer || a.IsDead()) continue;
            Vector3 to = a.position + Vector3.up * (a.height * 0.5f) - o;
            if (to.magnitude > 30f) continue;
            float ang = Vector3.Angle(d, to);
            if (ang < bestAng) { bestAng = ang; best = a; }
        }
        if (best == null) { BaitData.Status = "Nothing in front of you. Aim at a zombie or animal first."; BaitData.StatusAt = Time.time; BaitData.Version++; return; }
        BaitData.Cmd("/thrall exclude add " + EntityClass.list[best.entityClass].entityClassName);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        page = 0; exPage = 0; seen = -1;
        BaitData.Cmd("/thrall ui", false);
        RefreshBindings();
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (seen != BaitData.Version || Time.time > nextRefresh)
        {
            seen = BaitData.Version;
            nextRefresh = Time.time + 0.5f;
            RefreshBindings();
        }
    }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        switch (name)
        {
            case "title": value = BaitData.Admin ? "Bait settings (admin)" : "Bait settings (read only, admins only can change them)"; return true;
            case "status": value = BaitData.StatusText; return true;
            case "pagetext": value = "Page " + (page + 1) + "/" + Math.Max(1, (BaitData.Config.Count + Rows - 1) / Rows); return true;
            case "expagetext": value = "Excluded " + BaitData.Exclude.Count + (BaitData.Exclude.Count > ExRows ? "  (" + (exPage + 1) + "/" + ((BaitData.Exclude.Count + ExRows - 1) / ExRows) + ")" : ""); return true;
        }
        if (name.Length > 2 && (name[0] == 'c' || name[0] == 'x') && char.IsDigit(name[1]))
        {
            int j = 1;
            while (j < name.Length && char.IsDigit(name[j])) j++;
            int row = int.Parse(name.Substring(1, j - 1));
            string field = name.Substring(j);
            if (name[0] == 'c')
            {
                var c = CfgAt(row);
                bool isBool = c != null && c[2] == "b";
                switch (field)
                {
                    case "vis": value = c != null ? "true" : "false"; return true;
                    case "text": value = c == null ? "" : isBool ? (c[1].Equals("true", StringComparison.OrdinalIgnoreCase) ? "[X] " : "[  ] ") + Label(c[0]) : c[0] + " = " + c[1]; return true;
                    case "minustxt": value = c == null ? "" : isBool ? "Flip" : "-"; return true;
                    case "plusvis": value = c != null && !isBool ? "true" : "false"; return true;
                    case "plustxt": value = "+"; return true;
                }
            }
            else
            {
                var x = ExAt(row);
                switch (field)
                {
                    case "vis": value = x != null ? "true" : "false"; return true;
                    case "text": value = x ?? ""; return true;
                    case "deltxt": value = "Remove"; return true;
                }
            }
        }
        return base.GetBindingValueInternal(ref value, name);
    }
}
