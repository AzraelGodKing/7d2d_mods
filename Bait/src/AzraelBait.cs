using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

// Zombie and Animal Bait: protection for baited creatures.
// While a zombie or animal is going for (or eating) a thrown bait it must only be hurt by the player who
// threw that bait. Turrets stop picking it as a target, and damage from anything else (traps, fire, other
// monsters, other defences) is cancelled. Damage the thrower deals still goes through, and so do
// internal/suicide damage (despawn, hunger and the like), so nothing gets stuck alive.

public class AzraelBaitMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        BaitConfig.Load(_modInstance.Path);
        var harmony = new Harmony("azrael.bait");
        foreach (Type type in typeof(AzraelBaitMod).Assembly.GetTypes())
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.Warning("[Bait] patch " + type.Name + " failed: " + e.Message); }
        }
        BaitTrace.Start(_modInstance.Path);
        ModEvents.GameStartDone.RegisterHandler(Thralls.OnGameStart);
        ModEvents.GameShutdown.RegisterHandler(Thralls.OnShutdown);
        ModEvents.GameUpdate.RegisterHandler(Thralls.OnUpdate);
        ModEvents.EntityKilled.RegisterHandler(Thralls.OnEntityKilled);
        ModEvents.ChatMessage.RegisterHandler(ThrallChatEvent.OnChat);
        Log.Out("[Bait] Loaded: creatures going for Zombie and Animal Bait are only hurt by the player who threw it.");
    }
}

public static class BaitCheck
{
    public const string BaitItem = "azraelBait";
    public const string RichItem = "azraelBaitRich";
    public const string FeastItem = "azraelBaitFeast";

    public static bool IsBait(string item) { return item == BaitItem || item == RichItem || item == FeastItem; }

    // How many meals one of this bait counts as.
    public static int Meals(string item)
    {
        if (item == FeastItem) return Math.Max(1, BaitConfig.FeastBaitMeals);
        if (item == RichItem) return Math.Max(1, BaitConfig.RichBaitMeals);
        return 1;
    }

    // The bait a creature is currently going for, or null.
    public static EntityItem ActiveBait(Entity e)
    {
        var alive = e as EntityAlive;
        if (alive == null) return null;
        var d = alive.distraction;
        if (d == null || d.IsDead() || d.itemClass == null) return null;
        return IsBait(d.itemClass.GetItemName()) ? d : null;
    }
}

// Damage: only the thrower may hurt a baited creature.
[HarmonyPatch]
public static class BaitDamageGuard
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var t in new[] { typeof(EntityAlive), typeof(EntityAnimal), typeof(EntityEnemy), typeof(EntityHuman) })
        {
            var m = AccessTools.DeclaredMethod(t, "DamageEntity", new[] { typeof(DamageSource), typeof(int), typeof(bool), typeof(float) });
            if (m != null) yield return m;
        }
    }

    static bool Prefix(EntityAlive __instance, DamageSource _damageSource, ref int __result)
    {
        try
        {
            var bait = BaitCheck.ActiveBait(__instance);
            if (bait == null) return true;
            if (_damageSource == null) return true;
            if (_damageSource.damageType == EnumDamageTypes.Suicide) return true;      // despawn etc.
            if (_damageSource.GetSource() == EnumDamageSource.Internal) return true;   // hunger, fall, disease
            if (_damageSource.getEntityId() == bait.OwnerId && bait.OwnerId != -1) return true; // the thrower
            __result = -1; // same "no damage" result the game uses for blocked hits
            return false;
        }
        catch (Exception e) { Log.Warning("[Bait] " + e.Message); return true; }
    }
}

// Turrets: don't pick a baited creature as a target.
[HarmonyPatch(typeof(AutoTurretFireController), nameof(AutoTurretFireController.shouldIgnoreTarget))]
public static class BaitAutoTurretIgnore
{
    static void Postfix(Entity _target, ref bool __result)
    {
        if (!__result && _target != null && (BaitCheck.ActiveBait(_target) != null || Thralls.IsThrall(_target.entityId))) __result = true;
    }
}

[HarmonyPatch(typeof(MiniTurretFireController), nameof(MiniTurretFireController.shouldIgnoreTarget))]
public static class BaitMiniTurretIgnore
{
    static void Postfix(Entity _target, ref bool __result)
    {
        if (!__result && _target != null && (BaitCheck.ActiveBait(_target) != null || Thralls.IsThrall(_target.entityId))) __result = true;
    }
}
// Writes the stack trace of any NullReferenceException (the player log sometimes drops them) to AzraelBaitTrace.log
// in the mod folder, so a spam of NREs can be traced to the code that throws it. Capped so it can't fill the disk.
public static class BaitTrace
{
    static string path;
    static int written;
    static readonly HashSet<string> seen = new HashSet<string>();

    public static void Start(string modPath)
    {
        try
        {
            path = System.IO.Path.Combine(modPath, "AzraelBaitTrace.log");
            UnityEngine.Application.logMessageReceived += OnLog;
        }
        catch { }
    }

    static void OnLog(string msg, string stack, UnityEngine.LogType type)
    {
        if (type != UnityEngine.LogType.Exception || written >= 40) return;
        try
        {
            string key = (msg ?? "") + "|" + (stack ?? "");
            if (!seen.Add(key)) return;
            written++;
            System.IO.File.AppendAllText(path, DateTime.Now.ToString("s") + " " + msg + "\n" + stack + "\n----\n");
        }
        catch { }
    }
}
// A thrown item is removed after 60 seconds, which cut the bait short. Keep it for as long as it calls creatures
// (DistractionLifetime in items.xml) plus a little.
[HarmonyPatch(typeof(EntityItem), "OnUpdateEntity")]
public static class BaitLifetimePatch
{
    static readonly HashSet<int> done = new HashSet<int>();
    static void Prefix(EntityItem __instance)
    {
        try
        {
            var ic = __instance.itemClass;
            if (ic == null || !BaitCheck.IsBait(ic.GetItemName())) return;
            if (!done.Add(__instance.entityId)) return;
            float want = __instance.distractionLifetime + 15f;
            if (__instance.lifetime < want) __instance.lifetime = want;
            if (done.Count > 2000) done.Clear();
        }
        catch { }
    }
}