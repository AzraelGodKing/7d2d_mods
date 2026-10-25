using HarmonyLib;

// Turrets, mini turrets and motion sensors run their aiming on every client too, so a thrall (yours or anyone's) would
// still be tracked and shot at on screen even though the server ignores it. The server sends the thrall entity ids.
[HarmonyPatch(typeof(AutoTurretFireController), nameof(AutoTurretFireController.shouldIgnoreTarget))]
public static class ClientAutoTurretIgnore
{
    static void Postfix(Entity _target, ref bool __result)
    {
        if (!__result && _target != null && BaitData.All.Contains(_target.entityId)) __result = true;
    }
}

[HarmonyPatch(typeof(MiniTurretFireController), nameof(MiniTurretFireController.shouldIgnoreTarget))]
public static class ClientMiniTurretIgnore
{
    static void Postfix(Entity _target, ref bool __result)
    {
        if (!__result && _target != null && BaitData.All.Contains(_target.entityId)) __result = true;
    }
}

[HarmonyPatch(typeof(MotionSensorController), nameof(MotionSensorController.shouldIgnoreTarget))]
public static class ClientMotionSensorIgnore
{
    static void Postfix(Entity _target, ref bool __result)
    {
        if (!__result && _target != null && BaitData.All.Contains(_target.entityId)) __result = true;
    }
}