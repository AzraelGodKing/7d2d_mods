using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Batch 3 (1.12.0): backpack guard, bodyguard rush, sneak follow, screamer call, PvP thralls, duels, second chance,
// the whistle, short commands and aliases, catch-up levels, retiring, owner XP and quest credit, weekly Alphas,
// the feeder and kennel blocks, taming resistance, presets, the admin log, name rules and stats. All server side.
public static partial class Thralls
{
    // ------------------------------------------------------------------ per-player values (AzraelBaitPlayers.tsv, next to bm=)
    // pvp=1, al.<alias>=<command>, st.tamed / st.retired / st.alphas = lifetime counts. "_server" holds server values.
    static readonly Dictionary<string, Dictionary<string, string>> pdata = new Dictionary<string, Dictionary<string, string>>();
    const string ServerKey = "_server";

    static string PGet(string key, string name)
    {
        Dictionary<string, string> d; string v;
        return key != null && pdata.TryGetValue(key, out d) && d.TryGetValue(name, out v) ? v : null;
    }
    static void PSet(string key, string name, string value)
    {
        if (key == null) return;
        Dictionary<string, string> d;
        if (!pdata.TryGetValue(key, out d)) { d = new Dictionary<string, string>(); pdata[key] = d; }
        if (value == null) d.Remove(name); else d[name] = value.Replace('\t', ' ').Replace('\n', ' ');
        playersDirty = true;
    }
    static void CountStat(string key, string name, int add) { PSet(key, "st." + name, (PI(PGet(key, "st." + name)) + add).ToString()); }

    // ------------------------------------------------------------------ S51 backpack guard
    class PackGuard { public Vector3 Pos; public float Until; }
    static readonly Dictionary<string, PackGuard> packGuards = new Dictionary<string, PackGuard>();
    static readonly HashSet<string> deadSeen = new HashSet<string>();

    static bool BackpackGuarding(LiveThrall lt)
    {
        PackGuard g;
        return packGuards.TryGetValue(lt.OwnerKey, out g) && Time.time < g.Until;
    }

    // Every 5 s, before Slow decides which thralls stay: an owner who just died leaves the followers guarding the body.
    static void BackpackGuardSlow(World world, float now)
    {
        foreach (var p in world.GetPlayers())
        {
            if (p == null) continue;
            string key = KeyOf(p);
            if (key == null) continue;
            if (!p.IsDead()) { deadSeen.Remove(key); if (packGuards.ContainsKey(key)) EndPackGuard(key); continue; }
            if (!deadSeen.Add(key) || BaitConfig.BackpackGuardSeconds <= 0f) continue;
            int n = 0;
            foreach (var lt in Live.Values)
            {
                var rec = Find(lt.RecordId);
                if (lt.OwnerKey != key || rec == null || rec.Tether) continue;
                var e = world.GetEntity(lt.EntityId) as EntityAlive;
                if (e == null || e.IsDead()) continue;
                lt.Guard = true; lt.GX = p.position.x; lt.GY = p.position.y; lt.GZ = p.position.z; lt.GR = BaitConfig.BackpackGuardRadius;
                if ((e.position - p.position).magnitude > 25f) e.SetPosition(p.position + new Vector3(UnityEngine.Random.Range(-2f, 2f), 0.3f, UnityEngine.Random.Range(-2f, 2f)));
                n++;
            }
            if (n == 0) continue;
            packGuards[key] = new PackGuard { Pos = p.position, Until = now + BaitConfig.BackpackGuardSeconds };
            Tell(p.entityId, n + " thrall" + (n == 1 ? "" : "s") + " guard your backpack for " + Mathf.RoundToInt(BaitConfig.BackpackGuardSeconds) + "s.");
        }
        foreach (var kv in new List<KeyValuePair<string, PackGuard>>(packGuards)) if (now >= kv.Value.Until) EndPackGuard(kv.Key);
    }

    static void EndPackGuard(string key)
    {
        packGuards.Remove(key);
        foreach (var lt in Live.Values)
        {
            if (lt.OwnerKey != key) continue;
            var rec = Find(lt.RecordId);
            if (rec == null) continue;
            lt.Guard = rec.Tether && (rec.Guard || rec.Patrol); lt.GX = rec.TX; lt.GY = rec.TY; lt.GZ = rec.TZ; lt.GR = rec.TR;
        }
    }

    // ------------------------------------------------------------------ S52 bodyguard rush
    static readonly Dictionary<int, float> rushUntil = new Dictionary<int, float>();
    static readonly Dictionary<int, float> rushReady = new Dictionary<int, float>();
    static readonly List<Entity> b3Tmp = new List<Entity>();

    static void BodyguardTick(World world, float now)
    {
        foreach (var kv in new List<KeyValuePair<int, float>>(rushUntil))
        {
            if (now < kv.Value) continue;
            rushUntil.Remove(kv.Key);
            LiveThrall lt;
            if (Live.TryGetValue(kv.Key, out lt) && !duelOf.ContainsKey(kv.Key)) { var rec = Find(lt.RecordId); if (rec != null) lt.Mode = rec.Mode; }
        }
        if (!BaitConfig.BodyguardRush || Live.Count == 0) return;
        foreach (var p in world.GetPlayers())
        {
            if (p == null || p.IsDead() || HpFrac(p) * 100f >= BaitConfig.BodyguardHpPct) continue;
            float ready;
            if (rushReady.TryGetValue(p.entityId, out ready) && now < ready) continue;
            world.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(p.position, new Vector3(40f, 12f, 40f)), b3Tmp);
            var attackers = new List<EntityAlive>();
            foreach (var o in b3Tmp)
            {
                var a = o as EntityAlive;
                if (a == null || a is EntityPlayer || a.IsDead() || Live.ContainsKey(a.entityId)) continue;
                if (a.GetAttackTarget() == p) attackers.Add(a);
            }
            b3Tmp.Clear();
            if (attackers.Count == 0) continue;
            int n = 0;
            foreach (var lt in new List<LiveThrall>(Live.Values))
            {
                if (lt.OwnerEntityId != p.entityId || lt.Mode == 3 || duelOf.ContainsKey(lt.EntityId)) continue;
                var rec = Find(lt.RecordId);
                var e = world.GetEntity(lt.EntityId) as EntityAlive;
                if (rec == null || rec.Tether || e == null || e.IsDead() || (e.position - p.position).magnitude > 40f) continue;
                lt.Mode = 0;
                rushUntil[lt.EntityId] = now + BaitConfig.BodyguardSeconds;
                EntityAlive best = null; float bd = float.MaxValue;
                foreach (var a in attackers) { float d = (a.position - e.position).sqrMagnitude; if (d < bd) { bd = d; best = a; } }
                if (best != null) e.SetAttackTarget(best, 400);
                n++;
            }
            if (n == 0) continue;
            int pulled = 0;
            foreach (var a in attackers)
            {
                EntityAlive near = null; float nd = float.MaxValue;
                foreach (var lt in Live.Values)
                {
                    if (lt.OwnerEntityId != p.entityId || !rushUntil.ContainsKey(lt.EntityId)) continue;
                    var e = world.GetEntity(lt.EntityId) as EntityAlive;
                    if (e == null) continue;
                    float d = (a.position - e.position).sqrMagnitude;
                    if (d < nd) { nd = d; near = e; }
                }
                if (near == null) continue;
                if (a is EntityZombie) TeachFightBack(a);
                TauntedUntil[a.entityId] = now + BaitConfig.BodyguardSeconds;
                a.SetAttackTarget(near, 400);
                pulled++;
            }
            rushReady[p.entityId] = now + BaitConfig.BodyguardCooldownSeconds;
            Notify(p.entityId, "Bodyguard rush: " + n + " thrall" + (n == 1 ? "" : "s") + " pull " + pulled + " attacker" + (pulled == 1 ? "" : "s") + " off you.");
        }
    }

    // ------------------------------------------------------------------ S53 sneak follow
    public static bool Sneaking(LiveThrall lt)
    {
        if (!BaitConfig.SneakFollow || lt == null || W == null) return false;
        var o = W.GetEntity(lt.OwnerEntityId) as EntityPlayer;
        return o != null && !o.IsDead() && o.IsCrouching;
    }

    // While the owner sneaks a thrall only fights what is after the owner or itself, and never wakes a sleeper.
    public static bool SneakBlocks(EntityAlive self, LiveThrall lt, EntityAlive target)
    {
        if (target == null || rushUntil.ContainsKey(self.entityId) || duelOf.ContainsKey(self.entityId) || !Sneaking(lt)) return false;
        if (target.IsSleeping) return true;
        var t = target.GetAttackTarget();
        return t == null || (t.entityId != lt.OwnerEntityId && t != self);
    }

    // ------------------------------------------------------------------ S54 screamer call
    static readonly Dictionary<int, float> lastScream = new Dictionary<int, float>();

    static void ScreamCommand(int me, string arg, List<ThrallRecord> list)
    {
        var t = Pick(arg, list);
        if (t.Count != 1) { Tell(me, "Usage: /thrall scream <number|name>   (a tamed screamer calls wild zombies to it)"); return; }
        var r = t[0];
        if (r.ClassName.IndexOf("Screamer", StringComparison.OrdinalIgnoreCase) < 0) { Tell(me, Label(r) + " is not a screamer."); return; }
        int lid;
        var e = liveByRecord.TryGetValue(r.Id, out lid) && W != null ? W.GetEntity(lid) as EntityAlive : null;
        if (e == null || e.IsDead()) { Tell(me, Label(r) + " has to be out to scream."); return; }
        float last, cd = BaitConfig.ScreamCooldownSeconds;
        if (cd > 0f && lastScream.TryGetValue(me, out last) && Time.time - last < cd) { Tell(me, "Your screamer can call again in " + Mathf.CeilToInt(cd - (Time.time - last)) + "s."); return; }
        float rad = BaitConfig.ScreamRadius;
        W.GetEntitiesInBounds(typeof(EntityEnemy), new Bounds(e.position, new Vector3(rad * 2f, 40f, rad * 2f)), b3Tmp);
        int n = 0;
        foreach (var o in b3Tmp)
        {
            var z = o as EntityAlive;
            if (z == null || z.IsDead() || z.IsSleeping || Live.ContainsKey(z.entityId) || (z.entityFlags & EntityFlags.Zombie) == 0) continue;
            if ((z.position - e.position).magnitude > rad) continue;
            z.SetInvestigatePosition(e.position, 1200, true);
            n++;
        }
        b3Tmp.Clear();
        lastScream[me] = Time.time;
        Tell(me, Label(r) + " screams. " + (n == 0 ? "Nothing answered." : n + " wild zombie" + (n == 1 ? " is" : "s are") + " on the way."));
    }

    // ------------------------------------------------------------------ S55 PvP thralls
    static bool PvpOn(string key) { return PGet(key, "pvp") == "1"; }

    static bool SameSide(EntityPlayer a, EntityPlayer b)
    {
        try
        {
            if (a.Party != null && a.Party.ContainsMember(b)) return true;
            return a.IsFriendsWith(b) || b.IsFriendsWith(a);
        }
        catch { return true; }
    }

    static bool PvpAllowed(EntityPlayer owner, EntityPlayer victim)
    {
        if (!BaitConfig.PvpThralls || owner == null || victim == null || owner == victim || victim.IsDead()) return false;
        return PvpOn(KeyOf(owner)) && PvpOn(KeyOf(victim)) && !SameSide(owner, victim);
    }

    // Used by the patches: may this thrall go for (and hurt) this player?
    public static bool ThrallMayHitPlayer(int thrallId, EntityPlayer victim)
    {
        LiveThrall lt;
        if (!BaitConfig.PvpThralls || !Live.TryGetValue(thrallId, out lt) || lt.Mode == 3 || W == null) return false;
        return PvpAllowed(W.GetEntity(lt.OwnerEntityId) as EntityPlayer, victim);
    }
    // ...and may this player hit back at it?
    public static bool PvpFoe(int thrallId, int playerId)
    {
        LiveThrall lt;
        if (!BaitConfig.PvpThralls || !Live.TryGetValue(thrallId, out lt) || W == null) return false;
        return PvpAllowed(W.GetEntity(lt.OwnerEntityId) as EntityPlayer, W.GetEntity(playerId) as EntityPlayer);
    }

    static void PvpCommand(int me, string key, string arg)
    {
        if (!BaitConfig.PvpThralls) { Tell(me, "PvP thralls are turned off on this server (PvpThralls)."); return; }
        if (arg == "on" || arg == "off") { PSet(key, "pvp", arg == "on" ? "1" : null); }
        Tell(me, "PvP thralls: " + (PvpOn(key) ? "on. Your aggressive thralls go for players who also have it on, never your party or allies." : "off.") + "   (/thrall pvp on|off)");
    }

    static void PvpTick(World world)
    {
        if (!BaitConfig.PvpThralls || Live.Count == 0) return;
        foreach (var lt in new List<LiveThrall>(Live.Values))
        {
            if (lt.Mode != 0 || duelOf.ContainsKey(lt.EntityId) || !PvpOn(lt.OwnerKey)) continue;
            var e = world.GetEntity(lt.EntityId) as EntityAlive;
            var owner = world.GetEntity(lt.OwnerEntityId) as EntityPlayer;
            if (e == null || e.IsDead() || owner == null || e.GetAttackTarget() != null) continue;
            EntityPlayer best = null; float bd = 12f * 12f;
            foreach (var p in world.GetPlayers())
            {
                if (p == null || !PvpAllowed(owner, p)) continue;
                float d = (p.position - e.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best != null) e.SetAttackTarget(best, 600);
        }
    }

    // ------------------------------------------------------------------ S56 duels
    class Duel { public int A, B, OwnerA, OwnerB; public string RecA, RecB; public float Until; }
    class Challenge { public string FromKey, RecId; public int FromId; public float Until; }
    static readonly Dictionary<int, Duel> duelOf = new Dictionary<int, Duel>();
    static readonly Dictionary<string, Challenge> challenges = new Dictionary<string, Challenge>();

    public static bool InDuel(int a, int b) { Duel d; return duelOf.TryGetValue(a, out d) && (d.A == b || d.B == b); }

    static EntityAlive LiveEntity(ThrallRecord r)
    {
        int lid;
        var e = r != null && liveByRecord.TryGetValue(r.Id, out lid) && W != null ? W.GetEntity(lid) as EntityAlive : null;
        return e != null && !e.IsDead() ? e : null;
    }

    static void DuelCommand(int me, EntityPlayer owner, string key, string[] a, List<ThrallRecord> list)
    {
        string sub = a.Length > 2 ? a[2].ToLowerInvariant() : null;
        string usage = "Usage: /thrall duel <player> <your thrall>  |  /thrall duel accept <your thrall>  |  /thrall duel decline";
        if (sub == null) { Tell(me, usage); return; }
        if (sub == "decline")
        {
            Challenge c;
            if (!challenges.TryGetValue(key, out c)) { Tell(me, "Nobody challenged you."); return; }
            challenges.Remove(key);
            Notify(c.FromId, NameOfKey(key) + " declined your duel.");
            Tell(me, "Duel declined.");
            return;
        }
        if (sub == "accept")
        {
            Challenge c;
            if (!challenges.TryGetValue(key, out c) || Time.time > c.Until) { challenges.Remove(key); Tell(me, "No duel is waiting for you."); return; }
            var mine = Pick(a.Length > 3 ? a[3].ToLowerInvariant() : null, list);
            if (mine.Count != 1) { Tell(me, "Usage: /thrall duel accept <your thrall>"); return; }
            var theirs = Find(c.RecId);
            var ea = LiveEntity(theirs); var eb = LiveEntity(mine[0]);
            if (ea == null || eb == null) { Tell(me, "Both thralls have to be out."); return; }
            if (mine[0].Mode == 3 || theirs.Mode == 3) { Tell(me, "Invulnerable thralls can't duel."); return; }
            if (duelOf.ContainsKey(ea.entityId) || duelOf.ContainsKey(eb.entityId)) { Tell(me, "One of them is already in a duel."); return; }
            if ((ea.position - eb.position).magnitude > 40f) { Tell(me, "Bring the two thralls within 40m of each other."); return; }
            challenges.Remove(key);
            var d = new Duel { A = ea.entityId, B = eb.entityId, OwnerA = c.FromId, OwnerB = me, RecA = theirs.Id, RecB = mine[0].Id, Until = Time.time + BaitConfig.DuelSeconds };
            duelOf[d.A] = d; duelOf[d.B] = d;
            foreach (int id in new[] { d.A, d.B }) { LiveThrall l; if (Live.TryGetValue(id, out l)) l.Mode = 0; Grace.Remove(id); }
            ea.SetAttackTarget(eb, 1200); eb.SetAttackTarget(ea, 1200);
            string text = "Duel: " + Label(theirs) + " (" + NameOfKey(theirs.OwnerKey) + ") vs " + Label(mine[0]) + " (" + NameOfKey(key) + "). First to " + Mathf.RoundToInt(BaitConfig.DuelLosePct) + "% health loses.";
            Notify(c.FromId, text); Notify(me, text);
            return;
        }
        string other = a[2];
        string okey = KeyByName(other);
        EntityPlayer op = null;
        if (okey != null) foreach (var p in W.GetPlayers()) if (p != null && KeyOf(p) == okey) op = p;
        if (op == null || okey == key) { Tell(me, "No such player online. " + usage); return; }
        var t = Pick(a.Length > 3 ? a[3].ToLowerInvariant() : null, list);
        if (t.Count != 1) { Tell(me, usage); return; }
        if (LiveEntity(t[0]) == null) { Tell(me, Label(t[0]) + " has to be out to duel."); return; }
        challenges[okey] = new Challenge { FromKey = key, FromId = me, RecId = t[0].Id, Until = Time.time + 60f };
        Notify(op.entityId, NameOfKey(key) + " challenges you to a thrall duel with " + Label(t[0]) + ". /thrall duel accept <your thrall> or /thrall duel decline (60s).");
        Tell(me, "Challenge sent to " + NameOfKey(okey) + ".");
    }

    // Called by the damage patch for a hit between two duelling thralls: nobody dies, the loser stops at DuelLosePct.
    public static void DuelHit(EntityAlive attacker, EntityAlive victim, ref int strength)
    {
        Duel d;
        if (!duelOf.TryGetValue(victim.entityId, out d)) return;
        float floor = victim.GetMaxHealth() * BaitConfig.DuelLosePct / 100f;
        float hp = victim.Stats.Health.Value;
        if (hp - strength > floor) return;
        strength = Math.Max(0, (int)(hp - floor));
        EndDuel(d, attacker.entityId);
    }

    static void DuelTick(World world, float now)
    {
        foreach (var kv in new List<KeyValuePair<int, Duel>>(duelOf))
        {
            var d = kv.Value;
            if (kv.Key != d.A || !duelOf.ContainsKey(d.A)) continue;
            var ea = world.GetEntity(d.A) as EntityAlive; var eb = world.GetEntity(d.B) as EntityAlive;
            if (ea == null || eb == null || ea.IsDead() || eb.IsDead() || now > d.Until) { EndDuel(d, -1); continue; }
            if (ea.GetAttackTarget() != eb) ea.SetAttackTarget(eb, 1200);
            if (eb.GetAttackTarget() != ea) eb.SetAttackTarget(ea, 1200);
        }
        foreach (var kv in new List<KeyValuePair<string, Challenge>>(challenges)) if (now > kv.Value.Until) challenges.Remove(kv.Key);
    }

    static void EndDuel(Duel d, int winner)
    {
        duelOf.Remove(d.A); duelOf.Remove(d.B);
        var ra = Find(d.RecA); var rb = Find(d.RecB);
        foreach (int id in new[] { d.A, d.B })
        {
            LiveThrall l;
            if (!Live.TryGetValue(id, out l)) continue;
            var r = Find(l.RecordId);
            if (r != null) l.Mode = r.Mode;
            var e = W != null ? W.GetEntity(id) as EntityAlive : null;
            if (e != null) e.SetAttackTarget(null, 0);
        }
        string text;
        if (winner < 0 || ra == null || rb == null) text = "The duel ended in a draw.";
        else
        {
            bool aWon = winner == d.A;
            var wr = aWon ? ra : rb; var lr = aWon ? rb : ra;
            text = Label(wr) + " (" + NameOfKey(wr.OwnerKey) + ") won the duel against " + Label(lr) + " (" + NameOfKey(lr.OwnerKey) + ").";
            LiveThrall wl; var le = W.GetEntity(aWon ? d.B : d.A) as EntityAlive; var we = W.GetEntity(winner) as EntityAlive;
            if (Live.TryGetValue(winner, out wl) && le != null && we != null) ThrallKilledSomething(wl, we, le, true);
            CountStat(wr.OwnerKey, "duels", 1);
        }
        Notify(d.OwnerA, text); Notify(d.OwnerB, text);
    }

    // ------------------------------------------------------------------ S58 second chance
    static void SecondChanceCommand(int me, EntityPlayer owner, string arg, List<ThrallRecord> list)
    {
        if (BaitConfig.ReviveSeconds > 0) { Tell(me, "Thralls already come back on this server, so a second chance isn't needed."); return; }
        if (BaitConfig.SecondChanceFeast <= 0) { Tell(me, "Second chances are turned off on this server."); return; }
        var t = Pick(arg, list);
        if (t.Count != 1) { Tell(me, "Usage: /thrall secondchance <number|name>   (" + BaitConfig.SecondChanceFeast + " Feast Bait: it survives its next death once)"); return; }
        if (t[0].SecondChance) { Tell(me, Label(t[0]) + " already has a second chance."); return; }
        if (!TakeItems(owner, BaitCheck.FeastItem, BaitConfig.SecondChanceFeast)) { Tell(me, "You need " + BaitConfig.SecondChanceFeast + " Feast Bait."); return; }
        t[0].SecondChance = true; dirty = true;
        Tell(me, Label(t[0]) + " has a second chance: it comes back once from its next death.");
    }

    // ------------------------------------------------------------------ S59 whistle
    public const string WhistleBuff = "azraelThrallWhistle";
    static void WhistleTick(World world)
    {
        if (!BaitConfig.Whistle) return;
        foreach (var p in world.GetPlayers())
        {
            if (p == null || p.Buffs == null || p.IsDead() || !p.Buffs.HasBuff(WhistleBuff)) continue;
            p.Buffs.RemoveBuff(WhistleBuff);
            string key = KeyOf(p);
            if (key != null) HereCommand(p.entityId, p, null, RosterOf(key));
        }
    }

    // ------------------------------------------------------------------ S60 short commands: /t, /t last, aliases
    static readonly Dictionary<string, string> lastCmd = new Dictionary<string, string>();
    static int chatDepth;
    static readonly string[] KnownVerbs = { "list", "hp", "call", "stow", "tether", "untether", "release", "kill", "name", "stay", "follow", "mode", "heal",
        "revive", "autorevive", "autoheal", "here", "where", "team", "teams", "move", "deaths", "perk", "perks", "guard", "patrol", "bloodmoon", "bm", "top",
        "scout", "evolve", "admin", "exclude", "config", "scream", "pvp", "duel", "secondchance", "retire", "alias", "last", "kennel", "goto", "stats", "hello", "ui", "help" };

    static string AliasOf(string key, string verb) { return PGet(key, "al." + verb); }

    static void AliasCommand(int me, string key, string[] a)
    {
        if (a.Length < 3)
        {
            var l = new List<string>();
            Dictionary<string, string> d;
            if (pdata.TryGetValue(key, out d)) foreach (var kv in d) if (kv.Key.StartsWith("al.")) l.Add(kv.Key.Substring(3) + " = " + kv.Value);
            Tell(me, l.Count == 0 ? "No aliases. /thrall alias <name> <command>, e.g. /thrall alias g guard all 12" : "Aliases: " + string.Join("; ", l.ToArray()));
            return;
        }
        string name = a[2].ToLowerInvariant();
        if (Array.IndexOf(KnownVerbs, name) >= 0) { Tell(me, name + " is already a command."); return; }
        if (a.Length < 4 || a[3].ToLowerInvariant() == "clear") { PSet(key, "al." + name, null); Tell(me, "Alias " + name + " removed."); return; }
        string cmd = string.Join(" ", a, 3, a.Length - 3);
        if (cmd.StartsWith("/")) { var parts = cmd.Split(new[] { ' ' }, 2); cmd = parts.Length > 1 ? parts[1] : ""; }
        if (cmd.Length == 0 || cmd.Length > 120) { Tell(me, "Give the command after the alias name."); return; }
        int count = 0; Dictionary<string, string> dd;
        if (pdata.TryGetValue(key, out dd)) foreach (var k in dd.Keys) if (k.StartsWith("al.")) count++;
        if (count >= 20 && AliasOf(key, name) == null) { Tell(me, "You have 20 aliases already."); return; }
        PSet(key, "al." + name, cmd);
        Tell(me, "/t " + name + " now runs /thrall " + cmd);
    }

    // Returns true when the line was handled here (last or an alias); HandleChat then stops.
    static bool ShortCommand(int me, string key, string verb, string[] a)
    {
        if (chatDepth > 2) return false;
        string line = null;
        if (verb == "last")
        {
            if (!lastCmd.TryGetValue(key, out line)) { Tell(me, "Nothing to repeat yet."); return true; }
        }
        else
        {
            string al = Array.IndexOf(KnownVerbs, verb) < 0 ? AliasOf(key, verb) : null;
            if (al == null) return false;
            line = al + (a.Length > 2 ? " " + string.Join(" ", a, 2, a.Length - 2) : "");
        }
        chatDepth++;
        try { HandleChat(me, "/thrall " + line); }
        finally { chatDepth--; }
        return true;
    }

    static void NoteLast(string key, string verb, string[] a)
    {
        if (verb == "last" || verb == "alias" || chatDepth > 0) return;
        lastCmd[key] = a.Length > 1 ? string.Join(" ", a, 1, a.Length - 1) : "list";
    }

    // ------------------------------------------------------------------ S61 catch-up, S63 owner XP, S64 quest credit
    static int CatchUpCredit(ThrallRecord rec)
    {
        if (!BaitConfig.CatchUpXp) return 1;
        int best = 0;
        foreach (var r in RosterOf(rec.OwnerKey)) if (r.Level > best) best = r.Level;
        return best - rec.Level > BaitConfig.CatchUpGap ? 2 : 1;
    }

    static void OwnerCredit(LiveThrall lt, EntityAlive victim)
    {
        if (victim == null || victim is EntityPlayer || W == null) return;
        var owner = W.GetEntity(lt.OwnerEntityId) as EntityPlayer;
        if (owner == null || owner.IsDead() || (owner.position - victim.position).magnitude > BaitConfig.OwnerCreditRange) return;
        if (BaitConfig.OwnerXpSharePct > 0f)
            try { owner.AddKillXP(victim, ItemValue.None, BaitConfig.OwnerXpSharePct / 100f); } catch (Exception e) { Dbg("owner xp: " + e.Message); }
        if (BaitConfig.QuestKillCredit)
            try { GameManager.Instance.AwardKill(owner, victim); } catch (Exception e) { Dbg("quest credit: " + e.Message); }
    }

    // ------------------------------------------------------------------ S62 retire
    static void RetireCommand(int me, EntityPlayer owner, string key, string arg, string arg2, List<ThrallRecord> list)
    {
        var t = Pick(arg, list);
        if (t.Count != 1) { Tell(me, "Usage: /thrall retire <number|name> [yes]   (level " + BaitConfig.RetireLevel + "+, it leaves for good and you get bait)"); return; }
        var r = t[0];
        if (r.Level < BaitConfig.RetireLevel) { Tell(me, Label(r) + " has to reach level " + BaitConfig.RetireLevel + " to retire."); return; }
        bool feast = r.Level >= BaitConfig.RetireFeastLevel && BaitConfig.RetireFeastLevel > 0;
        int n = feast ? Math.Min(5, Math.Max(1, r.Level / BaitConfig.RetireFeastLevel)) : BaitConfig.RetireRich;
        string reward = n + " " + (feast ? "Feast" : "Rich") + " Bait";
        if (arg2 == null || !arg2.Equals("yes", StringComparison.OrdinalIgnoreCase)) { Tell(me, "Retiring " + Label(r) + " gives you " + reward + " and it leaves for good. To do it: /thrall retire " + arg + " yes"); return; }
        if (!GiveItems(owner, feast ? BaitCheck.FeastItem : BaitCheck.RichItem, n)) { Tell(me, "Could not hand out the reward, so nothing changed."); return; }
        DespawnLive(r.Id); list.Remove(r); dirty = true;
        CountStat(key, "retired", 1);
        Tell(me, Label(r) + " retired after " + r.Kills + " kills. You got " + reward + ".");
        Webhook("**" + Esc(NameOfKey(key)) + "** retired **" + Esc(Label(r)) + "** after " + r.Kills + " kills.");
    }

    // ------------------------------------------------------------------ S65 Alpha creatures
    static readonly Dictionary<int, string> alphas = new Dictionary<int, string>();
    static readonly string[] AlphaNames = { "Grimfang", "Old Scar", "Ashjaw", "Red Maw", "Bonecrusher", "Widowmaker", "Gutripper", "Nightfang", "Ironhide", "Hollow King", "Rotclaw", "the Butcher" };

    public static string AlphaOf(int entityId) { string n; return alphas.TryGetValue(entityId, out n) ? n : null; }

    static void AlphaTick(World world, Dictionary<string, EntityPlayer> online)
    {
        foreach (int id in new List<int>(alphas.Keys)) { var e = world.GetEntity(id) as EntityAlive; if (e == null || e.IsDead() || Live.ContainsKey(id)) alphas.Remove(id); }
        int every = BaitConfig.AlphaEveryDays;
        if (every <= 0 || online.Count == 0) return;
        int day = GameUtils.WorldTimeToDays(world.worldTime);
        if (day <= every || (day - 1) % every != 0 || PGet(ServerKey, "alphaDay") == day.ToString()) return;   // the day after each horde night (8, 15, 22 with 7)
        var players = new List<EntityPlayer>(online.Values);
        var p = players[UnityEngine.Random.Range(0, players.Count)];
        world.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(p.position, new Vector3(120f, 40f, 120f)), b3Tmp);
        var pick = new List<EntityAlive>();
        foreach (var o in b3Tmp)
        {
            var z = o as EntityAlive;
            if (z == null || z is EntityPlayer || z.IsSleeping || !CanTame(z) || (z.position - p.position).magnitude < 20f) continue;
            if (!(z is EntityZombie) && !(z is EntityAnimal)) continue;
            pick.Add(z);
        }
        b3Tmp.Clear();
        if (pick.Count == 0) return;
        var a = pick[UnityEngine.Random.Range(0, pick.Count)];
        string name = AlphaNames[UnityEngine.Random.Range(0, AlphaNames.Length)];
        alphas[a.entityId] = name;
        int lvl = Math.Min(BaitConfig.MaxLevel, Math.Max(LevelOf(a.entityId), DayLevel(ClassName(a))) + BaitConfig.AlphaLevelBonus);
        levels[a.entityId] = lvl; ApplyMarker(a, lvl);
        PSet(ServerKey, "alphaDay", day.ToString());
        string msg = "An Alpha roams: " + name + " the " + Pretty(ClassName(a)) + ", level " + lvl + ", " + Mathf.RoundToInt((a.position - p.position).magnitude) + "m " + Compass(p.position, a.position) + " of " + p.EntityName + ". Only Feast Bait tames it.";
        try { GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, "[ff9933][Bait][-] " + msg, null, EMessageSender.Server); } catch (Exception e) { Dbg("alpha chat: " + e.Message); }
        Webhook("**Alpha:** " + Esc(msg));
        Log.Out("[Bait] " + msg);
    }

    static void MakeAlphaThrall(ThrallRecord rec, string name, EntityAlive z)
    {
        alphas.Remove(z.entityId);
        rec.Alpha = true;
        rec.Name = name;
        if (string.IsNullOrEmpty(rec.Perks)) rec.Perks = "THRA"[UnityEngine.Random.Range(0, 4)].ToString();
        CountStat(rec.OwnerKey, "alphas", 1);
        string msg = NameOfKey(rec.OwnerKey) + " tamed the Alpha " + name + " the " + Pretty(rec.ClassName) + ".";
        try { GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, "[ff9933][Bait][-] " + msg, null, EMessageSender.Server); } catch { }
        Webhook("**Alpha tamed:** " + Esc(msg));
    }

    // ------------------------------------------------------------------ S67 taming resistance
    static bool Resists(EntityAlive z, string cls, Feed f)
    {
        if (BaitConfig.TamingResistPct <= 0f || TierOf(cls) < 2 || UnityEngine.Random.value * 100f >= BaitConfig.TamingResistPct) return false;
        f.Count = Math.Max(0, f.Count - 1);
        z.distraction = null; z.pendingDistraction = null; z.IsEating = false;
        return true;
    }

    // ------------------------------------------------------------------ S66 feeder and S68 kennel blocks
    public const string FeederBlock = "azraelBaitFeeder";
    public const string KennelBlock = "azraelThrallKennel";
    class Station { public string Kind, Owner; }
    static readonly Dictionary<Vector3i, Station> stations = new Dictionary<Vector3i, Station>();
    static bool stationsDirty, stationsLoaded;
    static float nextStationScan, nextFeeder;
    static string StationsPath { get { return Path.Combine(GameIO.GetSaveGameDir(), "AzraelBaitStations.tsv"); } }

    static void LoadStations()
    {
        stations.Clear(); stationsDirty = false; stationsLoaded = true;
        try
        {
            if (!File.Exists(StationsPath)) return;
            foreach (string line in File.ReadAllLines(StationsPath))
            {
                string[] p = line.Split('\t');
                if (p.Length < 3) continue;
                string[] xyz = p[1].Split(',');
                if (xyz.Length != 3) continue;
                stations[new Vector3i(PI(xyz[0]), PI(xyz[1]), PI(xyz[2]))] = new Station { Kind = p[0], Owner = p[2] };
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read stations: " + e.Message); }
    }

    static void FlushStations()
    {
        if (!stationsDirty) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in stations) sb.Append(kv.Value.Kind).Append('\t').Append(kv.Key.x).Append(',').Append(kv.Key.y).Append(',').Append(kv.Key.z).Append('\t').Append(kv.Value.Owner).Append('\n');
            WriteSafe(StationsPath, sb.ToString());
            stationsDirty = false;
        }
        catch (Exception e) { Log.Warning("[Bait] could not save stations: " + e.Message); }
    }

    static string OwnerKeyOf(PlatformUserIdentifierAbs id)
    {
        if (id == null) return null;
        try
        {
            var d = GameManager.Instance.GetPersistentPlayerList().GetPlayerData(id);
            if (d != null && d.PrimaryId != null) return d.PrimaryId.CombinedString;
        }
        catch { }
        return id.CombinedString;
    }

    // Every 10 s: the feeders and kennels in loaded chunks (placed, picked up or destroyed since the last look).
    static void StationScan(World world, float now)
    {
        if (now < nextStationScan || world.ChunkCache == null) return;
        nextStationScan = now + 10f;
        if (!stationsLoaded) LoadStations();
        foreach (var chunk in world.ChunkCache.GetChunkArrayCopySync())
        {
            if (chunk == null) continue;
            var found = new HashSet<Vector3i>();
            var tes = chunk.GetTileEntities();
            if (tes != null)
                foreach (var te in tes.list)
                {
                    var comp = te as TileEntityComposite;
                    if (comp == null) continue;
                    string b = BlockNameOf(te);
                    if (b != FeederBlock && b != KennelBlock) continue;
                    var pos = te.ToWorldPos();
                    found.Add(pos);
                    string owner = OwnerKeyOf(comp.Owner);
                    Station st;
                    if (!stations.TryGetValue(pos, out st) || st.Kind != b || st.Owner != owner) { stations[pos] = new Station { Kind = b, Owner = owner }; stationsDirty = true; }
                }
            foreach (var pos in new List<Vector3i>(stations.Keys))
                if (!found.Contains(pos) && World.toChunkXZ(pos.x) == chunk.X && World.toChunkXZ(pos.z) == chunk.Z) { stations.Remove(pos); stationsDirty = true; }
        }
        FlushStations();
    }

    static Vector3 Center(Vector3i p) { return new Vector3(p.x + 0.5f, p.y + 0.5f, p.z + 0.5f); }

    static void FeederTick(World world, Dictionary<string, EntityPlayer> online, float now)
    {
        if (!BaitConfig.Feeders || now < nextFeeder) return;
        nextFeeder = now + Mathf.Max(5f, BaitConfig.FeederSeconds);
        foreach (var kv in new List<KeyValuePair<Vector3i, Station>>(stations))
        {
            if (kv.Value.Kind != FeederBlock || kv.Value.Owner == null) continue;
            var c = Center(kv.Key);
            if (!ChunkLoaded(c)) continue;
            var te = world.GetTileEntity(kv.Key);
            var comp = te as TileEntityComposite;
            var s = comp != null ? comp.GetFeature<TEFeatureStorage>() : null;
            var items = s != null && s.ItemGrid != null ? s.ItemGrid.items : null;
            if (items == null) continue;
            int slot = -1;
            for (int k = 0; k < BaitKinds.Length && slot < 0; k++)
                for (int i = 0; i < items.Length; i++)
                    if (items[i] != null && !items[i].IsEmpty() && items[i].itemValue.ItemClass != null && items[i].itemValue.ItemClass.GetItemName() == BaitKinds[k]) { slot = i; break; }
            if (slot < 0) continue;
            world.GetEntitiesInBounds(typeof(EntityAnimal), new Bounds(c, new Vector3(80f, 20f, 80f)), b3Tmp);
            EntityAlive best = null; float bd = float.MaxValue;
            foreach (var o in b3Tmp)
            {
                var an = o as EntityAlive;
                if (an == null || !CanTame(an)) continue;
                float d = (an.position - c).magnitude;
                if (d <= BaitConfig.FeederRadius) { if (d < bd) { bd = d; best = an; } }
                else if (d <= 40f && an.GetAttackTarget() == null) an.SetInvestigatePosition(c, 600, false);   // the smell draws it in
            }
            b3Tmp.Clear();
            if (best == null) continue;
            string item = items[slot].itemValue.ItemClass.GetItemName();
            items[slot].count--;
            if (items[slot].count <= 0) items[slot] = ItemStack.Empty.Clone();
            te.SetModified();
            EntityPlayer owner;
            online.TryGetValue(kv.Value.Owner, out owner);
            CountMealFor(best, kv.Value.Owner, owner, BaitCheck.Meals(item), item, c);
        }
    }

    static List<Vector3i> KennelsOf(string key)
    {
        var l = new List<Vector3i>();
        foreach (var kv in stations) if (kv.Value.Kind == KennelBlock && kv.Value.Owner == key) l.Add(kv.Key);
        return l;
    }

    // Inside any kennel's circle a thrall heals faster (every 5 s from Slow).
    static void KennelCare(EntityAlive e)
    {
        if (stations.Count == 0 || BaitConfig.KennelHealPct <= 0f) return;
        foreach (var kv in stations)
        {
            if (kv.Value.Kind != KennelBlock || Flat(e.position - Center(kv.Key)) > BaitConfig.KennelRadius + 1f) continue;
            float max = e.GetMaxHealth();
            if (e.Stats.Health.Value < max) e.Stats.Health.Value = Mathf.Min(max, e.Stats.Health.Value + max * BaitConfig.KennelHealPct / 100f);
            return;
        }
    }

    // ------------------------------------------------------------------ walking somewhere: /thrall kennel and /thrall goto (Bait UI's go-to ping)
    class GotoT { public Vector3 Goal; public string RecId; public int Owner; public bool Kennel; public float Until; }
    static readonly Dictionary<int, GotoT> gotos = new Dictionary<int, GotoT>();
    public static bool GotoGoal(int entityId, out Vector3 goal)
    {
        GotoT g;
        if (gotos.TryGetValue(entityId, out g)) { goal = g.Goal; return true; }
        goal = Vector3.zero; return false;
    }

    static void KennelCommand(int me, EntityPlayer owner, string key, string arg, List<ThrallRecord> list)
    {
        var t = Pick(arg, list);
        if (t.Count == 0) { Tell(me, "Usage: /thrall kennel <number|name|all>   (it walks to your nearest Thrall Kennel and stays there)"); return; }
        var ks = KennelsOf(key);
        if (ks.Count == 0) { Tell(me, "You have no Thrall Kennel. Craft one and place it first (the server sees it within 10s)."); return; }
        Vector3i best = ks[0]; float bd = float.MaxValue;
        foreach (var k in ks) { float d = (Center(k) - owner.position).sqrMagnitude; if (d < bd) { bd = d; best = k; } }
        var c = Center(best);
        int walking = 0, moved = 0;
        foreach (var r in t)
        {
            var e = LiveEntity(r);
            if (e != null && !r.Tether && (e.position - c).magnitude < 150f)
            {
                gotos[e.entityId] = new GotoT { Goal = c, RecId = r.Id, Owner = me, Kennel = true, Until = Time.time + 90f };
                e.SetAttackTarget(null, 0);
                walking++;
            }
            else { SetKennel(r, c); moved++; }
        }
        dirty = true; nextSlow = 0f;
        Tell(me, (walking > 0 ? walking + " walking home to the kennel " + Mathf.RoundToInt((c - owner.position).magnitude) + "m " + Compass(owner.position, c) + ". " : "")
                 + (moved > 0 ? moved + " sent straight to the kennel." : ""));
    }

    static void SetKennel(ThrallRecord r, Vector3 c)
    {
        r.Active = true; r.Tether = true; r.TX = c.x; r.TY = c.y; r.TZ = c.z; r.TR = BaitConfig.KennelRadius;
        ClearPost(r);
        DespawnLive(r.Id); nextSpawn.Remove(r.Id);
        dirty = true;
    }

    static void GotoCommand(int me, EntityPlayer owner, string[] a, List<ThrallRecord> list)
    {
        var t = Pick(a.Length > 2 ? a[2].ToLowerInvariant() : null, list);
        float x, y, z;
        if (t.Count == 0 || a.Length < 6 || !float.TryParse(a[3], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            || !float.TryParse(a[4], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !float.TryParse(a[5], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
        { Tell(me, "Usage: /thrall goto <number|name|all> <x> <y> <z>   (Bait UI sends this from the go-to ping)"); return; }
        var goal = new Vector3(x, y, z);
        if ((goal - owner.position).magnitude > 150f) { Tell(me, "That spot is too far away (150m at most)."); return; }
        int n = 0;
        foreach (var r in t)
        {
            var e = LiveEntity(r);
            if (e == null || r.Tether) continue;
            gotos[e.entityId] = new GotoT { Goal = goal + new Vector3(UnityEngine.Random.Range(-1f, 1f) * (t.Count > 1 ? 2f : 0f), 0f, UnityEngine.Random.Range(-1f, 1f) * (t.Count > 1 ? 2f : 0f)), RecId = r.Id, Owner = me, Until = Time.time + 60f };
            e.SetAttackTarget(null, 0);
            n++;
        }
        Tell(me, n == 0 ? "None of those are out and following you." : n + " on the way. They hold that spot when they get there (/thrall follow to call them back).");
    }

    static void GotoTick(World world, float now)
    {
        if (gotos.Count == 0) return;
        foreach (var kv in new List<KeyValuePair<int, GotoT>>(gotos))
        {
            var g = kv.Value;
            var e = world.GetEntity(kv.Key) as EntityAlive;
            LiveThrall lt;
            if (e == null || e.IsDead() || !Live.TryGetValue(kv.Key, out lt)) { gotos.Remove(kv.Key); continue; }
            if (Flat(e.position - g.Goal) > 2.5f && now < g.Until) continue;
            gotos.Remove(kv.Key);
            var rec = Find(g.RecId);
            if (rec == null) continue;
            if (g.Kennel) { SetKennel(rec, g.Goal); Notify(g.Owner, Label(rec) + " is home at its kennel."); }
            else { rec.Stay = true; lt.Hold = true; dirty = true; }
        }
    }

    // ------------------------------------------------------------------ S69 quiet thralls
    public static bool Quiet(Entity instigator) { return BaitConfig.QuietThralls && instigator != null && Live.ContainsKey(instigator.entityId); }

    // ------------------------------------------------------------------ S73 admin audit log
    static readonly List<string> audit = new List<string>();
    static string AuditPath { get { return Path.Combine(GameIO.GetSaveGameDir(), "AzraelBaitAudit.log"); } }

    static void Audit(int me, string what)
    {
        string who = "#" + me;
        try { var p = W != null ? W.GetEntity(me) as EntityPlayer : null; if (p != null) who = p.EntityName; } catch { }
        string line = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC  " + who + ": " + what;
        audit.Add(line);
        if (audit.Count > 200) audit.RemoveRange(0, audit.Count - 200);
        try { File.AppendAllText(AuditPath, line + "\n"); } catch (Exception e) { Log.Warning("[Bait] could not write the audit log: " + e.Message); }
        Log.Out("[Bait] audit: " + line);
    }

    static void LoadAudit()
    {
        audit.Clear();
        try
        {
            if (!File.Exists(AuditPath)) return;
            var lines = File.ReadAllLines(AuditPath);
            for (int i = Math.Max(0, lines.Length - 200); i < lines.Length; i++) audit.Add(lines[i]);
        }
        catch { }
    }

    static void AuditCommand(int me, string[] a)
    {
        int n = 10, nn;
        if (a.Length > 3 && int.TryParse(a[3], out nn)) n = Mathf.Clamp(nn, 1, 50);
        if (audit.Count == 0) { Tell(me, "The admin log is empty."); return; }
        for (int i = Math.Max(0, audit.Count - n); i < audit.Count; i++) Tell(me, audit[i]);
    }

    // ------------------------------------------------------------------ S74 name rules
    static string NameProblem(string nm)
    {
        if (nm.Length > Math.Max(1, BaitConfig.NameMaxLength)) return "Names can be at most " + BaitConfig.NameMaxLength + " letters here.";
        foreach (string w in BaitConfig.BannedNames) if (nm.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return "That name is not allowed on this server.";
        return null;
    }

    static void BanwordCommand(int me, string[] a)
    {
        string sub = a.Length > 3 ? a[3].ToLowerInvariant() : "list";
        if (sub == "list") { Tell(me, BaitConfig.BannedNames.Count == 0 ? "No banned name words." : "Banned in names: " + string.Join(", ", new List<string>(BaitConfig.BannedNames).ToArray())); return; }
        if ((sub != "add" && sub != "remove") || a.Length < 5) { Tell(me, "Usage: /thrall admin banword list | add <word> | remove <word>"); return; }
        if (sub == "add") BaitConfig.BannedNames.Add(a[4]); else BaitConfig.BannedNames.Remove(a[4]);
        BaitConfig.SaveRuntime();
        Audit(me, "banword " + sub + " " + a[4]);
        Tell(me, sub == "add" ? a[4] + " is now banned in thrall names." : a[4] + " is allowed again.");
    }

    // ------------------------------------------------------------------ S75 stats
    static void StatsCommand(int me, string key, string arg)
    {
        if (arg == "server")
        {
            int owners = 0, total = 0, kills = 0, best = 0, alphaN = 0; string bestLabel = "-";
            var byClass = new Dictionary<string, int>();
            foreach (var kv in roster)
            {
                if (kv.Value.Count > 0) owners++;
                foreach (var r in kv.Value)
                {
                    total++; kills += r.Kills; if (r.Alpha) alphaN++;
                    if (r.Level > best) { best = r.Level; bestLabel = Label(r) + " (" + NameOfKey(r.OwnerKey) + ")"; }
                    string c = Pretty(r.ClassName); int n; byClass.TryGetValue(c, out n); byClass[c] = n + 1;
                }
            }
            string top = "-"; int topN = 0;
            foreach (var kv in byClass) if (kv.Value > topN) { topN = kv.Value; top = kv.Key; }
            Tell(me, "Server: " + total + " thralls held by " + owners + " players, " + kills + " kills in all, " + alphaN + " Alphas.");
            Tell(me, "Highest: " + bestLabel + ". Most tamed: " + top + " (" + topN + "). Live now: " + Live.Count + (alphas.Count > 0 ? ", an Alpha roams" : "") + ".");
            return;
        }
        var list = RosterOf(key);
        int k = 0, d = 0, hi = 0, active = 0; string hiLabel = "-";
        foreach (var r in list) { k += r.Kills; d += r.Deaths; if (r.Active) active++; if (r.Level > hi) { hi = r.Level; hiLabel = Label(r); } }
        Tell(me, "You: " + list.Count + " thralls (" + active + " out), " + k + " kills, " + d + " deaths. Best: " + hiLabel + ".");
        Tell(me, "Lifetime: tamed " + PI(PGet(key, "st.tamed")) + ", retired " + PI(PGet(key, "st.retired")) + ", Alphas " + PI(PGet(key, "st.alphas"))
                 + ", duels won " + PI(PGet(key, "st.duels")) + ". Thrall power " + PowerOf(key) + ".   (/thrall stats server)");
    }

    // ------------------------------------------------------------------ U59 admin player browser (Bait UI)
    static void AdminUiCommand(int me, string[] a)
    {
        var recs = new List<string>();
        var sb = new StringBuilder("AP|");
        bool first = true;
        foreach (var kv in roster)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Clean(NameOfKey(kv.Key)).Replace(',', ' ').Replace(':', ' ')).Append(':').Append(kv.Value.Count);
        }
        recs.Add(sb.ToString());
        string tkey = a.Length > 3 ? KeyByName(a[3]) : null;
        if (tkey != null)
        {
            var tl = RosterOf(tkey);
            recs.Add("AH|" + Clean(NameOfKey(tkey)) + "|" + tl.Count + "|" + MaxOwnedFor(tkey) + "|" + MaxActiveFor(tkey));
            for (int i = 0; i < tl.Count; i++)
            {
                var r = tl[i];
                long down = r.ReviveAt - NowSec();
                string st = down > 0 ? "downed" : liveByRecord.ContainsKey(r.Id) ? "out" : r.Tether ? "tethered" : r.Active ? "away" : "stowed";
                recs.Add("AT|" + (i + 1) + "|" + Clean(Label(r)) + "|" + r.Level + "|" + r.Kills + "|" + st);
            }
        }
        UiSend(me, recs);
    }

    // ------------------------------------------------------------------ the extra ticks
    static void Batch3Second(World world, float now)
    {
        BodyguardTick(world, now);
        PvpTick(world);
        DuelTick(world, now);
        GotoTick(world, now);
    }

    static void Batch3Slow(World world, Dictionary<string, EntityPlayer> online, float now)
    {
        StationScan(world, now);
        FeederTick(world, online, now);
        AlphaTick(world, online);
    }
}
