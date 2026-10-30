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

        // invulnerable mode: nothing hurts it (despawn and other suicide-type damage still go through)
        LiveThrall invl;
        if (victimThrall && Thralls.Live.TryGetValue(__instance.entityId, out invl) && invl.Mode == 3 && _damageSource.damageType != EnumDamageTypes.Suicide) { __result = -1; return false; }

        // a thrall that was just tamed or called is untouchable for a few seconds
        if (victimThrall && Thralls.InGrace(__instance.entityId) && _damageSource.damageType != EnumDamageTypes.Suicide) { __result = -1; return false; }

        // traps and damage blocks (spikes, barbed wire, blade traps, electric fence shocks) leave thralls alone
        if (victimThrall && BaitConfig.TrapsSpareThralls && IsTrapHit(_damageSource)) { __result = -1; return false; }

        // a thrall never hurts a player or another thrall (except a PvP foe, and the other thrall in a duel)
        if (attackerThrall && __instance is EntityPlayer && !Thralls.ThrallMayHitPlayer(attacker.entityId, (EntityPlayer)__instance)) { __result = -1; return false; }
        bool duel = attackerThrall && victimThrall && Thralls.InDuel(attacker.entityId, __instance.entityId);
        if (attackerThrall && victimThrall && !duel) { __result = -1; return false; }
        // players: the owner only with OwnerCanHurtThralls, anyone else only with OthersCanHurtThralls
        EntityPlayer actor = attacker as EntityPlayer ?? (world != null ? (world.GetEntity(_damageSource.ownerEntityId) as EntityPlayer ?? world.GetEntity(_damageSource.CreatorEntityId) as EntityPlayer) : null);
        if (victimThrall && actor != null && !Thralls.PlayerMayHurt(__instance.entityId, actor.entityId) && _damageSource.damageType != EnumDamageTypes.Suicide)
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
            // perks: hit (more damage dealt to creatures) and tough (less taken)
            int ph = attackerThrall ? Thralls.PerkCount(attacker.entityId, 'H') : 0;
            if (ph > 0 && !(__instance is EntityPlayer)) _strength = (int)(_strength * (1f + BaitConfig.PerkHitPct * ph / 100f) + 0.5f);
            int pt = victimThrall ? Thralls.PerkCount(__instance.entityId, 'T') : 0;
            if (pt > 0) _strength = Math.Max(1, (int)(_strength / (1f + BaitConfig.PerkToughPct * pt / 100f) + 0.5f));
            // animal thralls can't evolve, so they get a flat bonus on top of their level
            if (attackerThrall && !(__instance is EntityPlayer) && BaitConfig.AnimalDamagePct > 0f && IsAnimal(attacker))
                _strength = (int)(_strength * (1f + BaitConfig.AnimalDamagePct / 100f) + 0.5f);
            if (victimThrall && BaitConfig.AnimalToughPct > 0f && IsAnimal(__instance))
                _strength = Math.Max(1, (int)(_strength / (1f + BaitConfig.AnimalToughPct / 100f) + 0.5f));
        }

        if (duel) Thralls.DuelHit(attacker, __instance, ref _strength);
        if (victimThrall) Thralls.NoteHit(__instance, _damageSource, attacker, _strength);

        if (Thralls.Live.Count > 0 && (attackerThrall || victimThrall) && attacker != null && (attacker.entityFlags & EntityFlags.Zombie) != 0)
        {
            __state = attacker;
            attacker.entityFlags &= ~EntityFlags.Zombie;
        }
        return true;
    }

    // A hit made by a block: damage blocks and blade traps name the block as the attacking item, and electric fences
    // hurt through the shocked buff. Spikes and barbed wire are also stopped earlier, in DroneSpikePatch.
    public static bool IsTrapHit(DamageSource ds)
    {
        if (ds == null) return false;
        var ic = ds.AttackingItem != null && !ds.AttackingItem.IsEmpty() ? ds.AttackingItem.ItemClass : null;
        if (ic != null && ic.IsBlock()) return true;
        return ds.BuffClass != null && ds.BuffClass.Name == "buffShocked";
    }

    static bool IsAnimal(EntityAlive e)
    {
        var ec = e.EntityClass;
        return ec != null && ec.entityClassName != null && ec.entityClassName.StartsWith("animal", StringComparison.OrdinalIgnoreCase);
    }

    static void Finalizer(object __state)
    {
        var a = __state as EntityAlive;
        if (a != null) a.entityFlags |= EntityFlags.Zombie;
    }
}

// A client's hit never reaches DamageEntity on the server: the client works the hit out on its own copy of the
// thrall and sends the result, which the server applies straight through ProcessDamageResponse. So the player rules
// above have to be checked here as well, or any player on a dedicated server can kill anyone's thrall.
[HarmonyPatch(typeof(NetPackageDamageEntity), nameof(NetPackageDamageEntity.ProcessPackage))]
public static class ThrallRemoteHitPatch
{
    static bool Prefix(NetPackageDamageEntity __instance, World _world)
    {
        try
        {
            if (_world == null || !Thralls.IsServer || Thralls.Live.Count == 0) return true;
            int victimId = __instance.entityId;
            if (!Thralls.IsThrall(victimId) || __instance.damageTyp == EnumDamageTypes.Suicide) return true;
            var victim = _world.GetEntity(victimId) as EntityAlive;
            if (victim == null) return true;
            var actor = _world.GetEntity(__instance.attackerEntityId) as EntityPlayer;
            bool blocked = Thralls.InGrace(victimId) || (actor != null && !Thralls.PlayerMayHurt(victimId, actor.entityId));
            if (!blocked) return true;
            // the attacker's copy already took the hit; send the real health back so its screen agrees
            if (actor != null)
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(
                    NetPackageManager.GetPackage<NetPackageEntityStatChanged>().Setup(victim, actor.entityId, NetPackageEntityStatChanged.EnumStat.Health),
                    false, actor.entityId);
            return false;
        }
        catch (Exception e) { Log.Warning("[Bait] remote hit patch: " + e); return true; }
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
            if (_attackTarget is EntityPlayer) return lt.Mode < 2 && Thralls.ThrallMayHitPlayer(__instance.entityId, (EntityPlayer)_attackTarget);
            if (Thralls.IsThrall(_attackTarget.entityId)) return Thralls.InDuel(__instance.entityId, _attackTarget.entityId);
            if (lt.Mode >= 2) return false;
            if (Thralls.SneakBlocks(__instance, lt, _attackTarget)) return false;
            if (lt.Mode == 1 && !Thralls.DefendOk(__instance, lt, _attackTarget)) return false;
            return true;
        }
        float until;
        if (_attackTarget is EntityPlayer && Thralls.TauntedUntil.TryGetValue(__instance.entityId, out until) && UnityEngine.Time.time < until)
            return false;
        return true;
    }
}

// Live thralls come from the roster, never from the world save. Nor does anything that used to be a thrall (see Thralls.NeverSave).
[HarmonyPatch(typeof(EntityEnemy), "IsSavedToFile")]
public static class ThrallNoSave
{
    static void Postfix(EntityEnemy __instance, ref bool __result)
    {
        if (__result && Thralls.NeverSave(__instance.entityId)) __result = false;
    }
}

[HarmonyPatch(typeof(Entity), "IsSavedToFile")]
public static class ThrallNoSaveAnimal
{
    static void Postfix(Entity __instance, ref bool __result)
    {
        if (__result && Thralls.NeverSave(__instance.entityId)) __result = false;
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
            if (e != null && Thralls.IsServer && Thralls.Live.TryGetValue(e.entityId, out lt) && lt.Mode >= 2) { __result = false; return false; }
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

// Spikes and the other damage blocks do not hurt drones, or thralls (TrapsSpareThralls). Skipping the whole touch also
// means the spikes don't wear down or break when a thrall walks over them, and don't slow it.
[HarmonyPatch(typeof(BlockDamage), nameof(BlockDamage.OnEntityCollidedWithBlock))]
public static class DroneSpikePatch
{
    static bool Prefix(Entity _targetEntity, ref bool __result)
    {
        if (BaitConfig.DronesIgnoreSpikes && _targetEntity is EntityDrone) { __result = false; return false; }
        if (BaitConfig.TrapsSpareThralls && _targetEntity != null && Thralls.IsThrall(_targetEntity.entityId)) { __result = false; return false; }
        return true;
    }
}

// Diagnostics: when a thrall is killed by something that never went through DamageEntity (so there is no "last hit"), the game
// still calls Kill. Log who called it, the damage type, health and active buffs, so a "died with no recorded hit" can be traced.
[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.Kill))]
public static class ThrallKillTracePatch
{
    static void Prefix(EntityAlive __instance, DamageResponse _dmResponse)
    {
        try
        {
            if (__instance == null || !Thralls.Live.ContainsKey(__instance.entityId)) return;
            if (_dmResponse.Source != null) Thralls.NoteHit(__instance, _dmResponse.Source, null, _dmResponse.Strength);   // the killing blow names the cause even when DamageEntity never saw it
            var sb = new System.Text.StringBuilder();
            sb.Append("[Bait] kill trace for #").Append(__instance.entityId).Append(' ').Append(__instance.EntityName)
              .Append(": health ").Append(__instance.Stats.Health.Value.ToString("0.#")).Append('/').Append(__instance.GetMaxHealth())
              .Append(", damage ").Append(_dmResponse.Source != null ? _dmResponse.Source.damageType.ToString() : "none")
              .Append(", strength ").Append(_dmResponse.Strength).Append(", at ").Append(__instance.position);
            if (__instance.Buffs != null && __instance.Buffs.ActiveBuffs != null)
            {
                sb.Append(", buffs [");
                for (int i = 0; i < __instance.Buffs.ActiveBuffs.Count; i++) { if (i > 0) sb.Append(','); sb.Append(__instance.Buffs.ActiveBuffs[i].BuffName); }
                sb.Append(']');
            }
            var st = new System.Diagnostics.StackTrace(2, false);
            sb.Append(", from ");
            for (int i = 0; i < st.FrameCount && i < 6; i++)
            {
                var m = st.GetFrame(i).GetMethod();
                if (m == null) continue;
                if (i > 0) sb.Append(" < ");
                sb.Append(m.DeclaringType != null ? m.DeclaringType.Name : "?").Append('.').Append(m.Name);
            }
            Log.Out(sb.ToString());
        }
        catch { }
    }
}
// Quiet thralls: noise a thrall makes adds no heat and wakes no sleepers.
[HarmonyPatch(typeof(AIDirector), nameof(AIDirector.NotifyNoise))]
public static class ThrallQuietPatch
{
    static bool Prefix(Entity instigator)
    {
        try { return !(Thralls.IsServer && Thralls.Quiet(instigator)); } catch { return true; }
    }
}
