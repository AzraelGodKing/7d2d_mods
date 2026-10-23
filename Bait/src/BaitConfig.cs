using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// Settings for taming. Read from AzraelBait.cfg next to ModInfo.xml (key=value, # starts a comment).
// Anything missing keeps the default below. Changes need a server restart.
public static class BaitConfig
{
    public const string FileName = "AzraelBait.cfg";

    public static int FeedsToTame = 4;       // meals a plain walker needs
    public static int FeralBonus = 2;        // extra meals on top for feral
    public static int RadiatedBonus = 4;     // radiated
    public static int ChargedBonus = 6;      // charged and infernal
    public static int MaxOwned = 6;          // thralls one player may hold
    public static int MaxActive = 2;         // how many of those may be out at once
    public static float FollowDistance = 5f;     // metres of slack before a thrall closes on its owner
    public static float TeleportDistance = 45f;  // beyond this a thrall snaps to its owner
    public static float TauntRadius = 14f;       // wild zombies within this of a thrall may go for it
    public static float TauntShare = 0.5f;       // fraction of them that do (rolled once per zombie)
    public static float TauntSeconds = 8f;       // how long a taunted zombie is held off players
    public static bool OwnerCanHurtThralls = false;
    public static bool Debug = false;

    public static readonly HashSet<string> Exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "zombieDemolition", "zombieScreamer", "zombieScreamerFeral", "zombieScreamerRadiated", "zombieScreamerCharged"
    };

    public static void Load(string modPath)
    {
        try
        {
            string file = Path.Combine(modPath, FileName);
            if (!File.Exists(file)) return;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                switch (k.ToLowerInvariant())
                {
                    case "feedstotame": Int(v, ref FeedsToTame); break;
                    case "feralbonus": Int(v, ref FeralBonus); break;
                    case "radiatedbonus": Int(v, ref RadiatedBonus); break;
                    case "chargedbonus": Int(v, ref ChargedBonus); break;
                    case "maxowned": Int(v, ref MaxOwned); break;
                    case "maxactive": Int(v, ref MaxActive); break;
                    case "followdistance": Flt(v, ref FollowDistance); break;
                    case "teleportdistance": Flt(v, ref TeleportDistance); break;
                    case "tauntradius": Flt(v, ref TauntRadius); break;
                    case "tauntshare": Flt(v, ref TauntShare); break;
                    case "tauntseconds": Flt(v, ref TauntSeconds); break;
                    case "ownercanhurtthralls": Bool(v, ref OwnerCanHurtThralls); break;
                    case "debug": Bool(v, ref Debug); break;
                    case "excludezombies":
                        Exclude.Clear();
                        foreach (string n in v.Split(',')) if (n.Trim().Length > 0) Exclude.Add(n.Trim());
                        break;
                }
            }
        }
        catch (Exception e) { Log.Warning("[Bait] could not read " + FileName + ": " + e.Message); }
    }

    static void Int(string s, ref int f) { int r; if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) f = r; }
    static void Flt(string s, ref float f) { float r; if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) f = r; }
    static void Bool(string s, ref bool f) { bool r; if (bool.TryParse(s, out r)) f = r; }
}