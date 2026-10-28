using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SandboxOptions;

public class AzraelSpawnTunerMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        StSettings.ModPath = _modInstance.Path;
        StSettings.Load();
        new HarmonyLib.Harmony("azrael.spawntuner").PatchAll(typeof(AzraelSpawnTunerMod).Assembly);
        Log.Out($"[SpawnTuner] Loaded. {StSettings.Summary()}");
    }
}

// One tunable value. The game keeps each of these in a static field (or a game pref) that the
// spawners read every time, so changing the field takes effect without a restart.
public class StSetting
{
    public enum Kind { Density, Respawn, Count, Percent }

    public string Key;
    public Kind Type;
    public Func<int> Get;
    public Action<int> Set;
    public string Help;

    // The value from the world's own settings, captured before we override it.
    public int WorldValue;
    public bool HaveWorldValue;
    // Our override, or null to leave the world's value.
    public int? Override;
    // A Spawn Tuner setting with no game equivalent: its "world" value is always 100 (vanilla).
    public bool ModOnly;
    // Optional ramp: add Step every Every game days, never past Limit.
    public StRamp Ramp;
}

public class StRamp
{
    public int Step;
    public int Every;
    public int? Limit;

    public int Steps(int day) => Every <= 0 ? 0 : Math.Max(0, day - 1) / Every;

    public override string ToString() =>
        $"{(Step >= 0 ? "+" : "")}{Step} every {Every} day{(Every == 1 ? "" : "s")}" + (Limit.HasValue ? $" max {Limit}" : "");
}

public static class StSettings
{
    public const string FileName = "spawns.txt";
    public static string ModPath;
    public static readonly List<string> Problems = new List<string>();

    static readonly string[] RespawnNames = { "default", "veryslow", "slow", "normal", "fast", "veryfast" };

    public static readonly List<StSetting> All = new List<StSetting>
    {
        Density("DayDensity", "zombies per spawn spot by day",
            () => ChunkAreaBiomeSpawnData.RespawnDayEnemyCountOverride, v => ChunkAreaBiomeSpawnData.RespawnDayEnemyCountOverride = v),
        Density("NightDensity", "zombies per spawn spot at night",
            () => ChunkAreaBiomeSpawnData.RespawnNightEnemyCountOverride, v => ChunkAreaBiomeSpawnData.RespawnNightEnemyCountOverride = v),
        Respawn("DayRespawn", "how fast zombies refill an area by day",
            () => ChunkAreaBiomeSpawnData.RespawnDayDelayIndexEnemies, v => ChunkAreaBiomeSpawnData.RespawnDayDelayIndexEnemies = v),
        Respawn("NightRespawn", "how fast zombies refill an area at night",
            () => ChunkAreaBiomeSpawnData.RespawnNightDelayIndexEnemies, v => ChunkAreaBiomeSpawnData.RespawnNightDelayIndexEnemies = v),
        Density("AnimalDayDensity", "animals per spawn spot by day",
            () => ChunkAreaBiomeSpawnData.RespawnDayAnimalCountOverride, v => ChunkAreaBiomeSpawnData.RespawnDayAnimalCountOverride = v),
        Density("AnimalNightDensity", "animals per spawn spot at night",
            () => ChunkAreaBiomeSpawnData.RespawnNightAnimalCountOverride, v => ChunkAreaBiomeSpawnData.RespawnNightAnimalCountOverride = v),
        Respawn("AnimalDayRespawn", "how fast animals refill an area by day",
            () => ChunkAreaBiomeSpawnData.RespawnDayDelayIndexAnimals, v => ChunkAreaBiomeSpawnData.RespawnDayDelayIndexAnimals = v),
        Respawn("AnimalNightRespawn", "how fast animals refill an area at night",
            () => ChunkAreaBiomeSpawnData.RespawnNightDelayIndexAnimals, v => ChunkAreaBiomeSpawnData.RespawnNightDelayIndexAnimals = v),
        Count("BloodMoonCount", "zombies per player at a blood moon",
            () => AIDirectorBloodMoonComponent.BloodMoonEnemyCount,
            v => { AIDirectorBloodMoonComponent.BloodMoonEnemyCount = v; GameStats.Set(EnumGameStats.BloodMoonEnemyCount, v); }),
        Count("MaxZombies", "most zombies alive in the world at once",
            () => GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedZombies), v => GamePrefs.Set(EnumGamePrefs.MaxSpawnedZombies, v)),
        Count("MaxAnimals", "most animals alive in the world at once",
            () => GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedAnimals), v => GamePrefs.Set(EnumGamePrefs.MaxSpawnedAnimals, v)),
        Percent("HordeFrequency", "how often wandering hordes come, % of vanilla (0 = never)",
            () => StHorde.Frequency, v => StHorde.Frequency = v),
        Percent("HordeSize", "zombies in each wandering horde, % of vanilla",
            () => StHorde.Size, v => StHorde.Size = v),
    };

    static StSetting Density(string key, string help, Func<int> get, Action<int> set) =>
        new StSetting { Key = key, Type = StSetting.Kind.Density, Get = get, Set = set, Help = help };
    static StSetting Respawn(string key, string help, Func<int> get, Action<int> set) =>
        new StSetting { Key = key, Type = StSetting.Kind.Respawn, Get = get, Set = set, Help = help };
    static StSetting Count(string key, string help, Func<int> get, Action<int> set) =>
        new StSetting { Key = key, Type = StSetting.Kind.Count, Get = get, Set = set, Help = help };

    static StSetting Percent(string key, string help, Func<int> get, Action<int> set) =>
        new StSetting { Key = key, Type = StSetting.Kind.Percent, Get = get, Set = set, Help = help,
                        ModOnly = true, WorldValue = 100, HaveWorldValue = true };

    public static StSetting Find(string key) =>
        All.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    // Text to value. Density: none = -1, default = 0 (spawning.xml's maxcount), N = N.
    // Respawn: an index into spawning.xml's respawndelay list.
    public static bool TryParse(StSetting s, string text, out int value, out string error)
    {
        value = 0; error = null;
        var t = text.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
        switch (s.Type)
        {
            case StSetting.Kind.Density:
                if (t == "none" || t == "off") { value = -1; return true; }
                if (t == "default") { value = 0; return true; }
                if (int.TryParse(t, out value) && value >= 1 && value <= 100) return true;
                error = "use none, default, or a number from 1 to 100 (the game's own range is 1 to 5)";
                return false;
            case StSetting.Kind.Respawn:
                value = Array.IndexOf(RespawnNames, t);
                if (value >= 0) return true;
                error = "use " + string.Join(", ", RespawnNames);
                return false;
            case StSetting.Kind.Percent:
                t = t.TrimEnd('%');
                if (t == "default" || t == "vanilla") { value = 100; return true; }
                if (int.TryParse(t, out value) && value >= 0 && value <= 1000) return true;
                error = "use a percentage from 0 to 1000 (100 = vanilla)";
                return false;
            default:
                if (int.TryParse(t, out value) && value >= 0 && value <= 1000) return true;
                error = "use a number from 0 to 1000";
                return false;
        }
    }

    // The range a ramp may move a setting through.
    static void Bounds(StSetting s, out int min, out int max)
    {
        switch (s.Type)
        {
            case StSetting.Kind.Density: min = 1; max = 100; break;
            case StSetting.Kind.Respawn: min = 1; max = RespawnNames.Length - 1; break;
            default: min = 0; max = 1000; break;
        }
    }

    // "+1 every 7 max 6", "+2 every 1 day", "-1 every 10 days max 1"
    public static bool TryParseRamp(StSetting s, string text, out StRamp ramp, out string error)
    {
        ramp = null; error = null;
        var words = text.Trim().ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w != "days" && w != "day").ToList();
        const string usage = "write it as  <+/-step> every <days> [max <value>], e.g.  +1 every 7 max 6";
        if (words.Count < 3 || words[1] != "every" || !int.TryParse(words[0], out var step) || step == 0
            || !int.TryParse(words[2], out var every) || every < 1) { error = usage; return false; }
        int? limit = null;
        if (words.Count >= 5 && (words[3] == "max" || words[3] == "min" || words[3] == "limit"))
        {
            if (!TryParse(s, words[4], out var l, out var err)) { error = "max: " + err; return false; }
            limit = l;
        }
        else if (words.Count != 3) { error = usage; return false; }
        ramp = new StRamp { Step = step, Every = every, Limit = limit };
        return true;
    }

    public static int CurrentDay()
    {
        var w = GameManager.Instance?.World;
        return w == null ? 1 : GameUtils.WorldTimeToDays(w.worldTime);
    }

    // The value to use today: the override (or the world's value), moved along its ramp.
    public static int? Effective(StSetting s, int day)
    {
        int? baseValue = s.Override ?? (s.HaveWorldValue ? s.WorldValue : (int?)null);
        if (!baseValue.HasValue || s.Ramp == null) return baseValue;
        // "none" and "default" aren't numbers to count up from.
        if (s.Type == StSetting.Kind.Density && baseValue.Value <= 0) return baseValue;
        if (s.Type == StSetting.Kind.Respawn && baseValue.Value <= 0) return baseValue;
        Bounds(s, out var min, out var max);
        long v = baseValue.Value + (long)s.Ramp.Step * s.Ramp.Steps(day);
        if (s.Ramp.Limit.HasValue)
            v = s.Ramp.Step > 0 ? Math.Min(v, s.Ramp.Limit.Value) : Math.Max(v, s.Ramp.Limit.Value);
        return (int)Math.Max(min, Math.Min(max, v));
    }

    public static string Display(StSetting s, int value)
    {
        switch (s.Type)
        {
            case StSetting.Kind.Density: return value == -1 ? "none" : value == 0 ? "default" : value.ToString();
            case StSetting.Kind.Respawn: return value >= 0 && value < RespawnNames.Length ? RespawnNames[value] : value.ToString();
            case StSetting.Kind.Percent: return value + "%";
            default: return value.ToString();
        }
    }

    public static void Load()
    {
        Problems.Clear();
        foreach (var s in All) { s.Override = null; s.Ramp = null; }
        ReadInto(Path.Combine(ModPath ?? "", FileName), FileName, Problems, true);
    }

    // Read a spawns.txt-style file. Each "Key = value" sets that override; each
    // "Ramp.Key = +1 every 7" sets a ramp. With apply false it only checks the file.
    public static bool ReadInto(string path, string name, List<string> problems, bool apply)
    {
        try
        {
            if (!File.Exists(path)) return false;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { problems.Add($"'{line}' in {name} has no '='"); continue; }
                var key = line.Substring(0, eq).Trim();
                bool isRamp = key.StartsWith("Ramp.", StringComparison.OrdinalIgnoreCase);
                var s = Find(isRamp ? key.Substring(5) : key);
                if (s == null) { problems.Add($"'{key}' in {name} isn't a setting"); continue; }
                if (isRamp)
                {
                    if (TryParseRamp(s, line.Substring(eq + 1), out var r, out var rerr)) { if (apply) s.Ramp = r; }
                    else problems.Add($"Ramp.{s.Key} in {name}: {rerr}");
                }
                else if (TryParse(s, line.Substring(eq + 1), out var v, out var err)) { if (apply) s.Override = v; }
                else problems.Add($"{s.Key} in {name}: {err}");
            }
            return true;
        }
        catch (Exception e) { problems.Add($"could not read {name}: {e.Message}"); return false; }
    }

    // Write one setting to spawns.txt: replace its line (commented or not), or add one at the end.
    // A null value comments the line out, so the world's value is used again.
    public static void Save(StSetting s) =>
        SaveLine(s.Key, s.Override.HasValue ? $"{s.Key} = {Display(s, s.Override.Value)}" : null);

    public static void SaveRamp(StSetting s) =>
        SaveLine("Ramp." + s.Key, s.Ramp != null ? $"Ramp.{s.Key} = {s.Ramp}" : null);

    static void SaveLine(string key, string newLine)
    {
        var path = Path.Combine(ModPath ?? "", FileName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        bool done = false;
        for (int i = 0; i < lines.Count; i++)
        {
            var body = lines[i].Trim().TrimStart('#').Trim();
            int eq = body.IndexOf('=');
            if (eq <= 0 || !body.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (done) { if (!lines[i].TrimStart().StartsWith("#")) lines[i] = "#" + lines[i].TrimStart(); continue; }
            lines[i] = newLine ?? (lines[i].TrimStart().StartsWith("#") ? lines[i] : "#" + lines[i].TrimStart());
            done = true;
        }
        if (!done && newLine != null) lines.Add(newLine);
        File.WriteAllLines(path, lines);
    }

    // Spawning is decided by the server; a client that isn't hosting leaves its values alone.
    public static bool IsServer
    {
        get
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            return cm == null || cm.IsServer;
        }
    }

    // Remember the world's values (the game has just set them) and put our overrides on top.
    public static void CaptureAndApply()
    {
        foreach (var s in All)
        {
            if (s.ModOnly) continue;
            // Game prefs aren't reset with the sandbox options, so once overridden the
            // pref holds our value: keep the first one we saw.
            bool isPref = s.Key == "MaxZombies" || s.Key == "MaxAnimals";
            if (!isPref || !s.HaveWorldValue) { s.WorldValue = s.Get(); s.HaveWorldValue = true; }
        }
        Apply();
    }

    public static int AppliedDay;

    public static void Apply()
    {
        int day = CurrentDay();
        AppliedDay = day;
        foreach (var s in All)
        {
            var v = Effective(s, day);
            if (v.HasValue) s.Set(v.Value);
        }
    }

    // Presets are spawns.txt-style files in the presets folder.
    public static string PresetDir => Path.Combine(ModPath ?? "", "presets");

    public static string PresetPath(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return Path.Combine(PresetDir, name + ".txt");
    }

    public static List<string> PresetNames() =>
        Directory.Exists(PresetDir)
            ? Directory.GetFiles(PresetDir, "*.txt").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList()
            : new List<string>();

    public static void SavePreset(string name)
    {
        Directory.CreateDirectory(PresetDir);
        var lines = new List<string> { $"# Spawn Tuner preset '{name}'. Load it with: spawns load {name}",
                                       "# Settings not listed here go back to the world's own value." };
        foreach (var s in All)
        {
            if (s.Override.HasValue) lines.Add($"{s.Key} = {Display(s, s.Override.Value)}");
            if (s.Ramp != null) lines.Add($"Ramp.{s.Key} = {s.Ramp}");
        }
        File.WriteAllLines(PresetPath(name), lines);
    }

    // Replace every override and ramp with the preset's, then write them all to spawns.txt.
    public static bool LoadPreset(string name, List<string> problems)
    {
        var path = PresetPath(name);
        if (!File.Exists(path)) { problems.Add($"no preset called '{name}'"); return false; }
        var check = new List<string>();
        ReadInto(path, Path.GetFileName(path), check, false);
        if (check.Count > 0) { problems.AddRange(check); return false; }
        foreach (var s in All) { s.Override = null; s.Ramp = null; }
        ReadInto(path, Path.GetFileName(path), problems, true);
        foreach (var s in All) { Save(s); SaveRamp(s); }
        Apply();
        return true;
    }

    // Restart the respawn timers in every loaded area, so new densities and respawn speeds
    // apply on the next spawn tick instead of when each area's old timer runs out.
    // Areas that were cleared fill up again. Returns how many areas were reset.
    public static int RefreshLoadedAreas()
    {
        var world = GameManager.Instance?.World;
        var cache = world?.ChunkCache;
        if (cache == null) return -1;
        int areas = 0;
        foreach (var chunk in cache.GetChunkArrayCopySync())
        {
            if (chunk == null || !chunk.IsAreaMaster()) continue;
            var data = chunk.GetChunkBiomeSpawnData();
            if (data == null || data.entitesSpawned.Count == 0) continue;
            foreach (var key in data.entitesSpawned.Keys.ToList())
            {
                var v = data.entitesSpawned[key];
                v.delayWorldTime = 0;
                data.entitesSpawned[key] = v;
            }
            chunk.isModified = true;
            areas++;
        }
        return areas;
    }

    public static string Summary()
    {
        var set = All.Where(s => s.Override.HasValue).Select(s => $"{s.Key} = {Display(s, s.Override.Value)}")
                     .Concat(All.Where(s => s.Ramp != null).Select(s => $"{s.Key} ramps {s.Ramp}")).ToList();
        return set.Count == 0 ? "Nothing changed from the world's settings." : "Changed: " + string.Join(", ", set);
    }
}

// The game copies its sandbox options into the spawners' static fields when a world starts
// and again when a local player joins. Put our values back on top each time.
[HarmonyLib.HarmonyPatch(typeof(SandboxOptionManager), nameof(SandboxOptionManager.UpdateInGameValuesWithSandboxOptions))]
public static class StSandboxPatch
{
    static void Postfix()
    {
        try
        {
            if (!StSettings.IsServer) return;
            StSettings.CaptureAndApply();
            foreach (var p in StSettings.Problems) Log.Warning("[SpawnTuner] " + p);
            if (StSettings.All.Any(s => s.Override.HasValue)) Log.Out("[SpawnTuner] " + StSettings.Summary());
        }
        catch (Exception e) { Log.Warning("[SpawnTuner] " + e.Message); }
    }
}

// Wandering hordes. The AI Director picks the next horde 12 to 24 game hours ahead and sizes it
// from the WanderingHorde game stage. These two settings scale both.
public static class StHorde
{
    public static int Frequency = 100;
    public static int Size = 100;

    public static AIDirectorWanderingHordeComponent Component =>
        GameManager.Instance?.World?.aiDirector?.GetComponent<AIDirectorWanderingHordeComponent>();

    public static string NextHorde()
    {
        var c = Component;
        var w = GameManager.Instance?.World;
        if (c == null || w == null) return "?";
        if (Frequency == 0) return "never (HordeFrequency 0%)";
        if (c.HordeNextTime == 0) return "not picked yet";
        long ticks = (long)c.HordeNextTime - (long)w.worldTime;
        if (ticks <= 0) return "due now";
        return $"in about {ticks / 1000.0:0.#} game hours (day {GameUtils.WorldTimeToDays(c.HordeNextTime)}, {GameUtils.WorldTimeToHours(c.HordeNextTime):00}:00)";
    }
}

[HarmonyLib.HarmonyPatch(typeof(AIDirectorWanderingHordeComponent), nameof(AIDirectorWanderingHordeComponent.ChooseNextTime))]
public static class StHordeTimePatch
{
    static void Postfix(AIDirectorWanderingHordeComponent __instance, AIWanderingHordeSpawner.SpawnType _spawnType)
    {
        try
        {
            if (_spawnType != AIWanderingHordeSpawner.SpawnType.Horde || StHorde.Frequency == 100) return;
            ulong now = __instance.Director.World.worldTime;
            if (StHorde.Frequency == 0) { __instance.HordeNextTime = now + 24000; return; }
            ulong delay = __instance.HordeNextTime > now ? __instance.HordeNextTime - now : 0;
            // At least one game hour apart, however high the frequency.
            delay = Math.Max(1000UL, delay * 100UL / (ulong)StHorde.Frequency);
            __instance.HordeNextTime = now + delay;
        }
        catch (Exception e) { Log.Warning("[SpawnTuner] horde timer: " + e.Message); }
    }
}

[HarmonyLib.HarmonyPatch(typeof(AIDirectorWanderingHordeComponent), nameof(AIDirectorWanderingHordeComponent.StartSpawning))]
public static class StHordeOffPatch
{
    // HordeFrequency 0: when a horde comes due, skip it and look again in a day.
    static bool Prefix(AIDirectorWanderingHordeComponent __instance, AIWanderingHordeSpawner.SpawnType _spawnType)
    {
        if (_spawnType != AIWanderingHordeSpawner.SpawnType.Horde || StHorde.Frequency != 0) return true;
        __instance.HordeNextTime = __instance.Director.World.worldTime + 24000;
        return false;
    }
}

[HarmonyLib.HarmonyPatch(typeof(AIDirectorGameStagePartySpawner), nameof(AIDirectorGameStagePartySpawner.SetupGroup))]
public static class StHordeSizePatch
{
    static void Postfix(AIDirectorGameStagePartySpawner __instance)
    {
        if (StHorde.Size == 100 || __instance.def?.name != "WanderingHorde" || __instance.spawnGroupName == null) return;
        __instance.numToSpawn = Math.Max(StHorde.Size == 0 ? 0 : 1, (int)Math.Round(__instance.numToSpawn * StHorde.Size / 100.0));
    }
}

// Ramps change with the game day, so re-apply when a new day starts.
[HarmonyLib.HarmonyPatch(typeof(AIDirector), nameof(AIDirector.Tick))]
public static class StDayPatch
{
    static void Postfix()
    {
        try
        {
            if (!StSettings.All.Any(s => s.Ramp != null)) return;
            int day = StSettings.CurrentDay();
            if (day == StSettings.AppliedDay) return;
            StSettings.Apply();
            Log.Out($"[SpawnTuner] Day {day}: " + string.Join(", ", StSettings.All.Where(s => s.Ramp != null)
                .Select(s => $"{s.Key} {StSettings.Display(s, s.Get())}")));
        }
        catch (Exception e) { Log.Warning("[SpawnTuner] " + e.Message); }
    }
}

public class ConsoleCmdAzraelSpawnTuner : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient => false;
    public override bool AllowedInMainMenu => false;
    public override int DefaultPermissionLevel => 0;
    public override string[] getCommands() => new[] { "spawns", "spawntuner" };
    public override string getDescription() => "Spawn Tuner: change spawn rates while the game runs";
    public override string getHelp() => "spawns                  - show every setting, now and from the world\n" +
                                        "spawns <setting> <value> - change one, save it to spawns.txt, apply it now\n" +
                                        "spawns <setting> world   - go back to the world's own value\n" +
                                        "spawns refresh          - restart the respawn timers in loaded areas so changes show now\n" +
                                        "spawns reload           - re-read spawns.txt after editing it\n" +
                                        "spawns ramp <setting> +1 every 7 [max 6] - raise (or lower) a setting as the game days pass\n" +
                                        "spawns ramp <setting> off - stop ramping it\n" +
                                        "spawns save <name>      - save every change as a preset\n" +
                                        "spawns load <name>      - switch to a preset (replaces every change)\n" +
                                        "spawns presets          - list the presets\n" +
                                        "e.g. spawns NightDensity 4   spawns HordeFrequency 200   spawns BloodMoonCount 16";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        var sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";

        if (sub == "refresh")
        {
            int n = StSettings.RefreshLoadedAreas();
            con.Output(n < 0 ? "No world is loaded." : $"Restarted the spawn timers in {n} loaded areas. They refill with the current settings over the next few seconds.");
            return;
        }
        if (sub == "reload")
        {
            StSettings.Load();
            StSettings.Apply();
            con.Output($"{StSettings.FileName} re-read. {StSettings.Summary()}");
            foreach (var p in StSettings.Problems) con.Output("PROBLEM: " + p);
            con.Output("Type 'spawns refresh' to apply it to areas already loaded.");
            return;
        }
        if (sub == "presets")
        {
            var names = StSettings.PresetNames();
            con.Output(names.Count == 0 ? "No presets yet. Save one with: spawns save <name>"
                                        : "Presets: " + string.Join(", ", names) + ". Switch with: spawns load <name>");
            return;
        }
        if (sub == "save" || sub == "load")
        {
            if (_params.Count < 2) { con.Output($"Usage: spawns {sub} <name>"); return; }
            var name = string.Join(" ", _params.Skip(1)).Trim();
            if (sub == "save")
            {
                try { StSettings.SavePreset(name); con.Output($"Saved preset '{name}': {StSettings.Summary()}"); }
                catch (Exception e) { con.Output($"Could not save preset '{name}': {e.Message}"); }
                return;
            }
            var problems = new List<string>();
            if (StSettings.LoadPreset(name, problems))
            {
                con.Output($"Loaded preset '{name}'. {StSettings.Summary()}");
                con.Output("Type 'spawns refresh' to apply it to areas already loaded.");
            }
            else
            {
                con.Output($"Preset '{name}' not loaded; nothing changed.");
                foreach (var p in problems) con.Output("PROBLEM: " + p);
                var names = StSettings.PresetNames();
                if (names.Count > 0) con.Output("Presets: " + string.Join(", ", names));
            }
            return;
        }
        if (sub == "ramp")
        {
            if (_params.Count < 3) { con.Output("Usage: spawns ramp <setting> +1 every 7 [max 6]   or   spawns ramp <setting> off"); return; }
            var rs = StSettings.Find(_params[1]);
            if (rs == null) { con.Output($"No setting called '{_params[1]}'. Type 'spawns' to list them."); return; }
            var rtext = string.Join(" ", _params.Skip(2));
            if (rtext.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) rs.Ramp = null;
            else if (StSettings.TryParseRamp(rs, rtext, out var ramp, out var rerr)) rs.Ramp = ramp;
            else { con.Output($"{rs.Key}: {rerr}"); return; }
            StSettings.Apply();
            try { StSettings.SaveRamp(rs); }
            catch (Exception e) { con.Output($"Applied, but could not save {StSettings.FileName}: {e.Message}"); }
            if (rs.Ramp == null) con.Output($"{rs.Key} no longer ramps; it is {StSettings.Display(rs, rs.Get())}.");
            else
            {
                con.Output($"{rs.Key} ramps {rs.Ramp}. Today (day {StSettings.AppliedDay}) it is {StSettings.Display(rs, rs.Get())}.");
                var start = rs.Override ?? rs.WorldValue;
                if ((rs.Type == StSetting.Kind.Density || rs.Type == StSetting.Kind.Respawn) && start <= 0)
                    con.Output($"Note: {rs.Key} is '{StSettings.Display(rs, start)}', which has no number to ramp from. Set a starting number first, e.g. spawns {rs.Key} 2");
            }
            return;
        }
        if (_params.Count >= 2)
        {
            var s = StSettings.Find(_params[0]);
            if (s == null) { con.Output($"No setting called '{_params[0]}'. Type 'spawns' to list them."); return; }
            var text = string.Join(" ", _params.Skip(1));
            if (text.Trim().Equals("world", StringComparison.OrdinalIgnoreCase)) s.Override = null;
            else if (StSettings.TryParse(s, text, out var v, out var err)) s.Override = v;
            else { con.Output($"{s.Key}: {err}"); return; }
            StSettings.Apply();
            try { StSettings.Save(s); }
            catch (Exception e) { con.Output($"Applied, but could not save {StSettings.FileName}: {e.Message}"); }
            con.Output($"{s.Key} is now {StSettings.Display(s, s.Get())}" + (s.Override.HasValue ? "." : " (the world's value).")
                       + (s.Ramp != null ? $" It ramps {s.Ramp} on top of that." : ""));
            if (s.Type == StSetting.Kind.Percent)
                con.Output("This applies from the next wandering horde.");
            else if (s.Type != StSetting.Kind.Count)
                con.Output("Areas pick this up as their timers run out. Type 'spawns refresh' to apply it now.");
            return;
        }
        if (_params.Count == 1) { con.Output(getHelp()); return; }

        con.Output("Spawn Tuner (setting: now / world's value):");
        foreach (var s in StSettings.All)
        {
            var world = s.HaveWorldValue ? StSettings.Display(s, s.WorldValue) : "?";
            con.Output($"  {s.Key,-19} {StSettings.Display(s, s.Get()),-8} / {world,-8} {(s.Override.HasValue || s.Ramp != null ? "*" : " ")} {s.Help}"
                       + (s.Ramp != null ? $" [ramps {s.Ramp}]" : ""));
        }
        con.Output("* = changed by Spawn Tuner. Change one with: spawns <setting> <value>");
        con.Output($"Right now (day {StSettings.CurrentDay()}): {GameStats.GetInt(EnumGameStats.EnemyCount)} / {GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedZombies)} zombies alive, " +
                   $"{GameStats.GetInt(EnumGameStats.AnimalCount)} / {GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedAnimals)} animals. Next wandering horde: {StHorde.NextHorde()}.");
        var presets = StSettings.PresetNames();
        if (presets.Count > 0) con.Output("Presets: " + string.Join(", ", presets));
        foreach (var p in StSettings.Problems) con.Output("PROBLEM: " + p);
    }
}
