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
    public enum Kind { Density, Respawn, Count }

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
    };

    static StSetting Density(string key, string help, Func<int> get, Action<int> set) =>
        new StSetting { Key = key, Type = StSetting.Kind.Density, Get = get, Set = set, Help = help };
    static StSetting Respawn(string key, string help, Func<int> get, Action<int> set) =>
        new StSetting { Key = key, Type = StSetting.Kind.Respawn, Get = get, Set = set, Help = help };
    static StSetting Count(string key, string help, Func<int> get, Action<int> set) =>
        new StSetting { Key = key, Type = StSetting.Kind.Count, Get = get, Set = set, Help = help };

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
            default:
                if (int.TryParse(t, out value) && value >= 0 && value <= 1000) return true;
                error = "use a number from 0 to 1000";
                return false;
        }
    }

    public static string Display(StSetting s, int value)
    {
        switch (s.Type)
        {
            case StSetting.Kind.Density: return value == -1 ? "none" : value == 0 ? "default" : value.ToString();
            case StSetting.Kind.Respawn: return value >= 0 && value < RespawnNames.Length ? RespawnNames[value] : value.ToString();
            default: return value.ToString();
        }
    }

    public static void Load()
    {
        Problems.Clear();
        foreach (var s in All) s.Override = null;
        var path = Path.Combine(ModPath ?? "", FileName);
        try
        {
            if (!File.Exists(path)) return;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { Problems.Add($"'{line}' in {FileName} has no '='"); continue; }
                var s = Find(line.Substring(0, eq).Trim());
                if (s == null) { Problems.Add($"'{line.Substring(0, eq).Trim()}' in {FileName} isn't a setting"); continue; }
                if (TryParse(s, line.Substring(eq + 1), out var v, out var err)) s.Override = v;
                else Problems.Add($"{s.Key} in {FileName}: {err}");
            }
        }
        catch (Exception e) { Problems.Add($"could not read {FileName}: {e.Message}"); }
    }

    // Write one setting to spawns.txt: replace its line (commented or not), or add one at the end.
    // A null value comments the line out, so the world's value is used again.
    public static void Save(StSetting s)
    {
        var path = Path.Combine(ModPath ?? "", FileName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        string newLine = s.Override.HasValue ? $"{s.Key} = {Display(s, s.Override.Value)}" : null;
        bool done = false;
        for (int i = 0; i < lines.Count; i++)
        {
            var body = lines[i].Trim().TrimStart('#').Trim();
            int eq = body.IndexOf('=');
            if (eq <= 0 || !body.Substring(0, eq).Trim().Equals(s.Key, StringComparison.OrdinalIgnoreCase)) continue;
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
            // Game prefs aren't reset with the sandbox options, so once overridden the
            // pref holds our value: keep the first one we saw.
            bool isPref = s.Key == "MaxZombies" || s.Key == "MaxAnimals";
            if (!isPref || !s.HaveWorldValue) { s.WorldValue = s.Get(); s.HaveWorldValue = true; }
        }
        Apply();
    }

    public static void Apply()
    {
        foreach (var s in All)
        {
            if (s.Override.HasValue) s.Set(s.Override.Value);
            else if (s.HaveWorldValue) s.Set(s.WorldValue);
        }
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
        var set = All.Where(s => s.Override.HasValue).Select(s => $"{s.Key} = {Display(s, s.Override.Value)}").ToList();
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
                                        "e.g. spawns NightDensity 4   spawns NightRespawn fast   spawns BloodMoonCount 16";

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
            con.Output($"{s.Key} is now {StSettings.Display(s, s.Get())}" + (s.Override.HasValue ? "." : " (the world's value)."));
            if (s.Type != StSetting.Kind.Count)
                con.Output("Areas pick this up as their timers run out. Type 'spawns refresh' to apply it now.");
            return;
        }
        if (_params.Count == 1) { con.Output(getHelp()); return; }

        con.Output("Spawn Tuner (setting: now / world's value):");
        foreach (var s in StSettings.All)
        {
            var world = s.HaveWorldValue ? StSettings.Display(s, s.WorldValue) : "?";
            con.Output($"  {s.Key,-19} {StSettings.Display(s, s.Get()),-8} / {world,-8} {(s.Override.HasValue ? "*" : " ")} {s.Help}");
        }
        con.Output("* = changed by Spawn Tuner. Change one with: spawns <setting> <value>");
        foreach (var p in StSettings.Problems) con.Output("PROBLEM: " + p);
    }
}
