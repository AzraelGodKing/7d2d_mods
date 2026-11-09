using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

// Repair Pad: a powered pressure plate that repairs every damaged block in the land claim it sits in, paid for
// from Repair Supply Chests in that same claim. Server only: the blocks reuse vanilla models and the server
// sends the XML to players.
//
// How it works in V3.4:
// - Stepping on it: the server already moves every player, so a postfix on EntityPlayer.OnUpdateEntity checks
//   the block under each player's feet and fires once when they step onto a pad (not every frame they stand on it).
// - The claim: the pad must sit inside an active land claim owned by the stepping player or one of their allies
//   (same Self/Ally rules as World.GetLandClaimOwner). The area is the claim block +/- (LandClaimSize-1)/2 on X/Z,
//   full height, the same square vanilla protects.
// - Scanning: one 4-block-high chunk layer per frame, so a whole claim never stalls the server.
// - Cost: materials copy vanilla's own claim repair (TEFeatureAreaRepair.repairBlock): each block's
//   RepairItems scaled by how damaged it is. Repair kits are a pool of hit points. "both" (default) runs a
//   materials pass first, then spends kits on whatever materials couldn't pay for.
// - Chests that a player has open are skipped, so nobody's open container gets out of sync.
public class AzraelRepairPad : IModApi
{
    public const string Tag = "[RepairPad]";

    public void InitMod(Mod _modInstance)
    {
        RpConfig.ModPath = _modInstance.Path;
        RpConfig.Load();
        var harmony = new Harmony("azrael.repairpad");
        foreach (Type t in typeof(AzraelRepairPad).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception e) { Log.Error(Tag + " BROKEN: patch " + t.Name + " failed (the game may have changed): " + e.Message); }
        }
        Log.Out(Tag + " Loaded. " + RpConfig.Describe() + " Players do not install this mod.");
    }
}

public enum RpCostMode { Materials, Kits, Both }

public static class RpConfig
{
    public static string ModPath = "";
    public static RpCostMode CostMode = RpCostMode.Both;
    public static int HpPerKit = 10000;
    public static string KitItem = "resourceRepairKit";
    public static float CooldownSeconds = 10f;
    public static bool RequirePower = true;
    public static int RepairsPerFrame = 100;
    public static bool LogRuns = true;

    static string SettingsPath => Path.Combine(ModPath, "settings.txt");

    public static void Load()
    {
        CostMode = RpCostMode.Both;
        HpPerKit = 10000;
        KitItem = "resourceRepairKit";
        CooldownSeconds = 10f;
        RequirePower = true;
        RepairsPerFrame = 100;
        LogRuns = true;
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Log.Warning(AzraelRepairPad.Tag + " settings.txt not found, using defaults.");
                return;
            }
            foreach (var raw in File.ReadAllLines(SettingsPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                string lower = value.ToLowerInvariant();
                switch (key)
                {
                    case "costmode":
                        if (lower == "both") CostMode = RpCostMode.Both;
                        else if (lower == "materials" || lower == "material") CostMode = RpCostMode.Materials;
                        else if (lower == "kits" || lower == "kit") CostMode = RpCostMode.Kits;
                        else Warn(key, value, "both, materials or kits");
                        break;
                    case "hpperkit":
                        if (int.TryParse(value, out int hp) && hp > 0) HpPerKit = hp; else Warn(key, value, "a whole number above 0");
                        break;
                    case "kititem":
                        if (value.Length > 0) KitItem = value;
                        break;
                    case "cooldownseconds":
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cd) && cd >= 0f) CooldownSeconds = cd;
                        else Warn(key, value, "a number of seconds");
                        break;
                    case "requirepower":
                        if (TryBool(lower, out bool rp)) RequirePower = rp; else Warn(key, value, "on or off");
                        break;
                    case "repairsperframe":
                        if (int.TryParse(value, out int rpf) && rpf > 0) RepairsPerFrame = rpf; else Warn(key, value, "a whole number above 0");
                        break;
                    case "logruns":
                        if (TryBool(lower, out bool lr)) LogRuns = lr; else Warn(key, value, "on or off");
                        break;
                    default:
                        Log.Warning($"{AzraelRepairPad.Tag} Unknown setting '{line.Substring(0, eq).Trim()}'.");
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelRepairPad.Tag} Could not read settings.txt: {e.Message}");
        }
    }

    static bool TryBool(string v, out bool b)
    {
        b = v == "on" || v == "true" || v == "1" || v == "yes";
        return b || v == "off" || v == "false" || v == "0" || v == "no";
    }

    static void Warn(string key, string value, string expected) =>
        Log.Warning($"{AzraelRepairPad.Tag} Ignoring {key}={value} (use {expected}).");

    public static string Describe()
    {
        string kits = $"repair kits ({KitItem}, {HpPerKit} HP each)";
        string cost = CostMode == RpCostMode.Kits ? "Cost: " + kits + "."
            : CostMode == RpCostMode.Materials ? "Cost: materials, same as a repair hammer."
            : "Cost: materials first (same as a repair hammer), then " + kits + " for the rest.";
        return cost + $" Cooldown {CooldownSeconds:0.#}s. Power {(RequirePower ? "required" : "not required")}.";
    }
}

public static class RepairPad
{
    public const string PadName = "azrRepairPad";
    public const string ChestName = "azrRepairPadChest";

    static int padType = -2;
    static int chestType = -2;

    // Players currently standing on a pad (so the pad fires once per step, not every frame).
    static readonly HashSet<int> onPad = new HashSet<int>();
    // Pads running right now, and when each last finished.
    static readonly HashSet<Vector3i> running = new HashSet<Vector3i>();
    static readonly Dictionary<Vector3i, float> lastRun = new Dictionary<Vector3i, float>();
    // Kits mode: hit points left over from a kit, kept per pad so a partly used kit isn't wasted.
    static readonly Dictionary<Vector3i, int> hpBank = new Dictionary<Vector3i, int>();

    public static int PadType
    {
        get
        {
            if (padType == -2) padType = ResolveType(PadName);
            return padType;
        }
    }

    public static int ChestType
    {
        get
        {
            if (chestType == -2) chestType = ResolveType(ChestName);
            return chestType;
        }
    }

    static int ResolveType(string name)
    {
        Block b = Block.GetBlockByName(name);
        if (b == null)
        {
            Log.Warning($"{AzraelRepairPad.Tag} Block {name} not found (XML missing?). The pad does nothing.");
            return -1;
        }
        return b.blockID;
    }

    // Called every server tick for every living player. Cheap: two block reads.
    public static void CheckPlayer(EntityPlayer player)
    {
        int pad = PadType;
        if (pad < 0) return;
        World world = player.world;
        Vector3i feet = player.GetBlockPosition();
        Vector3i padPos = Vector3i.zero;
        bool found = false;
        for (int dy = 0; dy >= -1; dy--)
        {
            var p = new Vector3i(feet.x, feet.y + dy, feet.z);
            if (world.GetBlock(p).type == pad)
            {
                padPos = p;
                found = true;
                break;
            }
        }
        if (!found)
        {
            onPad.Remove(player.entityId);
            return;
        }
        if (!onPad.Add(player.entityId)) return; // still standing on it from before
        TryStart(world, player, padPos);
    }

    public static void Forget(int entityId) => onPad.Remove(entityId);

    static void TryStart(World world, EntityPlayer player, Vector3i padPos)
    {
        if (running.Contains(padPos))
        {
            Tell(player, "This pad is already repairing.");
            return;
        }
        if (lastRun.TryGetValue(padPos, out float last))
        {
            float wait = RpConfig.CooldownSeconds - (Time.time - last);
            if (wait > 0f)
            {
                Tell(player, $"The pad is recharging, try again in {Mathf.CeilToInt(wait)}s.");
                return;
            }
        }
        if (RpConfig.RequirePower)
        {
            var te = world.GetTileEntity(padPos) as TileEntityPowered;
            if (te == null || !te.IsPowered)
            {
                Tell(player, "The pad has no power.");
                return;
            }
        }

        PersistentPlayerData ppd = GameManager.Instance.persistentPlayers?.GetPlayerDataFromEntityID(player.entityId);
        if (ppd == null || !FindClaim(world, padPos, ppd, out Vector3i claimPos, out int half))
        {
            Tell(player, "The pad only works inside your land claim or an ally's.");
            return;
        }

        running.Add(padPos);
        GameManager.Instance.StartCoroutine(Guard(padPos, Repair(world, player.entityId, padPos, claimPos, half)));
    }

    // Finds the active land claim (owned by this player or an ally) whose area holds the pad. Nearest wins.
    static bool FindClaim(World world, Vector3i padPos, PersistentPlayerData ppd, out Vector3i claimPos, out int half)
    {
        claimPos = Vector3i.zero;
        half = Math.Max(0, (GameStats.GetInt(EnumGameStats.LandClaimSize) - 1) / 2);
        var map = GameManager.Instance.persistentPlayers?.m_lpBlockMap;
        if (map == null) return false;
        bool found = false;
        int best = int.MaxValue;
        foreach (var kv in map)
        {
            Vector3i lcb = kv.Key;
            PersistentPlayerData owner = kv.Value;
            int dx = Math.Abs(lcb.x - padPos.x), dz = Math.Abs(lcb.z - padPos.z);
            if (dx > half || dz > half || owner == null) continue;
            if (owner != ppd && !owner.IsAlly(ppd)) continue;
            if (!world.IsLandProtectionValidForPlayer(owner)) continue;
            TEFeatureLandClaim claim = world.GetTileEntity(lcb)?.GetSelfOrFeature<TEFeatureLandClaim>();
            if (claim == null || !claim.IsPrimary()) continue;
            int d = dx * dx + dz * dz;
            if (d < best)
            {
                best = d;
                claimPos = lcb;
                found = true;
            }
        }
        return found;
    }

    // Runs a repair and always clears the "running" flag, even if something throws.
    static IEnumerator Guard(Vector3i padPos, IEnumerator inner)
    {
        while (true)
        {
            bool more;
            try { more = inner.MoveNext(); }
            catch (Exception e)
            {
                Log.Error($"{AzraelRepairPad.Tag} Repair at {padPos} failed: {e}");
                more = false;
            }
            if (!more) break;
            yield return inner.Current;
        }
        running.Remove(padPos);
        lastRun[padPos] = Time.time;
    }

    struct Damaged
    {
        public Vector3i Pos;
        public int Type;
        public float Fraction;
    }

    sealed class Supply
    {
        public TileEntityComposite Te;
        public TEFeatureStorage Storage;
        public bool Changed;
    }

    static IEnumerator Repair(World world, int playerId, Vector3i padPos, Vector3i claimPos, int half)
    {
        int minX = claimPos.x - half, maxX = claimPos.x + half;
        int minZ = claimPos.z - half, maxZ = claimPos.z + half;
        int chestT = ChestType;

        var damaged = new List<Damaged>();
        var chests = new List<Vector3i>();

        // 1. Scan the claim, one chunk layer per frame.
        for (int cx = World.toChunkXZ(minX); cx <= World.toChunkXZ(maxX); cx++)
        {
            for (int cz = World.toChunkXZ(minZ); cz <= World.toChunkXZ(maxZ); cz++)
            {
                if (!(world.GetChunkSync(cx, cz) is Chunk chunk)) continue;
                Vector3i origin = chunk.worldPosIMin;
                ChunkBlockLayer.LoopBlocksDelegate visit = (x, y, z, bv) =>
                {
                    int wx = origin.x + x, wz = origin.z + z;
                    if (wx < minX || wx > maxX || wz < minZ || wz > maxZ) return;
                    if (bv.type == chestT) chests.Add(new Vector3i(wx, origin.y + y, wz));
                    if (bv.damage <= 0 || bv.isair) return;
                    Block block = bv.Block;
                    if (block == null || block.MaxDamage <= 0) return;
                    if (block.RepairItems == null && block.RepairItemsMeshDamage == null) return; // a hammer can't fix it either
                    damaged.Add(new Damaged
                    {
                        Pos = new Vector3i(wx, origin.y + y, wz),
                        Type = bv.type,
                        Fraction = (float)bv.damage / block.MaxDamage
                    });
                };
                ChunkBlockLayer[] layers = chunk.m_BlockLayers;
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i] == null) continue;
                    layers[i].LoopOverAllBlocks(chunk, i << 2, visit);
                    yield return null;
                }
            }
        }

        EntityPlayer player = world.GetEntity(playerId) as EntityPlayer;

        if (damaged.Count == 0)
        {
            Tell(player, "Nothing in this claim needs repairing.");
            yield break;
        }

        // 2. Gather the supply chests (closed ones only).
        var supplies = new List<Supply>();
        int openChests = 0;
        foreach (Vector3i pos in chests)
        {
            if (!(world.GetTileEntity(pos) is TileEntityComposite te)) continue;
            TEFeatureStorage storage = te.GetFeature<TEFeatureStorage>();
            if (storage == null) continue;
            if (LockManager.Instance != null && LockManager.Instance.IsLockedServer(te))
            {
                openChests++;
                continue;
            }
            supplies.Add(new Supply { Te = te, Storage = storage });
        }
        if (supplies.Count == 0)
        {
            Tell(player, chests.Count == 0
                ? $"{damaged.Count} blocks need repair, but there is no Repair Supply Chest in this claim."
                : $"{damaged.Count} blocks need repair, but every Repair Supply Chest is open. Close them and step on the pad again.");
            yield break;
        }

        // 3. Repair, most damaged first. Materials pass first, then repair kits for whatever is left.
        damaged.Sort((a, b) => b.Fraction.CompareTo(a.Fraction));
        bool useMaterials = RpConfig.CostMode != RpCostMode.Kits;
        bool useKits = RpConfig.CostMode != RpCostMode.Materials;
        ItemValue kit = null;
        if (useKits)
        {
            kit = ItemClass.GetItem(RpConfig.KitItem);
            if (kit == null || kit.IsEmpty() || kit.ItemClass == null)
            {
                Log.Warning($"{AzraelRepairPad.Tag} KitItem '{RpConfig.KitItem}' is not an item. Check settings.txt.");
                if (!useMaterials)
                {
                    Tell(player, "The pad is set up wrong (unknown repair kit item). Tell an admin.");
                    yield break;
                }
                useKits = false;
            }
        }
        hpBank.TryGetValue(padPos, out int bank);

        int repaired = 0, partial = 0, hpRestored = 0, kitsUsed = 0, sinceYield = 0;
        var used = new Dictionary<string, int>();
        var unpaid = new List<Damaged>();

        // 3a. Materials: the same cost vanilla's land claim repair charges. All or nothing per block.
        foreach (Damaged d in damaged)
        {
            if (++sinceYield >= RpConfig.RepairsPerFrame)
            {
                sinceYield = 0;
                yield return null;
            }
            BlockValue bv = world.GetBlock(d.Pos);
            if (bv.type != d.Type || bv.damage <= 0) continue; // changed since the scan
            if (!useMaterials) { unpaid.Add(d); continue; }
            List<RpCost> cost = MaterialCost(bv, out int amount);
            if (cost == null || !cost.TrueForAll(c => Count(supplies, c.Item) >= c.Count)) { unpaid.Add(d); continue; }
            foreach (RpCost c in cost)
            {
                Take(supplies, c.Item, c.Count);
                used.TryGetValue(c.Name, out int had);
                used[c.Name] = had + c.Count;
            }
            bv.Block.DamageBlock(world, new BlockValueRef(d.Pos), bv, -amount, -1);
            hpRestored += amount;
            repaired++;
        }

        // 3b. Repair kits: a pool of hit points for the blocks materials couldn't pay for.
        if (useKits)
        {
            foreach (Damaged d in unpaid)
            {
                if (++sinceYield >= RpConfig.RepairsPerFrame)
                {
                    sinceYield = 0;
                    yield return null;
                }
                BlockValue bv = world.GetBlock(d.Pos);
                if (bv.type != d.Type || bv.damage <= 0) continue;
                int need = bv.damage;
                while (bank < need)
                {
                    if (!Take(supplies, kit, 1)) break;
                    bank += RpConfig.HpPerKit;
                    kitsUsed++;
                }
                int heal = Math.Min(need, bank);
                if (heal <= 0) break; // out of kits
                bank -= heal;
                bv.Block.DamageBlock(world, new BlockValueRef(d.Pos), bv, -heal, -1);
                hpRestored += heal;
                if (heal < need) partial++; else repaired++;
            }
            hpBank[padPos] = bank;
        }

        foreach (Supply s in supplies)
            if (s.Changed) s.Te.SetModified();

        // 3c. What is still damaged, and what it would take to finish.
        int left = 0, hpLeft = 0;
        var stillNeed = new Dictionary<string, (ItemValue item, int count)>();
        foreach (Damaged d in unpaid)
        {
            BlockValue bv = world.GetBlock(d.Pos);
            if (bv.type != d.Type || bv.damage <= 0) continue;
            left++;
            hpLeft += bv.damage;
            List<RpCost> cost = MaterialCost(bv, out _);
            if (cost == null) continue;
            foreach (RpCost c in cost)
            {
                stillNeed.TryGetValue(c.Name, out var n);
                stillNeed[c.Name] = (c.Item, n.count + c.Count);
            }
        }
        var missing = new List<string>();
        if (useMaterials)
        {
            foreach (var kv in stillNeed.OrderByDescending(kv => kv.Value.count))
            {
                int short_ = kv.Value.count - Count(supplies, kv.Value.item);
                if (short_ > 0) missing.Add($"{short_} {Localization.Get(kv.Key)}");
            }
        }
        int kitsMissing = useKits && hpLeft > bank ? (hpLeft - bank + RpConfig.HpPerKit - 1) / RpConfig.HpPerKit : 0;

        // 4. Report.
        player = world.GetEntity(playerId) as EntityPlayer;
        var sb = new StringBuilder();
        if (repaired + partial == 0)
            sb.Append($"Not enough supplies to repair any of the {damaged.Count} damaged blocks.");
        else
        {
            sb.Append($"Repaired {repaired} block{(repaired == 1 ? "" : "s")}");
            if (partial > 0) sb.Append($" ({partial} partly)");
            var usedParts = used.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {Localization.Get(kv.Key)}").ToList();
            if (kitsUsed > 0) usedParts.Add($"{kitsUsed} {Localization.Get(RpConfig.KitItem)}");
            sb.Append(". Used " + string.Join(", ", usedParts) + ".");
        }
        if (left > 0)
        {
            sb.Append($" {left} block{(left == 1 ? "" : "s")} still need repair. Missing: ");
            var parts = new List<string>();
            if (missing.Count > 0)
            {
                const int maxShown = 8;
                string mats = string.Join(", ", missing.Take(maxShown));
                if (missing.Count > maxShown) mats += $" and {missing.Count - maxShown} more";
                parts.Add(mats);
            }
            if (kitsMissing > 0) parts.Add($"{kitsMissing} {Localization.Get(RpConfig.KitItem)}");
            sb.Append(parts.Count == 0 ? "nothing the pad can use." : string.Join(parts.Count == 2 && missing.Count > 0 ? ", or " : ", ", parts) + ".");
        }
        if (openChests > 0) sb.Append($" Skipped {openChests} open chest{(openChests == 1 ? "" : "s")}.");
        Tell(player, sb.ToString());

        if (RpConfig.LogRuns)
            Log.Out($"{AzraelRepairPad.Tag} Pad {padPos} (claim {claimPos}) for {player?.EntityName ?? playerId.ToString()}: " +
                    $"{repaired} repaired, {partial} partly, {left} left, {hpRestored} HP, " +
                    $"items {string.Join(", ", used.Select(kv => kv.Key + " x" + kv.Value))}, {kitsUsed} kits, " +
                    $"{supplies.Count} chest(s), {openChests} open.");
    }

    struct RpCost
    {
        public ItemValue Item;
        public int Count;
        public string Name;
    }

    // What a repair hammer would charge to fully repair this block (vanilla TEFeatureAreaRepair.repairBlock).
    // Null if the block has no repair items or one of them isn't a real item.
    static List<RpCost> MaterialCost(BlockValue bv, out int amount)
    {
        Block block = bv.Block;
        amount = bv.damage;
        float fraction = (float)bv.damage / block.MaxDamage;
        List<Block.SItemNameCount> items = block.RepairItems;
        if (block.RepairItemsMeshDamage != null && block.shape.UseRepairDamageState(bv))
        {
            amount = 1;
            fraction = 1f;
            items = block.RepairItemsMeshDamage;
        }
        if (items == null || items.Count == 0) return null;
        var cost = new List<RpCost>(items.Count);
        foreach (Block.SItemNameCount ic in items)
        {
            ItemValue iv = ItemClass.GetItem(ic.ItemName);
            if (iv == null || iv.ItemClass == null) return null;
            cost.Add(new RpCost { Item = iv, Count = Math.Max((int)(ic.Count * fraction * block.ResourceScale), 1), Name = ic.ItemName });
        }
        return cost;
    }

    static int Count(List<Supply> supplies, ItemValue item)
    {
        int n = 0;
        foreach (Supply s in supplies) n += s.Storage.CountItem(item.ItemClass);
        return n;
    }

    static bool Take(List<Supply> supplies, ItemValue item, int count)
    {
        if (Count(supplies, item) < count) return false;
        foreach (Supply s in supplies)
        {
            if (count <= 0) break;
            int have = s.Storage.CountItem(item.ItemClass);
            if (have <= 0) continue;
            count -= s.Storage.RemoveItems(item, Math.Min(have, count));
            s.Changed = true;
        }
        return true;
    }

    public static void Tell(EntityPlayer player, string msg)
    {
        if (player == null) return;
        try
        {
            GameManager.Instance.ChatMessageServer(null, EChatType.Whisper, -1, "[60d080][Repair Pad][-] " + msg,
                new List<int> { player.entityId }, EMessageSender.None);
        }
        catch (Exception e) { Log.Warning($"{AzraelRepairPad.Tag} chat: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.OnUpdateEntity))]
public static class RpPlayerUpdatePatch
{
    public static void Postfix(EntityPlayer __instance)
    {
        try
        {
            if (__instance == null || __instance.world == null) return;
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;
            if (!__instance.IsAlive())
            {
                RepairPad.Forget(__instance.entityId);
                return;
            }
            RepairPad.CheckPlayer(__instance);
        }
        catch (Exception e) { Log.Warning($"{AzraelRepairPad.Tag} player check: {e.Message}"); }
    }
}

public class ConsoleCmdRepairPad : ConsoleCmdAbstract
{
    public override int DefaultPermissionLevel => 0;
    public override string[] getCommands() => new[] { "rpad", "repairpad" };
    public override string getDescription() => "Repair Pad: show or reload the settings";
    public override string getHelp() =>
        "rpad          - show how the Repair Pad is set up\n" +
        "rpad reload   - re-read settings.txt (takes effect right away)";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        if (_params.Count > 0 && _params[0].Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            RpConfig.Load();
            con.Output("Repair Pad reloaded. " + RpConfig.Describe());
            return;
        }
        con.Output(RpConfig.Describe());
    }
}
