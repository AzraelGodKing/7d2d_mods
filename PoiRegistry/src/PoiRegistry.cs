using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// POI Registry: works out which player owns which POI from land claims. Server only, private.
//
// How it decides (all from data the server already keeps):
// - Claims: PersistentPlayerList.m_lpBlockMap (claim block position -> owner). A claim counts when the owner's
//   protection is still valid (World.IsLandProtectionValidForPlayer, so the offline expiry works like vanilla) and
//   the block is not a non-primary one (TEFeatureLandClaim.IsPrimary, only known while its chunk is loaded). Its area is the block position plus or
//   minus (LandClaimSize - 1) / 2 on X and Z, the same square RepairPad and CraftFromChests use.
// - POIs: DynamicPrefabDecorator's prefab instances, each with a bounding box.
// - A POI is claimed when a claim square overlaps its box by at least MinOverlapBlocks (x * z). The owner is the
//   player with a claim block inside the box, else the biggest overlap. Allies of the owner are co-owners. Anyone
//   else who overlaps makes the POI contested.
// The result is written to poi-owners.json and, if PostUrl is set, pushed to the web page.
//
// When it recalculates: only when something changed. Every IntervalSeconds it takes a fingerprint of the claim
// list (one pass over the claim map, no allocation, no world lookups) and recalculates only if that differs, a
// player joined or left, or a heartbeat is due (which also catches claims that expired with time). Nothing is
// patched, so a game update can't break a Harmony patch here.
public class AzraelPoiRegistry : IModApi
{
    public const string Tag = "[PoiRegistry]";

    public void InitMod(Mod _modInstance)
    {
        PrConfig.ModPath = _modInstance.Path;
        PrConfig.Load();
        ModEvents.GameStartDone.RegisterHandler(PrRunner.OnGameStart);
        ModEvents.GameUpdate.RegisterHandler(PrRunner.OnUpdate);
        ModEvents.GameShutdown.RegisterHandler(PrRunner.OnShutdown);
        ModEvents.PlayerSpawnedInWorld.RegisterHandler(PrRunner.OnPlayerSpawned);
        ModEvents.PlayerDisconnected.RegisterHandler(PrRunner.OnPlayerDisconnected);
        Log.Out(Tag + " Loaded. " + PrConfig.Describe() + " Players do not install this mod.");
    }
}

public static class PrConfig
{
    public static string ModPath = "";
    public static bool Enabled = true;
    public static string PostUrl = "";
    public static string Token = "";
    public static string ServerName = "";
    public static int IntervalSeconds = 10;
    public static int HeartbeatMinutes = 10;
    public static bool ShowNames = true;
    public static bool ShowCoordinates = false;
    public static int MinOverlapBlocks = 16;
    public static bool HideTraders = true;
    public static bool WriteFile = true;

    public static string SettingsPath { get { return Path.Combine(ModPath, "settings.txt"); } }
    public static string OutputPath { get { return Path.Combine(ModPath, "poi-owners.json"); } }
    public static string StatePath { get { return Path.Combine(ModPath, "poi-state.txt"); } }

    public static void Load()
    {
        Enabled = true; PostUrl = ""; Token = ""; ServerName = ""; IntervalSeconds = 10; HeartbeatMinutes = 10;
        ShowNames = true; ShowCoordinates = false; MinOverlapBlocks = 16; HideTraders = true; WriteFile = true;
        try
        {
            if (!File.Exists(SettingsPath))
                Log.Warning(AzraelPoiRegistry.Tag + " settings.txt not found, using defaults (writes poi-owners.json only).");
            else
                foreach (var raw in File.ReadAllLines(SettingsPath)) ApplyLine(raw);
        }
        catch (Exception e) { Log.Warning(AzraelPoiRegistry.Tag + " Could not read settings.txt: " + e.Message); }

        // Environment variables win, so a reinstall of the mod never wipes the secret.
        string envUrl = Environment.GetEnvironmentVariable("POI_REGISTRY_URL");
        string envToken = Environment.GetEnvironmentVariable("POI_REGISTRY_TOKEN");
        if (!string.IsNullOrEmpty(envUrl)) PostUrl = envUrl.Trim();
        if (!string.IsNullOrEmpty(envToken)) Token = envToken.Trim();

        IntervalSeconds = Math.Max(5, Math.Min(3600, IntervalSeconds));
        HeartbeatMinutes = Math.Max(1, Math.Min(120, HeartbeatMinutes));
        MinOverlapBlocks = Math.Max(1, MinOverlapBlocks);
    }

    static void ApplyLine(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line[0] == '#') return;
        int eq = line.IndexOf('=');
        if (eq <= 0) return;
        string key = line.Substring(0, eq).Trim();
        string value = line.Substring(eq + 1).Trim();
        string lower = value.ToLowerInvariant();
        bool on = lower == "on" || lower == "true" || lower == "1";
        bool off = lower == "off" || lower == "false" || lower == "0";
        int n;
        switch (key.ToLowerInvariant())
        {
            case "posturl": PostUrl = value; return;
            case "token": Token = value; return;
            case "servername": ServerName = value; return;
            case "intervalseconds": if (int.TryParse(value, out n)) IntervalSeconds = n; else Bad(key, value); return;
            case "heartbeatminutes": if (int.TryParse(value, out n)) HeartbeatMinutes = n; else Bad(key, value); return;
            case "minoverlapblocks": if (int.TryParse(value, out n)) MinOverlapBlocks = n; else Bad(key, value); return;
        }
        if (!on && !off) { Bad(key, value); return; }
        switch (key.ToLowerInvariant())
        {
            case "enabled": Enabled = on; break;
            case "shownames": ShowNames = on; break;
            case "showcoordinates": ShowCoordinates = on; break;
            case "hidetraders": HideTraders = on; break;
            case "writefile": WriteFile = on; break;
            default: Log.Warning(AzraelPoiRegistry.Tag + " Unknown setting '" + key + "'."); break;
        }
    }

    static void Bad(string key, string value)
    {
        Log.Warning(AzraelPoiRegistry.Tag + " Ignoring " + key + "=" + value + ".");
    }

    // Null if the URL is fine, else why not.
    public static string UrlProblem()
    {
        if (string.IsNullOrEmpty(PostUrl)) return "PostUrl is empty";
        Uri u;
        if (!Uri.TryCreate(PostUrl, UriKind.Absolute, out u)) return "PostUrl is not a web address";
        bool local = u.Host == "localhost" || u.Host == "127.0.0.1";
        if (u.Scheme != "https" && !(u.Scheme == "http" && local)) return "PostUrl must be https (http only for localhost)";
        if (string.IsNullOrEmpty(Token)) return "Token is empty";
        return null;
    }

    public static string Describe()
    {
        if (!Enabled) return "Enabled=off.";
        string where = UrlProblem();
        string push = where == null ? "Pushing to " + MaskedUrl() + " on every change (and every " + HeartbeatMinutes + " min)"
                                    : "Not pushing (" + where + ")";
        return push + ". Looks for claim changes every " + IntervalSeconds + "s. Names " + (ShowNames ? "shown" : "hidden") +
               ", coordinates " + (ShowCoordinates ? "shown" : "hidden") + ".";
    }

    public static string MaskedUrl()
    {
        Uri u;
        return Uri.TryCreate(PostUrl, UriKind.Absolute, out u) ? u.Scheme + "://" + u.Host + u.AbsolutePath : "(none)";
    }
}

// One POI in the result.
public class PrPoi
{
    // Key is the mod's own id (name@x,z). Id is what the page sees: the same, or a hash when coordinates are hidden.
    public string Key, Id, Name, DisplayName, Status, ClaimedSince;
    public int Tier, X, Z, SizeX, SizeZ, Claims;
    public bool Trader;
    public PrPlayer Owner;
    public List<PrPlayer> CoOwners = new List<PrPlayer>();
    public List<PrPlayer> Contested = new List<PrPlayer>();
}

public class PrPlayer
{
    public string Id, Name;
    public bool Online;
    public string LastSeen;
    public int ClaimedPois;
}

public class PrSnapshot
{
    public string Json;
    public string Signature;
    public List<PrPoi> Pois = new List<PrPoi>();
    public List<PrPlayer> Players = new List<PrPlayer>();
    public int ClaimCount;
    public string BuiltAt;
    public double BuildMs;
}

public static class PrBuilder
{
    class Claim
    {
        public Vector3i Pos;
        public PersistentPlayerData Owner;
        public int X0, X1, Z0, Z1;
    }

    class Group
    {
        public PersistentPlayerData Owner;
        public int Area;
        public int Claims;
        public bool CenterInside;
    }

    static string salt;
    // poi id | owner id -> when this mod first saw that pair
    static readonly Dictionary<string, string> since = new Dictionary<string, string>();
    static bool stateLoaded, stateDirty;

    static void LoadState()
    {
        if (stateLoaded) return;
        stateLoaded = true;
        try
        {
            if (File.Exists(PrConfig.StatePath))
                foreach (var line in File.ReadAllLines(PrConfig.StatePath))
                {
                    var p = line.Split('\t');
                    if (p.Length == 2 && p[0] == "salt") salt = p[1];
                    else if (p.Length == 3 && p[0] == "since") since[p[1]] = p[2];
                }
        }
        catch (Exception e) { Log.Warning(AzraelPoiRegistry.Tag + " Could not read poi-state.txt: " + e.Message); }
        if (string.IsNullOrEmpty(salt)) { salt = Guid.NewGuid().ToString("N"); stateDirty = true; }
    }

    static void SaveState()
    {
        if (!stateDirty) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append("salt\t").Append(salt).Append('\n');
            foreach (var kv in since) sb.Append("since\t").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
            File.WriteAllText(PrConfig.StatePath, sb.ToString());
            stateDirty = false;
        }
        catch (Exception e) { Log.Warning(AzraelPoiRegistry.Tag + " Could not write poi-state.txt: " + e.Message); }
    }

    // A salted hash, so the page never sees a Steam or platform id.
    static string PlayerKey(PersistentPlayerData p)
    {
        string raw = null;
        try { raw = p.PrimaryId != null ? p.PrimaryId.CombinedString : null; } catch { }
        if (string.IsNullOrEmpty(raw)) raw = "entity" + p.EntityId;
        return Hash("p_", raw, 5);
    }

    static string Hash(string prefix, string raw, int bytes)
    {
        using (var sha = SHA1.Create())
        {
            var h = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + "|" + raw));
            var sb = new StringBuilder(prefix);
            for (int i = 0; i < bytes; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }
    }

    // The page refuses a whole snapshot that holds anything shaped like a platform id or an IP address.
    // Nothing the mod sends is one, but a player could pick such a name, so text like that is left out.
    static readonly System.Text.RegularExpressions.Regex Personal = new System.Text.RegularExpressions.Regex(
        @"7656119\d{10}|STEAM_[0-5]:[01]:\d+|\[U:1:\d+\]|(Steam|EOS|XBL|PSN)_[0-9A-Za-z]{6,}|(\d{1,3}\.){3}\d{1,3}|[0-9a-f]{1,4}(:[0-9a-f]{1,4}){7}",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static string Safe(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        text = text.Trim();
        if (text.Length > 60) text = text.Substring(0, 60);
        return text.Length == 0 || Personal.IsMatch(text) ? null : text;
    }

    static string Iso(DateTime t)
    {
        return t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    // A cheap fingerprint of the claim list: which blocks exist and who owns them. One pass, no allocation.
    public static long ClaimFingerprint()
    {
        var gm = GameManager.Instance;
        var lp = gm != null ? gm.persistentPlayers : null;
        if (lp == null || lp.m_lpBlockMap == null) return 0;
        long sum = lp.m_lpBlockMap.Count;
        foreach (var kv in lp.m_lpBlockMap)
        {
            long h = kv.Key.x * 73856093L ^ kv.Key.y * 19349663L ^ kv.Key.z * 83492791L;
            if (kv.Value != null) h ^= kv.Value.EntityId * 2654435761L;
            sum += h * 31 + (h >> 7); // order of the map doesn't matter
        }
        return sum;
    }

    public static PrSnapshot Build()
    {
        var gm = GameManager.Instance;
        var world = gm != null ? gm.World : null;
        var dpd = gm != null ? gm.GetDynamicPrefabDecorator() : null;
        var lp = gm != null ? gm.persistentPlayers : null;
        if (world == null || dpd == null || lp == null || lp.m_lpBlockMap == null) return null;
        LoadState();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        DateTime now = DateTime.UtcNow;
        int half = Math.Max(0, (GameStats.GetInt(EnumGameStats.LandClaimSize) - 1) / 2);

        // Active primary claims.
        var claims = new List<Claim>();
        foreach (var kv in lp.m_lpBlockMap)
        {
            PersistentPlayerData owner = kv.Value;
            if (owner == null) continue;
            try
            {
                if (!world.IsLandProtectionValidForPlayer(owner)) continue;
                // The claim block's own data is only there while its chunk is loaded. When it is, skip blocks
                // that are not the primary one; when it isn't (nobody near), the claim still counts.
                TileEntity te = world.GetTileEntity(kv.Key);
                TEFeatureLandClaim feature = te != null ? te.GetSelfOrFeature<TEFeatureLandClaim>() : null;
                if (feature != null && !feature.IsPrimary()) continue;
            }
            catch (Exception) { continue; }
            claims.Add(new Claim { Pos = kv.Key, Owner = owner, X0 = kv.Key.x - half, X1 = kv.Key.x + half, Z0 = kv.Key.z - half, Z1 = kv.Key.z + half });
        }

        var snap = new PrSnapshot { ClaimCount = claims.Count, BuiltAt = Iso(now) };
        var people = new Dictionary<PersistentPlayerData, PrPlayer>();
        Func<PersistentPlayerData, PrPlayer> person = p =>
        {
            PrPlayer pl;
            if (!people.TryGetValue(p, out pl))
            {
                string name = null;
                try { name = p.PlayerName != null ? p.PlayerName.DisplayName : null; } catch { }
                pl = new PrPlayer { Id = PlayerKey(p), Name = Safe(name) };
                people[p] = pl;
            }
            return pl;
        };

        var livePairs = new HashSet<string>();
        var prefabs = new List<PrefabInstance>();
        dpd.GetPOIPrefabs(prefabs);
        foreach (PrefabInstance pi in prefabs)
        {
            if (pi == null || pi.prefab == null) continue;
            bool trader = pi.prefab.bTraderArea;
            if (trader && PrConfig.HideTraders) continue;

            int px0 = pi.boundingBoxPosition.x, pz0 = pi.boundingBoxPosition.z;
            int sx = pi.boundingBoxSize.x, sz = pi.boundingBoxSize.z;
            int px1 = px0 + sx - 1, pz1 = pz0 + sz - 1;
            string pname = pi.prefab.PrefabName ?? pi.name ?? "unknown";

            string key = pname + "@" + px0 + "," + pz0;
            var poi = new PrPoi
            {
                Key = key, Id = PrConfig.ShowCoordinates ? key : Hash("poi_", key, 6), Name = pname, X = px0, Z = pz0, SizeX = sx, SizeZ = sz,
                Trader = trader, Status = "unclaimed"
            };
            try { poi.Tier = pi.prefab.DifficultyTier; } catch { }
            try { poi.DisplayName = pi.prefab.LocalizedName; } catch { }
            if (poi.DisplayName == pname) poi.DisplayName = null;

            var groups = new Dictionary<PersistentPlayerData, Group>();
            foreach (var c in claims)
            {
                int ox = Math.Min(px1, c.X1) - Math.Max(px0, c.X0) + 1;
                int oz = Math.Min(pz1, c.Z1) - Math.Max(pz0, c.Z0) + 1;
                if (ox <= 0 || oz <= 0) continue;
                int area = ox * oz;
                if (area < PrConfig.MinOverlapBlocks) continue;
                Group g;
                if (!groups.TryGetValue(c.Owner, out g)) { g = new Group { Owner = c.Owner }; groups[c.Owner] = g; }
                g.Area += area;
                g.Claims++;
                if (c.Pos.x >= px0 && c.Pos.x <= px1 && c.Pos.z >= pz0 && c.Pos.z <= pz1) g.CenterInside = true;
            }

            if (groups.Count > 0)
            {
                var ordered = groups.Values
                    .OrderByDescending(g => g.CenterInside).ThenByDescending(g => g.Area).ThenBy(g => PlayerKey(g.Owner)).ToList();
                Group top = ordered[0];
                poi.Owner = person(top.Owner);
                poi.Claims = top.Claims;
                poi.Status = "claimed";
                foreach (var g in ordered.Skip(1))
                {
                    bool ally = false;
                    try { ally = g.Owner.IsAlly(top.Owner) || top.Owner.IsAlly(g.Owner); } catch { }
                    if (ally) { poi.CoOwners.Add(person(g.Owner)); poi.Claims += g.Claims; }
                    else poi.Contested.Add(person(g.Owner));
                }
                if (poi.Contested.Count > 0) poi.Status = "contested";

                string pair = poi.Key + "|" + poi.Owner.Id;
                livePairs.Add(pair);
                string first;
                if (!since.TryGetValue(pair, out first)) { first = Iso(now); since[pair] = first; stateDirty = true; }
                poi.ClaimedSince = first;
                poi.Owner.ClaimedPois++;
                foreach (var co in poi.CoOwners) co.ClaimedPois++;
            }
            snap.Pois.Add(poi);
        }

        // Forget owners that no longer hold a POI, so a new owner gets a fresh "since".
        foreach (var stale in since.Keys.Where(k => !livePairs.Contains(k)).ToList()) { since.Remove(stale); stateDirty = true; }

        // Players: every owner of an active claim, plus everyone online.
        foreach (var c in claims) person(c.Owner);
        var onlineIds = new HashSet<int>();
        foreach (EntityPlayer ep in world.GetPlayers())
        {
            onlineIds.Add(ep.entityId);
            PersistentPlayerData ppd = lp.GetPlayerDataFromEntityID(ep.entityId);
            if (ppd != null) person(ppd);
        }
        foreach (var kv in people)
        {
            kv.Value.Online = onlineIds.Contains(kv.Key.EntityId);
            kv.Value.LastSeen = Iso(kv.Value.Online ? now : kv.Key.LastLogin);
            snap.Players.Add(kv.Value);
        }
        snap.Players.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        snap.Pois.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        SaveState();
        snap.Signature = Signature(snap);
        snap.Json = ToJson(snap, world, now);
        snap.BuildMs = watch.Elapsed.TotalMilliseconds;
        return snap;
    }

    static string Signature(PrSnapshot s)
    {
        var sb = new StringBuilder();
        foreach (var p in s.Pois)
        {
            sb.Append(p.Id).Append('|').Append(p.Status).Append('|').Append(p.Owner != null ? p.Owner.Id : "-").Append('|').Append(p.Claims);
            foreach (var c in p.CoOwners) sb.Append("+").Append(c.Id);
            foreach (var c in p.Contested) sb.Append("!").Append(c.Id);
            sb.Append(';');
        }
        foreach (var pl in s.Players) sb.Append(pl.Id).Append(',').Append(pl.Name).Append(',').Append(pl.ClaimedPois).Append(';');
        return sb.ToString();
    }

    // ---- JSON (hand-written, so the mod needs no extra DLL) -------------------------------------------------

    static string Q(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string B(bool b) { return b ? "true" : "false"; }
    static string N(int v) { return v.ToString(CultureInfo.InvariantCulture); }

    // Names the page may show. When names are hidden the page labels players by id.
    static string Nm(PrPlayer p) { return PrConfig.ShowNames ? Q(p.Name) : "null"; }

    static string Ref(PrPlayer p)
    {
        return "{ \"id\": " + Q(p.Id) + ", \"name\": " + Nm(p) + " }";
    }

    static string ToJson(PrSnapshot s, World world, DateTime now)
    {
        string day = "null";
        try { day = N(GameUtils.WorldTimeToDays(world.worldTime)); } catch { }
        string worldName = null, version = null;
        try { worldName = GamePrefs.GetString(EnumGamePrefs.GameWorld); } catch { }
        try { version = Constants.cVersionInformation.LongString; } catch { }

        var sb = new StringBuilder(8192);
        sb.Append("{\n  \"schema\": 1,\n");
        sb.Append("  \"server\": { \"name\": ").Append(Q(Safe(PrConfig.ServerName)))
          .Append(", \"world\": ").Append(Q(Safe(worldName))).Append(", \"seed\": null, \"gameVersion\": ").Append(Q(version))
          .Append(", \"day\": ").Append(day).Append(" },\n");
        sb.Append("  \"generatedAt\": ").Append(Q(Iso(now))).Append(",\n");
        sb.Append("  \"settings\": { \"landClaimSize\": ").Append(N(GameStats.GetInt(EnumGameStats.LandClaimSize)))
          .Append(", \"showNames\": ").Append(B(PrConfig.ShowNames))
          .Append(", \"showCoordinates\": ").Append(B(PrConfig.ShowCoordinates)).Append(" },\n");
        sb.Append("  \"pois\": [\n");
        for (int i = 0; i < s.Pois.Count; i++)
        {
            PrPoi p = s.Pois[i];
            bool xy = PrConfig.ShowCoordinates;
            sb.Append("    { \"id\": ").Append(Q(p.Id)).Append(", \"name\": ").Append(Q(p.Name))
              .Append(", \"displayName\": ").Append(Q(p.DisplayName))
              .Append(", \"tier\": ").Append(p.Tier > 0 ? N(p.Tier) : "null")
              .Append(", \"x\": ").Append(xy ? N(p.X) : "null").Append(", \"z\": ").Append(xy ? N(p.Z) : "null")
              .Append(", \"sizeX\": ").Append(xy ? N(p.SizeX) : "null").Append(", \"sizeZ\": ").Append(xy ? N(p.SizeZ) : "null")
              .Append(", \"trader\": ").Append(B(p.Trader))
              .Append(", \"status\": ").Append(Q(p.Status))
              .Append(", \"owner\": ").Append(p.Owner != null ? Ref(p.Owner) : "null")
              .Append(", \"coOwners\": [").Append(string.Join(", ", p.CoOwners.Select(Ref).ToArray())).Append("]")
              .Append(", \"contested\": [").Append(string.Join(", ", p.Contested.Select(Ref).ToArray())).Append("]")
              .Append(", \"claims\": ").Append(N(p.Claims))
              .Append(", \"claimedSince\": ").Append(Q(p.ClaimedSince)).Append(" }")
              .Append(i < s.Pois.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ],\n  \"players\": [\n");
        for (int i = 0; i < s.Players.Count; i++)
        {
            PrPlayer p = s.Players[i];
            sb.Append("    { \"id\": ").Append(Q(p.Id)).Append(", \"name\": ").Append(Nm(p))
              .Append(", \"online\": ").Append(B(p.Online)).Append(", \"lastSeen\": ").Append(Q(p.LastSeen))
              .Append(", \"claimedPois\": ").Append(N(p.ClaimedPois)).Append(" }")
              .Append(i < s.Players.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }
}

public static class PrRunner
{
    // The page takes one post per 5 seconds and answers 429 otherwise.
    const float MinPostGap = 6f;
    // Well past the 10 second request timeout.
    const float StuckPostSeconds = 60f;

    static bool active;
    static bool dirty;
    static bool worldReady;
    static float nextCheck;
    static float nextFull;
    static long lastFingerprint;
    static float lastPostAt = -1e9f;
    static float lastAttemptAt = -1e9f;
    static float nextPostAllowed;
    static int failures;
    static bool posting;
    static string lastPostedSignature;
    static string lastWrittenSignature;
    static string lastLoggedProblem;
    public static PrSnapshot Last;
    public static string PostStatus = "no post yet";

    public static void OnGameStart(ref ModEvents.SGameStartDoneData _d)
    {
        active = PrConfig.Enabled && SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer;
        worldReady = false;
        dirty = true;
        nextCheck = Time.time + 5f;
        if (active) Log.Out(AzraelPoiRegistry.Tag + " Running. " + PrConfig.Describe());
    }

    public static void OnShutdown(ref ModEvents.SGameShutdownData _d)
    {
        active = false;
    }

    public static void OnPlayerSpawned(ref ModEvents.SPlayerSpawnedInWorldData _d) { dirty = true; }
    public static void OnPlayerDisconnected(ref ModEvents.SPlayerDisconnectedData _d) { dirty = true; }

    // Runs every frame but returns at once except every IntervalSeconds, and then only takes the claim
    // fingerprint. Ownership is recalculated only when that changed, a player joined or left, or the
    // heartbeat is due.
    public static void OnUpdate(ref ModEvents.SGameUpdateData _d)
    {
        if (!active) return;
        float now = Time.time;
        if (now < nextCheck) return;

        if (!worldReady)
        {
            // First run after a restart: as soon as the POI list and the claim list exist.
            var gm = GameManager.Instance;
            if (gm == null || gm.World == null || gm.GetDynamicPrefabDecorator() == null || gm.persistentPlayers == null)
            {
                nextCheck = now + 2f;
                return;
            }
            worldReady = true;
        }
        nextCheck = now + PrConfig.IntervalSeconds;

        long fp = PrBuilder.ClaimFingerprint();
        bool full = now >= nextFull;
        if (fp != lastFingerprint || dirty || full)
        {
            lastFingerprint = fp;
            Refresh(false);
        }
        else TryPost(false);
    }

    // Recalculates ownership, writes the file if it changed, and posts if needed.
    public static PrSnapshot Refresh(bool forcePost)
    {
        PrSnapshot snap = null;
        try { snap = PrBuilder.Build(); }
        catch (Exception e) { Log.Error(AzraelPoiRegistry.Tag + " Could not work out ownership: " + e); return Last; }
        if (snap == null) return Last;
        Last = snap;
        dirty = false;
        nextFull = Time.time + PrConfig.HeartbeatMinutes * 60f;

        if (PrConfig.WriteFile && snap.Signature != lastWrittenSignature)
        {
            try
            {
                string tmp = PrConfig.OutputPath + ".tmp";
                File.WriteAllText(tmp, snap.Json, new UTF8Encoding(false));
                if (File.Exists(PrConfig.OutputPath)) File.Delete(PrConfig.OutputPath);
                File.Move(tmp, PrConfig.OutputPath);
                lastWrittenSignature = snap.Signature;
            }
            catch (Exception e) { Log.Warning(AzraelPoiRegistry.Tag + " Could not write poi-owners.json: " + e.Message); }
        }
        TryPost(forcePost);
        return snap;
    }

    // Sends the latest snapshot when it changed or the heartbeat is due, never more often than the page allows,
    // and not before a failed post's wait is over.
    static void TryPost(bool force)
    {
        PrSnapshot snap = Last;
        if (snap == null) return;
        string problem = PrConfig.UrlProblem();
        if (problem != null) { PostStatus = "not pushing: " + problem; return; }
        float now = Time.time;
        // A post can't take longer than its 10 second timeout. If the flag is still set well after that, the
        // coroutine died without finishing (an exception, or its host stopped it), and without this reset the
        // mod would never post again until a restart.
        if (posting && now - lastAttemptAt > StuckPostSeconds)
        {
            posting = false;
            Log.Warning(AzraelPoiRegistry.Tag + " A push never finished; trying again.");
        }
        if (posting || now < nextPostAllowed || now - lastAttemptAt < MinPostGap) return;
        bool changed = snap.Signature != lastPostedSignature;
        bool heartbeat = now - lastPostAt >= PrConfig.HeartbeatMinutes * 60f;
        if (!force && !changed && !heartbeat) return;
        posting = true;
        lastAttemptAt = now;
        PostStatus = "posting (started " + DateTime.Now.ToString("HH:mm:ss") + ")";
        try { GameManager.Instance.StartCoroutine(Post(snap)); }
        catch (Exception e)
        {
            posting = false;
            PostStatus = "failed to start a push: " + e.Message;
            Log.Warning(AzraelPoiRegistry.Tag + " Could not start a push: " + e.Message);
        }
    }

    // UnityWebRequest in a coroutine: the request runs in the background and the game never waits for it.
    static IEnumerator Post(PrSnapshot snap)
    {
        long code = 0;
        string error = null;
        string retryAfter = null;
        using (var req = new UnityWebRequest(PrConfig.PostUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(snap.Json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + PrConfig.Token);
            req.timeout = 10;
            yield return req.SendWebRequest();
            code = req.responseCode;
            error = req.error;
            retryAfter = req.GetResponseHeader("Retry-After");
        }
        posting = false;
        if (code >= 200 && code < 300)
        {
            if (failures > 0) Log.Out(AzraelPoiRegistry.Tag + " Push is working again.");
            failures = 0;
            lastLoggedProblem = null;
            lastPostAt = Time.time;
            lastPostedSignature = snap.Signature;
            PostStatus = "sent OK (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            yield break;
        }

        float wait;
        string problem;
        if (code == 429)
        {
            // The page's rate limit: wait as told and try again, without counting it as a failure.
            int seconds;
            wait = int.TryParse(retryAfter, out seconds) ? Mathf.Clamp(seconds, 5, 300) + 1f : 10f;
            problem = "the page asked to slow down (HTTP 429)";
        }
        else
        {
            failures++;
            wait = Mathf.Min(600f, 30f * (float)Math.Pow(2, Math.Min(failures - 1, 5)));
            problem = code > 0
                ? "HTTP " + code + (code == 401 || code == 403 ? " (is Token the page's ingest token?)" :
                                    code == 400 ? " (the page refused the snapshot)" : "")
                : error + " (can the server reach " + PrConfig.MaskedUrl() + "?)";
        }
        nextPostAllowed = Time.time + wait;
        PostStatus = "failed: " + problem + ", next try in " + (int)wait + "s";
        // One log line per kind of problem, not one per try.
        if (problem != lastLoggedProblem)
        {
            lastLoggedProblem = problem;
            Log.Warning(AzraelPoiRegistry.Tag + " Push failed: " + problem + ". It keeps trying quietly; type poi for the status.");
        }
    }
}

public class ConsoleCmdPoiRegistry : ConsoleCmdAbstract
{
    public override int DefaultPermissionLevel { get { return 0; } }
    public override string[] getCommands() { return new[] { "poi", "poiowners" }; }
    public override string getDescription() { return "POI Registry: who owns which POI (from land claims)"; }
    public override string getHelp()
    {
        return "poi              - summary and push status\n" +
               "poi list [name]  - POIs with an owner (optionally only a player's, part of the name works)\n" +
               "poi here         - the POI you are standing in (in game)\n" +
               "poi now          - recompute and push now (the page allows one post per 5 seconds)\n" +
               "poi reload       - re-read settings.txt";
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (sub == "reload")
        {
            PrConfig.Load();
            con.Output("POI Registry reloaded. " + PrConfig.Describe());
            return;
        }
        PrSnapshot s = sub == "now" ? PrRunner.Refresh(true) : (PrRunner.Last ?? PrRunner.Refresh(false));
        if (s == null) { con.Output("POI Registry has no data yet (the world may still be loading)."); return; }

        if (sub == "list")
        {
            string filter = _params.Count > 1 ? string.Join(" ", _params.Skip(1).ToArray()).ToLowerInvariant() : null;
            int shown = 0;
            foreach (var p in s.Pois)
            {
                if (p.Owner == null) continue;
                if (filter != null && (p.Owner.Name ?? "").ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                con.Output(Line(p));
                shown++;
            }
            con.Output(shown + " claimed POI(s).");
            return;
        }
        if (sub == "here")
        {
            var gm = GameManager.Instance;
            int eid = _senderInfo.RemoteClientInfo != null ? _senderInfo.RemoteClientInfo.entityId : -1;
            Entity e = eid >= 0 && gm.World != null ? gm.World.GetEntity(eid) : null;
            if (e == null) { con.Output("Run this in game, standing in a POI."); return; }
            Vector3i at = World.worldToBlockPos(e.position);
            foreach (var p in s.Pois)
                if (at.x >= p.X && at.x < p.X + p.SizeX && at.z >= p.Z && at.z < p.Z + p.SizeZ) { con.Output(Line(p)); return; }
            con.Output("You are not inside a listed POI.");
            return;
        }

        int claimed = s.Pois.Count(p => p.Status != "unclaimed");
        int contested = s.Pois.Count(p => p.Status == "contested");
        con.Output(s.Pois.Count + " POIs: " + claimed + " claimed (" + contested + " contested), " + (s.Pois.Count - claimed) +
                   " unclaimed. " + s.ClaimCount + " active claims, " + s.Players.Count + " players. Updated " + s.BuiltAt +
                   " (took " + s.BuildMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms).");
        con.Output("Push: " + PrRunner.PostStatus + ". " + PrConfig.Describe());
    }

    static string Line(PrPoi p)
    {
        string owner = p.Owner == null ? "unclaimed" : (p.Owner.Name ?? p.Owner.Id);
        string extra = "";
        if (p.CoOwners.Count > 0) extra += " + " + string.Join(", ", p.CoOwners.Select(c => c.Name ?? c.Id).ToArray());
        if (p.Contested.Count > 0) extra += " CONTESTED by " + string.Join(", ", p.Contested.Select(c => c.Name ?? c.Id).ToArray());
        return p.Name + " (" + p.X + "," + p.Z + "): " + owner + extra;
    }
}
