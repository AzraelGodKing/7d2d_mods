using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Helpers for Thralls: per-player limits, bait costs, claims, regeneration, defend range, player lookup.
public static partial class Thralls
{
    static readonly Dictionary<string, int[]> limits = new Dictionary<string, int[]>();
    const string LimitsFile = "AzraelBaitLimits.tsv";

    public static long NowSec() { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

    static string LimitsPath { get { return Path.Combine(GameIO.GetSaveGameDir(), LimitsFile); } }

    static void LoadLimits()
    {
        limits.Clear();
        try
        {
            if (!File.Exists(LimitsPath)) return;
            foreach (string line in File.ReadAllLines(LimitsPath))
            {
                string[] p = line.Split('\t');
                if (p.Length >= 3) limits[p[0]] = new[] { PI(p[1]), PI(p[2]) };
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read limits: " + e.Message); }
    }

    static void SetLimits(string key, int owned, int active)
    {
        if (owned <= 0 && active <= 0) limits.Remove(key); else limits[key] = new[] { Math.Max(0, owned), Math.Max(0, active) };
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in limits) sb.Append(kv.Key).Append('\t').Append(kv.Value[0]).Append('\t').Append(kv.Value[1]).Append('\n');
            File.WriteAllText(LimitsPath, sb.ToString());
        }
        catch (Exception e) { Log.Warning("[Bait] could not save limits: " + e.Message); }
    }

    static int MaxOwnedFor(string key) { int[] l; return limits.TryGetValue(key, out l) && l[0] > 0 ? l[0] : BaitConfig.MaxOwned; }
    static int MaxActiveFor(string key) { int[] l; return limits.TryGetValue(key, out l) && l[1] > 0 ? l[1] : BaitConfig.MaxActive; }

    static bool Claimed(EntityPlayer p, Vector3 pos)
    {
        try
        {
            var pd = GameManager.Instance.GetPersistentPlayerList().GetPlayerDataFromEntityID(p.entityId);
            return W.IsMyLandProtectedBlock(new Vector3i(pos), pd);
        }
        catch { return false; }
    }

    // Takes n bait from the toolbelt first, then the backpack. False (and nothing taken) when there isn't enough.
    static bool TakeBait(EntityPlayer p, int n)
    {
        try
        {
            var iv = ItemClass.GetItem(BaitCheck.BaitItem);
            if (iv == null || p.inventory == null || p.bag == null) return false;
            if (p.inventory.GetItemCount(iv) + p.bag.GetItemCount(iv) < n) return false;
            int left = n - p.inventory.DecItem(iv, n);
            if (left > 0) p.bag.DecItem(iv, left);
            return true;
        }
        catch (Exception e) { Log.Warning("[Bait] could not take bait: " + e.Message); return false; }
    }

    static void Regen(EntityAlive e)
    {
        if (BaitConfig.RegenPctPerTick <= 0f || e.IsDead() || e.GetAttackTarget() != null) return;
        float max = e.GetMaxHealth(), v = e.Stats.Health.Value;
        if (v >= max) return;
        e.Stats.Health.Value = Mathf.Min(max, v + max * BaitConfig.RegenPctPerTick / 100f);
    }

    // Defend mode: a target counts only when it is close to the thrall or to its owner.
    public static bool DefendOk(EntityAlive self, LiveThrall lt, EntityAlive target)
    {
        float r = BaitConfig.DefendRange;
        if ((target.position - self.position).magnitude <= r) return true;
        var o = W != null ? W.GetEntity(lt.OwnerEntityId) : null;
        return o != null && (target.position - o.position).magnitude <= r;
    }

    // Pack animals: each active, non-tethered, standing animal thrall adds bag slots to its owner.
    static int PackSlots(string key)
    {
        if (BaitConfig.PackSlotsPerAnimal <= 0) return 0;
        int n = 0;
        List<ThrallRecord> l;
        if (roster.TryGetValue(key, out l))
            foreach (var r in l) if (r.Active && !r.Tether && IsAnimalClass(r.ClassName) && r.ReviveAt <= NowSec()) n++;
        return Math.Min(BaitConfig.PackSlotsMax, n * BaitConfig.PackSlotsPerAnimal);
    }

    static readonly Dictionary<int, int> lastPack = new Dictionary<int, int>();

    static void ApplyPack(EntityPlayer p, string key)
    {
        if (p.Buffs == null) return;
        int slots = PackSlots(key), prev;
        bool has = p.Buffs.HasBuff("azraelPackPerk");
        if (lastPack.TryGetValue(p.entityId, out prev) && prev == slots && has == (slots > 0)) return;
        p.Buffs.SetCustomVar("$azraelPackSlots", slots);
        if (slots > 0 && !has) p.Buffs.AddBuff("azraelPackPerk");
        else if (slots == 0 && has) p.Buffs.RemoveBuff("azraelPackPerk");
        if (lastPack.TryGetValue(p.entityId, out prev) && prev != slots)
            Tell(p.entityId, slots > 0 ? "Your pack animals carry for you: +" + slots + " bag slots." : "No pack animals with you, so the extra bag slots are gone.");
        lastPack[p.entityId] = slots;
    }

    static string NameOfKey(string key)
    {
        try
        {
            foreach (var pd in GameManager.Instance.GetPersistentPlayerList().Players.Values)
                if (pd.PrimaryId != null && pd.PrimaryId.CombinedString == key) return pd.PlayerName.DisplayName;
        }
        catch { }
        return key;
    }

    static string KeyByName(string name)
    {
        try
        {
            string hit = null;
            foreach (var pd in GameManager.Instance.GetPersistentPlayerList().Players.Values)
            {
                if (pd.PrimaryId == null) continue;
                string n = pd.PlayerName.DisplayName;
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return pd.PrimaryId.CombinedString;
                if (hit == null && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) hit = pd.PrimaryId.CombinedString;
            }
            return hit;
        }
        catch { return null; }
    }
}
