using System;
using GameEvent.SequenceActions;
using UnityEngine;

// Honk Door Fix (code version): fixes the root cause instead of restricting the event.
//
// ActionBaseBlockAction.OnPerformAction scans startPoint + [minOffset..maxOffset] and skips y < 0,
// but never checks the top of the world (255), so any block-scan game event started near the top
// (e.g. honk_trader_doors at Y 253+) calls Chunk.GetBlockNoDamage out of range and throws every tick.
// This trims the scan to the world height; honking behaves exactly as vanilla everywhere else.

public class HonkDoorFixCodeMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        new HarmonyLib.Harmony("azrael.honkdoorfixcode").PatchAll(typeof(HonkDoorFixCodeMod).Assembly);
        Log.Out("[HonkDoorFix] Loaded: block-scan game events no longer read above the world height limit.");
    }
}

[HarmonyLib.HarmonyPatch(typeof(ActionBaseBlockAction), nameof(ActionBaseBlockAction.OnPerformAction))]
public static class HonkDoorFixHeightGuard
{
    const int TopY = 255; // highest block row in a chunk (0-255)
    static float lastLog = -999f;

    static bool Prefix(ActionBaseBlockAction __instance, ref BaseAction.ActionCompleteStates __result, out int __state)
    {
        __state = int.MinValue;
        try
        {
            var owner = __instance.Owner;
            if (owner == null) return true;
            Vector3 start = owner.TargetPosition.y != 0f ? owner.TargetPosition
                          : owner.Target != null ? owner.Target.position : Vector3.zero;
            int minY = __instance.minOffset.y, maxY = __instance.maxOffset.y;
            if (Utils.Fastfloor(start.y + maxY) <= TopY) return true; // normal case: untouched

            if (Utils.Fastfloor(start.y + minY) > TopY)
            {
                // Whole scan is above the world: nothing there to change. Same result vanilla gives
                // when no matching blocks are found, so the event ends instead of throwing.
                Note($"skipped a '{owner.Name}' block scan at Y {start.y:0.#} (above the world)");
                __result = BaseAction.ActionCompleteStates.InCompleteRefund;
                return false;
            }

            // Partly above: scan only the rows that exist.
            __state = maxY;
            while (maxY > minY && Utils.Fastfloor(start.y + maxY) > TopY) maxY--;
            __instance.maxOffset = new Vector3i(__instance.maxOffset.x, maxY, __instance.maxOffset.z);
            Note($"trimmed a '{owner.Name}' block scan at Y {start.y:0.#} to the world height");
        }
        catch (Exception e) { Log.Warning("[HonkDoorFix] " + e.Message); }
        return true;
    }

    // Restore the offset, and as a safety net stop any remaining out-of-range read from looping forever.
    static Exception Finalizer(ActionBaseBlockAction __instance, int __state, Exception __exception,
                               ref BaseAction.ActionCompleteStates __result)
    {
        if (__state != int.MinValue)
            __instance.maxOffset = new Vector3i(__instance.maxOffset.x, __state, __instance.maxOffset.z);
        if (__exception is IndexOutOfRangeException)
        {
            Note("stopped a block-scan game event that read outside the world (" + __exception.Message + ")");
            __result = BaseAction.ActionCompleteStates.InCompleteRefund;
            return null;
        }
        return __exception;
    }

    static void Note(string msg)
    {
        if (Time.realtimeSinceStartup - lastLog < 5f) return; // don't flood the log
        lastLog = Time.realtimeSinceStartup;
        Log.Out("[HonkDoorFix] " + msg);
    }
}
