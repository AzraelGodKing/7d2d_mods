using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Azrael's Speedometer: a HUD speed readout with MPH / KM/H toggle and hotkeys.
// Client-side UI. Settings are saved per player in %APPDATA%\7DaysToDie\AzraelSpeedometer.txt

public class AzraelSpeedometerMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        SpeedoSettings.Load();
        Log.Out($"[Speedometer] Loaded. Mode={SpeedoSettings.ModeName}, Unit={SpeedoSettings.UnitName}, " +
                $"Keys: show/hide={SpeedoSettings.KeyName(SpeedoSettings.KeyHide)}, units={SpeedoSettings.KeyName(SpeedoSettings.KeyUnit)}. " +
                "Type 'speedo' in the console (F1) for options.");
    }
}

public static class SpeedoSettings
{
    public enum ShowMode { Off, Vehicle, Always }

    // F2 and F5 have no default binding in V3.2, and the game forbids players from binding F-keys
    // in Options > Controls, so these can't clash with anyone's game controls.
    public const KeyCode DefaultKeyHide = KeyCode.F5;
    public const KeyCode DefaultKeyUnit = KeyCode.F2;

    public static ShowMode Mode = ShowMode.Vehicle;
    public static ShowMode LastShownMode = ShowMode.Vehicle; // what "show" returns to after hiding
    public static bool Kmh = false;
    public static KeyCode KeyHide = DefaultKeyHide;
    public static KeyCode KeyUnit = DefaultKeyUnit;
    public static int Version; // bumped on every change so the HUD refreshes immediately

    public static string ModeName => Mode.ToString().ToLowerInvariant();
    public static string UnitName => Kmh ? "kmh" : "mph";
    public static string UnitLabel => Kmh ? "KM/H" : "MPH";
    public static string KeyName(KeyCode k) => k == KeyCode.None ? "none" : k.ToString();

    static string FilePath
    {
        get
        {
            try { return Path.Combine(GameIO.GetUserGameDataDir(), "AzraelSpeedometer.txt"); }
            catch { return null; }
        }
    }

    public static bool TryParseKey(string s, out KeyCode key)
    {
        key = KeyCode.None;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.Equals("none", StringComparison.OrdinalIgnoreCase) || s.Equals("off", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Length == 1 && char.IsDigit(s[0])) s = "Alpha" + s; // "7" -> Alpha7
        return Enum.TryParse(s, true, out key) && Enum.IsDefined(typeof(KeyCode), key);
    }

    public static void Load()
    {
        var path = FilePath;
        try
        {
            if (path == null || !File.Exists(path)) return;
            foreach (var raw in File.ReadAllLines(path))
            {
                var kv = raw.Split('=');
                if (kv.Length != 2) continue;
                var k = kv[0].Trim().ToLowerInvariant();
                var v = kv[1].Trim();
                switch (k)
                {
                    case "mode":
                        var lv = v.ToLowerInvariant();
                        Mode = lv == "off" ? ShowMode.Off : lv == "always" ? ShowMode.Always : ShowMode.Vehicle;
                        break;
                    case "lastshown":
                        LastShownMode = v.Equals("always", StringComparison.OrdinalIgnoreCase) ? ShowMode.Always : ShowMode.Vehicle;
                        break;
                    case "unit": Kmh = v.Equals("kmh", StringComparison.OrdinalIgnoreCase); break;
                    case "keyhide": if (TryParseKey(v, out var kh)) KeyHide = kh; break;
                    case "keyunit": if (TryParseKey(v, out var ku)) KeyUnit = ku; break;
                }
            }
            if (Mode != ShowMode.Off) LastShownMode = Mode;
        }
        catch (Exception e) { Log.Warning("[Speedometer] Could not read settings: " + e.Message); }
    }

    public static void Changed()
    {
        if (Mode != ShowMode.Off) LastShownMode = Mode;
        Version++;
        var path = FilePath;
        if (path == null) return;
        try
        {
            File.WriteAllText(path,
                $"mode={ModeName}\nlastshown={LastShownMode.ToString().ToLowerInvariant()}\nunit={UnitName}\n" +
                $"keyhide={KeyName(KeyHide)}\nkeyunit={KeyName(KeyUnit)}\n");
        }
        catch (Exception e) { Log.Warning("[Speedometer] Could not save settings: " + e.Message); }
    }

    public static void ToggleUnit() { Kmh = !Kmh; Changed(); }

    public static void ToggleShown()
    {
        Mode = Mode == ShowMode.Off ? LastShownMode : ShowMode.Off;
        Changed();
    }

    public static string StatusText()
    {
        string modeText = Mode == ShowMode.Off ? "hidden"
                        : Mode == ShowMode.Always ? "always shown"
                        : "shown in vehicles";
        return $"Speedometer: {modeText}, units {UnitLabel}. Keys: show/hide = {KeyName(KeyHide)}, MPH/KM/H = {KeyName(KeyUnit)}.";
    }
}

// HUD controller. Referenced from XUi as controller="AzraelSpeedo, AzraelSpeedometer".
public class XUiC_AzraelSpeedo : XUiController
{
    const float SampleSeconds = 0.1f;
    const float MaxPlausibleMs = 150f; // anything faster is a teleport/respawn, not driving
    const float MsToMph = 2.2369363f;
    const float MsToKmh = 3.6f;

    EntityPlayerLocal player;
    Entity lastSource;
    Vector3 lastPos;
    bool hasLast;
    float accTime, accDist, speedMs;

    bool shownVisible;
    int shownValue = -1;
    int seenVersion = -1;

    public override void Update(float _dt)
    {
        base.Update(_dt);

        if (player == null && XUi.IsGameRunning())
            player = xui.playerUI.entityPlayer;
        if (player == null) return;

        var wm = xui.playerUI.windowManager;
        HandleHotkeys(wm);

        var vehicle = player.AttachedToEntity as EntityVehicle;
        Entity source = vehicle != null ? (Entity)vehicle : player;
        SampleSpeed(source, _dt);

        bool hudHidden = wm.IsFullHUDDisabled() || (!xui.DragAndDropWindow.InMenu && wm.IsHUDPartialHidden());

        bool visible = !hudHidden && !player.IsDead() &&
                       (SpeedoSettings.Mode == SpeedoSettings.ShowMode.Always ||
                        (SpeedoSettings.Mode == SpeedoSettings.ShowMode.Vehicle && vehicle != null));

        int value = Mathf.RoundToInt(speedMs * (SpeedoSettings.Kmh ? MsToKmh : MsToMph));

        if (visible != shownVisible || value != shownValue || seenVersion != SpeedoSettings.Version)
        {
            shownVisible = visible;
            shownValue = value;
            seenVersion = SpeedoSettings.Version;
            RefreshBindings();
        }
    }

    // Hotkeys only fire during normal play: not while typing (chat/console/text fields),
    // not with a menu or inventory open, not while paused.
    void HandleHotkeys(GUIWindowManager wm)
    {
        try
        {
            if (wm.IsInputActive() || wm.IsModalWindowOpen() || wm.IsCursorWindowOpen()) return;
            if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;

            if (SpeedoSettings.KeyHide != KeyCode.None && Input.GetKeyDown(SpeedoSettings.KeyHide))
            {
                SpeedoSettings.ToggleShown();
                Notify(SpeedoSettings.Mode == SpeedoSettings.ShowMode.Off ? "Speedometer hidden" : "Speedometer shown");
            }
            else if (SpeedoSettings.KeyUnit != KeyCode.None && Input.GetKeyDown(SpeedoSettings.KeyUnit))
            {
                SpeedoSettings.ToggleUnit();
                Notify("Speedometer: " + SpeedoSettings.UnitLabel);
            }
        }
        catch (Exception e)
        {
            Log.Warning("[Speedometer] Hotkey check failed: " + e.Message);
        }
    }

    void Notify(string text)
    {
        try { GameManager.ShowTooltip(player, text, _showImmediately: true); }
        catch { /* purely cosmetic */ }
    }

    // Speed from how far the player (or their vehicle) actually moved. Works the same for
    // drivers, passengers and on foot, and needs nothing from the server.
    void SampleSpeed(Entity source, float dt)
    {
        Vector3 pos = source.position;
        if (!hasLast || source != lastSource)
        {
            lastSource = source; lastPos = pos; hasLast = true;
            accTime = 0f; accDist = 0f; speedMs = 0f;
            return;
        }
        accDist += (pos - lastPos).magnitude;
        lastPos = pos;
        accTime += dt;
        if (accTime < SampleSeconds) return;

        float sample = accDist / accTime;
        accTime = 0f; accDist = 0f;
        if (sample > MaxPlausibleMs) return; // teleport: ignore this window
        speedMs = Mathf.Lerp(speedMs, sample, 0.5f);
        if (speedMs < 0.1f) speedMs = 0f;
    }

    public override void Pressed(int _mouseButton)
    {
        base.Pressed(_mouseButton);
        SpeedoSettings.ToggleUnit(); // click the speedometer (with the cursor out) to switch units
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "speedovisible": _value = shownVisible ? "true" : "false"; return true;
            case "speedovalue": _value = Math.Max(shownValue, 0).ToString(); return true;
            case "speedounit": _value = SpeedoSettings.UnitLabel; return true;
            default: return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}

// Console command (F1): speedo [on|off|always|mph|kmh|unit|key ...]
public class ConsoleCmdAzraelSpeedo : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient => true;
    public override bool AllowedInMainMenu => true;
    public override int DefaultPermissionLevel => 1000;

    public override string[] getCommands() => new[] { "speedo", "speedometer" };
    public override string getDescription() => "Speedometer: show/hide it, switch MPH / KM/H, change its hotkeys";
    public override string getHelp() =>
        "Usage:\n" +
        "  speedo                 - show current settings\n" +
        "  speedo off             - hide the speedometer\n" +
        "  speedo on              - show it while in a vehicle (default)\n" +
        "  speedo always          - show it on foot too\n" +
        "  speedo mph | kmh       - pick the unit\n" +
        "  speedo unit            - switch between MPH and KM/H\n" +
        "  speedo key hide <key>  - set the show/hide hotkey (default F5)\n" +
        "  speedo key unit <key>  - set the MPH/KM/H hotkey (default F2)\n" +
        "  speedo key hide none   - turn a hotkey off\n" +
        "  speedo key reset       - back to F5 / F2\n" +
        "Key names: F2, F5, Home, End, Insert, PageUp, K, 7, Keypad5, etc.\n" +
        "You can also click the speedometer (with your inventory open) to switch units.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        string arg = _params.Count > 0 ? _params[0].Trim().ToLowerInvariant() : "";
        switch (arg)
        {
            case "": break;
            case "off": case "hide": SpeedoSettings.Mode = SpeedoSettings.ShowMode.Off; SpeedoSettings.Changed(); break;
            case "on": case "show": case "vehicle": SpeedoSettings.Mode = SpeedoSettings.ShowMode.Vehicle; SpeedoSettings.Changed(); break;
            case "always": case "foot": SpeedoSettings.Mode = SpeedoSettings.ShowMode.Always; SpeedoSettings.Changed(); break;
            case "mph": SpeedoSettings.Kmh = false; SpeedoSettings.Changed(); break;
            case "kmh": case "km/h": case "kph": SpeedoSettings.Kmh = true; SpeedoSettings.Changed(); break;
            case "unit": case "units": case "toggle": SpeedoSettings.ToggleUnit(); break;
            case "key": case "keys": case "bind":
                if (!SetKey(_params, con)) return;
                break;
            default: con.Output("Unknown option '" + arg + "'.\n" + getHelp()); return;
        }
        con.Output(SpeedoSettings.StatusText());
    }

    static bool SetKey(List<string> p, SdtdConsole con)
    {
        string which = p.Count > 1 ? p[1].Trim().ToLowerInvariant() : "";
        if (which == "reset")
        {
            SpeedoSettings.KeyHide = SpeedoSettings.DefaultKeyHide;
            SpeedoSettings.KeyUnit = SpeedoSettings.DefaultKeyUnit;
            SpeedoSettings.Changed();
            return true;
        }
        if ((which != "hide" && which != "show" && which != "unit" && which != "units") || p.Count < 3)
        {
            con.Output("Usage: speedo key hide <key> | speedo key unit <key> | speedo key reset   (use 'none' to turn a key off)");
            return false;
        }
        if (!SpeedoSettings.TryParseKey(p[2], out var key))
        {
            con.Output($"'{p[2]}' isn't a key name I know. Examples: F2, F5, Home, End, Insert, PageUp, K, 7, Keypad5.");
            return false;
        }
        if (key == KeyCode.Escape || key == KeyCode.Mouse0 || key == KeyCode.Mouse1)
        {
            con.Output($"{key} can't be used for the speedometer.");
            return false;
        }
        bool isHide = which == "hide" || which == "show";
        var other = isHide ? SpeedoSettings.KeyUnit : SpeedoSettings.KeyHide;
        if (key != KeyCode.None && key == other)
        {
            con.Output($"{key} is already the other speedometer key. Pick a different one.");
            return false;
        }
        if (key >= KeyCode.F1 && key <= KeyCode.F12 && key != KeyCode.F2 && key != KeyCode.F5)
            con.Output($"Heads up: {key} is used by the game by default (console, debug, screenshots or HUD). It will do both.");
        if (isHide) SpeedoSettings.KeyHide = key; else SpeedoSettings.KeyUnit = key;
        SpeedoSettings.Changed();
        return true;
    }
}
