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
    // "animalCoyote in desert, wasteland": removed only from the open-world spawns in those biomes.
    public static readonly Dictionary<string, HashSet<string>> BiomeRemoved =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    // "animalZombieDog -> animalWolf": swapped for another entity at the same chance.
    public static readonly Dictionary<string, string> Replaced = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public static readonly List<string> ReplacementNames = new List<string>();
    public static readonly List<string> Problems = new List<string>();

    static readonly string[] DefaultReplacements =
        { "zombieArlene", "zombieBoe", "zombieJoe", "zombieMarlene", "zombieMoe", "zombieDarlene", "zombieYo", "zombieSteve", "zombieBusinessMan", "zombieJanitor" };

    public static void Load()
    {
        Removed.Clear(); BiomeRemoved.Clear(); Replaced.Clear(); ReplacementNames.Clear(); Problems.Clear();
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
                    ParseLine(line);
                }
            }
        }
        catch (Exception e) { Problems.Add($"could not read {FileName}: {e.Message}"); }
        if (!sawReplacement) ReplacementNames.AddRange(DefaultReplacements);
    }

    // Biome names as written in biomes.xml, plus a few shorter ones people will type.
    public static readonly string[] BiomeNames = { "pine_forest", "burnt_forest", "desert", "snow", "wasteland" };
    static readonly Dictionary<string, string> BiomeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "forest", "pine_forest" }, { "pine", "pine_forest" }, { "pineforest", "pine_forest" },
        { "burnt", "burnt_forest" }, { "burned", "burnt_forest" }, { "burntforest", "burnt_forest" },
        { "winter", "snow" }, { "waste", "wasteland" },
    };

    public static string BiomeName(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return BiomeAliases.TryGetValue(t, out var full) ? full : t;
    }

    static string[] Words(string text) => text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    // One active line of entities.txt:
    //   name [note]                 removed everywhere
    //   name in biome, biome        removed from those biomes' open-world spawns
    //   name -> other               replaced by other
    static void ParseLine(string line)
    {
        var words = Words(line.Replace("->", " -> "));
        var name = words[0];
        if (words.Length >= 3 && words[1] == "->")
        {
            Replaced[name] = words[2];
            return;
        }
        if (words.Length >= 3 && words[1].Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            var biomes = string.Join(" ", words.Skip(2)).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(BiomeName).ToList();
            foreach (var b in biomes)
                if (!BiomeNames.Contains(b)) Problems.Add($"'{b}' on the {name} line isn't a biome (use {string.Join(", ", BiomeNames)})");
            if (!BiomeRemoved.TryGetValue(name, out var set)) BiomeRemoved[name] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.UnionWith(biomes);
            return;
        }
        // First word is the entity name; anything after it is just a note.
        if (!Removed.Contains(name, StringComparer.OrdinalIgnoreCase)) Removed.Add(name);
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

    // Removed only in this biome (by a "name in biome" line)?
    public static bool IsRemovedIn(string className, string biome)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(biome) || BiomeRemoved.Count == 0) return false;
        foreach (var kv in BiomeRemoved)
            if (kv.Value.Contains(biome) && Matches(className, kv.Key)) return true;
        return false;
    }

    // The name this class is swapped for, or null.
    public static string ReplacementFor(string className)
    {
        if (string.IsNullOrEmpty(className) || Replaced.Count == 0) return null;
        // The longest listed name wins, so "zombieScreamerRadiated -> x" beats "zombieScreamer -> y".
        string best = null, target = null;
        foreach (var kv in Replaced)
            if (Matches(className, kv.Key) && (best == null || kv.Key.Length > best.Length)) { best = kv.Key; target = kv.Value; }
        return target;
    }

    // Write one entity's line in entities.txt. newBody null comments out every active line for
    // that name (it spawns as normal again). Otherwise the first line for the name (commented or
    // not) becomes newBody and any other active line for it is commented out. Returns true if the
    // file changed.
    public static bool SetLine(string name, string newBody, out bool found)
    {
        var path = Path.Combine(ModPath ?? "", FileName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        found = false;
        bool changed = false, placed = false;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            bool commented = line.StartsWith("#");
            var body = commented ? line.Substring(1) : line;
            if (body.Length == 0 || char.IsWhiteSpace(body[0])) continue;
            var first = Words(body.Replace("->", " -> "))[0];
            if (!first.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!commented) found = true;
            if (newBody != null && !placed)
            {
                // A plain removal keeps the line's note ("#zombieScreamer   screamer: ..." -> "zombieScreamer   screamer: ...").
                var w = Words(body.Replace("->", " -> "));
                var plainLine = w.Length < 2 || (!w[1].Equals("in", StringComparison.OrdinalIgnoreCase) && w[1] != "->");
                var next = newBody.Equals(name, StringComparison.OrdinalIgnoreCase) && plainLine ? body : newBody;
                if (lines[i] != next) { lines[i] = next; changed = true; }
                placed = true;
            }
            else if (!commented) { lines[i] = "#" + line; changed = true; }
        }
        if (newBody != null && !placed) { lines.Add(newBody); changed = true; }
        if (changed) File.WriteAllLines(path, lines);
        return changed;
    }

    // Turn a name on (remove it from spawning) or off in entities.txt: uncomment or comment
    // its existing line, or add a line at the end. Returns what changed, for the console.
    public static string SetRemoved(string name, bool removed)
    {
        bool changed = SetLine(name, removed ? name : null, out var found);
        if (!found && !removed) return $"'{name}' isn't in {FileName}, so it already spawns.";
        if (!changed) return removed ? $"'{name}' was already removed." : $"'{name}' was already spawning.";
        return removed ? $"'{name}' removed in {FileName}." : $"'{name}' spawns again ({FileName} line commented out).";
    }

    public static string SetBiomeRemoved(string name, IEnumerable<string> biomes)
    {
        var list = biomes.Select(BiomeName).Distinct().ToList();
        var bad = list.Where(b => !BiomeNames.Contains(b)).ToList();
        if (list.Count == 0 || bad.Count > 0)
            return $"'{string.Join(", ", bad)}' isn't a biome. Use: {string.Join(", ", BiomeNames)}";
        SetLine(name, $"{name} in {string.Join(", ", list)}", out _);
        return $"'{name}' removed from open-world spawns in {string.Join(", ", list)} ({FileName}).";
    }

    public static string SetReplaced(string name, string target)
    {
        SetLine(name, $"{name} -> {target}", out _);
        return $"'{name}' is replaced by '{target}' ({FileName}).";
    }

    public static string Summary()
    {
        var parts = new List<string>(Removed);
        parts.AddRange(BiomeRemoved.Select(kv => $"{kv.Key} (in {string.Join(", ", kv.Value)})"));
        parts.AddRange(Replaced.Select(kv => $"{kv.Key} -> {kv.Value}"));
        return parts.Count == 0 ? "nothing (all lines in entities.txt are commented out)" : string.Join(", ", parts);
    }

    public static bool AnyRules => Removed.Count > 0 || BiomeRemoved.Count > 0 || Replaced.Count > 0;
}

// What happened during the last entitygroups load, for the 'rae' console command.
public static class RaeStats
{
    public static int GroupsChanged, EntriesRemoved, GroupsRefilled, EntriesReplaced;
    public static readonly List<int> ReplacementIds = new List<int>();
    // Replace rules resolved to entity ids: listed name -> target id.
    public static readonly Dictionary<string, int> ReplaceIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public static readonly List<string> UnknownNames = new List<string>();
    public static readonly HashSet<string> ClassesRemoved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static void Reset()
    {
        GroupsChanged = EntriesRemoved = GroupsRefilled = EntriesReplaced = 0;
        ReplacementIds.Clear(); ReplaceIds.Clear(); UnknownNames.Clear(); ClassesRemoved.Clear();
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
        foreach (var kv in RaeSettings.Replaced)
        {
            int id = EntityClass.FromString(kv.Value);
            if (id == 0 || ClassName(id) == null) RaeSettings.Problems.Add($"'{kv.Key} -> {kv.Value}': '{kv.Value}' is not an entity in this game, so {kv.Key} spawns as normal");
            else if (RaeSettings.IsRemoved(kv.Value)) RaeSettings.Problems.Add($"'{kv.Key} -> {kv.Value}': '{kv.Value}' is removed too, so {kv.Key} is just removed");
            else RaeStats.ReplaceIds[kv.Key] = id;
        }
    }

    // The id this class swaps to, or 0 if it isn't replaced (or its target isn't usable).
    public static int ReplaceId(string className)
    {
        if (string.IsNullOrEmpty(className) || RaeStats.ReplaceIds.Count == 0) return 0;
        string best = null; int id = 0;
        foreach (var kv in RaeSettings.Replaced)
            if (RaeSettings.Matches(className, kv.Key) && (best == null || kv.Key.Length > best.Length)) { best = kv.Key; RaeStats.ReplaceIds.TryGetValue(kv.Key, out id); }
        return id;
    }

    // Bait's tamed zombies (thralls) must never be culled. Bait is optional, so look it up by name.
    static Func<int, bool> isThrall;
    static bool lookedForBait;
    public static bool IsThrall(int entityId)
    {
        if (!lookedForBait)
        {
            lookedForBait = true;
            try
            {
                var m = HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("Thralls"), "IsThrall", new[] { typeof(int) });
                if (m != null) isThrall = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), m);
            }
            catch { }
        }
        try { return isThrall != null && isThrall(entityId); } catch { return false; }
    }

    // Despawn living creatures that shouldn't be spawning: removed everywhere, removed in the
    // biome they're standing in, or replaced. With only set, just that name (and its tiers),
    // listed or not. Players, Bait thralls and anything dead are left alone.
    public static int Cull(string only, bool dryRun, Dictionary<string, int> counts)
    {
        var world = GameManager.Instance?.World;
        if (world == null) return -1;
        var targets = new List<Entity>();
        foreach (var e in world.Entities.list.ToList())
        {
            if (!(e is EntityAlive ea) || e is EntityPlayer || ea.IsDead() || e is EntityVehicle) continue;
            var cls = ClassName(e.entityClass);
            if (cls == null) continue;
            bool hit;
            if (only != null) hit = RaeSettings.Matches(cls, only);
            else
            {
                hit = RaeSettings.IsRemoved(cls) || RaeSettings.ReplacementFor(cls) != null;
                if (!hit && RaeSettings.BiomeRemoved.Count > 0)
                {
                    var b = world.GetBiome((int)e.position.x, (int)e.position.z);
                    hit = b != null && RaeSettings.IsRemovedIn(cls, b.m_sBiomeName);
                }
            }
            if (!hit || IsThrall(e.entityId)) continue;
            targets.Add(e);
            counts.TryGetValue(cls, out var n);
            counts[cls] = n + 1;
        }
        if (!dryRun)
            foreach (var e in targets) world.RemoveEntity(e.entityId, EnumRemoveEntityReason.Despawned);
        return targets.Count;
    }

    public static void CheckNames()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in EntityClass.list.Dict)
            if (kv.Value != null) known.Add(kv.Value.entityClassName);
        foreach (var r in RaeSettings.Removed.Concat(RaeSettings.BiomeRemoved.Keys).Concat(RaeSettings.Replaced.Keys))
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
        if (RaeSettings.Removed.Count == 0 && RaeStats.ReplaceIds.Count == 0) return;

        // The game decides whether a group is zombies or animals from its first entry; keep that.
        var firstReal = list.FirstOrDefault(e => !RaeUtil.IsNone(e.entityClassId));
        bool wasEnemy = !RaeUtil.IsNone(firstReal.entityClassId) && EntityClass.list[firstReal.entityClassId].bIsEnemyEntity;

        var kept = new List<SEntityClassAndProb>(list.Count);
        int removedHere = 0, replacedHere = 0;
        foreach (var e in list)
        {
            var cls = RaeUtil.ClassName(e.entityClassId);
            if (cls != null && RaeSettings.IsRemoved(cls)) { removedHere++; RaeStats.ClassesRemoved.Add(cls); continue; }
            int swap = RaeUtil.ReplaceId(cls);
            if (swap != 0)
            {
                kept.Add(new SEntityClassAndProb { entityClassId = swap, prob = e.prob });
                replacedHere++;
                RaeStats.ClassesRemoved.Add(cls);
                continue;
            }
            kept.Add(e);
        }
        if (removedHere == 0 && replacedHere == 0) return;

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
        var real = kept.Where(e => !RaeUtil.IsNone(e.entityClassId)).ToList();
        var ordered = real.Where(e => EntityClass.list[e.entityClassId].bIsEnemyEntity == wasEnemy)
                          .Concat(real.Where(e => EntityClass.list[e.entityClassId].bIsEnemyEntity != wasEnemy))
                          .Concat(kept.Where(e => RaeUtil.IsNone(e.entityClassId))).ToList();

        // The game picks by walking cumulative chances, so rescale to a total of 1 again.
        float total = ordered.Sum(e => e.prob);
        if (total > 0f)
            for (int i = 0; i < ordered.Count; i++) { var e = ordered[i]; e.prob /= total; ordered[i] = e; }

        list.Clear();
        list.AddRange(ordered);
        RaeStats.GroupsChanged++;
        RaeStats.EntriesRemoved += removedHere;
        RaeStats.EntriesReplaced += replacedHere;
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
        Log.Out($"[RemoveAnyEntity] Reloaded. Removed {RaeStats.EntriesRemoved} and replaced {RaeStats.EntriesReplaced} spawn entries in {RaeStats.GroupsChanged} groups. Rules: {RaeSettings.Summary()}");
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
        if (RaeSettings.AnyRules)
            Log.Out($"[RemoveAnyEntity] Removed {RaeStats.EntriesRemoved} and replaced {RaeStats.EntriesReplaced} spawn entries in {RaeStats.GroupsChanged} groups " +
                    $"({RaeStats.GroupsRefilled} refilled with replacements). Entities changed: {string.Join(", ", RaeStats.ClassesRemoved.OrderBy(x => x))}." +
                    (RaeSettings.BiomeRemoved.Count > 0 ? $" Per biome: {string.Join(", ", RaeSettings.BiomeRemoved.Select(kv => $"{kv.Key} in {string.Join("/", kv.Value)}"))}." : ""));
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
            if (!RaeSettings.AnyRules || __instance.entityIDs == null || __instance.entityIDs.Count == 0) return;
            var kept = __instance.entityIDs.Where(id => !RaeSettings.IsRemoved(RaeUtil.ClassName(id)))
                                           .Select(id => { int swap = RaeUtil.ReplaceId(RaeUtil.ClassName(id)); return swap != 0 ? swap : id; })
                                           .ToList();
            if (kept.SequenceEqual(__instance.entityIDs)) return;
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

// Per-biome removals. Open-world spawns pick from a group while SpawnManagerBiomes knows the
// biome, so note it, and re-roll a pick that's removed there. Sleepers and hordes have no biome.
public static class RaeBiome
{
    [ThreadStatic] public static string Current;
}

[HarmonyLib.HarmonyPatch(typeof(SpawnManagerBiomes), nameof(SpawnManagerBiomes.SpawnUpdate))]
public static class RaeBiomeSpawnPatch
{
    static void Prefix(SpawnManagerBiomes __instance, ChunkAreaBiomeSpawnData _spawnData)
    {
        RaeBiome.Current = null;
        if (RaeSettings.BiomeRemoved.Count == 0 || _spawnData == null) return;
        try { RaeBiome.Current = __instance.world?.Biomes?.GetBiome(_spawnData.biomeId)?.m_sBiomeName; }
        catch { }
    }

    static Exception Finalizer(Exception __exception)
    {
        RaeBiome.Current = null;
        return __exception;
    }
}

[HarmonyLib.HarmonyPatch(typeof(EntityGroups), nameof(EntityGroups.GetRandomFromGroup))]
public static class RaeBiomePickPatch
{
    static void Postfix(string _sEntityGroupName, ref int __result, GameRandom random)
    {
        var biome = RaeBiome.Current;
        if (biome == null || __result == 0 || !RaeSettings.IsRemovedIn(RaeUtil.ClassName(__result), biome)) return;
        try
        {
            var list = EntityGroups.list[_sEntityGroupName];
            var rnd = random ?? GameManager.Instance.World.GetGameRandom();
            for (int i = 0; i < 8; i++)
            {
                int id = EntityGroups.GetRandomFromGroupList(list, rnd);
                if (id > 0 && !RaeSettings.IsRemovedIn(RaeUtil.ClassName(id), biome)) { __result = id; return; }
            }
        }
        catch { }
        // Nothing else came up: this spot stays empty, as with a "none" pick.
        __result = 0;
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
                                        "rae remove <name> in <biome>[,<biome>] - stop <name> spawning in the open world of those biomes only\n" +
                                        "rae replace <name> <other> - spawn <other> wherever <name> would have spawned\n" +
                                        "rae keep <name>   - let <name> spawn again (undoes remove, remove-in and replace)\n" +
                                        "rae cull [<name>] - despawn creatures already out that the rules no longer allow (or just <name>)\n" +
                                        "rae cull count [<name>] - only count what cull would despawn\n" +
                                        "Biomes: pine_forest (forest), burnt_forest (burnt), desert, snow, wasteland.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        var sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (sub == "cull")
        {
            bool count = _params.Count >= 2 && _params[1].Equals("count", StringComparison.OrdinalIgnoreCase);
            var only = _params.Count > (count ? 2 : 1) ? _params[count ? 2 : 1] : null;
            if (only == null && !RaeSettings.AnyRules) { con.Output("Nothing is removed or replaced, so there's nothing to cull. Name one: rae cull <name>"); return; }
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int n = RaeUtil.Cull(only, count, counts);
            if (n < 0) { con.Output("No world is loaded."); return; }
            var detail = string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}"));
            if (n == 0) con.Output("Nothing to cull in the loaded areas.");
            else con.Output((count ? $"Would despawn {n}: " : $"Despawned {n}: ") + detail + ".");
            con.Output("Only loaded areas are checked. Players and Bait thralls are never touched.");
            return;
        }
        if (sub == "replace")
        {
            if (_params.Count < 3) { con.Output("Usage: rae replace <name> <other>, e.g. rae replace animalZombieDog animalWolf"); return; }
            if (EntityClass.FromString(_params[2]) == 0 || RaeUtil.ClassName(EntityClass.FromString(_params[2])) == null)
            { con.Output($"'{_params[2]}' isn't an entity in this game. Names are case-sensitive in the game's lists, e.g. animalWolf."); return; }
            try { con.Output(RaeSettings.SetReplaced(_params[1], _params[2])); }
            catch (Exception e) { con.Output($"Could not change {RaeSettings.FileName}: {e.Message}"); return; }
            sub = "reload";
        }
        else if (sub == "remove" && _params.Count >= 4 && _params[2].Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var msg = RaeSettings.SetBiomeRemoved(_params[1], string.Join(" ", _params.Skip(3)).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                con.Output(msg);
                if (msg.Contains("isn't a biome")) return;
            }
            catch (Exception e) { con.Output($"Could not change {RaeSettings.FileName}: {e.Message}"); return; }
            sub = "reload";
        }
        else if (sub == "remove" || sub == "keep")
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
            con.Output($"Applied. Removed {RaeStats.EntriesRemoved} and replaced {RaeStats.EntriesReplaced} spawn entries in {RaeStats.GroupsChanged} groups " +
                       $"({RaeStats.GroupsRefilled} refilled). Rules: {RaeSettings.Summary()}");
            foreach (var u in RaeStats.UnknownNames) con.Output($"PROBLEM: '{u}' doesn't match any entity (typo?)");
            foreach (var p in RaeSettings.Problems) con.Output("PROBLEM: " + p);
            if (_params.Count >= 2 && _params[0].Equals("keep", StringComparison.OrdinalIgnoreCase) && RaeSettings.IsRemoved(_params[1]))
                con.Output($"'{_params[1]}' is still removed by a shorter name in {RaeSettings.FileName} (a name covers its tiers).");
            con.Output("New spawns use this now. Creatures already in the world stay; 'rae cull' despawns them.");
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
            con.Output($"Now: removed {RaeStats.EntriesRemoved} and replaced {RaeStats.EntriesReplaced} spawn entries in {RaeStats.GroupsChanged} groups, " +
                       $"{RaeStats.GroupsRefilled} groups refilled with replacements.");
        if (RaeStats.ClassesRemoved.Count > 0)
            con.Output("Entities no longer spawning: " + string.Join(", ", RaeStats.ClassesRemoved.OrderBy(x => x)));
        foreach (var kv in RaeSettings.BiomeRemoved)
            con.Output($"Not spawning in the open world of {string.Join(", ", kv.Value)}: {kv.Key}");
        foreach (var kv in RaeSettings.Replaced)
            con.Output($"Replaced: {kv.Key} -> {kv.Value}" + (RaeStats.ReplaceIds.ContainsKey(kv.Key) ? "" : " (not applied, see PROBLEM)"));
        var reps = RaeStats.ReplacementIds.Select(RaeUtil.ClassName).Where(n => n != null).ToList();
        con.Output("Replacements: " + (reps.Count > 0 ? string.Join(", ", reps) : "(none usable)"));
        foreach (var u in RaeStats.UnknownNames) con.Output($"PROBLEM: '{u}' doesn't match any entity (typo?)");
        foreach (var p in RaeSettings.Problems) con.Output("PROBLEM: " + p);
        con.Output("Changed entities.txt? Type 'rae reload' to apply it now.");
    }
}
