using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// Blood Moon Sound: plays a user-supplied clip when the blood moon horde starts (and/or as a warning).
// The sound is client-side only: it reads the world clock the client already has and needs nothing
// from the server. On a dedicated server the same mod posts blood moon updates to Discord instead
// (see BloodMoonDiscord.cs); players don't need the mod for that.

public class AzraelBloodMoonSoundMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        BmsSettings.ModPath = _modInstance.Path;
        if (GameManager.IsDedicatedServer)
        {
            Log.Out("[BloodMoonSound] Dedicated server: no sound here (it plays on players' PCs). Starting Discord posts.");
            var srv = new GameObject("AzraelBloodMoonDiscord");
            UnityEngine.Object.DontDestroyOnLoad(srv);
            BmdNotifier.Instance = srv.AddComponent<BmdNotifier>();
            return;
        }
        var go = new GameObject("AzraelBloodMoonSound");
        UnityEngine.Object.DontDestroyOnLoad(go);
        BmsPlayer.Instance = go.AddComponent<BmsPlayer>();
        new HarmonyLib.Harmony("azrael.bloodmoonsound").PatchAll(typeof(AzraelBloodMoonSoundMod).Assembly);
    }
}

// On a multiplayer server the game sends EVERY F1 command to the server, and the server
// answers "Unknown command" for commands it doesn't have. This makes "bmsound" run on
// the player's own PC instead, so the server doesn't need this mod.
[HarmonyLib.HarmonyPatch(typeof(GUIWindowConsole), nameof(GUIWindowConsole.EnterCommand))]
public static class BmsConsolePatch
{
    static bool Prefix(GUIWindowConsole __instance, string _command)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_command)) return true;
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsClient) return true; // single player / host: normal path already works
            var first = _command.Trim().Split(' ')[0].ToLowerInvariant();
            if (first != "bmsound") return true;

            __instance.scrollRect.verticalNormalizedPosition = 0f;
            __instance.internalAddLine(new GUIWindowConsole.ConsoleLine("> " + _command, string.Empty, LogType.Log));
            GUIWindowConsole.AddLines(SingletonMonoBehaviour<SdtdConsole>.Instance.ExecuteSync(_command, null));

            var hist = __instance.lastCommands;
            if (hist.Count == 0 || !hist[hist.Count - 1].Equals(_command)) { hist.Remove(_command); hist.Add(_command); }
            __instance.lastCommandsIdx = hist.Count;
            __instance.commandField.text = "";
            __instance.commandField.Select();
            __instance.commandField.ActivateInputField();
            return false;
        }
        catch (Exception e)
        {
            Log.Warning("[BloodMoonSound] console patch: " + e.Message);
            return true;
        }
    }
}

// The server's horde sound (see BmdNotifier.BroadcastHordeSound) arrives as "AzraelBloodMoon/<name>".
// Players WITHOUT this mod: the game drops the "folder" part of sound names and plays <name>.
// Players WITH this mod and their own clip: around horde start, skip it and play their own clip
// instead, so nobody hears two sounds. Any other time, or with no clip loaded, it plays normally.
[HarmonyLib.HarmonyPatch(typeof(NetPackageAudioPlayInHead), nameof(NetPackageAudioPlayInHead.ProcessPackage))]
public static class BmsHordeSoundPatch
{
    public const string Marker = "AzraelBloodMoon/";

    static bool Prefix(NetPackageAudioPlayInHead __instance, World _world)
    {
        try
        {
            var name = __instance.soundName;
            if (name == null || !name.StartsWith(Marker, StringComparison.OrdinalIgnoreCase)) return true;
            var p = BmsPlayer.Instance;
            if (p == null || p.Clip == null || !BmsSettings.PlaysAtHorde || _world == null) return true;
            if (!p.InHordeWindow(_world.worldTime)) return true;
            Log.Out($"[BloodMoonSound] Server horde sound '{name.Substring(Marker.Length)}' replaced by {p.ClipFile}.");
            p.TryPlayHorde(GameStats.GetInt(EnumGameStats.BloodMoonDay));
            return false;
        }
        catch (Exception e)
        {
            Log.Warning("[BloodMoonSound] horde sound patch: " + e.Message);
            return true;
        }
    }
}

public static class BmsSettings
{
    public enum Trigger { Horde, Warning, Both }
    public static string ModPath;
    public static Trigger PlayAt = Trigger.Horde;
    public static int WarningHour = 21;
    public static float Volume = 1f;

    // Server only: vanilla sound the server tells every player to play at horde start.
    public const string DefaultHordeSoundName = "alarm1_oneshot";
    public static bool HordeSound = true;
    public static string HordeSoundName = DefaultHordeSoundName;

    public static bool PlaysAtHorde => PlayAt == Trigger.Horde || PlayAt == Trigger.Both;

    public static void Load()
    {
        PlayAt = Trigger.Horde; WarningHour = 21; Volume = 1f;
        HordeSound = true; HordeSoundName = DefaultHordeSoundName;
        try
        {
            var path = Path.Combine(ModPath, "settings.txt");
            if (!File.Exists(path)) return;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var kv = line.Split('=');
                if (kv.Length != 2) continue;
                var k = kv[0].Trim().ToLowerInvariant(); var v = kv[1].Trim().ToLowerInvariant();
                if (k == "playat") PlayAt = v == "warning" ? Trigger.Warning : v == "both" ? Trigger.Both : Trigger.Horde;
                else if (k == "warninghour" && int.TryParse(v, out var h)) WarningHour = Mathf.Clamp(h, 0, 23);
                else if (k == "volume" && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vol)) Volume = Mathf.Clamp01(vol);
                else if (k == "hordesound") HordeSound = !(v == "off" || v == "false" || v == "no" || v == "0");
                else if (k == "hordesoundname" && v.Length > 0) HordeSoundName = Path.GetFileName(v);
            }
        }
        catch (Exception e) { Log.Warning("[BloodMoonSound] Could not read settings.txt: " + e.Message); }
    }
}

public class BmsPlayer : MonoBehaviour
{
    public static BmsPlayer Instance;
    public AudioClip Clip;
    public string ClipFile;
    public string LoadError;
    AudioSource source;

    float timer;
    bool? lastBloodMoon;
    int lastDay = -1, lastHour = -1, lastHordeKey = -1, lastWarnDay = -1;
    float lastHordeEdgeAt = -999f; // realtime when this PC saw the blood moon begin

    // How close (in game time) to the horde start the server's sound may be replaced.
    // 1000 world-time units = 1 game hour, so 250 = 15 game minutes either side.
    const ulong WindowWorldTime = 250;
    const float WindowRealSeconds = 30f;

    // Plays the custom clip once per blood moon. Keyed by the blood moon's day (not today's date),
    // so the local trigger and the server's sound can never both play it.
    public void TryPlayHorde(int bloodMoonDay)
    {
        if (lastHordeKey == bloodMoonDay) return;
        lastHordeKey = bloodMoonDay;
        Play();
    }

    // True only right around the start of the blood moon horde.
    public bool InHordeWindow(ulong t)
    {
        int bmDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
        if (bmDay <= 0) return false;
        var duskDawn = GameUtils.CalcDuskDawnHours(GameStats.GetInt(EnumGameStats.DayLightLength));
        ulong start = (ulong)(bmDay - 1) * 24000UL + (ulong)duskDawn.duskHour * 1000UL;
        ulong diff = t > start ? t - start : start - t;
        if (diff <= WindowWorldTime) return true;
        // Admin time skips can jump straight into the blood moon: allow it only if this PC
        // is seeing (or has just seen) the blood moon begin.
        return GameUtils.IsBloodMoonTime(t, duskDawn, bmDay)
            && (lastBloodMoon == false || Time.realtimeSinceStartup - lastHordeEdgeAt < WindowRealSeconds);
    }

    void Start()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D: same volume wherever you are
        Reload();
    }

    public void Reload()
    {
        BmsSettings.Load();
        StartCoroutine(LoadClip());
    }

    IEnumerator LoadClip()
    {
        Clip = null; ClipFile = null; LoadError = null;
        var candidates = new List<(string file, AudioType type)> {
            ("bloodmoon.ogg", AudioType.OGGVORBIS), ("bloodmoon.wav", AudioType.WAV), ("bloodmoon.mp3", AudioType.MPEG) };
        foreach (var (file, type) in candidates)
        {
            var full = Path.Combine(BmsSettings.ModPath, file);
            if (!File.Exists(full)) continue;
            var uri = new Uri(Path.GetFullPath(full)).AbsoluteUri;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    LoadError = $"{file}: {req.error}";
                    Log.Warning("[BloodMoonSound] Could not load " + LoadError);
                    continue;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.length <= 0f) { LoadError = file + ": empty or unsupported audio"; continue; }
                clip.name = file; Clip = clip; ClipFile = file;
                Log.Out($"[BloodMoonSound] Loaded {file} ({clip.length:0.0}s). Plays at: {Describe()}.");
                yield break;
            }
        }
        if (LoadError == null) LoadError = "no bloodmoon.ogg / .wav / .mp3 found in " + BmsSettings.ModPath;
        Log.Warning("[BloodMoonSound] " + LoadError);
    }

    public static string Describe()
    {
        switch (BmsSettings.PlayAt)
        {
            case BmsSettings.Trigger.Warning: return $"{BmsSettings.WarningHour:00}:00 on blood moon day";
            case BmsSettings.Trigger.Both: return $"{BmsSettings.WarningHour:00}:00 on blood moon day and when the horde starts";
            default: return "when the blood moon horde starts";
        }
    }

    public void Play()
    {
        if (Clip == null) { Log.Warning("[BloodMoonSound] Nothing to play: " + LoadError); return; }
        source.PlayOneShot(Clip, BmsSettings.Volume);
    }

    void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < 0.5f) return;
        timer = 0f;
        try { Tick(); }
        catch (Exception e) { Log.Warning("[BloodMoonSound] " + e.Message); }
    }

    void Tick()
    {
        var world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) { lastBloodMoon = null; return; } // main menu: reset so joining never plays instantly

        ulong t = world.worldTime;
        int bmDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
        var duskDawn = GameUtils.CalcDuskDawnHours(GameStats.GetInt(EnumGameStats.DayLightLength));
        bool bloodMoon = GameUtils.IsBloodMoonTime(t, duskDawn, bmDay);
        int day = GameUtils.WorldTimeToDays(t);
        int hour = GameUtils.WorldTimeToHours(t);

        if (lastBloodMoon == null) // first tick in this world: remember state, don't play (joined mid-event)
        {
            lastBloodMoon = bloodMoon; lastDay = day; lastHour = hour;
            return;
        }

        var mode = BmsSettings.PlayAt;
        if (bloodMoon && lastBloodMoon == false)
        {
            lastHordeEdgeAt = Time.realtimeSinceStartup;
            if (BmsSettings.PlaysAtHorde) TryPlayHorde(bmDay);
        }

        if ((mode == BmsSettings.Trigger.Warning || mode == BmsSettings.Trigger.Both)
            && day == bmDay && !bloodMoon && hour >= BmsSettings.WarningHour
            && !(lastDay == day && lastHour >= BmsSettings.WarningHour) && lastWarnDay != day)
        {
            lastWarnDay = day;
            Play();
        }

        lastBloodMoon = bloodMoon; lastDay = day; lastHour = hour;
    }
}

// F1 console: bmsound [test|reload]
public class ConsoleCmdAzraelBloodMoonSound : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient => true;
    public override bool AllowedInMainMenu => true;
    public override int DefaultPermissionLevel => 1000;
    public override string[] getCommands() => new[] { "bmsound" };
    public override string getDescription() => "Blood Moon Sound: status, test, reload";
    public override string getHelp() => "bmsound          - show status\nbmsound test     - play the sound now\nbmsound reload   - re-read settings.txt and the sound file";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        var p = BmsPlayer.Instance;
        if (p == null) { con.Output("Blood Moon Sound isn't running here (dedicated server?)."); return; }
        string arg = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (arg == "test") { p.Play(); con.Output(p.Clip != null ? "Playing " + p.ClipFile : "No sound loaded: " + p.LoadError); return; }
        if (arg == "reload") { p.Reload(); con.Output("Reloading settings and sound..."); return; }
        con.Output($"Blood Moon Sound: {(p.Clip != null ? p.ClipFile + $" ({p.Clip.length:0.0}s)" : "NO SOUND - " + p.LoadError)}");
        con.Output($"Plays {BmsPlayer.Describe()}, volume {BmsSettings.Volume:0.##}. Next blood moon: day {GameStats.GetInt(EnumGameStats.BloodMoonDay)}.");
    }
}
