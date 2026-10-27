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

    static int MaxOwnedFor(string key) { int[] l; return limits.TryGetValue(key, out l) && l[0] > 0 ? l[0] : BaitConfig.KatsSpecial ? BaitConfig.KatsSpecialOwned : BaitConfig.MaxOwned; }
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
    // Bait counts in meals: plain bait is 1 each, Rich and Feast count for what they are worth when fed.
    // On a dedicated server (or for a guest in a hosted game) a remote player's backpack and toolbelt are not live here:
    // the server only keeps the saved copy their game sends whenever it changes. So counts are read from that copy, and
    // bait is taken by a game event that runs on the player's own game (Config/gameevents.xml), so no client mod is needed.
    class PendingTake { public int N; public float Until; }
    static readonly Dictionary<string, PendingTake> pendingTakes = new Dictionary<string, PendingTake>();

    static ClientInfo ClientOf(EntityPlayer p)
    {
        try
        {
            if (p == null || p is EntityPlayerLocal) return null;
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            var ci = cm != null && cm.Clients != null ? cm.Clients.ForEntityId(p.entityId) : null;
            return ci != null && ci.latestPlayerData != null ? ci : null;
        }
        catch { return null; }
    }

    // The toolbelt blob has the same item grid as the backpack blob, with a different version byte at the front.
    static int BlobCount(System.IO.MemoryStream ms, bool toolbelt, ItemValue iv)
    {
        if (ms == null || ms.Length < 2) return 0;
        byte[] d = ms.ToArray();
        if (toolbelt) d[0] = 2;
        using (var br = MemoryPools.poolBinaryReader.AllocSync(false))
        {
            br.SetBaseStream(new System.IO.MemoryStream(d));
            var b = Bag.Read(br, XUiC_ItemStack.StackLocationTypes.Backpack, StreamModeRead.Persistency, null);
            return b.GetItemCount(iv);
        }
    }

    static int ItemCount(EntityPlayer p, string item, out int belt, out int bag)
    {
        var ci = ClientOf(p);
        if (ci == null) return ItemCountLocal(p, item, out belt, out bag);
        belt = 0; bag = 0;
        try
        {
            var iv = ItemClass.GetItem(item);
            if (iv == null) return 0;
            belt = BlobCount(ci.latestPlayerData.inventoryData, true, iv);
            bag = BlobCount(ci.latestPlayerData.bagData, false, iv);
            PendingTake pt;
            string key = p.entityId + "|" + item;
            if (pendingTakes.TryGetValue(key, out pt))
            {
                if (Time.time > pt.Until) pendingTakes.Remove(key);
                else
                {
                    int left = pt.N, fromBelt = Math.Min(belt, left);
                    belt -= fromBelt; left -= fromBelt;
                    bag = Math.Max(0, bag - left);
                }
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read " + item + " from the player's saved inventory: " + e.Message); }
        return belt + bag;
    }

    static bool TakeItems(EntityPlayer p, string item, int n)
    {
        if (n <= 0) return true;
        if (ClientOf(p) == null) return TakeItemsLocal(p, item, n);
        try
        {
            string kind = item == BaitCheck.BaitItem ? "Plain" : item == BaitCheck.RichItem ? "Rich" : "Feast";
            p.Buffs.SetCustomVar("azrTake" + kind, n, true);
            if (!GameEventManager.Current.HandleAction("azrael_take_" + kind.ToLowerInvariant(), p, p, false))
            {
                Log.Error("[Bait] the game event azrael_take_" + kind.ToLowerInvariant() + " did not run. Is Config/gameevents.xml from the Bait mod loaded?");
                return false;
            }
            PendingTake pt;
            string key = p.entityId + "|" + item;
            if (pendingTakes.TryGetValue(key, out pt) && Time.time <= pt.Until) { pt.N += n; pt.Until = Time.time + 5f; }
            else pendingTakes[key] = new PendingTake { N = n, Until = Time.time + 5f };
            return true;
        }
        catch (Exception e) { Log.Warning("[Bait] could not take " + item + " from the player: " + e.Message); return false; }
    }
    static int ItemCountLocal(EntityPlayer p, string item, out int belt, out int bag)
    {
        belt = 0; bag = 0;
        try
        {
            var iv = ItemClass.GetItem(item);
            if (iv == null || p == null) return 0;
            if (p.inventory != null) belt = p.inventory.GetItemCount(iv);
            if (p.bag != null) bag = p.bag.GetItemCount(iv);
        }
        catch (Exception e) { Log.Warning("[Bait] could not count " + item + ": " + e.Message); }
        return belt + bag;
    }

    static bool TakeItemsLocal(EntityPlayer p, string item, int n)
    {
        if (n <= 0) return true;
        try
        {
            var iv = ItemClass.GetItem(item);
            int left = n - p.inventory.DecItem(iv, n);
            if (left > 0) p.bag.DecItem(iv, left);
            return true;
        }
        catch (Exception e) { Log.Warning("[Bait] could not take " + item + ": " + e.Message); return false; }
    }

    // Pays n meals of bait: plain first, then Rich, then Feast (an overpay is not refunded).
    static bool TakeBait(EntityPlayer p, int n)
    {
        int b, g;
        int plain = ItemCount(p, BaitCheck.BaitItem, out b, out g);
        int rich = ItemCount(p, BaitCheck.RichItem, out b, out g);
        int feast = ItemCount(p, BaitCheck.FeastItem, out b, out g);
        int rm = BaitCheck.Meals(BaitCheck.RichItem), fm = BaitCheck.Meals(BaitCheck.FeastItem);
        if (plain + rich * rm + feast * fm < n) return false;
        int usePlain = Math.Min(plain, n), rem = n - usePlain;
        int useRich = rem > 0 ? Math.Min(rich, (rem + rm - 1) / rm) : 0;
        rem -= useRich * rm;
        int useFeast = rem > 0 ? (rem + fm - 1) / fm : 0;
        return TakeItems(p, BaitCheck.BaitItem, usePlain) && TakeItems(p, BaitCheck.RichItem, useRich) && TakeItems(p, BaitCheck.FeastItem, useFeast);
    }

    // ---- revive: 3 Plain, or 2 Rich, or 1 Feast. Plain is preferred, then Rich, then Feast.
    static string ReviveCostText()
    {
        return BaitConfig.ReviveCostPlain + " Plain, " + BaitConfig.ReviveCostRich + " Rich or " + BaitConfig.ReviveCostFeast + " Feast bait";
    }

    static bool TakeRevive(EntityPlayer p)
    {
        int b, g;
        var items = new[] { BaitCheck.BaitItem, BaitCheck.RichItem, BaitCheck.FeastItem };
        var costs = new[] { BaitConfig.ReviveCostPlain, BaitConfig.ReviveCostRich, BaitConfig.ReviveCostFeast };
        for (int i = 0; i < 3; i++)
        {
            if (costs[i] <= 0) return true;
            if (ItemCount(p, items[i], out b, out g) >= costs[i]) return TakeItems(p, items[i], costs[i]);
        }
        return false;
    }

    static string NeedReviveText(EntityPlayer p)
    {
        int b1, g1, b2, g2, b3, g3;
        int plain = ItemCount(p, BaitCheck.BaitItem, out b1, out g1);
        int rich = ItemCount(p, BaitCheck.RichItem, out b2, out g2);
        int feast = ItemCount(p, BaitCheck.FeastItem, out b3, out g3);
        return "Reviving costs " + ReviveCostText() + ". The server sees " + plain + " plain, " + rich + " Rich, " + feast + " Feast (toolbelt " + (b1 + b2 + b3) + ", bag " + (g1 + g2 + g3) + ").";
    }
    static string NeedBaitText(EntityPlayer p, int n)
    {
        int b1, g1, b2, g2, b3, g3;
        int plain = ItemCount(p, BaitCheck.BaitItem, out b1, out g1);
        int rich = ItemCount(p, BaitCheck.RichItem, out b2, out g2);
        int feast = ItemCount(p, BaitCheck.FeastItem, out b3, out g3);
        return "You need " + n + " meals of bait (plain = 1, Rich = " + BaitCheck.Meals(BaitCheck.RichItem) + ", Feast = " + BaitCheck.Meals(BaitCheck.FeastItem) +
               "). The server sees " + plain + " plain, " + rich + " Rich, " + feast + " Feast (toolbelt " + (b1 + b2 + b3) + ", bag " + (g1 + g2 + g3) + ").";
    }

    // ---- why a thrall died: the last hit it took is remembered
    class LastHit { public string Cause; public int Amount; public float At; }
    static readonly Dictionary<int, LastHit> lastHit = new Dictionary<int, LastHit>();

    public static void NoteHit(EntityAlive victim, DamageSource ds, EntityAlive attacker, int strength)
    {
        try
        {
            if (lastHit.Count > 300) lastHit.Clear();
            lastHit[victim.entityId] = new LastHit { Cause = CauseOf(ds, attacker), Amount = strength, At = Time.time };
        }
        catch { }
    }

    static string CauseOf(DamageSource ds, EntityAlive attacker)
    {
        if (attacker != null)
        {
            if (attacker is EntityPlayer) return "a player";
            string n = Pretty(ClassName(attacker));
            int lv = LevelOf(attacker.entityId);
            return n + (lv > 0 ? " Lv" + lv : "") + (Live.ContainsKey(attacker.entityId) ? " (another thrall)" : "");
        }
        try
        {
            if (ds.BuffClass != null) return "the " + ds.BuffClass.Name + " effect";
            if (ds.BlockPosition != Vector3i.zero && W != null)
            {
                var bv = W.GetBlock(ds.BlockPosition);
                if (bv.type != 0) return "a block (" + bv.Block.GetBlockName() + ")";
            }
            if (ds.AttackingItem != null && ds.ItemClass != null) return ds.ItemClass.GetItemName();
        }
        catch { }
        switch (ds.damageType.ToString())
        {
            case "Falling": return "a fall";
            case "Heat": return "fire or heat";
            case "Cold": return "the cold";
            case "Radiation": return "radiation";
            case "Starvation": return "starvation";
            case "Dehydration": return "thirst";
            case "Suffocation": return "suffocation";
            case "BloodLoss": return "bleeding";
            case "Electrical": return "electricity";
            case "Crushing": return "something crushing it";
            case "Weather": return "the weather";
            case "Disease": case "Infection": return "disease";
            case "Suicide": return "being killed on command";
        }
        return ds.damageType + " damage";
    }

    // Text for the death toast and the server log.
    public static string DeathReason(EntityAlive victim, EntityAlive killer)
    {
        LastHit h;
        string why;
        if (lastHit.TryGetValue(victim.entityId, out h) && Time.time - h.At < 60f)
            why = "killed by " + h.Cause + " (last hit " + h.Amount + ", " + Mathf.RoundToInt(Time.time - h.At) + "s ago)";
        else if (killer != null) why = "killed by " + Pretty(ClassName(killer));
        else why = "died with no recorded hit";
        if (victim.position.y < 2f) why += "; it was at the bottom of the world";
        lastHit.Remove(victim.entityId);
        return why;
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

    class Carried { public string RecId, ClassName; public int PlayerId, MaxId; public bool WasTether; public float Radius, At; }
    static readonly List<Carried> carried = new List<Carried>();

    // A carried thrall that gets put down again: the freshly spawned creature of that class beside the player becomes it.
    static void AdoptPlaced()
    {
        if (carried.Count == 0) return;
        for (int i = carried.Count - 1; i >= 0; i--)
        {
            var c = carried[i];
            try
            {
                var rec = Find(c.RecId);
                var p = W.GetEntity(c.PlayerId) as EntityPlayer;
                if (rec == null || rec.Active) { carried.RemoveAt(i); continue; }
                if (p == null) continue;
                int cid = EntityClass.FromString(c.ClassName);
                EntityAlive found = null;
                foreach (var en in W.Entities.list)
                {
                    var ea = en as EntityAlive;
                    if (ea == null || ea.entityId <= c.MaxId || ea.entityClass != cid || ea.IsDead() || Live.ContainsKey(ea.entityId)) continue;
                    if ((ea.position - p.position).magnitude > 10f) continue;
                    found = ea; break;
                }
                if (found == null) continue;
                carried.RemoveAt(i);
                rec.Active = true;
                if (c.WasTether) { rec.Tether = true; rec.TX = found.position.x; rec.TY = found.position.y; rec.TZ = found.position.z; rec.TR = c.Radius; }
                dirty = true;
                MakeLive(found, rec, p);
                Notify(p.entityId, Label(rec) + (rec.Tether ? " is tethered here again." : " is back with you."));
            }
            catch (Exception ex) { Dbg("adopt: " + ex.Message); carried.RemoveAt(i); }
        }
    }

    class GrabItem { public int PlayerId; public string Item; public float At; }
    static readonly List<GrabItem> grabItems = new List<GrabItem>();

    // The item the player got for grabbing a thrall is taken back, so the creature is one thing: a stowed thrall.
    static void ProcessGrabs()
    {
        if (grabItems.Count == 0) return;
        for (int i = grabItems.Count - 1; i >= 0; i--)
        {
            var g = grabItems[i];
            float age = Time.time - g.At;
            if (age < 1.5f) continue;
            bool done = age > 30f;
            try
            {
                var p = W != null ? W.GetEntity(g.PlayerId) as EntityPlayer : null;
                var iv = ItemClass.GetItem(g.Item);
                if (p != null && iv != null && p.inventory != null && p.bag != null)
                {
                    if (p.inventory.GetItemCount(iv) + p.bag.GetItemCount(iv) > 0)
                    {
                        int left = 1 - p.inventory.DecItem(iv, 1);
                        if (left > 0) p.bag.DecItem(iv, left);
                        done = true;
                    }
                }
                else done = true;
            }
            catch (Exception ex) { Dbg("grab item: " + ex.Message); done = true; }
            if (done) grabItems.RemoveAt(i);
        }
    }

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
            // Picking it up stows it: it stays on the roster (still tamed, with its level and name), inactive.
            // The item stays in the inventory. Placing it again hands the new creature back to this record:
            // tethered where it is put down if it was tethered, otherwise following.
            int maxId = 0;
            foreach (var en in W.Entities.list) if (en != null && en.entityId > maxId) maxId = en.entityId;
            var c = new Carried { RecId = rec.Id, PlayerId = playerId, ClassName = rec.ClassName, WasTether = rec.Tether, Radius = rec.TR, MaxId = maxId, At = Time.time };
            carried.RemoveAll(x => x.RecId == rec.Id);
            carried.Add(c);
            rec.Active = false; rec.Tether = false; dirty = true;
            if (lt.OwnerEntityId >= 0) Notify(lt.OwnerEntityId, Label(rec) + " is in your inventory. Place it down to bring it back" + (c.WasTether ? " (tethered there)." : "."));
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
            if (lt.Mode < 2) continue;
            var e = W != null ? W.GetEntity(lt.EntityId) as EntityAlive : null;
            if (e == null || e.IsDead()) continue;
            if (lt.Mode == 3) { e.Stats.Health.Value = e.GetMaxHealth(); ClearAttackers(e); }   // invulnerable: also covers bleeding, poison and fire
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
    // A spot a few blocks behind and to the side of the player (behind = opposite to the way they are moving or facing),
    // so a thrall that is called or teleported never lands in front of a vehicle or a charging player.
    public static Vector3 BehindOwner(EntityAlive o)
    {
        Vector3 fwd = o.motion; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.25f) fwd = Quaternion.Euler(0f, o.rotation.y, 0f) * Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 side = new Vector3(-fwd.z, 0f, fwd.x);
        return o.position - fwd * 4f + side * UnityEngine.Random.Range(-2f, 2f) + new Vector3(0f, 0.5f, 0f);
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
