using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

// Storage Backpack: a placeable storage block that can be carried with its contents. Server only.
//
// Pack:   the backpack block is removed (broken by anyone, or anything). Instead of the vanilla loot bag, the
//         server saves the contents to a record file in the save folder and drops one "Packed Storage
//         Backpack" item whose metadata holds only the record id. An empty backpack drops as itself.
// Unpack: a player places a packed backpack. Placement is client-side and the block change carries no item
//         data, so the server reads the record id from the item that player is holding (the client sends the
//         held item, metadata included, whenever it changes). It swaps the block for a normal backpack, puts
//         the contents back and retires the record, so an id can be unpacked only once.
// Clients need nothing: the XML (blocks, loot, recipe, text) is sent to them by the server on join.

public class AzraelStorageBackpack : IModApi
{
    public const string Tag = "[StorageBackpack]";

    public void InitMod(Mod _modInstance)
    {
        new Harmony("azrael.storagebackpack").PatchAll(typeof(AzraelStorageBackpack).Assembly);
        ModEvents.GameStartDone.RegisterHandler(OnGameStart);
        Log.Out($"{Tag} Loaded. Break a Storage Backpack to pack it, place the packed one to unpack. Players do not install this mod.");
    }

    static void OnGameStart(ref ModEvents.SGameStartDoneData _data)
    {
        try { PackStore.PurgeUsed(); }
        catch (Exception e) { Log.Warning($"{Tag} Could not clean old records: {e.Message}"); }
        DroneGuard.TryPatch();
    }
}

public static class Packs
{
    public const string BlockName = "azrStorageBackpack";
    public const string PackedName = "azrStorageBackpackPacked";
    public const string MetaKey = "azrPackId";
    // Real seconds a dropped packed backpack stays on the ground (vanilla drops: 60). One day.
    public const float DropLifetime = 86400f;

    static int blockType = -2, packedType = -2;

    public static int BlockType { get { Resolve(); return blockType; } }
    public static int PackedType { get { Resolve(); return packedType; } }

    static void Resolve()
    {
        if (blockType != -2) return;
        Block b = Block.GetBlockByName(BlockName);
        Block p = Block.GetBlockByName(PackedName);
        blockType = b != null ? b.blockID : -1;
        packedType = p != null ? p.blockID : -1;
        if (blockType < 0 || packedType < 0)
            Log.Warning($"{AzraelStorageBackpack.Tag} Blocks not found (XML missing?). The mod does nothing.");
    }

    public static bool IsOurs(int type) => type >= 0 && (type == BlockType || type == PackedType);

    public static bool IsServer => SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer;

    // Last item each remote player told the server they hold. Kept here, not read from the entity, because
    // the entity applies held-item updates on its next tick and the block change can arrive before that.
    public static readonly Dictionary<int, ItemStack> Held = new Dictionary<int, ItemStack>();

    // The player whose block changes the server is applying right now (set around GameManager.ChangeBlocks).
    [ThreadStatic] public static EntityPlayer CurrentChanger;

    public static EntityPlayer ResolvePlayer(PlatformUserIdentifierAbs persistentPlayerId)
    {
        GameManager gm = GameManager.Instance;
        if (persistentPlayerId == null) return gm.myEntityPlayerLocal; // host player, or null on a dedicated server
        PersistentPlayerData ppd = gm.persistentPlayers?.GetPlayerData(persistentPlayerId);
        if (ppd == null || ppd.EntityId == -1) return null;
        return gm.World?.GetEntity(ppd.EntityId) as EntityPlayer;
    }

    // The packed backpack id in the item the player is holding. Two sources, freshest first: what the client last
    // sent (remote players), then the server's own copy of their hand. The first id that still has contents wins.
    public static string HeldPackId(EntityPlayer player)
    {
        if (player == null) return null;
        var stacks = new List<ItemStack>();
        if (!(player is EntityPlayerLocal) && Held.TryGetValue(player.entityId, out ItemStack sent)) stacks.Add(sent);
        if (player.inventory != null) stacks.Add(player.inventory.holdingItemStack);
        string fallback = null;
        foreach (ItemStack stack in stacks)
        {
            if (stack == null || stack.IsEmpty() || stack.itemValue.type != PackedType) continue;
            if (!stack.itemValue.TryGetMetadata(MetaKey, out string id)) continue;
            if (PackStore.Exists(id)) return id;
            fallback ??= id;
        }
        return fallback;
    }

    // ---- pack ----

    public static void Pack(TEFeatureStorage storage, BlockValue bvOld)
    {
        Vector3i pos = storage.ToWorldPos();
        ItemStack[] items = storage.ItemGrid.CloneItems();
        // No packed backpacks inside packed backpacks (1.0.2): they come out and drop next to it instead,
        // otherwise nesting gives unlimited storage in one inventory slot. Empty backpacks are plain items and stay.
        var nested = new List<ItemStack>();
        bool any = false;
        for (int i = 0; i < items.Length; i++)
        {
            ItemStack s = items[i];
            if (s == null || s.IsEmpty()) continue;
            if (s.itemValue.type == PackedType) { nested.Add(s); items[i] = ItemStack.Empty.Clone(); continue; }
            any = true;
        }

        ItemValue drop;
        string id = null;
        if (any)
        {
            id = PackStore.Save(items);
            drop = Block.GetBlockValue(PackedName).ToItemValue();
            drop.SetMetadata(MetaKey, id);
        }
        else
        {
            drop = Block.GetBlockValue(BlockName).ToItemValue();
        }

        // Empty the grid so the vanilla code that runs next drops no loot bag.
        storage.SetEmpty();

        // Drop it at the feet of the player who broke it when they are close, otherwise where it stood.
        Vector3 at = pos.ToVector3() + new Vector3(0.5f, 0.5f, 0.5f);
        EntityPlayer p = CurrentChanger;
        if (p != null && (p.GetPosition() - at).sqrMagnitude < 15f * 15f) at = p.GetPosition() + new Vector3(0f, 0.5f, 0f);
        GameManager.Instance.ItemDropServer(new ItemStack(drop, 1), at, Vector3.zero, -1, DropLifetime);
        foreach (ItemStack n in nested)
            GameManager.Instance.ItemDropServer(n, at, new Vector3(0.5f, 0f, 0.5f), -1, DropLifetime);

        if (id != null)
            Log.Out($"{AzraelStorageBackpack.Tag} Packed backpack at {pos} into {id}" + (p != null ? $" for {p.EntityName}" : "") + ".");
    }

    // ---- unpack ----

    public static void Unpack(Vector3i pos, BlockValue placed, EntityPlayer player)
    {
        World world = GameManager.Instance.World;
        string id = HeldPackId(player);
        ItemStack[] items = id != null ? PackStore.Take(id) : null;

        // Turn the packed block into a normal backpack (same rotation). The empty storage carries over.
        BlockValue normal = Block.GetBlockValue(BlockName);
        normal.rotation = placed.rotation;
        normal.meta = placed.meta;
        world.SetBlockRPC(pos, normal);

        if (items == null)
        {
            if (id != null) Log.Warning($"{AzraelStorageBackpack.Tag} No contents found for packed backpack {id} placed at {pos} (already unpacked?).");
            return;
        }

        TEFeatureStorage storage = (world.GetTileEntity(pos) as ITileEntity)?.GetSelfOrFeature<TEFeatureStorage>();
        if (storage == null)
        {
            // Should not happen; keep the contents safe rather than lose them.
            GameManager.Instance.DropContentInLootContainerServer(-1, "DroppedLootContainer", pos.ToVector3() + new Vector3(0.5f, 1f, 0.5f), items);
            Log.Warning($"{AzraelStorageBackpack.Tag} Backpack at {pos} has no storage; dropped {id} as a loot bag.");
            return;
        }

        ItemStackGrid grid = storage.ItemGrid;
        var overflow = new List<ItemStack>();
        // Same slot as before where possible (keeps the player's layout), else the first free one.
        for (int i = 0; i < items.Length; i++)
        {
            ItemStack s = items[i];
            if (s == null || s.IsEmpty()) continue;
            int slot = i < grid.Length && grid[i].IsEmpty() ? i : -1;
            for (int j = 0; slot < 0 && j < grid.Length; j++) if (grid[j].IsEmpty()) slot = j;
            if (slot >= 0) storage.UpdateSlot(slot, s);
            else overflow.Add(s);
        }
        grid.Touch();
        storage.SetModified();
        if (overflow.Count > 0)
            GameManager.Instance.DropContentInLootContainerServer(-1, "DroppedLootContainer", pos.ToVector3() + new Vector3(0.5f, 1f, 0.5f), overflow.ToArray());

        Log.Out($"{AzraelStorageBackpack.Tag} Unpacked {id} at {pos}" + (player != null ? $" for {player.EntityName}" : "") + ".");
    }
}

// Contents of packed backpacks: one file per id in <save>/AzraelStorageBackpack. A placed id moves to used/
// (kept 7 days so an admin can recover it after a crash), so the same id never unpacks twice.
public static class PackStore
{
    static readonly Regex IdPattern = new Regex("^[0-9a-f]{32}$");

    static string Dir => Path.Combine(GameIO.GetSaveGameDir(), "AzraelStorageBackpack");
    static string UsedDir => Path.Combine(Dir, "used");

    public static string Save(ItemStack[] items)
    {
        string id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Dir);
        byte[] data;
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(1); // format version
            using (PooledBinaryWriter bw = MemoryPools.poolBinaryWriter.AllocSync(_bReset: false))
            {
                bw.SetBaseStream(ms);
                ItemStack.WriteArray(bw, items);
            }
            data = ms.ToArray();
        }
        string path = Path.Combine(Dir, id + ".bin");
        File.WriteAllBytes(path + ".tmp", data);
        File.Move(path + ".tmp", path);
        return id;
    }

    // Reads and retires a record. Null if the id is malformed, unknown or already used.
    public static ItemStack[] Take(string id)
    {
        if (string.IsNullOrEmpty(id) || !IdPattern.IsMatch(id)) return null;
        string path = Path.Combine(Dir, id + ".bin");
        if (!File.Exists(path)) return null;

        ItemStack[] items;
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (PooledBinaryReader br = MemoryPools.poolBinaryReader.AllocSync(_bReset: false))
        {
            ms.ReadByte(); // format version
            br.SetBaseStream(ms);
            items = ItemStack.ReadArray(br);
        }

        Directory.CreateDirectory(UsedDir);
        string used = Path.Combine(UsedDir, id + ".bin");
        if (File.Exists(used)) File.Delete(used);
        File.Move(path, used);
        return items ?? new ItemStack[0];
    }

    public static bool Exists(string id) =>
        !string.IsNullOrEmpty(id) && IdPattern.IsMatch(id) && File.Exists(Path.Combine(Dir, id + ".bin"));

    // Records still waiting to be unpacked (active) or already unpacked in the last 7 days (used).
    public static List<(string id, bool used, DateTime saved)> List()
    {
        var list = new List<(string, bool, DateTime)>();
        foreach (var (dir, used) in new[] { (Dir, false), (UsedDir, true) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string f in Directory.GetFiles(dir, "*.bin"))
                list.Add((Path.GetFileNameWithoutExtension(f), used, File.GetLastWriteTimeUtc(f)));
        }
        list.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        return list;
    }

    // Admin recovery: make a used record unpackable again. True if the id is (now) active.
    public static bool Reactivate(string id)
    {
        if (string.IsNullOrEmpty(id) || !IdPattern.IsMatch(id)) return false;
        if (Exists(id)) return true;
        string used = Path.Combine(UsedDir, id + ".bin");
        if (!File.Exists(used)) return false;
        File.Move(used, Path.Combine(Dir, id + ".bin"));
        return true;
    }

    public static void PurgeUsed()
    {
        if (!Directory.Exists(UsedDir)) return;
        DateTime cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (string f in Directory.GetFiles(UsedDir, "*.bin"))
            if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f);
    }
}

// Drone Automation (tehAon) Auto-Loot drones vacuum up dropped items, packed backpacks included, which can end
// with one in the drone and one in the player's inventory. Keep drones off both backpack items, on the ground and
// in containers. Optional: does nothing without Drone Automation. Same hooks Drone Loot Filter uses.
public static class DroneGuard
{
    static bool done;

    public static void TryPatch()
    {
        if (done) return;
        done = true;
        Type vac = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name != "DroneAutomation") continue;
            try { foreach (var t in a.GetTypes()) if (t.Name == "VacuumCore") vac = t; }
            catch (Exception e) { Log.Warning($"{AzraelStorageBackpack.Tag} Could not read Drone Automation: {e.Message}"); }
        }
        if (vac == null) return;
        var h = new Harmony("azrael.storagebackpack.drone");
        int hooked = 0;
        var settled = AccessTools.Method(vac, "IsSettledLoot");
        if (settled != null && settled.ReturnType == typeof(bool) && HasParam(settled, "_item", typeof(EntityItem)))
        {
            h.Patch(settled, postfix: new HarmonyMethod(typeof(DroneGuard), nameof(SettledPost)));
            hooked++;
        }
        var absorb = AccessTools.Method(vac, "Absorb");
        if (absorb != null && absorb.ReturnType == typeof(bool) && HasParam(absorb, "_moving", typeof(ItemStack)))
        {
            h.Patch(absorb, prefix: new HarmonyMethod(typeof(DroneGuard), nameof(AbsorbPre)));
            hooked++;
        }
        if (hooked == 2) Log.Out($"{AzraelStorageBackpack.Tag} Drone Automation found: Auto-Loot drones leave Storage Backpacks alone.");
        else Log.Warning($"{AzraelStorageBackpack.Tag} Drone Automation found but its Auto-Loot changed ({hooked}/2 hooks); drones may still pick up packed backpacks.");
    }

    static bool HasParam(System.Reflection.MethodInfo m, string name, Type type)
    {
        foreach (var p in m.GetParameters()) if (p.Name == name && p.ParameterType == type) return true;
        return false;
    }

    static bool IsBackpack(ItemStack s) => s != null && !s.IsEmpty() && Packs.IsOurs(s.itemValue.type);

    public static void SettledPost(EntityItem _item, ref bool __result)
    {
        if (__result && _item != null && IsBackpack(_item.itemStack)) __result = false;
    }

    public static bool AbsorbPre(ItemStack _moving, ref bool __result)
    {
        if (!IsBackpack(_moving)) return true;
        __result = false;
        return false;
    }
}

// ---- patches ----

// Remember what each remote player holds, as soon as the server hears about it.
[HarmonyPatch(typeof(NetPackageHoldingItem), nameof(NetPackageHoldingItem.ProcessPackage))]
public static class Patch_HoldingItem
{
    static void Prefix(NetPackageHoldingItem __instance)
    {
        if (!Packs.IsServer || __instance.holdingItemStack == null) return;
        Packs.Held[__instance.entityId] = __instance.holdingItemStack.Clone();
    }
}

// Note who is changing blocks, and which of the changes place a packed backpack.
[HarmonyPatch(typeof(GameManager), nameof(GameManager.ChangeBlocks))]
public static class Patch_ChangeBlocks
{
    static void Prefix(GameManager __instance, PlatformUserIdentifierAbs persistentPlayerId, List<BlockChangeInfo> _blocksToChange, out List<Vector3i> __state)
    {
        __state = null;
        if (!Packs.IsServer || __instance.World == null || _blocksToChange == null) return;
        Packs.CurrentChanger = Packs.ResolvePlayer(persistentPlayerId);
        int packed = Packs.PackedType;
        if (packed < 0) return;
        foreach (BlockChangeInfo c in _blocksToChange)
        {
            if (!c.bChangeBlockValue || c.blockValue.type != packed || c.blockValue.ischild) continue;
            if (!c.blockValueRef.TryGetBlockPos(out Vector3i pos)) continue;
            if (__instance.World.GetBlock(pos).type == packed) continue; // damage or rotation, not a placement
            (__state ??= new List<Vector3i>()).Add(pos);
        }
    }

    static void Postfix(GameManager __instance, PlatformUserIdentifierAbs persistentPlayerId, List<Vector3i> __state)
    {
        EntityPlayer changer = Packs.CurrentChanger;
        Packs.CurrentChanger = null;
        if (__state == null) return;
        foreach (Vector3i pos in __state)
        {
            try
            {
                BlockValue bv = __instance.World.GetBlock(pos);
                if (bv.type == Packs.PackedType) Packs.Unpack(pos, bv, changer);
            }
            catch (Exception e)
            {
                Log.Error($"{AzraelStorageBackpack.Tag} Unpack at {pos} failed: {e}");
            }
        }
    }

    static Exception Finalizer(Exception __exception)
    {
        Packs.CurrentChanger = null;
        return __exception;
    }
}

// A backpack replaced by something without storage (broken, picked away, exploded): pack it.
[HarmonyPatch(typeof(TEFeatureStorage), nameof(TEFeatureStorage.ReplacedBy))]
public static class Patch_StorageReplaced
{
    static void Prefix(TEFeatureStorage __instance, BlockValue _bvOld, TileEntity _teNew)
    {
        if (!Packs.IsServer || !Packs.IsOurs(_bvOld.type)) return;
        if (_teNew != null && _teNew.TryGetSelfOrFeature<TEFeatureStorage>(out _)) return; // still a container
        try
        {
            Packs.Pack(__instance, _bvOld);
        }
        catch (Exception e)
        {
            // If saving failed the grid is untouched and vanilla drops the contents as a loot bag. If it failed
            // after saving, the contents are in the record: sbp list / sbp give recover them.
            Log.Error($"{AzraelStorageBackpack.Tag} Pack failed: {e}");
        }
    }
}

// Admin recovery, for a packed backpack that was lost (despawned on the ground, server crash, and so on).
// The server log names every pack and unpack: "[StorageBackpack] Packed backpack at ... into <id> for <name>".
public class ConsoleCmdStorageBackpack : ConsoleCmdAbstract
{
    public override int DefaultPermissionLevel => 0;
    public override string[] getCommands() => new[] { "storagebackpack", "sbp" };
    public override string getDescription() => "Storage Backpack: list stored backpack contents, or give one back to a player";
    public override string getHelp() =>
        "sbp                      - list stored backpacks, newest first (active = still packed, used = unpacked in the last 7 days)\n" +
        "sbp give <player> <id>   - drop a packed backpack with that id at the player (name or entity id). A used id is made active again.\n" +
        "Ids are in the server log: [StorageBackpack] Packed backpack at ... into <id> for <name>.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        string arg = _params.Count > 0 ? _params[0].ToLowerInvariant() : "list";
        if (arg == "list")
        {
            var all = PackStore.List();
            if (all.Count == 0) { con.Output("No stored backpacks."); return; }
            int shown = 0;
            foreach (var r in all)
            {
                if (shown++ == 30) { con.Output($"... and {all.Count - 30} more."); break; }
                con.Output($"{r.id}  {(r.used ? "used  " : "active")}  {r.saved.ToLocalTime():yyyy-MM-dd HH:mm}");
            }
            return;
        }
        if (arg == "give" && _params.Count >= 3)
        {
            string id = _params[2].Trim().ToLowerInvariant();
            ClientInfo ci = ConsoleHelper.ParseParamIdOrName(_params[1]);
            EntityPlayer player = null;
            if (ci != null) GameManager.Instance.World.Players.dict.TryGetValue(ci.entityId, out player);
            else if (GameManager.Instance.myEntityPlayerLocal != null &&
                     (_params[1] == GameManager.Instance.myEntityPlayerLocal.entityId.ToString() ||
                      string.Equals(_params[1], GameManager.Instance.myEntityPlayerLocal.EntityName, StringComparison.OrdinalIgnoreCase)))
                player = GameManager.Instance.myEntityPlayerLocal;
            if (player == null) { con.Output("Player not found (must be online)."); return; }
            if (!PackStore.Reactivate(id)) { con.Output("No stored backpack with that id."); return; }
            ItemValue iv = Block.GetBlockValue(Packs.PackedName).ToItemValue();
            iv.SetMetadata(Packs.MetaKey, id);
            GameManager.Instance.ItemDropServer(new ItemStack(iv, 1), player.GetPosition() + new Vector3(0f, 0.5f, 0f), Vector3.zero, -1, Packs.DropLifetime);
            con.Output($"Dropped packed backpack {id} at {player.EntityName}.");
            Log.Out($"{AzraelStorageBackpack.Tag} Admin gave packed backpack {id} to {player.EntityName}.");
            return;
        }
        con.Output("Usage: sbp | sbp give <player> <id>");
    }
}
