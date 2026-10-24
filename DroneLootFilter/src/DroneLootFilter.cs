using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

// Drone Loot Filter: companion to Drone Automation (tehAon). Players choose items the Auto-Loot drone leaves alone.
// Drone Automation is only touched through Harmony patches and reflection, so its DLL is not modified or referenced.

public class AzraelDroneLootFilterMod : IModApi
{
    static Harmony harmony;
    static bool patched;

    public void InitMod(Mod mod)
    {
        DroneFilter.LoadCfg(mod.Path);
        harmony = new Harmony("azrael.dronelootfilter");
        TryPatch();
        ModEvents.GameStartDone.RegisterHandler(OnStart);
        ModEvents.GameShutdown.RegisterHandler(DroneFilter.Save);
        ModEvents.ChatMessage.RegisterHandler(DroneFilter.OnChat);
    }

    static void OnStart(ref ModEvents.SGameStartDoneData d)
    {
        TryPatch();
        DroneFilter.Load();
    }

    static void TryPatch()
    {
        if (patched) return;
        try
        {
            Type vac = null, compat = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "DroneAutomation") continue;
                foreach (var t in a.GetTypes())
                {
                    if (t.Name == "VacuumCore") vac = t;
                    else if (t.Name == "StorageCompat") compat = t;
                }
            }
            if (vac == null) { Log.Out("[DroneLootFilter] Drone Automation not loaded (yet)."); return; }
            DroneFilter.BindCompat(compat);
            var me = typeof(DroneFilterPatches);
            harmony.Patch(AccessTools.Method(vac, "Tick"), prefix: new HarmonyMethod(me.GetMethod("TickPre")), finalizer: new HarmonyMethod(me.GetMethod("TickEnd")));
            harmony.Patch(AccessTools.Method(vac, "Absorb"), prefix: new HarmonyMethod(me.GetMethod("AbsorbPre")), postfix: new HarmonyMethod(me.GetMethod("AbsorbPost")));
            harmony.Patch(AccessTools.Method(vac, "IsSettledLoot"), postfix: new HarmonyMethod(me.GetMethod("SettledPost")));
            var lockM = AccessTools.Method(typeof(LockManager), "IsLockedServer");
            if (lockM != null) harmony.Patch(lockM, prefix: new HarmonyMethod(me.GetMethod("LockPre")));
            patched = true;
            Log.Out("[DroneLootFilter] Hooked Drone Automation Auto-Loot. Players use " + DroneFilter.Prefix + ".");
        }
        catch (Exception e) { Log.Warning("[DroneLootFilter] could not hook Drone Automation: " + e.Message); }
    }
}

public static class DroneFilterPatches
{
    public static void TickPre(PersistentPlayerData _ownerData)
    {
        DroneFilter.CurrentOwner = _ownerData != null && _ownerData.PrimaryId != null ? _ownerData.PrimaryId.CombinedString : null;
    }

    public static void TickEnd() { DroneFilter.CurrentOwner = null; }

    public static bool AbsorbPre(ItemStack _moving, ref bool __result, ref string __state)
    {
        __state = null;
        if (DroneFilter.CurrentOwner == null) return true;
        string name = DroneFilter.NameOf(_moving);
        if (name == null) return true;
        if (DroneFilter.Ignored(DroneFilter.CurrentOwner, name)) { __result = false; return false; }
        __state = name;
        return true;
    }

    public static void AbsorbPost(bool __result, string __state)
    {
        if (__result && __state != null && DroneFilter.CurrentOwner != null) DroneFilter.NoteTaken(DroneFilter.CurrentOwner, __state);
    }

    public static void SettledPost(EntityItem _item, ref bool __result)
    {
        if (!__result || DroneFilter.CurrentOwner == null || _item == null) return;
        string name = DroneFilter.NameOf(_item.itemStack);
        if (name != null && DroneFilter.Ignored(DroneFilter.CurrentOwner, name)) __result = false;
    }

    // Containers/bags holding only ignored items are treated like locked ones, so the drone stops re-opening them.
    public static bool LockPre(ILockTarget __0, ref bool __result)
    {
        if (DroneFilter.CurrentOwner == null) return true;
        try
        {
            if (DroneFilter.OnlyIgnoredLeft(DroneFilter.CurrentOwner, __0)) { __result = true; return false; }
        }
        catch { }
        return true;
    }
}

public static class DroneFilter
{
    public static string Prefix = "/dai";
    public static string CurrentOwner;
    static readonly List<string> defaults = new List<string>();
    static readonly Dictionary<string, List<string>> rules = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, List<string>> recent = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, Regex> rx = new Dictionary<string, Regex>();
    static readonly Dictionary<string, bool> cache = new Dictionary<string, bool>();
    static Func<TEFeatureStorage, ItemStack[]> teItems;
    static Func<TEFeatureStorage, bool> teTouched;
    static Func<Bag, ItemStack[]> bagSlots;
    static Func<Bag, bool> bagTouched;
    static string savePath;
    static bool dirty;

    public static void BindCompat(Type compat)
    {
        if (compat == null) return;
        teItems = Field<Func<TEFeatureStorage, ItemStack[]>>(compat, "Items");
        teTouched = Field<Func<TEFeatureStorage, bool>>(compat, "Touched");
        bagSlots = Field<Func<Bag, ItemStack[]>>(compat, "BagSlots");
        bagTouched = Field<Func<Bag, bool>>(compat, "BagTouched");
    }

    static T Field<T>(Type t, string n) where T : class
    {
        var f = t.GetField(n, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return f == null ? null : f.GetValue(null) as T;
    }

    public static void LoadCfg(string modPath)
    {
        try
        {
            string p = Path.Combine(modPath, "DroneLootFilter.cfg");
            if (!File.Exists(p)) return;
            foreach (var raw in File.ReadAllLines(p))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                if (k == "Prefix" && v.StartsWith("/")) Prefix = v;
                else if (k == "DefaultIgnore") foreach (var s in v.Split(',')) if (s.Trim().Length > 0) defaults.Add(s.Trim());
            }
        }
        catch (Exception e) { Log.Warning("[DroneLootFilter] cfg: " + e.Message); }
    }

    // ---- saved per world: one line per rule, "playerKey<TAB>pattern"
    public static void Load()
    {
        try
        {
            rules.Clear(); cache.Clear();
            savePath = Path.Combine(GameIO.GetSaveGameDir(), "AzraelDroneLootFilter.txt");
            if (!File.Exists(savePath)) return;
            foreach (var line in File.ReadAllLines(savePath))
            {
                int i = line.IndexOf('\t');
                if (i <= 0) continue;
                string who = line.Substring(0, i), pat = line.Substring(i + 1);
                List<string> l;
                if (!rules.TryGetValue(who, out l)) rules[who] = l = new List<string>();
                if (!l.Contains(pat)) l.Add(pat);
            }
        }
        catch (Exception e) { Log.Warning("[DroneLootFilter] load: " + e.Message); }
    }

    public static void Save(ref ModEvents.SGameShutdownData d) { Save(); }

    static void Save()
    {
        if (!dirty || savePath == null) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in rules) foreach (var p in kv.Value) sb.Append(kv.Key).Append('\t').Append(p).Append('\n');
            File.WriteAllText(savePath, sb.ToString());
            dirty = false;
        }
        catch (Exception e) { Log.Warning("[DroneLootFilter] save: " + e.Message); }
    }

    // ---- matching
    public static string NameOf(ItemStack s)
    {
        try
        {
            if (s == null || s.IsEmpty() || s.itemValue == null) return null;
            var ic = s.itemValue.ItemClass;
            return ic == null ? null : ic.Name;
        }
        catch { return null; }
    }

    static string Localized(string name)
    {
        try { var ic = ItemClass.GetItemClass(name, true); return ic == null ? name : ic.GetLocalizedItemName(); }
        catch { return name; }
    }

    static Regex Rx(string pattern)
    {
        Regex r;
        if (!rx.TryGetValue(pattern, out r))
            rx[pattern] = r = new Regex("^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return r;
    }

    static bool Match(string pattern, string name)
    {
        var r = Rx(pattern);
        if (r.IsMatch(name)) return true;
        string loc = Localized(name);
        return loc != null && r.IsMatch(loc);
    }

    public static bool Ignored(string who, string name)
    {
        string key = who + "|" + name;
        bool v;
        if (cache.TryGetValue(key, out v)) return v;
        v = false;
        foreach (var d in defaults) if (Match(d, name)) { v = true; break; }
        List<string> l;
        if (!v && rules.TryGetValue(who, out l)) foreach (var p in l) if (Match(p, name)) { v = true; break; }
        if (cache.Count > 5000) cache.Clear();
        cache[key] = v;
        return v;
    }

    static bool AllIgnored(string who, ItemStack[] items)
    {
        if (items == null) return false;
        int seen = 0;
        foreach (var s in items)
        {
            string n = NameOf(s);
            if (n == null) continue;
            seen++;
            if (!Ignored(who, n)) return false;
        }
        return seen > 0;
    }

    public static bool OnlyIgnoredLeft(string who, ILockTarget target)
    {
        var te = target as TEFeatureStorage;
        if (te != null)
        {
            if (teItems == null || teTouched == null || !teTouched(te)) return false;
            return AllIgnored(who, teItems(te));
        }
        var ent = target as Entity;
        if (ent != null && ent.bag != null && bagSlots != null && bagTouched != null)
        {
            if (!bagTouched(ent.bag)) return false;
            return AllIgnored(who, bagSlots(ent.bag));
        }
        return false;
    }

    public static void NoteTaken(string who, string name)
    {
        List<string> l;
        if (!recent.TryGetValue(who, out l)) recent[who] = l = new List<string>();
        l.Remove(name);
        l.Insert(0, name);
        if (l.Count > 20) l.RemoveAt(l.Count - 1);
    }

    // ---- chat
    static void Tell(int id, string msg)
    {
        try
        {
            GameManager.Instance.ChatMessageServer(null, EChatType.Whisper, -1, "[a8d8ff][Drone filter][-] " + msg,
                new List<int> { id }, EMessageSender.None);
        }
        catch (Exception e) { Log.Warning("[DroneLootFilter] chat: " + e.Message); }
    }

    public static ModEvents.EModEventResult OnChat(ref ModEvents.SChatMessageData data)
    {
        try
        {
            if (data.SenderEntityId == -1 || string.IsNullOrEmpty(data.Message)) return ModEvents.EModEventResult.Continue;
            string msg = data.Message.Trim();
            if (msg.Length == 0 || msg[0] != '/') return ModEvents.EModEventResult.Continue;
            var parts = msg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!string.Equals(parts[0], Prefix, StringComparison.OrdinalIgnoreCase)) return ModEvents.EModEventResult.Continue;
            Run(data.SenderEntityId, parts);
            return ModEvents.EModEventResult.StopHandlersAndVanilla;
        }
        catch (Exception e) { Log.Warning("[DroneLootFilter] command failed: " + e); return ModEvents.EModEventResult.Continue; }
    }

    static string KeyOf(int entityId)
    {
        var pd = GameManager.Instance.GetPersistentPlayerList().GetPlayerDataFromEntityID(entityId);
        return pd != null && pd.PrimaryId != null ? pd.PrimaryId.CombinedString : null;
    }

    static List<string> Mine(string who, bool create)
    {
        List<string> l;
        if (!rules.TryGetValue(who, out l) && create) rules[who] = l = new List<string>();
        return l;
    }

    static void Run(int id, string[] parts)
    {
        string who = KeyOf(id);
        if (who == null) return;
        string sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "help";
        string rest = parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2) : null;
        string p = Prefix;
        switch (sub)
        {
            case "list":
            {
                var l = Mine(who, false);
                if ((l == null || l.Count == 0) && defaults.Count == 0) { Tell(id, "Your drone ignores nothing. " + p + " last shows what it took; " + p + " add <item> to ignore something."); break; }
                var sb = new StringBuilder("Your drone leaves these alone:");
                if (l != null) for (int i = 0; i < l.Count; i++) sb.Append("\n ").Append(i + 1).Append(". ").Append(l[i]);
                foreach (var d in defaults) sb.Append("\n (server) ").Append(d);
                Tell(id, sb.ToString());
                break;
            }
            case "last":
            {
                List<string> l;
                if (!recent.TryGetValue(who, out l) || l.Count == 0) { Tell(id, "Your drone hasn't taken anything since the server started."); break; }
                var sb = new StringBuilder("Last things your drone took (" + p + " skip <n> to ignore one):");
                for (int i = 0; i < l.Count; i++) sb.Append("\n ").Append(i + 1).Append(". ").Append(Localized(l[i])).Append(" [").Append(l[i]).Append("]");
                Tell(id, sb.ToString());
                break;
            }
            case "skip":
            {
                int n; List<string> l;
                if (rest == null || !int.TryParse(rest, out n) || !recent.TryGetValue(who, out l) || n < 1 || n > l.Count) { Tell(id, "Usage: " + p + " skip <number from " + p + " last>"); break; }
                AddRule(id, who, l[n - 1]);
                break;
            }
            case "add":
            case "ignore":
            {
                if (rest == null) { Tell(id, "Usage: " + p + " add <item name, * wildcard allowed> or " + p + " add held"); break; }
                string pat = rest;
                if (rest.Equals("held", StringComparison.OrdinalIgnoreCase))
                {
                    var pl = GameManager.Instance.World.GetEntity(id) as EntityPlayer;
                    var ic = pl != null && pl.inventory != null ? pl.inventory.holdingItem : null;
                    if (ic == null || ic.Name == "air") { Tell(id, "You aren't holding anything."); break; }
                    pat = ic.Name;
                }
                AddRule(id, who, pat);
                break;
            }
            case "remove":
            case "unignore":
            case "allow":
            {
                var l = Mine(who, false);
                if (rest == null || l == null) { Tell(id, "Usage: " + p + " remove <number or name> (see " + p + " list)"); break; }
                int n; string hit = null;
                if (int.TryParse(rest, out n) && n >= 1 && n <= l.Count) hit = l[n - 1];
                else hit = l.FirstOrDefault(x => string.Equals(x, rest, StringComparison.OrdinalIgnoreCase));
                if (hit == null) { Tell(id, "That isn't on your list."); break; }
                l.Remove(hit); dirty = true; cache.Clear(); Save();
                Tell(id, "Your drone will take " + hit + " again.");
                break;
            }
            case "clear":
            {
                var l = Mine(who, false);
                if (l != null) l.Clear();
                dirty = true; cache.Clear(); Save();
                Tell(id, "Ignore list cleared.");
                break;
            }
            case "find":
            {
                if (rest == null) { Tell(id, "Usage: " + p + " find <part of a name>"); break; }
                var found = new List<string>();
                string q = rest.ToLowerInvariant();
                foreach (var ic in ItemClass.list)
                {
                    if (ic == null) continue;
                    string loc = ic.GetLocalizedItemName() ?? "";
                    if (ic.Name.ToLowerInvariant().Contains(q) || loc.ToLowerInvariant().Contains(q)) found.Add(loc + " [" + ic.Name + "]");
                    if (found.Count >= 15) break;
                }
                Tell(id, found.Count == 0 ? "No item matches '" + rest + "'." : "Matches (use the name in [ ] or in-game name with " + p + " add):\n " + string.Join("\n ", found.ToArray()));
                break;
            }
            default:
                Tell(id, "Drone loot filter:\n " + p + " last / skip <n>  - ignore something the drone just took\n " + p + " add <item|pattern*|held>  - ignore an item\n " +
                         p + " remove <n|name> / list / clear / find <text>");
                break;
        }
    }

    static void AddRule(int id, string who, string pat)
    {
        var l = Mine(who, true);
        if (l.Any(x => string.Equals(x, pat, StringComparison.OrdinalIgnoreCase))) { Tell(id, pat + " is already ignored."); return; }
        l.Add(pat); dirty = true; cache.Clear(); Save();
        Tell(id, "Your drone will leave " + pat + " alone from now on (bags, containers and ground items).");
    }
}
