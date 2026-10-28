using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Roles (1.10.0): perks, guard posts, patrol routes, blood moon mode, the leaderboard, scouting, evolution, extra admin
// tools, Discord webhook events and the taming helper feed for Bait UI. All server side.
public static partial class Thralls
{
    // ------------------------------------------------------------------ perks
    // One perk slot opens at each of PerkLevel1/2/3. A perk is one letter in ThrallRecord.Perks:
    // T tough (takes less damage), H hit (deals more), R regen (heals faster), A taunt (pulls zombies off you, animals too).
    static readonly string[] PerkNames = { "tough", "hit", "regen", "taunt" };
    const string PerkLetters = "THRA";

    static int PerkSlots(int level)
    {
        int n = 0;
        foreach (int l in new[] { BaitConfig.PerkLevel1, BaitConfig.PerkLevel2, BaitConfig.PerkLevel3 }) if (l > 0 && level >= l) n++;
        return n;
    }

    static string PerkText(string perks)
    {
        if (string.IsNullOrEmpty(perks)) return "none";
        var l = new List<string>();
        foreach (char c in perks) { int i = PerkLetters.IndexOf(c); if (i >= 0) l.Add(PerkNames[i]); }
        return string.Join(", ", l.ToArray());
    }

    // how many of one perk a live thrall has (used by the damage patch, regen and taunt)
    public static int PerkCount(int entityId, char perk)
    {
        LiveThrall lt;
        if (!Live.TryGetValue(entityId, out lt) || string.IsNullOrEmpty(lt.Perks)) return 0;
        int n = 0;
        foreach (char c in lt.Perks) if (c == perk) n++;
        return n;
    }

    static void SyncLive(ThrallRecord r)
    {
        int lid; LiveThrall l;
        if (liveByRecord.TryGetValue(r.Id, out lid) && Live.TryGetValue(lid, out l)) { l.Perks = r.Perks ?? ""; l.Mode = r.Mode; }
    }

    static void PerkCommand(int me, EntityPlayer owner, string arg, string arg2, List<ThrallRecord> list)
    {
        var t = Pick(arg, list);
        if (t.Count != 1) { Tell(me, "Usage: /thrall perk <number|name> [tough|hit|regen|taunt|reset]   (slots open at levels " + BaitConfig.PerkLevel1 + ", " + BaitConfig.PerkLevel2 + " and " + BaitConfig.PerkLevel3 + ")"); return; }
        var r = t[0];
        string have = r.Perks ?? "";
        int slots = PerkSlots(r.Level);
        if (arg2 == null)
        {
            Tell(me, Label(r) + ": perks " + PerkText(have) + ", " + have.Length + "/" + slots + " slots used. Pick with /thrall perk " + (list.IndexOf(r) + 1) + " tough|hit|regen|taunt.");
            return;
        }
        string w = arg2.ToLowerInvariant();
        if (w == "reset" || w == "clear")
        {
            if (have.Length == 0) { Tell(me, Label(r) + " has no perks."); return; }
            int cost = BaitConfig.PerkResetFeast, b, g;
            if (cost > 0)
            {
                if (ItemCount(owner, BaitCheck.FeastItem, out b, out g) < cost) { Tell(me, "Clearing perks costs " + cost + " Feast Bait."); return; }
                if (!TakeItems(owner, BaitCheck.FeastItem, cost)) { Tell(me, "Could not take the Feast Bait. Try again."); return; }
            }
            r.Perks = ""; dirty = true; SyncLive(r);
            Tell(me, Label(r) + "'s perks are cleared" + (cost > 0 ? " (" + cost + " Feast Bait)" : "") + ". Pick new ones.");
            return;
        }
        int pi = -1;
        for (int i = 0; i < PerkNames.Length; i++) if (PerkNames[i].StartsWith(w)) { pi = i; break; }
        if (w == "strong" || w == "damage") pi = 1;
        if (pi < 0) { Tell(me, "Perks: tough (takes " + F(BaitConfig.PerkToughPct) + "% less), hit (" + F(BaitConfig.PerkHitPct) + "% more damage), regen (x" + F(BaitConfig.PerkRegenMult) + " regen), taunt (pulls zombies off you)."); return; }
        if (have.Length >= slots)
        {
            int next = have.Length == 0 ? BaitConfig.PerkLevel1 : have.Length == 1 ? BaitConfig.PerkLevel2 : BaitConfig.PerkLevel3;
            Tell(me, slots == 0 || have.Length < 3 ? Label(r) + " gets its next perk slot at level " + next + "." : Label(r) + " has all three perks. /thrall perk " + (list.IndexOf(r) + 1) + " reset to choose again.");
            return;
        }
        r.Perks = have + PerkLetters[pi]; dirty = true; SyncLive(r);
        Tell(me, Label(r) + " learned " + PerkNames[pi] + ". Perks: " + PerkText(r.Perks) + ".");
    }

    // ------------------------------------------------------------------ guard posts and patrols
    // A guard is a tethered thrall that also goes after anything hostile that comes inside its circle, and tells its owner
    // when it fights while the owner is away. A patrol walks between points instead of wandering, and guards the same way.
    static void ClearPost(ThrallRecord r) { r.Guard = false; r.Patrol = false; r.BmCalled = false; }

    static void GuardCommand(int me, EntityPlayer owner, string arg, string arg2, List<ThrallRecord> list)
    {
        float radius = BaitConfig.TetherRadius, rr;
        if (arg2 != null && float.TryParse(arg2, NumberStyles.Float, CultureInfo.InvariantCulture, out rr)) radius = rr;
        radius = Mathf.Clamp(radius, 2f, BaitConfig.TetherMaxRadius);
        var t = Pick(arg, list);
        if (t.Count == 0) { Tell(me, "Usage: /thrall guard <number|name|all> [radius]   (stand where the post should be)"); return; }
        if (BaitConfig.TetherRespectClaims && Claimed(owner, owner.position)) { Tell(me, "That spot is inside someone else's land claim."); return; }
        foreach (var r in t)
        {
            r.Tether = true; r.Active = true; r.TX = owner.position.x; r.TY = owner.position.y; r.TZ = owner.position.z; r.TR = radius;
            ClearPost(r); r.Guard = true;
            DespawnLive(r.Id); nextSpawn.Remove(r.Id);
        }
        dirty = true; nextSlow = 0f;
        Tell(me, (t.Count == 1 ? Label(t[0]) + " guards" : t.Count + " thralls guard") + " this spot, radius " + F(radius) + ". They attack anything hostile that comes in (not in passive mode) and you get a message when they fight while you are away.");
    }

    static List<Vector3> PatrolPoints(ThrallRecord r)
    {
        var l = new List<Vector3>();
        if (string.IsNullOrEmpty(r.PatrolPts)) return l;
        foreach (string s in r.PatrolPts.Split(';'))
        {
            var c = s.Split(',');
            if (c.Length == 3) l.Add(new Vector3(P(c[0]), P(c[1]), P(c[2])));
        }
        return l;
    }

    static string PointsText(List<Vector3> pts)
    {
        var sb = new StringBuilder();
        foreach (var v in pts) { if (sb.Length > 0) sb.Append(';'); sb.Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)); }
        return sb.ToString();
    }

    static void PatrolCommand(int me, EntityPlayer owner, string arg, string arg2, List<ThrallRecord> list)
    {
        string usage = "Usage: /thrall patrol <number|name|all> add | start | stop | clear | list   (add a point where you stand, at least 2 to start)";
        var t = Pick(arg, list);
        string sub = arg2 != null ? arg2.ToLowerInvariant() : "list";
        if (t.Count == 0) { Tell(me, usage); return; }
        switch (sub)
        {
            case "add":
            {
                if (BaitConfig.TetherRespectClaims && Claimed(owner, owner.position)) { Tell(me, "That spot is inside someone else's land claim."); return; }
                foreach (var r in t)
                {
                    var pts = PatrolPoints(r);
                    if (pts.Count >= BaitConfig.PatrolMaxPoints) { Tell(me, Label(r) + " already has " + pts.Count + " points (the most)."); continue; }
                    pts.Add(owner.position);
                    r.PatrolPts = PointsText(pts);
                    if (r.Patrol) { SetPatrolArea(r, pts); DespawnLive(r.Id); nextSpawn.Remove(r.Id); }
                }
                dirty = true; nextSlow = 0f;
                Tell(me, "Point " + PatrolPoints(t[0]).Count + " added" + (t.Count > 1 ? " for " + t.Count + " thralls" : " for " + Label(t[0])) + "." + (PatrolPoints(t[0]).Count >= 2 && !t[0].Patrol ? " /thrall patrol " + arg + " start" : ""));
                return;
            }
            case "clear":
                foreach (var r in t) { r.PatrolPts = ""; if (r.Patrol) { r.Patrol = false; r.Tether = false; DespawnLive(r.Id); nextSpawn.Remove(r.Id); } }
                dirty = true; nextSlow = 0f;
                Tell(me, "Patrol points cleared.");
                return;
            case "start":
            {
                int started = 0;
                foreach (var r in t)
                {
                    var pts = PatrolPoints(r);
                    if (pts.Count < 2) continue;
                    ClearPost(r);
                    r.Patrol = true; r.Tether = true; r.Active = true;
                    SetPatrolArea(r, pts);
                    DespawnLive(r.Id); nextSpawn.Remove(r.Id);
                    started++;
                }
                dirty = true; nextSlow = 0f;
                Tell(me, started == 0 ? "A patrol needs at least 2 points: stand at each spot and use /thrall patrol " + arg + " add." : started + " on patrol. They walk the route, fight what comes near it, and stay there while you are away.");
                return;
            }
            case "stop":
                foreach (var r in t) { if (!r.Patrol) continue; r.Patrol = false; r.Tether = false; DespawnLive(r.Id); nextSpawn.Remove(r.Id); }
                dirty = true; nextSlow = 0f;
                Tell(me, "Patrol stopped. They follow you again (the points are kept).");
                return;
            default:
                foreach (var r in t)
                {
                    var pts = PatrolPoints(r);
                    Tell(me, Label(r) + ": " + pts.Count + " points" + (r.Patrol ? ", patrolling" : "") + (pts.Count > 0 ? ", first one " + Mathf.RoundToInt((pts[0] - owner.position).magnitude) + "m " + Compass(owner.position, pts[0]) : "") + ".");
                }
                return;
        }
    }

    // The patrol's tether circle (its centre is where the chunk check and the snap back go) covers every point.
    static void SetPatrolArea(ThrallRecord r, List<Vector3> pts)
    {
        Vector3 c = Vector3.zero;
        foreach (var v in pts) c += v;
        c /= pts.Count;
        float far = 0f;
        foreach (var v in pts) far = Mathf.Max(far, new Vector2(v.x - c.x, v.z - c.z).magnitude);
        r.TX = c.x; r.TY = c.y; r.TZ = c.z;
        r.TR = Mathf.Max(4f, far + 4f);
    }

    static readonly Dictionary<string, float> guardToast = new Dictionary<string, float>();
    static readonly List<Entity> guardTmp = new List<Entity>();

    // Once a second: guards and patrols pick fights inside their circle, drop ones that run off, and tell an absent owner.
    static void GuardTick(World world, float now)
    {
        foreach (var lt in new List<LiveThrall>(Live.Values))
        {
            if (!lt.Guard || lt.Mode >= 2) continue;
            var e = world.GetEntity(lt.EntityId) as EntityAlive;
            if (e == null || e.IsDead()) continue;
            Vector3 c = new Vector3(lt.GX, lt.GY, lt.GZ);
            float r = Mathf.Max(2f, lt.GR);
            var target = e.GetAttackTarget();
            if (target != null && (target.IsDead() || Flat(target.position - c) > r * 1.5f + 6f)) { e.SetAttackTarget(null, 0); target = null; }
            if (target == null)
            {
                world.GetEntitiesInBounds(typeof(EntityEnemy), new Bounds(c, new Vector3(r * 2f, 12f, r * 2f)), guardTmp);
                EntityAlive best = null; float bd = float.MaxValue;
                foreach (var o in guardTmp)
                {
                    var z = o as EntityAlive;
                    if (z == null || z.IsDead() || Live.ContainsKey(z.entityId) || Flat(z.position - c) > r) continue;
                    float d = (z.position - e.position).sqrMagnitude;
                    if (d < bd) { bd = d; best = z; }
                }
                guardTmp.Clear();
                if (best != null)
                {
                    e.SetAttackTarget(best, 600);
                    if (best is EntityZombie) TeachFightBack(best);
                    if (best.GetAttackTarget() == null) best.SetAttackTarget(e, 400);
                    target = e.GetAttackTarget();
                }
            }
            if (target == null || lt.OwnerEntityId < 0) continue;
            var owner = world.GetEntity(lt.OwnerEntityId) as EntityPlayer;
            if (owner == null || Flat(owner.position - c) < r + 30f) continue;
            float last;
            if (guardToast.TryGetValue(lt.RecordId, out last) && now - last < BaitConfig.GuardAlertSeconds) continue;
            guardToast[lt.RecordId] = now;
            var rec = Find(lt.RecordId);
            if (rec != null)
                Notify(lt.OwnerEntityId, Label(rec) + " is fighting " + Pretty(ClassName(target)) + " at its " + (rec.Patrol ? "patrol" : "guard post") + " (" + Mathf.RoundToInt(Flat(owner.position - c)) + "m " + Compass(owner.position, c) + ").");
        }
    }

    static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

    // ------------------------------------------------------------------ per-player settings (blood moon mode)
    // AzraelBaitPlayers.tsv: owner key, then key=value pairs. Only bm (off, call, fight, both) for now.
    static readonly Dictionary<string, string> bmMode = new Dictionary<string, string>();
    static bool playersDirty;
    const string PlayersFile = "AzraelBaitPlayers.tsv";
    static string PlayersPath { get { return Path.Combine(GameIO.GetSaveGameDir(), PlayersFile); } }

    static void LoadPlayers()
    {
        bmMode.Clear(); playersDirty = false;
        try
        {
            if (!File.Exists(PlayersPath)) return;
            foreach (string line in File.ReadAllLines(PlayersPath))
            {
                string[] p = line.Split('\t');
                if (p.Length < 2 || p[0].Length == 0) continue;
                for (int i = 1; i < p.Length; i++)
                    if (p[i].StartsWith("bm=")) bmMode[p[0]] = p[i].Substring(3);
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read player settings: " + e.Message); }
    }

    static void FlushPlayers()
    {
        if (!playersDirty || !loaded) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in bmMode) if (kv.Value != "off") sb.Append(kv.Key).Append("\tbm=").Append(kv.Value).Append('\n');
            WriteSafe(PlayersPath, sb.ToString());
            try { File.Copy(PlayersPath, Path.Combine(KeepDir, PlayersFile), true); } catch { }
            playersDirty = false;
        }
        catch (Exception e) { Log.Warning("[Bait] could not save player settings: " + e.Message); }
    }

    static string BmOf(string key) { string m; return bmMode.TryGetValue(key, out m) ? m : "off"; }

    static void BloodMoonCommand(int me, string key, string arg)
    {
        if (!BaitConfig.BloodMoonMode) { Tell(me, "Blood moon mode is turned off on this server."); return; }
        if (arg == null) { Tell(me, "Blood moon mode: " + BmOf(key) + ". /thrall bloodmoon off|call|fight|both  (call: tethered thralls come to you for the night; fight: thralls out with you switch to aggressive)"); return; }
        string m = arg.StartsWith("c") ? "call" : arg.StartsWith("f") ? "fight" : arg.StartsWith("b") ? "both" : arg.StartsWith("o") ? "off" : null;
        if (m == null) { Tell(me, "Usage: /thrall bloodmoon off|call|fight|both"); return; }
        bmMode[key] = m; playersDirty = true;
        Tell(me, "Blood moon mode: " + m + "." + (m == "off" ? "" : bmOn ? " The horde is on now, so it applies right away." : " It starts with the next horde night and ends at dawn."));
        bmNext = 0f;
    }

    static bool bmOn;
    static float bmNext;

    static bool BloodMoonNow(World world)
    {
        try
        {
            int bmDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
            var dd = GameUtils.CalcDuskDawnHours(GameStats.GetInt(EnumGameStats.DayLightLength));
            return bmDay > 0 && GameUtils.IsBloodMoonTime(world.worldTime, dd, bmDay);
        }
        catch { return false; }
    }

    // Every few seconds: while the horde is on, apply each online player's mode; when it is over (or a player turns it
    // off) put everything back. What was changed is kept on the record, so a restart mid-horde still restores it.
    static void BloodMoonTick(World world, Dictionary<string, EntityPlayer> online, float now)
    {
        if (now < bmNext) return;
        bmNext = now + 5f;
        bool on = BaitConfig.BloodMoonMode && BloodMoonNow(world);
        bool changed = on != bmOn, any = false;
        bmOn = on;
        foreach (var kv in roster)
        {
            string m = BmOf(kv.Key);
            EntityPlayer owner;
            bool has = online.TryGetValue(kv.Key, out owner);
            bool call = on && has && (m == "call" || m == "both"), fight = on && has && (m == "fight" || m == "both");
            int called = 0, back = 0;
            foreach (var r in kv.Value)
            {
                if (call && r.Active && r.Tether && !r.Guard && !r.Patrol && !r.BmCalled && r.ReviveAt <= NowSec())
                { r.BmCalled = true; r.Tether = false; DespawnLive(r.Id); nextSpawn.Remove(r.Id); called++; dirty = true; }
                else if (!call && r.BmCalled && (!on || has))
                { r.BmCalled = false; if (r.Active) { r.Tether = true; DespawnLive(r.Id); nextSpawn.Remove(r.Id); } back++; dirty = true; }
                if (fight && r.Active && !r.Tether && (r.Mode == 1 || r.Mode == 2) && r.BmPrevMode < 0)
                { r.BmPrevMode = r.Mode; r.Mode = 0; SyncLive(r); dirty = true; }
                else if (!fight && r.BmPrevMode >= 0 && (!on || has))
                { r.Mode = r.BmPrevMode; r.BmPrevMode = -1; SyncLive(r); dirty = true; }
            }
            if (called > 0 || back > 0) any = true;
            if (has && called > 0) Notify(owner.entityId, "Blood moon: " + called + " tethered thrall" + (called == 1 ? "" : "s") + " came to fight beside you.");
            if (has && back > 0) Notify(owner.entityId, (on ? "" : "Dawn: ") + back + " thrall" + (back == 1 ? "" : "s") + " went back to the daycare.");
        }
        if (changed || any) nextSlow = 0f;   // spawn the called ones (and the ones going back) right away
    }

    // ------------------------------------------------------------------ leaderboard
    static void TopCommand(int me, string arg)
    {
        bool kills = arg != null && arg.StartsWith("k");
        var all = new List<ThrallRecord>();
        foreach (var kv in roster) all.AddRange(kv.Value);
        if (all.Count == 0) { Tell(me, "Nobody has thralls yet."); return; }
        if (kills) all.Sort((a, b) => b.Kills != a.Kills ? b.Kills.CompareTo(a.Kills) : b.Level.CompareTo(a.Level));
        else all.Sort((a, b) => b.Level != a.Level ? b.Level.CompareTo(a.Level) : b.Kills.CompareTo(a.Kills));
        Tell(me, kills ? "Top thralls by kills:" : "Top thralls by level:   (/thrall top kills for kills)");
        for (int i = 0; i < all.Count && i < 10; i++)
            Tell(me, "  " + (i + 1) + ". " + Label(all[i]) + ", " + all[i].Kills + " kills  (" + NameOfKey(all[i].OwnerKey) + ")");
    }

    // ------------------------------------------------------------------ scouting
    class Scout { public int Owner; public string RecId; public Vector3 Goal; public float Until; }
    static readonly Dictionary<int, Scout> scouts = new Dictionary<int, Scout>();
    static readonly Dictionary<int, float> lastScout = new Dictionary<int, float>();

    public static bool ScoutGoal(int entityId, out Vector3 goal)
    {
        Scout s;
        if (scouts.TryGetValue(entityId, out s)) { goal = s.Goal; return true; }
        goal = Vector3.zero; return false;
    }

    static void ScoutCommand(int me, EntityPlayer owner, string arg, List<ThrallRecord> list)
    {
        var t = Pick(arg, list);
        if (t.Count != 1) { Tell(me, "Usage: /thrall scout <number|name>   (it runs ahead the way you look and reports what is there)"); return; }
        var r = t[0];
        int lid;
        var e = liveByRecord.TryGetValue(r.Id, out lid) && W != null ? W.GetEntity(lid) as EntityAlive : null;
        if (e == null || e.IsDead() || r.Tether || r.Stay) { Tell(me, Label(r) + " has to be out and following you to scout."); return; }
        float last, cd = BaitConfig.ScoutCooldownSeconds;
        if (cd > 0f && lastScout.TryGetValue(me, out last) && Time.time - last < cd) { Tell(me, "Scouting is ready again in " + Mathf.CeilToInt(cd - (Time.time - last)) + "s."); return; }
        Vector3 fwd = owner.GetLookVector(); fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Quaternion.Euler(0f, owner.rotation.y, 0f) * Vector3.forward;
        fwd.Normalize();
        Vector3 goal = owner.position + fwd * BaitConfig.ScoutDistance;
        scouts[e.entityId] = new Scout { Owner = me, RecId = r.Id, Goal = goal, Until = Time.time + 20f };
        lastScout[me] = Time.time;
        e.SetAttackTarget(null, 0);
        Tell(me, Label(r) + " runs ahead " + Mathf.RoundToInt(BaitConfig.ScoutDistance) + "m " + Compass(owner.position, goal) + " to look around.");
    }

    // Once a second: a scout that got there (or ran out of time) reports and goes back to following.
    static void ScoutTick(World world, float now)
    {
        if (scouts.Count == 0) return;
        foreach (var kv in new List<KeyValuePair<int, Scout>>(scouts))
        {
            var s = kv.Value;
            var e = world.GetEntity(kv.Key) as EntityAlive;
            if (e == null || e.IsDead() || !Live.ContainsKey(kv.Key)) { scouts.Remove(kv.Key); continue; }
            if (Flat(e.position - s.Goal) > 4f && now < s.Until) continue;
            scouts.Remove(kv.Key);
            float range = BaitConfig.ScoutRange;
            world.GetEntitiesInBounds(typeof(EntityEnemy), new Bounds(e.position, new Vector3(range * 2f, 40f, range * 2f)), guardTmp);
            int n = 0, sleeping = 0; EntityAlive near = null; float nd = float.MaxValue;
            foreach (var o in guardTmp)
            {
                var z = o as EntityAlive;
                if (z == null || z.IsDead() || Live.ContainsKey(z.entityId)) continue;
                float d = (z.position - e.position).magnitude;
                if (d > range) continue;
                n++; if (z.IsSleeping) sleeping++;
                if (d < nd) { nd = d; near = z; }
            }
            guardTmp.Clear();
            var owner = world.GetEntity(s.Owner) as EntityPlayer;
            var rec = Find(s.RecId);
            string who = rec != null ? Label(rec) : "Your scout";
            if (owner == null) continue;
            if (n == 0) Notify(s.Owner, who + " scouted " + Mathf.RoundToInt(range) + "m around " + Mathf.RoundToInt(Flat(e.position - owner.position)) + "m " + Compass(owner.position, e.position) + ": all clear.");
            else Notify(s.Owner, who + " found " + n + " hostile" + (n == 1 ? "" : "s") + (sleeping > 0 ? " (" + sleeping + " asleep)" : "") + ". Closest: " + Pretty(ClassName(near))
                + (LevelOf(near.entityId) > 0 ? " Lv" + LevelOf(near.entityId) : "") + ", " + Mathf.RoundToInt((near.position - owner.position).magnitude) + "m " + Compass(owner.position, near.position) + " of you.");
        }
    }

    // ------------------------------------------------------------------ evolution
    static bool ClassKnown(string cls)
    {
        try { return EntityClass.list.ContainsKey(EntityClass.FromString(cls)); } catch { return false; }
    }

    // The next stronger version of a zombie class (walker -> feral -> radiated -> charged), or null.
    static string EvolveTarget(ThrallRecord r)
    {
        if (r == null || IsAnimalClass(r.ClassName) || string.IsNullOrEmpty(r.ClassName)) return null;
        string c = r.ClassName, b = c;
        string[] tiers = { "Feral", "Radiated", "Charged" };
        int cur = -1;
        foreach (string s in new[] { "Infernal", "Charged", "Radiated", "Feral" })
            if (c.EndsWith(s, StringComparison.OrdinalIgnoreCase)) { b = c.Substring(0, c.Length - s.Length); cur = Array.IndexOf(tiers, s); if (s == "Infernal") cur = 3; break; }
        for (int i = cur + 1; i < tiers.Length; i++) if (ClassKnown(b + tiers[i])) return b + tiers[i];
        return null;
    }

    static void EvolveCommand(int me, EntityPlayer owner, string arg, List<ThrallRecord> list)
    {
        var t = Pick(arg, list);
        if (t.Count != 1) { Tell(me, "Usage: /thrall evolve <number|name>   (level " + BaitConfig.EvolveLevel + "+ zombies, costs " + BaitConfig.EvolveFeastCost + " Feast Bait)"); return; }
        var r = t[0];
        if (BaitConfig.EvolveLevel <= 0) { Tell(me, "Evolution is turned off on this server."); return; }
        string to = EvolveTarget(r);
        if (to == null) { Tell(me, IsAnimalClass(r.ClassName) ? "Animals don't evolve." : Label(r) + " has no stronger version."); return; }
        if (r.Level < BaitConfig.EvolveLevel) { Tell(me, Label(r) + " can evolve into " + Pretty(to) + " at level " + BaitConfig.EvolveLevel + "."); return; }
        if (r.ReviveAt > NowSec()) { Tell(me, Label(r) + " is downed."); return; }
        int cost = Math.Max(0, BaitConfig.EvolveFeastCost), b, g;
        if (cost > 0 && ItemCount(owner, BaitCheck.FeastItem, out b, out g) < cost) { Tell(me, "Evolving costs " + cost + " Feast Bait. The server sees " + ItemCount(owner, BaitCheck.FeastItem, out b, out g) + "."); return; }
        if (cost > 0 && !TakeItems(owner, BaitCheck.FeastItem, cost)) { Tell(me, "Could not take the Feast Bait. Try again."); return; }
        string from = r.ClassName;
        r.ClassName = to; dirty = true;
        DespawnLive(r.Id); nextSpawn.Remove(r.Id); nextSlow = 0f;
        Log.Out("[Bait] " + owner.EntityName + " evolved " + from + " into " + to + " (record " + r.Id + ")");
        Tell(me, Pretty(from) + " evolved into " + Label(r) + ".");
        Webhook("✨ **" + Esc(owner.EntityName) + "**'s " + Esc(Pretty(from)) + " evolved into **" + Esc(Label(r)) + "**.");
    }

    // ------------------------------------------------------------------ admin tools: heal, revive, tp
    static bool AdminCare(int me, string sub, string[] a)
    {
        if (sub != "heal" && sub != "revive" && sub != "tp") return false;
        string tkey = a.Length > 3 ? KeyByName(a[3]) : null;
        if (tkey == null) { Tell(me, "Usage: /thrall admin heal|revive|tp <player> <n|all>"); return true; }
        var tl = RosterOf(tkey);
        var t = Pick(a.Length > 4 ? a[4].ToLowerInvariant() : "all", tl);
        if (t.Count == 0) { Tell(me, NameOfKey(tkey) + " has no such thrall."); return true; }
        EntityPlayer target = null;
        foreach (var p in W.GetPlayers()) if (p != null && KeyOf(p) == tkey) target = p;
        int n = 0;
        foreach (var r in t)
        {
            int lid;
            var e = liveByRecord.TryGetValue(r.Id, out lid) ? W.GetEntity(lid) as EntityAlive : null;
            if (sub == "revive") { if (r.ReviveAt > NowSec()) { r.ReviveAt = 0; nextSpawn.Remove(r.Id); n++; } }
            else if (sub == "heal") { if (e != null && !e.IsDead()) { e.Stats.Health.Value = e.GetMaxHealth(); n++; } }
            else
            {
                if (target == null) { Tell(me, NameOfKey(tkey) + " is not online."); return true; }
                if (!r.Active || r.Tether) continue;
                if (e != null && !e.IsDead()) { e.SetPosition(BehindOwner(target)); e.SetAttackTarget(null, 0); }
                else nextSpawn.Remove(r.Id);
                n++;
            }
        }
        dirty = true; nextSlow = 0f;
        Tell(me, (sub == "revive" ? "Revived " : sub == "heal" ? "Healed " : "Brought ") + n + " of " + NameOfKey(tkey) + "'s thralls" + (sub == "tp" ? " to them." : "."));
        Log.Out("[Bait] admin #" + me + " " + sub + " " + n + " thralls of " + tkey);
        if (target != null && n > 0) Notify(target.entityId, "An admin " + (sub == "revive" ? "revived" : sub == "heal" ? "healed" : "brought back") + " " + n + " of your thralls.");
        return true;
    }

    // ------------------------------------------------------------------ Discord webhook
    // The URL comes from webhook.txt in this mod's folder (one line, the URL), or else from Blood Moon Sound's discord.txt.
    static string webhookUrl;
    static bool webhookRead;
    static readonly List<UnityWebRequest> webSends = new List<UnityWebRequest>();

    static string WebhookUrl()
    {
        if (webhookRead) return webhookUrl;
        webhookRead = true;
        try
        {
            string mod = BaitConfig.ModPath;
            if (string.IsNullOrEmpty(mod)) return null;
            string own = Path.Combine(mod, "webhook.txt");
            if (File.Exists(own)) webhookUrl = UrlFrom(File.ReadAllLines(own));
            if (webhookUrl == null)
            {
                foreach (string dir in Directory.GetDirectories(Path.GetDirectoryName(mod.TrimEnd('/', '\\'))))
                {
                    string f = Path.Combine(dir, "discord.txt");
                    if (File.Exists(f)) { webhookUrl = UrlFrom(File.ReadAllLines(f)); if (webhookUrl != null) break; }
                }
            }
            if (webhookUrl != null) Log.Out("[Bait] Discord webhook found: rare tames, level " + BaitConfig.WebhookLevelStep + "s and evolutions are posted.");
        }
        catch (Exception e) { Log.Warning("[Bait] webhook: " + e.Message); }
        return webhookUrl;
    }

    static string UrlFrom(string[] lines)
    {
        foreach (string raw in lines)
        {
            string l = raw.Trim();
            if (l.Length == 0 || l[0] == '#') continue;
            if (l.StartsWith("webhook=", StringComparison.OrdinalIgnoreCase)) l = l.Substring(8).Trim();
            if (l.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && l.IndexOf("/api/webhooks/", StringComparison.OrdinalIgnoreCase) > 0) return l;
        }
        return null;
    }

    static string Esc(string s) { return (s ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("@", "@​"); }

    static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c < 32) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static void Webhook(string text)
    {
        if (!BaitConfig.WebhookEvents) return;
        string url = WebhookUrl();
        if (url == null) return;
        try
        {
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"username\":\"Bait\",\"content\":" + Json(text) + ",\"allowed_mentions\":{\"parse\":[]}}"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 15;
            req.SendWebRequest();   // async; finished ones are cleaned up by WebTick
            webSends.Add(req);
        }
        catch (Exception e) { Log.Warning("[Bait] webhook post: " + e.Message); }
    }

    static void WebTick()
    {
        for (int i = webSends.Count - 1; i >= 0; i--)
        {
            var r = webSends[i];
            if (!r.isDone) continue;
            if (r.responseCode < 200 || r.responseCode >= 300) Log.Warning("[Bait] webhook post failed: " + (r.responseCode > 0 ? "HTTP " + r.responseCode : r.error));
            r.Dispose();
            webSends.RemoveAt(i);
        }
    }

    static bool IsRare(string cls)
    {
        string c = (cls ?? "").ToLowerInvariant();
        return TierOf(cls) >= 2 || c.Contains("boss") || c.Contains("demolition") || c.Contains("behemoth");
    }

    static void WebhookTamed(EntityPlayer owner, ThrallRecord r)
    {
        if (IsRare(r.ClassName)) Webhook("\U0001F9DF **" + Esc(owner.EntityName) + "** tamed a **" + Esc(Pretty(r.ClassName)) + "**" + (r.Level > 0 ? " (level " + r.Level + ")" : "") + "!");
    }

    static void WebhookLevel(ThrallRecord r)
    {
        int step = BaitConfig.WebhookLevelStep;
        if (step > 0 && r.Level > 0 && r.Level % step == 0)
            Webhook("⭐ **" + Esc(Label(r)) + "** reached level " + r.Level + " for **" + Esc(NameOfKey(r.OwnerKey)) + "** (" + r.Kills + " kills).");
    }

    // ------------------------------------------------------------------ taming helper (Bait UI)
    // While a Bait UI player holds bait, the creature under their crosshair is sent as L|<id>|<fed>|<needed>|<status>|<level>,
    // status ok, mine (already a thrall), excluded, full (roster full) or other (someone else is taming it). L|0 clears it.
    static readonly Dictionary<int, string> uiLook = new Dictionary<int, string>();

    static void TamingHelperTick(World world)
    {
        if (uiClients.Count == 0) return;
        foreach (int id in new List<int>(uiClients))
        {
            var p = world.GetEntity(id) as EntityPlayer;
            if (p == null || p.IsDead()) continue;
            string rec = "L|0";
            string held = null;
            try { held = p.inventory != null && p.inventory.holdingItem != null ? p.inventory.holdingItem.GetItemName() : null; } catch { }
            if (BaitConfig.TamingHelper && held != null && BaitCheck.IsBait(held))
            {
                var e = LookedAt(world, p, 30f, 10f);
                if (e != null)
                {
                    string key = KeyOf(p);
                    string cls = ClassName(e);
                    Feed f;
                    int cur = feeds.TryGetValue(e.entityId, out f) && f.Owner == id ? f.Count : 0;
                    string st = Live.ContainsKey(e.entityId) ? "mine"
                              : BaitConfig.Exclude.Contains(cls) ? "excluded"
                              : f != null && f.Owner != id && f.Count > 0 ? "other"
                              : key != null && RosterOf(key).Count >= MaxOwnedFor(key) ? "full" : "ok";
                    rec = "L|" + e.entityId + "|" + cur + "|" + FeedsNeeded(cls, LevelOf(e.entityId)) + "|" + st + "|" + LevelOf(e.entityId);
                }
            }
            string old;
            if (uiLook.TryGetValue(id, out old) && old == rec) continue;
            uiLook[id] = rec;
            UiSend(id, new List<string> { rec });
        }
    }

    static readonly List<Entity> helperTmp = new List<Entity>();
    static EntityAlive LookedAt(World world, EntityPlayer p, float range, float maxAngle)
    {
        Vector3 head = p.getHeadPosition();
        Vector3 fwd = p.GetLookVector();
        if (fwd.sqrMagnitude < 0.01f) fwd = Quaternion.Euler(p.rotation.x, p.rotation.y, 0f) * Vector3.forward;
        world.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(head, new Vector3(range * 2f, range * 2f, range * 2f)), helperTmp);
        EntityAlive best = null; float bestAngle = maxAngle;
        foreach (var o in helperTmp)
        {
            var e = o as EntityAlive;
            if (e == null || e is EntityPlayer || e.IsDead() || !(e is EntityEnemy || e is EntityAnimal)) continue;
            Vector3 to = e.position + Vector3.up * (e.height * 0.5f) - head;
            if (to.magnitude > range || to.magnitude < 0.5f) continue;
            float ang = Vector3.Angle(fwd, to);
            if (ang < bestAngle) { bestAngle = ang; best = e; }
        }
        helperTmp.Clear();
        return best;
    }
}
