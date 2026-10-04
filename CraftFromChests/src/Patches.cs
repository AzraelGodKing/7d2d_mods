using System;
using System.Collections.Generic;
using HarmonyLib;

// ---- Scope: chest items only count inside these crafting-UI methods ----

[HarmonyPatch(typeof(ItemActionEntryCraft), nameof(ItemActionEntryCraft.RefreshEnabled))]
public static class CfcRefreshEnabledScope
{
    static void Prefix(ItemActionEntryCraft __instance) => Chests.Enter(__instance.ItemController);
    static Exception Finalizer(Exception __exception) { Chests.Exit(); return __exception; }
}

[HarmonyPatch(typeof(XUiC_RecipeCraftCount), nameof(XUiC_RecipeCraftCount.calcMaxCraftable))]
public static class CfcMaxCraftableScope
{
    static void Prefix(XUiC_RecipeCraftCount __instance) => Chests.Enter(__instance);
    static Exception Finalizer(Exception __exception) { Chests.Exit(); return __exception; }
}

[HarmonyPatch(typeof(XUiC_IngredientEntry), nameof(XUiC_IngredientEntry.GetBindingValueInternal))]
public static class CfcIngredientScope
{
    static void Prefix(XUiC_IngredientEntry __instance) => Chests.Enter(__instance);
    static Exception Finalizer(Exception __exception) { Chests.Exit(); return __exception; }
}

// Recipe list: add chest materials to the list it checks recipes against.
[HarmonyPatch(typeof(XUiC_RecipeList), nameof(XUiC_RecipeList.BuildRecipeInfosList))]
public static class CfcRecipeListPatch
{
    static void Prefix(XUiC_RecipeList __instance, List<ItemStack> _items)
    {
        Chests.Enter(__instance);
        try { if (Chests.Active && _items != null) _items.AddRange(Chests.Stacks()); }
        catch (Exception e) { Log.Warning("[CraftFromChests] " + e.Message); }
    }
    static Exception Finalizer(Exception __exception) { Chests.Exit(); return __exception; }
}

// ---- Inventory answers include chest items while in scope ----

[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetItemCount), new[] { typeof(ItemValue) })]
public static class CfcCountByValue
{
    static void Postfix(ItemValue _itemValue, ref int __result)
    {
        if (Chests.Active && _itemValue != null && !_itemValue.HasQuality) __result += Chests.Count(_itemValue.type);
    }
}

[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetItemCount), new[] { typeof(int) })]
public static class CfcCountById
{
    static void Postfix(int _itemId, ref int __result)
    {
        if (Chests.Active) __result += Chests.Count(_itemId);
    }
}

[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetAllItemStacks))]
public static class CfcAllStacks
{
    static void Postfix(ref List<ItemStack> __result)
    {
        if (Chests.Active && __result != null) __result.AddRange(Chests.Stacks());
    }
}

[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.HasItems))]
public static class CfcHasItems
{
    static void Postfix(XUiM_PlayerInventory __instance, IList<ItemStack> _itemStacks, int _multiplier, ref bool __result)
    {
        if (__result || !Chests.Active || _itemStacks == null) return;
        Chests.Suspend = true;
        try
        {
            var need = new Dictionary<int, int>();
            foreach (var st in _itemStacks)
            {
                if (st == null || st.IsEmpty()) continue;
                if (st.itemValue.HasQuality) return;          // quality items never come from chests
                need[st.itemValue.type] = (need.TryGetValue(st.itemValue.type, out int n) ? n : 0) + st.count * _multiplier;
            }
            foreach (var kv in need)
            {
                int have = __instance.GetItemCount(new ItemValue(kv.Key));   // backpack + toolbelt (suspended)
                Chests.Suspend = false;
                have += Chests.Count(kv.Key);
                Chests.Suspend = true;
                if (have < kv.Value) return;
            }
            __result = true;
        }
        finally { Chests.Suspend = false; }
    }
}

// Safety: removing items never uses the chest-inflated numbers (vanilla RemoveItems re-checks HasItems first).
[HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.RemoveItems))]
public static class CfcRemoveItemsGuard
{
    static void Prefix(out bool __state) { __state = Chests.Suspend; Chests.Suspend = true; }
    static Exception Finalizer(Exception __exception, bool __state) { Chests.Suspend = __state; return __exception; }
}

// ---- Crafting: move what's missing from chests into the backpack, then let vanilla craft ----

[HarmonyPatch(typeof(ItemActionEntryCraft), nameof(ItemActionEntryCraft.OnActivated))]
public static class CfcCraftPatch
{
    public static bool Bypass;

    static bool Prefix(ItemActionEntryCraft __instance)
    {
        if (Bypass) return true;
        var ctrl = __instance.ItemController;
        var xui = ctrl?.xui;
        var recipe = (ctrl as XUiC_RecipeEntry)?.Recipe;
        if (xui == null || recipe == null) return true;

        Chests.Enter(ctrl);
        try
        {
            if (!Chests.Active) return true;
            if (Chests.Busy)
            {
                GameManager.ShowTooltip(xui.playerUI.entityPlayer, Localization.Get("ttAzraelCfcBusy"));
                return false;
            }

            // Enough in the backpack already? Then it's a normal vanilla craft.
            Chests.Suspend = true;
            bool backpackHasAll;
            try { backpackHasAll = __instance.hasItems(xui, recipe); }
            finally { Chests.Suspend = false; }
            if (backpackHasAll) return true;

            // With chests? (fills tempIngredientList with the real per-craft amounts)
            if (!__instance.hasItems(xui, recipe)) return true;

            int mult = Math.Max(1, __instance.craftCountControl?.Count ?? 1);
            var total = new Dictionary<int, int>();
            foreach (var st in __instance.tempIngredientList)
            {
                if (st == null || st.IsEmpty() || st.itemValue.HasQuality) continue;
                total[st.itemValue.type] = (total.TryGetValue(st.itemValue.type, out int n) ? n : 0) + st.count * mult;
            }

            var need = new Dictionary<int, int>();
            Chests.Suspend = true;
            try
            {
                foreach (var kv in total)
                {
                    int shortBy = kv.Value - xui.PlayerInventory.GetItemCount(new ItemValue(kv.Key));
                    if (shortBy <= 0) continue;
                    if (xui.PlayerInventory.CountAvailableSpaceForItem(new ItemValue(kv.Key), false) < shortBy)
                    {
                        GameManager.ShowTooltip(xui.playerUI.entityPlayer, Localization.Get("ttAzraelCfcNoRoom"), string.Empty, "ui_denied");
                        return false;
                    }
                    need[kv.Key] = shortBy;
                }
            }
            finally { Chests.Suspend = false; }
            if (need.Count == 0) return true;

            var entry = __instance;
            GameManager.Instance.StartCoroutine(Chests.Pull(xui, need, () =>
            {
                Bypass = true;
                try { entry.OnActivated(); }
                finally { Bypass = false; }
            }));
            return false;
        }
        catch (Exception e)
        {
            Log.Warning("[CraftFromChests] " + e);
            return true;
        }
        finally { Chests.Exit(); }
    }
}
