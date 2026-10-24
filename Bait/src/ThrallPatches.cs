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
        try { return PrefixInner(__instance, _damageSource, ref _strength, ref __result, ref __state); }
        catch (Exception e) { Log.Warning("[Bait] damage patch: " + e); return true; }
    }

    static bool PrefixInner(EntityAlive __instance, DamageSource _damageSource, ref int _strength, ref int __result, ref object __state)
    {
        __state = null;
        if (!Thralls.IsServer) return true;
        var world = __instance.world;
        var attacker = world != null ? world.GetEntity(_damageSource.getEntityId()) as EntityAlive : null;
        bool victimThrall = Thralls.IsThrall(__instance.entityId);
        bool attackerThrall = attacker != null && Thralls.IsThrall(attacker.entityId);

        // a thrall that was just tamed or called is untouchable for a few seconds
        if (victimThrall && Thralls.InGrace(__instance.entityId) && _damageSource.damageType != EnumDamageTypes.Suicide) { __result = -1; return false; }

        // a thrall never hurts a player or another thrall
        if (attackerThrall && (__instance is EntityPlayer || victimThrall)) { __result = -1; return false; }
        // optionally the owner can't hurt their own thrall
        if (victimThrall && !BaitConfig.OwnerCanHurtThralls && attacker != null && Thralls.OwnedBy(__instance.entityId, attacker.entityId)
            && _damageSource.damageType != EnumDamageTypes.Suicide) { __result = -1; return false; }

        // other players can't hurt it either
        if (victimThrall && !BaitConfig.OthersCanHurtThralls && attacker is EntityPlayer && _damageSource.damageType != EnumDamageTypes.Suicide)
        { __result = -1; return false; }

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

        if (victimThrall) Thralls.NoteHit(__instance, _damageSource, attacker, _strength);

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
        try { return Inner(__instance, _attackTarget); } catch (Exception e) { Log.Warning("[Bait] target patch: " + e); return true; }
    }

    static bool Inner(EntityAlive __instance, EntityAlive _attackTarget)
    {
        if (_attackTarget == null || !Thralls.IsServer) return true;
        if (Thralls.InGrace(_attackTarget.entityId) && !Thralls.Live.ContainsKey(__instance.entityId)) return false;
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
[HarmonyPatch(typeof(EntityEnemy), "IsSavedToFile")]
public static class ThrallNoSave
{
    static void Postfix(EntityEnemy __instance, ref bool __result)
    {
        if (__result && Thralls.IsThrall(__instance.entityId)) __result = false;
    }
}

[HarmonyPatch(typeof(Entity), "IsSavedToFile")]
public static class ThrallNoSaveAnimal
{
    static void Postfix(Entity __instance, ref bool __result)
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

// Passive mode: a thrall in passive mode never starts or keeps an attack, whoever started the fight.
[HarmonyPatch]
public static class ThrallPassivePatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var a = AccessTools.DeclaredMethod(typeof(EAIApproachAndAttackTarget), "CanExecute");
        var b = AccessTools.DeclaredMethod(typeof(EAIApproachAndAttackTarget), "Continue");
        if (a != null) yield return a;
        if (b != null) yield return b;
    }

    static bool Prefix(EAIBase __instance, ref bool __result)
    {
        try
        {
            LiveThrall lt;
            var e = __instance.theEntity;
            if (e != null && Thralls.IsServer && Thralls.Live.TryGetValue(e.entityId, out lt) && lt.Mode == 2) { __result = false; return false; }
        }
        catch { }
        return true;
    }
}
// Drones: a thrall is never a drone target, and a drone counts it as an ally (no shooting it, no enemy chatter).
[HarmonyPatch]
public static class ThrallDroneTargetPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type n in typeof(EntityDrone).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            var m = AccessTools.DeclaredMethod(n, "canAttackTarget");
            if (m != null) yield return m;
        }
    }

    static void Postfix(EntityAlive enemy, ref bool __result)
    {
        if (__result && enemy != null && Thralls.IsThrall(enemy.entityId)) __result = false;
    }
}

[HarmonyPatch(typeof(EntityDrone), "isAlly")]
public static class ThrallDroneAllyPatch
{
    static void Postfix(EntityAlive _target, ref bool __result)
    {
        if (!__result && _target != null && Thralls.IsThrall(_target.entityId)) __result = true;
    }
}
// The game counts every living enemy and animal against MaxSpawnedZombies / MaxSpawnedAnimals once a second. Thralls are
// taken out of those counts so a player's army doesn't leave the server with room for fewer wild creatures.
[HarmonyPatch(typeof(GameStats), "Set", new[] { typeof(EnumGameStats), typeof(int) })]
public static class ThrallSpawnCapPatch
{
    static void Prefix(EnumGameStats _eProperty, ref int _value)
    {
        try
        {
            if (BaitConfig.ThrallsCountTowardSpawnCap || !Thralls.IsServer || Thralls.Live.Count == 0) return;
            if (_eProperty != EnumGameStats.EnemyCount && _eProperty != EnumGameStats.AnimalCount) return;
            int en, an;
            Thralls.CountLive(out en, out an);
            _value = Math.Max(0, _value - (_eProperty == EnumGameStats.EnemyCount ? en : an));
        }
        catch { }
    }
}
// Grabbing a creature (a chicken) turns it into an item and removes it. Without this the roster would respawn it,
// leaving the player with both the item and a copy.
// The server runs OnCollectServer when a client grabs (Entity.Collect only runs on the host), so the hook goes there.
[HarmonyPatch(typeof(EntityAnimal), "OnCollectServer")]
public static class ThrallGrabPatch
{
    static void Prefix(Entity __instance, int _playerId)
    {
        if (Thralls.IsServer && Thralls.Live.Count > 0) Thralls.OnGrabbed(__instance, _playerId);
    }
}

// Thralls never dig through or hit blocks: the move helper starts "digging" when a path is blocked and breaking is allowed.
[HarmonyPatch(typeof(EntityMoveHelper), "DigStart")]
public static class ThrallNoDigPatch
{
    static bool Prefix(EntityMoveHelper __instance)
    {
        try
        {
            var e = __instance.entity;
            return !(e != null && Thralls.IsServer && Thralls.IsThrall(e.entityId));
        }
        catch { return true; }
    }
}

// Motion sensors (what sets off dart traps and other powered traps) skip thralls like the turrets do.
[HarmonyPatch(typeof(MotionSensorController), nameof(MotionSensorController.shouldIgnoreTarget))]
public static class BaitMotionSensorIgnore
{
    static void Postfix(Entity _target, ref bool __result)
    {
        if (!__result && _target != null && (BaitCheck.ActiveBait(_target) != null || Thralls.IsThrall(_target.entityId))) __result = true;
    }
}

// Spikes and the other damage blocks do not hurt drones.
[HarmonyPatch(typeof(BlockDamage), nameof(BlockDamage.OnEntityCollidedWithBlock))]
public static class DroneSpikePatch
{
    static bool Prefix(Entity _targetEntity, ref bool __result)
    {
        if (BaitConfig.DronesIgnoreSpikes && _targetEntity is EntityDrone) { __result = false; return false; }
        return true;
    }
}