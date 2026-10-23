using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// Own taming for Zombie and Animal Bait. Feed a wild zombie enough bait and it becomes your thrall: it follows
// you, fights other zombies and never touches a player. Thralls live in a roster file (not in the world save),
// are spawned back when their owner is online, and give the owner perk buffs. All server side.

public class ThrallRecord { public string Id, OwnerKey, ClassName; public bool Active; }
public class LiveThrall { public int EntityId, OwnerEntityId; public string RecordId, OwnerKey; }

public static class Thralls
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
    static void Dbg(string m) { if (BaitConfig.Debug) Log.Out("[Bait] " + m); }

    // ------------------------------------------------------------------ names, tiers, keys
    static int TierOf(string cls)
    {
        string c = (cls ?? "").ToLowerInvariant();
        if (c.EndsWith("infernal") || c.EndsWith("charged")) return 3;
        if (c.EndsWith("radiated")) return 2;
        if (c.EndsWith("feral")) return 1;
        return 0;
    }
    public static int FeedsNeeded(string cls)
    {
        int t = TierOf(cls);
        return BaitConfig.FeedsToTame + (t == 3 ? BaitConfig.ChargedBonus : t == 2 ? BaitConfig.RadiatedBonus : t == 1 ? BaitConfig.FeralBonus : 0);
    }
    public static string Pretty(string cls)
    {
        string c = cls ?? "zombie";
        if (c.StartsWith("zombie", StringComparison.OrdinalIgnoreCase)) c = c.Substring(6);
        string tier = "";
        foreach (string s in new[] { "Infernal", "Charged", "Radiated", "Feral" })
            if (c.EndsWith(s, StringComparison.OrdinalIgnoreCase)) { tier = s.ToLowerInvariant(); c = c.Substring(0, c.Length - s.Length); break; }
        if (c.Length == 0) c = "Zombie";
        return tier.Length > 0 ? c + " (" + tier + ")" : c;
    }
    static string ClassName(EntityAlive e) { return EntityClass.list[e.entityClass].entityClassName; }

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

    public static void Tell(int entityId, string msg)
    {
        try
        {
            GameManager.Instance.ChatMessageServer(null, EChatType.Whisper, -1, "[ffd966][Bait][-] " + msg,
                new List<int> { entityId }, EMessageSender.Server);
        }
        catch (Exception e) { Log.Warning("[Bait] chat reply failed: " + e.Message); }
    }

    // ------------------------------------------------------------------ roster file
    static string FilePath { get { return Path.Combine(GameIO.GetSaveGameDir(), FileName); } }

    public static void Load()
    {
        roster.Clear();
        try
        {
            if (File.Exists(FilePath))
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 4) continue;
                    var r = new ThrallRecord { Id = p[0], OwnerKey = p[1], ClassName = p[2], Active = p[3] == "1" };
                    RosterOf(r.OwnerKey).Add(r);
                }
            Log.Out("[Bait] Thrall roster loaded: " + CountAll() + " tamed zombies.");
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
                    sb.Append(r.Id).Append('\t').Append(r.OwnerKey).Append('\t').Append(r.ClassName).Append('\t').Append(r.Active ? "1" : "0").Append('\n');
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
    static int ActiveCount(string key) { int n = 0; foreach (var r in RosterOf(key)) if (r.Active) n++; return n; }
    static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 8); }

    // ------------------------------------------------------------------ feeding and taming
    static bool CanTame(EntityAlive z)
    {
        if (!(z is EntityZombie)) return false;
        if (z.IsDead() || Live.ContainsKey(z.entityId)) return false;
        return !BaitConfig.Exclude.Contains(ClassName(z));
    }

    // Called every update of a zombie that is going for bait. A meal counts when the pile is used up.
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
        int need = FeedsNeeded(cls);
        if (f.Count < need) { Tell(ownerId, Pretty(cls) + " fed " + f.Count + "/" + need + "."); return; }

        string key = KeyOf(owner);
        if (key == null) return;
        var list = RosterOf(key);
        if (list.Count >= BaitConfig.MaxOwned)
        {
            f.Count = need - 1;
            Tell(ownerId, "You already hold " + list.Count + " thralls. Release one first: /thrall release <number>");
            return;
        }
        var rec = new ThrallRecord { Id = NewId(), OwnerKey = key, ClassName = cls, Active = ActiveCount(key) < BaitConfig.MaxActive };
        list.Add(rec);
        dirty = true;
        feeds.Remove(z.entityId);
        if (rec.Active)
        {
            MakeLive(z, rec, owner);
            Tell(ownerId, Pretty(cls) + " is yours now. It follows you and fights for you. /thrall help");
        }
        else
        {
            RemoveFromWorld(z);
            Tell(ownerId, Pretty(cls) + " is yours, but you already have " + BaitConfig.MaxActive + " out, so it is stowed. /thrall call " + list.Count);
        }
        Flush();
    }

    // ------------------------------------------------------------------ making a live thrall
    static void MakeLive(EntityAlive z, ThrallRecord rec, EntityPlayer owner)
    {
        Live[z.entityId] = new LiveThrall { EntityId = z.entityId, OwnerEntityId = owner.entityId, RecordId = rec.Id, OwnerKey = rec.OwnerKey };
        liveByRecord[rec.Id] = z.entityId;

        z.distraction = null;
        z.pendingDistraction = null;
        z.IsEating = false;
        z.SetAttackTarget(null, 0);
        z.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);

        var ai = z.aiManager;
        if (ai == null) return;
        foreach (var entry in new List<EAITaskEntry>(ai.tasks.Tasks))
        {
            var a = entry.action;
            if (a is EAIBreakBlock || a is EAIDestroyArea || a is EAITerritorial || a is EAIApproachSpot
                || a is EAIApproachDistraction || a is EAIWander)
            {
                try { a.Reset(); } catch { }
                ai.tasks.RemoveTask(entry);
            }
        }
        var follow = new EAIFollowMaster();
        follow.Init(z);
        ai.tasks.AddTask(6, follow);

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
        if (Live.TryGetValue(entityId, out lt)) { liveByRecord.Remove(lt.RecordId); Live.Remove(entityId); }
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

    static void Spawn(ThrallRecord rec, EntityPlayer owner)
    {
        try
        {
            int cid = EntityClass.FromString(rec.ClassName);
            Vector3 pos = owner.position + new Vector3(UnityEngine.Random.Range(-3f, 3f), 0.5f, UnityEngine.Random.Range(-3f, 3f));
            var e = EntityFactory.CreateEntity(cid, pos, Vector3.zero) as EntityAlive;
            if (e == null) { Log.Warning("[Bait] could not create " + rec.ClassName); return; }
            e.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            W.SpawnEntityInWorld(e);
            MakeLive(e, rec, owner);
        }
        catch (Exception ex) { Log.Warning("[Bait] spawn failed for " + rec.ClassName + ": " + ex.Message); }
    }

    // ------------------------------------------------------------------ death
    public static void OnEntityKilled(ref ModEvents.SEntityKilledData data)
    {
        try
        {
            var e = data.KilledEntitiy;
            LiveThrall lt;
            if (e == null || !Live.TryGetValue(e.entityId, out lt)) return;
            var rec = Find(lt.RecordId);
            ForgetLive(e.entityId);
            if (rec != null)
            {
                RosterOf(rec.OwnerKey).Remove(rec);
                dirty = true;
                Tell(lt.OwnerEntityId, Pretty(rec.ClassName) + " fell and is gone for good.");
                Flush();
            }
        }
        catch (Exception ex) { Log.Warning("[Bait] kill handler: " + ex.Message); }
    }

    // ------------------------------------------------------------------ the tick
    public static void OnGameStart(ref ModEvents.SGameStartDoneData _d) { if (IsServer) Load(); }
    public static void OnShutdown(ref ModEvents.SGameShutdownData _d) { Flush(); Live.Clear(); liveByRecord.Clear(); loaded = false; }

    public static void OnUpdate(ref ModEvents.SGameUpdateData _d)
    {
        if (!IsServer || !loaded) return;
        var world = W; if (world == null) return;
        float now = Time.time;
        try
        {
            if (now >= nextTaunt) { nextTaunt = now + 1f; Taunt(world, now); }
            if (now >= nextSlow) { nextSlow = now + 5f; Slow(world, now); }
        }
        catch (Exception e) { Log.Warning("[Bait] tick: " + e.Message); }
    }

    static void Taunt(World world, float now)
    {
        if (BaitConfig.TauntShare <= 0f || BaitConfig.TauntRadius <= 0f) return;
        foreach (var lt in new List<LiveThrall>(Live.Values))
        {
            var th = world.GetEntity(lt.EntityId) as EntityAlive;
            if (th == null || th.IsDead()) continue;
            float r = BaitConfig.TauntRadius;
            world.GetEntitiesInBounds(typeof(EntityEnemy), new Bounds(th.position, new Vector3(r * 2f, 8f, r * 2f)), tmp);
            foreach (var o in tmp)
            {
                var z = o as EntityEnemy;
                if (z == null || z == th || z.IsDead() || z.IsSleeping || Live.ContainsKey(z.entityId)) continue;
                if ((z.entityFlags & EntityFlags.Zombie) == 0) continue;
                bool chosen;
                if (!tauntRoll.TryGetValue(z.entityId, out chosen)) { chosen = UnityEngine.Random.value < BaitConfig.TauntShare; tauntRoll[z.entityId] = chosen; }
                if (!chosen) continue;
                TeachFightBack(z);
                TauntedUntil[z.entityId] = now + BaitConfig.TauntSeconds;
                var cur = z.GetAttackTarget();
                if (cur == null || cur is EntityPlayer) z.SetAttackTarget(th, 400);
            }
            tmp.Clear();
        }
    }

    static void Slow(World world, float now)
    {
        var online = new Dictionary<string, EntityPlayer>();
        foreach (var p in world.GetPlayers())
        {
            if (p == null || p.IsDead()) continue;
            string k = KeyOf(p);
            if (k != null) online[k] = p;
        }

        // keep or drop the live thralls
        foreach (var lt in new List<LiveThrall>(Live.Values))
        {
            var e = world.GetEntity(lt.EntityId) as EntityAlive;
            var rec = Find(lt.RecordId);
            if (e == null || e.IsDead()) { ForgetLive(lt.EntityId); continue; }
            EntityPlayer owner;
            if (rec == null || !rec.Active || !online.TryGetValue(lt.OwnerKey, out owner)) { ForgetLive(lt.EntityId); RemoveFromWorld(e); continue; }
            lt.OwnerEntityId = owner.entityId;
        }

        // bring back the active ones whose owner is online
        foreach (var kv in new List<KeyValuePair<string, List<ThrallRecord>>>(roster))
        {
            EntityPlayer owner;
            if (!online.TryGetValue(kv.Key, out owner)) continue;
            foreach (var rec in kv.Value)
            {
                if (!rec.Active || liveByRecord.ContainsKey(rec.Id)) continue;
                float at;
                if (nextSpawn.TryGetValue(rec.Id, out at) && now < at) continue;
                nextSpawn[rec.Id] = now + 10f;
                Spawn(rec, owner);
            }
        }

        // perks
        foreach (var kv in online) ApplyPerks(kv.Value, PowerOf(kv.Key));

        // forget bookkeeping for zombies that are gone
        if (tauntRoll.Count > 400 || feeds.Count > 200 || counted.Count > 400)
        {
            tauntRoll.Clear(); taughtFight.Clear(); feeds.Clear(); counted.Clear();
            var stale = new List<int>();
            foreach (var id in TauntedUntil.Keys) if (world.GetEntity(id) == null) stale.Add(id);
            foreach (var id in stale) TauntedUntil.Remove(id);
        }
        Flush();
    }

    // ------------------------------------------------------------------ perks
    static readonly int[] TierPower = { 3, 8, 16 };
    static readonly string[] TierBuff = { "azraelThrallPerk1", "azraelThrallPerk2", "azraelThrallPerk3" };
    static readonly string[] TierName = { "Horde Bond I", "Horde Bond II", "Horde Bond III" };
    static readonly Dictionary<int, int> lastTier = new Dictionary<int, int>();

    public static int PowerOf(string ownerKey)
    {
        int power = 0;
        List<ThrallRecord> l;
        if (roster.TryGetValue(ownerKey, out l)) foreach (var r in l) power += 1 + TierOf(r.ClassName);
        return power;
    }

    static void ApplyPerks(EntityPlayer p, int power)
    {
        if (p.Buffs == null) return;
        int tier = 0;
        for (int i = 0; i < TierPower.Length; i++) if (power >= TierPower[i]) tier = i + 1;
        for (int i = 0; i < TierBuff.Length; i++)
        {
            bool want = (i + 1) == tier, has = p.Buffs.HasBuff(TierBuff[i]);
            if (want && !has) p.Buffs.AddBuff(TierBuff[i]);
            else if (!want && has) p.Buffs.RemoveBuff(TierBuff[i]);
        }
        int prev;
        if (lastTier.TryGetValue(p.entityId, out prev) && prev != tier)
            Tell(p.entityId, tier == 0 ? "Your thrall perk faded: you no longer hold enough tamed zombies."
                                       : "Perk: " + TierName[tier - 1] + " (thrall power " + power + ").");
        lastTier[p.entityId] = tier;
    }

    // ------------------------------------------------------------------ chat commands
    static readonly string[] Prefixes = { "/thrall", "/thralls", "/tame" };

    public static bool HandleChat(int senderEntityId, string msg)
    {
        if (!IsServer || !loaded || senderEntityId < 0 || string.IsNullOrEmpty(msg)) return false;
        string[] a = msg.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0 || Array.IndexOf(Prefixes, a[0].ToLowerInvariant()) < 0) return false;
        var owner = W != null ? W.GetEntity(senderEntityId) as EntityPlayer : null;
        string key = owner != null ? KeyOf(owner) : null;
        if (key == null) return true;
        string verb = a.Length > 1 ? a[1].ToLowerInvariant() : "list";
        string arg = a.Length > 2 ? a[2].ToLowerInvariant() : null;
        var list = RosterOf(key);

        switch (verb)
        {
            case "list":
                if (list.Count == 0) { Tell(senderEntityId, "You have no thralls. Throw bait at a zombie and let it eat " + BaitConfig.FeedsToTame + " times."); break; }
                for (int i = 0; i < list.Count; i++)
                    Tell(senderEntityId, (i + 1) + ". " + Pretty(list[i].ClassName) + (list[i].Active ? " [out]" : " [stowed]"));
                Tell(senderEntityId, "Thrall power " + PowerOf(key) + ".");
                break;
            case "call":
            {
                int n;
                if (!Index(arg, list.Count, out n)) { Tell(senderEntityId, "Usage: /thrall call <number>"); break; }
                var r = list[n];
                if (r.Active) { Tell(senderEntityId, Pretty(r.ClassName) + " is already out."); break; }
                if (ActiveCount(key) >= BaitConfig.MaxActive) { Tell(senderEntityId, "You already have " + BaitConfig.MaxActive + " out. Stow one first: /thrall stow <number>"); break; }
                r.Active = true; dirty = true; nextSpawn.Remove(r.Id); nextSlow = 0f;
                Tell(senderEntityId, "Calling " + Pretty(r.ClassName) + ".");
                break;
            }
            case "stow":
            {
                if (arg == null || arg == "all")
                { foreach (var r in list) { r.Active = false; DespawnLive(r.Id); } dirty = true; Tell(senderEntityId, "All thralls stowed."); break; }
                int n;
                if (!Index(arg, list.Count, out n)) { Tell(senderEntityId, "Usage: /thrall stow [number|all]"); break; }
                list[n].Active = false; DespawnLive(list[n].Id); dirty = true;
                Tell(senderEntityId, Pretty(list[n].ClassName) + " stowed.");
                break;
            }
            case "release":
            {
                int n;
                if (!Index(arg, list.Count, out n)) { Tell(senderEntityId, "Usage: /thrall release <number>"); break; }
                var r = list[n]; DespawnLive(r.Id); list.RemoveAt(n); dirty = true;
                Tell(senderEntityId, Pretty(r.ClassName) + " let go for good.");
                break;
            }
            case "kill":
            {
                if (arg == "all")
                {
                    int count = list.Count;
                    foreach (var r in new List<ThrallRecord>(list)) KillLive(r.Id);
                    list.Clear(); dirty = true;
                    Tell(senderEntityId, count == 0 ? "You have no thralls." : "Killed all " + count + " thralls.");
                    break;
                }
                int n;
                if (!Index(arg, list.Count, out n)) { Tell(senderEntityId, "Usage: /thrall kill <number|all>"); break; }
                var rr = list[n]; KillLive(rr.Id); list.RemoveAt(n); dirty = true;
                Tell(senderEntityId, Pretty(rr.ClassName) + " killed.");
                break;
            }
            default:
                Tell(senderEntityId, "/thrall list | call <n> | stow [n|all] | release <n> | kill <n|all>");
                break;
        }
        Flush();
        return true;
    }

    static bool Index(string s, int count, out int index)
    {
        index = -1; int n;
        if (s == null || !int.TryParse(s, out n) || n < 1 || n > count) return false;
        index = n - 1; return true;
    }
}