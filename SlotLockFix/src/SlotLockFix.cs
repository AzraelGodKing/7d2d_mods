using System;
using HarmonyLib;

// Slot Lock Fix: the padlock button on the backpack, vehicle/bag storage and loot windows works again.
// Vanilla bug (V3.4 b6): clicking a slot in lock mode only flips XUiC_ItemStack.UserLockedSlot on the UI.
// Each window has an UpdateLockedSlots method that copies those flags into the bag/container's real slot locks,
// but nothing ever calls it: XUiC_ContainerStandardControls.UpdateLockedSlotStates is never assigned, and
// LockModeToggled only marks the window dirty. So Sort and the Move buttons ignore the locks, and the locks are
// gone the next time the window opens. This hooks UpdateLockedSlots up again: before Sort/Move (what the
// controls already expect) and whenever lock mode is turned off (button, Esc, or closing the window).
public class AzraelSlotLockFix : IModApi
{
    public const string Tag = "[SlotLockFix]";

    public void InitMod(Mod _modInstance)
    {
        var harmony = new Harmony("azrael.slotlockfix");
        foreach (Type t in typeof(AzraelSlotLockFix).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception e) { Log.Error(Tag + " BROKEN: patch " + t.Name + " failed (the game may have changed): " + e.Message); }
        }
        Log.Out(Tag + " Loaded: slot locks are saved again in the backpack, bag and loot windows.");
    }

    public static void Wire(XUiC_ContainerStandardControls _csc, Action<XUiC_ContainerStandardControls> _save, string _window)
    {
        if (_csc == null || _save == null) return;
        // If a future game version wires this up itself, leave it alone.
        if (_csc.UpdateLockedSlotStates == null) _csc.UpdateLockedSlotStates = c => Save(c, _save, _window);
        Action<bool> vanilla = _csc.LockModeToggled;
        _csc.LockModeToggled = on =>
        {
            if (!on) Save(_csc, _save, _window);
            vanilla?.Invoke(on);
        };
    }

    static void Save(XUiC_ContainerStandardControls _csc, Action<XUiC_ContainerStandardControls> _save, string _window)
    {
        try { _save(_csc); }
        catch (Exception e) { Log.Warning(Tag + " could not save slot locks (" + _window + "): " + e.Message); }
    }
}

[HarmonyPatch(typeof(XUiC_BackpackWindow), nameof(XUiC_BackpackWindow.Init))]
public static class SlotLockBackpackPatch
{
    static void Postfix(XUiC_BackpackWindow __instance) =>
        AzraelSlotLockFix.Wire(__instance.standardControls, __instance.UpdateLockedSlots, "backpack");
}

[HarmonyPatch(typeof(XUiC_BagContainer), nameof(XUiC_BagContainer.Init))]
public static class SlotLockBagPatch
{
    static void Postfix(XUiC_BagContainer __instance) =>
        AzraelSlotLockFix.Wire(__instance.standardControls, __instance.UpdateLockedSlots, "bag");
}

[HarmonyPatch(typeof(XUiC_LootWindow), nameof(XUiC_LootWindow.Init))]
public static class SlotLockLootPatch
{
    static void Postfix(XUiC_LootWindow __instance) =>
        AzraelSlotLockFix.Wire(__instance.standardControls, __instance.UpdateLockedSlots, "loot");
}
