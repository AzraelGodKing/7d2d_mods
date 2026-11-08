using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;

// Turret Claim Guard: a player's turrets stop damaging blocks inside land claims (by default the owner's
// claim and their allies'). Server only.
//
// How turrets hurt blocks in V3.4: every turret shot ends in ItemActionAttack.Hit with a block damage value
// (autoTurret BlockDamage=1, x2 on wood; junk turret ammo 1; junk sledge 17+). Turrets only fire on the server
// (AutoTurretFireController.Fire, MiniTurretFireController.Fire, JunkSledgeFireController.hitTarget all check
// ConnectionManager.IsServer), so a server-side prefix on Hit sees every automatic shot.
//
// Telling a turret shot apart:
// - Junk turret and junk sledge pass their own entity id as ownedEntityId, so the entity is an EntityTurret
//   and EntityTurret.OwnerID is the owner.
// - Block turrets pass -2, so AutoTurretFireController.Fire is wrapped to remember which turret is firing
//   (its TileEntity owner) while it runs.
// The claim check is the game's own World.GetLandClaimOwner, which answers Self/Ally/Other/None for a block
// relative to a player, so expiry and the ally list work the same as vanilla land claims.
public class AzraelTurretClaimGuard : IModApi
{
    public const string Tag = "[TurretClaimGuard]";

    public void InitMod(Mod _modInstance)
    {
        TcgConfig.ModPath = _modInstance.Path;
        TcgConfig.Load();
        var harmony = new Harmony("azrael.turretclaimguard");
        foreach (Type t in typeof(AzraelTurretClaimGuard).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception e) { Log.Error(Tag + " BROKEN: patch " + t.Name + " failed (the game may have changed): " + e.Message); }
        }
        Log.Out(Tag + " Loaded. " + TcgConfig.Describe() + " Players do not install this mod.");
    }
}

public enum TcgProtect { Off, Own, Allies, Any }

public static class TcgConfig
{
    public static string ModPath = "";
    public static TcgProtect Protect = TcgProtect.Allies;
    public static bool BlockTurrets = true;
    public static bool JunkTurrets = true;
    public static bool LogBlocked = false;

    static string SettingsPath => Path.Combine(ModPath, "settings.txt");

    public static void Load()
    {
        Protect = TcgProtect.Allies;
        BlockTurrets = true;
        JunkTurrets = true;
        LogBlocked = false;
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Log.Warning(AzraelTurretClaimGuard.Tag + " settings.txt not found, using defaults (Protect=allies).");
                return;
            }
            foreach (var raw in File.ReadAllLines(SettingsPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim().ToLowerInvariant();
                if (key.Equals("Protect", StringComparison.OrdinalIgnoreCase))
                {
                    switch (value)
                    {
                        case "off": Protect = TcgProtect.Off; break;
                        case "own": Protect = TcgProtect.Own; break;
                        case "allies": Protect = TcgProtect.Allies; break;
                        case "any": Protect = TcgProtect.Any; break;
                        default: Log.Warning($"{AzraelTurretClaimGuard.Tag} Ignoring Protect={value} (use own, allies, any or off)."); break;
                    }
                    continue;
                }
                bool on = value == "on" || value == "true" || value == "1";
                bool off = value == "off" || value == "false" || value == "0";
                if (!on && !off) { Log.Warning($"{AzraelTurretClaimGuard.Tag} Ignoring {key}={value} (use on or off)."); continue; }
                if (key.Equals("BlockTurrets", StringComparison.OrdinalIgnoreCase)) BlockTurrets = on;
                else if (key.Equals("JunkTurrets", StringComparison.OrdinalIgnoreCase)) JunkTurrets = on;
                else if (key.Equals("LogBlocked", StringComparison.OrdinalIgnoreCase)) LogBlocked = on;
                else Log.Warning($"{AzraelTurretClaimGuard.Tag} Unknown setting '{key}'.");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelTurretClaimGuard.Tag} Could not read settings.txt: {e.Message}");
        }
    }

    public static string Describe()
    {
        string where;
        switch (Protect)
        {
            case TcgProtect.Off: return "Protect=off: turrets damage blocks as in vanilla.";
            case TcgProtect.Own: where = "the owner's own land claim"; break;
            case TcgProtect.Any: where = "any active land claim"; break;
            default: where = "the owner's land claim and their allies' claims"; break;
        }
        var kinds = new List<string>();
        if (BlockTurrets) kinds.Add("auto/shotgun/M60 turrets");
        if (JunkTurrets) kinds.Add("junk turrets and sledges");
        if (kinds.Count == 0) return "No turret types are on (BlockTurrets and JunkTurrets are off).";
        return "Turret block damage is blocked inside " + where + " for " + string.Join(" and ", kinds) + ".";
    }
}

public static class TurretGuard
{
    // Set while a block turret (AutoTurretFireController) is firing. Turrets fire on the main thread.
    public static bool BlockTurretFiring;
    public static PlatformUserIdentifierAbs BlockTurretOwner;

    // Called for every ItemActionAttack.Hit. Returns true if this is a turret hitting a protected block.
    public static bool ShouldBlock(WorldRayHitInfo _hit, int _ownedEntityId, out string _kind)
    {
        _kind = null;
        if (TcgConfig.Protect == TcgProtect.Off || _hit == null) return false;
        if (!GameUtils.IsBlockOrTerrain(_hit.tag)) return false;

        PlatformUserIdentifierAbs owner;
        if (BlockTurretFiring)
        {
            if (!TcgConfig.BlockTurrets) return false;
            owner = BlockTurretOwner;
            _kind = "turret";
        }
        else
        {
            if (!TcgConfig.JunkTurrets || _ownedEntityId < 0) return false;
            var world = GameManager.Instance?.World;
            if (world == null || !(world.GetEntity(_ownedEntityId) is EntityTurret junk)) return false;
            owner = junk.OwnerID;
            _kind = "junk turret";
        }
        return IsProtected(_hit.hit.blockPos, owner);
    }

    static bool IsProtected(Vector3i _pos, PlatformUserIdentifierAbs _owner)
    {
        var world = GameManager.Instance?.World;
        if (world == null) return false;
        if (TcgConfig.Protect == TcgProtect.Any)
            return world.GetLandClaimOwner(_pos, null) != EnumLandClaimOwner.None;

        PersistentPlayerData ownerData = _owner == null ? null : GameManager.Instance.GetPersistentPlayerList()?.GetPlayerData(_owner);
        if (ownerData == null) return false; // unowned turret: nothing to compare the claim with
        EnumLandClaimOwner who = world.GetLandClaimOwner(_pos, ownerData);
        if (who == EnumLandClaimOwner.Self) return true;
        return who == EnumLandClaimOwner.Ally && TcgConfig.Protect == TcgProtect.Allies;
    }
}

[HarmonyPatch(typeof(AutoTurretFireController), nameof(AutoTurretFireController.Fire))]
public static class TcgAutoTurretFirePatch
{
    static void Prefix(AutoTurretFireController __instance)
    {
        TurretGuard.BlockTurretFiring = true;
        TurretGuard.BlockTurretOwner = __instance.TileEntity?.GetOwner();
    }

    static Exception Finalizer(Exception __exception)
    {
        TurretGuard.BlockTurretFiring = false;
        TurretGuard.BlockTurretOwner = null;
        return __exception;
    }
}

[HarmonyPatch(typeof(ItemActionAttack), nameof(ItemActionAttack.Hit))]
public static class TcgHitPatch
{
    static void Prefix(WorldRayHitInfo hitInfo, ref float _blockDamage, int ownedEntityId)
    {
        if (_blockDamage <= 0f) return;
        try
        {
            if (!TurretGuard.ShouldBlock(hitInfo, ownedEntityId, out string kind)) return;
            if (TcgConfig.LogBlocked)
                Log.Out($"{AzraelTurretClaimGuard.Tag} Blocked {_blockDamage:0.#} {kind} damage to {hitInfo.hit.blockValue.Block?.GetBlockName()} at {hitInfo.hit.blockPos}.");
            _blockDamage = 0f;
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelTurretClaimGuard.Tag} Claim check failed, leaving the hit alone: {e.Message}");
        }
    }
}

public class ConsoleCmdTcg : ConsoleCmdAbstract
{
    public override int DefaultPermissionLevel => 0;
    public override string[] getCommands() => new[] { "tcg", "turretguard" };
    public override string getDescription() => "Turret Claim Guard: show or reload the settings";
    public override string getHelp() =>
        "tcg          - show what turrets are kept from damaging blocks\n" +
        "tcg reload   - re-read settings.txt (takes effect right away)";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        if (_params.Count > 0 && _params[0].Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            TcgConfig.Load();
            con.Output("Turret Claim Guard reloaded. " + TcgConfig.Describe());
            return;
        }
        con.Output(TcgConfig.Describe());
    }
}
