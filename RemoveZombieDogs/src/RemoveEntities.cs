using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameEvent.SequenceActions;
using HarmonyLib;

// Server-side. The mod id stays AzraelRemoveZombieDogs so this replaces the existing upload.
// The in-game name is Remove Entities.
//
// Spawn groups and Twitch/game-event name lists are filtered when something is about to spawn.
// rement remove / rement add change the list immediately. Already living creatures are left alone.
// A saved creature is also left alone; it was stored as that class, not rolled again from the group.

public class AzraelRemoveZombieDogs : IModApi
{
    public const string Tag = "[RemoveEntities]";

    public void InitMod(Mod _modInstance)
    {
        RemovedEntities.ModPath = _modInstance.Path;
        RemovedEntities.Load();
        new Harmony("azrael.removeentities").PatchAll(typeof(AzraelRemoveZombieDogs).Assembly);
        ModEvents.GameStartDone.RegisterHandler(OnGameStart);
        Log.Out($"{Tag} Loaded. Commands: rement, rement list, rement remove, rement add. Players do not install this mod.");
    }

    static void OnGameStart(ref ModEvents.SGameStartDoneData _data)
    {
        if (!RemovedEntities.FileExists)
        {
            RemovedEntities.SeedDefaults();
            Log.Out($"{Tag} First run: dogs, coyotes, dire wolves and screamers will not spawn. rement add <name> puts one back.");
        }
        else
        {
            RemovedEntities.Load();
        }
        Log.Out($"{Tag} {RemovedEntities.Count} entities removed from spawns.");
    }
}

public static class RemovedEntities
{
    public static string ModPath;

    static readonly HashSet<string> Removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static readonly string[] FallbackNames =
    {
        "zombieArlene", "zombieBoe", "zombieJoe", "zombieMarlene", "zombieMoe",
        "zombieDarlene", "zombieYo", "zombieSteve", "zombieBusinessMan", "zombieJanitor"
    };
    static readonly string[] DefaultNames = { "animalZombieDog", "animalCoyote", "animalDireWolf" };
    static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "playerMale", "playerFemale", "item"
    };

    static string FilePath => Path.Combine(ModPath, "removed.txt");
    public static bool FileExists => File.Exists(FilePath);
    public static int Count => Removed.Count;
    static bool ready;

    // Classes are not loaded when the mod starts. The first spawn (or command) finishes setup.
    public static void EnsureReady()
    {
        if (ready) return;
        if (EntityClass.list?.Dict == null || EntityClass.list.Dict.Count == 0) return;
        ready = true;
        if (FileExists) Load();
        else SeedDefaults();
    }

    public static void Load()
    {
        Removed.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                Removed.Add(line);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelRemoveZombieDogs.Tag} Could not read removed.txt: {e.Message}");
        }
    }

    public static void SeedDefaults()
    {
        Removed.Clear();
        foreach (var name in DefaultNames) Removed.Add(name);
        foreach (var ec in AllClasses())
        {
            if (ec.entityClassName.StartsWith("zombieScreamer", StringComparison.OrdinalIgnoreCase))
                Removed.Add(ec.entityClassName);
        }
        Save();
    }

    public static bool IsRemoved(string name)
    {
        return name != null && Removed.Contains(name);
    }

    public static bool IsRemovedId(int entityClassId)
    {
        var ec = EntityClass.GetEntityClass(entityClassId);
        return ec != null && IsRemoved(ec.entityClassName);
    }

    public static bool IsProtected(string name)
    {
        return name != null && Protected.Contains(name);
    }

    // Weighted pick that skips removed classes. entityClassId 0 is the vanilla "none" entry and stays,
    // so a group of "dog + none" still usually spawns nothing instead of crashing or flooding zombies.
    public static bool TryRoll(List<SEntityClassAndProb> grpList, GameRandom random, out int entityClassId)
    {
        entityClassId = -1;
        EnsureReady();
        if (grpList == null || random == null) return false;
        float total = 0f;
        for (int i = 0; i < grpList.Count; i++)
        {
            if (Keep(grpList[i])) total += grpList[i].prob;
        }
        if (total <= 0f)
        {
            entityClassId = FallbackId();
            return true;
        }
        float roll = random.RandomFloat * total;
        float acc = 0f;
        for (int i = 0; i < grpList.Count; i++)
        {
            if (!Keep(grpList[i])) continue;
            acc += grpList[i].prob;
            entityClassId = grpList[i].entityClassId;
            if (roll <= acc) return true;
        }
        return true;
    }

    public static void FilterSpawnList(List<int> entityIds)
    {
        EnsureReady();
        if (entityIds == null) return;
        for (int i = entityIds.Count - 1; i >= 0; i--)
        {
            if (IsRemovedId(entityIds[i])) entityIds.RemoveAt(i);
        }
        if (entityIds.Count == 0)
        {
            foreach (var name in FallbackNames)
            {
                int id = EntityClass.GetId(name);
                if (id > 0 && !IsRemoved(name)) entityIds.Add(id);
            }
        }
    }

    public static List<string> Matching(string typed)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(typed)) return found;
        typed = typed.Trim();
        bool prefix = typed.EndsWith("*", StringComparison.Ordinal);
        if (prefix) typed = typed.Substring(0, typed.Length - 1);
        foreach (var ec in AllClasses())
        {
            var name = ec.entityClassName;
            if (name == null) continue;
            if (prefix)
            {
                if (name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) found.Add(name);
            }
            else if (name.Equals(typed, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(name);
                return found;
            }
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    public static List<string> Suggestions(string typed)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(typed)) return found;
        foreach (var ec in AllClasses())
        {
            var name = ec.entityClassName;
            if (name != null && name.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0)
                found.Add(name);
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    public static bool Remove(string canonical)
    {
        if (!Removed.Add(canonical)) return false;
        Save();
        return true;
    }

    public static bool AddBack(string canonical)
    {
        if (!Removed.Remove(canonical)) return false;
        Save();
        return true;
    }

    public static List<string> RemovedNames()
    {
        var names = new List<string>(Removed);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static List<EntityClass> Creatures()
    {
        var list = new List<EntityClass>();
        foreach (var ec in AllClasses())
        {
            if (ec.bIsEnemyEntity || ec.bIsAnimalEntity) list.Add(ec);
        }
        list.Sort((a, b) => string.Compare(a.entityClassName, b.entityClassName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static List<EntityClass> AllClasses()
    {
        var list = new List<EntityClass>();
        if (EntityClass.list == null) return list;
        foreach (var pair in EntityClass.list.Dict)
        {
            if (pair.Value?.entityClassName != null) list.Add(pair.Value);
        }
        return list;
    }

    static bool Keep(SEntityClassAndProb entry)
    {
        if (entry.entityClassId == 0) return entry.prob > 0f;
        var ec = EntityClass.GetEntityClass(entry.entityClassId);
        if (ec == null) return entry.prob > 0f;
        return !IsRemoved(ec.entityClassName) && entry.prob > 0f;
    }

    static int FallbackId()
    {
        for (int i = 0; i < FallbackNames.Length; i++)
        {
            if (IsRemoved(FallbackNames[i])) continue;
            int id = EntityClass.GetId(FallbackNames[i]);
            if (id > 0) return id;
        }
        return -1;
    }

    static void Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# One entity class per line. Edit with:  rement remove <name>   /   rement add <name>");
            sb.AppendLine("# A trailing * removes every class that starts with that text, e.g. zombieScreamer*");
            foreach (var name in RemovedNames()) sb.AppendLine(name);
            File.WriteAllText(FilePath, sb.ToString());
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelRemoveZombieDogs.Tag} Could not write removed.txt: {e.Message}");
        }
    }
}

// Both group rolls end up here. Skipping the original roll keeps the group itself intact,
// which is what stops IsEnemyGroup from crashing when the first entry would have become "none".
[HarmonyPatch(typeof(EntityGroups), "GetRandomFromGroupList")]
public static class Patch_GroupRoll
{
    static bool Prefix(List<SEntityClassAndProb> grpList, GameRandom random, ref int __result)
    {
        RemovedEntities.EnsureReady();
        if (RemovedEntities.Count == 0 || grpList == null || random == null) return true;
        if (!RemovedEntities.TryRoll(grpList, random, out int id)) return true;
        __result = id;
        return false;
    }
}

// Game events and Twitch actions that name a class (not a group) cache those ids.
// Rebuild the cache each time the action runs so rement remove applies without a restart.
[HarmonyPatch(typeof(ActionBaseSpawn), nameof(ActionBaseSpawn.OnPerformAction))]
public static class Patch_EventSpawnRefresh
{
    static void Prefix(ActionBaseSpawn __instance)
    {
        if (!__instance.useEntityGroup)
            __instance.SetupEntityIDs();
    }
}

[HarmonyPatch(typeof(ActionBaseSpawn), nameof(ActionBaseSpawn.SetupEntityIDs))]
public static class Patch_EventSpawnFilter
{
    static void Postfix(ActionBaseSpawn __instance)
    {
        RemovedEntities.EnsureReady();
        if (__instance.useEntityGroup || RemovedEntities.Count == 0) return;
        RemovedEntities.FilterSpawnList(__instance.entityIDs);
        __instance.selectedEntityIndex = -1;
    }
}

public class ConsoleCmdRement : ConsoleCmdAbstract
{
    public override int DefaultPermissionLevel => 0;
    public override string[] getCommands() => new[] { "rement" };
    public override string getDescription() => "Remove Entities: choose which creatures are allowed to spawn";
    public override string getHelp() =>
        "rement                         - list what is removed\n" +
        "rement list                    - every creature class\n" +
        "rement list <text>             - creature classes containing that text\n" +
        "rement list all                - every entity class\n" +
        "rement remove <name>           - stop that class from spawning\n" +
        "rement remove <name>*          - stop every class that starts with that name\n" +
        "rement add <name>              - let it spawn again\n" +
        "Takes effect on the next spawn. Creatures already alive are not deleted.\n" +
        "playerMale, playerFemale and item cannot be removed.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        RemovedEntities.EnsureReady();
        if (EntityClass.list?.Dict == null || EntityClass.list.Dict.Count == 0)
        {
            con.Output("Entity classes are not loaded yet.");
            return;
        }
        string arg = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (arg == "list")
        {
            string filter = _params.Count > 1 ? _params[1] : "";
            bool all = filter.Equals("all", StringComparison.OrdinalIgnoreCase);
            var classes = all ? RemovedEntities.AllClasses() : RemovedEntities.Creatures();
            if (all) classes.Sort((a, b) => string.Compare(a.entityClassName, b.entityClassName, StringComparison.OrdinalIgnoreCase));
            int shown = 0;
            foreach (var ec in classes)
            {
                if (!all && filter.Length > 0 && ec.entityClassName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (all && filter.Length > 0 && !filter.Equals("all", StringComparison.OrdinalIgnoreCase)
                    && ec.entityClassName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string mark = RemovedEntities.IsRemoved(ec.entityClassName) ? "removed  " : "         ";
                con.Output(mark + ec.entityClassName);
                shown++;
            }
            con.Output(shown + (all ? " entity classes." : " creatures.") + "  rement remove <name>  /  rement add <name>");
            return;
        }
        if (arg == "remove" || arg == "add")
        {
            if (_params.Count < 2)
            {
                con.Output("Usage: rement " + arg + " <name>");
                return;
            }
            string typed = _params[1].Trim();
            if (!typed.EndsWith("*", StringComparison.Ordinal) && RemovedEntities.IsProtected(typed))
            {
                con.Output(typed + " cannot be removed.");
                return;
            }
            var matches = RemovedEntities.Matching(typed);
            if (matches.Count == 0)
            {
                con.Output("No entity class named " + typed + ".");
                var suggestions = RemovedEntities.Suggestions(typed.TrimEnd('*'));
                int n = Math.Min(12, suggestions.Count);
                for (int i = 0; i < n; i++) con.Output("  " + suggestions[i]);
                if (suggestions.Count > n) con.Output("  ... " + (suggestions.Count - n) + " more. Try rement list " + typed.TrimEnd('*'));
                return;
            }
            int changed = 0;
            foreach (var name in matches)
            {
                if (RemovedEntities.IsProtected(name))
                {
                    con.Output(name + " cannot be removed.");
                    continue;
                }
                if (arg == "remove")
                {
                    if (RemovedEntities.Remove(name)) changed++;
                }
                else if (RemovedEntities.AddBack(name)) changed++;
            }
            if (arg == "remove")
                con.Output("Removed " + changed + " of " + matches.Count + ". The next spawn uses the new list. Creatures already alive stay until they die.");
            else
                con.Output("Restored " + changed + " of " + matches.Count + ". They can spawn again.");
            return;
        }
        if (arg.Length > 0)
        {
            con.Output("Usage: rement | rement list | rement remove <name> | rement add <name>");
            return;
        }
        var removed = RemovedEntities.RemovedNames();
        if (removed.Count == 0) con.Output("Nothing is removed. Every creature can spawn.");
        else con.Output("Not spawning (" + removed.Count + "):");
        foreach (var name in removed) con.Output("  " + name);
        con.Output("rement list    rement remove <name>    rement add <name>");
    }
}
