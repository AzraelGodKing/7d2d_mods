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
// The webhook URL is a secret (anyone with it can post to the channel): discord.txt is gitignored
// and build.ps1 leaves it out of every zip. The repo ships discord.example.txt instead.

public static class BmdSettings
{
    public const string FileName = "discord.txt";

    public static string Webhook;
    public static string BotName;
    public static bool Mentions;
    public static bool SkipEmpty;

    public static bool DayBefore;  public static int DayBeforeHour;
    public static bool Warning;    public static int WarningHour;
    public static bool Start;
    public static bool End;

    public static string MsgDayBefore, MsgWarning, MsgStart, MsgEnd;

    public static string Status; // why posting is off, or null when it's on
    public static bool Enabled => Status == null;
    public static string FilePath => Path.Combine(BmsSettings.ModPath ?? "", FileName);

    static void Defaults()
    {
        Webhook = null; BotName = "Blood Moon"; Mentions = false; SkipEmpty = true;
        DayBefore = true; DayBeforeHour = 12;
        Warning = true; WarningHour = 18;
        Start = true; End = true;
        MsgDayBefore = "⚠️ Blood Moon **tomorrow night** (day {day}). Get your base ready!";
        MsgWarning   = "\U0001F534 Blood Moon **tonight** (day {day}). The horde comes at dusk.";
        MsgStart     = "\U0001F315 The Blood Moon has risen! Horde incoming. Online: {players}";
        MsgEnd       = "\U0001F305 Dawn! Blood Moon day {day} survived. Online: {players}";
    }

    public static void Load()
    {
        Defaults();
        Status = null;
        try
        {
            if (!File.Exists(FilePath)) { Status = $"no {FileName} (copy discord.example.txt to {FileName} and paste the webhook URL)"; return; }
            foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var k = line.Substring(0, eq).Trim().ToLowerInvariant();
                var v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "webhook": Webhook = v; break;
                    case "botname": if (v.Length > 0) BotName = v; break;
                    case "mentions": Mentions = Bool(v, Mentions); break;
                    case "skipemptyserver": SkipEmpty = Bool(v, SkipEmpty); break;
                    case "daybefore": DayBefore = Bool(v, DayBefore); break;
                    case "daybeforehour": DayBeforeHour = Hour(v, DayBeforeHour); break;
                    case "warning": Warning = Bool(v, Warning); break;
                    case "warninghour": WarningHour = Hour(v, WarningHour); break;
                    case "start": Start = Bool(v, Start); break;
                    case "end": End = Bool(v, End); break;
                    case "msgdaybefore": MsgDayBefore = Msg(v, MsgDayBefore); break;
                    case "msgwarning": MsgWarning = Msg(v, MsgWarning); break;
                    case "msgstart": MsgStart = Msg(v, MsgStart); break;
                    case "msgend": MsgEnd = Msg(v, MsgEnd); break;
                }
            }
            if (string.IsNullOrEmpty(Webhook)) Status = $"Webhook= is empty in {FileName}";
            else if (!Webhook.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || Webhook.IndexOf("/api/webhooks/", StringComparison.OrdinalIgnoreCase) < 0)
                Status = $"Webhook= in {FileName} doesn't look like a Discord webhook URL (https://discord.com/api/webhooks/...)";
        }
        catch (Exception e) { Status = $"could not read {FileName}: {e.Message}"; }
    }

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

    public enum Kind { DayBefore, Warning, Start, End, Test }

    readonly Queue<string> outbox = new Queue<string>();
    bool sending;
    public string LastResult = "nothing sent yet";

    float timer;
    bool? lastBloodMoon;
    int lastDay = -1, lastHour = -1;
    int hordeDay = -1;                 // blood moon day of the horde in progress, -1 when none
    int lastDayBeforeDay = -1, lastWarnDay = -1;

    void Start() => Reload();

    public void Reload()
    {
        BmdSettings.Load();
        if (BmdSettings.Enabled)
            Log.Out($"[BloodMoonDiscord] Posting to {BmdSettings.MaskedWebhook()}: {Describe()}.");
        else
            Log.Out($"[BloodMoonDiscord] Discord posts off: {BmdSettings.Status}.");
    }

    public static string Describe()
    {
        var parts = new List<string>();
        if (BmdSettings.DayBefore) parts.Add($"day before at {BmdSettings.DayBeforeHour:00}:00");
        if (BmdSettings.Warning) parts.Add($"blood moon day at {BmdSettings.WarningHour:00}:00");
        if (BmdSettings.Start) parts.Add("horde start");
        if (BmdSettings.End) parts.Add("dawn after");
        var s = parts.Count > 0 ? string.Join(", ", parts) : "no messages turned on";
        if (BmdSettings.SkipEmpty) s += " (start/dawn skipped when nobody is online)";
        return s;
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
            hordeDay = day;
            if (BmdSettings.Start && PlayersOkay()) Post(Kind.Start, day);
        }

        // End check uses the horde's own day, not the current BloodMoonDay stat,
        // because the game moves that stat on to the next blood moon around dawn.
        if (hordeDay >= 0 && !GameUtils.IsBloodMoonTime(t, duskDawn, hordeDay))
        {
            if (BmdSettings.End && PlayersOkay()) Post(Kind.End, hordeDay);
            hordeDay = -1;
        }

        if (bmDay > 0 && !bloodMoon)
        {
            if (BmdSettings.DayBefore && day == bmDay - 1 && Crossed(day, hour, BmdSettings.DayBeforeHour) && lastDayBeforeDay != day)
            {
                lastDayBeforeDay = day;
                Post(Kind.DayBefore, bmDay);
            }
            if (BmdSettings.Warning && day == bmDay && Crossed(day, hour, BmdSettings.WarningHour) && lastWarnDay != day)
            {
                lastWarnDay = day;
                Post(Kind.Warning, bmDay);
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

    public static string Template(Kind kind)
    {
        switch (kind)
        {
            case Kind.DayBefore: return BmdSettings.MsgDayBefore;
            case Kind.Warning: return BmdSettings.MsgWarning;
            case Kind.Start: return BmdSettings.MsgStart;
            case Kind.End: return BmdSettings.MsgEnd;
            default: return "✅ Blood Moon Discord test message. Posting works!";
        }
    }

    // Returns false (and says why) when posting is off.
    public bool Post(Kind kind, int day)
    {
        if (!BmdSettings.Enabled) { LastResult = "not sent: " + BmdSettings.Status; return false; }
        var players = OnlinePlayers();
        var text = Template(kind)
            .Replace("{day}", day.ToString())
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

// Server console (F1 as admin, telnet, or the host's web console): bmdiscord [test [kind]|reload]
public class ConsoleCmdAzraelBloodMoonDiscord : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient => false; // runs on the server, which does the posting
    public override bool AllowedInMainMenu => false;
    public override int DefaultPermissionLevel => 0; // admins only
    public override string[] getCommands() => new[] { "bmdiscord" };
    public override string getDescription() => "Blood Moon Discord: status, test post, reload discord.txt";
    public override string getHelp() =>
        "bmdiscord                    - show status\n" +
        "bmdiscord test               - post a test message\n" +
        "bmdiscord test <daybefore|warning|start|end> - post that message now (with the real next blood moon day)\n" +
        "bmdiscord reload             - re-read discord.txt";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        var con = SingletonMonoBehaviour<SdtdConsole>.Instance;
        var n = BmdNotifier.Instance;
        if (n == null) { con.Output("Blood Moon Discord only runs on a dedicated server."); return; }

        string arg = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (arg == "reload")
        {
            n.Reload();
            con.Output(BmdSettings.Enabled ? "Reloaded. Posting: " + BmdNotifier.Describe() : "Reloaded. Posts are off: " + BmdSettings.Status);
            return;
        }
        if (arg == "test")
        {
            var kind = BmdNotifier.Kind.Test;
            if (_params.Count > 1)
            {
                switch (_params[1].ToLowerInvariant())
                {
                    case "daybefore": kind = BmdNotifier.Kind.DayBefore; break;
                    case "warning": kind = BmdNotifier.Kind.Warning; break;
                    case "start": kind = BmdNotifier.Kind.Start; break;
                    case "end": kind = BmdNotifier.Kind.End; break;
                    default: con.Output("Unknown message. Use daybefore, warning, start or end."); return;
                }
            }
            if (n.Post(kind, GameStats.GetInt(EnumGameStats.BloodMoonDay)))
                con.Output("Sending... type 'bmdiscord' in a few seconds to see if it went through.");
            else
                con.Output("Not sent: " + BmdSettings.Status);
            return;
        }

        con.Output(BmdSettings.Enabled
            ? $"Blood Moon Discord: ON, posting to {BmdSettings.MaskedWebhook()} as '{BmdSettings.BotName}'."
            : $"Blood Moon Discord: OFF ({BmdSettings.Status}).");
        con.Output($"Messages: {BmdNotifier.Describe()}.");
        con.Output($"Next blood moon: day {GameStats.GetInt(EnumGameStats.BloodMoonDay)}. Last post: {n.LastResult}.");
    }
}
