using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

// Settings for taming and leveling. Defaults come from the fields below, then AzraelBait.cfg next to ModInfo.xml,
// then AzraelBaitSettings.cfg in the save folder (written by the admin command "/thrall config <key> <value>",
// so runtime changes survive restarts and mod updates). Every public static int/float/bool field is a setting.
public static class BaitConfig
{
    public const string FileName = "AzraelBait.cfg";
    public const string SavedName = "AzraelBaitSettings.cfg";

    // taming
    public static int FeedsToTame = 4;          // meals a plain walker needs
    public static int FeralBonus = 2;           // extra meals for feral
    public static int RadiatedBonus = 4;        // radiated
    public static int ChargedBonus = 6;         // charged and infernal
    public static int AnimalFeedsToTame = 3;    // meals any animal needs
    public static int MaxOwned = 20;            // thralls one player may hold
    public static int MaxActive = 4;            // how many may be out at once (tethered ones may not count, see below)
    public static bool TetheredCountsActive = false; // false: daycare pets don't use up the active slots
    // follow and fight
    public static float FollowDistance = 5f;
    public static float TeleportDistance = 45f;
    public static float TauntRadius = 14f;
    public static float TauntShare = 0.5f;
    public static float TauntSeconds = 8f;
    public static bool OwnerCanHurtThralls = false;
    // tether (daycare)
    public static float TetherRadius = 8f;      // default radius for /thrall tether
    public static float TetherMaxRadius = 40f;
    // zombie leveling
    public static bool ZombieLevels = true;     // wild zombies spawn with a level and grow by killing
    public static int MaxLevel = 300;   // vanilla player level cap
    public static int DaysPerLevel = 5;         // game days per starting level
    public static int KillsPerLevel = 8;        // thralls gain a level per this many kills
    public static int TierLevelBonus = 5;       // starting levels added per tier (feral 1, radiated 2, charged/infernal 3)
    public static float LevelDamagePct = 3f;   // extra damage dealt per level
    public static float LevelToughPct = 3f;    // damage reduction per level (1/(1+pct*level))
    public static bool HealthAlerts = true;     // whisper the owner when a thrall drops below 50%, 25%, 10%
    public static bool Debug = false;

    public static readonly HashSet<string> Exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "zombieDemolition", "zombieScreamer", "zombieScreamerFeral", "zombieScreamerRadiated", "zombieScreamerCharged",
        "animalBossGrace", "animalInsectSwarm", "animalBeeSwarm"
    };

    static string modPath;

    static IEnumerable<FieldInfo> Fields()
    {
        foreach (FieldInfo f in typeof(BaitConfig).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (!f.IsLiteral && (f.FieldType == typeof(int) || f.FieldType == typeof(float) || f.FieldType == typeof(bool))) yield return f;
    }

    static FieldInfo Find(string key)
    {
        foreach (FieldInfo f in Fields()) if (string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase)) return f;
        return null;
    }

    public static void Load(string path) { modPath = path; ReadFile(Path.Combine(path, FileName)); }
    public static void LoadSaved() { ReadFile(Path.Combine(GameIO.GetSaveGameDir(), SavedName)); }

    static void ReadFile(string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (string.Equals(k, "ExcludeZombies", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "Exclude", StringComparison.OrdinalIgnoreCase))
                {
                    Exclude.Clear();
                    foreach (string n in v.Split(',')) if (n.Trim().Length > 0) Exclude.Add(n.Trim());
                    continue;
                }
                string err; Set(k, v, out err);
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read " + file + ": " + e.Message); }
    }

    // Returns true when the key exists and the value parsed.
    public static bool Set(string key, string value, out string error)
    {
        error = null;
        FieldInfo f = Find(key);
        if (f == null) { error = "unknown setting " + key; return false; }
        try
        {
            if (f.FieldType == typeof(int)) f.SetValue(null, int.Parse(value, CultureInfo.InvariantCulture));
            else if (f.FieldType == typeof(float)) f.SetValue(null, float.Parse(value, CultureInfo.InvariantCulture));
            else f.SetValue(null, bool.Parse(value));
            return true;
        }
        catch { error = "bad value for " + f.Name + " (expected " + f.FieldType.Name.ToLowerInvariant() + ")"; return false; }
    }

    public static string Get(string key)
    {
        FieldInfo f = Find(key);
        return f == null ? null : Convert.ToString(f.GetValue(null), CultureInfo.InvariantCulture);
    }

    public static string KeyName(string key) { FieldInfo f = Find(key); return f == null ? null : f.Name; }

    public static List<string> All()
    {
        var l = new List<string>();
        foreach (FieldInfo f in Fields()) l.Add(f.Name + "=" + Convert.ToString(f.GetValue(null), CultureInfo.InvariantCulture));
        return l;
    }

    public static void SaveRuntime()
    {
        try
        {
            var sb = new StringBuilder("# written by /thrall config - overrides AzraelBait.cfg for this save\n");
            foreach (string s in All()) sb.Append(s).Append('\n');
            sb.Append("ExcludeZombies=").Append(string.Join(",", new List<string>(Exclude).ToArray())).Append('\n');
            File.WriteAllText(Path.Combine(GameIO.GetSaveGameDir(), SavedName), sb.ToString());
        }
        catch (Exception e) { Log.Warning("[Bait] could not save settings: " + e.Message); }
    }
}