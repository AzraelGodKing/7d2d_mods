using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Bait items in a player's toolbelt and backpack are saved by item number, and the save matches numbers to names. If the
// server ever loads the save while this mod is missing or broken (a host update, a failed mod install, a lost
// itemmappings.nim), the game can't find the bait items and drops them from every inventory for good.
// So the server keeps its own count of each player's bait (AzraelBaitLedger.tsv, plus the kept copy outside the save).
// When a player comes back with none of a kind they logged out holding, that bait is given back. Only a loss across an
// offline gap counts, and only a kind that is down to zero, so nothing used, dropped or stored while playing is refunded.
// Bait in chests is not covered.
public static partial class Thralls
{
    const string LedgerFile = "AzraelBaitLedger.tsv";
    const float LedgerSettleSeconds = 20f;   // a joining player's inventory is checked only after this long
    static readonly string[] BaitKinds = { BaitCheck.BaitItem, BaitCheck.RichItem, BaitCheck.FeastItem };

    static readonly Dictionary<string, int[]> ledger = new Dictionary<string, int[]>();
    static readonly Dictionary<int, float> ledgerJoined = new Dictionary<int, float>();   // entity id -> time first seen
    static readonly HashSet<int> ledgerChecked = new HashSet<int>();
    static readonly Dictionary<int, string> ledgerKeyOf = new Dictionary<int, string>();
    static bool ledgerDirty;

    static string LedgerPath { get { return Path.Combine(GameIO.GetSaveGameDir(), LedgerFile); } }

    static void LoadLedger()
    {
        ledger.Clear(); ledgerJoined.Clear(); ledgerChecked.Clear(); ledgerKeyOf.Clear(); ledgerDirty = false;
        foreach (string path in new[] { LedgerPath, Path.Combine(KeepDir, LedgerFile) })
        {
            try
            {
                if (!File.Exists(path)) continue;
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 4 || p[0].Length == 0) continue;
                    ledger[p[0]] = new[] { PI(p[1]), PI(p[2]), PI(p[3]) };
                }
                if (ledger.Count > 0) return;
            }
            catch (Exception e) { Log.Warning("[Bait] could not read the bait ledger " + path + ": " + e.Message); }
        }
    }

    static void FlushLedger()
    {
        if (!ledgerDirty || !loaded) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in ledger) sb.Append(kv.Key).Append('\t').Append(kv.Value[0]).Append('\t').Append(kv.Value[1]).Append('\t').Append(kv.Value[2]).Append('\n');
            string text = sb.ToString();
            WriteSafe(LedgerPath, text);
            WriteSafe(Path.Combine(KeepDir, LedgerFile), text);
            ledgerDirty = false;
        }
        catch (Exception e) { Log.Warning("[Bait] could not save the bait ledger: " + e.Message); }
    }

    static void Record(string key, int[] counts)
    {
        int[] old;
        if (ledger.TryGetValue(key, out old) && old[0] == counts[0] && old[1] == counts[1] && old[2] == counts[2]) return;
        ledger[key] = counts;
        ledgerDirty = true;
    }

    // Every slow tick, for each online player.
    static void LedgerTick(EntityPlayer p, string key, float now)
    {
        if (!BaitConfig.RestoreLostBait) return;
        ledgerKeyOf[p.entityId] = key;
        float joined;
        if (!ledgerJoined.TryGetValue(p.entityId, out joined)) { ledgerJoined[p.entityId] = now; return; }
        if (now - joined < LedgerSettleSeconds) return;

        var counts = new int[3];
        int b, g;
        for (int i = 0; i < 3; i++) counts[i] = ItemCount(p, BaitKinds[i], out b, out g);

        if (ledgerChecked.Add(p.entityId))
        {
            int[] had;
            // A player who comes back with nothing at all died or was emptied some other way: their bait is in a dropped
            // backpack, not lost, so nothing is given.
            if (ledger.TryGetValue(key, out had) && HoldsAnything(p))
            {
                var parts = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    if (counts[i] > 0 || had[i] <= 0) continue;
                    if (!GiveItems(p, BaitKinds[i], had[i])) continue;
                    counts[i] = had[i];
                    parts.Add(had[i] + " " + (i == 0 ? "Bait" : i == 1 ? "Rich Bait" : "Feast Bait"));
                }
                if (parts.Count > 0)
                {
                    string what = string.Join(", ", parts.ToArray());
                    Log.Out("[Bait] " + NameOfKey(key) + " (" + key + ") came back without the bait they left with; gave back " + what + ".");
                    Notify(p.entityId, "Your bait went missing while you were away (most likely a server update). Gave back " + what + ".");
                }
            }
        }
        Record(key, counts);
    }

    static bool AnyStack(ItemStack[] slots)
    {
        if (slots == null) return false;
        foreach (var st in slots) if (st != null && !st.IsEmpty()) return true;
        return false;
    }
    static bool BlobHasAny(MemoryStream ms, bool toolbelt)
    {
        if (ms == null || ms.Length < 2) return false;
        byte[] d = ms.ToArray();
        if (toolbelt) d[0] = 2;
        using (var br = MemoryPools.poolBinaryReader.AllocSync(false))
        {
            br.SetBaseStream(new MemoryStream(d));
            var b = Bag.Read(br, XUiC_ItemStack.StackLocationTypes.Backpack, StreamModeRead.Persistency, null);
            return AnyStack(b.ItemGrid != null ? b.ItemGrid.items : null);
        }
    }
    static bool InvHasAny(Inventory inv)
    {
        for (int i = 0; i < inv.SlotCount; i++) { var st = inv.GetStackAt(i); if (st != null && !st.IsEmpty()) return true; }
        return false;
    }
    static bool HoldsAnything(EntityPlayer p)
    {
        try
        {
            var ci = ClientOf(p);
            if (ci != null) return BlobHasAny(ci.latestPlayerData.inventoryData, true) || BlobHasAny(ci.latestPlayerData.bagData, false);
            return (p.bag != null && AnyStack(p.bag.ItemGrid.items)) || (p.inventory != null && InvHasAny(p.inventory));
        }
        catch (Exception e) { Dbg("holds anything: " + e.Message); return false; }
    }

    // A player leaving: their game's last saved inventory is the count to keep.
    public static void OnPlayerDisconnected(ref ModEvents.SPlayerDisconnectedData d)
    {
        try
        {
            var ci = d.ClientInfo;
            if (!IsServer || !loaded || ci == null) return;
            int id = ci.entityId;
            string key;
            bool settled = ledgerChecked.Contains(id);
            ledgerJoined.Remove(id); ledgerChecked.Remove(id);
            if (!ledgerKeyOf.TryGetValue(id, out key)) return;
            ledgerKeyOf.Remove(id);
            if (!settled || !BaitConfig.RestoreLostBait || ci.latestPlayerData == null) { FlushLedger(); return; }
            var counts = new int[3];
            for (int i = 0; i < 3; i++)
            {
                var iv = ItemClass.GetItem(BaitKinds[i]);
                if (iv == null) return;
                counts[i] = BlobCount(ci.latestPlayerData.inventoryData, true, iv) + BlobCount(ci.latestPlayerData.bagData, false, iv);
            }
            Record(key, counts);
            FlushLedger();
        }
        catch (Exception e) { Log.Warning("[Bait] ledger on disconnect: " + e.Message); }
    }

    // Gives bait the same way it is taken: a game event that runs on the player's own game. The host player gets it directly.
    static bool GiveItems(EntityPlayer p, string item, int n)
    {
        if (n <= 0) return true;
        try
        {
            if (ClientOf(p) == null)
            {
                var iv = ItemClass.GetItem(item);
                var st = new ItemStack(iv, n);
                if (!p.bag.AddItem(st)) GameManager.Instance.ItemDropServer(st, p.GetPosition(), Vector3.zero, p.entityId, 600f);
                return true;
            }
            string kind = item == BaitCheck.BaitItem ? "Plain" : item == BaitCheck.RichItem ? "Rich" : "Feast";
            p.Buffs.SetCustomVar("azrGive" + kind, n, true);
            if (!GameEventManager.Current.HandleAction("azrael_give_" + kind.ToLowerInvariant(), p, p, false))
            {
                Log.Error("[Bait] the game event azrael_give_" + kind.ToLowerInvariant() + " did not run. Is Config/gameevents.xml from the Bait mod loaded?");
                return false;
            }
            return true;
        }
        catch (Exception e) { Log.Warning("[Bait] could not give " + item + " back: " + e.Message); return false; }
    }
}
