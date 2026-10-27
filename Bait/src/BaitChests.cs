using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

// Bait in player-placed chests (see BaitLedger.cs for why bait can vanish). The server notes how much bait each chest
// holds whenever the chest changes, and keeps it in the same ledger. A chest's contents can't change while its area is
// unloaded or the server is down, so the first time a chest is seen after a start it must hold what it held before.
// A kind that is down to zero then is put back. A chest that is picked up or destroyed is forgotten, so a new chest
// placed on the same spot gets nothing.
public static partial class Thralls
{
    class ChestBait { public string Block; public int[] Counts; }
    static readonly Dictionary<Vector3i, ChestBait> chests = new Dictionary<Vector3i, ChestBait>();
    static readonly HashSet<long> chestChunksSeen = new HashSet<long>();   // chunks whose chests were checked this session
    static readonly HashSet<Vector3i> chestsChanged = new HashSet<Vector3i>();
    static readonly object chestLock = new object();

    static void ResetChests()
    {
        chests.Clear(); chestChunksSeen.Clear();
        lock (chestLock) chestsChanged.Clear();
    }

    static void ReadChestLine(string[] p)
    {
        string[] xyz = p[1].Split(',');
        if (xyz.Length != 3) return;
        var pos = new Vector3i(PI(xyz[0]), PI(xyz[1]), PI(xyz[2]));
        chests[pos] = new ChestBait { Block = p[2], Counts = new[] { PI(p[3]), PI(p[4]), PI(p[5]) } };
    }

    static void WriteChestLines(StringBuilder sb)
    {
        foreach (var kv in chests)
            sb.Append("chest\t").Append(kv.Key.x).Append(',').Append(kv.Key.y).Append(',').Append(kv.Key.z)
              .Append('\t').Append(kv.Value.Block).Append('\t').Append(kv.Value.Counts[0]).Append('\t').Append(kv.Value.Counts[1])
              .Append('\t').Append(kv.Value.Counts[2]).Append('\n');
    }

    static TEFeatureStorage StorageOf(TileEntity te)
    {
        var comp = te as TileEntityComposite;
        if (comp == null || !comp.PlayerPlaced) return null;
        return comp.GetFeature<TEFeatureStorage>();
    }

    static string BlockNameOf(TileEntity te)
    {
        try { var comp = te as TileEntityComposite; return comp != null && comp.TeData != null && comp.TeData.Block != null ? comp.TeData.Block.GetBlockName() : "?"; }
        catch { return "?"; }
    }

    static int[] CountChest(TEFeatureStorage s)
    {
        var c = new int[3];
        var items = s.ItemGrid != null ? s.ItemGrid.items : null;
        if (items == null) return c;
        foreach (var st in items)
        {
            if (st == null || st.IsEmpty() || st.itemValue == null || st.itemValue.ItemClass == null) continue;
            int k = Array.IndexOf(BaitKinds, st.itemValue.ItemClass.GetItemName());
            if (k >= 0) c[k] += st.count;
        }
        return c;
    }

    static void NoteChest(Vector3i pos, string block, int[] c)
    {
        ChestBait old;
        if (c[0] == 0 && c[1] == 0 && c[2] == 0) { if (chests.Remove(pos)) ledgerDirty = true; return; }
        if (chests.TryGetValue(pos, out old) && old.Block == block && old.Counts[0] == c[0] && old.Counts[1] == c[1] && old.Counts[2] == c[2]) return;
        chests[pos] = new ChestBait { Block = block, Counts = c };
        ledgerDirty = true;
    }

    // Called by the patches below.
    public static void OnChestModified(TileEntity te)
    {
        if (!loaded || !BaitConfig.RestoreLostBait || StorageOf(te) == null) return;
        try { var pos = te.ToWorldPos(); lock (chestLock) chestsChanged.Add(pos); } catch { }
    }
    public static void OnChestRemoved(TileEntity te)
    {
        if (!loaded || !(te is TileEntityComposite)) return;
        try
        {
            var pos = te.ToWorldPos();
            lock (chestLock) chestsChanged.Remove(pos);
            if (chests.Remove(pos)) { ledgerDirty = true; FlushLedger(); }
        }
        catch { }
    }

    // Every second: record changed chests (saved right away, so a crash can't leave an old count behind).
    static void ChestTick(World world)
    {
        if (!BaitConfig.RestoreLostBait) return;
        List<Vector3i> changed;
        lock (chestLock)
        {
            if (chestsChanged.Count == 0) return;
            changed = new List<Vector3i>(chestsChanged);
            chestsChanged.Clear();
        }
        foreach (var pos in changed)
        {
            var chunk = world.GetChunkFromWorldPos(pos) as Chunk;
            if (chunk == null || !chestChunksSeen.Contains(chunk.Key)) continue;   // first look happens in ChestScan
            var te = world.GetTileEntity(pos);
            var s = te != null ? StorageOf(te) : null;
            if (s == null) { if (chests.Remove(pos)) ledgerDirty = true; continue; }
            NoteChest(pos, BlockNameOf(te), CountChest(s));
        }
        FlushLedger();
    }

    // Every second: the first look at each newly loaded chunk's chests this session.
    static void ChestScan(World world)
    {
        if (!BaitConfig.RestoreLostBait || world.ChunkCache == null) return;
        int refunds = 0;
        foreach (var chunk in world.ChunkCache.GetChunkArrayCopySync())
        {
            if (chunk == null || chestChunksSeen.Contains(chunk.Key)) continue;
            chestChunksSeen.Add(chunk.Key);
            var found = new HashSet<Vector3i>();
            var tes = chunk.GetTileEntities();
            if (tes != null)
                foreach (var te in new List<TileEntity>(tes.list))
                {
                    var s = StorageOf(te);
                    if (s == null) continue;
                    var pos = te.ToWorldPos();
                    found.Add(pos);
                    string block = BlockNameOf(te);
                    var c = CountChest(s);
                    ChestBait had;
                    if (chests.TryGetValue(pos, out had) && had.Block == block)
                    {
                        var parts = new List<string>();
                        for (int i = 0; i < 3; i++)
                        {
                            if (c[i] > 0 || had.Counts[i] <= 0) continue;
                            var iv = ItemClass.GetItem(BaitKinds[i]);
                            if (iv == null || iv.IsEmpty() || !s.AddItem(new ItemStack(iv, had.Counts[i]))) continue;
                            c[i] = had.Counts[i];
                            parts.Add(had.Counts[i] + " " + BaitKinds[i]);
                        }
                        if (parts.Count > 0)
                        {
                            te.SetModified();
                            refunds++;
                            Log.Out("[Bait] the " + block + " at " + pos + " lost its bait while the area was unloaded or the server was down; put back " + string.Join(", ", parts.ToArray()) + ".");
                        }
                    }
                    NoteChest(pos, block, c);
                }
            // chests on the record in this chunk that are gone
            var gone = new List<Vector3i>();
            foreach (var pos in chests.Keys)
                if (!found.Contains(pos) && World.toChunkXZ(pos.x) == chunk.X && World.toChunkXZ(pos.z) == chunk.Z) gone.Add(pos);
            foreach (var pos in gone) { chests.Remove(pos); ledgerDirty = true; }
        }
        if (chestChunksSeen.Count > 200000) chestChunksSeen.Clear();
        if (refunds > 0) FlushLedger();
    }
}

// A change the server makes itself.
[HarmonyPatch(typeof(TileEntity), "setModified")]
public static class BaitChestModified
{
    static void Postfix(TileEntity __instance) { Thralls.OnChestModified(__instance); }
}

// A change a player makes: their game sends the whole chest, and the server takes it without calling setModified.
[HarmonyPatch(typeof(NetPackageTileEntity), nameof(NetPackageTileEntity.ProcessPackage))]
public static class BaitChestFromClient
{
    static void Postfix(NetPackageTileEntity __instance, World _world)
    {
        try { var te = _world != null ? _world.GetTileEntity(__instance.teWorldPos) : null; if (te != null) Thralls.OnChestModified(te); } catch { }
    }
}

[HarmonyPatch(typeof(TileEntityComposite), nameof(TileEntityComposite.OnRemove))]
public static class BaitChestRemoved
{
    static void Prefix(TileEntityComposite __instance) { Thralls.OnChestRemoved(__instance); }
}
