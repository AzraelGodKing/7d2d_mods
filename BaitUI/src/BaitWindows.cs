using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// The roster window: tabs for the thrall list (Thralls), whole-roster commands (Groups), the HUD options (Display),
// hotkeys (Keys) and pages and stats (Tools), with a command bar under every tab.
// Referenced from XUi as controller="AzraelBaitRoster, AzraelBaitUI". There is one window per page size (8, 12, 16 rows);
// the controller counts its rows.
public class XUiC_AzraelBaitRoster : XUiController
{
    int Rows = 8;
    int page, seen = -1;
    float nextRefresh;
    Vector2i homePos;
    Vector2 dragAcc;
    List<UiThrall> view = new List<UiThrall>();
    XUiC_TextInput cmd;

    void Hook(string id, Action a)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] roster window: no button " + id); return; }
        c.OnPress += (s, b) => { try { a(); } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    // buttons that need a newer Bait on the server do nothing (and show blank) on an older one
    void HookQol(string id, Action a) { Hook(id, () => { if (BaitData.ServerHasQol) a(); }); }
    void HookB3(string id, Action a) { Hook(id, () => { if (BaitData.ServerHasBatch3) a(); else BaitData.SetStatus("That needs Bait " + BaitData.Batch3Need + " or newer on the server."); }); }
    void Toggle(string id, Func<bool> get, Action<bool> set) { Hook(id, () => { set(!get()); UiSettings.Save(); seen = -1; }); }

    static void CloseAll(XUiController c) { var wm = c.xui.playerUI.windowManager; foreach (string g in BaitData.RosterGroups) wm.Close(g); }
    void OpenOther(string group) { CloseAll(this); xui.playerUI.windowManager.Open(group, true); }

    public override void Init()
    {
        base.Init();
        Rows = 0;
        while (GetChildById("row" + (Rows + 1)) != null) Rows++;
        if (Rows == 0) Rows = 8;
        homePos = ViewComponent.Position;
        for (int i = 0; i < 5; i++) { int tab = i; Hook("tab" + i, () => { UiSettings.Tab = tab; UiSettings.Save(); seen = -1; }); }
        for (int i = 1; i <= Rows; i++)
        {
            int row = i;
            Hook("r" + i + "sel", () => { var t = At(row); if (t != null) { BaitData.SelectedIdx = BaitData.SelectedIdx == t.Idx ? -1 : t.Idx; seen = -1; } });
            Hook("r" + i + "call", () => { var t = At(row); if (t != null) BaitData.Cmd(t.Active ? "/thrall stow " + t.Idx : "/thrall call " + t.Idx); });
            Hook("r" + i + "stay", () => { var t = At(row); if (t != null) BaitData.Cmd((t.Stay ? "/thrall follow " : "/thrall stay ") + t.Idx); });
            Hook("r" + i + "mode", () => { var t = At(row); if (t != null) BaitData.Cmd("/thrall mode " + t.Idx + " " + BaitData.ModeWord((t.Mode + 1) % 4)); });
            Hook("r" + i + "teth", () => { var t = At(row); if (t != null) BaitData.Cmd((t.Tether ? "/thrall untether " : "/thrall tether ") + t.Idx); });
            Hook("r" + i + "heal", () => { var t = At(row); if (t != null) BaitData.Cmd((t.DownLeft > 0 ? "/thrall revive " : "/thrall heal ") + t.Idx); });
            Hook("r" + i + "track", () => { var t = At(row); if (t != null) { Extras.ToggleTrack(t); seen = -1; } });
            Hook("r" + i + "more", () => { var t = At(row); if (t != null && BaitData.ServerHasRoles) { BaitData.StatsIdx = t.Idx; OpenOther("azraelBaitStats"); } });
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
        // Groups
        Hook("stowall", () => BaitData.Cmd("/thrall stow all"));
        Hook("stowactive", () => BaitData.Cmd("/thrall stow active"));
        Hook("stowtethered", () => BaitData.Cmd("/thrall stow tethered"));
        Hook("followall", () => BaitData.Cmd("/thrall follow all"));
        Hook("stayall", () => BaitData.Cmd("/thrall stay all"));
        HookQol("allhere", () => BaitData.Cmd("/thrall here"));
        HookQol("allhere2", () => BaitData.Cmd("/thrall here"));
        HookQol("deaths", () => { BaitData.Cmd("/thrall deaths", false); BaitData.SetStatus("The last deaths are listed in chat."); });
        Hook("teamcycle", () => { if (BaitData.Teams.Count > 0) BaitData.TeamSel = (BaitData.TeamSel + 1) % BaitData.Teams.Count; seen = -1; });
        HookQol("teamcall", () => { var tn = TeamName(); if (tn != null) BaitData.Cmd("/thrall team call " + tn); });
        HookQol("teamsave", () => { BaitData.Cmd("/thrall team save " + (TeamName() ?? NewTeamName()) + " active"); });
        HookQol("teamnew", () => { string n = NewTeamName(); BaitData.Cmd("/thrall team save " + n + " active"); BaitData.TeamSel = BaitData.Teams.Count; });
        Hook("bloodmoon2", () =>
        {
            if (!BaitData.ServerHasRoles) return;
            string[] modes = { "off", "call", "fight", "both" };
            int i = Array.IndexOf(modes, string.IsNullOrEmpty(BaitData.BmMode) ? "off" : BaitData.BmMode);
            BaitData.Cmd("/thrall bloodmoon " + modes[(i + 1) % modes.Length]);
        });
        HookB3("pvp", () => BaitData.Cmd("/thrall pvp " + (BaitData.PvpOn ? "off" : "on")));
        HookB3("kennelall", () => BaitData.Cmd("/thrall kennel all"));
        // Display
        Toggle("tgltags", () => UiSettings.Nameplates, v => UiSettings.Nameplates = v);
        Toggle("tglparty", () => UiSettings.Party, v => UiSettings.Party = v);
        Toggle("tglfed", () => UiSettings.Feed, v => UiSettings.Feed = v);
        Toggle("tglpbars", () => UiSettings.PartyBars, v => UiSettings.PartyBars = v);
        Toggle("tglkills", () => UiSettings.KillFeed, v => UiSettings.KillFeed = v);
        Toggle("tglglow", () => UiSettings.LevelGlow, v => UiSettings.LevelGlow = v);
        Toggle("tgllowhp", () => UiSettings.LowHpAlert, v => UiSettings.LowHpAlert = v);
        Toggle("tgllowsnd", () => UiSettings.LowHpSound, v => UiSettings.LowHpSound = v);
        Toggle("tgldowned", () => UiSettings.DownedTags, v => UiSettings.DownedTags = v);
        Toggle("tglmap", () => UiSettings.MapMarkers, v => UiSettings.MapMarkers = v);
        Toggle("tglstrip", () => UiSettings.HpStrip, v => UiSettings.HpStrip = v);
        Toggle("tglcompact", () => UiSettings.CompactParty, v => UiSettings.CompactParty = v);
        Toggle("tglcb", () => UiSettings.ColorBlind, v => UiSettings.ColorBlind = v);
        Toggle("tglbanner", () => UiSettings.TameBanner, v => UiSettings.TameBanner = v);
        Toggle("tgldmg", () => UiSettings.DamageNumbers, v => UiSettings.DamageNumbers = v);
        Toggle("tgllines", () => UiSettings.TargetLines, v => UiSettings.TargetLines = v);
        Toggle("tgldpanel", () => UiSettings.DownedPanel, v => UiSettings.DownedPanel = v);
        Toggle("tglsetup", () => UiSettings.SetupPopup, v => UiSettings.SetupPopup = v);
        Toggle("tglhelp", () => UiSettings.TamingHelp, v => UiSettings.TamingHelp = v);
        Hook("cyclekf", () => { UiSettings.KillFilter = (UiSettings.KillFilter + 1) % UiSettings.KillFilterNames.Length; UiSettings.Save(); seen = -1; });
        Hook("cycletheme", () => { UiSettings.Theme = (UiSettings.Theme + 1) % UiSettings.ThemeNames.Length; UiSettings.Save(); seen = -1; });
        Hook("cyclecolor", () => { UiSettings.CycleName(); seen = -1; });
        Hook("cyclebar", () => { UiSettings.CycleBar(); seen = -1; });
        Hook("cycletarget", () => { UiSettings.NextTarget(); seen = -1; });
        Hook("sizedown", () => { UiSettings.Scale = Mathf.Clamp(Mathf.Round((UiSettings.Scale - 0.2f) * 10f) / 10f, 0.5f, 4f); UiSettings.Save(); seen = -1; });
        Hook("sizeup", () => { UiSettings.Scale = Mathf.Clamp(Mathf.Round((UiSettings.Scale + 0.2f) * 10f) / 10f, 0.5f, 4f); UiSettings.Save(); seen = -1; });
        Hook("wsizedown", () => { UiSettings.WinScale = Mathf.Clamp(Mathf.Round((UiSettings.WinScale - 0.1f) * 10f) / 10f, 0.6f, 1.4f); UiSettings.Save(); ApplyPos(); seen = -1; });
        Hook("wsizeup", () => { UiSettings.WinScale = Mathf.Clamp(Mathf.Round((UiSettings.WinScale + 0.1f) * 10f) / 10f, 0.6f, 1.4f); UiSettings.Save(); ApplyPos(); seen = -1; });
        Hook("resetpos", () => { UiSettings.RosterDX = 0; UiSettings.RosterDY = 0; UiSettings.Save(); ApplyPos(); });
        for (int i = 0; i < UiSettings.Cols.Length; i++) { int c = i; Hook("col" + i, () => { UiSettings.Cols[c] = !UiSettings.Cols[c]; UiSettings.Save(); seen = -1; }); }
        // Keys
        Hook("pickkey", () => { StopCapture(); UiSettings.CaptureKey = true; seen = -1; });
        Hook("pickgroup", () => { StopCapture(); UiSettings.CaptureGroup = 1; seen = -1; });
        Hook("pickwheel", () => { StopCapture(); UiSettings.CaptureWheel = true; seen = -1; });
        Hook("pickgoto", () => { StopCapture(); UiSettings.CaptureGoto = true; seen = -1; });
        Hook("cyclemod", () => { UiSettings.ThrallMod = (UiSettings.ThrallMod + 1) % UiSettings.ModNames.Length; UiSettings.Save(); seen = -1; });
        // Tools
        Hook("thrallpage", () =>
        {
            if (!BaitData.ServerHasRoles || BaitData.Thralls.Count == 0) return;
            var t = SelectedThrall ?? (view.Count > 0 ? view[0] : BaitData.Thralls[0]);
            BaitData.StatsIdx = t.Idx;
            OpenOther("azraelBaitStats");
        });
        Hook("bestiary", () => OpenOther("azraelBaitBestiary"));
        Hook("summary", () => OpenOther("azraelBaitSummary"));
        Hook("top", () => { if (BaitData.ServerHasRoles) { BaitData.Cmd("/thrall top", false); BaitData.SetStatus("The leaderboard is in chat."); } });
        HookB3("statsme", () => { BaitData.Cmd("/thrall stats", false); BaitData.SetStatus("Your stats are in chat."); });
        HookB3("statsserver", () => { BaitData.Cmd("/thrall stats server", false); BaitData.SetStatus("The server's stats are in chat."); });
        Hook("craft", () => { CloseAll(this); Extras.OpenBaitCrafting(xui); });
        Hook("admin", () => OpenOther("azraelBaitAdmin"));
        Hook("close", () => CloseAll(this));
        // search box and command bar
        var search = GetChildById("search") as XUiC_TextInput;
        if (search != null)
        {
            search.Text = BaitData.Search;
            search.OnChangeHandler += (s, text, fromCode) => { BaitData.Search = (text ?? "").Trim(); page = 0; seen = -1; };
        }
        cmd = GetChildById("cmd") as XUiC_TextInput;
        if (cmd != null)
        {
            cmd.OnSubmitHandler += (s, text) => RunCommand();
            cmd.OnChangeHandler += (s, text, fromCode) => { seen = -1; };
        }
        Hook("cmdgo", RunCommand);
        Tips.Apply(this, Rows);
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

    static void StopCapture() { UiSettings.CaptureKey = false; UiSettings.CaptureGroup = 0; UiSettings.CaptureWheel = false; UiSettings.CaptureGoto = false; }

    void RunCommand()
    {
        if (cmd == null) return;
        string line = CommandBar.Normalize(cmd.Text);
        if (line.Length == 0) return;
        BaitData.Cmd("/thrall " + line);
        cmd.Text = "";
        seen = -1;
    }

    void ApplyPos()
    {
        ViewComponent.Position = new Vector2i(homePos.x + UiSettings.RosterDX, homePos.y + UiSettings.RosterDY);
        Extras.ApplyScale(ViewComponent);
    }

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

    static UiThrall SelectedThrall { get { return BaitData.SelectedIdx < 0 ? null : BaitData.Thralls.Find(t => t.Idx == BaitData.SelectedIdx); } }

    public override void OnClose()
    {
        base.OnClose();
        StopCapture();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        StopCapture();
        page = 0; seen = -1;
        ApplyPos();
        BaitData.Cmd("/thrall ui", false);   // ask for a fresh copy of everything
        view = BaitData.View();
        RefreshBindings();
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (seen != BaitData.Version || Time.time > nextRefresh)
        {
            seen = BaitData.Version;
            nextRefresh = Time.time + 0.4f;
            view = BaitData.View();
            int pages = Math.Max(1, (view.Count + Rows - 1) / Rows);
            if (page >= pages) page = pages - 1;
            if (Time.time > nextTips) { nextTips = Time.time + 3f; Tips.Apply(this, Rows); }
            RefreshBindings();
        }
    }
    float nextTips;

    static string Hp(UiThrall t)
    {
        if (t.DownLeft > 0) return "downed " + t.DownLeft + "s";
        var e = BaitData.LiveThrall(t);
        if (e == null) return t.Active ? "away" : "";
        return Mathf.CeilToInt(e.Stats.Health.Value) + "/" + e.GetMaxHealth();
    }

    static string OnOff(bool b) { return L.OnOff(b); }
    static string Box(bool b) { return b ? "[X]" : "[  ]"; }

    public static int KillsToNext(UiThrall t)
    {
        int per = 0;
        foreach (var c in BaitData.Config) if (c[0] == "KillsPerLevel") int.TryParse(c[1], out per);
        if (per <= 0) return -1;
        return t.Xp >= 0 ? Math.Max(1, per - t.Xp) : per - t.Kills % per;
    }

    static string Details(UiThrall t)
    {
        if (t == null) return "";
        int k = KillsToNext(t);
        string next = k > 0 ? "   " + k + " kills to the next level" : "";
        string where = t.DownLeft > 0 ? "downed, back in " + BaitHud.Clock(t.DownLeft) : t.Tether ? "tethered (radius " + t.Radius.ToString("0.#", CultureInfo.InvariantCulture) + ")" : t.Active ? (t.Stay ? "out, holding position" : "out, following you") : "stowed";
        var e = BaitData.LiveThrall(t);
        string hp = e != null ? "   HP " + Mathf.CeilToInt(e.Stats.Health.Value) + "/" + e.GetMaxHealth() : "";
        return t.Idx + ". " + (t.Alpha ? "[Alpha] " : "") + t.Label + (string.IsNullOrEmpty(t.Name) ? "" : " (" + t.Pretty + ")") + "   level " + t.Level + "   kills " + t.Kills + next + hp + "\n"
             + where + "   mode " + BaitData.ModeWord(t.Mode) + "   auto-revive " + (t.AutoRevive ? "on" : "off") + (BaitData.ServerHasQol ? "   auto-heal " + (t.AutoHeal ? "on" : "off") : "")
             + (t.SecondChance ? "   second chance" : "") + "   (Page > for everything else)";
    }

    // The row's text, with the columns picked on the Display tab (U38).
    static string RowText(UiThrall t)
    {
        var c = UiSettings.Cols;
        var sb = new System.Text.StringBuilder();
        if (t.Idx == BaitData.SelectedIdx) sb.Append("> ");
        sb.Append(t.Idx).Append(". ");
        if (c[0] && t.Level > 0) sb.Append("Lv").Append(t.Level).Append(' ');
        if (t.Alpha) sb.Append("[Alpha] ");
        sb.Append(t.Label);
        if (!string.IsNullOrEmpty(t.Name)) sb.Append(" (").Append(t.Pretty).Append(')');
        if (Extras.TrackIdx == t.Idx) sb.Append("  [tracked]");
        var line2 = new List<string>();
        if (c[2]) line2.Add((t.DownLeft > 0 ? "downed" : t.Patrol ? "patrol" : t.Guard ? "guard r" + t.Radius.ToString("0.#", CultureInfo.InvariantCulture) : t.Tether ? "tethered r" + t.Radius.ToString("0.#", CultureInfo.InvariantCulture) : t.Active ? "active" : "inactive") + (t.Stay ? ", holding" : ""));
        string hp = Hp(t);
        if (c[1] && hp.Length > 0) line2.Add("HP " + hp);
        if (c[3]) line2.Add("kills " + t.Kills);
        if (c[4]) line2.Add(BaitData.ModeWord(t.Mode));
        if (c[5]) line2.Add((t.AutoRevive ? "[X]" : "[ ]") + " revive " + (t.AutoHeal ? "[X]" : "[ ]") + " heal");
        if (line2.Count > 0) sb.Append('\n').Append(string.Join("   ", line2.ToArray()));
        return sb.ToString();
    }

    static string Tab(int i, string name) { return UiSettings.Tab == i ? "[ffd966]" + L.T(name) + "[-]" : L.T(name); }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        switch (name)
        {
            case "title":
                value = BaitData.HasData ? (BaitData.ServerTooOld ? "Thralls   (server Bait " + BaitData.ServerVersion + " is too old: this UI needs " + BaitData.NeedsServer + " or newer)   " : L.T("Thralls") + "   ") + BaitData.ActiveCount + "/" + BaitData.MaxActive + " active   " + BaitData.Thralls.Count + "/" + BaitData.MaxOwned + " held   power " + BaitData.Power
                          + (BaitData.BaitCounts ? "   bait " + BaitData.Plain + " / " + BaitData.Rich + " rich / " + BaitData.Feast + " feast" : "")
                          + (string.IsNullOrEmpty(BaitData.ServerVersion) ? "" : "   Bait " + BaitData.ServerVersion)
                                         : BaitData.HelloGaveUp ? "Thralls   (no answer from the server: it needs the Zombie and Animal Bait mod, 1.3 or newer)" : "Thralls   (waiting for the server...)";
                return true;
            case "tab0txt": value = Tab(0, "Thralls"); return true;
            case "tab1txt": value = Tab(1, "Groups"); return true;
            case "tab2txt": value = Tab(2, "Display"); return true;
            case "tab3txt": value = Tab(3, "Keys"); return true;
            case "tab4txt": value = Tab(4, "Tools"); return true;
            case "tab0vis": value = UiSettings.Tab == 0 ? "true" : "false"; return true;
            case "tab1vis": value = UiSettings.Tab == 1 ? "true" : "false"; return true;
            case "tab2vis": value = UiSettings.Tab == 2 ? "true" : "false"; return true;
            case "tab3vis": value = UiSettings.Tab == 3 ? "true" : "false"; return true;
            case "tab4vis": value = UiSettings.Tab == 4 ? "true" : "false"; return true;
            case "admintxt": value = L.T("Admin"); return true;
            case "closetxt": value = L.T("Close"); return true;
            case "status": value = BaitData.StatusText; return true;
            case "details": value = Details(SelectedThrall); return true;
            case "pagetext": value = (page + 1) + "/" + Math.Max(1, (view.Count + Rows - 1) / Rows); return true;
            case "filtertxt": value = L.T("Show") + ": " + L.T(UiSettings.FilterNames[UiSettings.Filter]); return true;
            case "sorttxt": value = L.T("Sort") + ": " + L.T(UiSettings.SortNames[UiSettings.Sort]); return true;
            case "pagesizetxt": value = Rows + " " + L.T("per page"); return true;
            case "allheretxt": value = BaitData.ServerHasQol ? L.T("Bring all here") : ""; return true;
            // Groups
            case "grp1hdr": value = L.T("Whole roster"); return true;
            case "grp2hdr": value = L.T("Teams"); return true;
            case "grp3hdr": value = L.T("Night, PvP and home"); return true;
            case "stowalltxt": value = L.T("Stow all"); return true;
            case "stowactivetxt": value = L.T("Stow active"); return true;
            case "stowtethtxt": value = L.T("Stow tethered"); return true;
            case "followalltxt": value = L.T("Follow all"); return true;
            case "stayalltxt": value = L.T("Stay all"); return true;
            case "deathstxt": value = BaitData.ServerHasQol ? L.T("Deaths") : ""; return true;
            case "teamtxt":
            {
                if (!BaitData.ServerHasQol) { value = BaitData.HasData ? "Teams need Bait " + BaitData.QolNeed + "+" : ""; return true; }
                var tn = TeamName();
                value = L.T("Team") + ": " + (tn == null ? L.T("none yet") : tn + " (" + BaitData.Teams[BaitData.TeamSel].Value + ")" + (BaitData.Teams.Count > 1 ? "  >" : ""));
                return true;
            }
            case "teamcalltxt": value = BaitData.ServerHasQol && TeamName() != null ? L.T("Call team") : ""; return true;
            case "teamsavetxt": value = BaitData.ServerHasQol ? (TeamName() != null ? L.T("Save active") : "") : ""; return true;
            case "teamnewtxt": value = BaitData.ServerHasQol ? L.T("New team") : ""; return true;
            case "bmtxt": value = BaitData.ServerHasRoles ? L.T("Blood moon") + ": " + (string.IsNullOrEmpty(BaitData.BmMode) ? "off" : BaitData.BmMode) : ""; return true;
            case "pvptxt": value = !BaitData.ServerHasBatch3 ? "" : BaitData.PvpAllowed ? "PvP: " + OnOff(BaitData.PvpOn) : "PvP: server off"; return true;
            case "kennelalltxt": value = BaitData.ServerHasBatch3 ? L.T("All to kennel") + " (" + BaitData.Kennels + ")" : ""; return true;
            case "grouphelp":
                value = "Teams save which thralls are out. Blood moon: call brings daycare thralls to you for the horde, fight turns the ones with you aggressive.\n"
                      + (BaitData.ServerHasBatch3 ? "PvP thralls only fight players who turned it on too, never your party or allies. Kennels are crafted blocks: thralls heal faster there." : "");
                return true;
            // Display
            case "tagstxt": value = L.T("Name tags") + ": " + OnOff(UiSettings.Nameplates); return true;
            case "partytxt": value = L.T("Party list") + ": " + OnOff(UiSettings.Party); return true;
            case "fedtxt": value = L.T("Fed tags") + ": " + OnOff(UiSettings.Feed); return true;
            case "pbarstxt": value = BaitData.HasData && !BaitData.ServerHasPartyBars ? "Party bars: server " + BaitData.PartyBarsNeed + "+" : L.T("Party bars") + ": " + OnOff(UiSettings.PartyBars); return true;
            case "killstxt": value = L.T("Kill feed") + ": " + OnOff(UiSettings.KillFeed); return true;
            case "glowtxt": value = L.T("Level glow") + ": " + OnOff(UiSettings.LevelGlow); return true;
            case "lowhptxt": value = L.T("Low HP flash") + ": " + OnOff(UiSettings.LowHpAlert); return true;
            case "lowsndtxt": value = L.T("Low HP sound") + ": " + OnOff(UiSettings.LowHpSound); return true;
            case "downedtxt": value = L.T("Downed tags") + ": " + OnOff(UiSettings.DownedTags); return true;
            case "maptxt": value = L.T("Map") + ": " + OnOff(UiSettings.MapMarkers); return true;
            case "striptxt": value = L.T("HP strip") + ": " + OnOff(UiSettings.HpStrip); return true;
            case "compacttxt": value = L.T("Compact") + ": " + OnOff(UiSettings.CompactParty); return true;
            case "themetxt": value = L.T("Theme") + ": " + L.T(UiSettings.ThemeNames[Mathf.Clamp(UiSettings.Theme, 0, 2)]); return true;
            case "cbtxt": value = L.T("Colour-blind bars") + ": " + OnOff(UiSettings.ColorBlind); return true;
            case "bannertxt": value = L.T("Tame banner") + ": " + OnOff(UiSettings.TameBanner); return true;
            case "dmgtxt": value = L.T("Damage numbers") + ": " + OnOff(UiSettings.DamageNumbers); return true;
            case "linestxt": value = L.T("Target lines") + ": " + OnOff(UiSettings.TargetLines); return true;
            case "dpaneltxt": value = L.T("Downed panel") + ": " + OnOff(UiSettings.DownedPanel); return true;
            case "kftxt": value = L.T("Kills") + ": " + L.T(UiSettings.KillFilterNames[Mathf.Clamp(UiSettings.KillFilter, 0, 2)]); return true;
            case "setuptxt": value = L.T("Setup popup") + ": " + OnOff(UiSettings.SetupPopup); return true;
            case "colortxt": value = L.T("Name color") + ": " + UiSettings.NameLabel(); return true;
            case "bartxt": value = L.T("Bar color") + ": " + UiSettings.BarLabel(); return true;
            case "targettxt": { var tt = UiSettings.TargetThrall; value = L.T("Colors for") + ": " + (tt == null ? L.T("all thralls") : tt.Idx + ". " + tt.Label) + " (click)"; return true; }
            case "sizetxt": value = L.T("Tag size") + " " + UiSettings.Scale.ToString("0.0", CultureInfo.InvariantCulture); return true;
            case "wsizetxt": value = L.T("Window size") + " " + UiSettings.WinScale.ToString("0.0", CultureInfo.InvariantCulture); return true;
            case "recentertxt": value = L.T("Re-center"); return true;
            case "colshdr": value = L.T("Columns"); return true;
            // Keys
            case "keytxt": value = UiSettings.CaptureKey ? L.T("Press a key...") : L.T("Roster key") + ": " + UiSettings.KeyText(UiSettings.Key); return true;
            case "wheeltxt": value = UiSettings.CaptureWheel ? L.T("Press a key...") : L.T("Wheel key") + ": " + UiSettings.KeyText(UiSettings.WheelKey); return true;
            case "gototxt": value = UiSettings.CaptureGoto ? L.T("Press a key...") : L.T("Go-to key") + ": " + UiSettings.KeyText(UiSettings.GotoKey); return true;
            case "grouptxt":
                value = UiSettings.CaptureGroup > 0 ? "Press a key for " + UiSettings.GroupNames[UiSettings.CaptureGroup] + " (Del = none)"
                      : "Keys: follow " + UiSettings.KeyText(UiSettings.FollowKey) + ", stay " + UiSettings.KeyText(UiSettings.StayKey) + ", here " + UiSettings.KeyText(UiSettings.HereKey) + "  (click to change)";
                return true;
            case "modtxt": value = L.T("Thrall keys") + ": " + (UiSettings.ThrallMod >= 3 ? L.T("off") : UiSettings.ModNames[UiSettings.ThrallMod] + "+1-9"); return true;
            case "keyhelp":
                value = "Roster key opens this window. Hold the wheel key for quick actions. Group keys: follow all, stay all, bring all here.\n"
                      + "Thrall keys: hold " + (UiSettings.ThrallMod >= 3 ? "(off)" : UiSettings.ModNames[UiSettings.ThrallMod]) + " and press 1-9 to call or stow roster numbers 1 to 9.\n"
                      + "Go-to key: aim at a spot and press it. The thrall picked in the list (or every one with you) walks there and waits."
                      + (BaitData.ServerHasBatch3 ? "" : "  (Go-to needs Bait " + BaitData.Batch3Need + "+ on the server.)") + "\nWhile picking a key, Esc cancels and Del clears it.";
                return true;
            // Tools
            case "pagebtntxt": value = BaitData.ServerHasRoles ? L.T("Thrall page") : ""; return true;
            case "bestiarytxt": value = L.T("Bestiary"); return true;
            case "summarytxt": value = L.T("Session summary"); return true;
            case "toptxt": value = BaitData.ServerHasRoles ? L.T("Top") : ""; return true;
            case "helptxt": value = L.T("Tame help") + ": " + OnOff(UiSettings.TamingHelp); return true;
            case "statsmetxt": value = BaitData.ServerHasBatch3 ? L.T("My stats") : ""; return true;
            case "statsservertxt": value = BaitData.ServerHasBatch3 ? L.T("Server stats") : ""; return true;
            case "crafttxt": value = L.T("Craft bait") + " (" + Extras.CraftableText() + ")"; return true;
            case "toolhelp":
                value = "Thrall page: one thrall's stats and every action for it. Bestiary: every creature you can tame and what you have.\n"
                      + "Session summary: kills, level ups, deaths and bait used since you joined. Craft bait opens crafting on the bait recipes; the numbers are how many you can make now.";
                return true;
            // command bar
            case "cmdlbl": value = L.T("Command"); return true;
            case "cmdgotxt": value = L.T("Run"); return true;
            case "cmdhint": value = CommandBar.Hint(cmd != null ? cmd.Text : ""); return true;
            case "adminvis": value = BaitData.Admin ? "true" : "false"; return true;
            case "bgcol": value = UiSettings.BgCol; return true;
            case "textcol": value = UiSettings.TextCol; return true;
            case "emptytext":
                value = BaitData.HasData ? (BaitData.Thralls.Count == 0 ? "You have no thralls yet. Throw bait at a zombie or animal and let it eat."
                                          : view.Count == 0 && BaitData.Search.Length > 0 ? "No thrall matches \"" + BaitData.Search + "\"."
                                          : view.Count == 0 ? "No " + UiSettings.FilterNames[UiSettings.Filter].ToLowerInvariant() + " on your roster. Click Show to see the rest." : "")
                                         : "No data from the server yet. This needs the Zombie and Animal Bait mod on the server.";
                return true;
        }
        if (name.StartsWith("col") && name.EndsWith("txt") && name.Length == 7)
        {
            int c = name[3] - '0';
            if (c >= 0 && c < UiSettings.Cols.Length) { value = Box(UiSettings.Cols[c]) + " " + L.T(UiSettings.ColNames[c]); return true; }
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
                case "col": value = UiSettings.RowCol(t); return true;
                case "text": value = t == null ? "" : RowText(t); return true;
                case "calltxt": value = t == null ? "" : L.T(t.Active ? "Stow" : "Call"); return true;
                case "staytxt": value = t == null ? "" : L.T(t.Stay ? "Follow" : "Stay"); return true;
                case "modetxt": value = t == null ? "" : BaitData.ModeName(t.Mode); return true;
                case "tethtxt": value = t == null ? "" : L.T(t.Tether ? "Untether" : "Tether"); return true;
                case "healtxt": value = t == null ? "" : L.T(t.DownLeft > 0 ? "Revive" : "Heal"); return true;
                case "tracktxt": value = t == null ? "" : L.T(Extras.TrackIdx == t.Idx ? "Untrack" : "Track"); return true;
                case "moretxt": value = t == null || !BaitData.ServerHasRoles ? "" : L.T("Page") + " >"; return true;
            }
        }
        return base.GetBindingValueInternal(ref value, name);
    }
}

// The command bar (U37): /thrall commands typed in the roster window, with a hint of what fits as you type.
public static class CommandBar
{
    static readonly string[] Verbs = { "list", "hp", "call", "stow", "stay", "follow", "mode", "heal", "revive", "autorevive", "autoheal", "here", "where", "team",
        "move", "deaths", "perk", "guard", "patrol", "bloodmoon", "top", "scout", "evolve", "tether", "untether", "release", "kill", "name", "scream", "pvp",
        "duel", "secondchance", "retire", "kennel", "goto", "stats", "alias", "last", "config", "exclude", "admin" };
    static readonly Dictionary<string, string> Subs = new Dictionary<string, string>
    {
        { "mode", "<n|all> aggressive|defensive|passive|invulnerable" }, { "stow", "[n|all|active|tethered]" }, { "team", "list|save|call|delete <name>" },
        { "perk", "<n> tough|hit|regen|taunt|reset" }, { "patrol", "<n> add|start|stop|clear" }, { "bloodmoon", "off|call|fight|both" },
        { "pvp", "on|off" }, { "duel", "<player> <n>  |  accept <n>  |  decline" }, { "stats", "[server]" }, { "move", "<n> up|down|top|<pos>" },
        { "autorevive", "<n|all> [on|off]" }, { "autoheal", "<n|all> [on|off]" }, { "tether", "<n|all> [radius]" }, { "guard", "<n|all> [radius]" },
        { "retire", "<n> [yes]" }, { "alias", "<name> <command>" }, { "config", "[name value] | preset casual|default|hardcore" }, { "top", "[kills]" },
        { "goto", "<n|all> x y z" }, { "name", "<n> <name>" }, { "exclude", "list|add|remove <class>" }
    };
    static readonly HashSet<string> TakesThrall = new HashSet<string> { "call", "stow", "stay", "follow", "mode", "heal", "revive", "autorevive", "autoheal", "here", "where",
        "move", "perk", "guard", "patrol", "scout", "evolve", "tether", "untether", "release", "kill", "name", "scream", "secondchance", "retire", "kennel", "goto" };

    public static string Normalize(string text)
    {
        string s = (text ?? "").Trim();
        foreach (string p in new[] { "/thralls ", "/thrall ", "/tame ", "/t " })
            if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(p.Length).Trim(); break; }
        if (s.StartsWith("/")) s = s.Substring(1);
        return s.Replace("|", " ").Replace("~", " ");
    }

    public static string Hint(string text)
    {
        string s = Normalize(text);
        if (s.Length == 0) return "type a command: call 2, mode all defensive, here... Enter runs it";
        var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts[0].ToLowerInvariant();
        bool typingVerb = parts.Length == 1 && !s.EndsWith(" ");
        if (typingVerb)
        {
            var m = new List<string>();
            foreach (string v in Verbs) if (v.StartsWith(verb)) m.Add(v);
            if (m.Count == 0) return "no command starts with \"" + verb + "\"";
            return string.Join("  ", m.GetRange(0, Math.Min(8, m.Count)).ToArray()) + (m.Count == 1 && Subs.ContainsKey(m[0]) ? "   " + Subs[m[0]] : "");
        }
        string sub;
        Subs.TryGetValue(verb, out sub);
        if (TakesThrall.Contains(verb) && parts.Length <= 2 && !(parts.Length == 2 && s.EndsWith(" ")))
        {
            string arg = parts.Length == 2 ? parts[1] : "";
            var m = new List<string>();
            foreach (var t in BaitData.Thralls)
            {
                string tag = t.Idx + " " + t.Label;
                if (arg.Length == 0 || t.Idx.ToString().StartsWith(arg) || t.Label.StartsWith(arg, StringComparison.OrdinalIgnoreCase)) m.Add(tag);
                if (m.Count >= 5) break;
            }
            return (sub ?? "<n|all>") + (m.Count > 0 ? "   e.g. " + string.Join(", ", m.ToArray()) : "");
        }
        return sub ?? "Enter runs it";
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
        if (BaitData.StatsIdx == BaitData.RenameIdx) wm.Open("azraelBaitStats", true);
        else wm.Open(BaitData.RosterGroup, true);
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
        if (ok != null) { ok.Text = L.T("Rename"); ok.OnPressed += (s, b) => Ok(); }
        var cancel = GetChildById("btnCancel") as XUiC_SimpleButton;
        if (cancel != null) { cancel.Text = L.T("Back"); cancel.OnPressed += (s, b) => Back(); }
    }

    public override void OnOpen() { base.OnOpen(); RefreshBindings(); }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        if (name == "nametitle") { value = L.T("Rename") + " " + BaitData.RenameFrom + "  (empty clears the name)"; return true; }
        return base.GetBindingValueInternal(ref value, name);
    }
}

// The admin window, in tabs: Settings (- / + per setting, presets), Feed rates (meals per animal and per-creature caps),
// Exclude (every creature class, searchable) and Players (every player's thralls, with care buttons).
// The server checks admin rights on every command. Referenced as controller="AzraelBaitAdmin, AzraelBaitUI".
public class XUiC_AzraelBaitAdmin : XUiController
{
    public const int Rows = 10;
    int tab, page, seen = -1, relRow = -1;
    float nextRefresh, relAt;
    string search = "";
    string player;   // the player open on the Players tab (null = the list)
    static List<string> classes;

    void Hook(string id, Action a)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] admin window: no button " + id); return; }
        c.OnPress += (s, b) => { try { a(); } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    public override void Init()
    {
        base.Init();
        for (int i = 0; i < 4; i++) { int t = i; Hook("atab" + i, () => { tab = t; page = 0; relRow = -1; if (t == 3) { player = null; BaitData.Cmd("/thrall admin ui", false); } seen = -1; }); }
        for (int i = 1; i <= Rows; i++)
            for (int j = 1; j <= 4; j++) { int row = i, b = j; Hook("a" + i + "b" + j, () => { Press(row, b); seen = -1; }); }
        for (int j = 1; j <= 5; j++) { int b = j; Hook("f" + j, () => { Foot(b); seen = -1; }); }
        Hook("prev", () => { if (page > 0) page--; seen = -1; });
        Hook("next", () => { if ((page + 1) * Rows < Count()) page++; seen = -1; });
        Hook("back", () => { var wm = xui.playerUI.windowManager; wm.Close("azraelBaitAdmin"); wm.Open(BaitData.RosterGroup, true); });
        Hook("close", () => xui.playerUI.windowManager.Close("azraelBaitAdmin"));
        var s = GetChildById("asearch") as XUiC_TextInput;
        if (s != null) s.OnChangeHandler += (x, text, fromCode) => { search = (text ?? "").Trim(); page = 0; seen = -1; };
    }

    // ---- the rows of each tab
    List<string[]> Settings()
    {
        var l = new List<string[]>();
        foreach (var c in BaitData.Config) if (search.Length == 0 || c[0].IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) l.Add(c);
        return l;
    }
    List<KeyValuePair<string, int>> Feeds()
    {
        var l = new List<KeyValuePair<string, int>>();
        foreach (var kv in BaitData.AnimalFeeds) if (search.Length == 0 || kv.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) l.Add(kv);
        // animals that have no feed rate of their own yet (the server's AnimalFeedsToTame applies)
        foreach (string c in Classes()) if (c.StartsWith("animal", StringComparison.OrdinalIgnoreCase) && !BaitData.AnimalFeeds.ContainsKey(c) && (search.Length == 0 || c.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)) l.Add(new KeyValuePair<string, int>(c, -1));
        return l;
    }
    List<string> ExcludeRows()
    {
        var l = new List<string>();
        foreach (string c in Classes()) if (search.Length == 0 || c.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || Pretty(c).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) l.Add(c);
        // excluded first, so the list shows what is blocked
        l.Sort((a, b) => { bool ea = BaitData.Exclude.Contains(a), eb = BaitData.Exclude.Contains(b); return ea != eb ? (ea ? -1 : 1) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase); });
        return l;
    }
    List<KeyValuePair<string, int>> PlayerRows()
    {
        var l = new List<KeyValuePair<string, int>>();
        foreach (var kv in BaitData.AdminPlayers) if (search.Length == 0 || kv.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) l.Add(kv);
        return l;
    }

    int Count()
    {
        switch (tab)
        {
            case 0: return Settings().Count;
            case 1: return Feeds().Count;
            case 2: return ExcludeRows().Count;
            default: return player == null ? PlayerRows().Count : BaitData.AdminRoster.Count;
        }
    }

    // Every creature class that can be tamed: zombies and animals.
    public static List<string> Classes()
    {
        if (classes != null) return classes;
        classes = new List<string>();
        try
        {
            foreach (var ec in EntityClass.list.Dict.Values)
            {
                string n = ec.entityClassName;
                if (string.IsNullOrEmpty(n) || n.IndexOf("Template", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Test", StringComparison.Ordinal) >= 0) continue;
                if (n.StartsWith("zombie", StringComparison.OrdinalIgnoreCase) || n.StartsWith("animal", StringComparison.OrdinalIgnoreCase)) classes.Add(n);
            }
            classes.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) { Log.Warning("[BaitUI] creature list: " + e.Message); }
        return classes;
    }
    public static string Pretty(string cls) { string l = Localization.Get(cls); return string.IsNullOrEmpty(l) ? cls : l; }

    void Press(int row, int b)
    {
        int i = page * Rows + row - 1;
        switch (tab)
        {
            case 0:
            {
                var l = Settings();
                if (i < 0 || i >= l.Count) return;
                var c = l[i];
                if (c[2] == "b") { if (b == 1) BaitData.Cmd("/thrall config " + c[0] + " " + (c[1].Equals("true", StringComparison.OrdinalIgnoreCase) ? "false" : "true")); return; }
                if (b == 1 || b == 2) Step(c, b == 1 ? -1 : 1);
                return;
            }
            case 1:
            {
                var l = Feeds();
                if (i < 0 || i >= l.Count) return;
                var kv = l[i];
                int cur = kv.Value > 0 ? kv.Value : CfgInt("AnimalFeedsToTame", 3);
                int cap; BaitData.Caps.TryGetValue(kv.Key, out cap);
                if (b == 1) BaitData.Cmd("/thrall config feeds." + kv.Key + " " + Math.Max(1, cur - 1));
                else if (b == 2) BaitData.Cmd("/thrall config feeds." + kv.Key + " " + (cur + 1));
                else if (b == 3) BaitData.Cmd("/thrall config cap." + kv.Key + " " + Math.Max(0, cap - 1));
                else BaitData.Cmd("/thrall config cap." + kv.Key + " " + (cap + 1));
                return;
            }
            case 2:
            {
                var l = ExcludeRows();
                if (i < 0 || i >= l.Count || b != 1) return;
                BaitData.Cmd("/thrall exclude " + (BaitData.Exclude.Contains(l[i]) ? "remove " : "add ") + l[i]);
                return;
            }
            default:
                if (player == null)
                {
                    var l = PlayerRows();
                    if (i < 0 || i >= l.Count || b != 1) return;
                    player = l[i].Key; page = 0;
                    BaitData.AdminRoster.Clear();
                    BaitData.Cmd("/thrall admin ui " + player, false);
                    return;
                }
                if (i < 0 || i >= BaitData.AdminRoster.Count) return;
                string n = BaitData.AdminRoster[i][0];
                if (b == 1) BaitData.Cmd("/thrall admin heal " + player + " " + n);
                else if (b == 2) BaitData.Cmd("/thrall admin revive " + player + " " + n);
                else if (b == 3) BaitData.Cmd("/thrall admin tp " + player + " " + n);
                else if (relRow == row && Time.time - relAt < 4f) { relRow = -1; BaitData.Cmd("/thrall admin release " + player + " " + n); Refresh(); }
                else { relRow = row; relAt = Time.time; }
                if (b != 4) Refresh();
                return;
        }
    }

    void Refresh() { if (player != null) BaitData.Cmd("/thrall admin ui " + player, false); }

    void Foot(int b)
    {
        switch (tab)
        {
            case 0:
                if (b <= 3) BaitData.Cmd("/thrall config preset " + (b == 1 ? "casual" : b == 2 ? "default" : "hardcore"));
                else if (b == 4) { BaitData.Cmd("/thrall admin log 10", false); BaitData.SetStatus("The admin log is in chat."); }
                return;
            case 2:
                if (b == 1) AddLooked();
                else if (b == 2) BaitData.Cmd("/thrall exclude clear");
                return;
            case 3:
                if (player == null) { if (b == 1) BaitData.Cmd("/thrall admin ui", false); return; }
                if (b == 1) { player = null; page = 0; BaitData.Cmd("/thrall admin ui", false); }
                else if (b == 2) { BaitData.Cmd("/thrall admin heal " + player + " all"); Refresh(); }
                else if (b == 3) { BaitData.Cmd("/thrall admin revive " + player + " all"); Refresh(); }
                else if (b == 4) { BaitData.Cmd("/thrall admin tp " + player + " all"); Refresh(); }
                else if (b == 5) { BaitData.Cmd("/thrall admin limit " + player + " " + (BaitData.AdminMaxOwned + 5) + " " + BaitData.AdminMaxActive); Refresh(); }
                return;
        }
    }

    static int CfgInt(string key, int def)
    {
        foreach (var c in BaitData.Config) { int v; if (c[0] == key && int.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v; }
        return def;
    }

    // Settings with a friendlier name in the window. The name sent to the server is still the setting's own.
    static string Label(string key) { return key == "KatsSpecial" ? "Kats Special (27 thralls each)" : key; }

    static void Step(string[] c, int dir)
    {
        string v;
        if (c[2] == "i")
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
    static void AddLooked()
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
            Vector3 to = a.position - Origin.position + Vector3.up * (a.height * 0.5f) - o;
            if (to.magnitude > 30f) continue;
            float ang = Vector3.Angle(d, to);
            if (ang < bestAng) { bestAng = ang; best = a; }
        }
        if (best == null) { BaitData.SetStatus("Nothing in front of you. Aim at a zombie or animal first."); return; }
        BaitData.Cmd("/thrall exclude add " + EntityClass.list[best.entityClass].entityClassName);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        page = 0; seen = -1; relRow = -1;
        BaitData.Cmd("/thrall ui", false);
        if (tab == 3) BaitData.Cmd(player == null ? "/thrall admin ui" : "/thrall admin ui " + player, false);
        RefreshBindings();
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (relRow >= 0 && Time.time - relAt >= 4f) { relRow = -1; seen = -1; }
        if (seen != BaitData.Version || Time.time > nextRefresh)
        {
            seen = BaitData.Version;
            nextRefresh = Time.time + 0.5f;
            RefreshBindings();
        }
    }

    static string TabTxt(int me, int cur, string name) { return me == cur ? "[ffd966]" + L.T(name) + "[-]" : L.T(name); }

    string FootText(int b)
    {
        switch (tab)
        {
            case 0: return b == 1 ? L.T("Casual") : b == 2 ? L.T("Default") : b == 3 ? L.T("Hardcore") : b == 4 ? L.T("Admin log") : "";
            case 2: return b == 1 ? L.T("Add aimed") : b == 2 ? L.T("Clear list") : "";
            case 3:
                if (player == null) return b == 1 ? "Refresh" : "";
                return b == 1 ? L.T("Back") : b == 2 ? L.T("Heal all") : b == 3 ? L.T("Revive all") : b == 4 ? L.T("Bring all") : b == 5 ? "Limit +5" : "";
        }
        return "";
    }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        switch (name)
        {
            case "title": value = BaitData.Admin ? "Bait settings (admin)" : "Bait settings (read only, admins only can change them)"; return true;
            case "bgcol": value = UiSettings.BgCol; return true;
            case "textcol": value = UiSettings.TextCol; return true;
            case "atab0": value = TabTxt(0, tab, "Settings"); return true;
            case "atab1": value = TabTxt(1, tab, "Feed rates"); return true;
            case "atab2": value = TabTxt(2, tab, "Exclude"); return true;
            case "atab3": value = TabTxt(3, tab, "Players"); return true;
            case "rostertxt": value = L.T("Roster"); return true;
            case "closetxt": value = L.T("Close"); return true;
            case "status": value = BaitData.StatusText; return true;
            case "pagetext": value = (page + 1) + "/" + Math.Max(1, (Count() + Rows - 1) / Rows); return true;
            case "ahint":
                value = tab == 0 ? "type to find a setting" : tab == 1 ? "meals to tame each animal, and how many one player may hold (0 = no cap)"
                      : tab == 2 ? "Excluded " + BaitData.Exclude.Count + ". Type to find a creature."
                      : player == null ? "Open a player to see their thralls." : BaitData.AdminWho + ": " + BaitData.AdminOwned + "/" + BaitData.AdminMaxOwned + " held, " + BaitData.AdminMaxActive + " out";
                return true;
        }
        if (name.Length == 5 && name[0] == 'f' && char.IsDigit(name[1]) && name.EndsWith("txt")) { value = FootText(name[1] - '0'); return true; }
        if (name.Length > 2 && name[0] == 'a' && char.IsDigit(name[1]))
        {
            int j = 1;
            while (j < name.Length && char.IsDigit(name[j])) j++;
            int row = int.Parse(name.Substring(1, j - 1));
            string field = name.Substring(j);
            int i = page * Rows + row - 1;
            string text = null, b1 = "", b2 = "", b3 = "", b4 = "";
            switch (tab)
            {
                case 0:
                {
                    var l = Settings();
                    if (i >= 0 && i < l.Count)
                    {
                        var c = l[i];
                        bool isBool = c[2] == "b";
                        text = isBool ? (c[1].Equals("true", StringComparison.OrdinalIgnoreCase) ? "[X] " : "[  ] ") + Label(c[0]) : c[0] + " = " + c[1];
                        b1 = isBool ? "Flip" : "-"; b2 = isBool ? "" : "+";
                    }
                    break;
                }
                case 1:
                {
                    var l = Feeds();
                    if (i >= 0 && i < l.Count)
                    {
                        var kv = l[i];
                        int cap; BaitData.Caps.TryGetValue(kv.Key, out cap);
                        text = Pretty(kv.Key) + ": " + (kv.Value > 0 ? kv.Value + " meals" : "default meals") + (cap > 0 ? ", cap " + cap : "");
                        b1 = "-"; b2 = "+"; b3 = L.T("Cap") + " -"; b4 = L.T("Cap") + " +";
                    }
                    break;
                }
                case 2:
                {
                    var l = ExcludeRows();
                    if (i >= 0 && i < l.Count)
                    {
                        bool ex = BaitData.Exclude.Contains(l[i]);
                        text = (ex ? "[X] " : "[  ] ") + Pretty(l[i]) + "  (" + l[i] + ")";
                        b1 = L.T(ex ? "Allow" : "Exclude");
                    }
                    break;
                }
                default:
                    if (player == null)
                    {
                        var l = PlayerRows();
                        if (i >= 0 && i < l.Count) { text = l[i].Key + ": " + l[i].Value + " thralls"; b1 = L.T("Open"); }
                    }
                    else if (i >= 0 && i < BaitData.AdminRoster.Count)
                    {
                        var r = BaitData.AdminRoster[i];
                        text = r[0] + ". " + r[1] + "   " + r[3] + " kills, " + r[4];
                        b1 = L.T("Heal"); b2 = L.T("Revive"); b3 = L.T("Bring"); b4 = relRow == row && Time.time - relAt < 4f ? L.T("Sure?") : L.T("Release");
                    }
                    break;
            }
            switch (field)
            {
                case "vis": value = text != null ? "true" : "false"; return true;
                case "text": value = text ?? ""; return true;
                case "b1txt": value = b1; return true;
                case "b2txt": value = b2; return true;
                case "b3txt": value = b3; return true;
                case "b4txt": value = b4; return true;
            }
        }
        return base.GetBindingValueInternal(ref value, name);
    }
}
