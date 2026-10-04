using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        if (Chests.Active && Chests.Material(_itemValue)) __result += Chests.Count(_itemValue.type);
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
            var gear = new Dictionary<int, int>();
            foreach (var st in _itemStacks)
            {
                if (st == null || st.IsEmpty()) continue;
                int want = st.count * _multiplier;
                // A gyro needs wheels AND a small engine. The engine is a quality item and stays in the backpack;
                // that must not make the mod ignore the wheels sitting in a chest.
                if (!Chests.Material(st.itemValue))
                    gear[st.itemValue.type] = (gear.TryGetValue(st.itemValue.type, out int g) ? g : 0) + want;
                else
                    need[st.itemValue.type] = (need.TryGetValue(st.itemValue.type, out int n) ? n : 0) + want;
            }
            foreach (var kv in gear)
                if (__instance.GetItemCount(new ItemValue(kv.Key)) < kv.Value) return;
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
    static bool Prefix(ItemActionEntryCraft __instance)
    {
        if (Chests.Replay) return true;
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
                if (st == null || st.IsEmpty() || !Chests.Material(st.itemValue)) continue;
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
                Chests.Replay = true;
                try { entry.OnActivated(); }
                finally { Chests.Replay = false; }
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

// ---- Block upgrade: the repair tool is not a crafting screen, so it never entered Scope ----

[HarmonyPatch(typeof(ItemActionRepair), nameof(ItemActionRepair.CanRemoveRequiredResource))]
public static class CfcUpgradeCheck
{
    static void Postfix(ItemActionRepair __instance, ItemInventoryData data, BlockValue blockValue, ref bool __result)
    {
        if (__result || !Chests.Enabled) return;
        try { Chests.TryStageUpgrade(__instance, data, blockValue); }
        catch (Exception e) { Log.Warning("[CraftFromChests] " + e.Message); }
    }
}

// While materials are on the way from a chest, vanilla would flash a "0" missing-item icon.
[HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.AddUIHarvestingItem))]
public static class CfcQuietUpgrade
{
    static bool Prefix(ItemStack itemStack)
    {
        if (!Chests.SuppressMissing || itemStack == null || itemStack.count != 0) return true;
        Chests.SuppressMissing = false;
        return false;
    }
}

// ---- Upgrade Bench: its screen is the vanilla combine window, not a crafting window ----

[HarmonyPatch(typeof(XUiC_CombineWindowGroup), nameof(XUiC_CombineWindowGroup.OnOpen))]
public static class CfcBenchOpen
{
    static void Prefix(XUiC_CombineWindowGroup __instance) => Chests.Station = Chests.IsUpgradeBench(__instance.te);
    static void Postfix(XUiC_CombineWindowGroup __instance) => Chests.Station = Chests.IsUpgradeBench(__instance.te);
}

[HarmonyPatch(typeof(XUiC_CombineWindowGroup), nameof(XUiC_CombineWindowGroup.OnClose))]
public static class CfcBenchClose
{
    static void Prefix() => Chests.Station = false;
}

// Runs before the Upgrade Bench's own button patch. If the materials are only in a chest, move them into the
// backpack, wait for the server, then press the button again so the bench pays from the backpack.
[HarmonyPatch(typeof(XUiC_CombineGrid), nameof(XUiC_CombineGrid.BtnCombine_OnPressed))]
[HarmonyPriority(Priority.First)]
public static class CfcBenchPay
{
    static bool Prefix(XUiC_CombineGrid __instance, XUiController _sender, int _mouseButton)
    {
        if (Chests.Replay || !Chests.Station || !Chests.Enabled || __instance == null) return true;
        var xui = __instance.xui;
        if (xui == null) return true;
        try
        {
            if (Chests.Busy)
            {
                GameManager.ShowTooltip(xui.playerUI.entityPlayer, Localization.Get("ttAzraelCfcBusy"));
                return false;
            }
            var s1 = __instance.merge1.ItemStack;
            var s2 = __instance.merge2.ItemStack;
            if (s1.IsEmpty() || s2.IsEmpty()) return true;
            if (s1.itemValue.type != s2.itemValue.type || s1.itemValue.Quality != s2.itemValue.Quality) return true;
            if (!BenchCost(__instance, out var pay)) return true;

            var need = new Dictionary<int, int>();
            Chests.Suspend = true;
            try
            {
                foreach (var kv in pay)
                {
                    var iv = new ItemValue(kv.Key);
                    int shortBy = kv.Value - xui.PlayerInventory.GetItemCount(iv);
                    if (shortBy <= 0) continue;
                    if (xui.PlayerInventory.CountAvailableSpaceForItem(iv, false) < shortBy)
                    {
                        GameManager.ShowTooltip(xui.playerUI.entityPlayer, Localization.Get("ttAzraelCfcNoRoom"), string.Empty, "ui_denied");
                        return false;
                    }
                    need[kv.Key] = shortBy;
                }
            }
            finally { Chests.Suspend = false; }
            if (need.Count == 0) return true;

            var grid = __instance;
            GameManager.Instance.StartCoroutine(Chests.Pull(xui, need, () =>
            {
                Chests.Replay = true;
                try { grid.BtnCombine_OnPressed(_sender, _mouseButton); }
                finally { Chests.Replay = false; }
            }, "ttAzraelCfcUpgradeHeld"));
            return false;
        }
        catch (Exception e)
        {
            Log.Warning("[CraftFromChests] " + e.Message);
            return true;
        }
    }

    // The Upgrade Bench mod works out the cost. We only read it; this mod still runs if that one is not installed.
    static bool BenchCost(XUiC_CombineGrid grid, out Dictionary<int, int> pay)
    {
        pay = null;
        Assembly asm = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name == "AzraelUpgradeBench") { asm = a; break; }
        var make = asm?.GetType("ModUpgrades")?.GetMethod("Make", BindingFlags.Public | BindingFlags.Static);
        if (make == null) return false;
        object plan = make.Invoke(null, new object[] { grid.merge1.ItemStack, grid.xui.PlayerInventory });
        if (plan == null) return false;
        var state = plan.GetType().GetField("State")?.GetValue(plan);
        if (state == null || state.ToString() != "Ready") return false;
        if (!(plan.GetType().GetField("Pay")?.GetValue(plan) is IEnumerable list)) return false;
        pay = new Dictionary<int, int>();
        foreach (var cost in list)
        {
            if (cost == null) continue;
            var itemName = cost.GetType().GetField("Item")?.GetValue(cost) as string;
            var countObj = cost.GetType().GetField("Count")?.GetValue(cost);
            if (itemName == null || !(countObj is int count) || count <= 0) continue;
            var iv = ItemClass.GetItem(itemName);
            if (iv == null || iv.IsEmpty() || !Chests.Material(iv)) continue;
            pay[iv.type] = (pay.TryGetValue(iv.type, out int n) ? n : 0) + count;
        }
        return pay.Count > 0;
    }
}
