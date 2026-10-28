using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// Own taming for Zombie and Animal Bait. Feed a wild zombie or animal enough bait and it becomes your thrall:
// it follows you (or stays inside a tether, for a daycare), fights hostile things and never touches a player.
// Thralls live in a roster file (not in the world save, see ThrallsKeep.cs for how it is kept safe), are spawned back when
// they should be out, and give the owner perk buffs. Wild zombies also carry levels. All server side.

public class ThrallRecord
{
    public string Id, OwnerKey, ClassName;
    public bool Active;
    public int Level, Kills;
    public bool Tether; public float TX, TY, TZ, TR;
    public string Name;      // optional nickname
    public bool Stay;        // hold position instead of following
    public int Mode;         // 0 aggressive, 1 defensive, 2 passive
    public long ReviveAt;    // unix seconds; while in the future the thrall is downed
    public bool AutoRevive;  // when it is downed, pay plain bait from the owner's inventory and bring it straight back
    public bool AutoHeal;    // when it is hurt, pay plain bait from the owner's inventory to heal it
    public string Perks = "";   // one letter per perk slot: T tough, H hit, R regen, A taunt (see ThrallsRoles.cs)
    public bool Guard;       // tethered as a guard post: attacks what comes in, tells the owner
    public bool Patrol;      // tethered on a patrol route (PatrolPts)
    public string PatrolPts = "";   // x,y,z;x,y,z...
    public bool BmCalled;    // a tethered thrall called out for a blood moon; tethered again at dawn
    public int BmPrevMode = -1;     // the mode it had before blood moon mode made it aggressive (-1 = none)
    public int Deaths;
    public long TamedAt;     // unix seconds (0 = tamed before 1.10.0)
}
public class LiveThrall
{
    public int EntityId, OwnerEntityId; public string RecordId, OwnerKey; public bool Hold; public int Mode;
    public string Perks = "";
    public bool Guard; public float GX, GY, GZ, GR;   // guard post or patrol: the circle it defends
}

public static partial class Thralls
{
    public static readonly Dictionary<int, LiveThrall> Live = new Dictionary<int, LiveThrall>();
    static readonly Dictionary<string, int> liveByRecord = new Dictionary<string, int>();
    static readonly Dictionary<string, List<ThrallRecord>> roster = new Dictionary<string, List<ThrallRecord>>();

    class Feed { public int Owner, Count; }
    static readonly Dictionary<int, Feed> feeds = new Dictionary<int, Feed>();
    static readonly HashSet<long> counted = new HashSet<long>();

    public static readonly Dictionary<int, float> TauntedUntil = new Dictionary<int, float>();
    static readonly Dictionary<int, bool> tauntRoll = new Dictionary<int, bool>();
    static readonly HashSet<int> taughtFight = new HashSet<int>();
    static readonly Dictionary<string, float> nextSpawn = new Dictionary<string, float>();
    static readonly List<Entity> tmp = new List<Entity>();

    // levels: entityId -> level (0 = none). Wild zombies also carry a marker buff so the level survives a save.
    static readonly Dictionary<int, int> levels = new Dictionary<int, int>();
    static readonly HashSet<int> scanned = new HashSet<int>();

    static bool dirty, loaded;
    static float nextSlow, nextTaunt;
    const string FileName = "AzraelBaitThralls.tsv";

    public static bool IsServer { get { return SingletonMonoBehaviour<ConnectionManager>.Instance != null && SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer; } }
    static World W { get { return GameManager.Instance != null ? GameManager.Instance.World : null; } }
    public static bool IsThrall(int entityId) { return Live.ContainsKey(entityId); }

    // Every entity id that has ever been a thrall in this save (kept in AzraelBaitThrallIds.txt).
    // A tethered thrall whose area unloads is dropped from the world but stays in the chunk the game keeps in memory, and
    // the chunk respawns it with the same entity id when someone comes back, while the roster spawns a fresh one too.
    // Such an entity must never be saved or restored, and a copy that still turns up is removed.
    static readonly HashSet<int> pastIds = new HashSet<int>();
    static bool idsDirty;
    public static bool NeverSave(int entityId) { return Live.ContainsKey(entityId) || pastIds.Contains(entityId); }
    public static bool OwnedBy(int thrallEntityId, int playerEntityId)
    {
        LiveThrall lt; return Live.TryGetValue(thrallEntityId, out lt) && lt.OwnerEntityId == playerEntityId;
    }
    // Player damage rules: the owner only with OwnerCanHurtThralls, anyone else only with OthersCanHurtThralls.
    public static bool PlayerMayHurt(int thrallEntityId, int playerEntityId)
    {
        return OwnedBy(thrallEntityId, playerEntityId) ? BaitConfig.OwnerCanHurtThralls : BaitConfig.OthersCanHurtThralls;
    }
    public static int LevelOf(int entityId) { int l; return levels.TryGetValue(entityId, out l) ? l : 0; }
    static void Dbg(string m) { if (BaitConfig.Debug) Log.Out("[Bait] " + m); }

    // ------------------------------------------------------------------ names, tiers, keys
    static bool IsAnimalClass(string cls) { return cls != null && cls.StartsWith("animal", StringComparison.OrdinalIgnoreCase); }
    static int TierOf(string cls)
    {
        if (IsAnimalClass(cls)) return 0;
        string c = (cls ?? "").ToLowerInvariant();
        if (c.EndsWith("infernal") || c.EndsWith("charged")) return 3;
        if (c.EndsWith("radiated")) return 2;
        if (c.EndsWith("feral")) return 1;
        return 0;
    }
    public static int FeedsNeeded(string cls, int level)
    {
        int extra = BaitConfig.FeedsPerLevelStep > 0 ? level / BaitConfig.FeedsPerLevelStep : 0;
        int need;
        if (IsAnimalClass(cls)) need = BaitConfig.FeedsForAnimal(cls) + extra;
        else
        {
            int t = TierOf(cls);
            need = extra + BaitConfig.FeedsToTame + (t == 3 ? BaitConfig.ChargedBonus : t == 2 ? BaitConfig.RadiatedBonus : t == 1 ? BaitConfig.FeralBonus : 0);
        }
        return BaitConfig.MaxFeeds > 0 ? Math.Min(need, BaitConfig.MaxFeeds) : need;
    }
    public static string Pretty(string cls)
    {
        string c = cls ?? "zombie";
        if (c.StartsWith("zombie", StringComparison.OrdinalIgnoreCase)) c = c.Substring(6);
        else if (c.StartsWith("animal", StringComparison.OrdinalIgnoreCase)) c = c.Substring(6);
        string tier = "";
        foreach (string s in new[] { "Infernal", "Charged", "Radiated", "Feral" })
            if (c.EndsWith(s, StringComparison.OrdinalIgnoreCase)) { tier = s.ToLowerInvariant(); c = c.Substring(0, c.Length - s.Length); break; }
        if (c.Length == 0) c = "Zombie";
        return tier.Length > 0 ? c + " (" + tier + ")" : c;
    }
    static string ClassName(EntityAlive e) { return EntityClass.list[e.entityClass].entityClassName; }
    static string Label(ThrallRecord r)
    {
        string b = string.IsNullOrEmpty(r.Name) ? Pretty(r.ClassName) : r.Name + " (" + Pretty(r.ClassName) + ")";
        return b + (r.Level > 0 ? " Lv" + r.Level : "");
    }

    public static string KeyOf(EntityPlayer p)
    {
        try
        {
            var pl = GameManager.Instance.GetPersistentPlayerList();
            var d = pl != null ? pl.GetPlayerDataFromEntityID(p.entityId) : null;
            return d != null && d.PrimaryId != null ? d.PrimaryId.CombinedString : null;
        }
        catch { return null; }
    }

    // Events show as a vanilla on-screen toast (the client already knows how to show it, no mod needed there).
    // Command replies stay in chat. Falls back to chat if the toast cannot be sent.
    // Toasts are shown one after another by the client (a few seconds each), so a burst of them queues up and arrives late.
    // Low-priority ones (look-at HP) are dropped when a toast went to that player a moment ago.
    static readonly Dictionary<int, float> lastToast = new Dictionary<int, float>();
    public static bool NotifyLow(int entityId, string msg)
    {
        float last;
        if (lastToast.TryGetValue(entityId, out last) && Time.time - last < 6f) return false;
        Notify(entityId, msg);
        return true;
    }

    public static void Notify(int entityId, string msg)
    {
        if (entityId < 0) return;
        lastToast[entityId] = Time.time;
        Dbg("toast #" + entityId + ": " + msg);
        if (BaitConfig.UseToasts)
        {
            try
            {
                var p = W != null ? W.GetEntity(entityId) as EntityPlayer : null;
                if (p != null) { GameManager.ShowTooltipMP(p, msg); return; }
            }
            catch (Exception e) { Dbg("toast failed: " + e.Message); }
        }
        Tell(entityId, msg);
    }
    public static void Tell(int entityId, string msg)
    {
        if (entityId < 0) return;
        try
        {
            GameManager.Instance.ChatMessageServer(null, EChatType.Whisper, -1, "[ffd966][Bait][-] " + msg,
                new List<int> { entityId }, EMessageSender.Server);
        }
        catch (Exception e) { Log.Warning("[Bait] chat reply failed: " + e.Message); }
    }

    // ------------------------------------------------------------------ roster file
    static string FilePath { get { return Path.Combine(GameIO.GetSaveGameDir(), FileName); } }
    static string IdsPath { get { return Path.Combine(GameIO.GetSaveGameDir(), "AzraelBaitThrallIds.txt"); } }

    static void LoadIds()
    {
        pastIds.Clear(); idsDirty = false;
        try
        {
            if (File.Exists(IdsPath))
                foreach (string line in File.ReadAllLines(IdsPath)) { int id; if (int.TryParse(line.Trim(), out id)) pastIds.Add(id); }
        }
        catch (Exception e) { Log.Warning("[Bait] could not load thrall ids: " + e.Message); }
    }

    static void FlushIds()
    {
        if (!idsDirty || !loaded) return;
        try
        {
            if (pastIds.Count > 20000)   // ids only grow, so the oldest are the smallest
            {
                var all = new List<int>(pastIds); all.Sort();
                for (int i = 0; i < all.Count - 15000; i++) if (!Live.ContainsKey(all[i])) pastIds.Remove(all[i]);
            }
            var sb = new StringBuilder();
            foreach (int id in pastIds) sb.Append(id).Append('\n');
            File.WriteAllText(IdsPath, sb.ToString());
            idsDirty = false;
        }
        catch (Exception e) { Log.Warning("[Bait] could not save thrall ids: " + e.Message); }
    }

    // Takes out any entity that was a thrall but is no longer one: a copy the game brought back from a cached chunk or the save.
    static void RemoveStaleCopies(World world)
    {
        if (pastIds.Count == 0) return;
        foreach (var en in new List<Entity>(world.Entities.list))
        {
            var e = en as EntityAlive;
            if (e == null || e is EntityPlayer || Live.ContainsKey(e.entityId) || !pastIds.Contains(e.entityId)) continue;
            Log.Out("[Bait] removing a stale copy of a former thrall: " + ClassName(e) + " #" + e.entityId + " at " + e.position);
            RemoveFromWorld(e);
        }
    }
    static string F(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
    static float P(string s) { float r; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : 0f; }
    static int PI(string s) { int r; return int.TryParse(s, out r) ? r : 0; }

    public static void Load()
    {
        roster.Clear(); dirty = false;
        missingClass.Clear();
        BaitConfig.LoadSaved();
        LoadLimits();
        LoadIds();
        LoadOwners();
        LoadTeams();
        LoadPlayers();
        LoadLedger();
        SnapshotOnVersionChange();
        try
        {
            foreach (var r in LoadRosterRecords()) { RosterOf(r.OwnerKey).Add(r); ClassExists(r); }
            Log.Out("[Bait] Thrall roster loaded: " + CountAll() + " tamed creatures (" + FilePath + ", kept copy in " + KeepDir + ").");
        }
        catch (Exception e) { rosterBroken = true; Log.Warning("[Bait] could not load roster: " + e.Message); }
        loaded = true;
        if (dirty) Flush();
    }

    static int CountAll() { int n = 0; foreach (var kv in roster) n += kv.Value.Count; return n; }

    public static void Flush()
    {
        FlushIds();
        FlushOwners();
        FlushTeams();
        FlushPlayers();
        FlushLedger();
        if (!dirty || !loaded || rosterBroken) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in roster)
                foreach (var r in kv.Value)
                    sb.Append(r.Id).Append('\t').Append(r.OwnerKey).Append('\t').Append(r.ClassName).Append('\t').Append(r.Active ? "1" : "0")
                      .Append('\t').Append(r.Level).Append('\t').Append(r.Kills).Append('\t').Append(r.Tether ? "1" : "0")
                      .Append('\t').Append(F(r.TX)).Append('\t').Append(F(r.TY)).Append('\t').Append(F(r.TZ)).Append('\t').Append(F(r.TR))
                      .Append('\t').Append(r.Name ?? "").Append('\t').Append(r.Stay ? "1" : "0").Append('\t').Append(r.Mode).Append('\t').Append(r.ReviveAt)
                      .Append('\t').Append(r.AutoRevive ? "1" : "0").Append('\t').Append(r.AutoHeal ? "1" : "0")
                      .Append('\t').Append(r.Perks ?? "").Append('\t').Append(r.Guard ? "1" : "0").Append('\t').Append(r.Patrol ? "1" : "0").Append('\t').Append(r.PatrolPts ?? "")
                      .Append('\t').Append(r.BmCalled ? "1" : "0").Append('\t').Append(r.BmPrevMode).Append('\t').Append(r.Deaths).Append('\t').Append(r.TamedAt).Append('\n');
            string text = sb.ToString();
            WriteSafe(FilePath, text);
            KeepCopy(text);
            dirty = false;
        }
        catch (Exception e) { Log.Warning("[Bait] could not save roster: " + e.Message); }
    }

    static List<ThrallRecord> RosterOf(string key)
    {
        List<ThrallRecord> l;
        if (!roster.TryGetValue(key, out l)) { l = new List<ThrallRecord>(); roster[key] = l; }
        return l;
    }
    static ThrallRecord Find(string id)
    {
        foreach (var kv in roster) foreach (var r in kv.Value) if (r.Id == id) return r;
        return null;
    }
    static int ActiveCount(string key)
    {
        int n = 0;
        foreach (var r in RosterOf(key)) if (r.Active && (BaitConfig.TetheredCountsActive || !r.Tether)) n++;
        return n;
    }
    static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 8); }

    // ------------------------------------------------------------------ levels
    // The level rides on the zombie as a custom variable, which the game saves with the entity.
    const string LevelVar = "azraelZLvl";

    static void ApplyMarker(EntityAlive e, int level)
    {
        try { if (e.Buffs != null) e.Buffs.SetCustomVar(LevelVar, level, false); } catch { }
    }

    static int MarkerLevel(EntityAlive e)
    {
        try { return e.Buffs != null ? (int)e.Buffs.GetCustomVar(LevelVar) : 0; } catch { return 0; }
    }

    // Give new wild zombies their level. Day sets the floor, the tier adds to it, one point of luck either way.
    static void ScanZombies(World world)
    {
        if (!BaitConfig.ZombieLevels) return;
        var ents = new List<Entity>(world.Entities.list);
        var seen = new HashSet<int>();
        int day = GameUtils.WorldTimeToDays(world.worldTime);
        foreach (var o in ents)
        {
            var z = o as EntityZombie;
            if (z == null || z.IsDead()) continue;
            seen.Add(z.entityId);
            if (scanned.Contains(z.entityId) || Live.ContainsKey(z.entityId)) continue;
            scanned.Add(z.entityId);
            if (BaitConfig.Exclude.Contains(ClassName(z))) continue;
            int lvl = MarkerLevel(z);
            if (lvl == 0)
            {
                int dpl = Math.Max(1, BaitConfig.DaysPerLevel);
                lvl = day / dpl + TierOf(ClassName(z)) * BaitConfig.TierLevelBonus + UnityEngine.Random.Range(0, 3);
                lvl = Math.Max(1, Math.Min(BaitConfig.MaxLevel, lvl));   // every wild zombie starts at level 1 or more
                if (lvl > 0) ApplyMarker(z, lvl);
            }
            if (lvl > 0) levels[z.entityId] = Math.Min(lvl, BaitConfig.MaxLevel);
        }
        scanned.RemoveWhere(id => !seen.Contains(id));
        var gone = new List<int>();
        foreach (int id in levels.Keys) if (!seen.Contains(id) && !Live.ContainsKey(id) && world.GetEntity(id) == null) gone.Add(id);
        foreach (int id in gone) levels.Remove(id);
    }

    // Wild animals don't have the bait task (it used to be injected by XML, which wiped their attack/flee settings), so it
    // is added in code instead: just ahead of Look/Wander/ApproachSpot, so attacking and fleeing still come first.
    static readonly HashSet<int> baitAiDone = new HashSet<int>();
    static void EnsureBaitAi(World world)
    {
        foreach (var o in new List<Entity>(world.Entities.list))
        {
            var an = o as EntityAlive;
            if (!(an is EntityAnimal || an is EntityEnemy)) continue;
            if (an == null || an.IsDead() || an.aiManager == null || baitAiDone.Contains(an.entityId)) continue;
            baitAiDone.Add(an.entityId);
            if (Live.ContainsKey(an.entityId)) continue;
            string cls = ClassName(an);
            if (BaitConfig.Exclude.Contains(cls)) continue;
            try
            {
                var tl = an.aiManager.tasks;
                if (tl.GetTask<EAIApproachDistraction>() != null) continue;
                EAITaskEntry before = null;
                foreach (var en in tl.Tasks)
                    if (en.action is EAIApproachSpot || en.action is EAILook || en.action is EAIWander) { before = en; break; }
                var t = new EAIApproachDistraction();
                t.Init(an);
                tl.AddTask(before != null ? before.priority : 99, t);
                if (before != null)
                {
                    var added = tl.Tasks[tl.Tasks.Count - 1];
                    tl.Tasks.RemoveAt(tl.Tasks.Count - 1);
                    int at = tl.Tasks.IndexOf(before);
                    tl.Tasks.Insert(at < 0 ? tl.Tasks.Count : at, added);
                }
                Dbg("bait task added to " + cls + " #" + an.entityId);
            }
            catch (Exception e) { Dbg("bait ai: " + e.Message); }
        }
        if (baitAiDone.Count > 4000) baitAiDone.Clear();
    }
    static void LevelUp(EntityAlive e)
    {
        int cur = LevelOf(e.entityId);
        if (cur >= BaitConfig.MaxLevel) return;
        levels[e.entityId] = cur + 1;
        if (!Live.ContainsKey(e.entityId)) ApplyMarker(e, cur + 1);
        Dbg(ClassName(e) + " #" + e.entityId + " is now level " + (cur + 1));
    }

    // ------------------------------------------------------------------ feeding and taming
    static bool CanTame(EntityAlive z)
    {
        if (!(z is EntityEnemy) && !(z is EntityAnimal)) return false;
        if (z.IsDead() || Live.ContainsKey(z.entityId)) return false;
        return !BaitConfig.Exclude.Contains(ClassName(z));
    }

    // Called every update of a creature that is going for bait. A meal counts when the pile is used up.
    public static void OnBaitUpdate(EntityAlive z)
    {
        if (!IsServer || !loaded || z == null) return;
        var d = z.distraction;
        if (d == null || d.IsDead() || d.itemClass == null || !BaitCheck.IsBait(d.itemClass.GetItemName())) return;
        if (!z.IsEating || d.distractionEatTicks > 0) return;
        long key = ((long)d.entityId << 32) | (uint)z.entityId;
        if (!counted.Add(key)) return;
        CountMeal(z, d.OwnerId, BaitCheck.Meals(d.itemClass.GetItemName()));
    }

    static void CountMeal(EntityAlive z, int ownerId, int meals)
    {
        var world = W;
        var owner = world != null ? world.GetEntity(ownerId) as EntityPlayer : null;
        if (owner == null || owner.IsDead() || !CanTame(z)) return;
        string cls = ClassName(z);
        Feed f;
        if (!feeds.TryGetValue(z.entityId, out f) || f.Owner != ownerId) { f = new Feed { Owner = ownerId }; feeds[z.entityId] = f; }
        f.Count += Math.Max(1, meals);
        int need = FeedsNeeded(cls, LevelOf(z.entityId));
        UiFeed(ownerId, z.entityId, Math.Min(f.Count, need), need);
        if (f.Count < need) { Notify(ownerId, Pretty(cls) + " fed " + f.Count + "/" + need + (meals > 1 ? " (+" + meals + ")" : "") + "."); return; }

        string key = KeyOf(owner);
        if (key == null) return;
        var list = RosterOf(key);
        if (list.Count >= MaxOwnedFor(key))
        {
            f.Count = need - 1;
            Notify(ownerId, "You already hold " + list.Count + " thralls (limit " + MaxOwnedFor(key) + "). Release one first: /thrall release <number>");
            return;
        }
        var rec = new ThrallRecord { Id = NewId(), OwnerKey = key, ClassName = cls, Level = LevelOf(z.entityId), Active = ActiveCount(key) < MaxActiveFor(key), TamedAt = NowSec() };
        WebhookTamed(owner, rec);
        list.Add(rec);
        dirty = true;
        feeds.Remove(z.entityId);
        // A zombie from a POI sleeper volume or an infestation counts as dead for that volume once tamed.
        try { z.NotifySleeperDeath(); } catch (Exception ex) { Dbg("sleeper notify failed: " + ex.Message); }
        if (rec.Active)
        {
            if (z.IsSleeper)
            {
                // Clients keep the "sleeper" flag (red dot on the quest map) of the entity they were sent, so
                // swap it for a fresh thrall at the same spot.
                Vector3 at = z.position;
                RemoveFromWorld(z);
                Spawn(rec, owner, at);
            }
            else MakeLive(z, rec, owner);
            Notify(ownerId, Label(rec) + " is yours now. /thrall help");
        }
        else
        {
            RemoveFromWorld(z);
            Notify(ownerId, Label(rec) + " is yours, but you already have " + MaxActiveFor(key) + " active, so it is inactive. /thrall call " + list.Count);
        }
        Flush();
    }

    // ------------------------------------------------------------------ making a live thrall
    // Newly made thralls can't be targeted or hurt for a few seconds, so they can walk back to their owner.
    public static readonly Dictionary<int, float> Grace = new Dictionary<int, float>();
    public static bool InGrace(int entityId)
    {
        LiveThrall lt;
        if (Live.TryGetValue(entityId, out lt) && lt.Mode == 3) return true;   // invulnerable mode: untargetable by everything, for good
        float u;
        return Grace.Count > 0 && Grace.TryGetValue(entityId, out u) && Time.time < u;
    }

    static void StartGrace(EntityAlive z)
    {
        if (BaitConfig.NewThrallGraceSeconds <= 0f) return;
        if (Grace.Count > 200) Grace.Clear();
        Grace[z.entityId] = Time.time + BaitConfig.NewThrallGraceSeconds;
        ClearAttackers(z);
    }

    // anything already hunting this thrall lets go
    static void ClearAttackers(EntityAlive z)
    {
        try
        {
            foreach (var other in W.Entities.list)
            {
                var a = other as EntityAlive;
                if (a == null || a == z || a is EntityPlayer || a.IsDead()) continue;
                if (a.GetAttackTarget() == z && (a.position - z.position).sqrMagnitude < 60f * 60f) a.SetAttackTarget(null, 0);
            }
        }
        catch (Exception ex) { Dbg("clear attackers failed: " + ex.Message); }
    }

    public const string WardBuff = "azraelThrallWard";

    // Run when a thrall is made and every few seconds after.
    // Zombies (EntityHuman) and zombie dogs carry a lifetime from the game: once world time passes timeToDie and they have
    // no target, the game kills them outright, with no damage and no hit. A static spawn gets only 4000-8000 ticks
    // (a few game hours), so thralls "randomly died". A thrall has no lifetime.
    // The ward buff makes player hits do nothing on the attacker's own screen too (the server blocks them anyway), so a
    // client never sees a thrall die that is still alive on the server. Only when no player may hurt thralls at all.
    static void KeepSafe(EntityAlive z)
    {
        try
        {
            var h = z as EntityHuman; if (h != null) h.timeToDie = ulong.MaxValue;
            var d = z as EntityZombieDog; if (d != null) d.timeToDie = ulong.MaxValue;
            if (z.Buffs == null || z.IsDead()) return;
            bool want = !BaitConfig.OwnerCanHurtThralls && !BaitConfig.OthersCanHurtThralls;
            bool has = z.Buffs.HasBuff(WardBuff);
            if (want && !has) z.Buffs.AddBuff(WardBuff);
            else if (!want && has) z.Buffs.RemoveBuff(WardBuff);
        }
        catch (Exception ex) { Dbg("keep safe failed: " + ex.Message); }
    }

    static void MakeLive(EntityAlive z, ThrallRecord rec, EntityPlayer owner)
    {
        // no longer a POI sleeper: no red quest dot on the map, no leash back to the sleeper volume
        z.IsSleeper = false; z.IsSleeperPassive = false;
        StartGrace(z);
        if (pastIds.Add(z.entityId)) idsDirty = true;
        Live[z.entityId] = new LiveThrall { EntityId = z.entityId, OwnerEntityId = owner != null ? owner.entityId : -1, RecordId = rec.Id, OwnerKey = rec.OwnerKey, Hold = rec.Stay, Mode = rec.Mode,
                                           Perks = rec.Perks ?? "", Guard = rec.Tether && (rec.Guard || rec.Patrol), GX = rec.TX, GY = rec.TY, GZ = rec.TZ, GR = rec.TR };
        liveByRecord[rec.Id] = z.entityId;
        if (rec.Level < 1 && z is EntityZombie) { rec.Level = 1; dirty = true; }   // a zombie thrall is never level 0 (older ones are brought up to 1 here)
        if (rec.Level > 0) levels[z.entityId] = rec.Level; else levels.Remove(z.entityId);

        z.distraction = null;
        z.pendingDistraction = null;
        z.IsEating = false;
        z.SetAttackTarget(null, 0);
        z.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
        KeepSafe(z);

        var ai = z.aiManager;
        if (ai == null) return;
        bool animal = z is EntityAnimal;
        foreach (var entry in new List<EAITaskEntry>(ai.tasks.Tasks))
        {
            var a = entry.action;
            bool drop;
            if (animal) drop = !(a is EAIApproachAndAttackTarget) && !(a is EAILook);
            else drop = a is EAIBreakBlock || a is EAIDestroyArea || a is EAITerritorial || a is EAIApproachSpot
                        || a is EAIApproachDistraction || a is EAIWander;
            if (drop)
            {
                try { a.Reset(); } catch { }
                ai.tasks.RemoveTask(entry);
            }
        }
        EAIBase move;
        var pts = rec.Patrol ? PatrolPoints(rec) : null;
        if (rec.Tether && pts != null && pts.Count >= 2) move = new EAIPatrol(pts);
        else if (rec.Tether) move = new EAITether(new Vector3(rec.TX, rec.TY, rec.TZ), rec.TR);
        else move = new EAIFollowMaster();
        move.Init(z);
        ai.tasks.AddTask(6, move);
        var scout = new EAIScout();
        scout.Init(z);
        ai.tasks.AddTask(5, scout);

        var attack = ai.GetTasks<EAIApproachAndAttackTarget>();
        if (attack != null) foreach (var t in attack) SetClasses(t, "targetClasses", new[] { typeof(EntityEnemy), typeof(EntityEnemyAnimal), typeof(EntityBandit) }, false);
        var seek = ai.GetTargetTasks<EAISetNearestEntityAsTarget>();
        if (seek != null) foreach (var t in seek)
            {
                SetClasses(t, "targetClasses", new[] { typeof(EntityEnemy), typeof(EntityEnemyAnimal), typeof(EntityBandit) }, true);
                var fi = typeof(EAISetNearestEntityAsTarget).GetField("playerTargetClassIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null) fi.SetValue(t, -1);
            }
        Dbg("thrall made: " + ClassName(z) + " #" + z.entityId);
    }

    // Replace the TargetClass list on an AI task (the element type is a private struct, so reflection).
    static void SetClasses(object task, string field, Type[] types, bool nearestTarget)
    {
        var fi = task.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var list = fi != null ? fi.GetValue(task) as IList : null;
        if (list == null) return;
        Type elem = list.GetType().GetGenericArguments()[0];
        list.Clear();
        foreach (Type ty in types)
        {
            object e = Activator.CreateInstance(elem);
            elem.GetField("type").SetValue(e, ty);
            if (nearestTarget) { elem.GetField("hearDistMax").SetValue(e, 50f); elem.GetField("seeDistMax").SetValue(e, 0f); }
            else elem.GetField("chaseTimeMax").SetValue(e, 0f);
            list.Add(e);
        }
    }

    // A wild zombie only attacks what its AI list names, and no zombie's list names zombies.
    static void TeachFightBack(EntityAlive z)
    {
        if (!taughtFight.Add(z.entityId) || z.aiManager == null) return;
        var attack = z.aiManager.GetTasks<EAIApproachAndAttackTarget>();
        if (attack == null) return;
        foreach (var t in attack)
        {
            var fi = t.GetType().GetField("targetClasses", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var list = fi != null ? fi.GetValue(t) as IList : null;
            if (list == null) continue;
            Type elem = list.GetType().GetGenericArguments()[0];
            bool has = false;
            foreach (object o in list) if ((Type)elem.GetField("type").GetValue(o) == typeof(EntityEnemy)) has = true;
            if (has) continue;
            object e = Activator.CreateInstance(elem);
            elem.GetField("type").SetValue(e, typeof(EntityEnemy));
            elem.GetField("chaseTimeMax").SetValue(e, 0f);
            list.Add(e);
        }
    }

    // ------------------------------------------------------------------ world helpers
    static void RemoveFromWorld(EntityAlive z)
    {
        try { var w = W; if (w != null) w.RemoveEntity(z.entityId, EnumRemoveEntityReason.Despawned); }
        catch (Exception e) { Log.Warning("[Bait] despawn failed: " + e.Message); }
    }

    static void ForgetLive(int entityId)
    {
        LiveThrall lt;
        if (Live.TryGetValue(entityId, out lt)) { liveByRecord.Remove(lt.RecordId); Live.Remove(entityId); levels.Remove(entityId); Grace.Remove(entityId); }
    }

    static void DespawnLive(string recordId)
    {
        int id;
        if (!liveByRecord.TryGetValue(recordId, out id)) return;
        var e = W != null ? W.GetEntity(id) as EntityAlive : null;
        ForgetLive(id);
        if (e != null) RemoveFromWorld(e);
    }

    static void KillLive(string recordId)
    {
        int id;
        if (!liveByRecord.TryGetValue(recordId, out id)) return;
        var e = W != null ? W.GetEntity(id) as EntityAlive : null;
        ForgetLive(id);
        if (e != null && !e.IsDead())
        {
            NoHarvest(e);
            e.DamageEntity(new DamageSource(EnumDamageSource.Internal, EnumDamageTypes.Suicide), 99999, false, 1f);
            ClearBody(e);
        }
    }

    static bool ChunkLoaded(Vector3 p)
    {
        var w = W;
        return w != null && w.GetChunkFromWorldPos(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z)) != null;
    }

    static void Spawn(ThrallRecord rec, EntityPlayer owner, Vector3? at = null)
    {
        try
        {
            // Called while its item is still in the inventory: take the item back so there is only one of it.
            var cr = carried.Find(x => x.RecId == rec.Id);
            if (cr != null)
            {
                carried.Remove(cr);
                var ci = EntityClass.list[EntityClass.FromString(rec.ClassName)];
                if (!string.IsNullOrEmpty(ci.PickupItem)) grabItems.Add(new GrabItem { PlayerId = cr.PlayerId, Item = ci.PickupItem, At = Time.time });
            }
            if (!ClassExists(rec)) return;
            if (rec.ReviveAt != 0) { rec.ReviveAt = 0; dirty = true; }
            int cid = EntityClass.FromString(rec.ClassName);
            Vector3 pos;
            if (at.HasValue) pos = at.Value + new Vector3(0f, 0.3f, 0f);
            else if (rec.Tether && rec.Patrol && PatrolPoints(rec).Count > 0) pos = PatrolPoints(rec)[0] + new Vector3(0f, 0.5f, 0f);
            else if (rec.Tether) pos = new Vector3(rec.TX, rec.TY + 0.5f, rec.TZ);
            else if (owner != null) pos = BehindOwner(owner);
            else return;
            var e = EntityFactory.CreateEntity(cid, pos, Vector3.zero) as EntityAlive;
            if (e == null) { Log.Warning("[Bait] could not create " + rec.ClassName); return; }
            e.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            W.SpawnEntityInWorld(e);
            MakeLive(e, rec, owner);
        }
        catch (Exception ex) { Log.Warning("[Bait] spawn failed for " + rec.ClassName + ": " + ex.Message); }
    }

    // ------------------------------------------------------------------ kills and death
    public static void OnEntityKilled(ref ModEvents.SEntityKilledData data)
    {
        try
        {
            var victim = data.KilledEntitiy as EntityAlive;
            var killer = data.KillingEntity as EntityAlive;
            if (victim == null) return;

            LiveThrall klt;
            if (killer != null && Live.TryGetValue(killer.entityId, out klt) && !(victim is EntityPlayer) && !Live.ContainsKey(victim.entityId))
                ThrallKilledSomething(klt, killer, victim, false);
            else if (killer is EntityPlayer && !(victim is EntityPlayer) && !Live.ContainsKey(victim.entityId))
                ShareKill((EntityPlayer)killer, victim);
            else if (killer is EntityZombie && !Live.ContainsKey(killer.entityId) && BaitConfig.ZombieLevels
                     && (victim is EntityPlayer || Live.ContainsKey(victim.entityId)))
                LevelUp(killer);

            LiveThrall lt;
            if (!Live.TryGetValue(victim.entityId, out lt)) return;
            var rec = Find(lt.RecordId);
            ForgetLive(victim.entityId);
            NoHarvest(victim);
            ClearBody(victim);
            string why = DeathReason(victim, killer);
            if (rec != null)
            {
                Log.Out("[Bait] thrall died: " + Label(rec) + " (#" + victim.entityId + ") " + why + " at " + victim.position);
                LogDeath(rec, why);
                rec.Deaths++;
                var downedOwner = W != null ? W.GetEntity(lt.OwnerEntityId) as EntityPlayer : null;
                if (downedOwner != null && TryAutoRevive(rec, downedOwner, why)) { Flush(); return; }
                if (BaitConfig.ReviveSeconds > 0)
                {
                    rec.ReviveAt = NowSec() + BaitConfig.ReviveSeconds;
                    nextSpawn.Remove(rec.Id);
                    Notify(lt.OwnerEntityId, Label(rec) + " died: " + why + ". It rises again in " + BaitConfig.ReviveSeconds + "s, or /thrall revive "
                        + (RosterOf(rec.OwnerKey).IndexOf(rec) + 1) + " (" + ReviveCostText() + ")."
                        + (rec.AutoRevive && BaitConfig.AutoRevive ? " Auto-revive is on: it comes back as soon as you carry " + AutoReviveCostText() + "." : ""));
                }
                else
                {
                    RosterOf(rec.OwnerKey).Remove(rec);
                    Notify(lt.OwnerEntityId, Label(rec) + " died: " + why + ". It is gone for good.");
                }
                dirty = true;
                Flush();
            }
        }
        catch (Exception ex) { Log.Warning("[Bait] kill handler: " + ex.Message); }
    }

    // Butchering a body is worked out on the attacking client from its own copy (GameUtils.HarvestOnAttack), so the
    // server can't refuse it. The body is taken out of the world instead, half a second after death.
    static readonly Dictionary<int, float> bodies = new Dictionary<int, float>();
    static void ClearBody(EntityAlive v) { bodies[v.entityId] = Time.time + 0.5f; }
    static void ClearBodies(World world, float now)
    {
        if (bodies.Count == 0) return;
        foreach (var kv in new List<KeyValuePair<int, float>>(bodies))
        {
            if (now < kv.Value) continue;
            bodies.Remove(kv.Key);
            if (Live.ContainsKey(kv.Key)) continue;
            var e = world.GetEntity(kv.Key) as EntityAlive;
            if (e != null && e.IsDead()) RemoveFromWorld(e);
        }
    }

    // A fallen thrall comes back from its record, so its body gives nothing: no loot bag, nothing to butcher or search.
    static void NoHarvest(EntityAlive v)
    {
        try
        {
            v.lootDropProb = 0f;
            if (v.bag != null) { v.bag.Clear(); v.bag.ItemGrid.Touch(); }
        }
        catch (Exception ex) { Dbg("no-harvest failed: " + ex.Message); }
    }

    // Thralls level up from kills. A thrall that is out with its owner shares in the owner's kills of hostile creatures
    // (so a POI cleared mostly by the player still counts), as long as it or the owner is within OwnerKillShareRange.
    static void ShareKill(EntityPlayer owner, EntityAlive victim)
    {
        float r = BaitConfig.OwnerKillShareRange;
        if (r <= 0f || owner == null || victim == null || W == null) return;
        if (victim.EntityClass == null || !victim.EntityClass.bIsEnemyEntity) return;
        foreach (var kv in new List<KeyValuePair<int, LiveThrall>>(Live))
        {
            if (kv.Value.OwnerEntityId != owner.entityId) continue;
            var th = W.GetEntity(kv.Key) as EntityAlive;
            if (th == null || th.IsDead()) continue;
            if ((th.position - victim.position).sqrMagnitude > r * r && (th.position - owner.position).sqrMagnitude > r * r) continue;
            ThrallKilledSomething(kv.Value, th, victim, true);
        }
    }
    static void ThrallKilledSomething(LiveThrall lt, EntityAlive killer, EntityAlive victim, bool shared)
    {
        var rec = Find(lt.RecordId);
        if (rec == null) return;
        rec.Kills++;
        dirty = true;
        UiKill(lt.OwnerEntityId, rec, victim, shared);
        int per = Math.Max(1, BaitConfig.KillsPerLevel);
        if (rec.Kills % per == 0 && rec.Level < BaitConfig.MaxLevel)
        {
            rec.Level++;
            levels[killer.entityId] = rec.Level;
            Notify(lt.OwnerEntityId, Pretty(rec.ClassName) + " reached level " + rec.Level + "."
                + (PerkSlots(rec.Level) > PerkSlots(rec.Level - 1) ? " A perk slot opened: /thrall perk " + (RosterOf(rec.OwnerKey).IndexOf(rec) + 1) + " tough|hit|regen|taunt" : ""));
            WebhookLevel(rec);
        }
    }
}
