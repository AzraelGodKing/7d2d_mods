using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Quality of life (1.9.0): thralls by name, /thrall here, teams, auto-heal, the low bait warning, /thrall where,
// reordering the roster and the death log. All server side; Bait UI only adds buttons for them.
public static partial class Thralls
{
    // ------------------------------------------------------------------ picking a thrall by name
    // Anywhere a number is accepted, a nickname works too: exact (any case) first, then the one name that starts with it.
    static int IndexByName(string s, List<ThrallRecord> list)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        for (int i = 0; i < list.Count; i++)
            if (!string.IsNullOrEmpty(list[i].Name) && string.Equals(list[i].Name, s, StringComparison.OrdinalIgnoreCase)) return i;
        int found = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (string.IsNullOrEmpty(list[i].Name) || !list[i].Name.StartsWith(s, StringComparison.OrdinalIgnoreCase)) continue;
            if (found >= 0) return -1;   // two names start with it: ambiguous
            found = i;
        }
        return found;
    }

    // ------------------------------------------------------------------ /thrall here
    static readonly Dictionary<int, float> lastHere = new Dictionary<int, float>();

    static void HereCommand(int me, EntityPlayer owner, string arg, List<ThrallRecord> list)
    {
        var t = arg == null ? new List<ThrallRecord>(list) : Pick(arg, list);
        if (t.Count == 0) { Tell(me, "Usage: /thrall here [number|name|all]   (brings stuck thralls to you)"); return; }
        if (owner.AttachedToEntity != null) { Tell(me, "Get out of the vehicle first."); return; }
        float last;
        float cd = BaitConfig.HereCooldownSeconds;
        if (cd > 0f && lastHere.TryGetValue(me, out last) && Time.time - last < cd)
        { Tell(me, "/thrall here is ready again in " + Mathf.CeilToInt(cd - (Time.time - last)) + "s."); return; }
        int moved = 0, skipped = 0;
        foreach (var r in t)
        {
            if (!r.Active || r.Tether || r.ReviveAt > NowSec()) { if (arg != null && arg != "all") skipped++; continue; }
            int lid;
            var e = liveByRecord.TryGetValue(r.Id, out lid) && W != null ? W.GetEntity(lid) as EntityAlive : null;
            if (e != null && !e.IsDead())
            {
                e.SetPosition(BehindOwner(owner));
                e.SetAttackTarget(null, 0);
            }
            else { nextSpawn.Remove(r.Id); nextSlow = 0f; }
            moved++;
        }
        if (moved == 0) { Tell(me, skipped > 0 ? "That one is not out with you (inactive, tethered or downed)." : "None of your thralls are out with you."); return; }
        lastHere[me] = Time.time;
        Tell(me, moved == 1 ? "Brought 1 thrall to you." : "Brought " + moved + " thralls to you.");
    }

    // ------------------------------------------------------------------ teams
    // A team is a named list of thralls (by record id) that one command makes the active set.
    // Kept in AzraelBaitTeams.tsv in the save folder: owner key, team name, record ids.
    class Team { public string Name; public List<string> Ids = new List<string>(); }
    static readonly Dictionary<string, List<Team>> teams = new Dictionary<string, List<Team>>();
    static bool teamsDirty;
    const string TeamsFile = "AzraelBaitTeams.tsv";
    static string TeamsPath { get { return Path.Combine(GameIO.GetSaveGameDir(), TeamsFile); } }

    static List<Team> TeamsOf(string key)
    {
        List<Team> l;
        if (!teams.TryGetValue(key, out l)) { l = new List<Team>(); teams[key] = l; }
        return l;
    }

    static void LoadTeams()
    {
        teams.Clear(); teamsDirty = false;
        try
        {
            if (!File.Exists(TeamsPath)) return;
            foreach (string line in File.ReadAllLines(TeamsPath))
            {
                string[] p = line.Split('\t');
                if (p.Length < 3 || p[0].Length == 0 || p[1].Length == 0) continue;
                var tm = new Team { Name = p[1] };
                foreach (string id in p[2].Split(',')) if (id.Length > 0) tm.Ids.Add(id);
                TeamsOf(p[0]).Add(tm);
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read teams: " + e.Message); }
    }

    static void FlushTeams()
    {
        if (!teamsDirty || !loaded) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in teams)
                foreach (var tm in kv.Value)
                    sb.Append(kv.Key).Append('\t').Append(tm.Name).Append('\t').Append(string.Join(",", tm.Ids.ToArray())).Append('\n');
            WriteSafe(TeamsPath, sb.ToString());
            try { File.Copy(TeamsPath, Path.Combine(KeepDir, TeamsFile), true); } catch { }
            teamsDirty = false;
        }
        catch (Exception e) { Log.Warning("[Bait] could not save teams: " + e.Message); }
    }

    static Team FindTeam(string key, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var tm in TeamsOf(key)) if (string.Equals(tm.Name, name, StringComparison.OrdinalIgnoreCase)) return tm;
        return null;
    }

    static string TeamText(Team tm, List<ThrallRecord> list)
    {
        var names = new List<string>();
        foreach (string id in tm.Ids) { int i = list.FindIndex(r => r.Id == id); if (i >= 0) names.Add((i + 1) + ". " + Label(list[i])); }
        return tm.Name + " (" + names.Count + "): " + (names.Count == 0 ? "empty" : string.Join(", ", names.ToArray()));
    }

    static void TeamCommand(int me, string key, string[] a, List<ThrallRecord> list)
    {
        string usage = "Usage: /thrall team list | save <team> <numbers|names|active> | call <team> | delete <team>";
        string sub = a.Length > 2 ? a[2].ToLowerInvariant() : "list";
        string name = a.Length > 3 ? a[3].Replace('\t', ' ').Replace(',', ' ').Replace('|', ' ').Replace('~', ' ').Trim() : null;
        if (name != null && name.Length > 16) name = name.Substring(0, 16);
        var mine = TeamsOf(key);
        switch (sub)
        {
            case "list":
                if (mine.Count == 0) { Tell(me, "You have no teams. /thrall team save <team> active   saves the ones out with you now."); return; }
                foreach (var tm in mine) Tell(me, TeamText(tm, list));
                return;
            case "save":
            {
                if (string.IsNullOrEmpty(name) || a.Length < 5) { Tell(me, usage); return; }
                var pick = new List<ThrallRecord>();
                for (int i = 4; i < a.Length; i++)
                {
                    string w = a[i].ToLowerInvariant();
                    if (w == "active") { foreach (var r in list) if (r.Active && !r.Tether && !pick.Contains(r)) pick.Add(r); continue; }
                    foreach (var r in Pick(w, list)) if (!pick.Contains(r)) pick.Add(r);
                }
                if (pick.Count == 0) { Tell(me, "No thralls matched. " + usage); return; }
                var tm = FindTeam(key, name);
                if (tm == null)
                {
                    if (mine.Count >= BaitConfig.MaxTeams) { Tell(me, "You already have " + mine.Count + " teams (limit " + BaitConfig.MaxTeams + "). /thrall team delete <team> first."); return; }
                    tm = new Team { Name = name }; mine.Add(tm);
                }
                tm.Ids.Clear();
                foreach (var r in pick) tm.Ids.Add(r.Id);
                teamsDirty = true;
                Tell(me, "Saved team " + TeamText(tm, list));
                return;
            }
            case "call":
            {
                var tm = FindTeam(key, name);
                if (tm == null) { Tell(me, name == null ? usage : "No team called " + name + ". /thrall team list"); return; }
                var want = list.FindAll(r => tm.Ids.Contains(r.Id));
                if (want.Count == 0) { Tell(me, "Team " + tm.Name + " has no thralls left."); return; }
                // everything following you that is not in the team goes inactive; tethered ones stay where they are
                foreach (var r in list)
                    if (r.Active && !r.Tether && !tm.Ids.Contains(r.Id)) { r.Active = false; DespawnLive(r.Id); }
                int called = 0, over = 0;
                foreach (var r in want)
                {
                    if (r.Active) { called++; continue; }
                    if (ActiveCount(key) >= MaxActiveFor(key)) { over++; continue; }
                    r.Active = true; nextSpawn.Remove(r.Id); called++;
                }
                dirty = true; nextSlow = 0f;
                Tell(me, "Team " + tm.Name + " is out: " + called + " thralls" + (over > 0 ? " (" + over + " left inactive: you may have " + MaxActiveFor(key) + " active)" : "") + ".");
                return;
            }
            case "delete":
            {
                var tm = FindTeam(key, name);
                if (tm == null) { Tell(me, name == null ? usage : "No team called " + name + "."); return; }
                mine.Remove(tm); teamsDirty = true;
                Tell(me, "Team " + tm.Name + " deleted (the thralls are untouched).");
                return;
            }
        }
        Tell(me, usage);
    }

    // ------------------------------------------------------------------ auto-heal
    // A thrall with auto-heal on that drops below AutoHealBelowPct is healed by HealPct for HealBaitCost plain bait from
    // its owner's inventory, at most once every AutoHealCooldownSeconds. Checked every 5 s.
    static readonly Dictionary<string, float> lastAutoHeal = new Dictionary<string, float>();

    static void AutoHealTick(World world, Dictionary<string, EntityPlayer> online, float now)
    {
        if (!BaitConfig.AutoHeal) return;
        foreach (var lt in new List<LiveThrall>(Live.Values))
        {
            EntityPlayer owner;
            if (!online.TryGetValue(lt.OwnerKey, out owner)) continue;
            var rec = Find(lt.RecordId);
            if (rec == null || !rec.AutoHeal) continue;
            var e = world.GetEntity(lt.EntityId) as EntityAlive;
            if (e == null || e.IsDead() || HpFrac(e) * 100f >= BaitConfig.AutoHealBelowPct) continue;
            float last;
            if (lastAutoHeal.TryGetValue(rec.Id, out last) && now - last < BaitConfig.AutoHealCooldownSeconds) continue;
            int cost = Math.Max(1, BaitConfig.HealBaitCost), b, g;
            if (ItemCount(owner, BaitCheck.BaitItem, out b, out g) < cost || !TakeItems(owner, BaitCheck.BaitItem, cost)) continue;
            lastAutoHeal[rec.Id] = now;
            float max = e.GetMaxHealth();
            e.Stats.Health.Value = Mathf.Min(max, e.Stats.Health.Value + max * BaitConfig.HealPct / 100f);
            NotifyLow(owner.entityId, "Auto-heal used " + cost + " bait on " + Label(rec) + ".");
        }
        if (lastAutoHeal.Count > 500) lastAutoHeal.Clear();
    }

    // ------------------------------------------------------------------ low bait warning
    // Players with auto-revive or auto-heal on get one toast when their plain bait drops below what one use costs,
    // and another only after they have had enough again.
    static readonly HashSet<string> warnedLowBait = new HashSet<string>();

    static void LowBaitTick(Dictionary<string, EntityPlayer> online)
    {
        if (!BaitConfig.LowBaitWarning) return;
        foreach (var kv in online)
        {
            int need = 0;
            foreach (var r in RosterOf(kv.Key))
            {
                if (r.AutoRevive && BaitConfig.AutoRevive) need = Math.Max(need, Math.Max(1, BaitConfig.AutoReviveCost));
                if (r.AutoHeal && BaitConfig.AutoHeal) need = Math.Max(need, Math.Max(1, BaitConfig.HealBaitCost));
            }
            if (need == 0) { warnedLowBait.Remove(kv.Key); continue; }
            int b, g, have = ItemCount(kv.Value, BaitCheck.BaitItem, out b, out g);
            if (have >= need) { warnedLowBait.Remove(kv.Key); continue; }
            if (!warnedLowBait.Add(kv.Key)) continue;
            Notify(kv.Value.entityId, "Low on bait: you carry " + have + " Zombie and Animal Bait, auto-revive/auto-heal needs " + need + ".");
        }
    }

    // ------------------------------------------------------------------ /thrall where
    static string Compass(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;   // 0 = north (+z), 90 = east (+x)
        if (ang < 0f) ang += 360f;
        string[] dirs = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        return dirs[Mathf.RoundToInt(ang / 45f) % 8];
    }

    static void WhereCommand(int me, EntityPlayer owner, string arg, List<ThrallRecord> list)
    {
        var t = arg == null || arg == "all" ? list.FindAll(r => r.Active) : Pick(arg, list);
        if (t.Count == 0) { Tell(me, arg == null ? "None of your thralls are out." : "Usage: /thrall where [number|name]"); return; }
        foreach (var r in t)
        {
            int i = list.IndexOf(r) + 1, lid;
            var e = liveByRecord.TryGetValue(r.Id, out lid) && W != null ? W.GetEntity(lid) as EntityAlive : null;
            string at;
            if (r.ReviveAt > NowSec()) at = "downed";
            else if (e != null && !e.IsDead()) at = Mathf.RoundToInt((e.position - owner.position).magnitude) + "m " + Compass(owner.position, e.position);
            else if (r.Tether) { var tp = new Vector3(r.TX, r.TY, r.TZ); at = "tethered " + Mathf.RoundToInt((tp - owner.position).magnitude) + "m " + Compass(owner.position, tp) + " (area not loaded)"; }
            else at = r.Active ? "on its way" : "stowed";
            if (e != null && r.Tether) at += ", tether at " + Mathf.RoundToInt(r.TX) + " " + Mathf.RoundToInt(r.TZ);
            Tell(me, "  " + i + ". " + Label(r) + ": " + at);
        }
    }

    // ------------------------------------------------------------------ reordering
    static void MoveCommand(int me, string arg, string arg2, List<ThrallRecord> list)
    {
        var t = Pick(arg, list);
        int to;
        if (t.Count != 1) { Tell(me, "Usage: /thrall move <number|name> <new number|top|bottom|up|down>"); return; }
        int from = list.IndexOf(t[0]);
        string w = arg2 != null ? arg2.ToLowerInvariant() : "top";
        if (w == "top" || w == "pin") to = 0;
        else if (w == "bottom") to = list.Count - 1;
        else if (w == "up") to = Math.Max(0, from - 1);
        else if (w == "down") to = Math.Min(list.Count - 1, from + 1);
        else if (int.TryParse(w, out to)) to = Mathf.Clamp(to - 1, 0, list.Count - 1);
        else { Tell(me, "Usage: /thrall move <number|name> <new number|top|bottom|up|down>"); return; }
        if (to == from) { Tell(me, Label(t[0]) + " is already number " + (from + 1) + "."); return; }
        list.RemoveAt(from);
        list.Insert(to, t[0]);
        dirty = true;
        Tell(me, Label(t[0]) + " is now number " + (to + 1) + ".");
    }

    // ------------------------------------------------------------------ death log
    // The last few deaths per owner, with what killed them (kept while the server runs).
    const int DeathLogSize = 10;
    static readonly Dictionary<string, List<string>> deathLog = new Dictionary<string, List<string>>();

    static void LogDeath(ThrallRecord rec, string why)
    {
        List<string> l;
        if (!deathLog.TryGetValue(rec.OwnerKey, out l)) { l = new List<string>(); deathLog[rec.OwnerKey] = l; }
        l.Insert(0, DateTime.UtcNow.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC  " + Label(rec) + ": " + why);
        if (l.Count > DeathLogSize) l.RemoveAt(l.Count - 1);
    }

    static void DeathsCommand(int me, string key)
    {
        List<string> l;
        if (!deathLog.TryGetValue(key, out l) || l.Count == 0) { Tell(me, "None of your thralls died since the server started."); return; }
        Tell(me, "Last " + l.Count + " deaths (newest first):");
        foreach (string s in l) Tell(me, "  " + s);
    }

    // ------------------------------------------------------------------ per-thrall toggles (autorevive, autoheal)
    static void ToggleCommand(int me, string verb, string arg, string arg2, List<ThrallRecord> list, bool heal)
    {
        var t = Pick(arg, list);
        string what = heal ? "Auto-heal" : "Auto-revive";
        string cost = heal ? Math.Max(1, BaitConfig.HealBaitCost) + " Zombie and Animal Bait when it drops below " + F(BaitConfig.AutoHealBelowPct) + "% HP" : AutoReviveCostText() + " each time it falls";
        if (t.Count == 0) { Tell(me, "Usage: /thrall " + verb + " <number|name|all> [on|off]   (" + cost + ", from your inventory)"); return; }
        if (heal ? !BaitConfig.AutoHeal : !BaitConfig.AutoRevive) { Tell(me, what + " is turned off on this server."); return; }
        string onoff = arg2 != null ? arg2.ToLowerInvariant() : null;
        bool? want = onoff == "on" || onoff == "1" || onoff == "true" ? true : onoff == "off" || onoff == "0" || onoff == "false" ? (bool?)false : null;
        if (want == null && arg == "all") want = !t.TrueForAll(r => heal ? r.AutoHeal : r.AutoRevive);   // all: on unless every one already has it
        var owner = W != null ? W.GetEntity(me) as EntityPlayer : null;
        foreach (var r in t)
        {
            bool v = want ?? !(heal ? r.AutoHeal : r.AutoRevive);
            if (heal) r.AutoHeal = v; else r.AutoRevive = v;
            if (!heal && v && r.ReviveAt > NowSec()) TryAutoRevive(r, owner, null);
        }
        dirty = true;
        bool on = t.Count == 1 ? (heal ? t[0].AutoHeal : t[0].AutoRevive) : want == true;
        Tell(me, what + " " + (on ? "on" : "off") + " for " + (t.Count == 1 ? Label(t[0]) : t.Count + " thralls") + (on ? " (" + cost + ")." : "."));
    }

    // ------------------------------------------------------------------ uneaten bait comes back
    // A thrown bait that stops calling (DistractionLifetime ran out) without anything having eaten it goes back to the
    // inventory of the player who threw it, instead of lying there until it despawns. Half-eaten bait counts as uneaten:
    // a meal only counts when a bait is used up.
    static readonly HashSet<int> returnedBait = new HashSet<int>();

    public static void CheckUneatenBait(EntityItem bait)
    {
        if (!BaitConfig.ReturnUneatenBait || !IsServer || !loaded || bait == null || bait.IsDead() || bait.distractionLifetime > 0) return;
        if (bait.itemClass == null || !BaitCheck.IsBait(bait.itemClass.GetItemName()) || bait.OwnerId < 0) return;
        if (returnedBait.Contains(bait.entityId)) return;
        if (bait.ticksExisted % 20 != 0) return;   // once a second is plenty
        var world = W;
        var p = world != null ? world.GetEntity(bait.OwnerId) as EntityPlayer : null;
        if (p == null || p.IsDead()) return;
        // something still eating it: leave it alone
        foreach (var en in world.Entities.list)
        {
            var a = en as EntityAlive;
            if (a != null && !a.IsDead() && (a.distraction == bait || a.pendingDistraction == bait)) return;
        }
        returnedBait.Add(bait.entityId);
        if (returnedBait.Count > 2000) returnedBait.Clear();
        string item = bait.itemClass.GetItemName();
        int n = Math.Max(1, bait.itemStack != null ? bait.itemStack.count : 1);
        bait.SetDead();
        if (GiveItems(p, item, n)) NotifyLow(p.entityId, "Nothing ate your " + Localization.Get(item) + ", so it came back to you.");
    }
}
