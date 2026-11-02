using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Networking;

// Discord posts (1.13.0). webhook.txt in this mod's folder holds the URL and which posts are sent and how they read
// (see webhook.example.txt). A webhook.txt that is only the URL, as before 1.13.0, still works with the standard posts.
// With no URL there, the Webhook= line of Blood Moon Sound's discord.txt is used.
// Nothing is posted when webhook.txt says Enabled=off or WebhookEvents=false (AzraelBait.cfg, /thrall admin webhook off).
public static partial class Thralls
{
    static string webhookUrl;
    static string webhookFrom = "";     // where the URL came from, for /thrall admin webhook
    static bool webhookRead;
    static readonly List<UnityWebRequest> webSends = new List<UnityWebRequest>();

    // Settings from webhook.txt. Reset() puts back the standard ones before each read.
    static class Hook
    {
        public static bool Enabled;
        public static string BotName;
        public static string Tames;         // rare, all or off
        public static int TameMinLevel;
        public static int LevelEvery;       // -1 = not in webhook.txt, use BaitConfig.WebhookLevelStep
        public static bool Evolutions, Retirements, AlphaSightings, AlphaTames;
        public static readonly Dictionary<string, string> Msg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static readonly Dictionary<string, string> Standard = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "MsgTame", "\U0001F9DF **{player}** tamed a **{creature}** (level {level})!" },
            { "MsgLevel", "⭐ **{thrall}** reached level {level} for **{player}** ({kills} kills)." },
            { "MsgEvolve", "✨ **{player}**'s {from} evolved into **{thrall}**." },
            { "MsgRetire", "**{player}** retired **{thrall}** after {kills} kills." },
            { "MsgAlpha", "**Alpha:** An Alpha roams: {alpha} the {creature}, level {level}, {where} of {player}. Only Feast Bait tames it." },
            { "MsgAlphaTamed", "**Alpha tamed:** {player} tamed the Alpha {alpha} the {creature}." },
        };

        public static void Reset()
        {
            Enabled = true; BotName = "Bait"; Tames = "rare"; TameMinLevel = 0; LevelEvery = -1;
            Evolutions = true; Retirements = true; AlphaSightings = true; AlphaTames = true;
            Msg.Clear();
            foreach (var kv in Standard) Msg[kv.Key] = kv.Value;
        }

        static Hook() { Reset(); }

        public static int LevelStep { get { return LevelEvery >= 0 ? LevelEvery : BaitConfig.WebhookLevelStep; } }
    }

    static bool OnOff(string v, bool fallback)
    {
        string s = (v ?? "").Trim().ToLowerInvariant();
        if (s == "on" || s == "true" || s == "yes" || s == "1") return true;
        if (s == "off" || s == "false" || s == "no" || s == "0") return false;
        return fallback;
    }

    static void ReadHookSetting(string key, string v, string file, int line)
    {
        int n;
        switch (key.ToLowerInvariant())
        {
            case "enabled": Hook.Enabled = OnOff(v, Hook.Enabled); return;
            case "botname": if (v.Length > 0) Hook.BotName = v.Length > 80 ? v.Substring(0, 80) : v; return;
            case "posttames":
            {
                string t = v.ToLowerInvariant();
                if (t == "rare" || t == "all" || t == "off") Hook.Tames = t;
                else Log.Warning("[Bait] " + file + " line " + line + ": PostTames is rare, all or off");
                return;
            }
            case "tameminlevel": if (int.TryParse(v, out n) && n >= 0) Hook.TameMinLevel = n; return;
            case "levelevery": if (int.TryParse(v, out n) && n >= 0) Hook.LevelEvery = n; return;
            case "evolutions": Hook.Evolutions = OnOff(v, Hook.Evolutions); return;
            case "retirements": Hook.Retirements = OnOff(v, Hook.Retirements); return;
            case "alphasightings": Hook.AlphaSightings = OnOff(v, Hook.AlphaSightings); return;
            case "alphatames": Hook.AlphaTames = OnOff(v, Hook.AlphaTames); return;
        }
        if (Hook.Standard.ContainsKey(key)) { Hook.Msg[key] = v.Length > 0 ? v : Hook.Standard[key]; return; }
        Log.Warning("[Bait] " + file + " line " + line + ": unknown setting " + key);
    }

    static void ReadWebhook()
    {
        webhookRead = true;
        webhookUrl = null; webhookFrom = "";
        Hook.Reset();
        try
        {
            string mod = BaitConfig.ModPath;
            if (string.IsNullOrEmpty(mod)) return;
            string own = Path.Combine(mod, "webhook.txt");
            if (File.Exists(own))
            {
                string[] all = File.ReadAllLines(own);
                for (int i = 0; i < all.Length; i++)
                {
                    string l = all[i].Trim();
                    if (l.Length == 0 || l[0] == '#' || l[0] == ';') continue;
                    if (IsWebhookUrl(l)) { webhookUrl = l; continue; }   // the whole file used to be just the URL
                    int eq = l.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                    if (k.Equals("Webhook", StringComparison.OrdinalIgnoreCase)) { if (IsWebhookUrl(v)) webhookUrl = v; continue; }
                    ReadHookSetting(k, v, "webhook.txt", i + 1);
                }
                if (webhookUrl != null) webhookFrom = "webhook.txt";
            }
            if (webhookUrl == null && Hook.Enabled)
            {
                foreach (string dir in Directory.GetDirectories(Path.GetDirectoryName(mod.TrimEnd('/', '\\'))))
                {
                    string f = Path.Combine(dir, "discord.txt");
                    if (!File.Exists(f)) continue;
                    webhookUrl = UrlFrom(File.ReadAllLines(f));
                    if (webhookUrl != null) { webhookFrom = Path.GetFileName(dir) + "/discord.txt"; break; }
                }
            }
            if (!Hook.Enabled) Log.Out("[Bait] webhook.txt has Enabled=off: nothing is posted to Discord.");
            else if (webhookUrl != null) Log.Out("[Bait] Discord webhook from " + webhookFrom + ": posting " + HookSummary() + ".");
        }
        catch (Exception e) { Log.Warning("[Bait] webhook: " + e.Message); }
    }

    static string WebhookUrl()
    {
        if (!webhookRead) ReadWebhook();
        return webhookUrl;
    }

    static bool IsWebhookUrl(string l)
    {
        return l.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && l.IndexOf("/api/webhooks/", StringComparison.OrdinalIgnoreCase) > 0 && l.IndexOf(' ') < 0;
    }

    // Blood Moon Sound's discord.txt: the Webhook= line.
    static string UrlFrom(string[] lines)
    {
        foreach (string raw in lines)
        {
            string l = raw.Trim();
            if (l.Length == 0 || l[0] == '#') continue;
            int eq = l.IndexOf('=');
            if (eq > 0 && l.Substring(0, eq).Trim().Equals("Webhook", StringComparison.OrdinalIgnoreCase)) l = l.Substring(eq + 1).Trim();
            if (IsWebhookUrl(l)) return l;
        }
        return null;
    }

    static string HookSummary()
    {
        var on = new List<string>();
        if (Hook.Tames != "off") on.Add((Hook.Tames == "all" ? "all tames" : "rare tames") + (Hook.TameMinLevel > 0 ? " (level " + Hook.TameMinLevel + "+)" : ""));
        if (Hook.LevelStep > 0) on.Add("every " + Hook.LevelStep + "th level");
        if (Hook.Evolutions) on.Add("evolutions");
        if (Hook.Retirements) on.Add("retirements");
        if (Hook.AlphaSightings) on.Add("Alpha sightings");
        if (Hook.AlphaTames) on.Add("Alpha tames");
        return on.Count == 0 ? "nothing (every post is off)" : string.Join(", ", on.ToArray());
    }

    static string Esc(string s) { return (s ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("@", "@​"); }

    static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c < 32) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    // The message for one kind of post with its {placeholders} filled in (names escaped, the message's own Markdown kept).
    static string Fill(string msgKey, params string[] pairs)
    {
        string text;
        if (!Hook.Msg.TryGetValue(msgKey, out text)) text = Hook.Standard[msgKey];
        for (int i = 0; i + 1 < pairs.Length; i += 2) text = text.Replace("{" + pairs[i] + "}", Esc(pairs[i + 1]));
        return text.Replace("\\n", "\n");
    }

    static bool Posting()
    {
        if (!BaitConfig.WebhookEvents) return false;
        string url = WebhookUrl();
        return url != null && Hook.Enabled;
    }

    static void Webhook(string text) { if (Posting()) Send(webhookUrl, text); }

    static void Send(string url, string text)
    {
        try
        {
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"username\":" + Json(Hook.BotName) + ",\"content\":" + Json(text) + ",\"allowed_mentions\":{\"parse\":[]}}"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 15;
            req.SendWebRequest();   // async; finished ones are cleaned up by WebTick
            webSends.Add(req);
        }
        catch (Exception e) { Log.Warning("[Bait] webhook post: " + e.Message); }
    }

    static void WebTick()
    {
        for (int i = webSends.Count - 1; i >= 0; i--)
        {
            var r = webSends[i];
            if (!r.isDone) continue;
            if (r.responseCode < 200 || r.responseCode >= 300) Log.Warning("[Bait] webhook post failed: " + (r.responseCode > 0 ? "HTTP " + r.responseCode : r.error));
            r.Dispose();
            webSends.RemoveAt(i);
        }
    }

    static bool IsRare(string cls)
    {
        string c = (cls ?? "").ToLowerInvariant();
        return TierOf(cls) >= 2 || c.Contains("boss") || c.Contains("demolition") || c.Contains("behemoth");
    }

    // ------------------------------------------------------------------ the posts
    static void WebhookTamed(EntityPlayer owner, ThrallRecord r)
    {
        if (!Posting() || Hook.Tames == "off" || r.Level < Hook.TameMinLevel) return;
        if (r.Alpha && Hook.AlphaTames) return;   // MsgAlphaTamed already covers it
        if (Hook.Tames == "rare" && !IsRare(r.ClassName)) return;
        Webhook(Fill("MsgTame", "player", owner.EntityName, "creature", Pretty(r.ClassName), "thrall", Label(r), "level", r.Level.ToString()));
    }

    static void WebhookLevel(ThrallRecord r)
    {
        int step = Hook.LevelStep;
        if (step > 0 && r.Level > 0 && r.Level % step == 0 && Posting())
            Webhook(Fill("MsgLevel", "thrall", Label(r), "player", NameOfKey(r.OwnerKey), "creature", Pretty(r.ClassName), "level", r.Level.ToString(), "kills", r.Kills.ToString()));
    }

    static void WebhookEvolved(EntityPlayer owner, string from, ThrallRecord r)
    {
        if (Posting() && Hook.Evolutions)
            Webhook(Fill("MsgEvolve", "player", owner.EntityName, "from", Pretty(from), "thrall", Label(r), "creature", Pretty(r.ClassName), "level", r.Level.ToString(), "kills", r.Kills.ToString()));
    }

    static void WebhookRetired(string key, ThrallRecord r)
    {
        if (Posting() && Hook.Retirements)
            Webhook(Fill("MsgRetire", "player", NameOfKey(key), "thrall", Label(r), "creature", Pretty(r.ClassName), "level", r.Level.ToString(), "kills", r.Kills.ToString()));
    }

    static void WebhookAlphaSeen(string alpha, string cls, int level, string where, string player)
    {
        if (Posting() && Hook.AlphaSightings)
            Webhook(Fill("MsgAlpha", "alpha", alpha, "creature", Pretty(cls), "level", level.ToString(), "where", where, "player", player));
    }

    static void WebhookAlphaTamed(ThrallRecord r, string alpha)
    {
        if (Posting() && Hook.AlphaTames)
            Webhook(Fill("MsgAlphaTamed", "player", NameOfKey(r.OwnerKey), "alpha", alpha, "creature", Pretty(r.ClassName), "level", r.Level.ToString()));
    }

    // ------------------------------------------------------------------ /thrall admin webhook [reload|test|on|off]
    static void WebhookCommand(int me, string[] a)
    {
        string sub = a.Length > 3 ? a[3].ToLowerInvariant() : "";
        if (sub == "reload") { ReadWebhook(); Audit(me, "webhook reload"); }
        else if (sub == "on" || sub == "off")
        {
            string err;
            BaitConfig.Set("WebhookEvents", sub == "on" ? "true" : "false", out err);
            BaitConfig.SaveRuntime();
            Audit(me, "webhook " + sub);
            Tell(me, sub == "on" ? "Discord posts are on." : "Discord posts are off. Nothing is posted until /thrall admin webhook on.");
            if (sub == "on" && !webhookRead) ReadWebhook();
            if (sub == "on" && !Hook.Enabled) Tell(me, "webhook.txt still says Enabled=off: change it, then /thrall admin webhook reload.");
            return;
        }
        else if (sub == "test")
        {
            if (!Posting()) { Tell(me, "Nothing to post to: " + WebhookState()); return; }
            Send(webhookUrl, "\U0001F9EA Bait test post. Posting " + HookSummary() + ".");
            Tell(me, "Test post sent. If it doesn't show up in Discord, the server log has the error.");
            return;
        }
        else if (sub != "")
        {
            Tell(me, "Usage: /thrall admin webhook [reload | test | on | off]");
            return;
        }
        if (!webhookRead) ReadWebhook();
        Tell(me, "Discord: " + WebhookState());
        if (Posting()) Tell(me, "Posting " + HookSummary() + ". Edit webhook.txt, then /thrall admin webhook reload.");
    }

    static string WebhookState()
    {
        if (!webhookRead) ReadWebhook();
        if (!BaitConfig.WebhookEvents) return "off (WebhookEvents=false; /thrall admin webhook on turns it back on).";
        if (!Hook.Enabled) return "off (webhook.txt says Enabled=off).";
        if (webhookUrl == null) return "no webhook URL in webhook.txt or Blood Moon Sound's discord.txt.";
        return "on, using the webhook in " + webhookFrom + ".";
    }
}
