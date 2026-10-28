using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Keeping the roster safe (see Thralls.cs). The roster is the only place a thrall lives, so losing that one file loses
// every thrall. It is written atomically, a copy is kept outside the save folder (and outside the game and mod folders,
// so a game update, a server reinstall or a mod update can't touch it), and a dated snapshot is taken every day and
// whenever the game version changes. A save whose roster went missing or empty is filled again from that copy.
// Owners are also remembered by every id the game gives them, so a player whose primary id changes keeps their thralls.
public static partial class Thralls
{
    const string OwnersFile = "AzraelBaitOwners.tsv";
    const int KeepSnapshots = 14;
    static bool rosterBroken;   // the save's roster could not be read: never write over it

    // <UserDataFolder>/AzraelBait/<world>/<game>: next to the Saves folder, not inside it.
    static string KeepDir
    {
        get
        {
            return Path.Combine(Path.Combine(Path.Combine(GameIO.GetUserGameDataDir(), "AzraelBait"),
                SafeName(GamePrefs.GetString(EnumGamePrefs.GameWorld))), SafeName(GamePrefs.GetString(EnumGamePrefs.GameName)));
        }
    }
    static string KeepPath { get { return Path.Combine(KeepDir, FileName); } }
    static string OwnersPath { get { return Path.Combine(GameIO.GetSaveGameDir(), OwnersFile); } }

    static string SafeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "_";
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == '/' || c == '\\' ? '_' : c);
        return sb.ToString();
    }

    static string GameVersion()
    {
        try { return Constants.cVersionInformation.LongString; } catch { return "unknown"; }
    }

    // Writes the whole file or nothing: a server stopped halfway through a write (an update, a crash) leaves the old file.
    // The file it replaces is kept as <name>.bak.
    static void WriteSafe(string path, string text)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", true);
            File.Delete(path);
        }
        File.Move(tmp, path);
    }

    // Reads a roster file. False when it exists but could not be read.
    static bool ReadRoster(string path, List<ThrallRecord> into)
    {
        if (!File.Exists(path)) return true;
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception e) { Log.Warning("[Bait] could not read " + path + ": " + e.Message); return false; }
        foreach (string line in lines)
        {
            try
            {
                string[] p = line.Split('\t');
                if (p.Length < 4 || p[0].Length == 0 || p[1].Length == 0) continue;
                var r = new ThrallRecord { Id = p[0], OwnerKey = p[1], ClassName = p[2], Active = p[3] == "1" };
                if (p.Length >= 6) { r.Level = PI(p[4]); r.Kills = PI(p[5]); }
                if (p.Length >= 11) { r.Tether = p[6] == "1"; r.TX = P(p[7]); r.TY = P(p[8]); r.TZ = P(p[9]); r.TR = P(p[10]); }
                if (p.Length >= 15) { r.Name = p[11].Length > 0 ? p[11] : null; r.Stay = p[12] == "1"; r.Mode = PI(p[13]); long ra; if (long.TryParse(p[14], out ra)) r.ReviveAt = ra; }
                if (p.Length >= 16) r.AutoRevive = p[15] == "1";
                if (p.Length >= 17) r.AutoHeal = p[16] == "1";
                if (p.Length >= 25)
                {
                    r.Perks = p[17]; r.Guard = p[18] == "1"; r.Patrol = p[19] == "1"; r.PatrolPts = p[20]; r.BmCalled = p[21] == "1";
                    int bm; r.BmPrevMode = int.TryParse(p[22], out bm) ? bm : -1;
                    r.Deaths = PI(p[23]); long ta; if (long.TryParse(p[24], out ta)) r.TamedAt = ta;
                }
                into.Add(r);
            }
            catch (Exception e) { Log.Warning("[Bait] skipped a bad roster line in " + path + ": " + e.Message); }
        }
        return true;
    }

    // Load: the save's roster, else (missing, empty or unreadable) its .bak, else the kept copy.
    static List<ThrallRecord> LoadRosterRecords()
    {
        var recs = new List<ThrallRecord>();
        rosterBroken = !ReadRoster(FilePath, recs);
        if (recs.Count > 0) return recs;

        string savePath = FilePath;
        bool hadFile = File.Exists(savePath);
        if (!BaitConfig.RestoreRosterFromBackup)
        {
            if (!hadFile && File.Exists(KeepPath))
                Log.Warning("[Bait] this save has no thrall roster, but a kept copy exists at " + KeepPath + ". RestoreRosterFromBackup is off, so it is not used.");
            return recs;
        }
        foreach (string alt in new[] { savePath + ".bak", KeepPath, KeepPath + ".bak" })
        {
            var got = new List<ThrallRecord>();
            if (!ReadRoster(alt, got) || got.Count == 0) continue;
            Log.Warning("[Bait] the thrall roster in the save folder was " + (rosterBroken ? "unreadable" : hadFile ? "empty" : "missing")
                + ". Restored " + got.Count + " thralls from " + alt + ".");
            if (rosterBroken)
            {
                try { File.Copy(savePath, savePath + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); } catch { }
                rosterBroken = false;
            }
            dirty = true;
            return got;
        }
        if (rosterBroken) Log.Warning("[Bait] the thrall roster could not be read and no copy was found. It will not be written over; fix or remove " + savePath + " and restart.");
        return recs;
    }

    // After every roster write: the kept copy, a snapshot per day, and one when the game version changed.
    static void KeepCopy(string text)
    {
        try
        {
            string dir = KeepDir;
            WriteSafe(KeepPath, text);
            string day = Path.Combine(dir, "AzraelBaitThralls-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".tsv");
            if (!File.Exists(day)) File.WriteAllText(day, text);
            string[] snaps = Directory.GetFiles(dir, "AzraelBaitThralls-2*.tsv");
            if (snaps.Length > KeepSnapshots)
            {
                Array.Sort(snaps, StringComparer.Ordinal);
                for (int i = 0; i < snaps.Length - KeepSnapshots; i++) File.Delete(snaps[i]);
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not keep a copy of the roster: " + e.Message); }
    }

    // On load: when the game version is not the one the roster was last kept with, snapshot the roster as it was.
    static void SnapshotOnVersionChange()
    {
        try
        {
            string dir = KeepDir;
            Directory.CreateDirectory(dir);
            string vf = Path.Combine(dir, "GameVersion.txt");
            string now = GameVersion();
            string was = File.Exists(vf) ? File.ReadAllText(vf).Trim() : null;
            if (was == now) return;
            if (was != null && File.Exists(FilePath))
            {
                string snap = Path.Combine(dir, "AzraelBaitThralls-before-" + SafeName(now) + ".tsv");
                File.Copy(FilePath, snap, true);
                Log.Out("[Bait] game version changed from " + was + " to " + now + "; roster saved to " + snap);
            }
            File.WriteAllText(vf, now);
        }
        catch (Exception e) { Log.Warning("[Bait] version snapshot failed: " + e.Message); }
    }

    // ------------------------------------------------------------------ owners
    // primary id -> every id seen for that player (native platform id, older primary ids) and their name
    class Owner { public string Key, Name; public readonly HashSet<string> Ids = new HashSet<string>(); }
    static readonly Dictionary<string, Owner> owners = new Dictionary<string, Owner>();
    static bool ownersDirty;
    static readonly HashSet<int> ownersChecked = new HashSet<int>();

    static void LoadOwners()
    {
        owners.Clear(); ownersChecked.Clear(); ownersDirty = false;
        try
        {
            if (!File.Exists(OwnersPath)) return;
            foreach (string line in File.ReadAllLines(OwnersPath))
            {
                string[] p = line.Split('\t');
                if (p.Length < 2 || p[0].Length == 0) continue;
                var o = new Owner { Key = p[0], Name = p[1] };
                for (int i = 2; i < p.Length; i++) if (p[i].Length > 0) o.Ids.Add(p[i]);
                owners[o.Key] = o;
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read owners: " + e.Message); }
    }

    static void FlushOwners()
    {
        if (!ownersDirty || !loaded) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var o in owners.Values)
            {
                sb.Append(o.Key).Append('\t').Append((o.Name ?? "").Replace('\t', ' '));
                foreach (string id in o.Ids) sb.Append('\t').Append(id);
                sb.Append('\n');
            }
            WriteSafe(OwnersPath, sb.ToString());
            try { File.Copy(OwnersPath, Path.Combine(KeepDir, OwnersFile), true); } catch { }
            ownersDirty = false;
        }
        catch (Exception e) { Log.Warning("[Bait] could not save owners: " + e.Message); }
    }

    // Once per player entity: note all their ids, and move any thralls held under one of their other ids to their key.
    static void CheckOwner(EntityPlayer p, string key)
    {
        if (!ownersChecked.Add(p.entityId)) return;
        if (ownersChecked.Count > 500) { ownersChecked.Clear(); ownersChecked.Add(p.entityId); }
        try
        {
            var pd = GameManager.Instance.GetPersistentPlayerList().GetPlayerDataFromEntityID(p.entityId);
            var ids = new HashSet<string>();
            if (pd != null && pd.NativeId != null) ids.Add(pd.NativeId.CombinedString);
            ids.Remove(key);
            // another key that shares one of these ids is this player under an old primary id
            foreach (var o in new List<Owner>(owners.Values))
            {
                if (o.Key == key) continue;
                bool same = ids.Contains(o.Key);
                foreach (string id in o.Ids) if (id == key || ids.Contains(id)) same = true;
                if (!same) continue;
                ids.Add(o.Key);
                foreach (string id in o.Ids) if (id != key) ids.Add(id);
                owners.Remove(o.Key); ownersDirty = true;
            }

            Owner me;
            if (!owners.TryGetValue(key, out me)) { me = new Owner { Key = key }; owners[key] = me; ownersDirty = true; }
            string name = pd != null ? pd.PlayerName.DisplayName : null;
            if (name != null && me.Name != name) { me.Name = name; ownersDirty = true; }
            foreach (string id in ids) if (me.Ids.Add(id)) ownersDirty = true;

            int moved = 0;
            foreach (string id in me.Ids) moved += MoveRoster(id, key);
            if (moved > 0)
            {
                Log.Out("[Bait] " + (name ?? key) + " now plays as " + key + "; moved " + moved + " thralls over from their other id.");
                Notify(p.entityId, "Your " + moved + " thralls were found under your old player id and are yours again.");
            }
        }
        catch (Exception e) { Dbg("owner check: " + e.Message); }
        FlushOwners();
    }

    // Moves every thrall (and the per-player limits) held under one key to another. Returns how many moved.
    static int MoveRoster(string from, string to)
    {
        List<ThrallRecord> src;
        if (from == to || !roster.TryGetValue(from, out src) || src.Count == 0) return 0;
        var dst = RosterOf(to);
        int n = src.Count;
        foreach (var r in src)
        {
            DespawnLive(r.Id);
            r.OwnerKey = to;
            dst.Add(r);
        }
        roster.Remove(from);
        int[] l;
        if (limits.TryGetValue(from, out l) && !limits.ContainsKey(to)) SetLimits(to, l[0], l[1]);
        if (limits.ContainsKey(from)) SetLimits(from, 0, 0);
        dirty = true;
        return n;
    }

    // Admin: hand a roster to a player, for an owner the game no longer knows (raw key or a name from the owners file).
    static string KeyFromAnything(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (roster.ContainsKey(s)) return s;
        string k = KeyByName(s);
        if (k != null) return k;
        foreach (var o in owners.Values) if (string.Equals(o.Name, s, StringComparison.OrdinalIgnoreCase)) return o.Key;
        foreach (var kv in roster) if (kv.Key.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return null;
    }

    static string OwnerLabel(string key)
    {
        string n = NameOfKey(key);
        Owner o;
        if (n == key && owners.TryGetValue(key, out o) && !string.IsNullOrEmpty(o.Name)) n = o.Name + " (not on this server's player list)";
        return n == key ? key : n;
    }

    // Records whose creature class the game no longer has: kept, never spawned, logged once.
    static readonly HashSet<string> missingClass = new HashSet<string>();
    static bool ClassExists(ThrallRecord r)
    {
        try { if (EntityClass.list.ContainsKey(EntityClass.FromString(r.ClassName))) return true; } catch { }
        if (missingClass.Add(r.ClassName))
            Log.Warning("[Bait] creature class " + r.ClassName + " does not exist in this game version (or a mod that added it is gone). "
                + "Thralls of that class are kept on the roster but cannot be brought out.");
        return false;
    }
}
