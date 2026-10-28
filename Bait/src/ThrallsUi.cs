using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Data feed for the optional BaitUI client mod. Only players whose client said "/thrall hello" get anything,
// so everyone else never sees these messages. Data goes out as whispers that start with a hidden marker; the client
// mod swallows them before they reach the chat window. It is pushed when it changes (nothing is polled).
public static partial class Thralls
{
    const string UiTag = "\u200B\u200BBAIT|";
    public const string ModVersion = "1.10.0";   // keep in step with mod.json; sent to Bait UI so it can warn about a server that is too old
    static readonly HashSet<int> uiClients = new HashSet<int>();
    static readonly Dictionary<int, string> uiRoster = new Dictionary<int, string>();
    static readonly Dictionary<int, string> uiCfg = new Dictionary<int, string>();
    static readonly Dictionary<int, string> uiAll = new Dictionary<int, string>();

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace('|', ' ').Replace('~', ' ').Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
    }

    static void UiSend(int to, List<string> recs)
    {
        var sb = new StringBuilder();
        for (int i = 0; i <= recs.Count; i++)
        {
            bool last = i == recs.Count;
            if (!last && sb.Length > 0 && sb.Length + recs[i].Length > 600) last = true;
            if (last)
            {
                if (sb.Length > 0)
                {
                    try
                    {
                        GameManager.Instance.ChatMessageServer(null, EChatType.Whisper, -1, UiTag + sb, new List<int> { to }, EMessageSender.Server);
                    }
                    catch (Exception e) { Dbg("ui send: " + e.Message); }
                    sb.Length = 0;
                }
                if (i == recs.Count) break;
            }
            if (sb.Length > 0) sb.Append('~');
            sb.Append(recs[i]);
        }
    }

    static List<string> UiRosterRecords(string key, int me)
    {
        var l = new List<string>();
        var list = RosterOf(key);
        int bb, bg;
        var pl = W != null ? W.GetEntity(me) as EntityPlayer : null;
        int plain = pl != null ? ItemCount(pl, BaitCheck.BaitItem, out bb, out bg) : 0;
        int rich = pl != null ? ItemCount(pl, BaitCheck.RichItem, out bb, out bg) : 0;
        int feast = pl != null ? ItemCount(pl, BaitCheck.FeastItem, out bb, out bg) : 0;
        l.Add("S|" + list.Count + "|" + MaxOwnedFor(key) + "|" + MaxActiveFor(key) + "|" + ActiveCount(key) + "|" + (IsAdmin(me) ? 1 : 0) + "|" + PowerOf(key) + "|" + ModVersion
              + "|" + plain + "|" + rich + "|" + feast + "|" + BmOf(key));
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            int eid;
            if (!liveByRecord.TryGetValue(r.Id, out eid)) eid = -1;
            long down = r.ReviveAt - NowSec();
            l.Add("T|" + (i + 1) + "|" + eid + "|" + Clean(r.ClassName) + "|" + (r.Active ? 1 : 0) + "|" + r.Level + "|" + r.Kills + "|" + (r.Tether ? 1 : 0) + "|" + F(r.TR) +
                  "|" + r.Mode + "|" + (r.Stay ? 1 : 0) + "|" + (down > 0 ? down : 0) + "|" + Clean(r.Name) + "|" + (r.AutoRevive ? 1 : 0) + "|" + (r.AutoHeal ? 1 : 0)
                  // 1.10.0: perks, guard, patrol, patrol points, deaths, tamed at (unix s), perk slots open, evolves into, tether x y z
                  + "|" + Clean(r.Perks) + "|" + (r.Guard ? 1 : 0) + "|" + (r.Patrol ? 1 : 0) + "|" + PatrolPoints(r).Count + "|" + r.Deaths + "|" + r.TamedAt
                  + "|" + PerkSlots(r.Level) + "|" + Clean(EvolveTarget(r))
                  // where its tether, guard post or patrol is (for Bait UI's map markers); blank when it follows
                  + "|" + (r.Tether ? F(r.TX) + "|" + F(r.TY) + "|" + F(r.TZ) : "||"));
        }
        // teams: TM|<count>|name:size,name:size (older Bait UI ignores it)
        var tl = new List<string>();
        foreach (var tm in TeamsOf(key)) tl.Add(Clean(tm.Name).Replace(':', ' ').Replace(',', ' ') + ":" + list.FindAll(r => tm.Ids.Contains(r.Id)).Count);
        l.Add("TM|" + tl.Count + "|" + string.Join(",", tl.ToArray()));
        return l;
    }

    static List<string> UiConfigRecords()
    {
        var l = new List<string>();
        var cfg = BaitConfig.Describe();
        l.Add("CS|" + cfg.Count);
        l.AddRange(cfg);
        l.Add("XS|" + BaitConfig.Exclude.Count);
        foreach (string x in BaitConfig.Exclude) l.Add("X|" + Clean(x));
        return l;
    }

    static void UiPushTo(int id, EntityPlayer p, bool force)
    {
        string key = KeyOf(p);
        if (key == null) return;
        var roster = UiRosterRecords(key, id);
        string sig = string.Join("~", roster.ToArray()), old;
        if (force || !uiRoster.TryGetValue(id, out old) || old != sig) { uiRoster[id] = sig; UiSend(id, roster); }
        var cfg = UiConfigRecords();
        sig = string.Join("~", cfg.ToArray());
        if (force || !uiCfg.TryGetValue(id, out old) || old != sig) { uiCfg[id] = sig; UiSend(id, cfg); }

        // every thrall entity id on the server (any owner), so the client can keep turrets from aiming at them
        var ids = new List<int>(Live.Keys);
        ids.Sort();
        var sbIds = new StringBuilder("A|");
        for (int i = 0; i < ids.Count; i++) { if (i > 0) sbIds.Append(','); sbIds.Append(ids[i]); }
        // who owns each one, so a party can see its members' thralls (separate records, in chunks, so older clients just skip them)
        var recs = new List<string> { sbIds.ToString() };
        for (int i = 0; i < ids.Count; i += 40)
        {
            var sbOwn = new StringBuilder("O|" + (i == 0 ? "1" : "0") + "|");
            for (int j = i; j < ids.Count && j < i + 40; j++) { if (j > i) sbOwn.Append(','); sbOwn.Append(ids[j]).Append(':').Append(Live[ids[j]].OwnerEntityId); }
            recs.Add(sbOwn.ToString());
        }
        if (ids.Count == 0) recs.Add("O|1|");
        sig = string.Join("~", recs.ToArray());
        if (force || !uiAll.TryGetValue(id, out old) || old != sig) { uiAll[id] = sig; UiSend(id, recs); }
    }

    // Called once a second.
    static void UiPush()
    {
        if (uiClients.Count == 0 || W == null) return;
        foreach (int id in new List<int>(uiClients))
        {
            var p = W.GetEntity(id) as EntityPlayer;
            if (p == null) { uiClients.Remove(id); uiRoster.Remove(id); uiCfg.Remove(id); uiAll.Remove(id); continue; }
            try { UiPushTo(id, p, false); } catch (Exception e) { Dbg("ui push: " + e.Message); }
        }
    }

    // kill feed for Bait UI: K|<roster number>|<victim>|<kills>|<1 = shared kill>
    static void UiKill(int ownerId, ThrallRecord rec, EntityAlive victim, bool shared)
    {
        if (!uiClients.Contains(ownerId) || victim == null) return;
        int n = RosterOf(rec.OwnerKey).IndexOf(rec) + 1;
        string v = Pretty(ClassName(victim));
        int lvl = LevelOf(victim.entityId);
        UiSend(ownerId, new List<string> { "K|" + n + "|" + Clean(v + (lvl > 0 ? " Lv" + lvl : "")) + "|" + rec.Kills + "|" + (shared ? 1 : 0) });
    }

    static void UiFeed(int ownerId, int entityId, int cur, int need)
    {
        if (!uiClients.Contains(ownerId)) return;
        UiSend(ownerId, new List<string> { "F|" + entityId + "|" + cur + "|" + need });
    }

    // /thrall hello (the client mod announces itself) and /thrall ui (send everything again). Quiet: not logged.
    static bool UiCommand(int me, string verb)
    {
        var p = W != null ? W.GetEntity(me) as EntityPlayer : null;
        if (p == null) return true;
        uiClients.Add(me);
        UiPushTo(me, p, true);
        return true;
    }
}