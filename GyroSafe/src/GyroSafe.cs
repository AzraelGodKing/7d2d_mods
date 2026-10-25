using System;
using System.Collections.Generic;
using HarmonyLib;

// Gyro Safe: a gyrocopter no longer damages blocks or terrain when it hits them.
// Block damage in 7 Days to Die is worked out on the machine running the vehicle's physics (the driver's game, or the host)
// and then sent to the server as block changes. So:
//  - installed where the gyro is driven (client or host) it stops the damage at the source;
//  - installed on the server it also drops any block change the game credits to a gyro, as a safety net.
public class AzraelGyroSafeMod : IModApi
{
    public void InitMod(Mod mod)
    {
        var harmony = new Harmony("azrael.gyrosafe");
        foreach (Type t in typeof(AzraelGyroSafeMod).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception e) { Log.Error("[GyroSafe] BROKEN: patch " + t.Name + " failed (the game may have changed): " + e.Message); }
        }
        Log.Out("[GyroSafe] Loaded: gyrocopters no longer damage blocks.");
    }

    public static bool IsGyro(int entityId)
    {
        try
        {
            var w = GameManager.Instance != null ? GameManager.Instance.World : null;
            var v = w != null ? w.GetEntity(entityId) as EntityVehicle : null;
            if (v == null) return false;
            string n = EntityClass.list[v.entityClass].entityClassName;
            return n != null && n.IndexOf("gyro", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }
}

// The vehicle's collision code calls ItemActionAttack.Hit with the vehicle as attacker. "Simulate" mode skips the block damage.
[HarmonyPatch(typeof(ItemActionAttack), "Hit")]
public static class GyroHitPatch
{
    static void Prefix(int _attackerEntityId, ref ItemActionAttack.EnumAttackMode _attackMode)
    {
        try { if (AzraelGyroSafeMod.IsGyro(_attackerEntityId)) _attackMode = ItemActionAttack.EnumAttackMode.Simulate; }
        catch (Exception e) { Log.Warning("[GyroSafe] hit patch: " + e.Message); }
    }
}

// Server safety net: block changes credited to a gyro are not applied or passed on.
[HarmonyPatch(typeof(NetPackageSetBlock), "ProcessPackage")]
public static class GyroSetBlockPatch
{
    static void Prefix(NetPackageSetBlock __instance)
    {
        try
        {
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;
            var list = Traverse.Create(__instance).Field("blockChanges").GetValue<List<BlockChangeInfo>>();
            if (list == null) return;
            list.RemoveAll(c => c != null && c.changedByEntityId >= 0 && AzraelGyroSafeMod.IsGyro(c.changedByEntityId));
        }
        catch (Exception e) { Log.Warning("[GyroSafe] set-block patch: " + e.Message); }
    }
}
