using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class BmdReminder
{
    public int DaysBefore;   // 0 = on blood moon day, 1 = the day before, ...
    public int Hour;         // in-game hour, 0-23
    public string Message;
    public string Describe() =>
        (DaysBefore == 0 ? "blood moon day" : DaysBefore == 1 ? "1 day before" : $"{DaysBefore} days before") + $" at {Hour:00}:00";
}

// How long until the horde (or until dawn), in game time and in real time.
public static class BmdTime
{
    // 1000 world time = 1 in-game hour, 24000 = 1 in-game day.
    public static (int duskHour, int dawnHour) DuskDawn() =>
        GameUtils.CalcDuskDawnHours(GameStats.GetInt(EnumGameStats.DayLightLength));

    // World time the horde of blood moon day bmDay starts (dusk) and ends (dawn the next morning).
    public static ulong HordeStart(int bmDay) => GameUtils.DayTimeToWorldTime(bmDay, DuskDawn().duskHour, 0);
    public static ulong HordeEnd(int bmDay) => GameUtils.DayTimeToWorldTime(bmDay + 1, DuskDawn().dawnHour, 0);

    public static ulong Now()
    {
        var world = GameManager.Instance != null ? GameManager.Instance.World : null;
        return world != null ? world.worldTime : 0UL;
    }

    public static ulong Until(ulong target, ulong now) => target > now ? target - now : 0UL;

    // Whole in-game hours, rounded to the nearest hour.
    public static int Hours(ulong span) => (int)((span + 500UL) / 1000UL);

    // "1 day 6 hours", "6 hours", "1 hour 30 minutes", "45 minutes".
    // Two hours or more is rounded to the hour; less than that to 5 minutes.
    public static string Game(ulong span)
    {
        long minutes = (long)Math.Round(span * 60.0 / 1000.0);
        if (minutes < 1) return "moments";
        if (minutes >= 120)
        {
            long h = (minutes + 30) / 60;
            long d = h / 24; h %= 24;
            if (d == 0) return Plural(h, "hour");
            return h == 0 ? Plural(d, "day") : Plural(d, "day") + " " + Plural(h, "hour");
        }
        long m5 = Math.Max(5, (minutes + 2) / 5 * 5);
        long hh = m5 / 60, mm = m5 % 60;
        if (hh == 0) return Plural(mm, "minute");
        return mm == 0 ? Plural(hh, "hour") : Plural(hh, "hour") + " " + Plural(mm, "minute");
    }

    // Real time for the same span, from the server's day length: "1h 15m", "25m", "under a minute".
    public static string Real(ulong span)
    {
        int dayMinutes = GamePrefs.GetInt(EnumGamePrefs.DayNightLength);
        if (dayMinutes <= 0) return "?";
        long secs = (long)Math.Round(span / 24000.0 * dayMinutes * 60.0);
        if (secs < 60) return "under a minute";
        long mins = (secs + 30) / 60;
        if (mins < 60) return mins + "m";
        return mins % 60 == 0 ? (mins / 60) + "h" : $"{mins / 60}h {mins % 60}m";
    }

    public static string Clock(int hour) => $"{hour:00}:00";

    static string Plural(long n, string word) => n + " " + word + (n == 1 ? "" : "s");
}

public static class BmdSettings
{
    public const string FileName = "discord.txt";

    public static string Webhook;
    public static string BotName;
    public static bool Mentions;
    public static bool SkipEmpty;

    public static readonly List<BmdReminder> Reminders = new List<BmdReminder>();

    public static bool Countdown; public static int CountdownHour; public static int CountdownFromDays;
    public static bool Start;
    public static int UpdateEveryHours;
    public static bool End;

    public static string MsgCountdown, MsgStart, MsgUpdate, MsgEnd;

    // Horde recap added to the dawn post.
    public static bool Recap;
    // Server events.
    public static bool ServerStart, ServerStop, PlayerJoin, PlayerLeave;
    public static string MsgServerStart, MsgServerStop, MsgJoin, MsgLeave;
    // Coloured Discord embeds instead of plain text.
    public static bool Embeds;
    // Also show these messages in the game's chat, for every player.
    public static bool InGameChat;
    public static readonly HashSet<BmdKind> ChatKinds = new HashSet<BmdKind>();
    public static bool AnyOutput => Enabled || InGameChat;

    public static readonly List<string> Problems = new List<string>(); // bad lines, shown by 'bmdiscord'

    public static string Status; // why posting is off, or null when it's on
    public static bool Enabled => Status == null;
    public static string FilePath => Path.Combine(BmsSettings.ModPath ?? "", FileName);

    // The first line of a message is its headline: the embed title in Discord, the bright line in game chat.
    public const string DefaultMsgDayBefore = "⚠️ **Blood Moon tomorrow** · Day {day}\nHorde in **{timeleft}** at {starts} (about {realtime} real time). Get your base ready!";
    public const string DefaultMsgWarning   = "\U0001F534 **Blood Moon tonight** · Day {day}\nHorde in **{timeleft}** at {starts} (about {realtime} real time). Get inside before dark!";
    public const string DefaultMsgDaysOut   = "⚠️ **Blood Moon in {timeleft}** · Day {day}\nHorde at {starts} (about {realtime} real time). Start stocking up!";
    public const string DefaultMsgCountdown = "⏳ **Blood Moon in {timeleft}** · Day {day}\nHorde at {starts} (about {realtime} real time).";
    public const string DefaultMsgStart     = "\U0001F315 **The Blood Moon has risen!** · Day {day}\nDawn in {timeleft} at {dawn}. Online: {players}";
    public const string DefaultMsgUpdate    = "\U0001FA78 **The Blood Moon rages on** · Day {day}\nDawn in {timeleft} at {dawn}. Still standing: {players}";
    public const string DefaultMsgEnd       = "\U0001F305 **Dawn! Blood Moon survived** · Day {day}\nOnline: {players}";

    // The 1.3.0 wording. A discord.txt still using it word for word gets the new wording,
    // so servers that copied the old example file pick up the hours left without editing it.
    static readonly Dictionary<string, string> OldDefaults = new Dictionary<string, string>
    {
        { "⚠️ Blood Moon **tomorrow night** (day {day}). Get your base ready!", DefaultMsgDayBefore },
        { "\U0001F534 Blood Moon **tonight** (day {day}). The horde comes at dusk.", DefaultMsgWarning },
        { "⚠️ Blood Moon in **{days} days** (day {day}).", DefaultMsgDaysOut },
        { "⏳ **{days} days** until the Blood Moon (day {day}).", DefaultMsgCountdown },
        { "\U0001F315 The Blood Moon has risen! Horde incoming. Online: {players}", DefaultMsgStart },
        { "\U0001FA78 The Blood Moon rages on. Still standing: {players}", DefaultMsgUpdate },
        { "\U0001F305 Dawn! Blood Moon day {day} survived. Online: {players}", DefaultMsgEnd },
    };
    public static int Upgraded; // how many old-wording messages were swapped on the last load

    static string Upgrade(string msg)
    {
        if (msg != null && OldDefaults.TryGetValue(msg, out var now)) { Upgraded++; return now; }
        return msg;
    }

    static void Defaults()
    {
        Webhook = null; BotName = "Blood Moon"; Mentions = false; SkipEmpty = true;
        Reminders.Clear(); Problems.Clear(); Upgraded = 0;
        Countdown = false; CountdownHour = 8; CountdownFromDays = 7;
        Start = true; UpdateEveryHours = 0; End = true;
        MsgCountdown = DefaultMsgCountdown;
        MsgStart     = DefaultMsgStart;
        MsgUpdate    = DefaultMsgUpdate;
        MsgEnd       = DefaultMsgEnd;
        Recap = true;
        ServerStart = false; ServerStop = false; PlayerJoin = false; PlayerLeave = false;
        MsgServerStart = "\U0001F7E2 The server is up.";
        MsgServerStop  = "\U0001F534 The server is shutting down.";
        MsgJoin        = "\u27A1\uFE0F **{player}** joined. Online: {count}";
        MsgLeave       = "\u2B05\uFE0F **{player}** left. Online: {count}";
        Embeds = true;
        InGameChat = true;
        ChatKinds.Clear();
        ChatKinds.UnionWith(new[] { BmdKind.Reminder, BmdKind.Countdown, BmdKind.Start, BmdKind.End });
    }

    public static void Load()
    {
        Defaults();
        Status = null;
        bool sawReminder = false;
        try
        {
            // No discord.txt means nothing was set up: no Discord posts and no game chat messages.
            if (!File.Exists(FilePath)) { Status = $"no {FileName} (copy discord.example.txt to {FileName} and paste the webhook URL)"; InGameChat = false; return; }
            int lineNo = 0;
            foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { Problems.Add($"line {lineNo}: no '=' in \"{Short(line)}\""); continue; }
                var k = line.Substring(0, eq).Trim().ToLowerInvariant();
                var v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "webhook": Webhook = v; break;
                    case "botname": if (v.Length > 0) BotName = v; break;
                    case "mentions": Mentions = Bool(v, Mentions); break;
                    case "skipemptyserver": SkipEmpty = Bool(v, SkipEmpty); break;

                    case "reminder":
                        sawReminder = true;
                        if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) break;
                        var r = ParseReminder(v, out var why);
                        if (r != null) Reminders.Add(r); else Problems.Add($"line {lineNo}: {why}");
                        break;

                    case "countdown": Countdown = Bool(v, Countdown); break;
                    case "countdownhour": CountdownHour = Hour(v, CountdownHour); break;
                    case "countdownfromdays": if (int.TryParse(v, out var cd)) CountdownFromDays = Mathf.Clamp(cd, 1, 365); break;
                    case "start": Start = Bool(v, Start); break;
                    case "updateeveryhours": if (int.TryParse(v, out var u)) UpdateEveryHours = Mathf.Clamp(u, 0, 24); break;
                    case "end": End = Bool(v, End); break;

                    case "msgcountdown": MsgCountdown = Msg(v, MsgCountdown); break;
                    case "msgstart": MsgStart = Msg(v, MsgStart); break;
                    case "msgupdate": MsgUpdate = Msg(v, MsgUpdate); break;
                    case "msgend": MsgEnd = Msg(v, MsgEnd); break;

                    case "recap": Recap = Bool(v, Recap); break;
                    case "serverstart": ServerStart = Bool(v, ServerStart); break;
                    case "serverstop": ServerStop = Bool(v, ServerStop); break;
                    case "playerjoin": PlayerJoin = Bool(v, PlayerJoin); break;
                    case "playerleave": PlayerLeave = Bool(v, PlayerLeave); break;
                    case "msgserverstart": MsgServerStart = Msg(v, MsgServerStart); break;
                    case "msgserverstop": MsgServerStop = Msg(v, MsgServerStop); break;
                    case "msgjoin": MsgJoin = Msg(v, MsgJoin); break;
                    case "msgleave": MsgLeave = Msg(v, MsgLeave); break;
                    case "embeds": Embeds = Bool(v, Embeds); break;
                    case "ingamechat": InGameChat = Bool(v, InGameChat); break;
                    case "ingamechatfor":
                        ChatKinds.Clear();
                        foreach (var w in v.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var kw = w.Trim().ToLowerInvariant();
                            if (kw == "reminders") kw = "reminder";
                            if (kw == "all") { ChatKinds.UnionWith((BmdKind[])Enum.GetValues(typeof(BmdKind))); ChatKinds.Remove(BmdKind.Test); continue; }
                            if (Enum.TryParse<BmdKind>(kw, true, out var kind) && kind != BmdKind.Test) ChatKinds.Add(kind);
                            else Problems.Add($"line {lineNo}: InGameChatFor doesn't know '{w}' (use reminder, countdown, start, update, end, serverstart, serverstop, join, leave or all)");
                        }
                        break;

                    default: Problems.Add($"line {lineNo}: unknown setting '{line.Substring(0, eq).Trim()}'"); break;
                }
            }
            // A discord.txt with no Reminder lines at all gets the two standard ones.
            // To have none, write:  Reminder=none
            if (!sawReminder)
            {
                Reminders.Add(new BmdReminder { DaysBefore = 1, Hour = 12, Message = DefaultMsgDayBefore });
                Reminders.Add(new BmdReminder { DaysBefore = 0, Hour = 18, Message = DefaultMsgWarning });
            }
            Reminders.Sort((a, b) => a.DaysBefore != b.DaysBefore ? b.DaysBefore.CompareTo(a.DaysBefore) : a.Hour.CompareTo(b.Hour));

            if (string.IsNullOrEmpty(Webhook)) Status = $"Webhook= is empty in {FileName}";
            else if (!Webhook.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || Webhook.IndexOf("/api/webhooks/", StringComparison.OrdinalIgnoreCase) < 0)
                Status = $"Webhook= in {FileName} doesn't look like a Discord webhook URL (https://discord.com/api/webhooks/...)";
        }
        catch (Exception e) { Status = $"could not read {FileName}: {e.Message}"; }
    }

    // "3 12 | message"  (days before, hour, message). The message is optional.
    static BmdReminder ParseReminder(string v, out string why)
    {
        why = null;
        string when = v, msg = null;
        int bar = v.IndexOf('|');
        if (bar >= 0) { when = v.Substring(0, bar).Trim(); msg = v.Substring(bar + 1).Trim(); }
        var parts = when.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var days) || !int.TryParse(parts[1], out var hour))
        {
            why = $"Reminder needs '<days before> <hour> | <message>', got \"{Short(v)}\"";
            return null;
        }
        if (days < 0 || days > 365) { why = $"Reminder days before must be 0-365, got {days}"; return null; }
        if (hour < 0 || hour > 23) { why = $"Reminder hour must be 0-23, got {hour}"; return null; }
        if (string.IsNullOrEmpty(msg))
            msg = days == 0 ? DefaultMsgWarning : days == 1 ? DefaultMsgDayBefore : DefaultMsgDaysOut;
        return new BmdReminder { DaysBefore = days, Hour = hour, Message = Upgrade(msg.Replace("\\n", "\n")) };
    }

    static string Short(string s) => s.Length > 60 ? s.Substring(0, 57) + "..." : s;

    static bool Bool(string v, bool fallback)
    {
        v = v.ToLowerInvariant();
        if (v == "true" || v == "yes" || v == "on" || v == "1") return true;
        if (v == "false" || v == "no" || v == "off" || v == "0") return false;
        return fallback;
    }

    static int Hour(string v, int fallback) => int.TryParse(v, out var h) ? Mathf.Clamp(h, 0, 23) : fallback;

    // Empty keeps the default; a literal \n becomes a line break.
    static string Msg(string v, string fallback) => v.Length == 0 ? fallback : Upgrade(v.Replace("\\n", "\n"));

    // For logs and the console: never print the token part of the URL.
    public static string MaskedWebhook()
    {
        if (string.IsNullOrEmpty(Webhook)) return "(none)";
        var m = Regex.Match(Webhook, @"/api/webhooks/(\d+)/");
        return m.Success ? $".../api/webhooks/{m.Groups[1].Value}/****" : "(set)";
    }
}

// What a message is about: picks its embed colour and whether it also goes to game chat.
public enum BmdKind { Reminder, Countdown, Start, Update, End, ServerStart, ServerStop, Join, Leave, Test }

// Who killed what and who died during the current horde, for the dawn recap.
public static class BmdRecap
{
    public static readonly Dictionary<string, int> Kills = new Dictionary<string, int>();
    public static readonly Dictionary<string, int> Deaths = new Dictionary<string, int>();
    public static int Total;
    public static bool Tracking;

    public static void Begin() { Kills.Clear(); Deaths.Clear(); Total = 0; Tracking = true; }

    public static void OnEntityKilled(ref ModEvents.SEntityKilledData data)
    {
        try
        {
            if (!Tracking || !(data.KilledEntitiy is EntityEnemy)) return;
            Total++;
            if (data.KillingEntity is EntityPlayer p)
            {
                var name = p.PlayerDisplayName ?? p.EntityName;
                Kills.TryGetValue(name, out var n);
                Kills[name] = n + 1;
            }
        }
        catch (Exception e) { Log.Warning("[BloodMoonDiscord] recap kill: " + e.Message); }
    }

    // Players die on their own PC, which tells the server through a "was killed" game message.
    public static ModEvents.EModEventResult OnGameMessage(ref ModEvents.SGameMessageData data)
    {
        try
        {
            if (Tracking && data.MessageType == EnumGameMessages.EntityWasKilled && !string.IsNullOrEmpty(data.MainName)
                && IsPlayerName(data.MainName))
            {
                Deaths.TryGetValue(data.MainName, out var n);
                Deaths[data.MainName] = n + 1;
            }
        }
        catch (Exception e) { Log.Warning("[BloodMoonDiscord] recap death: " + e.Message); }
        return ModEvents.EModEventResult.Continue;
    }

    static bool IsPlayerName(string name)
    {
        var players = GameManager.Instance?.World?.Players?.list;
        if (players == null) return false;
        foreach (var p in players) if (p != null && p.PlayerDisplayName == name) return true;
        return false;
    }

    static string List(Dictionary<string, int> d, int max)
    {
        var items = new List<KeyValuePair<string, int>>(d);
        items.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
        var parts = new List<string>();
        for (int i = 0; i < items.Count && i < max; i++) parts.Add($"{BmdNotifier.EscapeMarkdown(items[i].Key)} {items[i].Value}");
        if (items.Count > max) parts.Add($"and {items.Count - max} more");
        return string.Join(", ", parts);
    }

    public static string Text()
    {
        var sb = new StringBuilder();
        sb.Append($"**Horde recap:** {Total} zombie{(Total == 1 ? "" : "s")} killed");
        if (Kills.Count > 0) sb.Append("\nKills: ").Append(List(Kills, 10));
        sb.Append("\nDeaths: ").Append(Deaths.Count > 0 ? List(Deaths, 10) : "none \U0001F389");
        return sb.ToString();
    }
}

public class BmdNotifier : MonoBehaviour
{
    public static BmdNotifier Instance;

    readonly Queue<(string text, BmdKind kind)> outbox = new Queue<(string, BmdKind)>();
    bool sending;
    public string LastResult = "nothing sent yet";

    float timer;
    bool? lastBloodMoon;
    int lastDay = -1, lastHour = -1;
    int hordeDay = -1;                 // blood moon day of the horde in progress, -1 when none
    ulong nextUpdateAt;                // world time of the next "during" update
    int lastCountdownDay = -1;
    readonly Dictionary<string, int> reminderSentFor = new Dictionary<string, int>(); // reminder -> blood moon day

    void Start()
    {
        Reload();
        ModEvents.EntityKilled.RegisterHandler(BmdRecap.OnEntityKilled);
        ModEvents.GameMessage.RegisterHandler(BmdRecap.OnGameMessage);
        ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
        ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
        ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnPlayerSpawned);
        ModEvents.PlayerDisconnected.RegisterHandler(OnPlayerDisconnected);
    }

    void OnDestroy()
    {
        ModEvents.EntityKilled.UnregisterHandler(BmdRecap.OnEntityKilled);
        ModEvents.GameMessage.UnregisterHandler(BmdRecap.OnGameMessage);
        ModEvents.GameStartDone.UnregisterHandler(OnGameStartDone);
        ModEvents.WorldShuttingDown.UnregisterHandler(OnWorldShuttingDown);
        ModEvents.PlayerSpawnedInWorld.UnregisterHandler(OnPlayerSpawned);
        ModEvents.PlayerDisconnected.UnregisterHandler(OnPlayerDisconnected);
    }

    void OnGameStartDone(ref ModEvents.SGameStartDoneData _data)
    {
        if (BmdSettings.ServerStart) Post(BmdSettings.MsgServerStart, GameStats.GetInt(EnumGameStats.BloodMoonDay), 0, BmdKind.ServerStart);
    }

    // The server is about to stop, so a queued post would never go out: send this one right away.
    void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData _data)
    {
        if (!BmdSettings.ServerStop) return;
        var text = Fill(BmdSettings.MsgServerStop, GameStats.GetInt(EnumGameStats.BloodMoonDay), 0, OnlinePlayers(), null);
        if (BmdSettings.ChatKinds.Contains(BmdKind.ServerStop)) SendChat(text);
        if (BmdSettings.Enabled) PostNow(text, BmdKind.ServerStop);
    }

    void OnPlayerSpawned(ref ModEvents.SPlayerSpawnedInWorldData data)
    {
        if (!BmdSettings.PlayerJoin || (data.RespawnType != RespawnType.JoinMultiplayer && data.RespawnType != RespawnType.EnterMultiplayer)) return;
        var name = data.ClientInfo?.playerName;
        if (string.IsNullOrEmpty(name)) return;
        Post(BmdSettings.MsgJoin, GameStats.GetInt(EnumGameStats.BloodMoonDay), 0, BmdKind.Join, name);
    }

    void OnPlayerDisconnected(ref ModEvents.SPlayerDisconnectedData data)
    {
        if (!BmdSettings.PlayerLeave || data.GameShuttingDown) return;
        var name = data.ClientInfo?.playerName;
        if (string.IsNullOrEmpty(name)) return;
        Post(BmdSettings.MsgLeave, GameStats.GetInt(EnumGameStats.BloodMoonDay), 0, BmdKind.Leave, name);
    }

    // Tells every player's game to play one vanilla sound (no mod needed on their side).
    // The name carries a "folder" marker: vanilla games ignore it, modded games use it to
    // swap in the player's own clip (BmsHordeSoundPatch). Returns how many players were sent it.
    public static int BroadcastHordeSound()
    {
        try
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            int n = OnlinePlayers().Count;
            if (cm == null || n == 0) return 0;
            var pkg = NetPackageManager.GetPackage<NetPackageAudioPlayInHead>()
                .Setup(BmsHordeSoundPatch.Marker + BmsSettings.HordeSoundName, _isUnique: true);
            cm.SendPackage(pkg, _onlyClientsAttachedToAnEntity: true);
            Log.Out($"[BloodMoonSound] Horde sound '{BmsSettings.HordeSoundName}' sent to {n} player(s).");
            return n;
        }
        catch (Exception e)
        {
            Log.Warning("[BloodMoonSound] Could not send horde sound: " + e.Message);
            return 0;
        }
    }

    public void Reload()
    {
        BmsSettings.Load();
        Log.Out(BmsSettings.HordeSound
            ? $"[BloodMoonSound] Horde sound for all players: '{BmsSettings.HordeSoundName}'."
            : "[BloodMoonSound] Horde sound for all players: off.");
        BmdSettings.Load();
        foreach (var p in BmdSettings.Problems) Log.Warning($"[BloodMoonDiscord] {BmdSettings.FileName} {p}");
        if (BmdSettings.Upgraded > 0)
            Log.Out($"[BloodMoonDiscord] {BmdSettings.Upgraded} message(s) in {BmdSettings.FileName} still had the 1.3.0 wording; using the new wording with time left instead.");
        if (BmdSettings.Enabled)
            Log.Out($"[BloodMoonDiscord] Posting to {BmdSettings.MaskedWebhook()}: {Describe()}.");
        else
            Log.Out($"[BloodMoonDiscord] Discord posts off: {BmdSettings.Status}.");
    }

    public static List<string> Schedule()
    {
        var lines = new List<string>();
        for (int i = 0; i < BmdSettings.Reminders.Count; i++)
            lines.Add($"reminder {i + 1}: {BmdSettings.Reminders[i].Describe()}");
        if (BmdSettings.Countdown)
            lines.Add($"countdown: daily at {BmdSettings.CountdownHour:00}:00 from {BmdSettings.CountdownFromDays} days out");
        if (BmdSettings.Start) lines.Add("start: when the horde starts");
        if (BmdSettings.UpdateEveryHours > 0) lines.Add($"update: every {BmdSettings.UpdateEveryHours} in-game hour(s) during the horde");
        if (BmdSettings.End) lines.Add("end: at dawn after" + (BmdSettings.Recap ? ", with a horde recap (kills and deaths)" : ""));
        if (BmdSettings.ServerStart) lines.Add("server start");
        if (BmdSettings.ServerStop) lines.Add("server stop");
        if (BmdSettings.PlayerJoin) lines.Add("player joins");
        if (BmdSettings.PlayerLeave) lines.Add("player leaves");
        return lines;
    }

    public static string Describe()
    {
        var s = Schedule();
        var text = s.Count > 0 ? string.Join("; ", s) : "no messages turned on";
        if (BmdSettings.SkipEmpty) text += " (start/update/dawn skipped when nobody is online)";
        text += BmdSettings.Embeds ? ". Coloured embeds" : ". Plain text";
        if (BmdSettings.InGameChat) text += $". Also in game chat: {ChatKindsText()}";
        return text;
    }

    public static string ChatKindsText() =>
        BmdSettings.ChatKinds.Count == 0 ? "nothing" : string.Join(", ", new List<BmdKind>(BmdSettings.ChatKinds).ConvertAll(k => k.ToString().ToLowerInvariant()));

    void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < 1f) return;
        timer = 0f;
        try { Tick(); }
        catch (Exception e) { Log.Warning("[BloodMoonDiscord] " + e.Message); }
    }

    void Tick()
    {
        var world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) { lastBloodMoon = null; hordeDay = -1; BmdRecap.Tracking = false; return; }

        ulong t = world.worldTime;
        int bmDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
        var duskDawn = GameUtils.CalcDuskDawnHours(GameStats.GetInt(EnumGameStats.DayLightLength));
        bool bloodMoon = GameUtils.IsBloodMoonTime(t, duskDawn, bmDay);
        int day = GameUtils.WorldTimeToDays(t);
        int hour = GameUtils.WorldTimeToHours(t);

        // First tick after the world loads: remember where we are and post nothing,
        // so a server restart in the middle of a blood moon doesn't spam the channel.
        if (lastBloodMoon == null)
        {
            lastBloodMoon = bloodMoon; lastDay = day; lastHour = hour;
            return;
        }

        if (bloodMoon && lastBloodMoon == false)
        {
            // bmDay, not today: after midnight the horde still belongs to the previous day
            // (a time skip straight into 01:00 would otherwise post "start" and "dawn" together).
            hordeDay = bmDay;
            nextUpdateAt = t + (ulong)BmdSettings.UpdateEveryHours * 1000UL;
            if (BmsSettings.HordeSound) BroadcastHordeSound();
            BmdRecap.Begin();
            if (BmdSettings.Start && PlayersOkay()) Post(BmdSettings.MsgStart, bmDay, 0, BmdKind.Start);
        }

        // Updates while the horde is on (1000 world time = 1 in-game hour).
        if (hordeDay >= 0 && bloodMoon && BmdSettings.UpdateEveryHours > 0 && t >= nextUpdateAt)
        {
            ulong step = (ulong)BmdSettings.UpdateEveryHours * 1000UL;
            if (lastBloodMoon == true && PlayersOkay()) Post(BmdSettings.MsgUpdate, hordeDay, 0, BmdKind.Update);
            while (nextUpdateAt <= t) nextUpdateAt += step; // a time skip posts once, not once per missed hour
        }

        // End check uses the horde's own day, not the current BloodMoonDay stat,
        // because the game moves that stat on to the next blood moon around dawn.
        if (hordeDay >= 0 && !GameUtils.IsBloodMoonTime(t, duskDawn, hordeDay))
        {
            if (BmdSettings.End && PlayersOkay()) Post(BmdSettings.MsgEnd, hordeDay, 0, BmdKind.End);
            hordeDay = -1;
            BmdRecap.Tracking = false;
        }

        if (bmDay > 0 && !bloodMoon)
        {
            int daysLeft = bmDay - day;

            foreach (var r in BmdSettings.Reminders)
            {
                if (daysLeft != r.DaysBefore || !Crossed(day, hour, r.Hour)) continue;
                var key = $"{r.DaysBefore}/{r.Hour}/{r.Message}";
                if (reminderSentFor.TryGetValue(key, out var sent) && sent == bmDay) continue;
                reminderSentFor[key] = bmDay;
                Post(r.Message, bmDay, daysLeft, BmdKind.Reminder);
            }

            if (BmdSettings.Countdown && daysLeft >= 1 && daysLeft <= BmdSettings.CountdownFromDays
                && Crossed(day, hour, BmdSettings.CountdownHour) && lastCountdownDay != day)
            {
                lastCountdownDay = day;
                Post(BmdSettings.MsgCountdown, bmDay, daysLeft, BmdKind.Countdown);
            }
        }

        lastBloodMoon = bloodMoon; lastDay = day; lastHour = hour;
    }

    // True the first tick the clock is at/after targetHour today (also catches time skips past it).
    bool Crossed(int day, int hour, int targetHour) => hour >= targetHour && !(lastDay == day && lastHour >= targetHour);

    bool PlayersOkay() => !BmdSettings.SkipEmpty || OnlinePlayers().Count > 0;

    public static List<string> OnlinePlayers()
    {
        var names = new List<string>();
        try
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null) return names;
            foreach (var ci in cm.Clients.List)
                if (ci != null && !string.IsNullOrEmpty(ci.playerName)) names.Add(ci.playerName);
        }
        catch (Exception e) { Log.Warning("[BloodMoonDiscord] Could not list players: " + e.Message); }
        return names;
    }

    // "horde in 1 day 6 hours at 22:00 (about 1h 15m real time)", or how long until dawn during one.
    public static string TimeLeftText(int bmDay)
    {
        if (bmDay <= 0) return "no blood moons on this server";
        ulong now = BmdTime.Now(), start = BmdTime.HordeStart(bmDay);
        var dd = BmdTime.DuskDawn();
        if (now < start)
        {
            ulong span = start - now;
            return $"horde in {BmdTime.Game(span)} at {BmdTime.Clock(dd.duskHour)} (about {BmdTime.Real(span)} real time)";
        }
        ulong left = BmdTime.Until(BmdTime.HordeEnd(bmDay), now);
        return $"horde on now, dawn in {BmdTime.Game(left)} at {BmdTime.Clock(dd.dawnHour)} (about {BmdTime.Real(left)} real time)";
    }

    // Days until the blood moon right now (0 on the day itself), for {days} in test posts.
    public static int DaysUntil(int bmDay)
    {
        var world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return 0;
        return Math.Max(0, bmDay - GameUtils.WorldTimeToDays(world.worldTime));
    }

    // Fill in the placeholders. player is the one joining/leaving, for {player}.
    // at: the world time to count from (null = now); test posts pass the time the message would really go out.
    public static string Fill(string template, int day, int daysLeft, List<string> players, string player, ulong? at = null)
    {
        var text = template
            .Replace("{day}", day.ToString())
            .Replace("{days}", daysLeft.ToString());
        if (text.IndexOf('{') >= 0 && day > 0) text = FillTime(text, day, at ?? BmdTime.Now());
        text = text
            .Replace("{count}", players.Count.ToString())
            .Replace("{players}", players.Count > 0 ? string.Join(", ", players.ConvertAll(EscapeMarkdown)) : "nobody")
            .Replace("{player}", EscapeMarkdown(player ?? ""));
        if (text.Contains("{recap}")) text = text.Replace("{recap}", BmdRecap.Text());
        return text;
    }

    // {timeleft} {hours} {realtime}: until the horde starts, or until dawn once it has.
    static string FillTime(string text, int day, ulong now)
    {
        var dd = BmdTime.DuskDawn();
        ulong start = BmdTime.HordeStart(day);
        ulong span = BmdTime.Until(now < start ? start : BmdTime.HordeEnd(day), now);
        return text
            .Replace("{timeleft}", BmdTime.Game(span))
            .Replace("{hours}", BmdTime.Hours(span).ToString())
            .Replace("{realtime}", BmdTime.Real(span))
            .Replace("{starts}", BmdTime.Clock(dd.duskHour))
            .Replace("{dawn}", BmdTime.Clock(dd.dawnHour));
    }

    // Returns false (and says why) when nothing could go out.
    public bool Post(string template, int day, int daysLeft = 0, BmdKind kind = BmdKind.Test, string player = null, ulong? at = null)
    {
        var players = OnlinePlayers();
        // A player who is leaving is still in the client list for a moment.
        if (kind == BmdKind.Leave && player != null) players.Remove(player);
        var text = Fill(template, day, daysLeft, players, player, at);
        if (kind == BmdKind.End && BmdSettings.Recap && !template.Contains("{recap}")) text += "\n" + BmdRecap.Text();
        bool any = false;
        if (BmdSettings.InGameChat && BmdSettings.ChatKinds.Contains(kind)) { SendChat(text); any = true; }
        if (!BmdSettings.Enabled) { if (!any) LastResult = "not sent: " + BmdSettings.Status; return any; }
        if (text.Length > 2000) text = text.Substring(0, 1997) + "..."; // Discord's content limit
        outbox.Enqueue((text, kind));
        if (!sending) StartCoroutine(Drain());
        return true;
    }

    // Game chat: no Discord markdown, and emoji become nothing (the game font has none).
    public static string ForChat(string text)
    {
        var t = text.Replace("**", "").Replace("__", "").Replace("~~", "").Replace("`", "");
        t = Regex.Replace(t, @"(?<![\w\\])[*_](?=\S)|(?<=\S)[*_](?![\w])", "");
        t = t.Replace("\\", "");
        var sb = new StringBuilder();
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (char.IsSurrogate(c)) continue;
            if ((c >= '\u2190' && c <= '\u21FF') || (c >= '\u2300' && c <= '\u27BF') || (c >= '\u2B00' && c <= '\u2BFF')
                || c == '\uFE0F' || c == '\u200D') continue;
            sb.Append(c);
        }
        return Regex.Replace(sb.ToString(), @"[ \t]{2,}", " ").Trim();
    }

    public static void SendChat(string text)
    {
        try
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.World == null) return;
            bool first = true;
            foreach (var line in ForChat(text).Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                // Headline in blood red, the details under it in a softer pink.
                gm.ChatMessageServer(null, EChatType.Global, -1, (first ? "[ff4040]" : "[ffc0c0]") + line.Trim() + "[-]", null, EMessageSender.Server);
                first = false;
            }
        }
        catch (Exception e) { Log.Warning("[BloodMoonDiscord] game chat: " + e.Message); }
    }

    // Blocking post, only for shutdown: the coroutine wouldn't get to run. Gives up after 5 seconds.
    static void PostNow(string text, BmdKind kind)
    {
        try
        {
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(BmdSettings.Webhook);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 5000;
            var body = Encoding.UTF8.GetBytes(BuildJson(text, kind));
            using (var stream = req.GetRequestStream()) stream.Write(body, 0, body.Length);
            using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                Log.Out($"[BloodMoonDiscord] Posted ({(int)resp.StatusCode}): " + FirstLine(text));
        }
        catch (Exception e) { Log.Warning("[BloodMoonDiscord] Shutdown post failed: " + e.Message); }
    }

    IEnumerator Drain()
    {
        sending = true;
        while (outbox.Count > 0)
        {
            var (text, kind) = outbox.Dequeue();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float retryAfter = 0f;
                using (var req = new UnityWebRequest(BmdSettings.Webhook, "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(BuildJson(text, kind)));
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.timeout = 15;
                    yield return req.SendWebRequest(); // async: never blocks the server tick

                    long code = req.responseCode;
                    if (code >= 200 && code < 300)
                    {
                        LastResult = $"sent OK ({DateTime.Now:HH:mm:ss})";
                        Log.Out("[BloodMoonDiscord] Posted: " + FirstLine(text));
                        break;
                    }
                    if (code == 429 && attempt == 0)
                    {
                        var m = Regex.Match(req.downloadHandler?.text ?? "", "\"retry_after\"\\s*:\\s*([0-9.]+)");
                        retryAfter = m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var ra) ? Mathf.Clamp(ra, 1f, 30f) : 5f;
                    }
                    else
                    {
                        LastResult = code > 0 ? $"failed: HTTP {code}" + (code == 401 || code == 404 ? " (webhook deleted or URL wrong?)" : "")
                                              : $"failed: {req.error} (can the server reach discord.com?)";
                        Log.Warning("[BloodMoonDiscord] Post " + LastResult);
                        break;
                    }
                }
                Log.Out($"[BloodMoonDiscord] Discord rate limit, retrying in {retryAfter:0.#}s.");
                yield return new WaitForSecondsRealtime(retryAfter);
            }
        }
        sending = false;
    }

    static string FirstLine(string s)
    {
        int nl = s.IndexOf('\n');
        var line = nl >= 0 ? s.Substring(0, nl) : s;
        return line.Length > 100 ? line.Substring(0, 97) + "..." : line;
    }

    public static int Colour(BmdKind kind)
    {
        switch (kind)
        {
            case BmdKind.Reminder: return 0xE67E22;   // orange
            case BmdKind.Countdown: return 0xF1C40F;  // yellow
            case BmdKind.Start: return 0xC0392B;      // red
            case BmdKind.Update: return 0x7B0000;     // dark red
            case BmdKind.End: return 0xF5B041;        // dawn gold
            case BmdKind.ServerStart: return 0x2ECC71;
            case BmdKind.ServerStop: return 0x7F8C8D;
            case BmdKind.Join: return 0x3498DB;
            case BmdKind.Leave: return 0x95A5A6;
            default: return 0x9B59B6;
        }
    }

    static string BuildJson(string text, BmdKind kind)
    {
        // allowed_mentions: by default nothing pings, even if a message or player name contains @everyone.
        var mentions = BmdSettings.Mentions ? "[\"everyone\",\"roles\",\"users\"]" : "[]";
        // Pings only work in plain content, so a message that pings is never an embed.
        bool pings = BmdSettings.Mentions && (text.Contains("@everyone") || text.Contains("@here") || text.Contains("<@"));
        var body = BmdSettings.Embeds && !pings
            ? ",\"embeds\":[" + Embed(text, kind) + "]"
            : ",\"content\":" + Json(text);
        return "{\"username\":" + Json(BmdSettings.BotName) + body +
               ",\"allowed_mentions\":{\"parse\":" + mentions + "}}";
    }

    // First line becomes the embed title (Discord shows titles bold already), the rest the body,
    // plus a timestamp that Discord shows in each reader's own time zone.
    static string Embed(string text, BmdKind kind)
    {
        string title = text, desc = "";
        int nl = text.IndexOf('\n');
        if (nl >= 0) { title = text.Substring(0, nl); desc = text.Substring(nl + 1).Trim(); }
        title = title.Replace("**", "").Replace("__", "").Trim();
        if (title.Length > 256) { desc = (title + "\n" + desc).Trim(); title = ""; }
        var sb = new StringBuilder("{");
        if (title.Length > 0) sb.Append("\"title\":").Append(Json(title)).Append(',');
        if (desc.Length > 0) sb.Append("\"description\":").Append(Json(desc)).Append(',');
        sb.Append("\"color\":").Append(Colour(kind));
        sb.Append(",\"timestamp\":").Append(Json(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)));
        return sb.Append('}').ToString();
    }

    static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // Player names shouldn't turn into bold/italics/spoilers in Discord.
    public static string EscapeMarkdown(string s) => Regex.Replace(s, @"([\\*_~`|>])", @"\$1");
}

// Server console (F1 as admin, telnet, or the host's web console)
public class ConsoleCmdAzraelBloodMoonDiscord : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient => false; // runs on the server, which does the posting
    public override bool AllowedInMainMenu => false;
    public override int DefaultPermissionLevel => 0; // admins only
    public override string[] getCommands() => new[] { "bmdiscord" };
    public override string getDescription() => "Blood Moon Discord: status, test posts, horde sound, reload settings";
    public override string getHelp() =>
        "bmdiscord                    - show status and the full schedule\n" +
        "bmdiscord test               - post a test message\n" +
        "bmdiscord test reminder <n>  - post reminder number n now (numbers are shown by 'bmdiscord')\n" +
        "bmdiscord test <countdown|start|update|end> - post that message now (end includes the recap so far)\n" +
        "bmdiscord test <serverstart|serverstop|join|leave> - post that message now\n" +
        "bmdiscord recap              - show the horde recap so far (or from the last horde)\n" +
        "bmdiscord sound              - play the horde sound for everyone online now (a test: players with\n" +
        "                               their own clip hear the game sound too, since it's not horde time)\n" +
        "bmdiscord reload             - re-read discord.txt and settings.txt";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        var n = BmdNotifier.Instance;
        if (n == null) { con.Output("Blood Moon Discord only runs on a dedicated server."); return; }

        string arg = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (arg == "reload")
        {
            n.Reload();
            con.Output(BmdSettings.Enabled ? "Reloaded." : "Reloaded. Posts are off: " + BmdSettings.Status);
            ShowSchedule(con);
            return;
        }
        if (arg == "sound")
        {
            int sent = BmdNotifier.BroadcastHordeSound();
            con.Output(sent > 0 ? $"Played '{BmsSettings.HordeSoundName}' for {sent} player(s)." : "Nobody is online to hear it.");
            return;
        }
        if (arg == "test")
        {
            int bmDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
            string template;
            var kind = BmdKind.Test;
            int daysLeft = BmdNotifier.DaysUntil(bmDay);
            ulong? at = null; // world time to preview the message at
            string which = _params.Count > 1 ? _params[1].ToLowerInvariant() : "";
            switch (which)
            {
                case "": template = "✅ Blood Moon Discord test message. Posting works!"; break;
                case "countdown":
                    template = BmdSettings.MsgCountdown; kind = BmdKind.Countdown;
                    daysLeft = Math.Max(1, daysLeft);
                    at = GameUtils.DayTimeToWorldTime(bmDay - daysLeft, BmdSettings.CountdownHour, 0);
                    break;
                case "start": template = BmdSettings.MsgStart; kind = BmdKind.Start; at = BmdTime.HordeStart(bmDay); break;
                case "update":
                    template = BmdSettings.MsgUpdate; kind = BmdKind.Update;
                    at = BmdTime.HordeStart(bmDay) + (ulong)Math.Max(1, BmdSettings.UpdateEveryHours) * 1000UL;
                    break;
                case "end": template = BmdSettings.MsgEnd; kind = BmdKind.End; at = BmdTime.HordeEnd(bmDay); break;
                case "serverstart": template = BmdSettings.MsgServerStart; kind = BmdKind.ServerStart; break;
                case "serverstop": template = BmdSettings.MsgServerStop; kind = BmdKind.ServerStop; break;
                case "join": template = BmdSettings.MsgJoin; kind = BmdKind.Join; break;
                case "leave": template = BmdSettings.MsgLeave; kind = BmdKind.Leave; break;
                case "reminder":
                    if (_params.Count < 3 || !int.TryParse(_params[2], out var idx) || idx < 1 || idx > BmdSettings.Reminders.Count)
                    {
                        con.Output($"Use: bmdiscord test reminder <1-{BmdSettings.Reminders.Count}>");
                        return;
                    }
                    template = BmdSettings.Reminders[idx - 1].Message;
                    daysLeft = BmdSettings.Reminders[idx - 1].DaysBefore;
                    at = GameUtils.DayTimeToWorldTime(bmDay - daysLeft, BmdSettings.Reminders[idx - 1].Hour, 0);
                    kind = BmdKind.Reminder;
                    break;
                default: con.Output("Unknown message. Use: reminder <n>, countdown, start, update, end, serverstart, serverstop, join or leave."); return;
            }
            if (n.Post(template, bmDay, daysLeft, kind, kind == BmdKind.Join || kind == BmdKind.Leave ? "TestPlayer" : null, at))
                con.Output((BmdSettings.Enabled ? "Sending... type 'bmdiscord' in a few seconds to see if it went through." : "Not posted to Discord: " + BmdSettings.Status + ".")
                           + (BmdSettings.InGameChat && BmdSettings.ChatKinds.Contains(kind) ? " Also shown in game chat." : ""));
            else
                con.Output("Not sent: " + BmdSettings.Status);
            return;
        }
        if (arg == "recap")
        {
            con.Output((BmdRecap.Tracking ? "Horde in progress. " : "Last horde. ") + BmdNotifier.ForChat(BmdRecap.Text()).Replace("\n", " | "));
            return;
        }

        con.Output(BmdSettings.Enabled
            ? $"Blood Moon Discord: ON, posting to {BmdSettings.MaskedWebhook()} as '{BmdSettings.BotName}'" + (BmdSettings.Embeds ? " (coloured embeds)." : " (plain text).")
            : $"Blood Moon Discord: OFF ({BmdSettings.Status}).");
        con.Output(BmdSettings.InGameChat ? $"In-game chat: {BmdNotifier.ChatKindsText()}." : "In-game chat: off.");
        ShowSchedule(con);
        con.Output(BmsSettings.HordeSound
            ? $"Horde sound for all players: '{BmsSettings.HordeSoundName}' (settings.txt)."
            : "Horde sound for all players: off (settings.txt).");
        int next = GameStats.GetInt(EnumGameStats.BloodMoonDay);
        con.Output($"Next blood moon: day {next}, {BmdNotifier.TimeLeftText(next)}. Last post: {n.LastResult}.");
    }

    static void ShowSchedule(SdtdConsole con)
    {
        var s = BmdNotifier.Schedule();
        if (s.Count == 0) con.Output("Schedule: no messages turned on.");
        else { con.Output("Schedule:"); foreach (var line in s) con.Output("  " + line); }
        if (BmdSettings.SkipEmpty) con.Output("  (start/update/dawn are skipped when nobody is online)");
        foreach (var p in BmdSettings.Problems) con.Output($"  PROBLEM in {BmdSettings.FileName} {p}");
    }
}
