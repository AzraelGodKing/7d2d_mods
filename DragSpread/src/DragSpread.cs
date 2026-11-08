using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Drag Spread: click-and-drag a held stack across inventory slots (client side).
//
// How it plays: pick up a stack so it is on the cursor. Hold a mouse button on a slot and sweep over more
// slots, then let go.
//   Left button  = split the stack evenly over the slots you swept. Any odd items go one each to the
//                  first slots, so the whole stack is placed (unless the slots run out of room).
//   Right button = put 1 item in each slot you swept. The rest stays on the cursor.
// Empty slots are filled and stacks of the same item are topped up. Slots the drag will fill get a white border.
// A click without a drag (press and release on one slot) is left to the game, so vanilla clicks are unchanged.
//
// How it works: every slot is an XUiC_ItemStack and the game runs XUiC_ItemStack.Update on each visible slot
// every frame. A prefix on it watches the mouse: a press on a slot while holding a stack starts a drag, every
// slot the mouse passes over with the button still down is recorded, and on release the stack is shared out by setting each
// slot's ItemStack. That setter fires the slot's SlotChangedEvent, which is the same path a normal click uses
// to write into the backpack, toolbelt or container (and the game syncs containers to the server from there).
// Vanilla only places a held stack when the press and release happen on the same slot, so a real drag (two or
// more slots) never triggers the vanilla click as well.
public class AzraelDragSpread : IModApi
{
    public const string Tag = "[DragSpread]";

    public void InitMod(Mod _modInstance)
    {
        var harmony = new Harmony("azrael.dragspread");
        foreach (Type t in typeof(AzraelDragSpread).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception e) { Log.Error(Tag + " BROKEN: patch " + t.Name + " failed (the game may have changed): " + e.Message); }
        }
        Log.Out(Tag + " Loaded: hold a stack, then left-drag to split evenly or right-drag to place 1 per slot.");
    }
}

public static class DragSpread
{
    // Drag state. Only one drag can exist at a time (there is one cursor).
    static bool active;
    static int button;                 // 0 = left (even split), 1 = right (one each)
    static XUi xui;
    static ItemStack startStack;       // what was on the cursor when the drag began
    static readonly List<XUiC_ItemStack> visited = new List<XUiC_ItemStack>();
    static readonly HashSet<XUiC_ItemStack> visitedSet = new HashSet<XUiC_ItemStack>();
    static int lastTickFrame = -1;
    static int lastSeenFrame = -1;

    public static bool IsDragging => active && visited.Count >= 2;

    // Called from every slot's Update prefix.
    public static void OnSlotUpdate(XUiC_ItemStack _slot)
    {
        if (_slot.isDragAndDrop || _slot is XUiC_Creative2Stack) return;
        if (_slot.WindowGroup == null || !_slot.WindowGroup.isShowing) return;

        int frame = Time.frameCount;
        if (frame != lastTickFrame)
        {
            lastTickFrame = frame;
            Tick(_slot.xui, frame);
        }
        lastSeenFrame = frame;

        if (!UnderMouse(_slot)) return;

        if (!active)
        {
            if (_slot.IsLocked || _slot.SimpleClick || ShiftHeld()) return;
            int pressed = Input.GetMouseButtonDown(0) ? 0 : Input.GetMouseButtonDown(1) ? 1 : -1;
            if (pressed < 0) return;
            ItemStack held = _slot.xui.DragAndDropWindow.CurrentStack;
            if (held.IsEmpty()) return;
            active = true;
            button = pressed;
            xui = _slot.xui;
            startStack = held;
            Visit(_slot);
        }
        else if (_slot.xui == xui && Input.GetMouseButton(button))
        {
            Visit(_slot);
        }
    }

    // Once per frame: end the drag on release, or drop it if something else changed under us.
    static void Tick(XUi _xui, int _frame)
    {
        if (!active) return;
        // No slot updated for a while: the inventory closed mid-drag.
        if (_frame - lastSeenFrame > 2 || _xui != xui) { Reset(); return; }

        ItemStack held = xui.DragAndDropWindow.CurrentStack;
        if (held.IsEmpty() || held.itemValue.type != startStack.itemValue.type || held.count != startStack.count)
        {
            Reset();
            return;
        }
        if (Input.GetMouseButton(button)) return;

        // Button released this frame (or released while the game was not looking): finish.
        try
        {
            if (visited.Count >= 2) Spread(held);
        }
        catch (Exception e)
        {
            Log.Warning(AzraelDragSpread.Tag + " spread failed: " + e.Message);
        }
        Reset();
    }

    // NGUI stops updating its hovered object while a mouse button is held, so the slot's own isOver can't
    // follow a drag. The mouse's raycast target (UICamera.mouse0.current) is still updated every frame.
    static bool UnderMouse(XUiC_ItemStack _slot)
    {
        XUiView view = _slot.ViewComponent;
        if (view == null || !view.EventOnPress || view.UiTransform == null) return false;
        GameObject go = UICamera.mouse0?.current;
        return go != null && go == view.UiTransform.gameObject;
    }

    static void Visit(XUiC_ItemStack _slot)
    {
        if (visitedSet.Add(_slot)) visited.Add(_slot);
    }

    static void Reset()
    {
        active = false;
        xui = null;
        startStack = null;
        visited.Clear();
        visitedSet.Clear();
    }

    static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    public static bool IsTarget(XUiC_ItemStack _slot) =>
        IsDragging && visitedSet.Contains(_slot) && Room(_slot, startStack) > 0;

    // How many of _held this slot can still take; 0 if it can't take any.
    static int Room(XUiC_ItemStack _slot, ItemStack _held)
    {
        if (_slot == null || _held == null || _held.IsEmpty()) return 0;
        if (_slot.isDragAndDrop || _slot is XUiC_Creative2Stack) return 0;
        if (!_slot.AllowDropping || _slot.IsLocked || _slot.StackLock || _slot.SimpleClick) return 0;
        if (_slot.ViewComponent == null || !_slot.ViewComponent.IsVisible) return 0;
        if (_slot.StackLocation == XUiC_ItemStack.StackLocationTypes.CollectorCatalysts) return 0;
        if (!_held.CanMoveTo(_slot.StackLocation, _slot.SlotNumber)) return 0;

        ItemClass cls = _held.itemValue.ItemClassOrMissing;
        if (cls == null) return 0;

        // Same rule as the vanilla click: quest items stay out of containers.
        if (_slot.StackLocation == XUiC_ItemStack.StackLocationTypes.LootContainer
            && xui != null && xui.DragAndDropWindow.PickUpType != XUiC_ItemStack.StackLocationTypes.LootContainer
            && !cls.CanPlaceInContainer())
            return 0;

        // Workstation fuel/tool/input and dew collector slots only take what they allow. Ask directly instead of
        // CanSwap, which pops a "can't go here" message every time it says no.
        if (_slot is XUiC_RequiredItemStack req)
        {
            if (req.TakeOnly || !req.ItemAllowed(_held)) return 0;
        }
        else if (!_slot.CanSwap(_held)) return 0;

        int max = _slot.OverrideStackCount == -1 ? cls.MaxCount : Mathf.Min(cls.MaxCount, _slot.OverrideStackCount);
        ItemStack cur = _slot.ItemStack;
        if (cur.IsEmpty()) return Mathf.Max(0, max);

        // Top up: same item, both stackable, no quality (same test the vanilla click uses to merge).
        if (cur.itemValue.type != _held.itemValue.type) return 0;
        if (cur.itemValue.HasQuality || _held.itemValue.HasQuality) return 0;
        ItemClass curCls = cur.itemValue.ItemClassOrMissing;
        if (curCls == null || !curCls.CanStack() || !cls.CanStack()) return 0;
        return Mathf.Max(0, max - cur.count);
    }

    static void Spread(ItemStack _held)
    {
        var slots = new List<XUiC_ItemStack>();
        var room = new List<int>();
        foreach (XUiC_ItemStack s in visited)
        {
            int r = Room(s, _held);
            if (r <= 0) continue;
            slots.Add(s);
            room.Add(r);
        }
        if (slots.Count == 0) return;

        int total = _held.count;
        int[] give = new int[slots.Count];
        int remaining = total;

        if (button == 1)
        {
            // Right-drag: one per slot, in the order they were swept.
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                give[i] = 1;
                remaining--;
            }
        }
        else
        {
            // Left-drag: even split. Repeat while there is stock and room, so slots that fill up early pass
            // their share to the others. Odd leftovers go one each to the first slots swept.
            while (remaining > 0)
            {
                var open = new List<int>();
                for (int i = 0; i < slots.Count; i++)
                    if (room[i] - give[i] > 0) open.Add(i);
                if (open.Count == 0) break;

                int share = remaining / open.Count;
                if (share == 0)
                {
                    for (int k = 0; k < open.Count && remaining > 0; k++)
                    {
                        give[open[k]]++;
                        remaining--;
                    }
                    break;
                }
                foreach (int i in open)
                {
                    int g = Mathf.Min(share, room[i] - give[i]);
                    give[i] += g;
                    remaining -= g;
                }
            }
        }

        int placed = 0;
        XUiC_ItemStack soundSlot = null;
        for (int i = 0; i < slots.Count; i++)
        {
            if (give[i] <= 0) continue;
            XUiC_ItemStack s = slots[i];
            ItemStack cur = s.ItemStack;
            ItemStack next;
            if (cur.IsEmpty())
            {
                next = _held.Clone();
                next.count = give[i];
            }
            else
            {
                next = cur.Clone();
                next.count += give[i];
            }
            // The setter fires SlotChangedEvent, which writes into the real inventory/container.
            s.ItemStack = next;
            s.lastClicked = false;
            placed += give[i];
            soundSlot = soundSlot ?? s;
        }
        if (placed <= 0) return;

        ItemStack left = _held.Clone();
        left.count -= placed;
        xui.DragAndDropWindow.SetCurrentStack(left.count > 0 ? left : ItemStack.Empty, playPickupSound: false);
        soundSlot?.PlayPlaceSound();

        foreach (XUiC_ItemStack s in visited) s.lastClicked = false;
    }
}

[HarmonyPatch(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.Update))]
public static class DragSpreadUpdatePatch
{
    static void Prefix(XUiC_ItemStack __instance)
    {
        try { DragSpread.OnSlotUpdate(__instance); }
        catch (Exception e) { Log.Warning(AzraelDragSpread.Tag + " " + e.Message); }
    }
}

// White border on every slot the current drag will put items into.
[HarmonyPatch(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.updateBorderColor))]
public static class DragSpreadBorderPatch
{
    static void Postfix(XUiC_ItemStack __instance)
    {
        if (DragSpread.IsTarget(__instance)) __instance.SelectionBorderColor = __instance.selectColor;
    }
}
