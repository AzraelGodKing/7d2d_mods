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
    public static int RichBaitMeals = 2;      // meals one Rich Bait counts as
    public static int FeastBaitMeals = 4;     // meals one Feast Bait counts as
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
    public static bool ThrallsCountTowardSpawnCap = false; // false: live thralls are not counted against MaxSpawnedZombies / MaxSpawnedAnimals
    public static bool OthersCanHurtThralls = false; // false: no player can hurt anyone else's thrall
    public static bool AutoStowInVehicle = true;
    public static bool DronesIgnoreSpikes = true;   // spikes and other damage blocks do not hurt drones
    // (AutoStowInVehicle above: thralls go inactive while you ride a vehicle and come back when you get off)
    public static bool OwnerCanHurtThralls = false;
    // tether (daycare)
    public static float TetherRadius = 8f;      // default radius for /thrall tether
    public static float TetherMaxRadius = 40f;
    // zombie leveling
    public static bool ZombieLevels = true;     // wild zombies spawn with a level and grow by killing
    public static int MaxLevel = 300;   // vanilla player level cap
    public static int DaysPerLevel = 5;         // game days per starting level
    public static float OwnerKillShareRange = 30f;   // an active thrall gets credit for a hostile kill its owner makes within this many blocks (0 = off)
    public static int KillsPerLevel = 8;        // thralls gain a level per this many kills
    public static int TierLevelBonus = 5;       // starting levels added per tier (feral 1, radiated 2, charged/infernal 3)
    public static float LevelDamagePct = 3f;   // extra damage dealt per level
    public static float LevelToughPct = 3f;    // damage reduction per level (1/(1+pct*level))
    // taming extras
    public static int MaxFeeds = 27;             // hard cap on meals needed for anything (0 = no cap)
    public static int FeedsPerLevelStep = 17;   // +1 meal per this many levels of the target (0 = off)
    // thrall care
    public static int ReviveSeconds = 300;      // a fallen thrall returns after this long (0 = gone for good)
    public static int ReviveCostPlain = 3;        // /thrall revive costs this many Plain bait...
    public static int ReviveCostRich = 2;         // ...or this many Rich bait...
    public static int ReviveCostFeast = 1;        // ...or this many Feast bait. Tried in that order; the first you can afford is taken.
    public static int HealBaitCost = 1;         // bait for /thrall heal
    public static float NewThrallGraceSeconds = 8f; // a thrall that was just tamed or called can't be targeted or hurt for this long (0 = off)
    public static float HealPct = 40f;          // health restored by /thrall heal
    public static float RegenPctPerTick = 1f;   // health a thrall regains every 5 s while not fighting
    public static float DefendRange = 8f;       // defend mode: fights only what comes this close to it or its owner
    public static bool TetherRespectClaims = true; // no tethers inside someone else's land claim
    public static int PackSlotsPerAnimal = 4;   // bag slots each animal thrall that follows you adds (0 = off)
    public static int PackSlotsMax = 48;        // cap on the total bonus
    public static bool LookHp = true;           // aim at a thrall (or a wild zombie/animal) to see its name, level and HP as a toast
    public static bool LookHpWild = true;       // include wild zombies and animals, not just your thralls
    public static float LookHpRange = 25f;
    public static float LookHpAngle = 12f;       // degrees of aim tolerance
    public static bool UseToasts = true;        // events (fed, tamed, level up, alerts) show as an on-screen toast instead of chat
    public static bool HealthAlerts = true;     // whisper the owner when a thrall drops below 50%, 25%, 10%
    public static bool RestoreRosterFromBackup = true; // a save with no (or an empty or unreadable) thrall roster takes the kept copy from <UserDataFolder>/AzraelBait
    public static bool Debug = false;

    // Animal classes whose AI list is replaced by other mods and so needs the bait task added in code.
    public static readonly HashSet<string> NeedsBaitAi = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "animalBearCub" };

    public static readonly HashSet<string> Exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // empty: every creature can be tamed. Add class names with  Exclude=name1,name2  to protect some.
    };

    static string modPath;

    // Meals each kind of animal needs (AnimalFeedsToTame is the fallback for anything not listed).
    // Set in AzraelBait.cfg:  AnimalFeeds=animalChicken:1,animalBear:6
    public static readonly Dictionary<string, int> AnimalFeeds = DefaultAnimalFeeds();

    static Dictionary<string, int> DefaultAnimalFeeds()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        const string s = "animalChicken:1,animalRabbit:1,animalSnake:2,animalPig:2,animalStag:2,animalDoe:2,animalBoar:3,animalCoyote:3,"
                       + "animalBearSmall:4,animalWolf:4,animalZombieDog:4,animalZombieVulture:4,animalMountainLion:5,animalZombieBoar:5,"
                       + "animalBear:6,animalZombieVultureRadiated:6,animalDireWolf:8,animalZombieBear:8";
        ReadFeedList(d, s);
        return d;
    }

    static void ReadFeedList(Dictionary<string, int> d, string v)
    {
        foreach (string pair in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int c = pair.IndexOf(':');
            int n;
            if (c <= 0 || !int.TryParse(pair.Substring(c + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1) continue;
            d[pair.Substring(0, c).Trim()] = n;
        }
    }

    public static int FeedsForAnimal(string cls)
    {
        int n;
        return cls != null && AnimalFeeds.TryGetValue(cls, out n) ? n : AnimalFeedsToTame;
    }

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
    public static void LoadSaved() { savedPass = true; try { ReadFile(Path.Combine(GameIO.GetSaveGameDir(), SavedName)); } finally { savedPass = false; } }

    // Feed rates an admin set in game with "/thrall config feeds.<class> <n>"; only these are written to the save settings.
    static readonly Dictionary<string, int> feedOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    static bool savedPass;

    static bool IsFeedKey(string key, out string cls)
    {
        cls = null;
        if (key == null || !key.StartsWith("feeds.", StringComparison.OrdinalIgnoreCase) || key.Length <= 6) return false;
        cls = key.Substring(6);
        return true;
    }

    public static string FeedsSummary()
    {
        var parts = new List<string>();
        foreach (var kv in AnimalFeeds) parts.Add(kv.Key.StartsWith("animal") ? kv.Key.Substring(6) + " " + kv.Value : kv.Key + " " + kv.Value);
        return string.Join(", ", parts.ToArray()) + " (other animals " + AnimalFeedsToTame + ")";
    }

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
                if (string.Equals(k, "Exclude", StringComparison.OrdinalIgnoreCase))
                {
                    Exclude.Clear();
                    foreach (string n in v.Split(',')) if (n.Trim().Length > 0) Exclude.Add(n.Trim());
                    continue;
                }
                if (string.Equals(k, "AnimalFeeds", StringComparison.OrdinalIgnoreCase))
                {
                    ReadFeedList(AnimalFeeds, v);
                    if (savedPass) ReadFeedList(feedOverrides, v);
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
        string fcls;
        if (IsFeedKey(key, out fcls))
        {
            int n;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1) { error = "bad value for " + key + " (expected a whole number, 1 or more)"; return false; }
            if (!fcls.StartsWith("animal", StringComparison.OrdinalIgnoreCase)) { error = "feed rates are for animals; the class should start with animal, like animalBear"; return false; }
            AnimalFeeds[fcls] = n; feedOverrides[fcls] = n;
            return true;
        }
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
        string fcls;
        if (string.Equals(key, "feeds", StringComparison.OrdinalIgnoreCase)) return FeedsSummary();
        if (IsFeedKey(key, out fcls)) return AnimalFeeds.ContainsKey(fcls) ? AnimalFeeds[fcls].ToString() : null;
        FieldInfo f = Find(key);
        return f == null ? null : Convert.ToString(f.GetValue(null), CultureInfo.InvariantCulture);
    }

    public static string KeyName(string key)
    {
        string fcls;
        if (string.Equals(key, "feeds", StringComparison.OrdinalIgnoreCase)) return "feeds";
        if (IsFeedKey(key, out fcls)) return "Feeds." + fcls;
        FieldInfo f = Find(key); return f == null ? null : f.Name;
    }

    // name|value|type for the client UI (type: i int, f float, b bool)
    public static List<string> Describe()
    {
        var l = new List<string>();
        foreach (FieldInfo f in Fields())
            l.Add("C|" + f.Name + "|" + Convert.ToString(f.GetValue(null), CultureInfo.InvariantCulture) + "|" + (f.FieldType == typeof(int) ? "i" : f.FieldType == typeof(float) ? "f" : "b"));
        return l;
    }

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
            if (feedOverrides.Count > 0)
            {
                var fp = new List<string>();
                foreach (var kv in feedOverrides) fp.Add(kv.Key + ":" + kv.Value);
                sb.Append("AnimalFeeds=").Append(string.Join(",", fp.ToArray())).Append('\n');
            }
            sb.Append("Exclude=").Append(string.Join(",", new List<string>(Exclude).ToArray())).Append('\n');
            File.WriteAllText(Path.Combine(GameIO.GetSaveGameDir(), SavedName), sb.ToString());
        }
        catch (Exception e) { Log.Warning("[Bait] could not save settings: " + e.Message); }
    }
}
