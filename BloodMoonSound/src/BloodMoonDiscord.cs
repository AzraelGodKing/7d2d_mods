using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

// Blood Moon Discord: the server-side half of Blood Moon Sound.
//
// Runs ONLY on a dedicated server, and only posts if discord.txt (next to ModInfo.xml) has a webhook URL.
// Players never need this part: the server does the posting. And the sound half never needs the server.
// So the one mod works in both places without either side requiring the other.
//
// Everything about WHEN and WHAT gets posted is set in discord.txt: any number of reminders before
// the blood moon, an optional daily countdown, and the start / during / dawn messages.
//
// The webhook URL is a secret (anyone with it can post to the channel): discord.txt is gitignored
// and build.ps1 leaves it out of every zip. The repo ships discord.example.txt instead.

public class BmdReminder
{
    public int DaysBefore;   // 0 = on blood moon day, 1 = the day before, ...
    public int Hour;         // in-game hour, 0-23
    public string Message;
    public string Describe() =>
        (DaysBefore == 0 ? "blood moon day" : DaysBefore == 1 ? "1 day before" : $"{DaysBefore} days before") + $" at {Hour:00}:00";
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

    public static readonly List<string> Problems = new List<string>(); // bad lines, shown by 'bmdiscord'

    public static string Status; // why posting is off, or null when it's on
    public static bool Enabled => Status == null;
    public static string FilePath => Path.Combine(BmsSettings.ModPath ?? "", FileName);

    public const string DefaultMsgDayBefore = "⚠️ Blood Moon **tomorrow night** (day {day}). Get your base ready!";
    public const string DefaultMsgWarning   = "\U0001F534 Blood Moon **tonight** (day {day}). The horde comes at dusk.";

    static void Defaults()
    {
        Webhook = null; BotName = "Blood Moon"; Mentions = false; SkipEmpty = true;
        Reminders.Clear(); Problems.Clear();
        Countdown = false; CountdownHour = 8; CountdownFromDays = 7;
        Start = true; UpdateEveryHours = 0; End = true;
        MsgCountdown = "⏳ **{days} days** until the Blood Moon (day {day}).";
        MsgStart     = "\U0001F315 The Blood Moon has risen! Horde incoming. Online: {players}";
        MsgUpdate    = "\U0001FA78 The Blood Moon rages on. Still standing: {players}";
        MsgEnd       = "\U0001F305 Dawn! Blood Moon day {day} survived. Online: {players}";
    }

    public static void Load()
    {
        Defaults();
        Status = null;
        bool sawReminder = false;
        try
        {
            if (!File.Exists(FilePath)) { Status = $"no {FileName} (copy discord.example.txt to {FileName} and paste the webhook URL)"; return; }
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
            msg = days == 0 ? DefaultMsgWarning : days == 1 ? DefaultMsgDayBefore : "⚠️ Blood Moon in **{days} days** (day {day}).";
        return new BmdReminder { DaysBefore = days, Hour = hour, Message = msg.Replace("\\n", "\n") };
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
    static string Msg(string v, string fallback) => v.Length == 0 ? fallback : v.Replace("\\n", "\n");

    // For logs and the console: never print the token part of the URL.
    public static string MaskedWebhook()
    {
        if (string.IsNullOrEmpty(Webhook)) return "(none)";
        var m = Regex.Match(Webhook, @"/api/webhooks/(\d+)/");
        return m.Success ? $".../api/webhooks/{m.Groups[1].Value}/****" : "(set)";
    }
}

public class BmdNotifier : MonoBehaviour
{
    public static BmdNotifier Instance;

    readonly Queue<string> outbox = new Queue<string>();
    bool sending;
    public string LastResult = "nothing sent yet";

    float timer;
    bool? lastBloodMoon;
    int lastDay = -1, lastHour = -1;
    int hordeDay = -1;                 // blood moon day of the horde in progress, -1 when none
    ulong nextUpdateAt;                // world time of the next "during" update
    int lastCountdownDay = -1;
    readonly Dictionary<string, int> reminderSentFor = new Dictionary<string, int>(); // reminder -> blood moon day

    void Start() => Reload();

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
        if (BmdSettings.End) lines.Add("end: at dawn after");
        return lines;
    }

    public static string Describe()
    {
        var s = Schedule();
        var text = s.Count > 0 ? string.Join("; ", s) : "no messages turned on";
        if (BmdSettings.SkipEmpty) text += " (start/update/dawn skipped when nobody is online)";
        return text;
    }

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
        if (world == null) { lastBloodMoon = null; hordeDay = -1; return; }

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
            if (BmdSettings.Start && PlayersOkay()) Post(BmdSettings.MsgStart, bmDay);
        }

        // Updates while the horde is on (1000 world time = 1 in-game hour).
        if (hordeDay >= 0 && bloodMoon && BmdSettings.UpdateEveryHours > 0 && t >= nextUpdateAt)
        {
            ulong step = (ulong)BmdSettings.UpdateEveryHours * 1000UL;
            if (lastBloodMoon == true && PlayersOkay()) Post(BmdSettings.MsgUpdate, hordeDay);
            while (nextUpdateAt <= t) nextUpdateAt += step; // a time skip posts once, not once per missed hour
        }

        // End check uses the horde's own day, not the current BloodMoonDay stat,
        // because the game moves that stat on to the next blood moon around dawn.
        if (hordeDay >= 0 && !GameUtils.IsBloodMoonTime(t, duskDawn, hordeDay))
        {
            if (BmdSettings.End && PlayersOkay()) Post(BmdSettings.MsgEnd, hordeDay);
            hordeDay = -1;
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
                Post(r.Message, bmDay, daysLeft);
            }

            if (BmdSettings.Countdown && daysLeft >= 1 && daysLeft <= BmdSettings.CountdownFromDays
                && Crossed(day, hour, BmdSettings.CountdownHour) && lastCountdownDay != day)
            {
                lastCountdownDay = day;
                Post(BmdSettings.MsgCountdown, bmDay, daysLeft);
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

    // Days until the blood moon right now (0 on the day itself), for {days} in test posts.
    public static int DaysUntil(int bmDay)
    {
        var world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null) return 0;
        return Math.Max(0, bmDay - GameUtils.WorldTimeToDays(world.worldTime));
    }

    // Returns false (and says why) when posting is off.
    public bool Post(string template, int day, int daysLeft = 0)
    {
        if (!BmdSettings.Enabled) { LastResult = "not sent: " + BmdSettings.Status; return false; }
        var players = OnlinePlayers();
        var text = template
            .Replace("{day}", day.ToString())
            .Replace("{days}", daysLeft.ToString())
            .Replace("{count}", players.Count.ToString())
            .Replace("{players}", players.Count > 0 ? string.Join(", ", players.ConvertAll(EscapeMarkdown)) : "nobody");
        if (text.Length > 2000) text = text.Substring(0, 1997) + "..."; // Discord's content limit
        outbox.Enqueue(text);
        if (!sending) StartCoroutine(Drain());
        return true;
    }

    IEnumerator Drain()
    {
        sending = true;
        while (outbox.Count > 0)
        {
            var text = outbox.Dequeue();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float retryAfter = 0f;
                using (var req = new UnityWebRequest(BmdSettings.Webhook, "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(BuildJson(text)));
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

    static string BuildJson(string text)
    {
        // allowed_mentions: by default nothing pings, even if a message or player name contains @everyone.
        var mentions = BmdSettings.Mentions ? "[\"everyone\",\"roles\",\"users\"]" : "[]";
        return "{\"username\":" + Json(BmdSettings.BotName) + ",\"content\":" + Json(text) +
               ",\"allowed_mentions\":{\"parse\":" + mentions + "}}";
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
    static string EscapeMarkdown(string s) => Regex.Replace(s, @"([\\*_~`|>])", @"\$1");
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
        "bmdiscord test <countdown|start|update|end> - post that message now\n" +
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
            int daysLeft = BmdNotifier.DaysUntil(bmDay);
            string which = _params.Count > 1 ? _params[1].ToLowerInvariant() : "";
            switch (which)
            {
                case "": template = "✅ Blood Moon Discord test message. Posting works!"; break;
                case "countdown": template = BmdSettings.MsgCountdown; break;
                case "start": template = BmdSettings.MsgStart; break;
                case "update": template = BmdSettings.MsgUpdate; break;
                case "end": template = BmdSettings.MsgEnd; break;
                case "reminder":
                    if (_params.Count < 3 || !int.TryParse(_params[2], out var idx) || idx < 1 || idx > BmdSettings.Reminders.Count)
                    {
                        con.Output($"Use: bmdiscord test reminder <1-{BmdSettings.Reminders.Count}>");
                        return;
                    }
                    template = BmdSettings.Reminders[idx - 1].Message;
                    daysLeft = BmdSettings.Reminders[idx - 1].DaysBefore;
                    break;
                default: con.Output("Unknown message. Use: reminder <n>, countdown, start, update or end."); return;
            }
            if (n.Post(template, bmDay, daysLeft))
                con.Output("Sending... type 'bmdiscord' in a few seconds to see if it went through.");
            else
                con.Output("Not sent: " + BmdSettings.Status);
            return;
        }

        con.Output(BmdSettings.Enabled
            ? $"Blood Moon Discord: ON, posting to {BmdSettings.MaskedWebhook()} as '{BmdSettings.BotName}'."
            : $"Blood Moon Discord: OFF ({BmdSettings.Status}).");
        ShowSchedule(con);
        con.Output(BmsSettings.HordeSound
            ? $"Horde sound for all players: '{BmsSettings.HordeSoundName}' (settings.txt)."
            : "Horde sound for all players: off (settings.txt).");
        int next = GameStats.GetInt(EnumGameStats.BloodMoonDay);
        con.Output($"Next blood moon: day {next} (in {BmdNotifier.DaysUntil(next)} day(s)). Last post: {n.LastResult}.");
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
