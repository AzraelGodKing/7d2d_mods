using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Platform;
using UnityEngine;

// Craft From Chests (client-only).
// While a crafting screen is open (backpack crafting, workbench, chemistry station, cement mixer, campfire - not the
// forge, which has its own material slots), the recipe list, "have" counts and the craft button also count what's in
// player-placed chests inside the land claim you're standing in (your claim, or an ally's).
//
// When you press craft and your backpack is short, the missing materials are moved from those chests into your
// backpack first, then the normal vanilla craft runs. On a multiplayer server the mod waits for the server to confirm
// each chest change; if a chest was open by someone else the server refuses the change, so nothing is crafted and
// whatever WAS taken is left in your backpack - items are never created or lost.
//
// Runs only on players' PCs. The server needs nothing.

public class AzraelCraftFromChestsMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        if (GameManager.IsDedicatedServer)
        {
            Log.Out("[CraftFromChests] Dedicated server: nothing to do here (this mod runs on players' PCs).");
            return;
        }
        Chests.LoadSettings(Path.Combine(_modInstance.Path, "settings.txt"));
        new Harmony("azrael.craftfromchests").PatchAll(typeof(AzraelCraftFromChestsMod).Assembly);
        Log.Out("[CraftFromChests] Loaded.");
    }
}

public static class Chests
{
    // ---- settings ----
    public static bool Enabled = true, AllowAllies = true, BackpackCrafting = true;

    public static void LoadSettings(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash).Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            string v = line.Substring(eq + 1).Trim().ToLowerInvariant();
            bool on = v == "on" || v == "true" || v == "yes" || v == "1";
            if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) Enabled = on;
            else if (key.Equals("AllowAllies", StringComparison.OrdinalIgnoreCase)) AllowAllies = on;
            else if (key.Equals("BackpackCrafting", StringComparison.OrdinalIgnoreCase)) BackpackCrafting = on;
        }
        Log.Out($"[CraftFromChests] Settings: Enabled={Enabled}, AllowAllies={AllowAllies}, BackpackCrafting={BackpackCrafting}");
    }

    // ---- when chests count ----
    // Scope > 0 only inside the crafting UI methods we patch, so nothing else in the game sees chest items.
    public static int Scope;
    public static bool Suspend;   // our own code wants vanilla (backpack + toolbelt only) numbers
    public static bool Busy;      // a pull is waiting for the server
    static XUiC_CraftingWindowGroup scopeGroup;

    public static void Enter(XUiController ctrl)
    {
        Scope++;
        if (Scope == 1) scopeGroup = ctrl?.WindowGroup?.Controller as XUiC_CraftingWindowGroup;
    }

    public static void Exit()
    {
        if (Scope > 0) Scope--;
        if (Scope == 0) scopeGroup = null;
    }

    public static bool Active => Enabled && !Suspend && Scope > 0 && GroupAllowed(scopeGroup);

    static readonly Dictionary<XUiC_CraftingWindowGroup, bool> groupOk = new Dictionary<XUiC_CraftingWindowGroup, bool>();

    public static bool GroupAllowed(XUiC_CraftingWindowGroup g)
    {
        if (g == null) return false;
        if (!groupOk.TryGetValue(g, out bool ok))
        {
            bool hasInputGrid = g.GetChildByType<XUiC_WorkstationInputGrid>() != null;   // forge: own material slots
            bool isBackpack = !(g is XUiC_WorkstationWindowGroup);
            ok = !hasInputGrid && (!isBackpack || BackpackCrafting);
            groupOk[g] = ok;
        }
        return ok;
    }

    // ---- finding chests ----
    static float nextRefresh;
    static readonly List<TEFeatureStorage> chests = new List<TEFeatureStorage>();
    static readonly Dictionary<int, int> counts = new Dictionary<int, int>();

    public static void Invalidate() => nextRefresh = 0f;

    static void Refresh()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        chests.Clear();
        counts.Clear();
        try
        {
            FindChests(chests);
            foreach (var s in chests)
                foreach (var st in s.items)
                    if (Usable(st))
                        counts[st.itemValue.type] = (counts.TryGetValue(st.itemValue.type, out int n) ? n : 0) + st.count;
        }
        catch (Exception e) { Log.Warning("[CraftFromChests] " + e.Message); }
    }

    // Only plain stackable materials are taken from chests (never tools/weapons/armor with quality or mods).
    static bool Usable(ItemStack st) => st != null && !st.IsEmpty() && !st.itemValue.HasQuality && !st.itemValue.HasMods();

    public static int Count(int type)
    {
        Refresh();
        return counts.TryGetValue(type, out int n) ? n : 0;
    }

    public static List<ItemStack> Stacks()
    {
        Refresh();
        var list = new List<ItemStack>();
        foreach (var kv in counts) list.Add(new ItemStack(new ItemValue(kv.Key), kv.Value));
        return list;
    }

    static void FindChests(List<TEFeatureStorage> into)
    {
        var gm = GameManager.Instance;
        var world = gm?.World;
        var player = world?.GetPrimaryPlayer();
        var pp = gm?.persistentPlayers;
        if (player == null || pp == null) return;
        var me = pp.GetPlayerDataFromEntityID(player.entityId);
        if (me == null) return;

        int half = (GameStats.GetInt(EnumGameStats.LandClaimSize) - 1) / 2;
        var pos = new Vector3i(player.position);

        // Claims (own, or an ally's) that the player is standing in.
        var claims = new List<Vector3i>();
        foreach (var kv in pp.m_lpBlockMap)
        {
            var owner = kv.Value;
            if (owner == null) continue;
            bool mine = owner == me || (owner.PrimaryId != null && owner.PrimaryId.Equals(me.PrimaryId));
            if (!mine && !(AllowAllies && owner.IsAlly(me))) continue;
            if (Math.Abs(kv.Key.x - pos.x) <= half && Math.Abs(kv.Key.z - pos.z) <= half) claims.Add(kv.Key);
        }
        if (claims.Count == 0) return;

        var seenChunks = new HashSet<long>();
        var seenPos = new HashSet<Vector3i>();
        foreach (var c in claims)
        {
            for (int x = c.x - half; x <= c.x + half + 15; x += 16)
            for (int z = c.z - half; z <= c.z + half + 15; z += 16)
            {
                var chunk = world.GetChunkFromWorldPos(new Vector3i(Math.Min(x, c.x + half), 0, Math.Min(z, c.z + half))) as Chunk;
                if (chunk == null || !seenChunks.Add(chunk.Key)) continue;
                var tes = chunk.GetTileEntities();
                if (tes == null) continue;
                foreach (var te in tes.list)
                {
                    if (!(te is TileEntityComposite comp)) continue;
                    var p = te.ToWorldPos();
                    if (!InAnyClaim(p, claims, half) || !seenPos.Add(p)) continue;
                    var storage = comp.GetFeature<TEFeatureStorage>();
                    if (storage == null || !comp.PlayerPlaced) continue;
                    if (comp.IsUserAccessing()) continue;                       // open on this PC
                    var lockable = comp.GetFeature<TEFeatureLockable>();
                    if (lockable != null && lockable.IsLocked() && !lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier)) continue;
                    var name = world.GetBlock(p).Block?.GetBlockName() ?? "";
                    if (name.IndexOf("vending", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    into.Add(storage);
                }
            }
        }
    }

    static bool InAnyClaim(Vector3i p, List<Vector3i> claims, int half)
    {
        foreach (var c in claims)
            if (Math.Abs(p.x - c.x) <= half && Math.Abs(p.z - c.z) <= half) return true;
        return false;
    }

    // ---- moving materials from chests into the backpack ----
    public static IEnumerator Pull(XUi xui, Dictionary<int, int> need, Action onSuccess)
    {
        Busy = true;
        Invalidate();
        Refresh();

        var touched = new List<(TEFeatureStorage s, int type, int expected, int took)>();
        var taken = new Dictionary<int, int>();
        foreach (var kv in need)
        {
            int left = kv.Value;
            foreach (var s in chests)
            {
                if (left <= 0) break;
                int before = CountIn(s, kv.Key);
                if (before <= 0) continue;
                int took = TakeFrom(s, kv.Key, left);
                if (took <= 0) continue;
                left -= took;
                taken[kv.Key] = (taken.TryGetValue(kv.Key, out int t) ? t : 0) + took;
                touched.Add((s, kv.Key, before - took, took));
            }
        }
        var changed = new HashSet<TEFeatureStorage>();
        foreach (var t in touched) changed.Add(t.s);
        foreach (var s in changed) s.SetModified();

        // Wait for the server to answer each changed chest (instant in single player / when hosting).
        float until = Time.unscaledTime + 3f;
        while (Time.unscaledTime < until)
        {
            bool waiting = false;
            foreach (var s in changed)
                if (s.Parent != null && s.Parent.bWaitingForServerResponse) { waiting = true; break; }
            if (!waiting) break;
            yield return null;
        }

        // If the server refused a change (chest open by someone else), its real contents came back: don't count those.
        bool failed = false;
        foreach (var t in touched)
        {
            int now = CountIn(t.s, t.type);
            if (now > t.expected)
            {
                int notTaken = Math.Min(t.took, now - t.expected);
                taken[t.type] -= notTaken;
                failed = true;
            }
        }

        foreach (var kv in taken)
            if (kv.Value > 0) GiveToBackpack(xui, kv.Key, kv.Value);

        Busy = false;
        Invalidate();
        if (failed)
        {
            GameManager.ShowTooltip(xui.playerUI.entityPlayer, Localization.Get("ttAzraelCfcInUse"), string.Empty, "ui_denied");
            Log.Out("[CraftFromChests] A chest was in use by someone else; craft cancelled, taken materials left in backpack.");
        }
        else
        {
            try { onSuccess(); } catch (Exception e) { Log.Warning("[CraftFromChests] " + e); }
        }
    }

    static int CountIn(TEFeatureStorage s, int type)
    {
        int n = 0;
        foreach (var st in s.items) if (Usable(st) && st.itemValue.type == type) n += st.count;
        return n;
    }

    static int TakeFrom(TEFeatureStorage s, int type, int want)
    {
        int took = 0;
        var items = s.items;
        for (int i = 0; i < items.Length && took < want; i++)
        {
            var st = items[i];
            if (!Usable(st) || st.itemValue.type != type) continue;
            int t = Math.Min(st.count, want - took);
            st.count -= t;
            took += t;
            if (st.count <= 0) items[i] = ItemStack.Empty.Clone();
        }
        return took;
    }

    static void GiveToBackpack(XUi xui, int type, int count)
    {
        var iv = new ItemValue(type);
        int max = Math.Max(1, iv.ItemClass?.Stacknumber?.Value ?? 1);
        while (count > 0)
        {
            int n = Math.Min(max, count);
            count -= n;
            var stack = new ItemStack(iv.Clone(), n);
            if (!xui.PlayerInventory.AddItem(stack))
            {
                var player = xui.playerUI.entityPlayer;
                GameManager.Instance.ItemDropServer(stack, player.GetPosition(), Vector3.zero);
            }
        }
    }
}
