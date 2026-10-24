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
    public void InitMod(Mod _modInstance)
    {
        if (GameManager.IsDedicatedServer) return;
        UiSettings.Load();
        new Harmony("azrael.baitui").PatchAll(typeof(AzraelBaitUIMod).Assembly);
        var go = new GameObject("AzraelBaitUI");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<BaitHud>();
        Log.Out("[BaitUI] Loaded. Roster window key: " + UiSettings.Key + ". Type 'baitui' in the console (F1) for options.");
    }
}

public class UiThrall
{
    public int Idx, EntityId, Level, Kills, Mode;
    public long Down;
    public string Cls, Name;
    public bool Active, Tether, Stay;
    public float Radius;
    public string Pretty { get { string l = Localization.Get(Cls); return string.IsNullOrEmpty(l) ? Cls : l; } }
    public string Label { get { return string.IsNullOrEmpty(Name) ? Pretty : Name; } }
}

public class UiFeedInfo { public int Cur, Need; public float At; }

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
        Thralls.Clear(); Config.Clear(); Exclude.Clear(); Feeds.Clear(); All.Clear(); Owner.Clear();
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
                    MaxOwned = I(At(f, 2)); MaxActive = I(At(f, 3)); ActiveCount = I(At(f, 4)); Admin = I(At(f, 5)) == 1; Power = I(At(f, 6));
                    if (pendTn == 0) CommitT();
                    break;
                case "T":
                    if (pendT == null) break;
                    pendT.Add(new UiThrall
                    {
                        Idx = I(At(f, 1)), EntityId = I(At(f, 2)), Cls = At(f, 3), Active = I(At(f, 4)) == 1, Level = I(At(f, 5)), Kills = I(At(f, 6)),
                        Tether = I(At(f, 7)) == 1, Radius = Fl(At(f, 8)), Mode = I(At(f, 9)), Stay = I(At(f, 10)) == 1, Down = L(At(f, 11)), Name = At(f, 12)
                    });
                    if (pendT.Count >= pendTn) CommitT();
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
                    if (At(f, 1) == "1") Owner.Clear();
                    foreach (string s in At(f, 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) { var kv = s.Split(':'); if (kv.Length == 2) Owner[I(kv[0])] = I(kv[1]); }
                    break;
                case "F":
                    Feeds[I(At(f, 1))] = new UiFeedInfo { Cur = I(At(f, 2)), Need = I(At(f, 3)), At = Time.time };
                    Version++;
                    break;
            }
        }
    }

    static void CommitT() { Thralls.Clear(); Thralls.AddRange(pendT); pendT = null; HasData = true; Version++; }
    static void CommitC() { Config.Clear(); Config.AddRange(pendC); pendC = null; Version++; }
    static void CommitX() { Exclude.Clear(); Exclude.AddRange(pendX); pendX = null; Version++; }

    public static EntityPlayerLocal Me
    {
        get { var w = GameManager.Instance != null ? GameManager.Instance.World : null; return w != null ? w.GetPrimaryPlayer() : null; }
    }

    // Sends a /thrall chat command (hidden from other players by the server mod).
    public static void Cmd(string text, bool expectReply = true)
    {
        var p = Me;
        if (p == null || GameManager.Instance == null) return;
        if (expectReply) LastCmd = Time.time;
        try { GameManager.Instance.ChatMessageServer(null, EChatType.Global, p.entityId, text, null, EMessageSender.SenderIdAsPlayer); }
        catch (Exception e) { Log.Warning("[BaitUI] send: " + e.Message); }
    }

    public static bool ServerHasBait()
    {
        try { return ItemClass.GetItemClass("azraelBait", true) != null; } catch { return false; }
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
        return !BaitData.Handle(_message);
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
                "\npartyx=" + PartyX.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "\npartyy=" + PartyY.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }
        catch (Exception e) { Log.Warning("[BaitUI] settings save: " + e.Message); }
    }
}