using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Platform;
using UnityEngine;

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
    // Off by default: block upgrades (repair tool) and the Upgrade Bench's material cost only use chests when on.
    public static bool UpgradeFromChests = false;

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
            else if (key.Equals("UpgradeFromChests", StringComparison.OrdinalIgnoreCase)) UpgradeFromChests = on;
        }
        Log.Out($"[CraftFromChests] Settings: Enabled={Enabled}, AllowAllies={AllowAllies}, BackpackCrafting={BackpackCrafting}, UpgradeFromChests={UpgradeFromChests}");
    }

    // ---- when chests count ----
    // Scope > 0 only inside the crafting UI methods we patch, so nothing else in the game sees chest items.
    // Station is set while the Upgrade Bench screen is open (that screen is not a crafting window).
    public static int Scope;
    public static bool Station;
    public static bool Suspend;   // our own code wants vanilla (backpack + toolbelt only) numbers
    public static bool Busy;      // a pull is waiting for the server
    public static bool Replay;    // the pull finished; let the real craft / upgrade button run
    public static bool SuppressMissing; // block upgrade is fetching materials; don't flash "you have none"
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

    public static bool Active => Enabled && !Suspend && ((Scope > 0 && GroupAllowed(scopeGroup)) || Station);

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
                EachSlot(s, st =>
                {
                    if (Usable(st))
                        counts[st.itemValue.type] = (counts.TryGetValue(st.itemValue.type, out int n) ? n : 0) + st.count;
                });
        }
        catch (Exception e) { Log.Warning("[CraftFromChests] " + e.Message); }
    }

    // Stackable materials can come from a chest. A quality item that does not stack (tool, weapon, armor,
    // small engine) cannot - it has to be the one in your backpack. Some stackable parts still report a quality
    // flag; those are materials, and a gyro's wheels are the case players hit.
    public static bool Material(ItemValue iv)
    {
        if (iv == null || iv.IsEmpty() || iv.HasMods()) return false;
        if (!iv.HasQuality) return true;
        return (iv.ItemClass?.Stacknumber?.Value ?? 1) > 1;
    }

    static bool Usable(ItemStack st) => st != null && !st.IsEmpty() && Material(st.itemValue);

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
    public static IEnumerator Pull(XUi xui, Dictionary<int, int> need, Action onSuccess, string failedKey = null)
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
        float until = Time.unscaledTime + AnswerWaitSeconds;
        while (Time.unscaledTime < until && AnyWaiting(changed))
            yield return null;

        // If the server refused a change (chest open by someone else), its real contents came back: don't count those.
        // A chest the server still hasn't answered is not counted either. Until the answer arrives the local count
        // only shows our own edit, and handing those items out now would duplicate them if the server then refuses.
        bool failed = false;
        var late = new List<(TEFeatureStorage s, int type, int expected, int took)>();
        foreach (var t in touched)
        {
            if (Waiting(t.s))
            {
                taken[t.type] -= t.took;
                late.Add(t);
                failed = true;
                continue;
            }
            int now = CountIn(t.s, t.type);
            if (now > t.expected)
            {
                int notTaken = Math.Min(t.took, now - t.expected);
                taken[t.type] -= notTaken;
                failed = true;
            }
        }
        if (late.Count > 0)
        {
            Log.Out($"[CraftFromChests] The server hasn't answered for {late.Count} chest change(s) yet; those materials come later if it accepts.");
            GameManager.Instance.StartCoroutine(DeliverLate(xui, late));
        }

        foreach (var kv in taken)
            if (kv.Value > 0) GiveToBackpack(xui, kv.Key, kv.Value);

        Busy = false;
        Invalidate();
        if (failed)
        {
            GameManager.ShowTooltip(xui.playerUI.entityPlayer, Localization.Get(failedKey ?? "ttAzraelCfcInUse"), string.Empty, "ui_denied");
            Log.Out("[CraftFromChests] A chest was in use by someone else; cancelled, taken materials left in backpack.");
        }
        else
        {
            try { onSuccess(); } catch (Exception e) { Log.Warning("[CraftFromChests] " + e); }
        }
    }

    const float AnswerWaitSeconds = 10f;
    const float LateAnswerWaitSeconds = 120f;

    static bool Waiting(TEFeatureStorage s) => s.Parent != null && s.Parent.bWaitingForServerResponse;

    static bool AnyWaiting(IEnumerable<TEFeatureStorage> chests)
    {
        foreach (var s in chests)
            if (Waiting(s)) return true;
        return false;
    }

    // Gives the materials from a slow chest once the server answers, and only what it actually let us take.
    static IEnumerator DeliverLate(XUi xui, List<(TEFeatureStorage s, int type, int expected, int took)> late)
    {
        float until = Time.unscaledTime + LateAnswerWaitSeconds;
        while (late.Count > 0 && Time.unscaledTime < until)
        {
            for (int i = late.Count - 1; i >= 0; i--)
            {
                var t = late[i];
                if (Waiting(t.s)) continue;
                late.RemoveAt(i);
                int now = CountIn(t.s, t.type);
                int got = now > t.expected ? t.took - Math.Min(t.took, now - t.expected) : t.took;
                if (got > 0 && xui?.playerUI?.entityPlayer != null) GiveToBackpack(xui, t.type, got);
            }
            if (late.Count > 0) yield return null;
        }
        if (late.Count > 0)
            Log.Warning($"[CraftFromChests] No server answer for {late.Count} chest change(s) after {LateAnswerWaitSeconds:0}s; those materials were not handed out.");
        Invalidate();
    }

    // 3.3 stores a chest as ItemGrid. The old ItemStack[] items getter is gone.
    static void EachSlot(TEFeatureStorage s, Action<ItemStack> visit)
    {
        var grid = s?.ItemGrid;
        if (grid == null) return;
        for (int i = 0; i < grid.Length; i++) visit(grid[i]);
    }

    static int CountIn(TEFeatureStorage s, int type)
    {
        int n = 0;
        EachSlot(s, st => { if (Usable(st) && st.itemValue.type == type) n += st.count; });
        return n;
    }

    static int TakeFrom(TEFeatureStorage s, int type, int want)
    {
        int took = 0;
        var grid = s?.ItemGrid;
        if (grid == null) return 0;
        for (int i = 0; i < grid.Length && took < want; i++)
        {
            var st = grid[i];
            if (!Usable(st) || st.itemValue.type != type) continue;
            int t = Math.Min(st.count, want - took);
            st.count -= t;
            took += t;
            if (st.count <= 0) grid[i] = ItemStack.Empty.Clone();
        }
        return took;
    }

    public static void GiveToBackpack(XUi xui, int type, int count)
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

    public static bool IsUpgradeBench(TEFeatureCombine te)
    {
        try
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || te == null) return false;
            var block = world.GetBlock(te.ToWorldPos()).Block;
            return block != null && block.GetBlockName() == "azraelUpgradeBench";
        }
        catch { return false; }
    }

    // The repair tool asks "do you already have the whole upgrade cost in one pile?" before it will start.
    // If the rest is in a claim chest, move that rest (and a partial hotbar stack) into the backpack and let the
    // next swing run the vanilla upgrade. Tools and other quality items are not fetched.
    public static void TryStageUpgrade(ItemActionRepair action, ItemInventoryData data, BlockValue blockValue)
    {
        if (!Enabled || !UpgradeFromChests) return;
        if (Busy) { SuppressMissing = true; return; }
        var player = data?.holdingEntity as EntityPlayerLocal;
        var block = blockValue.Block;
        if (player == null || action == null || block == null) return;

        string name = action.GetUpgradeItemName(block);
        if (string.IsNullOrEmpty(name)) return;
        if (action.allowedUpgradeItems != null && action.allowedUpgradeItems.Length > 0 && !action.allowedUpgradeItems.ContainsCaseInsensitive(name)) return;
        if (action.restrictedUpgradeItems != null && action.restrictedUpgradeItems.Length > 0 && action.restrictedUpgradeItems.ContainsCaseInsensitive(name)) return;
        if (!int.TryParse(block.Properties.GetString("UpgradeBlock", "UpgradeHitCount"), out _)) return;
        if (!int.TryParse(block.Properties.GetString(Block.PropUpgradeBlockClass, Block.PropUpgradeBlockItemCount), out int need) || need <= 0) return;

        var item = ItemClass.GetItem(name);
        if (item == null || item.IsEmpty() || !Material(item)) return;

        int onBar = data.holdingEntity.inventory.GetItemCount(item);
        int inBag = data.holdingEntity.bag.GetItemCount(item);
        if (onBar >= need || inBag >= need) return;
        int inChest = Count(item.type);
        if (onBar + inBag + inChest < need) return;

        int shortfall = need - onBar - inBag;
        if (shortfall <= 0) return;

        var xui = LocalPlayerUI.GetUIForPlayer(player)?.xui;
        if (xui == null) return;

        int alsoMove = onBar > 0 ? onBar : 0;
        Suspend = true;
        int space;
        try { space = xui.PlayerInventory.CountAvailableSpaceForItem(item, false); }
        finally { Suspend = false; }
        if (space < shortfall + alsoMove)
        {
            GameManager.ShowTooltip(player, Localization.Get("ttAzraelCfcNoRoom"), string.Empty, "ui_denied");
            return;
        }

        SuppressMissing = true;
        var held = data;
        var iv = item;
        int required = need;
        GameManager.Instance.StartCoroutine(Pull(xui, new Dictionary<int, int> { { item.type, shortfall } }, () =>
        {
            // Vanilla spends the whole cost from the toolbelt first, and a partial stack there is removed even when
            // the backpack has the rest. Empty the toolbelt into the backpack so one pile holds the full cost.
            int stillOnBar = held.holdingEntity.inventory.GetItemCount(iv);
            int nowInBag = held.holdingEntity.bag.GetItemCount(iv);
            if (stillOnBar <= 0 || stillOnBar >= required || nowInBag >= required) return;
            int moved = held.holdingEntity.inventory.DecItem(iv, Math.Min(stillOnBar, required - nowInBag));
            if (moved > 0) GiveToBackpack(xui, iv.type, moved);
        }, "ttAzraelCfcUpgradeHeld"));
    }
}
