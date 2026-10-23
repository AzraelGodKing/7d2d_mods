using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

// Harmony patches that make thralls behave and levels count. All server side.
[HarmonyPatch(typeof(EAIApproachDistraction), "Update")]
public static class ThrallFeedPatch
{
    static void Postfix(EAIApproachDistraction __instance)
    {
        try { Thralls.OnBaitUpdate(Traverse.Create(__instance).Field("theEntity").GetValue<EntityAlive>()); } catch { }
    }
}

// Zombie vs zombie damage is blocked by the Zombie flag; lift it for the length of one hit that involves a thrall.
// Also scales damage by level: leveled attackers hit harder, leveled victims take less.
[HarmonyPatch]
public static class ThrallDamagePatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type t in new[] { typeof(EntityAlive), typeof(EntityEnemy), typeof(EntityZombie) })
        {
            var m = AccessTools.DeclaredMethod(t, "DamageEntity", new[] { typeof(DamageSource), typeof(int), typeof(bool), typeof(float) });
            if (m != null) yield return m;
        }
    }

    static bool Prefix(EntityAlive __instance, DamageSource _damageSource, ref int _strength, ref int __result, ref object __state)
    {
        __state = null;
        if (!Thralls.IsServer) return true;
        var world = __instance.world;
        var attacker = world != null ? world.GetEntity(_damageSource.getEntityId()) as EntityAlive : null;
        bool victimThrall = Thralls.IsThrall(__instance.entityId);
        bool attackerThrall = attacker != null && Thralls.IsThrall(attacker.entityId);

        // a thrall never hurts a player or another thrall
        if (attackerThrall && (__instance is EntityPlayer || victimThrall)) { __result = -1; return false; }
        // optionally the owner can't hurt their own thrall
        if (victimThrall && !BaitConfig.OwnerCanHurtThralls && attacker != null && Thralls.OwnedBy(__instance.entityId, attacker.entityId)
            && _damageSource.damageType != EnumDamageTypes.Suicide) { __result = -1; return false; }

        // levels
        if (_damageSource.damageType != EnumDamageTypes.Suicide && _strength > 0)
        {
            int la = attacker != null && attacker != __instance ? Thralls.LevelOf(attacker.entityId) : 0;
            int lv = Thralls.LevelOf(__instance.entityId);
            if (la > 0 && !(__instance is EntityPlayer && attackerThrall))
                _strength = (int)(_strength * (1f + BaitConfig.LevelDamagePct * la / 100f) + 0.5f);
            if (lv > 0)
                _strength = Math.Max(1, (int)(_strength / (1f + BaitConfig.LevelToughPct * lv / 100f) + 0.5f));
        }

        if (Thralls.Live.Count > 0 && (attackerThrall || victimThrall) && attacker != null && (attacker.entityFlags & EntityFlags.Zombie) != 0)
        {
            __state = attacker;
            attacker.entityFlags &= ~EntityFlags.Zombie;
        }
        return true;
    }

    static void Finalizer(object __state)
    {
        var a = __state as EntityAlive;
        if (a != null) a.entityFlags |= EntityFlags.Zombie;
    }
}

// Thralls must not pick players or each other as targets, and taunted wild zombies stay off players while taunted.
[HarmonyPatch(typeof(EntityAlive), "SetAttackTarget")]
public static class ThrallTargetPatch
{
    static bool Prefix(EntityAlive __instance, EntityAlive _attackTarget)
    {
        if (_attackTarget == null || !Thralls.IsServer) return true;
        LiveThrall lt;
        if (Thralls.Live.TryGetValue(__instance.entityId, out lt))
        {
            if (_attackTarget is EntityPlayer || Thralls.IsThrall(_attackTarget.entityId)) return false;
            if (lt.Mode == 2) return false;
            if (lt.Mode == 1 && !Thralls.DefendOk(__instance, lt, _attackTarget)) return false;
            return true;
        }
        float until;
        if (_attackTarget is EntityPlayer && Thralls.TauntedUntil.TryGetValue(__instance.entityId, out until) && UnityEngine.Time.time < until)
            return false;
        return true;
    }
}

// Live thralls come from the roster, never from the world save.
[HarmonyPatch(typeof(EntityEnemy), "get_IsSavedToFile")]
public static class ThrallNoSave
{
    static void Postfix(EntityEnemy __instance, ref bool __result)
    {
        if (__result && Thralls.IsThrall(__instance.entityId)) __result = false;
    }
}

[HarmonyPatch(typeof(EntityAnimal), "get_IsSavedToFile")]
public static class ThrallNoSaveAnimal
{
    static void Postfix(EntityAnimal __instance, ref bool __result)
    {
        if (__result && Thralls.IsThrall(__instance.entityId)) __result = false;
    }
}

// /thrall commands: swallow the chat line before it is broadcast.
[HarmonyPatch(typeof(GameManager), "ChatMessageServer")]
public static class ThrallChatPatch
{
    static bool Prefix(ClientInfo _cInfo, EChatType _chatType, int _senderEntityId, string _msg)
    {
        try { if (_senderEntityId >= 0 && Thralls.HandleChat(_senderEntityId, _msg)) return false; }
        catch (Exception e) { Log.Warning("[Bait] chat command: " + e.Message); }
        return true;
    }
}
