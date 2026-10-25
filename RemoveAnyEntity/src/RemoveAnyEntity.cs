using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

public class AzraelRemoveAnyEntityMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        RaeSettings.ModPath = _modInstance.Path;
        RaeSettings.Load();
        new HarmonyLib.Harmony("azrael.removeanyentity").PatchAll(typeof(AzraelRemoveAnyEntityMod).Assembly);
        Log.Out($"[RemoveAnyEntity] Loaded. Removing: {RaeSettings.Summary()}");
    }
}

public static class RaeSettings
{
    public const string FileName = "entities.txt";
    public static string ModPath;

    // A name also covers its tiered versions: zombieScreamer -> zombieScreamerFeral, ...Radiated, ...
    static readonly string[] TierSuffixes = { "", "Feral", "Radiated", "Charged", "Infernal" };

    public static readonly List<string> Removed = new List<string>();
    public static readonly List<string> ReplacementNames = new List<string>();
    public static readonly List<string> Problems = new List<string>();

    static readonly string[] DefaultReplacements =
        { "zombieArlene", "zombieBoe", "zombieJoe", "zombieMarlene", "zombieMoe", "zombieDarlene", "zombieYo", "zombieSteve", "zombieBusinessMan", "zombieJanitor" };

    public static void Load()
    {
        Removed.Clear(); ReplacementNames.Clear(); Problems.Clear();
        var path = Path.Combine(ModPath ?? "", FileName);
        bool sawReplacement = false;
        try
        {
            if (!File.Exists(path)) { Problems.Add($"{FileName} not found, nothing is removed"); }
            else
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim().Equals("Replacement", StringComparison.OrdinalIgnoreCase))
                    {
                        sawReplacement = true;
                        foreach (var n in line.Substring(eq + 1).Split(','))
                            if (n.Trim().Length > 0) ReplacementNames.Add(n.Trim());
                        continue;
                    }
                    // First word is the entity name; anything after it is just a note.
                    var name = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (!Removed.Contains(name, StringComparer.OrdinalIgnoreCase)) Removed.Add(name);
                }
            }
        }
        catch (Exception e) { Problems.Add($"could not read {FileName}: {e.Message}"); }
        if (!sawReplacement) ReplacementNames.AddRange(DefaultReplacements);
    }

    // True if className is the listed name or one of its tiered versions.
    public static bool Matches(string className, string listed)
    {
        foreach (var suffix in TierSuffixes)
            if (className.Equals(listed + suffix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsRemoved(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        foreach (var r in Removed) if (Matches(className, r)) return true;
        return false;
    }

    // Turn a name on (remove it from spawning) or off in entities.txt: uncomment or comment
    // its existing line, or add a line at the end. Returns what changed, for the console.
    public static string SetRemoved(string name, bool removed)
    {
        var path = Path.Combine(ModPath ?? "", FileName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        bool found = false, changed = false;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            bool commented = line.StartsWith("#");
            // Only "#name" with no space after the # is an entity line; "# text" is a comment.
            var body = commented ? line.Substring(1) : line;
            if (body.Length == 0 || char.IsWhiteSpace(body[0])) continue;
            var first = body.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (!first.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            found = true;
            if (commented == removed)
            {
                lines[i] = removed ? body : "#" + line;
                changed = true;
            }
        }
        if (!found && removed) { lines.Add(name); changed = true; }
        if (changed) File.WriteAllLines(path, lines);
        if (!found && !removed) return $"'{name}' isn't in {FileName}, so it already spawns.";
        if (!changed) return removed ? $"'{name}' was already removed." : $"'{name}' was already spawning.";
        return removed ? $"'{name}' removed in {FileName}." : $"'{name}' spawns again ({FileName} line commented out).";
    }

    public static string Summary() => Removed.Count == 0 ? "nothing (all lines in entities.txt are commented out)" : string.Join(", ", Removed);
}

// What happened during the last entitygroups load, for the 'rae' console command.
public static class RaeStats
{
    public static int GroupsChanged, EntriesRemoved, GroupsRefilled;
    public static readonly List<int> ReplacementIds = new List<int>();
    public static readonly List<string> UnknownNames = new List<string>();
    public static readonly HashSet<string> ClassesRemoved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static void Reset()
    {
        GroupsChanged = EntriesRemoved = GroupsRefilled = 0;
        ReplacementIds.Clear(); UnknownNames.Clear(); ClassesRemoved.Clear();
    }
}

// The spawn groups as entitygroups.xml defined them, before anything was removed,
// so 'rae reload' can rebuild them after entities.txt changes without a restart.
public static class RaeOriginal
{
    public static readonly Dictionary<string, List<SEntityClassAndProb>> Groups =
        new Dictionary<string, List<SEntityClassAndProb>>(StringComparer.Ordinal);
}

static class RaeUtil
{
    public static string ClassName(int id) =>
        EntityClass.list.TryGetValue(id, out var ec) && ec != null ? ec.entityClassName : null;

    public static bool IsNone(int id) => id == SEntityClassAndProb.cIdNone || ClassName(id) == null;

    public static void BuildReplacementIds()
    {
        RaeStats.ReplacementIds.Clear();
        foreach (var n in RaeSettings.ReplacementNames)
        {
            if (RaeSettings.IsRemoved(n)) continue;
            int id = EntityClass.FromString(n);
            if (id != 0 && ClassName(id) != null) RaeStats.ReplacementIds.Add(id);
            else RaeSettings.Problems.Add($"Replacement '{n}' is not an entity in this game");
        }
    }

    public static void CheckNames()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in EntityClass.list.Dict)
            if (kv.Value != null) known.Add(kv.Value.entityClassName);
        foreach (var r in RaeSettings.Removed)
        {
            if (!known.Any(k => RaeSettings.Matches(k, r))) RaeStats.UnknownNames.Add(r);
        }
    }
}

// Before entitygroups.xml is read: re-read entities.txt and check the names.
[HarmonyLib.HarmonyPatch(typeof(EntityGroupsFromXml), nameof(EntityGroupsFromXml.LoadEntityGroups))]
public static class RaeLoadPatch
{
    static void Prefix()
    {
        try
        {
            RaeSettings.Load();
            RaeStats.Reset();
            RaeOriginal.Groups.Clear();
            RaeUtil.BuildReplacementIds();
            RaeUtil.CheckNames();
            foreach (var p in RaeSettings.Problems) Log.Warning("[RemoveAnyEntity] " + p);
            foreach (var u in RaeStats.UnknownNames) Log.Warning($"[RemoveAnyEntity] '{u}' in {RaeSettings.FileName} doesn't match any entity (typo?)");
        }
        catch (Exception e) { Log.Warning("[RemoveAnyEntity] " + e.Message); }
    }
}

// After each <entitygroup> is read: take the removed entities out of it.
[HarmonyLib.HarmonyPatch(typeof(EntityGroupsFromXml), "parseGroup")]
public static class RaeGroupPatch
{
    static void Postfix(XElement _elementGroup)
    {
        try
        {
            var name = _elementGroup.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name) || !EntityGroups.list.TryGetValue(name, out var list) || list == null) return;
            RaeOriginal.Groups[name] = new List<SEntityClassAndProb>(list);
            RaeFilter.Apply(name, list);
        }
        catch (Exception e) { Log.Warning("[RemoveAnyEntity] group patch: " + e.Message); }
    }
}

public static class RaeFilter
{
    // Take the removed entities out of one group's list, in place.
    public static void Apply(string name, List<SEntityClassAndProb> list)
    {
        if (RaeSettings.Removed.Count == 0) return;

        var kept = new List<SEntityClassAndProb>(list.Count);
        int removedHere = 0;
        foreach (var e in list)
        {
            var cls = RaeUtil.ClassName(e.entityClassId);
            if (cls != null && RaeSettings.IsRemoved(cls)) { removedHere++; RaeStats.ClassesRemoved.Add(cls); }
            else kept.Add(e);
        }
        if (removedHere == 0) return;

        bool anyReal = kept.Any(e => !RaeUtil.IsNone(e.entityClassId));
        if (!anyReal)
        {
            if (RaeStats.ReplacementIds.Count == 0)
            {
                // Nothing valid to put in: leave this group as it was rather than break it.
                Log.Warning($"[RemoveAnyEntity] Group '{name}' would be empty and there's no usable Replacement; left unchanged.");
                return;
            }
            // Keep the original "nothing spawns" share, and give the rest to the replacements.
            float noneShare = kept.Sum(e => e.prob);
            float each = Math.Max(0.0001f, 1f - noneShare) / RaeStats.ReplacementIds.Count;
            var refill = RaeStats.ReplacementIds.Select(id => new SEntityClassAndProb { entityClassId = id, prob = each }).ToList();
            kept.InsertRange(0, refill);
            RaeStats.GroupsRefilled++;
        }

        // A "none" entry must never be first: EntityGroups.IsEnemyGroup reads the first entry's class.
        var ordered = kept.Where(e => !RaeUtil.IsNone(e.entityClassId)).Concat(kept.Where(e => RaeUtil.IsNone(e.entityClassId))).ToList();

        // The game picks by walking cumulative chances, so rescale to a total of 1 again.
        float total = ordered.Sum(e => e.prob);
        if (total > 0f)
            for (int i = 0; i < ordered.Count; i++) { var e = ordered[i]; e.prob /= total; ordered[i] = e; }

        list.Clear();
        list.AddRange(ordered);
        RaeStats.GroupsChanged++;
        RaeStats.EntriesRemoved += removedHere;
    }

    // Re-read entities.txt and rebuild every group from its original entries. Returns false
    // if no world has loaded the groups yet. Lists are changed in place, so anything holding
    // a reference to one sees the new contents.
    public static bool Reload()
    {
        RaeSettings.Load();
        if (RaeOriginal.Groups.Count == 0) return false;
        RaeStats.Reset();
        RaeUtil.BuildReplacementIds();
        RaeUtil.CheckNames();
        foreach (var kv in RaeOriginal.Groups)
        {
            if (!EntityGroups.list.TryGetValue(kv.Key, out var list) || list == null) continue;
            list.Clear();
            list.AddRange(kv.Value);
            try { Apply(kv.Key, list); }
            catch (Exception e) { Log.Warning($"[RemoveAnyEntity] reload '{kv.Key}': " + e.Message); }
        }
        foreach (var p in RaeSettings.Problems) Log.Warning("[RemoveAnyEntity] " + p);
        foreach (var u in RaeStats.UnknownNames) Log.Warning($"[RemoveAnyEntity] '{u}' in {RaeSettings.FileName} doesn't match any entity (typo?)");
        Log.Out($"[RemoveAnyEntity] Reloaded. Removed {RaeStats.EntriesRemoved} spawn entries from {RaeStats.GroupsChanged} groups. Removing: {RaeSettings.Summary()}");
        return true;
    }
}

// Log a one-line result once all groups are read.
[HarmonyLib.HarmonyPatch(typeof(EntityGroupsFromXml), nameof(EntityGroupsFromXml.LoadEntityGroups))]
public static class RaeLoadDonePatch
{
    static System.Collections.IEnumerator Postfix(System.Collections.IEnumerator __result)
    {
        while (__result.MoveNext()) yield return __result.Current;
        if (RaeSettings.Removed.Count > 0)
            Log.Out($"[RemoveAnyEntity] Removed {RaeStats.EntriesRemoved} spawn entries from {RaeStats.GroupsChanged} groups " +
                    $"({RaeStats.GroupsRefilled} refilled with replacements). Entities removed: {string.Join(", ", RaeStats.ClassesRemoved.OrderBy(x => x))}.");
    }
}

// Twitch integration actions that spawn entities by name.
[HarmonyLib.HarmonyPatch(typeof(GameEvent.SequenceActions.ActionBaseSpawn), nameof(GameEvent.SequenceActions.ActionBaseSpawn.SetupEntityIDs))]
public static class RaeTwitchPatch
{
    static bool warnedNoReplacement;

    static void Postfix(GameEvent.SequenceActions.ActionBaseSpawn __instance)
    {
        try
        {
            if (RaeSettings.Removed.Count == 0 || __instance.entityIDs == null || __instance.entityIDs.Count == 0) return;
            var kept = __instance.entityIDs.Where(id => !RaeSettings.IsRemoved(RaeUtil.ClassName(id))).ToList();
            if (kept.Count == __instance.entityIDs.Count) return;
            if (kept.Count == 0) kept.AddRange(RaeStats.ReplacementIds);
            // An empty list makes the action call SetupEntityIDs again every tick and never finish,
            // so with nothing usable to put in, leave it as it was (same as an emptied spawn group).
            if (kept.Count == 0)
            {
                if (!warnedNoReplacement)
                {
                    warnedNoReplacement = true;
                    Log.Warning("[RemoveAnyEntity] A Twitch spawn would be empty and there's no usable Replacement; left unchanged.");
                }
                return;
            }
            __instance.entityIDs.Clear();
            __instance.entityIDs.AddRange(kept);
        }
        catch (Exception e) { Log.Warning("[RemoveAnyEntity] twitch patch: " + e.Message); }
    }
}

public class ConsoleCmdAzraelRemoveAnyEntity : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient => false;
    public override bool AllowedInMainMenu => true;
    public override int DefaultPermissionLevel => 0;
    public override string[] getCommands() => new[] { "rae", "removeanyentity" };
    public override string getDescription() => "Remove Any Entity: show what's removed from spawning";
    public override string getHelp() => "rae               - show the removed entities, and what changed when the world loaded\n" +
                                        "rae group <name>  - list what a spawn group can spawn now, with chances (e.g. rae group ZombiesAll)\n" +
                                        "rae reload        - re-read entities.txt and apply it now, no restart\n" +
                                        "rae remove <name> - stop <name> (and its tiers) spawning, saved to entities.txt, applied now\n" +
                                        "rae keep <name>   - let <name> spawn again, saved to entities.txt, applied now\n" +
                                        "Creatures already in the world stay.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        var sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (sub == "remove" || sub == "keep")
        {
            if (_params.Count < 2) { con.Output($"Usage: rae {sub} <entity name>, e.g. rae {sub} zombieScreamer"); return; }
            try { con.Output(RaeSettings.SetRemoved(_params[1], sub == "remove")); }
            catch (Exception e) { con.Output($"Could not change {RaeSettings.FileName}: {e.Message}"); return; }
            sub = "reload";
        }
        if (sub == "reload")
        {
            if (!RaeFilter.Reload())
            {
                con.Output($"{RaeSettings.FileName} re-read. No world is loaded yet, so it applies when one loads.");
                return;
            }
            con.Output($"Applied. Removed {RaeStats.EntriesRemoved} spawn entries from {RaeStats.GroupsChanged} groups " +
                       $"({RaeStats.GroupsRefilled} refilled). Removing: {RaeSettings.Summary()}");
            foreach (var u in RaeStats.UnknownNames) con.Output($"PROBLEM: '{u}' doesn't match any entity (typo?)");
            foreach (var p in RaeSettings.Problems) con.Output("PROBLEM: " + p);
            if (_params.Count >= 2 && _params[0].Equals("keep", StringComparison.OrdinalIgnoreCase) && RaeSettings.IsRemoved(_params[1]))
                con.Output($"'{_params[1]}' is still removed by a shorter name in {RaeSettings.FileName} (a name covers its tiers).");
            con.Output("New spawns use this now. Creatures already in the world stay.");
            return;
        }
        if (_params.Count >= 2 && _params[0].Equals("group", StringComparison.OrdinalIgnoreCase))
        {
            if (!EntityGroups.list.TryGetValue(_params[1], out var grp) || grp == null)
            {
                var similar = EntityGroups.list.Dict.Keys.Where(k => k.IndexOf(_params[1], StringComparison.OrdinalIgnoreCase) >= 0).Take(15).ToList();
                con.Output($"No group called '{_params[1]}'." + (similar.Count > 0 ? " Did you mean: " + string.Join(", ", similar) : ""));
                return;
            }
            con.Output($"Group '{_params[1]}' ({grp.Count} entries):");
            foreach (var e in grp) con.Output($"  {RaeUtil.ClassName(e.entityClassId) ?? "none"}  {e.prob * 100f:0.#}%");
            return;
        }
        con.Output("Remove Any Entity - listed in entities.txt: " + RaeSettings.Summary());
        if (RaeStats.GroupsChanged > 0 || RaeStats.EntriesRemoved > 0)
            con.Output($"Now: removed {RaeStats.EntriesRemoved} spawn entries from {RaeStats.GroupsChanged} groups, " +
                       $"{RaeStats.GroupsRefilled} groups refilled with replacements.");
        if (RaeStats.ClassesRemoved.Count > 0)
            con.Output("Entities no longer spawning: " + string.Join(", ", RaeStats.ClassesRemoved.OrderBy(x => x)));
        var reps = RaeStats.ReplacementIds.Select(RaeUtil.ClassName).Where(n => n != null).ToList();
        con.Output("Replacements: " + (reps.Count > 0 ? string.Join(", ", reps) : "(none usable)"));
        foreach (var u in RaeStats.UnknownNames) con.Output($"PROBLEM: '{u}' doesn't match any entity (typo?)");
        foreach (var p in RaeSettings.Problems) con.Output("PROBLEM: " + p);
        con.Output("Changed entities.txt? Type 'rae reload' to apply it now.");
    }
}
