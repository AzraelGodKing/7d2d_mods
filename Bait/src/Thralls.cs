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
// Thralls live in a roster file (not in the world save), are spawned back when they should be out, and give the
// owner perk buffs. Wild zombies also carry levels. All server side.

public class ThrallRecord
{
    public string Id, OwnerKey, ClassName;
    public bool Active;
    public int Level, Kills;
    public bool Tether; public float TX, TY, TZ, TR;
    public string Name;      // optional nickname
    public bool Stay;        // hold position instead of following
    public int Mode;         // 0 aggressive, 1 defend, 2 passive
    public long ReviveAt;    // unix seconds; while in the future the thrall is downed
}
public class LiveThrall { public int EntityId, OwnerEntityId; public string RecordId, OwnerKey; public bool Hold; public int Mode; }

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
    public static bool OwnedBy(int thrallEntityId, int playerEntityId)
    {
        LiveThrall lt; return Live.TryGetValue(thrallEntityId, out lt) && lt.OwnerEntityId == playerEntityId;
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
        if (IsAnimalClass(cls)) return BaitConfig.AnimalFeedsToTame + extra;
        int t = TierOf(cls);
        return extra + BaitConfig.FeedsToTame + (t == 3 ? BaitConfig.ChargedBonus : t == 2 ? BaitConfig.RadiatedBonus : t == 1 ? BaitConfig.FeralBonus : 0);
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
        if (lastToast.TryGetValue(entityId, out last) && Time.time - last < 4f) return false;
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
    static string F(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
    static float P(string s) { float r; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : 0f; }
    static int PI(string s) { int r; return int.TryParse(s, out r) ? r : 0; }

    public static void Load()
    {
        roster.Clear();
        BaitConfig.LoadSaved();
        LoadLimits();
        try
        {
            if (File.Exists(FilePath))
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 4) continue;
                    var r = new ThrallRecord { Id = p[0], OwnerKey = p[1], ClassName = p[2], Active = p[3] == "1" };
                    if (p.Length >= 6) { r.Level = PI(p[4]); r.Kills = PI(p[5]); }
                    if (p.Length >= 11) { r.Tether = p[6] == "1"; r.TX = P(p[7]); r.TY = P(p[8]); r.TZ = P(p[9]); r.TR = P(p[10]); }
                    if (p.Length >= 15) { r.Name = p[11].Length > 0 ? p[11] : null; r.Stay = p[12] == "1"; r.Mode = PI(p[13]); long ra; if (long.TryParse(p[14], out ra)) r.ReviveAt = ra; }
                    RosterOf(r.OwnerKey).Add(r);
                }
            Log.Out("[Bait] Thrall roster loaded: " + CountAll() + " tamed creatures.");
        }
        catch (Exception e) { Log.Warning("[Bait] could not load roster: " + e.Message); }
        loaded = true;
    }

    static int CountAll() { int n = 0; foreach (var kv in roster) n += kv.Value.Count; return n; }

    public static void Flush()
    {
        if (!dirty || !loaded) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in roster)
                foreach (var r in kv.Value)
                    sb.Append(r.Id).Append('\t').Append(r.OwnerKey).Append('\t').Append(r.ClassName).Append('\t').Append(r.Active ? "1" : "0")
                      .Append('\t').Append(r.Level).Append('\t').Append(r.Kills).Append('\t').Append(r.Tether ? "1" : "0")
                      .Append('\t').Append(F(r.TX)).Append('\t').Append(F(r.TY)).Append('\t').Append(F(r.TZ)).Append('\t').Append(F(r.TR))
                      .Append('\t').Append(r.Name ?? "").Append('\t').Append(r.Stay ? "1" : "0").Append('\t').Append(r.Mode).Append('\t').Append(r.ReviveAt).Append('\n');
            File.WriteAllText(FilePath, sb.ToString());
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
                lvl = Math.Max(0, Math.Min(BaitConfig.MaxLevel, lvl));
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
        if (d == null || d.IsDead() || d.itemClass == null || d.itemClass.GetItemName() != BaitCheck.BaitItem) return;
        if (!z.IsEating || d.distractionEatTicks > 0) return;
        long key = ((long)d.entityId << 32) | (uint)z.entityId;
        if (!counted.Add(key)) return;
        CountMeal(z, d.OwnerId);
    }

    static void CountMeal(EntityAlive z, int ownerId)
    {
        var world = W;
        var owner = world != null ? world.GetEntity(ownerId) as EntityPlayer : null;
        if (owner == null || owner.IsDead() || !CanTame(z)) return;
        string cls = ClassName(z);
        Feed f;
        if (!feeds.TryGetValue(z.entityId, out f) || f.Owner != ownerId) { f = new Feed { Owner = ownerId }; feeds[z.entityId] = f; }
        f.Count++;
        int need = FeedsNeeded(cls, LevelOf(z.entityId));
        if (f.Count < need) { Notify(ownerId, Pretty(cls) + " fed " + f.Count + "/" + need + "."); return; }

        string key = KeyOf(owner);
        if (key == null) return;
        var list = RosterOf(key);
        if (list.Count >= MaxOwnedFor(key))
        {
            f.Count = need - 1;
            Notify(ownerId, "You already hold " + list.Count + " thralls (limit " + MaxOwnedFor(key) + "). Release one first: /thrall release <number>");
            return;
        }
        var rec = new ThrallRecord { Id = NewId(), OwnerKey = key, ClassName = cls, Level = LevelOf(z.entityId), Active = ActiveCount(key) < MaxActiveFor(key) };
        list.Add(rec);
        dirty = true;
        feeds.Remove(z.entityId);
        // A zombie from a POI sleeper volume or an infestation counts as dead for that volume once tamed.
        try { z.NotifySleeperDeath(); } catch (Exception ex) { Dbg("sleeper notify failed: " + ex.Message); }
        if (rec.Active)
        {
            MakeLive(z, rec, owner);
            Notify(ownerId, Label(rec) + " is yours now. /thrall help");
        }
        else
        {
            RemoveFromWorld(z);
            Notify(ownerId, Label(rec) + " is yours, but you already have " + MaxActiveFor(key) + " out, so it is stowed. /thrall call " + list.Count);
        }
        Flush();
    }

    // ------------------------------------------------------------------ making a live thrall
    static void MakeLive(EntityAlive z, ThrallRecord rec, EntityPlayer owner)
    {
        Live[z.entityId] = new LiveThrall { EntityId = z.entityId, OwnerEntityId = owner != null ? owner.entityId : -1, RecordId = rec.Id, OwnerKey = rec.OwnerKey, Hold = rec.Stay, Mode = rec.Mode };
        liveByRecord[rec.Id] = z.entityId;
        if (rec.Level > 0) levels[z.entityId] = rec.Level; else levels.Remove(z.entityId);

        z.distraction = null;
        z.pendingDistraction = null;
        z.IsEating = false;
        z.SetAttackTarget(null, 0);
        z.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);

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
        if (rec.Tether) move = new EAITether(new Vector3(rec.TX, rec.TY, rec.TZ), rec.TR);
        else move = new EAIFollowMaster();
        move.Init(z);
        ai.tasks.AddTask(6, move);

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
        if (Live.TryGetValue(entityId, out lt)) { liveByRecord.Remove(lt.RecordId); Live.Remove(entityId); levels.Remove(entityId); }
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
            e.DamageEntity(new DamageSource(EnumDamageSource.Internal, EnumDamageTypes.Suicide), 99999, false, 1f);
    }

    static bool ChunkLoaded(Vector3 p)
    {
        var w = W;
        return w != null && w.GetChunkFromWorldPos(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z)) != null;
    }

    static void Spawn(ThrallRecord rec, EntityPlayer owner)
    {
        try
        {
            if (rec.ReviveAt != 0) { rec.ReviveAt = 0; dirty = true; }
            int cid = EntityClass.FromString(rec.ClassName);
            Vector3 pos;
            if (rec.Tether) pos = new Vector3(rec.TX, rec.TY + 0.5f, rec.TZ);
            else if (owner != null) pos = owner.position + new Vector3(UnityEngine.Random.Range(-3f, 3f), 0.5f, UnityEngine.Random.Range(-3f, 3f));
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
                ThrallKilledSomething(klt, killer);
            else if (killer is EntityZombie && !Live.ContainsKey(killer.entityId) && BaitConfig.ZombieLevels
                     && (victim is EntityPlayer || Live.ContainsKey(victim.entityId)))
                LevelUp(killer);

            LiveThrall lt;
            if (!Live.TryGetValue(victim.entityId, out lt)) return;
            var rec = Find(lt.RecordId);
            ForgetLive(victim.entityId);
            if (rec != null)
            {
                if (BaitConfig.ReviveSeconds > 0)
                {
                    rec.ReviveAt = NowSec() + BaitConfig.ReviveSeconds;
                    nextSpawn.Remove(rec.Id);
                    Notify(lt.OwnerEntityId, Label(rec) + " fell. It rises again in " + BaitConfig.ReviveSeconds + "s, or /thrall revive "
                        + (RosterOf(rec.OwnerKey).IndexOf(rec) + 1) + " (" + BaitConfig.ReviveBaitCost + " bait).");
                }
                else
                {
                    RosterOf(rec.OwnerKey).Remove(rec);
                    Notify(lt.OwnerEntityId, Label(rec) + " fell and is gone for good.");
                }
                dirty = true;
                Flush();
            }
        }
        catch (Exception ex) { Log.Warning("[Bait] kill handler: " + ex.Message); }
    }

    static void ThrallKilledSomething(LiveThrall lt, EntityAlive killer)
    {
        var rec = Find(lt.RecordId);
        if (rec == null) return;
        rec.Kills++;
        dirty = true;
        int per = Math.Max(1, BaitConfig.KillsPerLevel);
        if (rec.Kills % per == 0 && rec.Level < BaitConfig.MaxLevel)
        {
            rec.Level++;
            levels[killer.entityId] = rec.Level;
            Notify(lt.OwnerEntityId, Pretty(rec.ClassName) + " reached level " + rec.Level + ".");
        }
    }
}
