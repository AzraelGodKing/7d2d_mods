using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Mod upgrades (upgrades.txt): two of the same listed mod at the same quality + the listed materials from your
// backpack/toolbelt = one mod a quality level higher. This file works out the materials; UpgradeBench.cs checks
// the two mods.
//
// upgrades.txt follows the requester's sheet, one row per item:
//   <item> = <recipe item #1> or <#2> or <#3> : <item amounts 1->2 .. 5->6> : <metal amounts 1->2 .. 5->6>
// The metal is Iron (sheet column G) for upgrades to Q2/Q3 and Steel (column H) for upgrades to Q4/Q5/Q6.
public static class ModUpgrades
{
    public class Cost { public string Item; public int Count; }

    public class Entry
    {
        public string Name;
        public List<string> Items = new List<string>();     // alternatives: any ONE of these (columns D, E, F)
        public int[] Counts = new int[0];                   // item amount per step: [0] = 1->2, [1] = 2->3, ...
        public int[] MetalCounts = new int[0];              // iron/steel amount per step
    }

    static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    static string ironItem = "resourceForgedIron";          // column G: upgrades to Q2 and Q3
    static string steelItem = "resourceForgedSteel";        // column H: upgrades to Q4, Q5 and Q6
    const int LastIronQuality = 3;
    static bool validated;

    // Metal for the step that upgrades TO quality 'toQuality'.
    static string MetalFor(int toQuality) => toQuality <= LastIronQuality ? ironItem : steelItem;

    public static void Load(string path)
    {
        entries.Clear(); validated = false;
        ironItem = "resourceForgedIron"; steelItem = "resourceForgedSteel";
        if (!File.Exists(path)) { Log.Warning("[UpgradeBench] upgrades.txt not found; single-item upgrades are off."); return; }
        int lineNo = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash).Trim();
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) { Warn(lineNo, "no '='"); continue; }
            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();
            try
            {
                if (key.Equals("Iron", StringComparison.OrdinalIgnoreCase)) { ironItem = val; continue; }
                if (key.Equals("Steel", StringComparison.OrdinalIgnoreCase)) { steelItem = val; continue; }

                var parts = val.Split(':');
                if (parts.Length != 3) { Warn(lineNo, "expected  item(s) : item amounts : iron/steel amounts"); continue; }
                var e = new Entry { Name = key };
                foreach (var alt in parts[0].Split(new[] { " or ", "/" }, StringSplitOptions.RemoveEmptyEntries))
                    if (alt.Trim().Length > 0) e.Items.Add(alt.Trim());
                e.Counts = Numbers(parts[1]);
                e.MetalCounts = Numbers(parts[2]);
                entries[key] = e;
            }
            catch (Exception ex) { Warn(lineNo, ex.Message); }
        }
        Log.Out($"[UpgradeBench] upgrades.txt: {entries.Count} upgradable item(s). Metal: {ironItem} up to Q{LastIronQuality}, {steelItem} after.");
    }

    static int[] Numbers(string s) =>
        s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(int.Parse).ToArray();

    static void Warn(int line, string why) => Log.Warning($"[UpgradeBench] upgrades.txt line {line} skipped: {why}");

    // Checks every name once the game's items are loaded, so typos show up in the log.
    static void Validate()
    {
        if (validated) return;
        validated = true;
        foreach (var m in new[] { ironItem, steelItem })
            if (ItemClass.GetItemClass(m) == null) Log.Warning($"[UpgradeBench] upgrades.txt: unknown item '{m}' (Iron/Steel).");
        foreach (var e in entries.Values)
        {
            if (ItemClass.GetItemClass(e.Name) == null) Log.Warning($"[UpgradeBench] upgrades.txt: unknown item '{e.Name}'.");
            foreach (var i in e.Items)
                if (ItemClass.GetItemClass(i) == null) Log.Warning($"[UpgradeBench] upgrades.txt: unknown item '{i}' (for {e.Name}).");
        }
    }

    public enum State { NotListed, MaxQuality, NotEnough, Ready }

    public class Plan
    {
        public State State;
        public ItemValue Result;
        public List<Cost> Pay = new List<Cost>();      // what will be taken
        public string Text;                            // tooltip text
        public List<string[]> Rows = new List<string[]>(); // result panel rows: { title, value } (value is "have/need", coloured)
    }

    public const string Green = "[7FE37F]", Red = "[FF6060]", Grey = "[9A9A9A]";

    public static string HaveNeed(int have, int need, string colorIfShort = Red) =>
        (Math.Max(0, have) >= need ? Green : colorIfShort) + Math.Max(0, have) + " / " + need + "[-]";

    public static Plan Make(ItemStack stack, XUiM_PlayerInventory inv)
    {
        Validate();
        var plan = new Plan { State = State.NotListed };
        var cls = stack.itemValue.ItemClass;
        if (cls == null || !entries.TryGetValue(cls.GetItemName(), out var e)) return plan;

        string itemName = cls.GetLocalizedItemName();
        int q = stack.itemValue.Quality;
        int step = q - 1;                                  // Q1->2 is step 0
        if (q >= ItemClass.MaxQualityTier || step < 0 || (step >= e.Counts.Length && step >= e.MetalCounts.Length))
        {
            plan.State = State.MaxQuality;
            plan.Text = string.Format(Localization.Get("ttAzraelUpgradeMax"), itemName, q);
            return plan;
        }

        bool short_ = false;
        var lines = new List<string>();

        // Iron for upgrades to Q2/Q3, steel for Q4/Q5/Q6 (sheet columns G/H), amount from this item's row.
        int metalCount = step < e.MetalCounts.Length ? e.MetalCounts[step] : 0;
        Cost m = metalCount > 0 ? new Cost { Item = MetalFor(q + 1), Count = metalCount } : null;
        // If the item and the metal are the same thing (e.g. forged iron), you need enough for both.
        int Reserved(string item) => m != null && string.Equals(m.Item, item, StringComparison.OrdinalIgnoreCase) ? m.Count : 0;

        // The item: any one of the alternatives (first one you have enough of).
        int itemCount = step < e.Counts.Length ? e.Counts[step] : 0;
        string pick = null;
        if (itemCount > 0 && e.Items.Count > 0)
        {
            pick = e.Items.FirstOrDefault(i => Have(inv, i) - Reserved(i) >= itemCount);
            if (pick != null) plan.Pay.Add(new Cost { Item = pick, Count = itemCount });
            else short_ = true;

            // Result panel rows: each choice with have/need. If one choice is covered, the others are greyed.
            for (int k = 0; k < e.Items.Count; k++)
            {
                string alt = e.Items[k];
                int h = Have(inv, alt) - Reserved(alt);
                string title = (k == 0 ? "" : Localization.Get("xuiAzraelUpgradeOr") + " ") + Name(alt);
                plan.Rows.Add(new[] { title, HaveNeed(h, itemCount, pick != null ? Grey : Red) });
            }

            if (e.Items.Count == 1)
                lines.Add(Line(itemCount, e.Items[0], Have(inv, e.Items[0]) - Reserved(e.Items[0])));
            else
            {
                // "6 of any ONE of these:" then each choice with how many you have.
                lines.Add(string.Format(Localization.Get("ttAzraelUpgradeAnyOf"), itemCount));
                foreach (var alt in e.Items)
                    lines.Add(string.Format(Localization.Get("ttAzraelUpgradeAnyOfItem"), Name(alt), Math.Max(0, Have(inv, alt) - Reserved(alt))));
            }
        }
        // The metal for this step.
        if (m != null)
        {
            int usedByItem = pick != null && string.Equals(pick, m.Item, StringComparison.OrdinalIgnoreCase) ? itemCount : 0;
            int have = Have(inv, m.Item) - usedByItem;
            if (have >= m.Count) plan.Pay.Add(m);
            else short_ = true;
            lines.Add(Line(m.Count, m.Item, have));
            plan.Rows.Add(new[] { Name(m.Item), HaveNeed(have, m.Count) });
        }

        var result = stack.itemValue.Clone();
        result.Quality = (ushort)(q + 1);
        if (result.Modifications != null) Array.Resize(ref result.Modifications, result.CalcModSlotCount());
        plan.Result = result;

        plan.State = short_ ? State.NotEnough : State.Ready;
        var sb = new StringBuilder();
        sb.Append(string.Format(Localization.Get("ttAzraelUpgradeHeader"), itemName, q, q + 1));
        foreach (var l in lines) sb.Append('\n').Append(l);
        sb.Append('\n').Append(Localization.Get(short_ ? "ttAzraelUpgradeNotReady" : "ttAzraelUpgradeReady"));
        plan.Text = sb.ToString();
        return plan;
    }

    // "- 20 Forged Iron (you have 50)"  or  "- 20 Forged Iron (you have 5, need 15 more)"
    static string Line(int need, string item, int have)
    {
        have = Math.Max(0, have);
        return have >= need
            ? string.Format(Localization.Get("ttAzraelUpgradeLine"), need, Name(item), have)
            : string.Format(Localization.Get("ttAzraelUpgradeLineShort"), need, Name(item), have, need - have);
    }

    static int Have(XUiM_PlayerInventory inv, string item)
    {
        var iv = ItemClass.GetItem(item);
        return iv == null || iv.IsEmpty() ? 0 : inv.GetItemCount(iv);
    }

    static string Name(string item)
    {
        var c = ItemClass.GetItemClass(item);
        return c != null ? c.GetLocalizedItemName() : item;
    }

    public static List<ItemStack> ToStacks(List<Cost> pay) =>
        pay.Select(c => new ItemStack(ItemClass.GetItem(c.Item), c.Count)).ToList();
}

