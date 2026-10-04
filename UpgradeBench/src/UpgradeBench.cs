using System;
using System.Linq;
using UnityEngine;

// Upgrade Bench: put TWO of the same listed mod, at the SAME quality, in the bench and pay the materials listed
// in upgrades.txt; both are used up and ONE comes back a quality level higher. The costs and the list live in ModUpgrades.cs / upgrades.txt.
//
// It reuses the vanilla Combine Station's tile entity and screen (TEFeatureCombine / "combine" window group);
// the patches only change behaviour when the screen was opened from an azraelUpgradeBench block, so the vanilla
// Combine Station keeps working exactly as before.

public class AzraelUpgradeBenchMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        if (GameManager.IsDedicatedServer)
        {
            Log.Out("[UpgradeBench] Dedicated server: block and recipe loaded (upgrades happen on players' PCs).");
            return;
        }
        ModUpgrades.Load(System.IO.Path.Combine(_modInstance.Path, "upgrades.txt"));
        new HarmonyLib.Harmony("azrael.upgradebench").PatchAll(typeof(AzraelUpgradeBenchMod).Assembly);
        Log.Out("[UpgradeBench] Loaded.");
    }
}

public static class UpgradeBench
{
    public const string BlockName = "azraelUpgradeBench";

    public static bool IsOurs(TEFeatureCombine te)
    {
        try
        {
            if (te == null) return false;
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null) return false;
            var block = world.GetBlock(te.ToWorldPos()).Block;
            return block != null && block.GetBlockName() == BlockName;
        }
        catch { return false; }
    }

    // Returns false if the backpack was full and it was dropped at the player's feet instead.
    public static bool GiveOrDrop(XUi xui, ItemStack stack)
    {
        if (stack.IsEmpty()) return true;
        if (xui.PlayerInventory.AddItem(stack)) return true;
        var player = xui.playerUI.entityPlayer;
        GameManager.Instance.ItemDropServer(stack, player.GetPosition(), Vector3.zero);
        player.PlayOneShot("itemdropped");
        return false;
    }

    // Player-facing message. 'bad' plays the "denied" sound.
    public static void Tip(XUi xui, string text, bool bad = false) =>
        GameManager.ShowTooltip(xui.playerUI.entityPlayer, text, string.Empty, bad ? "ui_denied" : string.Empty);

    public static string L(string key, params object[] args) =>
        args.Length == 0 ? Localization.Get(key) : string.Format(Localization.Get(key), args);

    public static string NameOf(ItemStack s) => s.itemValue.ItemClass?.GetLocalizedItemName() ?? "?";

    // What the result panel's 7 stat rows show while our bench is open ({ title, value }); null = vanilla stats.
    public static System.Collections.Generic.List<string[]> Rows;

    // True while an Upgrade Bench screen is open (drives the "how it works" text in the Inspect panel).
    public static bool BenchOpen;

    // Null if the two slots hold the same mod at the same quality; otherwise the message to show.
    public static string Mismatch(ItemStack a, ItemStack b)
    {
        if (a.itemValue.type != b.itemValue.type)
            return L("ttAzraelUpgradeDifferent", NameOf(a), NameOf(b));
        if (a.itemValue.Quality != b.itemValue.Quality)
            return L("ttAzraelUpgradeDifferentQuality", NameOf(a), a.itemValue.Quality, b.itemValue.Quality);
        return null;
    }
}

// Work out (and preview) the upgrade whenever an input slot changes.
[HarmonyLib.HarmonyPatch(typeof(XUiC_CombineGrid), nameof(XUiC_CombineGrid.Merge_SlotChangedEvent))]
public static class UpgradeBenchSlotPatch
{
    static bool Prefix(XUiC_CombineGrid __instance)
    {
        if (!UpgradeBench.IsOurs(__instance.te)) return true; // vanilla Combine Station: untouched
        try
        {
            __instance.btnCombine.Enabled = false;
            UpgradeBench.Rows = null;
            var s1 = __instance.merge1.ItemStack;
            var s2 = __instance.merge2.ItemStack;
            var xui = __instance.xui;

            if (s1.IsEmpty() && s2.IsEmpty())
            {
                __instance.SetResult(ItemStack.Empty);
                return false;
            }
            // Needs TWO of the same mod at the SAME quality.
            var one = s1.IsEmpty() ? s2 : s1;
            var plan = ModUpgrades.Make(one, xui.PlayerInventory);
            if (plan.State == ModUpgrades.State.NotListed)
            {
                UpgradeBench.Tip(xui, UpgradeBench.L("ttAzraelUpgradeNotListed", UpgradeBench.NameOf(one)), true);
                __instance.SetResult(ItemStack.Empty);
                return false;
            }
            if (plan.State == ModUpgrades.State.MaxQuality)
            {
                UpgradeBench.Tip(xui, plan.Text, true);
                __instance.SetResult(ItemStack.Empty);
                return false;
            }
            string mismatch = s1.IsEmpty() || s2.IsEmpty() ? null : UpgradeBench.Mismatch(s1, s2);
            if (mismatch != null)
            {
                UpgradeBench.Tip(xui, mismatch, true);
                __instance.SetResult(ItemStack.Empty);
                return false;
            }

            // Result panel: first row = the 2 mods (have/need), then each material with have/need.
            int modsIn = s1.IsEmpty() || s2.IsEmpty() ? 1 : 2;
            UpgradeBench.Rows = new System.Collections.Generic.List<string[]>
            {
                new[] { $"{UpgradeBench.NameOf(one)} Q{one.itemValue.Quality}", ModUpgrades.HaveNeed(modsIn, 2) }
            };
            UpgradeBench.Rows.AddRange(plan.Rows);

            bool ready = modsIn == 2 && plan.State == ModUpgrades.State.Ready;
            // No pop-up here: the result panel already shows what's needed (have / need, green / red).
            // Preview the Q+1 result even when something is missing; the button only works when ready.
            __instance.SetResult(plan.Result != null ? new ItemStack(plan.Result, 1) : ItemStack.Empty);
            __instance.btnCombine.Enabled = ready;
        }
        catch (Exception e)
        {
            Log.Warning("[UpgradeBench] " + e);
            __instance.SetResult(ItemStack.Empty);
        }
        return false;
    }
}

// Upgrade button: take the materials, give the item back one quality higher.
[HarmonyLib.HarmonyPatch(typeof(XUiC_CombineGrid), nameof(XUiC_CombineGrid.BtnCombine_OnPressed))]
public static class UpgradeBenchUpgradePatch
{
    static bool Prefix(XUiC_CombineGrid __instance)
    {
        if (!UpgradeBench.IsOurs(__instance.te)) return true;
        try
        {
            var xui = __instance.xui;
            var s1 = __instance.merge1.ItemStack;
            var s2 = __instance.merge2.ItemStack;
            if (s1.IsEmpty() || s2.IsEmpty() || UpgradeBench.Mismatch(s1, s2) != null) return false; // two matching mods needed

            var plan = ModUpgrades.Make(s1, xui.PlayerInventory);   // re-check now: the backpack may have changed
            if (plan.State != ModUpgrades.State.Ready)
            {
                if (plan.Text != null) UpgradeBench.Tip(xui, plan.Text, true);
                __instance.btnCombine.Enabled = false;
                return false;
            }
            var pay = ModUpgrades.ToStacks(plan.Pay);
            if (!xui.PlayerInventory.HasItems(pay))
            {
                UpgradeBench.Tip(xui, UpgradeBench.L("ttAzraelUpgradeNotReady"), true);
                return false;
            }
            xui.PlayerInventory.RemoveItems(pay);

            // Both mods are used up; one comes back a level higher.
            var upgraded = new ItemStack(plan.Result, 1);
            foreach (var slot in new[] { __instance.merge1, __instance.merge2 })
            {
                var rest = slot.ItemStack.Clone();
                rest.count -= 1;
                slot.ItemStack = rest.count > 0 ? rest : ItemStack.Empty;
            }
            bool inBag = UpgradeBench.GiveOrDrop(xui, upgraded);
            UpgradeBench.Rows = null;
            __instance.SetResult(ItemStack.Empty);
            __instance.te?.HandlePlayComplete();
            UpgradeBench.Tip(xui, UpgradeBench.L("ttAzraelUpgradeDone", UpgradeBench.NameOf(upgraded), plan.Result.Quality)
                                  + "\n" + UpgradeBench.L(inBag ? "ttAzraelPutInBackpack" : "ttAzraelDroppedAtFeet"));
            Log.Out($"[UpgradeBench] Upgraded 2x {plan.Result.ItemClass?.GetItemName()} into Q{plan.Result.Quality} for " +
                    string.Join(", ", plan.Pay.Select(c => c.Count + "x " + c.Item)) + ".");
            if (!__instance.merge1.ItemStack.IsEmpty() || !__instance.merge2.ItemStack.IsEmpty())
                __instance.Merge_SlotChangedEvent(0, __instance.merge1.ItemStack); // anything left: refresh the message
        }
        catch (Exception e) { Log.Warning("[UpgradeBench] " + e); }
        return false;
    }
}

// Show "Upgrade Bench" in the screen's title instead of "Combine Station".
[HarmonyLib.HarmonyPatch(typeof(XUiC_CombineWindowGroup), nameof(XUiC_CombineWindowGroup.OnOpen))]
public static class UpgradeBenchTitlePatch
{
    // Runs before the windows inside the group open, so the Inspect panel already knows it's our bench.
    static void Prefix(XUiC_CombineWindowGroup __instance) => UpgradeBench.BenchOpen = UpgradeBench.IsOurs(__instance.te);

    static void Postfix(XUiC_CombineWindowGroup __instance)
    {
        bool ours = UpgradeBench.IsOurs(__instance.te);
        if (ours && __instance.nonPagingHeaderWindow != null)
            __instance.nonPagingHeaderWindow.SetHeader(Localization.Get("xuiAzraelUpgradeBench"));

        // The panel's own "Combine" header and "Combine" button: say "Upgrade" at our bench, put vanilla text back otherwise.
        try
        {
            string vanillaHeader = Localization.Get("xuiCombine");
            string vanillaButton = Localization.Get("xuiCombineGridCombineButton");
            string ourHeader = Localization.Get("xuiAzraelUpgradeHeader");
            string ourButton = Localization.Get("xuiAzraelUpgradeButton");
            foreach (var grid in __instance.GetChildrenByType<XUiC_CombineGrid>())
            {
                if (grid.btnCombine != null) grid.btnCombine.Text = ours ? ourButton : vanillaButton;
                var window = grid.Parent ?? grid;
                foreach (var label in window.GetChildrenByViewType<XUiV_Label>())
                {
                    string t = label.Text ?? "";
                    if (t.Equals(vanillaHeader, StringComparison.OrdinalIgnoreCase) || t.Equals(ourHeader, StringComparison.OrdinalIgnoreCase))
                        label.Text = ours ? ourHeader : vanillaHeader;
                }
            }
        }
        catch (Exception e) { Log.Warning("[UpgradeBench] header/button text: " + e.Message); }
    }
}

// Clearer message when something that can't go in the bench is dropped on a slot.
[HarmonyLib.HarmonyPatch(typeof(XUiC_CombineGrid), nameof(XUiC_CombineGrid.Merge_FailedSwap))]
public static class UpgradeBenchFailedSwapPatch
{
    static bool Prefix(XUiC_CombineGrid __instance, ItemStack stack)
    {
        if (!UpgradeBench.IsOurs(__instance.te)) return true;
        UpgradeBench.Tip(__instance.xui, UpgradeBench.L("ttAzraelUpgradeNotListed", UpgradeBench.NameOf(stack)), true);
        return false;
    }
}

// Result panel (the 7 stat rows next to the result icon): show what the upgrade needs, with have / need,
// green when you have enough and red when you're short.
[HarmonyLib.HarmonyPatch(typeof(XUiC_CombineGrid), nameof(XUiC_CombineGrid.GetBindingValueInternal))]
public static class UpgradeBenchPanelPatch
{
    static void Postfix(XUiC_CombineGrid __instance, ref string value, string bindingName, ref bool __result)
    {
        var rows = UpgradeBench.Rows;
        if (rows == null || bindingName == null || !bindingName.StartsWith("itemstat")) return;
        if (!UpgradeBench.IsOurs(__instance.te)) return;
        bool title = bindingName.StartsWith("itemstattitle");
        int i = (int)char.GetNumericValue(bindingName[bindingName.Length - 1]);
        value = i >= 0 && i < rows.Count ? rows[i][title ? 0 : 1] : string.Empty;
        __result = true;
    }
}

// Clear the panel rows whenever a bench screen opens.
[HarmonyLib.HarmonyPatch(typeof(XUiC_CombineGrid), nameof(XUiC_CombineGrid.OnOpen))]
public static class UpgradeBenchOpenPatch
{
    static void Prefix() => UpgradeBench.Rows = null;
}

// Inspect panel (shown when nothing is selected): explain how the Upgrade Bench works instead of the mouse help.
[HarmonyLib.HarmonyPatch(typeof(XUiC_EmptyInfoWindow), nameof(XUiC_EmptyInfoWindow.UpdateDescriptionText))]
public static class UpgradeBenchHelpPatch
{
    static void Postfix(XUiC_EmptyInfoWindow __instance)
    {
        if (UpgradeBench.BenchOpen && __instance.descriptionText != null)
            __instance.descriptionText.Text = Localization.Get("xuiAzraelUpgradeHelp");
    }
}

[HarmonyLib.HarmonyPatch(typeof(XUiC_CombineWindowGroup), nameof(XUiC_CombineWindowGroup.OnClose))]
public static class UpgradeBenchClosePatch
{
    static void Prefix() => UpgradeBench.BenchOpen = false;
}
