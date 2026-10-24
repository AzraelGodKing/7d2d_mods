using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// The roster window: one row per thrall with the buttons the /thrall commands give you.
// Referenced from XUi as controller="AzraelBaitRoster, AzraelBaitUI".
public class XUiC_AzraelBaitRoster : XUiController
{
    public const int Rows = 8;
    int page, seen = -1, relRow = -1;
    float relAt, nextRefresh;

    void Hook(string id, Action a)
    {
        var c = GetChildById(id);
        if (c == null) { Log.Warning("[BaitUI] roster window: no button " + id); return; }
        c.OnPress += (s, b) => { try { a(); } catch (Exception e) { Log.Warning("[BaitUI] click " + id + ": " + e.Message); } };
    }

    public override void Init()
    {
        base.Init();
        for (int i = 1; i <= Rows; i++)
        {
            int row = i;
            Hook("r" + i + "call", () => { var t = At(row); if (t != null) BaitData.Cmd(t.Active ? "/thrall stow " + t.Idx : "/thrall call " + t.Idx); });
            Hook("r" + i + "stay", () => { var t = At(row); if (t != null) BaitData.Cmd((t.Stay ? "/thrall follow " : "/thrall stay ") + t.Idx); });
            Hook("r" + i + "mode", () => { var t = At(row); if (t != null) BaitData.Cmd("/thrall mode " + t.Idx + " " + BaitData.ModeWord((t.Mode + 1) % 3)); });
            Hook("r" + i + "teth", () => { var t = At(row); if (t != null) BaitData.Cmd((t.Tether ? "/thrall untether " : "/thrall tether ") + t.Idx); });
            Hook("r" + i + "heal", () => { var t = At(row); if (t != null) BaitData.Cmd((t.Down > 0 ? "/thrall revive " : "/thrall heal ") + t.Idx); });
            Hook("r" + i + "name", () =>
            {
                var t = At(row);
                if (t == null) return;
                BaitData.RenameIdx = t.Idx; BaitData.RenameFrom = t.Label;
                var wm = xui.playerUI.windowManager; wm.Close("azraelBaitRoster"); wm.Open("azraelBaitName", true);
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
        Hook("next", () => { if ((page + 1) * Rows < BaitData.Thralls.Count) page++; seen = -1; });
        Hook("stowall", () => BaitData.Cmd("/thrall stow all"));
        Hook("followall", () => BaitData.Cmd("/thrall follow all"));
        Hook("stayall", () => BaitData.Cmd("/thrall stay all"));
        Hook("admin", () => { var wm = xui.playerUI.windowManager; wm.Close("azraelBaitRoster"); wm.Open("azraelBaitAdmin", true); });
        Hook("close", () => xui.playerUI.windowManager.Close("azraelBaitRoster"));
    }

    UiThrall At(int row)
    {
        int i = page * Rows + row - 1;
        return i >= 0 && i < BaitData.Thralls.Count ? BaitData.Thralls[i] : null;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        page = 0; relRow = -1; seen = -1;
        BaitData.Cmd("/thrall ui", false);   // ask for a fresh copy of everything
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
            int pages = Math.Max(1, (BaitData.Thralls.Count + Rows - 1) / Rows);
            if (page >= pages) page = pages - 1;
            RefreshBindings();
        }
    }

    static string Hp(UiThrall t)
    {
        if (t.Down > 0) return "downed " + t.Down + "s";
        var e = BaitData.Live(t.EntityId);
        if (e == null) return t.Active ? "away" : "";
        return Mathf.CeilToInt(e.Stats.Health.Value) + "/" + e.GetMaxHealth();
    }

    public override bool GetBindingValueInternal(ref string value, string name)
    {
        switch (name)
        {
            case "title":
                value = BaitData.HasData ? "Thralls   " + BaitData.ActiveCount + "/" + BaitData.MaxActive + " active   " + BaitData.Thralls.Count + "/" + BaitData.MaxOwned + " held   power " + BaitData.Power
                                         : "Thralls   (waiting for the server...)";
                return true;
            case "status": value = BaitData.StatusText; return true;
            case "pagetext": value = "Page " + (page + 1) + "/" + Math.Max(1, (BaitData.Thralls.Count + Rows - 1) / Rows); return true;
            case "adminvis": value = BaitData.Admin ? "true" : "false"; return true;
            case "emptytext":
                value = BaitData.HasData ? (BaitData.Thralls.Count == 0 ? "You have no thralls yet. Throw bait at a zombie or animal and let it eat." : "")
                                         : "No data from the server yet. This needs the Zombie and Animal Bait mod on the server.";
                return true;
        }
        // r<N><field>
        if (name.Length > 2 && name[0] == 'r' && char.IsDigit(name[1]))
        {
            int row = name[1] - '0';
            string field = name.Substring(2);
            var t = At(row);
            switch (field)
            {
                case "vis": value = t != null ? "true" : "false"; return true;
                case "text":
                    if (t == null) { value = ""; return true; }
                    string st = t.Down > 0 ? "downed" : t.Tether ? "tethered r" + t.Radius.ToString("0.#", CultureInfo.InvariantCulture) : t.Active ? "active" : "inactive";
                    string hp = Hp(t);
                    value = t.Idx + ". " + (t.Level > 0 ? "Lv" + t.Level + " " : "") + t.Label + (string.IsNullOrEmpty(t.Name) ? "" : " (" + t.Pretty + ")") + "\n" + st + (t.Stay ? ", holding" : "") + (hp.Length > 0 ? "   HP " + hp : "") + "   kills " + t.Kills;
                    return true;
                case "calltxt": value = t == null ? "" : t.Active ? "Stow" : "Call"; return true;
                case "staytxt": value = t == null ? "" : t.Stay ? "Follow" : "Stay"; return true;
                case "modetxt": value = t == null ? "" : BaitData.ModeName(t.Mode); return true;
                case "tethtxt": value = t == null ? "" : t.Tether ? "Untether" : "Tether"; return true;
                case "healtxt": value = t == null ? "" : t.Down > 0 ? "Revive" : "Heal"; return true;
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
        wm.Open("azraelBaitRoster", true);
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
        Hook("back", () => { var wm = xui.playerUI.windowManager; wm.Close("azraelBaitAdmin"); wm.Open("azraelBaitRoster", true); });
        Hook("close", () => xui.playerUI.windowManager.Close("azraelBaitAdmin"));
    }

    string[] CfgAt(int row)
    {
        int i = page * Rows + row - 1;
        return i >= 0 && i < BaitData.Config.Count ? BaitData.Config[i] : null;
    }

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
                    case "text": value = c == null ? "" : c[0] + " = " + c[1]; return true;
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