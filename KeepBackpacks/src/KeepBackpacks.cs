using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// Server-side only. Players do not install this mod.
//
// settings.txt is the source of truth for how long each bag stays. Those numbers are written
// into Config/entityclasses.xml before the game loads configs, so the server sends the same
// times to clients when they connect. kbags reload does that again without a process restart.
// A new DLL still needs a restart: the game loads each mod assembly once at boot.

public class AzraelKeepBackpacks : IModApi
{
    public const string Tag = "[KeepBackpacks]";

    public void InitMod(Mod _modInstance)
    {
        KbConfig.ModPath = _modInstance.Path;
        KbConfig.Load();
        KbConfig.WriteXml();
        ModEvents.GameStartDone.RegisterHandler(OnGameStart);
        Log.Out($"{Tag} Loaded. Times come from settings.txt. Players do not install this mod.");
    }

    static void OnGameStart(ref ModEvents.SGameStartDoneData _data)
    {
        try
        {
            KbConfig.Load();
            int bags = KbConfig.Apply();
            Log.Out($"{Tag} Applied bag times ({bags} already loaded).");
        }
        catch (Exception e)
        {
            // Never let a game update break server start-up; the XML times still apply.
            Log.Warning($"{Tag} Could not apply bag times at start-up: {e.Message}");
        }
    }
}

public static class KbConfig
{
    public const int PermanentSeconds = 100000000;
    public const int MaxSeconds = int.MaxValue / 20; // 107374182; above this, seconds*20 overflows

    public static string ModPath;
    public static int DeathBackpack = PermanentSeconds;
    public static int DroppedItems = 86400;
    public static int VehicleBag = 86400;
    public static int ZombieLoot = 86400;

    static string SettingsPath => Path.Combine(ModPath, "settings.txt");
    static string PasswordPath => Path.Combine(ModPath, "password.txt");
    static string XmlPath => Path.Combine(ModPath, "Config", "entityclasses.xml");

    public static void Load()
    {
        DeathBackpack = PermanentSeconds;
        DroppedItems = 86400;
        VehicleBag = 86400;
        ZombieLoot = 86400;
        try
        {
            if (!File.Exists(SettingsPath))
            {
                WriteSettings();
                return;
            }
            foreach (var raw in File.ReadAllLines(SettingsPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (!TryParseDuration(value, out int seconds, out bool clamped))
                {
                    Log.Warning($"{AzraelKeepBackpacks.Tag} Ignoring {key}={value} in settings.txt.");
                    continue;
                }
                if (clamped)
                    Log.Warning($"{AzraelKeepBackpacks.Tag} {key} was too large and was capped at {MaxSeconds} seconds.");
                if (key.Equals("DeathBackpack", StringComparison.OrdinalIgnoreCase)) DeathBackpack = seconds;
                else if (key.Equals("DroppedItems", StringComparison.OrdinalIgnoreCase)) DroppedItems = seconds;
                else if (key.Equals("VehicleBag", StringComparison.OrdinalIgnoreCase)) VehicleBag = seconds;
                else if (key.Equals("ZombieLoot", StringComparison.OrdinalIgnoreCase)) ZombieLoot = seconds;
                else Log.Warning($"{AzraelKeepBackpacks.Tag} Unknown setting '{key}'.");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelKeepBackpacks.Tag} Could not read settings.txt: {e.Message}");
        }
    }

    public static void WriteXml()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(XmlPath));
            File.WriteAllText(XmlPath,
                "<configs>\r\n" +
                "\t<!-- Written from settings.txt. Edit settings.txt, then run:  kbags reload -->\r\n" +
                $"\t<set xpath=\"/entity_classes/entity_class[@name='Backpack']/property[@name='TimeStayAfterDeath']/@value\">{DeathBackpack}</set>\r\n" +
                $"\t<set xpath=\"/entity_classes/entity_class[@name='DroppedLootContainer']/property[@name='TimeStayAfterDeath']/@value\">{DroppedItems}</set>\r\n" +
                $"\t<set xpath=\"/entity_classes/entity_class[@name='DroppedVehicleContainer']/property[@name='TimeStayAfterDeath']/@value\">{VehicleBag}</set>\r\n" +
                $"\t<set xpath=\"/entity_classes/entity_class[starts-with(@name,'EntityLootContainer')]/property[@name='TimeStayAfterDeath']/@value\">{ZombieLoot}</set>\r\n" +
                "</configs>\r\n");
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelKeepBackpacks.Tag} Could not write entityclasses.xml: {e.Message}");
        }
    }

    // Push the times into the live entity classes, then onto bags that are already loaded.
    // Bags in unloaded areas pick up the class value the next time that area loads.
    public static int Apply()
    {
        SetClass("Backpack", DeathBackpack);
        SetClass("DroppedLootContainer", DroppedItems);
        SetClass("DroppedVehicleContainer", VehicleBag);
        if (EntityClass.list != null)
        {
            foreach (var pair in EntityClass.list.Dict)
            {
                var name = pair.Value.entityClassName;
                if (name != null && name.StartsWith("EntityLootContainer", StringComparison.Ordinal))
                    pair.Value.Properties.SetValue(EntityClass.PropTimeStayAfterDeath, ZombieLoot.ToString(CultureInfo.InvariantCulture));
            }
        }
        return ApplyToLoaded();
    }

    public static bool ReloadClasses()
    {
        try
        {
            WorldStaticData.Reset("entityclasses");
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelKeepBackpacks.Tag} Entity class reload failed: {e.Message}");
            return false;
        }
    }

    public static bool HasPassword()
    {
        try { return File.Exists(PasswordPath) && File.ReadAllText(PasswordPath).Trim().Length > 0; }
        catch { return false; }
    }

    public static void SetPassword(string password)
    {
        File.WriteAllText(PasswordPath, password.Trim() + "\n");
    }

    public static bool PasswordMatches(string password)
    {
        try
        {
            if (!File.Exists(PasswordPath)) return false;
            string saved = File.ReadAllText(PasswordPath).Trim();
            return saved.Length > 0 && string.Equals(saved, password ?? "", StringComparison.Ordinal);
        }
        catch (Exception e)
        {
            Log.Warning($"{AzraelKeepBackpacks.Tag} Could not read password.txt: {e.Message}");
            return false;
        }
    }

    public static string Format(int seconds)
    {
        if (seconds == PermanentSeconds) return $"permanent ({seconds}s)";
        if (seconds == 0) return "0 (vanishes immediately)";
        if (seconds % 3600 == 0) return $"{seconds / 3600}h ({seconds}s)";
        if (seconds % 60 == 0) return $"{seconds / 60}m ({seconds}s)";
        return $"{seconds}s";
    }

    static void WriteSettings()
    {
        File.WriteAllText(SettingsPath,
            "DeathBackpack=permanent\r\nDroppedItems=24h\r\nVehicleBag=24h\r\nZombieLoot=24h\r\n");
    }

    static void SetClass(string name, int seconds)
    {
        if (EntityClass.list == null) return;
        if (!EntityClass.list.TryGetValue(EntityClass.FromString(name), out var ec) || ec == null) return;
        ec.Properties.SetValue(EntityClass.PropTimeStayAfterDeath, seconds.ToString(CultureInfo.InvariantCulture));
    }

    static int ApplyToLoaded()
    {
        var world = GameManager.Instance?.World;
        if (world?.Entities?.list == null) return 0;
        int n = 0;
        var list = world.Entities.list;
        for (int i = 0; i < list.Count; i++)
        {
            var entity = list[i];
            if (entity == null) continue;
            // Game 3.3: death backpacks and loot bags share EntityContainerAbs.timeStayAfterDeath
            // (3.2's EntityBackpack.ticksStayAfterDeath is gone).
            if (!(entity is EntityContainerAbs container)) continue;
            string name = EntityClass.GetEntityClassName(entity.entityClass);
            int seconds = -1;
            if (entity is EntityBackpack && name == "Backpack") seconds = DeathBackpack;
            else if (name == "DroppedLootContainer") seconds = DroppedItems;
            else if (name == "DroppedVehicleContainer") seconds = VehicleBag;
            else if (name != null && name.StartsWith("EntityLootContainer", StringComparison.Ordinal)) seconds = ZombieLoot;
            if (seconds >= 0)
            {
                container.timeStayAfterDeath = seconds * 20;
                n++;
            }
        }
        return n;
    }

    // Removes dropped-item bags and loose items in loaded areas.
    // Leaves death backpacks, zombie loot bags, and vehicle storage bags.
    public static void ClearDroppedItems(out int bags, out int loose)
    {
        bags = 0;
        loose = 0;
        var world = GameManager.Instance?.World;
        if (world?.Entities?.list == null) return;
        var list = world.Entities.list;
        var copy = new List<Entity>(list.Count);
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) copy.Add(list[i]);

        for (int i = 0; i < copy.Count; i++)
        {
            var entity = copy[i];
            string name = EntityClass.GetEntityClassName(entity.entityClass);
            if (name == "DroppedLootContainer" && entity is EntityLootContainer container)
            {
                container.bRemoved = true;
                container.MarkToUnload();
                bags++;
            }
            else if (name == "item" && entity.GetType() == typeof(EntityItem))
            {
                entity.lifetime = 0f;
                entity.SetDead();
                entity.MarkToUnload();
                loose++;
            }
        }
    }

    static bool TryParseDuration(string text, out int seconds, out bool clamped)
    {
        seconds = 0;
        clamped = false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().ToLowerInvariant();
        if (text == "permanent" || text == "forever")
        {
            seconds = PermanentSeconds;
            return true;
        }
        int factor = 1;
        if (text.EndsWith("h", StringComparison.Ordinal)) { factor = 3600; text = text.Substring(0, text.Length - 1).Trim(); }
        else if (text.EndsWith("m", StringComparison.Ordinal)) { factor = 60; text = text.Substring(0, text.Length - 1).Trim(); }
        else if (text.EndsWith("s", StringComparison.Ordinal)) { text = text.Substring(0, text.Length - 1).Trim(); }
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) || n < 0)
            return false;
        long total = n * factor;
        if (total > MaxSeconds) { total = MaxSeconds; clamped = true; }
        seconds = (int)total;
        return true;
    }
}

// Admin / server console. Permission 0.
public class ConsoleCmdKbags : ConsoleCmdAbstract
{
    public override int DefaultPermissionLevel => 0;
    public override string[] getCommands() => new[] { "kbags" };
    public override string getDescription() => "Keep Backpacks: show times, reload them, set the clear password";
    public override string getHelp() =>
        "kbags                     - show the current times\n" +
        "kbags reload              - re-read settings.txt and apply it without a restart\n" +
        "kbags password <word>     - set the password required by kbclear\n" +
        "Players already connected keep the old times until they rejoin.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        string arg = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (arg == "reload")
        {
            KbConfig.Load();
            KbConfig.WriteXml();
            bool reset = KbConfig.ReloadClasses();
            int bags = KbConfig.Apply();
            con.Output("Keep Backpacks reloaded" + (reset ? "" : " (entity class reload failed, applied in memory)") + $".");
            con.Output($"Death backpack {KbConfig.Format(KbConfig.DeathBackpack)}, dropped items {KbConfig.Format(KbConfig.DroppedItems)}, vehicle bag {KbConfig.Format(KbConfig.VehicleBag)}, zombie loot {KbConfig.Format(KbConfig.ZombieLoot)}.");
            con.Output($"Updated {bags} loaded bags. Players already connected keep the old times until they rejoin. Unloaded areas pick this up when someone goes there.");
            return;
        }
        if (arg == "password")
        {
            if (_params.Count < 2 || string.IsNullOrWhiteSpace(_params[1]))
            {
                con.Output("Usage: kbags password <word>");
                return;
            }
            string password = string.Join(" ", _params.GetRange(1, _params.Count - 1)).Trim();
            KbConfig.SetPassword(password);
            con.Output("Clear password set. kbclear needs that password. It is saved on the server in password.txt.");
            return;
        }
        if (arg.Length > 0)
        {
            con.Output("Usage: kbags | kbags reload | kbags password <word>");
            return;
        }
        con.Output($"Death backpack: {KbConfig.Format(KbConfig.DeathBackpack)}");
        con.Output($"Dropped items: {KbConfig.Format(KbConfig.DroppedItems)}");
        con.Output($"Vehicle bag: {KbConfig.Format(KbConfig.VehicleBag)}");
        con.Output($"Zombie loot: {KbConfig.Format(KbConfig.ZombieLoot)}");
        con.Output(KbConfig.HasPassword() ? "Clear password: set.  kbclear <password>" : "Clear password: not set.  kbags password <word>");
    }
}

// Anyone can run it; the password is the lock. Does not remove death backpacks or zombie loot.
public class ConsoleCmdKbClear : ConsoleCmdAbstract
{
    public override int DefaultPermissionLevel => 1000;
    public override string[] getCommands() => new[] { "kbclear" };
    public override string getDescription() => "Remove dropped items in loaded areas. Needs the password from kbags password.";
    public override string getHelp() =>
        "kbclear <password>\n" +
        "Removes dropped-item bags and loose items on the ground in loaded areas.\n" +
        "Does not remove death backpacks, zombie loot bags, or vehicle storage bags.\n" +
        "Bags in unloaded areas stay until someone goes there.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        if (_params.Count < 1)
        {
            con.Output("Usage: kbclear <password>");
            return;
        }
        if (!KbConfig.HasPassword())
        {
            con.Output("No clear password set. On the server console: kbags password <word>");
            return;
        }
        string password = string.Join(" ", _params).Trim();
        if (!KbConfig.PasswordMatches(password))
        {
            con.Output("Wrong password.");
            return;
        }
        if (GameManager.Instance?.World == null)
        {
            con.Output("No world is running.");
            return;
        }
        KbConfig.ClearDroppedItems(out int bags, out int loose);
        con.Output($"Removed {bags} dropped-item bags and {loose} loose items in loaded areas. Death backpacks, zombie loot, and vehicle bags were left alone. Unloaded areas were not changed.");
        Log.Out($"{AzraelKeepBackpacks.Tag} Cleared {bags} dropped-item bags and {loose} loose items.");
    }
}
