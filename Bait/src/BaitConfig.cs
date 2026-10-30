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
    public static bool KatsSpecial = true;      // Kats Special: every player may hold 27 thralls, whatever MaxOwned says
    public const int KatsSpecialOwned = 27;
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
    public static bool TrapsSpareThralls = true;    // spikes, barbed wire, blade traps and electric fence shocks do not hurt thralls (or wear the trap down)
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
    // animal thralls (1.11.0): wild animals never got the day/tier starting level wild zombies get, and animals can't evolve
    public static bool AnimalDayLevels = true;  // an animal thrall is kept at least at the level a wild walker of today starts at (zombie animals as feral)
    public static float AnimalDamagePct = 50f;  // animal thralls deal this much more damage
    public static float AnimalToughPct = 50f;   // damage taken by animal thralls is divided by 1 + this percent
    // taming extras
    public static int MaxFeeds = 27;             // hard cap on meals needed for anything (0 = no cap)
    public static int FeedsPerLevelStep = 17;   // +1 meal per this many levels of the target (0 = off)
    // thrall care
    public static int ReviveSeconds = 300;      // a fallen thrall returns after this long (0 = gone for good)
    public static int ReviveCostPlain = 3;        // /thrall revive costs this many Plain bait...
    public static int ReviveCostRich = 2;         // ...or this many Rich bait...
    public static int ReviveCostFeast = 1;        // ...or this many Feast bait. Tried in that order; the first you can afford is taken.
    public static bool AutoRevive = true;       // players may turn on auto-revive for a thrall (/thrall autorevive <n>)
    public static int AutoReviveCost = 3;       // plain Zombie and Animal Bait an auto-revive takes from the owner's inventory
    public static bool AutoHeal = true;         // players may turn on auto-heal for a thrall (/thrall autoheal <n>)
    public static float AutoHealBelowPct = 30f; // auto-heal kicks in below this much health
    public static float AutoHealCooldownSeconds = 20f; // at most one auto-heal per thrall this often
    public static bool LowBaitWarning = true;   // toast when you carry too little plain bait for auto-revive/auto-heal
    public static float HereCooldownSeconds = 30f; // /thrall here once every this many seconds (0 = no cooldown)
    public static int MaxTeams = 8;             // teams one player may save
    public static bool ReturnUneatenBait = true; // a thrown bait nothing ate goes back to the thrower when it stops calling
    // roles (1.10.0)
    public static int PerkLevel1 = 10;          // levels that open the three perk slots (/thrall perk)
    public static int PerkLevel2 = 25;
    public static int PerkLevel3 = 50;
    public static float PerkToughPct = 25f;     // tough perk: damage taken is divided by 1 + this percent
    public static float PerkHitPct = 25f;       // hit perk: extra damage to creatures
    public static float PerkRegenMult = 3f;     // regen perk: regen times this, also while fighting
    public static int PerkResetFeast = 1;       // Feast Bait to clear a thrall's perks (0 = free)
    public static float GuardAlertSeconds = 60f; // a guard or patrol fighting while you are away tells you at most this often
    public static int PatrolMaxPoints = 8;
    public static bool BloodMoonMode = true;    // players may pick a blood moon mode (/thrall bloodmoon)
    public static float ScoutDistance = 30f;    // /thrall scout runs this far ahead...
    public static float ScoutRange = 40f;       // ...and reports hostiles within this many blocks of where it got to
    public static float ScoutCooldownSeconds = 30f;
    public static int EvolveLevel = 50;         // /thrall evolve: zombie thralls of this level or more (0 = off)
    public static int EvolveFeastCost = 3;      // Feast Bait an evolution takes
    public static bool WebhookEvents = true;    // post rare tames, level milestones and evolutions to Discord (webhook.txt, or Blood Moon Sound's discord.txt)
    public static int WebhookLevelStep = 50;    // post at every multiple of this level (0 = never)
    public static bool TamingHelper = true;     // Bait UI shows meals needed for the creature you aim at while holding bait
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
    public static bool RestoreLostBait = true;  // give back bait a player held at logout but no longer has at their next login (see BaitLedger.cs)
    // batch 3 (1.12.0)
    public static float BackpackGuardSeconds = 120f; // when you die, the thralls with you guard your backpack this long (0 = off)
    public static float BackpackGuardRadius = 10f;
    public static bool BodyguardRush = true;    // below BodyguardHpPct health, thralls with you turn aggressive and pull attackers off you
    public static float BodyguardHpPct = 30f;
    public static float BodyguardSeconds = 10f;
    public static float BodyguardCooldownSeconds = 60f;
    public static bool SneakFollow = true;      // while you crouch, followers walk, start no fights and leave sleepers alone
    public static float ScreamRadius = 60f;     // /thrall scream: a tamed screamer calls wild zombies from this far
    public static float ScreamCooldownSeconds = 600f;
    public static bool PvpThralls = false;      // players may turn on /thrall pvp (both players must have it on; never party or allies)
    public static float DuelSeconds = 120f;     // /thrall duel: longest a duel lasts (it ends at 10% health)
    public static float DuelLosePct = 10f;
    public static int SecondChanceFeast = 1;    // permadeath servers (ReviveSeconds 0): Feast Bait for one saved life (0 = off)
    public static int SecondChanceSeconds = 60; // how long a saved thrall stays downed
    public static bool Whistle = true;          // the Thrall Whistle item does /thrall here
    public static bool CatchUpXp = true;        // kills count double for a thrall more than CatchUpGap levels below your best
    public static int CatchUpGap = 10;
    public static int RetireLevel = 25;         // /thrall retire: lowest level
    public static int RetireRich = 2;           // reward under RetireFeastLevel
    public static int RetireFeastLevel = 50;    // at or above this level: one Feast Bait per this many levels (up to 5)
    public static float OwnerXpSharePct = 25f;  // the owner gets this much of the game XP of a thrall's kill nearby (0 = off)
    public static bool QuestKillCredit = true;  // thrall kills near you count for quests and challenges
    public static float OwnerCreditRange = 30f; // range for the two above
    public static int AlphaEveryDays = 7;       // a named Alpha appears near a player the day after every this many days (8, 15, 22...); only Feast Bait tames it (0 = off)
    public static int AlphaLevelBonus = 10;
    public static bool Feeders = true;          // the Bait Feeder block tames animals for its owner, even while away
    public static float FeederSeconds = 30f;    // a feeder feeds one animal one bait this often
    public static float FeederRadius = 12f;
    public static float FeederTetherRadius = 8f;
    public static float TamingResistPct = 0f;   // chance a radiated, charged or infernal creature breaks off a meal (0 = off)
    public static float KennelRadius = 6f;      // the Thrall Kennel block: tether circle and healing area
    public static float KennelHealPct = 5f;     // extra health every 5 s inside a kennel's circle
    public static bool QuietThralls = true;     // thralls make no heat (noise from them is not sent to the AI director)
    public static int NameMaxLength = 24;       // longest thrall name (banned words: /thrall admin banword)
    public static bool Debug = false;

    // Animal classes whose AI list is replaced by other mods and so needs the bait task added in code.
    public static readonly HashSet<string> NeedsBaitAi = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "animalBearCub" };

    public static readonly HashSet<string> Exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // empty: every creature can be tamed. Add class names with  Exclude=name1,name2  to protect some.
    };

    // Per-creature limits an admin set with "/thrall config cap.<class> <n>" (0 removes it).
    public static readonly Dictionary<string, int> Caps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public static int CapFor(string cls) { int n; return cls != null && Caps.TryGetValue(cls, out n) ? n : 0; }
    // Words a thrall name may not contain (any case).
    public static readonly HashSet<string> BannedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static string CapsSummary()
    {
        if (Caps.Count == 0) return "no per-creature caps";
        var parts = new List<string>();
        foreach (var kv in Caps) parts.Add(kv.Key + " " + kv.Value);
        return string.Join(", ", parts.ToArray());
    }

    // ------------------------------------------------------------------ presets
    static Dictionary<string, string> defaults;
    static void Snapshot()
    {
        if (defaults != null) return;
        defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (FieldInfo f in Fields()) defaults[f.Name] = Convert.ToString(f.GetValue(null), CultureInfo.InvariantCulture);
    }

    static readonly Dictionary<string, string> Casual = new Dictionary<string, string>
    {
        {"FeedsToTame","2"}, {"FeralBonus","1"}, {"RadiatedBonus","2"}, {"ChargedBonus","3"}, {"AnimalFeedsToTame","2"}, {"MaxActive","6"},
        {"ReviveSeconds","60"}, {"ReviveCostPlain","1"}, {"ReviveCostRich","1"}, {"ReviveCostFeast","1"}, {"KillsPerLevel","5"},
        {"TamingResistPct","0"}, {"HereCooldownSeconds","10"}, {"RegenPctPerTick","2"},
    };
    static readonly Dictionary<string, string> Hardcore = new Dictionary<string, string>
    {
        {"FeedsToTame","6"}, {"FeralBonus","3"}, {"RadiatedBonus","6"}, {"ChargedBonus","9"}, {"AnimalFeedsToTame","4"}, {"MaxActive","3"},
        {"ReviveSeconds","0"}, {"ReviveCostPlain","5"}, {"ReviveCostRich","3"}, {"ReviveCostFeast","2"}, {"KillsPerLevel","12"},
        {"TamingResistPct","15"}, {"HereCooldownSeconds","60"}, {"RegenPctPerTick","0.5"},
    };

    // casual, default (the values before any config file) or hardcore. Returns false for an unknown name.
    public static bool ApplyPreset(string name)
    {
        Snapshot();
        Dictionary<string, string> set;
        string n = (name ?? "").ToLowerInvariant();
        if (n == "casual") set = Casual;
        else if (n == "hardcore") set = Hardcore;
        else if (n == "default") set = defaults;
        else return false;
        if (n != "default") foreach (var kv in Casual) { string d; if (defaults.TryGetValue(kv.Key, out d)) { string e; Set(kv.Key, d, out e); } }
        foreach (var kv in set) { string e; Set(kv.Key, kv.Value, out e); }
        return true;
    }

    static string modPath;
    public static string ModPath { get { return modPath; } }

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

    public static void Load(string path) { Snapshot(); modPath = path; ReadFile(Path.Combine(path, FileName)); }
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
                if (string.Equals(k, "BannedNames", StringComparison.OrdinalIgnoreCase))
                {
                    BannedNames.Clear();
                    foreach (string n in v.Split(',')) if (n.Trim().Length > 0) BannedNames.Add(n.Trim());
                    continue;
                }
                if (string.Equals(k, "Caps", StringComparison.OrdinalIgnoreCase))
                {
                    Caps.Clear(); ReadFeedList(Caps, v);
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
        if (key != null && key.StartsWith("cap.", StringComparison.OrdinalIgnoreCase) && key.Length > 4)
        {
            int c;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out c) || c < 0) { error = "bad value for " + key + " (expected a whole number, 0 removes the cap)"; return false; }
            string ccls = key.Substring(4);
            if (c == 0) Caps.Remove(ccls); else Caps[ccls] = c;
            return true;
        }
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
        if (string.Equals(key, "caps", StringComparison.OrdinalIgnoreCase)) return CapsSummary();
        if (key != null && key.StartsWith("cap.", StringComparison.OrdinalIgnoreCase)) return CapFor(key.Substring(4)).ToString();
        if (IsFeedKey(key, out fcls)) return AnimalFeeds.ContainsKey(fcls) ? AnimalFeeds[fcls].ToString() : null;
        FieldInfo f = Find(key);
        return f == null ? null : Convert.ToString(f.GetValue(null), CultureInfo.InvariantCulture);
    }

    public static string KeyName(string key)
    {
        string fcls;
        if (string.Equals(key, "feeds", StringComparison.OrdinalIgnoreCase)) return "feeds";
        if (string.Equals(key, "caps", StringComparison.OrdinalIgnoreCase)) return "caps";
        if (key != null && key.StartsWith("cap.", StringComparison.OrdinalIgnoreCase) && key.Length > 4) return "Cap." + key.Substring(4);
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
            if (Caps.Count > 0)
            {
                var cp = new List<string>();
                foreach (var kv in Caps) cp.Add(kv.Key + ":" + kv.Value);
                sb.Append("Caps=").Append(string.Join(",", cp.ToArray())).Append('\n');
            }
            sb.Append("BannedNames=").Append(string.Join(",", new List<string>(BannedNames).ToArray())).Append('\n');
            sb.Append("Exclude=").Append(string.Join(",", new List<string>(Exclude).ToArray())).Append('\n');
            File.WriteAllText(Path.Combine(GameIO.GetSaveGameDir(), SavedName), sb.ToString());
        }
        catch (Exception e) { Log.Warning("[Bait] could not save settings: " + e.Message); }
    }
}
