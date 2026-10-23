using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Helpers for Thralls: per-player limits, bait costs, claims, regeneration, defend range, player lookup.
public static partial class Thralls
{
    static readonly Dictionary<string, int[]> limits = new Dictionary<string, int[]>();
    const string LimitsFile = "AzraelBaitLimits.tsv";

    public static long NowSec() { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

    static string LimitsPath { get { return Path.Combine(GameIO.GetSaveGameDir(), LimitsFile); } }

    static void LoadLimits()
    {
        limits.Clear();
        try
        {
            if (!File.Exists(LimitsPath)) return;
            foreach (string line in File.ReadAllLines(LimitsPath))
            {
                string[] p = line.Split('\t');
                if (p.Length >= 3) limits[p[0]] = new[] { PI(p[1]), PI(p[2]) };
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read limits: " + e.Message); }
    }

    static void SetLimits(string key, int owned, int active)
    {
        if (owned <= 0 && active <= 0) limits.Remove(key); else limits[key] = new[] { Math.Max(0, owned), Math.Max(0, active) };
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in limits) sb.Append(kv.Key).Append('\t').Append(kv.Value[0]).Append('\t').Append(kv.Value[1]).Append('\n');
            File.WriteAllText(LimitsPath, sb.ToString());
        }
        catch (Exception e) { Log.Warning("[Bait] could not save limits: " + e.Message); }
    }

    static int MaxOwnedFor(string key) { int[] l; return limits.TryGetValue(key, out l) && l[0] > 0 ? l[0] : BaitConfig.MaxOwned; }
    static int MaxActiveFor(string key) { int[] l; return limits.TryGetValue(key, out l) && l[1] > 0 ? l[1] : BaitConfig.MaxActive; }

    static bool Claimed(EntityPlayer p, Vector3 pos)
    {
        try
        {
            // Only another player's claim blocks it; your own claim and an ally's are fine.
            var pd = GameManager.Instance.GetPersistentPlayerList().GetPlayerDataFromEntityID(p.entityId);
            return W.GetLandClaimOwner(new Vector3i(pos), pd) == EnumLandClaimOwner.Other;
        }
        catch { return false; }
    }

    // Takes n bait from the toolbelt first, then the backpack. False (and nothing taken) when there isn't enough.
    static bool TakeBait(EntityPlayer p, int n)
    {
        try
        {
            var iv = ItemClass.GetItem(BaitCheck.BaitItem);
            if (iv == null || p.inventory == null || p.bag == null) return false;
            if (p.inventory.GetItemCount(iv) + p.bag.GetItemCount(iv) < n) return false;
            int left = n - p.inventory.DecItem(iv, n);
            if (left > 0) p.bag.DecItem(iv, left);
            return true;
        }
        catch (Exception e) { Log.Warning("[Bait] could not take bait: " + e.Message); return false; }
    }

    static void Regen(EntityAlive e)
    {
        if (BaitConfig.RegenPctPerTick <= 0f || e.IsDead() || e.GetAttackTarget() != null) return;
        float max = e.GetMaxHealth(), v = e.Stats.Health.Value;
        if (v >= max) return;
        e.Stats.Health.Value = Mathf.Min(max, v + max * BaitConfig.RegenPctPerTick / 100f);
    }

    // Defend mode: a target counts only when it is close to the thrall or to its owner.
    public static bool DefendOk(EntityAlive self, LiveThrall lt, EntityAlive target)
    {
        float r = BaitConfig.DefendRange;
        if ((target.position - self.position).magnitude <= r) return true;
        var o = W != null ? W.GetEntity(lt.OwnerEntityId) : null;
        return o != null && (target.position - o.position).magnitude <= r;
    }

    // Pack animals: each active, non-tethered, standing animal thrall adds bag slots to its owner.
    static int PackSlots(string key)
    {
        if (BaitConfig.PackSlotsPerAnimal <= 0) return 0;
        int n = 0;
        List<ThrallRecord> l;
        if (roster.TryGetValue(key, out l))
            foreach (var r in l) if (r.Active && !r.Tether && IsAnimalClass(r.ClassName) && r.ReviveAt <= NowSec()) n++;
        return Math.Min(BaitConfig.PackSlotsMax, n * BaitConfig.PackSlotsPerAnimal);
    }

    static readonly Dictionary<int, int> lastPack = new Dictionary<int, int>();

    static void ApplyPack(EntityPlayer p, string key)
    {
        if (p.Buffs == null) return;
        int slots = PackSlots(key), prev;
        bool has = p.Buffs.HasBuff("azraelPackPerk");
        if (lastPack.TryGetValue(p.entityId, out prev) && prev == slots && has == (slots > 0)) return;
        p.Buffs.SetCustomVar("$azraelPackSlots", slots);
        if (slots > 0 && !has) p.Buffs.AddBuff("azraelPackPerk");
        else if (slots == 0 && has) p.Buffs.RemoveBuff("azraelPackPerk");
        if (lastPack.TryGetValue(p.entityId, out prev) && prev != slots)
            Notify(p.entityId, slots > 0 ? "Your pack animals carry for you: +" + slots + " bag slots." : "No pack animals with you, so the extra bag slots are gone.");
        lastPack[p.entityId] = slots;
    }

    // ------------------------------------------------------------------ look at it to see its HP (server side toast)
    static float nextLook;
    class LookState { public int Target; public float At; public int Hp; }
    static readonly Dictionary<int, LookState> looks = new Dictionary<int, LookState>();
    static readonly List<Entity> lookTmp = new List<Entity>();

    // A thrall that gets picked up (a chicken can be grabbed) leaves the roster, so it is not respawned as a copy.
    public static void OnGrabbed(Entity e, int playerId)
    {
        try
        {
            LiveThrall lt;
            if (e == null || !Live.TryGetValue(e.entityId, out lt)) return;
            var rec = Find(lt.RecordId);
            ForgetLive(e.entityId);
            if (rec == null) return;
            RosterOf(rec.OwnerKey).Remove(rec);
            dirty = true;
            if (lt.OwnerEntityId >= 0) Notify(lt.OwnerEntityId, Label(rec) + " was picked up, so it is no longer one of your thralls.");
        }
        catch (Exception ex) { Dbg("grab: " + ex.Message); }
    }

    // Live thralls by kind, so the spawn caps can ignore them.
    public static void CountLive(out int enemies, out int animals)
    {
        enemies = 0; animals = 0;
        var w = W;
        if (w == null) return;
        foreach (var lt in Live.Values)
        {
            var e = w.GetEntity(lt.EntityId) as EntityAlive;
            if (e == null || e.IsDead()) continue;
            EntityClass ec;
            if (!EntityClass.list.TryGetValue(e.entityClass, out ec)) continue;
            if (ec.bIsEnemyEntity) enemies++;
            else if (ec.bIsAnimalEntity) animals++;
        }
    }

    // Riding a vehicle: active thralls go inactive, and are called back when the player gets off.
    static readonly Dictionary<int, List<string>> vehStowed = new Dictionary<int, List<string>>();
    static void VehicleWatch(World world)
    {
        if (!BaitConfig.AutoStowInVehicle) return;
        foreach (var p in world.GetPlayers())
        {
            if (p == null || p.IsDead()) continue;
            string key = KeyOf(p);
            if (key == null) continue;
            bool riding = p.AttachedToEntity is EntityVehicle;
            List<string> ids;
            bool had = vehStowed.TryGetValue(p.entityId, out ids);
            if (riding && !had)
            {
                var stowed = new List<string>();
                foreach (var r in RosterOf(key))
                    if (r.Active && !r.Tether) { r.Active = false; stowed.Add(r.Id); DespawnLive(r.Id); nextSpawn.Remove(r.Id); }
                vehStowed[p.entityId] = stowed;
                if (stowed.Count > 0) { dirty = true; Notify(p.entityId, stowed.Count + " stowed while you ride. They come back when you get off."); }
            }
            else if (!riding && had)
            {
                vehStowed.Remove(p.entityId);
                int back = 0;
                foreach (string id in ids)
                {
                    var r = Find(id);
                    if (r == null || r.Active || r.Tether || r.ReviveAt > NowSec()) continue;
                    if (ActiveCount(key) >= MaxActiveFor(key)) break;
                    r.Active = true; back++;
                }
                if (back > 0) { dirty = true; nextSlow = 0f; Notify(p.entityId, back + " called back."); }
            }
        }
    }

    // Passive thralls drop any target they already have (the AI patch stops new fights).
    static void Calm()
    {
        foreach (var lt in new List<LiveThrall>(Live.Values))
        {
            if (lt.Mode != 2) continue;
            var e = W != null ? W.GetEntity(lt.EntityId) as EntityAlive : null;
            if (e == null || e.IsDead()) continue;
            if (e.GetAttackTarget() != null) e.SetAttackTarget(null, 0);
            e.SetRevengeTarget(null);
        }
    }

    static void LookHp(World world, float now)
    {
        if (!BaitConfig.LookHp) return;
        foreach (var p in world.GetPlayers())
        {
            if (p == null || p.IsDead()) continue;
            Vector3 head = p.getHeadPosition();
            Vector3 fwd = p.GetLookVector();
            if (fwd.sqrMagnitude < 0.01f) fwd = Quaternion.Euler(p.rotation.x, p.rotation.y, 0f) * Vector3.forward;
            float r = BaitConfig.LookHpRange;
            world.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(head, new Vector3(r * 2f, r * 2f, r * 2f)), lookTmp);
            EntityAlive best = null; float bestAngle = BaitConfig.LookHpAngle;
            foreach (var o in lookTmp)
            {
                var e = o as EntityAlive;
                if (e == null || e == p || e is EntityPlayer || e.IsDead()) continue;
                bool mine = Live.ContainsKey(e.entityId);
                if (!mine && (!BaitConfig.LookHpWild || !(e is EntityZombie || e is EntityAnimal))) continue;
                Vector3 to = e.position + Vector3.up * (e.height * 0.5f) - head;
                if (to.magnitude > r || to.magnitude < 0.5f) continue;
                float ang = Vector3.Angle(fwd, to);
                if (ang < bestAngle) { bestAngle = ang; best = e; }
            }
            lookTmp.Clear();
            LookState st;
            if (!looks.TryGetValue(p.entityId, out st)) { st = new LookState(); looks[p.entityId] = st; }
            if (best == null) { st.Target = 0; continue; }
            // A creature heading for bait or eating it is about to produce a fed toast; don't queue an HP toast in front of it.
            if (best.IsEating || BaitCheck.ActiveBait(best) != null) continue;
            int hp = Mathf.CeilToInt(best.Stats.Health.Value);
            bool changed = st.Target != best.entityId;
            if (!changed && now - st.At < (hp != st.Hp ? 4f : 10f)) continue;
            if (!NotifyLow(p.entityId, LookText(best, hp, KeyOf(p)))) continue;
            st.Target = best.entityId; st.At = now; st.Hp = hp;
            Dbg("look: #" + p.entityId + " -> " + best.EntityName + " " + hp);
        }
    }

    static string LookText(EntityAlive e, int hp, string viewerKey)
    {
        string name;
        LiveThrall lt;
        ThrallRecord rec = Live.TryGetValue(e.entityId, out lt) ? Find(lt.RecordId) : null;
        int lvl = rec != null ? rec.Level : LevelOf(e.entityId);
        name = rec != null ? Label(rec) : Pretty(ClassName(e)) + (lvl > 0 ? " Lv" + lvl : "");
        float max = e.GetMaxHealth();
        int n = Mathf.RoundToInt(Mathf.Clamp01(max <= 0f ? 0f : hp / max) * 10f);
        return name + "  " + new string('#', n) + new string('-', 10 - n) + "  " + hp + "/" + Mathf.RoundToInt(max) + (rec == null ? "" : rec.OwnerKey == viewerKey ? "  (yours)" : "  (owned by " + NameOfKey(rec.OwnerKey) + ")");
    }
    static string NameOfKey(string key)
    {
        try
        {
            foreach (var pd in GameManager.Instance.GetPersistentPlayerList().Players.Values)
                if (pd.PrimaryId != null && pd.PrimaryId.CombinedString == key) return pd.PlayerName.DisplayName;
        }
        catch { }
        return key;
    }

    static string KeyByName(string name)
    {
        try
        {
            string hit = null;
            foreach (var pd in GameManager.Instance.GetPersistentPlayerList().Players.Values)
            {
                if (pd.PrimaryId == null) continue;
                string n = pd.PlayerName.DisplayName;
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return pd.PrimaryId.CombinedString;
                if (hit == null && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) hit = pd.PrimaryId.CombinedString;
            }
            return hit;
        }
        catch { return null; }
    }
}
