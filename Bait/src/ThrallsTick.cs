using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// The tick, perks and chat commands of Thralls (see Thralls.cs).
public static partial class Thralls
{
    public static void OnGameStart(ref ModEvents.SGameStartDoneData _d) { if (IsServer) Load(); }
    public static void OnShutdown(ref ModEvents.SGameShutdownData _d)
    { Flush(); Live.Clear(); bodies.Clear(); liveByRecord.Clear(); levels.Clear(); scanned.Clear(); loaded = false; rosterBroken = false; }

    public static void OnUpdate(ref ModEvents.SGameUpdateData _d)
    {
        if (!IsServer || !loaded) return;
        var world = W; if (world == null) return;
        float now = Time.time;
        try
        {
            ClearBodies(world, now);
            if (now >= nextTaunt) { nextTaunt = now + 1f; ChestScan(world); ChestTick(world); RemoveStaleCopies(world); Taunt(world, now); VehicleWatch(world); GuardTick(world, now); ScoutTick(world, now); WebTick(); UiPush(); }
            if (now >= nextLook) { nextLook = now + 0.5f; Calm(); LookHp(world, now); TamingHelperTick(world); }
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
            int perk = string.IsNullOrEmpty(lt.Perks) ? 0 : PerkCount(lt.EntityId, 'A');   // the taunt perk: every zombie near it, animals too, wider
            if (th == null || th.IsDead() || (th is EntityAnimal && perk == 0) || lt.Mode >= 2) continue;
            float r = BaitConfig.TauntRadius * (1f + 0.5f * perk);
            world.GetEntitiesInBounds(typeof(EntityEnemy), new Bounds(th.position, new Vector3(r * 2f, 8f, r * 2f)), tmp);
            foreach (var o in tmp)
            {
                var z = o as EntityEnemy;
                if (z == null || z == th || z.IsDead() || z.IsSleeping || Live.ContainsKey(z.entityId)) continue;
                if ((z.entityFlags & EntityFlags.Zombie) == 0) continue;
                bool chosen;
                if (!tauntRoll.TryGetValue(z.entityId, out chosen)) { chosen = UnityEngine.Random.value < BaitConfig.TauntShare; tauntRoll[z.entityId] = chosen; }
                if (!chosen && perk == 0) continue;
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
            if (k == null) continue;
            online[k] = p;
            CheckOwner(p, k);
            LedgerTick(p, k, now);
        }

        ScanZombies(world);
        EnsureBaitAi(world);
        ProcessGrabs();
        AdoptPlaced();

        // keep or drop the live thralls
        foreach (var lt in new List<LiveThrall>(Live.Values))
        {
            var e = world.GetEntity(lt.EntityId) as EntityAlive;
            var rec = Find(lt.RecordId);
            if (e == null || e.IsDead())
            {
                if (rec != null && lt.OwnerEntityId >= 0)
                {
                    if (e == null && rec.Active && !rec.Tether)
                    {
                        Log.Out("[Bait] " + Label(rec) + " (#" + lt.EntityId + ") is gone from the world with no death event (despawned or its chunk unloaded).");
                        Notify(lt.OwnerEntityId, Label(rec) + " vanished from the world (despawned or its area unloaded). It will be brought back.");
                    }
                    else if (e != null) Log.Out("[Bait] " + Label(rec) + " (#" + lt.EntityId + ") is dead but no kill event was seen.");
                }
                ForgetLive(lt.EntityId); continue;
            }
            EntityPlayer owner;
            bool has = online.TryGetValue(lt.OwnerKey, out owner);
            if (rec == null || !rec.Active || (!has && !rec.Tether)) { ForgetLive(lt.EntityId); RemoveFromWorld(e); continue; }
            lt.OwnerEntityId = has ? owner.entityId : -1;
            KeepSafe(e);
            Regen(e);
        }

        // bring back the active ones: with their owner online, or at their tether when that area is loaded
        foreach (var kv in new List<KeyValuePair<string, List<ThrallRecord>>>(roster))
        {
            EntityPlayer owner;
            bool has = online.TryGetValue(kv.Key, out owner);
            foreach (var rec in kv.Value)
            {
                if (!rec.Active || liveByRecord.ContainsKey(rec.Id)) continue;
                if (rec.ReviveAt > NowSec() && !(has && TryAutoRevive(rec, owner, null))) continue;
                if (!has && !rec.Tether) continue;
                if (rec.Tether && !ChunkLoaded(new Vector3(rec.TX, rec.TY, rec.TZ))) continue;
                float at;
                if (nextSpawn.TryGetValue(rec.Id, out at) && now < at) continue;
                nextSpawn[rec.Id] = now + 10f;
                Spawn(rec, owner);
            }
        }

        HealthAlerts(world);
        AutoHealTick(world, online, now);
        BloodMoonTick(world, online, now);
        LowBaitTick(online);
        foreach (var kv in online) { ApplyPerks(kv.Value, PowerOf(kv.Key)); ApplyPack(kv.Value, kv.Key); }

        if (tauntRoll.Count > 400 || feeds.Count > 200 || counted.Count > 400)
        {
            tauntRoll.Clear(); taughtFight.Clear(); feeds.Clear(); counted.Clear();
            var stale = new List<int>();
            foreach (var id in TauntedUntil.Keys) if (world.GetEntity(id) == null) stale.Add(id);
            foreach (var id in stale) TauntedUntil.Remove(id);
        }
        Flush();
    }

    // ------------------------------------------------------------------ health display (chat; no client mod needed)
    static readonly Dictionary<int, int> alertStage = new Dictionary<int, int>();

    static float HpFrac(EntityAlive e)
    {
        float max = e.GetMaxHealth();
        return max <= 0f ? 0f : Mathf.Clamp01(e.Stats.Health.Value / max);
    }

    static string Bar(float frac)
    {
        int n = Mathf.RoundToInt(frac * 10f);
        string col = frac > 0.5f ? "66ff66" : frac > 0.25f ? "ffd966" : "ff6666";
        return "[" + col + "]" + new string('#', n) + "[-]" + new string('-', 10 - n);
    }

    static string HpText(ThrallRecord r)
    {
        int id;
        EntityAlive e = liveByRecord.TryGetValue(r.Id, out id) && W != null ? W.GetEntity(id) as EntityAlive : null;
        if (r.ReviveAt > NowSec()) return "[downed " + (r.ReviveAt - NowSec()) + "s]";
        if (e == null || e.IsDead()) return r.Active ? "[away]" : "[inactive]";
        return Bar(HpFrac(e)) + " " + Mathf.CeilToInt(e.Stats.Health.Value) + "/" + e.GetMaxHealth();
    }

    static void HealthAlerts(World world)
    {
        if (!BaitConfig.HealthAlerts) return;
        foreach (var lt in new List<LiveThrall>(Live.Values))
        {
            var e = world.GetEntity(lt.EntityId) as EntityAlive;
            var rec = Find(lt.RecordId);
            if (e == null || e.IsDead() || rec == null) continue;
            float f = HpFrac(e);
            int stage = f <= 0.10f ? 3 : f <= 0.25f ? 2 : f <= 0.5f ? 1 : 0, prev;
            alertStage.TryGetValue(lt.EntityId, out prev);
            if (f > 0.6f) { alertStage[lt.EntityId] = 0; continue; }
            if (stage > prev)
            {
                alertStage[lt.EntityId] = stage;
                Notify(lt.OwnerEntityId, Label(rec) + " is at " + Mathf.RoundToInt(f * 100f) + "% health. " + Bar(f));
            }
        }
        if (alertStage.Count > 300) alertStage.Clear();
    }

    // ------------------------------------------------------------------ perks
    static readonly int[] TierPower = { 3, 8, 16 };
    static readonly string[] TierBuff = { "azraelThrallPerk1", "azraelThrallPerk2", "azraelThrallPerk3" };
    static readonly string[] TierName = { "Horde Bond I", "Horde Bond II", "Horde Bond III" };
    static readonly Dictionary<int, int> lastTier = new Dictionary<int, int>();

    // walker/animal 1, feral 2, radiated 3, charged/infernal 4, plus the creature's level
    public static int PowerOf(string ownerKey)
    {
        int power = 0;
        List<ThrallRecord> l;
        if (roster.TryGetValue(ownerKey, out l)) foreach (var r in l) power += 1 + TierOf(r.ClassName) + r.Level / 10;
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
            Notify(p.entityId, tier == 0 ? "Your thrall perk faded: you no longer hold enough tamed creatures."
                                       : "Perk: " + TierName[tier - 1] + " (thrall power " + power + ").");
        lastTier[p.entityId] = tier;
    }

    // ------------------------------------------------------------------ chat commands
    static readonly string[] Prefixes = { "/thrall", "/thralls", "/tame" };

    static bool IsAdmin(int entityId)
    {
        try
        {
            var ci = SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.ForEntityId(entityId);
            if (ci == null) return true; // local host, no client record
            return GameManager.Instance.adminTools.Users.GetUserPermissionLevel(ci) <= 0;
        }
        catch { return false; }
    }

    static bool Index(string s, int count, out int index)
    {
        index = -1; int n;
        if (s == null || !int.TryParse(s, out n) || n < 1 || n > count) return false;
        index = n - 1; return true;
    }

    static List<ThrallRecord> Pick(string arg, List<ThrallRecord> list)
    {
        var l = new List<ThrallRecord>();
        if (arg == "all") { l.AddRange(list); return l; }
        int n;
        if (Index(arg, list.Count, out n)) l.Add(list[n]);
        else if ((n = IndexByName(arg, list)) >= 0) l.Add(list[n]);
        return l;
    }

    public static bool HandleChat(int senderEntityId, string msg)
    {
        if (!IsServer || !loaded || senderEntityId < 0 || string.IsNullOrEmpty(msg)) return false;
        string[] a = msg.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0 || Array.IndexOf(Prefixes, a[0].ToLowerInvariant()) < 0) return false;
        if (a.Length > 1 && (a[1].Equals("hello", StringComparison.OrdinalIgnoreCase) || a[1].Equals("ui", StringComparison.OrdinalIgnoreCase)))
            return UiCommand(senderEntityId, a[1].ToLowerInvariant());
        var owner = W != null ? W.GetEntity(senderEntityId) as EntityPlayer : null;
        string key = owner != null ? KeyOf(owner) : null;
        Log.Out("[Bait] command from #" + senderEntityId + ": " + msg + (key == null ? "  (no player record found)" : ""));
        if (key == null) { Notify(senderEntityId, "Could not find your player record yet. Try again in a moment."); return true; }
        string verb = a.Length > 1 ? a[1].ToLowerInvariant() : "list";
        string arg = a.Length > 2 ? a[2].ToLowerInvariant() : null;
        string arg2 = a.Length > 3 ? a[3] : null;
        var list = RosterOf(key);
        int me = senderEntityId;

        switch (verb)
        {
            case "list":
                if (list.Count == 0) { Tell(me, "You have no thralls. Throw bait at a zombie or animal and let it eat " + BaitConfig.FeedsToTame + " times."); break; }
                for (int pass = 0; pass < 2; pass++)
                {
                    bool wantAnimals = pass == 1;
                    bool any = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var r = list[i];
                        if (IsAnimalClass(r.ClassName) != wantAnimals) continue;
                        if (!any) { Tell(me, wantAnimals ? "Animals:" : "Zombies:"); any = true; }
                        string st = r.Patrol ? "[patrol, " + PatrolPoints(r).Count + " points]" : r.Guard ? "[guard r" + F(r.TR) + "]" : r.Tether ? "[tethered r" + F(r.TR) + "]" : r.Active ? "[active]" : "[inactive]";
                        if (!string.IsNullOrEmpty(r.Perks)) st += " [" + PerkText(r.Perks) + "]";
                        Tell(me, "  " + (i + 1) + ". " + Label(r) + " " + st + (r.AutoRevive ? " [auto-revive]" : "") + (r.AutoHeal ? " [auto-heal]" : "") + (liveByRecord.ContainsKey(r.Id) ? " " + HpText(r) : ""));
                    }
                }
                Tell(me, "Thrall power " + PowerOf(key) + (PackSlots(key) > 0 ? ", pack +" + PackSlots(key) + " slots" : "") + ", holding " + list.Count + "/" + MaxOwnedFor(key) + ".");
                break;
            case "hp":
                if (list.Count == 0) { Tell(me, "You have no thralls."); break; }
                for (int pass = 0; pass < 2; pass++)
                {
                    bool any = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (IsAnimalClass(list[i].ClassName) != (pass == 1)) continue;
                        if (!any) { Tell(me, pass == 1 ? "Animals:" : "Zombies:"); any = true; }
                        Tell(me, "  " + (i + 1) + ". " + Label(list[i]) + " " + HpText(list[i]));
                    }
                }
                break;
            case "call":
            {
                var t = Pick(arg, list);
                if (t.Count != 1) { Tell(me, "Usage: /thrall call <number>"); break; }
                var r = t[0];
                if (r.Active) { Tell(me, Label(r) + " is already active."); break; }
                if (ActiveCount(key) >= MaxActiveFor(key)) { Tell(me, "You already have " + MaxActiveFor(key) + " active. Stow one first: /thrall stow <number>"); break; }
                r.Active = true; dirty = true; nextSpawn.Remove(r.Id); nextSlow = 0f;
                Tell(me, "Calling " + Label(r) + ".");
                break;
            }
            case "stow":
            {
                // stow all = every thrall (tethered ones too); stow active = the ones following you; stow tethered = the ones in a daycare
                string sub2 = a.Length > 3 ? a[3].ToLowerInvariant() : null;
                string what = arg == "all" && sub2 != null ? sub2 : arg;
                List<ThrallRecord> t;
                string none = null;
                if (what == null || what == "all") t = new List<ThrallRecord>(list);
                else if (what == "active") { t = list.FindAll(r => r.Active && !r.Tether); none = "Nothing is active (following you) right now."; }
                else if (what == "tethered") { t = list.FindAll(r => r.Tether); none = "Nothing is tethered."; }
                else t = Pick(arg, list);
                if (t.Count == 0) { Tell(me, none ?? "Usage: /thrall stow [number|all|active|tethered]"); break; }
                foreach (var r in t) { r.Active = false; r.Tether = false; ClearPost(r); DespawnLive(r.Id); }
                dirty = true;
                Tell(me, t.Count == 1 ? Label(t[0]) + " is now inactive." : t.Count + " thralls are now inactive" + (what == "active" ? " (the active ones)." : what == "tethered" ? " (the tethered ones)." : "."));
                break;
            }
            case "tether":
            {
                float radius = BaitConfig.TetherRadius, rr;
                if (arg2 != null && float.TryParse(arg2, NumberStyles.Float, CultureInfo.InvariantCulture, out rr)) radius = rr;
                radius = Mathf.Clamp(radius, 2f, BaitConfig.TetherMaxRadius);
                var t = Pick(arg, list);
                if (t.Count == 0) { Tell(me, "Usage: /thrall tether <number|all> [radius]  (stand where the daycare should be)"); break; }
                if (BaitConfig.TetherRespectClaims && Claimed(owner, owner.position)) { Tell(me, "That spot is inside someone else's land claim."); break; }
                foreach (var r in t)
                {
                    r.Tether = true; r.Active = true; r.TX = owner.position.x; r.TY = owner.position.y; r.TZ = owner.position.z; r.TR = radius;
                    ClearPost(r);
                    DespawnLive(r.Id); nextSpawn.Remove(r.Id);
                }
                dirty = true; nextSlow = 0f;
                Tell(me, t.Count + " tethered here, radius " + F(radius) + ". They stay inside it, even while you are away, and come back when the area loads.");
                break;
            }
            case "untether":
            {
                var t = Pick(arg, list);
                if (t.Count == 0) { Tell(me, "Usage: /thrall untether <number|all>"); break; }
                foreach (var r in t) { if (!r.Tether) continue; r.Tether = false; ClearPost(r); DespawnLive(r.Id); nextSpawn.Remove(r.Id); }
                dirty = true; nextSlow = 0f;
                Tell(me, "Untethered. They follow you again.");
                break;
            }
            case "release":
            {
                var t = Pick(arg, list);
                if (t.Count != 1) { Tell(me, "Usage: /thrall release <number>"); break; }
                DespawnLive(t[0].Id); list.Remove(t[0]); dirty = true;
                Tell(me, Label(t[0]) + " let go for good.");
                break;
            }
            case "kill":
            {
                var t = Pick(arg, list);
                if (t.Count == 0) { Tell(me, arg == "all" ? "You have no thralls." : "Usage: /thrall kill <number|all>"); break; }
                foreach (var r in t) { KillLive(r.Id); list.Remove(r); }
                dirty = true;
                Tell(me, t.Count == 1 ? Label(t[0]) + " killed." : "Killed " + t.Count + " thralls.");
                break;
            }
            case "name":
            {
                var t = Pick(arg, list);
                if (t.Count != 1) { Tell(me, "Usage: /thrall name <number> <name>   (\"clear\" removes it)"); break; }
                string nm = a.Length > 3 ? string.Join(" ", a, 3, a.Length - 3) : "";
                nm = nm.Replace('\t', ' ').Replace('\n', ' ').Trim();
                if (nm.Length > 24) nm = nm.Substring(0, 24);
                t[0].Name = (nm.Length == 0 || nm.ToLowerInvariant() == "clear") ? null : nm;
                dirty = true;
                Tell(me, t[0].Name == null ? "Name cleared." : "Now called " + Label(t[0]) + ".");
                break;
            }
            case "stay":
            case "follow":
            {
                var t = Pick(arg, list);
                if (t.Count == 0) { Tell(me, "Usage: /thrall " + verb + " <number|all>"); break; }
                foreach (var r in t)
                {
                    r.Stay = verb == "stay";
                    int lid; LiveThrall l;
                    if (liveByRecord.TryGetValue(r.Id, out lid) && Live.TryGetValue(lid, out l)) l.Hold = r.Stay;
                }
                dirty = true;
                Tell(me, verb == "stay" ? "They hold position." : "They follow you.");
                break;
            }
            case "mode":
            {
                var t = Pick(arg, list);
                string m = arg2 != null ? arg2.ToLowerInvariant() : "";
                int mode = m.StartsWith("agg") ? 0 : m.StartsWith("def") ? 1 : m.StartsWith("pas") ? 2 : (m.StartsWith("inv") || m.StartsWith("unk") || m.StartsWith("god")) ? 3 : -1;
                if (t.Count == 0 || mode < 0) { Tell(me, "Usage: /thrall mode <number|all> aggressive|defensive|passive|invulnerable"); break; }
                foreach (var r in t)
                {
                    r.Mode = mode; r.BmPrevMode = -1;
                    int lid; LiveThrall l;
                    if (liveByRecord.TryGetValue(r.Id, out lid) && Live.TryGetValue(lid, out l)) l.Mode = mode;
                }
                dirty = true;
                if (mode == 3) foreach (var r2 in t) { int lid2; if (liveByRecord.TryGetValue(r2.Id, out lid2)) { var ze = W != null ? W.GetEntity(lid2) as EntityAlive : null; if (ze != null) ClearAttackers(ze); } }
                Tell(me, "Mode: " + (mode == 0 ? "aggressive (attacks hostiles it sees)" : mode == 1 ? "defensive (fights only what comes close to it or you)" : mode == 2 ? "passive (never starts a fight)" : "invulnerable (never attacks, cannot be killed, and nothing can target it)") + ".");
                break;
            }
            case "heal":
            {
                var t = Pick(arg, list);
                if (t.Count != 1) { Tell(me, "Usage: /thrall heal <number>   (costs " + BaitConfig.HealBaitCost + " bait)"); break; }
                int lid;
                var he = liveByRecord.TryGetValue(t[0].Id, out lid) && W != null ? W.GetEntity(lid) as EntityAlive : null;
                if (he == null || he.IsDead()) { Tell(me, Label(t[0]) + " is not out."); break; }
                if (HpFrac(he) >= 0.99f) { Tell(me, Label(t[0]) + " is already healthy."); break; }
                if (!TakeBait(owner, BaitConfig.HealBaitCost)) { Tell(me, NeedBaitText(owner, BaitConfig.HealBaitCost)); break; }
                float hmax = he.GetMaxHealth();
                he.Stats.Health.Value = Mathf.Min(hmax, he.Stats.Health.Value + hmax * BaitConfig.HealPct / 100f);
                Tell(me, Label(t[0]) + " healed. " + HpText(t[0]));
                break;
            }
            case "revive":
            {
                var t = Pick(arg, list);
                if (t.Count != 1) { Tell(me, "Usage: /thrall revive <number>   (costs " + ReviveCostText() + ")"); break; }
                if (t[0].ReviveAt <= NowSec()) { Tell(me, Label(t[0]) + " is not downed."); break; }
                if (!TakeRevive(owner)) { Tell(me, NeedReviveText(owner)); break; }
                t[0].ReviveAt = 0; dirty = true; nextSpawn.Remove(t[0].Id); nextSlow = 0f;
                Tell(me, Label(t[0]) + " is back on its feet.");
                break;
            }
            case "autorevive":
            case "autoheal":
                ToggleCommand(me, verb, arg, arg2, list, verb == "autoheal");
                break;
            case "here": HereCommand(me, owner, arg, list); break;
            case "where": WhereCommand(me, owner, arg, list); break;
            case "team":
            case "teams": TeamCommand(me, key, a, list); break;
            case "move": MoveCommand(me, arg, arg2, list); break;
            case "deaths": DeathsCommand(me, key); break;
            case "perk":
            case "perks": PerkCommand(me, owner, arg, arg2, list); break;
            case "guard": GuardCommand(me, owner, arg, arg2, list); break;
            case "patrol": PatrolCommand(me, owner, arg, arg2, list); break;
            case "bloodmoon":
            case "bm": BloodMoonCommand(me, key, arg); break;
            case "top": TopCommand(me, arg); break;
            case "scout": ScoutCommand(me, owner, arg, list); break;
            case "evolve": EvolveCommand(me, owner, arg, list); break;
            case "admin":
            {
                if (!IsAdmin(me)) { Tell(me, "Admins only."); break; }
                string sub = arg ?? "who";
                string usage = "/thrall admin who | list <player> | release <player> <n> | limit <player> <owned> <active> | give <from> <to> | heal|revive|tp <player> <n|all>";
                if (AdminCare(me, sub, a)) break;
                if (sub == "who")
                {
                    foreach (var kv in roster) Tell(me, OwnerLabel(kv.Key) + ": " + kv.Value.Count + " thralls");
                    if (roster.Count == 0) Tell(me, "Nobody has thralls yet.");
                    break;
                }
                if (sub == "give")
                {
                    // give <old owner: name, id or the end of an id> <player>: moves every thrall of one owner to another
                    string fk = a.Length > 3 ? KeyFromAnything(a[3]) : null, tk = a.Length > 4 ? KeyByName(a[4]) : null;
                    if (fk == null || tk == null) { Tell(me, "Usage: /thrall admin give <from: name or id from /thrall admin who> <to: player>"); break; }
                    int moved = MoveRoster(fk, tk);
                    Tell(me, moved == 0 ? OwnerLabel(fk) + " has no thralls." : "Moved " + moved + " thralls from " + OwnerLabel(fk) + " to " + NameOfKey(tk) + ".");
                    Log.Out("[Bait] admin #" + me + " moved " + moved + " thralls from " + fk + " to " + tk);
                    break;
                }
                string tkey = a.Length > 3 ? KeyByName(a[3]) : null;
                if (tkey == null) { Tell(me, "No such player. " + usage); break; }
                var tl = RosterOf(tkey);
                if (sub == "list")
                {
                    for (int i = 0; i < tl.Count; i++) Tell(me, (i + 1) + ". " + Label(tl[i]) + " " + HpText(tl[i]));
                    Tell(me, NameOfKey(tkey) + ": " + tl.Count + "/" + MaxOwnedFor(tkey) + " owned, up to " + MaxActiveFor(tkey) + " active.");
                    break;
                }
                if (sub == "release")
                {
                    int n;
                    if (!Index(a.Length > 4 ? a[4] : null, tl.Count, out n)) { Tell(me, usage); break; }
                    var rr = tl[n]; DespawnLive(rr.Id); tl.RemoveAt(n); dirty = true;
                    Tell(me, "Released " + Label(rr) + " from " + NameOfKey(tkey) + ".");
                    break;
                }
                if (sub == "limit")
                {
                    int lo, la;
                    if (a.Length > 5 && int.TryParse(a[4], out lo) && int.TryParse(a[5], out la)) { SetLimits(tkey, lo, la); Tell(me, NameOfKey(tkey) + " now holds up to " + lo + ", " + la + " out (0 = server default)."); }
                    else Tell(me, usage);
                    break;
                }
                Tell(me, usage);
                break;
            }
            case "exclude":
            {
                string sub = arg ?? "list";
                if (sub == "list")
                {
                    Tell(me, BaitConfig.Exclude.Count == 0 ? "Nothing is excluded: every creature can be tamed." : "Cannot be tamed: " + string.Join(", ", new List<string>(BaitConfig.Exclude).ToArray()));
                    break;
                }
                if (!IsAdmin(me)) { Tell(me, "Only admins can change the exclude list."); break; }
                if (sub == "clear") { BaitConfig.Exclude.Clear(); BaitConfig.SaveRuntime(); Tell(me, "Exclude list cleared."); break; }
                if (sub != "add" && sub != "remove" || a.Length < 4) { Tell(me, "Usage: /thrall exclude list | add <className> | remove <className> | clear   (e.g. animalDireWolf)"); break; }
                string want = a[3], real = null;
                foreach (var ec in EntityClass.list.Dict.Values)
                    if (string.Equals(ec.entityClassName, want, StringComparison.OrdinalIgnoreCase)) { real = ec.entityClassName; break; }
                if (real == null) { Tell(me, "No creature class called " + want + ". Use the entity name, like animalDireWolf or zombieScreamer."); break; }
                if (sub == "add") { BaitConfig.Exclude.Add(real); Tell(me, real + " can no longer be tamed."); }
                else { BaitConfig.Exclude.Remove(real); Tell(me, real + " can be tamed again."); }
                BaitConfig.SaveRuntime();
                break;
            }
            case "config":
            {
                string k = a.Length > 2 ? a[2] : null;
                if (k == null)
                {
                    var sb = new StringBuilder();
                    foreach (string s in BaitConfig.All())
                    {
                        if (sb.Length + s.Length > 150) { Tell(me, sb.ToString()); sb.Length = 0; }
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(s);
                    }
                    if (sb.Length > 0) Tell(me, sb.ToString());
                    Tell(me, "Animal meals: /thrall config feeds   (change one: /thrall config feeds.animalBear 6)");
                    Tell(me, "Admins change one with: /thrall config <name> <value>");
                    break;
                }
                if (arg2 == null)
                {
                    string cur = BaitConfig.Get(k);
                    Tell(me, cur == null ? "No setting called " + k + "." : BaitConfig.KeyName(k) + " = " + cur);
                    break;
                }
                if (!IsAdmin(me)) { Tell(me, "Only admins can change settings."); break; }
                string err;
                if (BaitConfig.Set(k, arg2, out err)) { BaitConfig.SaveRuntime(); Tell(me, BaitConfig.KeyName(k) + " is now " + BaitConfig.Get(k) + "."); }
                else Tell(me, err + ".");
                break;
            }
            default:
                Tell(me, "/thrall list | hp | name <n> <name> | call <n> | stow [n|all|active|tethered] | stay/follow <n|all> | mode <n|all> aggressive|defensive|passive|invulnerable | heal <n> | revive <n> | autorevive/autoheal <n|all> [on|off] | here [n] | where [n] | team list|save|call|delete | move <n> <pos|top|up|down> | deaths | perk <n> [perk] | guard <n|all> [radius] | patrol <n> add|start|stop|clear | bloodmoon off|call|fight|both | top [kills] | scout <n> | evolve <n> | tether <n|all> [radius] | untether <n|all> | release <n> | kill <n|all> | exclude list|add|remove <class> | config [name value] | admin ...");
                break;
        }
        Flush();
        return true;
    }
}
