using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;

// Azrael's Bait UI: the optional client side of Zombie and Animal Bait. A roster window (Home key), name tags with HP
// bars over your thralls, a small party list, a "fed x/y" tag over creatures you are taming, and an admin window.
// It only talks to the Bait mod on the server through the normal /thrall chat commands; the server mod sends
// the data back as hidden whispers that this mod swallows before they reach the chat window.

public class AzraelBaitUIMod : IModApi
{
    // From <Version> in the csproj, so there is no second copy to keep in step.
    public static string Version { get { return typeof(AzraelBaitUIMod).Assembly.GetName().Version.ToString(3); } }

    public void InitMod(Mod _modInstance)
    {
        if (GameManager.IsDedicatedServer) return;
        BaitXui.Mod = _modInstance;
        UiSettings.Load();
        new Harmony("azrael.baitui").PatchAll(typeof(AzraelBaitUIMod).Assembly);
        var go = new GameObject("AzraelBaitUI");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<BaitHud>();
        Log.Out("[BaitUI] " + Version + " loaded. Roster window key: " + UiSettings.Key + ". Type 'baitui' in the console (F1) for options.");
    }
}

public class UiThrall
{
    public int Idx, EntityId, Level, Kills, Mode;
    public long Down;
    public string Cls, Name;
    public bool Active, Tether, Stay, AutoRevive, AutoHeal;
    public float At;   // Time.time when this copy arrived (Down counts down from there)
    public long DownLeft { get { return Down <= 0 ? 0 : Math.Max(0, Down - (long)(Time.time - At)); } }
    public float Radius;
    // Bait 1.10.0+
    public string Perks = "", EvolveTo = "";
    public bool Guard, Patrol, HasPos;
    public int PatrolPts, Deaths, PerkSlots;
    public long TamedAt;     // unix seconds, 0 = unknown (tamed before 1.10.0)
    public Vector3 Pos;      // its tether, guard post or patrol centre (HasPos)
    public string Pretty { get { string l = Localization.Get(Cls); return string.IsNullOrEmpty(l) ? Cls : l; } }
    public string Label { get { return string.IsNullOrEmpty(Name) ? Pretty : Name; } }
}

public class UiFeedInfo { public int Cur, Need; public float At; }
public class UiLook { public int EntityId, Cur, Need, Level; public string Status; public float At; }

public static class BaitData
{
    public const string Tag = "\u200B\u200BBAIT|";
    public static readonly List<UiThrall> Thralls = new List<UiThrall>();
    public static readonly List<string[]> Config = new List<string[]>();   // name, value, type (i f b)
    public static readonly List<string> Exclude = new List<string>();
    public static readonly Dictionary<int, UiFeedInfo> Feeds = new Dictionary<int, UiFeedInfo>();
    public static readonly Dictionary<int, int> Owner = new Dictionary<int, int>();   // thrall entity id -> owner entity id (every thrall on the server)
    public static readonly HashSet<int> All = new HashSet<int>();   // every thrall on the server (any owner): turrets and motion sensors skip them
    public static int MaxOwned, MaxActive, ActiveCount, Power;
    public static int Plain, Rich, Feast;   // bait the server sees in your inventory (Bait 1.9.0+)
    public static bool BaitCounts;
    public static string BmMode = "";   // this player's blood moon mode (Bait 1.10.0+): off, call, fight, both
    public static UiLook Look;          // taming helper: the creature under the crosshair while you hold bait (Bait 1.10.0+)
    public static string Search = "";   // roster name search (not saved)
    public static int StatsIdx = -1;    // the thrall the thrall page shows (server number)
    public static int TeamSel;   // the team the roster's team button shows (and the wheel calls)
    public static readonly List<KeyValuePair<string, int>> Teams = new List<KeyValuePair<string, int>>();   // name, how many thralls (Bait 1.9.0+)
    public static bool Admin, HasData;
    public static string Status = "";
    public static float StatusAt, LastCmd;
    public static int Version;
    public static int RenameIdx;
    public static string RenameFrom = "";

    static List<UiThrall> pendT; static int pendTn;
    static List<string[]> pendC; static int pendCn;
    static List<string> pendX; static int pendXn;

    public static void Reset()
    {
        Thralls.Clear(); Teams.Clear(); Look = null; BmMode = ""; KillFeed.Clear(); LevelUpAt.Clear(); BaitCounts = false; Config.Clear(); Exclude.Clear(); Feeds.Clear(); All.Clear(); Owner.Clear(); HelloGaveUp = false; ServerVersion = ""; ServerHasOwners = false;
        HasData = false; Admin = false; Status = ""; Version++;
    }

    // Called with every chat line before it is shown. True = swallowed.
    public static bool Handle(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        try
        {
            if (msg.StartsWith(Tag, StringComparison.Ordinal)) { Parse(msg.Substring(Tag.Length)); return true; }
            const string bait = "[ffd966][Bait][-] ";
            if (Time.time - LastCmd < 2.5f && msg.StartsWith(bait, StringComparison.Ordinal))
            {
                Status = msg.Substring(bait.Length); StatusAt = Time.time; Version++;
                return true;
            }
        }
        catch (Exception e) { Log.Warning("[BaitUI] parse: " + e.Message); }
        return false;
    }

    static int I(string s) { int v; int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v); return v; }
    static long L(string s) { long v; long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v); return v; }
    static float Fl(string s) { float v; float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v); return v; }
    static string At(string[] f, int i) { return i < f.Length ? f[i] : ""; }

    static void Parse(string payload)
    {
        foreach (string rec in payload.Split('~'))
        {
            string[] f = rec.Split('|');
            switch (f[0])
            {
                case "S":
                    pendT = new List<UiThrall>(); pendTn = I(At(f, 1));
                    MaxOwned = I(At(f, 2)); MaxActive = I(At(f, 3)); ActiveCount = I(At(f, 4)); Admin = I(At(f, 5)) == 1; Power = I(At(f, 6)); ServerVersion = At(f, 7);
                    BaitCounts = f.Length > 10; Plain = I(At(f, 8)); Rich = I(At(f, 9)); Feast = I(At(f, 10)); BmMode = At(f, 11);
                    if (pendTn == 0) CommitT();
                    break;
                case "T":
                    if (pendT == null) break;
                    pendT.Add(new UiThrall
                    {
                        Idx = I(At(f, 1)), EntityId = I(At(f, 2)), Cls = At(f, 3), Active = I(At(f, 4)) == 1, Level = I(At(f, 5)), Kills = I(At(f, 6)),
                        Tether = I(At(f, 7)) == 1, Radius = Fl(At(f, 8)), Mode = I(At(f, 9)), Stay = I(At(f, 10)) == 1, Down = L(At(f, 11)), Name = At(f, 12), AutoRevive = I(At(f, 13)) == 1, AutoHeal = I(At(f, 14)) == 1, At = Time.time,
                        Perks = At(f, 15), Guard = I(At(f, 16)) == 1, Patrol = I(At(f, 17)) == 1, PatrolPts = I(At(f, 18)), Deaths = I(At(f, 19)), TamedAt = L(At(f, 20)),
                        PerkSlots = I(At(f, 21)), EvolveTo = At(f, 22), HasPos = At(f, 23).Length > 0, Pos = new Vector3(Fl(At(f, 23)), Fl(At(f, 24)), Fl(At(f, 25)))
                    });
                    if (pendT.Count >= pendTn) CommitT();
                    break;
                case "TM":
                    Teams.Clear();
                    foreach (string s in At(f, 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int c = s.LastIndexOf(':');
                        Teams.Add(c > 0 ? new KeyValuePair<string, int>(s.Substring(0, c), I(s.Substring(c + 1))) : new KeyValuePair<string, int>(s, 0));
                    }
                    Version++;
                    break;
                case "CS": pendC = new List<string[]>(); pendCn = I(At(f, 1)); if (pendCn == 0) CommitC(); break;
                case "C": if (pendC == null) break; pendC.Add(new[] { At(f, 1), At(f, 2), At(f, 3) }); if (pendC.Count >= pendCn) CommitC(); break;
                case "XS": pendX = new List<string>(); pendXn = I(At(f, 1)); if (pendXn == 0) CommitX(); break;
                case "X": if (pendX == null) break; pendX.Add(At(f, 1)); if (pendX.Count >= pendXn) CommitX(); break;
                case "A":
                    All.Clear();
                    foreach (string s in At(f, 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) All.Add(I(s));
                    break;
                case "O":
                    ServerHasOwners = true;
                    if (At(f, 1) == "1") Owner.Clear();
                    foreach (string s in At(f, 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) { var kv = s.Split(':'); if (kv.Length == 2) Owner[I(kv[0])] = I(kv[1]); }
                    break;
                case "K":
                {
                    var kt = Thralls.Find(t => t.Idx == I(At(f, 1)));
                    string who = kt != null ? kt.Label : "Thrall " + At(f, 1);
                    KillFeed.Add(new KeyValuePair<string, float>(who + (At(f, 4) == "1" ? " shares a kill: " : " killed ") + At(f, 2) + "  (" + At(f, 3) + " kills)", Time.time));
                    if (KillFeed.Count > 5) KillFeed.RemoveAt(0);
                    break;
                }
                case "L":
                    Look = I(At(f, 1)) <= 0 ? null : new UiLook { EntityId = I(At(f, 1)), Cur = I(At(f, 2)), Need = I(At(f, 3)), Status = At(f, 4), Level = I(At(f, 5)), At = Time.time };
                    break;
                case "F":
                    Feeds[I(At(f, 1))] = new UiFeedInfo { Cur = I(At(f, 2)), Need = I(At(f, 3)), At = Time.time };
                    Version++;
                    break;
            }
        }
    }

    static void CommitT()
    {
        // a thrall whose level went up since the last copy gets the level-up glow
        foreach (var n in pendT)
        {
            var old = Thralls.Find(t => t.Idx == n.Idx && t.Cls == n.Cls);
            if (old != null && n.Level > old.Level) LevelUpAt[UiSettings.ThrallKey(n)] = Time.time;
        }
        Thralls.Clear(); Thralls.AddRange(pendT); pendT = null; HasData = true; Version++;
    }

    public static readonly List<KeyValuePair<string, float>> KillFeed = new List<KeyValuePair<string, float>>();   // line, when
    public static readonly Dictionary<string, float> LevelUpAt = new Dictionary<string, float>();   // ThrallKey -> when it levelled up

    public static void SetStatus(string s) { Status = s; StatusAt = Time.time; Version++; }
    static void CommitC() { Config.Clear(); Config.AddRange(pendC); pendC = null; Version++; }
    static void CommitX() { Exclude.Clear(); Exclude.AddRange(pendX); pendX = null; Version++; }

    public static EntityPlayerLocal Me
    {
        get { var w = GameManager.Instance != null ? GameManager.Instance.World : null; return w != null ? w.GetPrimaryPlayer() : null; }
    }

    // Sends a /thrall command to the machine that runs Bait.
    // ChatMessageServer is the right call on every machine: on a remote client it sends the line to the server as a
    // NetPackageChat, on a host it runs it directly. ChatMessageClient only prints a line in this client's own chat
    // window and never reaches the server (BaitUI 1.6.3 used it, so the server never heard the hello).
    public static void Cmd(string text, bool expectReply = true)
    {
        var p = Me;
        if (p == null || GameManager.Instance == null) return;
        if (expectReply) LastCmd = Time.time;
        try { GameManager.Instance.ChatMessageServer(null, EChatType.Global, p.entityId, text, null, EMessageSender.SenderIdAsPlayer); }
        catch (Exception e) { Log.Warning("[BaitUI] send: " + e.Message); }
    }

    // One roster window per page size (8, 12 or 16 rows), all run by the same controller.
    public static readonly string[] RosterGroups = { "azraelBaitRoster", "azraelBaitRoster12", "azraelBaitRoster16" };
    public static string RosterGroup { get { return UiSettings.RowsPerPage >= 16 ? RosterGroups[2] : UiSettings.RowsPerPage >= 12 ? RosterGroups[1] : RosterGroups[0]; } }

    public static bool TryOpenRoster()
    {
        try
        {
            var ui = LocalPlayerUI.GetUIForPrimaryPlayer();
            if (ui == null || ui.windowManager == null) return false;
            var wm = ui.windowManager;
            foreach (string g in RosterGroups) if (wm.IsWindowOpen(g)) { wm.Close(g); return true; }
            BaitXui.TryRegister(ui);
            wm.Open(RosterGroup, true);
            if (!wm.IsWindowOpen(RosterGroup) && RosterGroup != RosterGroups[0]) wm.Open(RosterGroups[0], true);   // that size did not load: the 8-row one
            foreach (string g in RosterGroups) if (wm.IsWindowOpen(g)) return true;
            return false;
        }
        catch (Exception e) { Log.Warning("[BaitUI] open roster: " + e.Message); return false; }
    }

    public static bool ServerHasBait()
    {
        try { return ItemClass.GetItemClass("azraelBait", true) != null; } catch { return false; }
    }

    // What the server told us about itself. ServerVersion is empty on a Bait older than 1.7.6, which did not send it.
    public static string ServerVersion = "";
    public static bool ServerHasOwners;   // got the owner list (Bait 1.7.3+), which the party members' bars need
    public const string NeedsServer = "1.3.0";     // the oldest Bait this UI can talk to at all
    public const string PartyBarsNeed = "1.7.3";   // party members' bars
    public const string AutoReviveNeed = "1.8.0";  // the Auto tick box on each roster row (/thrall autorevive)
    public const string QolNeed = "1.9.0";         // Heal box, move arrows, teams, Bring all here, deaths, bait counts
    public const string RolesNeed = "1.10.0";      // thrall page (perks, guard, patrol, scout, evolve), Top, blood moon mode, taming helper, map markers for posts

    public static bool Older(string have, string need)
    {
        System.Version h, n;
        return System.Version.TryParse(have, out h) && System.Version.TryParse(need, out n) && h < n;
    }
    public static bool ServerTooOld { get { return !string.IsNullOrEmpty(ServerVersion) && Older(ServerVersion, NeedsServer); } }
    public static bool ServerHasAutoRevive { get { return !string.IsNullOrEmpty(ServerVersion) && !Older(ServerVersion, AutoReviveNeed); } }
    public static bool ServerHasQol { get { return !string.IsNullOrEmpty(ServerVersion) && !Older(ServerVersion, QolNeed); } }
    public static bool ServerHasRoles { get { return !string.IsNullOrEmpty(ServerVersion) && !Older(ServerVersion, RolesNeed); } }
    public static bool ServerHasPartyBars { get { return ServerHasOwners || (!string.IsNullOrEmpty(ServerVersion) && !Older(ServerVersion, PartyBarsNeed)); } }

    public static bool HelloGaveUp;   // asked a few times and the server never answered

    // The chat line a /thrall command leaves behind when the server does not swallow it ("Name: /thrall hello").
    public static bool IsThrallCommandLine(string m)
    {
        if (string.IsNullOrEmpty(m)) return false;
        int i = m.IndexOf("/thrall", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return false;
        return i == 0 || (i >= 2 && m[i - 1] == ' ' && m[i - 2] == ':');
    }

    public static string StatusText { get { return Time.time - StatusAt < 8f ? Status : ""; } }

    public static string ModeName(int m) { return m == 1 ? "Def" : m == 2 ? "Pass" : m == 3 ? "Inv" : "Aggr"; }
    public static string ModeWord(int m) { return m == 1 ? "defensive" : m == 2 ? "passive" : m == 3 ? "invulnerable" : "aggressive"; }

    // The thrall's own entity, or null if the saved id now points at something else (ids are reused after a respawn or relog,
    // which put the name tag and HP bar over a stranger). The entity class has to match the one the server reported.
    public static EntityAlive LiveThrall(UiThrall t)
    {
        var e = Live(t.EntityId);
        if (e == null) return null;
        try
        {
            EntityClass ec;
            if (!string.IsNullOrEmpty(t.Cls) && EntityClass.list.TryGetValue(e.entityClass, out ec) && !string.Equals(ec.entityClassName, t.Cls, StringComparison.OrdinalIgnoreCase)) return null;
        }
        catch { }
        return e;
    }

    // The roster as the window shows it: filtered (all, zombies, animals) and sorted (number, level, HP). Idx stays the
    // server's number, so every command still goes to the right thrall.
    public static List<UiThrall> View()
    {
        var l = new List<UiThrall>();
        foreach (var t in Thralls)
        {
            bool animal = t.Cls != null && t.Cls.StartsWith("animal", StringComparison.OrdinalIgnoreCase);
            if (UiSettings.Filter == 1 && animal || UiSettings.Filter == 2 && !animal) continue;
            if (Search.Length > 0 && t.Label.IndexOf(Search, StringComparison.OrdinalIgnoreCase) < 0 && t.Pretty.IndexOf(Search, StringComparison.OrdinalIgnoreCase) < 0 && t.Idx.ToString() != Search) continue;
            l.Add(t);
        }
        if (UiSettings.Sort == 1) l.Sort((a, b) => b.Level != a.Level ? b.Level.CompareTo(a.Level) : a.Idx.CompareTo(b.Idx));
        else if (UiSettings.Sort == 2) l.Sort((a, b) => { float ha = HpSortKey(a), hb = HpSortKey(b); return ha != hb ? ha.CompareTo(hb) : a.Idx.CompareTo(b.Idx); });
        return l;
    }

    // lowest HP first; downed before hurt, away/stowed last
    static float HpSortKey(UiThrall t)
    {
        if (t.DownLeft > 0) return -1f;
        var e = LiveThrall(t);
        if (e == null) return 2f;
        float m = e.GetMaxHealth();
        return m <= 0 ? 1f : e.Stats.Health.Value / m;
    }

    public static EntityAlive Live(int entityId)
    {
        if (entityId < 0) return null;
        var w = GameManager.Instance != null ? GameManager.Instance.World : null;
        var e = w != null ? w.GetEntity(entityId) as EntityAlive : null;
        return e != null && !e.IsDead() ? e : null;
    }
}

// Hides the server's data whispers (and answers to our own commands) from the chat window.
[HarmonyPatch(typeof(XUiC_ChatOutput), nameof(XUiC_ChatOutput.AddMessage))]
public static class BaitChatPatch
{
    static bool Prefix(string _message)
    {
        return !(BaitData.Handle(_message) || BaitData.IsThrallCommandLine(_message));
    }
}

public static class UiSettings
{
    public static KeyCode Key = KeyCode.Home;   // was Backslash, which not every keyboard layout has
    public static bool CaptureKey;   // the next key pressed becomes the roster hotkey (set by the button in the roster window)
    static bool keyChosen;   // the settings file was written by a version that knew the new default (keyver=2)
    public static bool Nameplates = true, Party = true, Feed = true;
    public static float PartyX = 0.006f, PartyY = 0.14f;   // party list position (fraction of the screen)
    public static float Scale = 1.6f;   // size of the name tags and party list
    public static bool PartyBars = true;   // also show name tags and HP bars over thralls that belong to members of your party
    public static int RowsPerPage = 8;     // 8, 12 or 16 roster rows per page
    public static int Filter, Sort;        // roster filter (0 all, 1 zombies, 2 animals) and sort (0 number, 1 level, 2 HP)
    public static readonly string[] FilterNames = { "All", "Zombies", "Animals" };
    public static readonly string[] SortNames = { "Number", "Level", "HP" };
    public static bool LowHpAlert = true, LowHpSound = true;   // flash a thrall's tag red (and a short sound) under 25% HP
    public static bool DownedTags = true;  // keep a grey 'back in m:ss' tag where a downed thrall fell
    public static bool KillFeed = true, LevelGlow = true;   // kill lines at the right of the screen; gold name tag on a level-up
    public static bool MapMarkers = true;    // your thralls and their tether/guard/patrol spots on the map and compass
    public static bool HpStrip;              // small HP bars in the bottom-right corner instead of the party list
    public static bool CompactParty;         // party list: name and bar on one line
    public static bool ColorRows = true;     // roster rows tinted with each thrall's own name colour
    public static bool TamingHelp = true;    // meals needed for the creature you aim at while holding bait
    public static int Theme;                 // 0 dark, 1 light, 2 high contrast
    public static readonly string[] ThemeNames = { "Dark", "Light", "High contrast" };
    // XUi colours (r,g,b,a) per theme: window background, row background, text
    static readonly string[] ThemeBg = { "28,28,28,235", "200,200,195,240", "0,0,0,255" };
    static readonly string[] ThemeRow = { "58,58,58,230", "232,232,226,240", "0,0,0,255" };
    static readonly string[] ThemeText = { "255,255,255,255", "20,20,20,255", "255,255,60,255" };
    public static string BgCol { get { return ThemeBg[Mathf.Clamp(Theme, 0, 2)]; } }
    public static string TextCol { get { return ThemeText[Mathf.Clamp(Theme, 0, 2)]; } }
    // a row takes on its thrall's own name colour (only one picked for that thrall, not the default)
    public static string RowCol(UiThrall t)
    {
        string baseCol = ThemeRow[Mathf.Clamp(Theme, 0, 2)];
        int[] c;
        if (t == null || !ColorRows || Theme == 2 || !PerThrall.TryGetValue(ThrallKey(t), out c) || c[0] < 0) return baseCol;
        var parts = baseCol.Split(',');
        var b = new Color(int.Parse(parts[0]) / 255f, int.Parse(parts[1]) / 255f, int.Parse(parts[2]) / 255f);
        var m = Color.Lerp(b, ColorValues[Mathf.Clamp(c[0], 0, ColorValues.Length - 1)], Theme == 1 ? 0.45f : 0.35f);
        return Mathf.RoundToInt(m.r * 255) + "," + Mathf.RoundToInt(m.g * 255) + "," + Mathf.RoundToInt(m.b * 255) + "," + parts[3];
    }
    public static Color HudBack { get { return Theme == 1 ? new Color(0.85f, 0.85f, 0.82f, 0.55f) : Theme == 2 ? new Color(0, 0, 0, 0.9f) : new Color(0, 0, 0, 0.35f); } }
    public static Color HudText { get { return Theme == 1 ? new Color(0.08f, 0.08f, 0.08f) : Theme == 2 ? new Color(1f, 1f, 0.25f) : Color.white; } }
    public static int RosterDX, RosterDY;  // where the roster window was dragged to (offset from the centre, XUi units)
    public static KeyCode WheelKey = KeyCode.Mouse2;   // hold for the quick-actions wheel (middle mouse button by default)
    public static bool CaptureWheel;       // the next key pressed becomes the wheel key
    // group hotkeys: follow all, stay all, bring all here (KeyCode.None = off)
    public static KeyCode FollowKey = KeyCode.F6, StayKey = KeyCode.F7, HereKey = KeyCode.F8;
    public static int CaptureGroup;        // 1-3: the next key pressed becomes that group hotkey (set by the roster button)
    public static readonly string[] GroupNames = { "", "Follow all", "Stay all", "Bring all here" };
    public static readonly string[] ColorNames = { "White", "Yellow", "Cyan", "Green", "Orange", "Pink", "Red" };
    static readonly Color[] ColorValues = { Color.white, new Color(1f, 0.92f, 0.25f), new Color(0.4f, 0.95f, 1f), new Color(0.5f, 1f, 0.45f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.55f, 0.85f), new Color(1f, 0.35f, 0.35f) };
    public static int BarColorIdx = 0;    // 0 = auto (green to red by health), 1.. = a fixed colour from the list below
    public static readonly string[] BarColorNames = { "Auto", "White", "Yellow", "Cyan", "Green", "Orange", "Pink", "Red" };
    public static Color BarColor(float frac, Color auto) { return BarColorIdx <= 0 ? auto : ColorValues[Mathf.Clamp(BarColorIdx - 1, 0, ColorValues.Length - 1)]; }
    // Per-thrall colours (client only, matched by roster number + class). -1 = use the default above.
    public static readonly Dictionary<string, int[]> PerThrall = new Dictionary<string, int[]>();
    public static int ColorTarget = -1;   // roster position the colour buttons act on; -1 = the default for every thrall
    public static string ThrallKey(UiThrall t) { return t.Idx + "|" + t.Cls; }
    public static UiThrall TargetThrall { get { return ColorTarget >= 0 && ColorTarget < BaitData.Thralls.Count ? BaitData.Thralls[ColorTarget] : null; } }
    public static int NameIdxFor(UiThrall t) { int[] c; return t != null && PerThrall.TryGetValue(ThrallKey(t), out c) && c[0] >= 0 ? c[0] : NameColorIdx; }
    public static int BarIdxFor(UiThrall t) { int[] c; return t != null && PerThrall.TryGetValue(ThrallKey(t), out c) && c[1] >= 0 ? c[1] : BarColorIdx; }
    public static Color NameColorFor(UiThrall t) { return ColorValues[Mathf.Clamp(NameIdxFor(t), 0, ColorValues.Length - 1)]; }
    public static Color BarColorFor(UiThrall t, Color auto) { int i = BarIdxFor(t); return i <= 0 ? auto : ColorValues[Mathf.Clamp(i - 1, 0, ColorValues.Length - 1)]; }
    static int[] Slot(UiThrall t) { int[] c; string k = ThrallKey(t); if (!PerThrall.TryGetValue(k, out c)) { c = new[] { -1, -1 }; PerThrall[k] = c; } return c; }
    public static void CycleName() { var t = TargetThrall; if (t == null) NameColorIdx = (NameColorIdx + 1) % ColorNames.Length; else { var c = Slot(t); c[0] = c[0] + 1 >= ColorNames.Length ? -1 : c[0] + 1; } Save(); }
    public static void CycleBar() { var t = TargetThrall; if (t == null) BarColorIdx = (BarColorIdx + 1) % BarColorNames.Length; else { var c = Slot(t); c[1] = c[1] + 1 >= BarColorNames.Length ? -1 : c[1] + 1; } Save(); }
    public static void NextTarget() { int n = BaitData.Thralls.Count; ColorTarget = ColorTarget + 1 >= n ? -1 : ColorTarget + 1; }
    public static string NameLabel() { var t = TargetThrall; int[] c; if (t == null || !PerThrall.TryGetValue(ThrallKey(t), out c) || c[0] < 0) return t == null ? ColorNames[NameColorIdx] : "Default (" + ColorNames[NameColorIdx] + ")"; return ColorNames[c[0]]; }
    public static string BarLabel() { var t = TargetThrall; int[] c; if (t == null || !PerThrall.TryGetValue(ThrallKey(t), out c) || c[1] < 0) return t == null ? BarColorNames[BarColorIdx] : "Default (" + BarColorNames[BarColorIdx] + ")"; return BarColorNames[c[1]]; }
    public static KeyCode GroupKey(int g) { return g == 1 ? FollowKey : g == 2 ? StayKey : g == 3 ? HereKey : KeyCode.None; }
    public static void SetGroupKey(int g, KeyCode k) { if (g == 1) FollowKey = k; else if (g == 2) StayKey = k; else if (g == 3) HereKey = k; }
    public static string KeyText(KeyCode k) { return k == KeyCode.None ? "none" : k.ToString(); }
    public static int NameColorIdx = 0;   // colour of the name tag text
    public static Color NameColor { get { return ColorValues[Mathf.Clamp(NameColorIdx, 0, ColorValues.Length - 1)]; } }

    static string FilePath { get { try { return Path.Combine(GameIO.GetUserGameDataDir(), "AzraelBaitUI.txt"); } catch { return null; } } }

    public static bool TryParseKey(string s, out KeyCode key)
    {
        key = KeyCode.None;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.Length == 1 && char.IsDigit(s[0])) s = "Alpha" + s;
        return Enum.TryParse(s, true, out key) && Enum.IsDefined(typeof(KeyCode), key);
    }

    public static void Load()
    {
        try
        {
            var p = FilePath;
            if (p == null || !File.Exists(p)) return;
            foreach (string raw in File.ReadAllLines(p))
            {
                var kv = raw.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToLowerInvariant(), v = kv[1].Trim();
                bool on = !v.Equals("off", StringComparison.OrdinalIgnoreCase);
                switch (k)
                {
                    case "keyver": keyChosen = true; break;
                    case "key": KeyCode kc; if (TryParseKey(v, out kc)) Key = kc; break;
                    case "nameplates": Nameplates = on; break;
                    case "party": Party = on; break;
                    case "feed": Feed = on; break;
                    case "partybars": PartyBars = on; break;
                    case "rows": { int rp; if (int.TryParse(v, out rp)) RowsPerPage = rp >= 16 ? 16 : rp >= 12 ? 12 : 8; break; }
                    case "filter": { int fi = Array.FindIndex(FilterNames, n => n.Equals(v, StringComparison.OrdinalIgnoreCase)); if (fi >= 0) Filter = fi; break; }
                    case "sort": { int si = Array.FindIndex(SortNames, n => n.Equals(v, StringComparison.OrdinalIgnoreCase)); if (si >= 0) Sort = si; break; }
                    case "lowhp": LowHpAlert = on; break;
                    case "lowhpsound": LowHpSound = on; break;
                    case "downedtags": DownedTags = on; break;
                    case "killfeed": KillFeed = on; break;
                    case "levelglow": LevelGlow = on; break;
                    case "mapmarkers": MapMarkers = on; break;
                    case "hpstrip": HpStrip = on; break;
                    case "compactparty": CompactParty = on; break;
                    case "colorrows": ColorRows = on; break;
                    case "taminghelp": TamingHelp = on; break;
                    case "theme": { int ti = Array.FindIndex(ThemeNames, n => n.Equals(v, StringComparison.OrdinalIgnoreCase)); if (ti >= 0) Theme = ti; break; }
                    case "rosterdx": int.TryParse(v, out RosterDX); break;
                    case "rosterdy": int.TryParse(v, out RosterDY); break;
                    case "wheelkey": { KeyCode gk; if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) WheelKey = KeyCode.None; else if (TryParseKey(v, out gk)) WheelKey = gk; break; }
                    case "followkey": { KeyCode gk; if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) FollowKey = KeyCode.None; else if (TryParseKey(v, out gk)) FollowKey = gk; break; }
                    case "staykey": { KeyCode gk; if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) StayKey = KeyCode.None; else if (TryParseKey(v, out gk)) StayKey = gk; break; }
                    case "herekey": { KeyCode gk; if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) HereKey = KeyCode.None; else if (TryParseKey(v, out gk)) HereKey = gk; break; }
                    case "thrall":
                    {
                        var tv = v.Split('='); if (tv.Length != 2) break;
                        var nb = tv[1].Split(','); int n0, b0;
                        if (nb.Length == 2 && int.TryParse(nb[0], out n0) && int.TryParse(nb[1], out b0)) PerThrall[tv[0]] = new[] { Mathf.Clamp(n0, -1, ColorNames.Length - 1), Mathf.Clamp(b0, -1, BarColorNames.Length - 1) };
                        break;
                    }
                    case "barcolor": { int bi = Array.FindIndex(BarColorNames, n => n.Equals(v, StringComparison.OrdinalIgnoreCase)); if (bi >= 0) BarColorIdx = bi; break; }
                    case "namecolor": { int ci = Array.FindIndex(ColorNames, n => n.Equals(v, StringComparison.OrdinalIgnoreCase)); if (ci >= 0) NameColorIdx = ci; break; }
                    case "partyx": float px; if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out px)) PartyX = Mathf.Clamp01(px); break;
                    case "partyy": float py; if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out py)) PartyY = Mathf.Clamp01(py); break;
                    case "scale": float sc; if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sc)) Scale = Mathf.Clamp(sc, 0.5f, 4f); break;
                }
            }
        }
        catch (Exception e) { Log.Warning("[BaitUI] settings: " + e.Message); }
        if (!keyChosen && Key == KeyCode.Backslash) Key = KeyCode.Home;   // an older settings file only holds the old default: move it to the new one
    }

    public static void Save()
    {
        try
        {
            var p = FilePath;
            if (p == null) return;
            var per = new System.Text.StringBuilder();
            foreach (var kvp in PerThrall) if (kvp.Value[0] >= 0 || kvp.Value[1] >= 0) per.Append("thrall=").Append(kvp.Key).Append('=').Append(kvp.Value[0]).Append(',').Append(kvp.Value[1]).Append('\n');
            File.WriteAllText(p, per.ToString() + "keyver=2\nkey=" + Key + "\nnameplates=" + (Nameplates ? "on" : "off") + "\nparty=" + (Party ? "on" : "off") + "\nfeed=" + (Feed ? "on" : "off") + "\npartybars=" + (PartyBars ? "on" : "off") + "\nbarcolor=" + BarColorNames[Mathf.Clamp(BarColorIdx, 0, BarColorNames.Length - 1)] + "\nnamecolor=" + ColorNames[Mathf.Clamp(NameColorIdx, 0, ColorNames.Length - 1)] + "\nscale=" + Scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                "\nrows=" + RowsPerPage + "\nfilter=" + FilterNames[Mathf.Clamp(Filter, 0, 2)] + "\nsort=" + SortNames[Mathf.Clamp(Sort, 0, 2)] +
                "\nlowhp=" + (LowHpAlert ? "on" : "off") + "\nlowhpsound=" + (LowHpSound ? "on" : "off") + "\ndownedtags=" + (DownedTags ? "on" : "off") +
                "\nkillfeed=" + (KillFeed ? "on" : "off") + "\nlevelglow=" + (LevelGlow ? "on" : "off") +
                "\nmapmarkers=" + (MapMarkers ? "on" : "off") + "\nhpstrip=" + (HpStrip ? "on" : "off") + "\ncompactparty=" + (CompactParty ? "on" : "off") +
                "\ncolorrows=" + (ColorRows ? "on" : "off") + "\ntaminghelp=" + (TamingHelp ? "on" : "off") + "\ntheme=" + ThemeNames[Mathf.Clamp(Theme, 0, 2)] + "\nrosterdx=" + RosterDX + "\nrosterdy=" + RosterDY + "\nwheelkey=" + WheelKey +
                "\nfollowkey=" + FollowKey + "\nstaykey=" + StayKey + "\nherekey=" + HereKey +
                "\npartyx=" + PartyX.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "\npartyy=" + PartyY.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }
        catch (Exception e) { Log.Warning("[BaitUI] settings save: " + e.Message); }
    }
}