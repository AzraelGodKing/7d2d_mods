using System;
using HarmonyLib;
using UnityEngine;

// Fixes "ghost players" on dedicated servers.
//
// Vanilla bug: when a player logs out, QuestEventManager.HandlePlayerDisconnect calls
// Quest.HandleUnlockPOI for every in-progress quest, which calls QuestUnlockPOI(entityId, pos).
// QuestUnlockPOI looks up the POI at that position and dereferences it without a null check.
// If a quest's saved POI position does not match any POI, it throws a NullReferenceException,
// ConnectionManager.DisconnectClient aborts halfway, and the player is never removed:
// they stay in the world, the server keeps pinging them, and rejoining fails with
// "Duplicate player ID" until the server restarts.
public class AzraelQuestDisconnectFix : IModApi
{
    public const string Tag = "[QuestDisconnectFix]";

    public void InitMod(Mod _modInstance)
    {
        var harmony = new Harmony("azrael.questdisconnectfix");
        harmony.PatchAll(typeof(AzraelQuestDisconnectFix).Assembly);
        Log.Out($"{Tag} Loaded. Patched QuestEventManager.QuestUnlockPOI and QuestEventManager.HandlePlayerDisconnect.");
    }
}

// Root-cause fix: skip the unlock when there is no POI at the quest's saved position.
// Unlocking a POI that does not exist has nothing to do, so skipping is the same outcome
// the original code intended, minus the crash. Also covers the client-requested unlock
// path (NetPackageQuestEvent), which hit the same exception in Kats's logs.
[HarmonyPatch(typeof(QuestEventManager), nameof(QuestEventManager.QuestUnlockPOI))]
public static class Patch_QuestUnlockPOI
{
    public static bool Prefix(int entityID, Vector3 prefabPos)
    {
        try
        {
            var decorator = GameManager.Instance?.GetDynamicPrefabDecorator();
            if (decorator == null)
            {
                Log.Warning($"{AzraelQuestDisconnectFix.Tag} Skipped POI unlock for entity {entityID}: prefab decorator not available.");
                return false;
            }
            var prefab = decorator.GetPrefabFromWorldPos((int)prefabPos.x, (int)prefabPos.z);
            if (prefab == null)
            {
                Log.Warning($"{AzraelQuestDisconnectFix.Tag} Skipped POI unlock for entity {entityID}: no POI at {prefabPos}. (This used to crash the logout.)");
                return false;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelQuestDisconnectFix.Tag} Skipped POI unlock for entity {entityID}: lookup failed ({e.GetType().Name}: {e.Message}).");
            return false;
        }
        return true; // POI exists: run the original unlock.
    }
}

// Safety net: if anything else in the quest cleanup throws during a logout, log it and let
// the logout continue instead of leaving a ghost player behind.
[HarmonyPatch(typeof(QuestEventManager), nameof(QuestEventManager.HandlePlayerDisconnect))]
public static class Patch_HandlePlayerDisconnect
{
    public static Exception Finalizer(Exception __exception, EntityPlayer player)
    {
        if (__exception != null)
        {
            Log.Warning($"{AzraelQuestDisconnectFix.Tag} Quest cleanup failed for entity {(player != null ? player.entityId.ToString() : "?")} " +
                        $"({__exception.GetType().Name}: {__exception.Message}). Continuing the logout so no ghost player is left behind.");
        }
        return null; // swallow so ConnectionManager.DisconnectClient can finish
    }
}
