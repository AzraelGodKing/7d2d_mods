using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Server only: horde history, per-player totals and survival streaks, kept with the save game
// (<save>/AzraelBloodMoon/) so each world has its own records.
public class BmdPlayerStats
{
    public string Name;
    public int HordeKills;   // zombies killed during blood moons, all time
    public int Hordes;       // blood moons this player was online for
    public int Survived;     // ... of those, without dying
    public int Streak;       // blood moons survived in a row, right now
    public int BestStreak;
}

public static class BmdStats
{
    public const string HistoryFile = "history.txt", PlayersFile = "players.txt", StateFile = "state.txt";

    public static readonly Dictionary<string, BmdPlayerStats> Players = new Dictionary<string, BmdPlayerStats>(StringComparer.OrdinalIgnoreCase);
    public static readonly List<string> History = new List<string>(); // one line per horde, oldest first
    public static int BestTotal;    // most zombies killed in one horde
    public static int BestTotalDay; // ... on this blood moon day
    // Small values that must survive a restart: the live countdown message, the last leaderboard post.
    public static readonly Dictionary<string, string> State = new Dictionary<string, string>();

    static string loadedDir;

    public static string Dir
    {
        get
        {
            try
            {
                var save = GameIO.GetSaveGameDir();
                return string.IsNullOrEmpty(save) ? null : Path.Combine(save, "AzraelBloodMoon");
            }
            catch { return null; }
        }
    }

    // Loads the files for the world that's running now (again if the world changed).
    public static bool EnsureLoaded()
    {
        var dir = Dir;
        if (dir == null) return false;
        if (dir == loadedDir) return true;
        loadedDir = dir;
        Players.Clear(); History.Clear(); State.Clear(); BestTotal = 0; BestTotalDay = 0;
        try
        {
            var h = Path.Combine(dir, HistoryFile);
            if (File.Exists(h))
                foreach (var line in File.ReadAllLines(h, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                    History.Add(line);
                    var f = Fields(line);
                    if (f.TryGetValue("total", out var t) && int.TryParse(t, out var total) && total > BestTotal)
                    {
                        BestTotal = total;
                        BestTotalDay = f.TryGetValue("day", out var d) && int.TryParse(d, out var dd) ? dd : 0;
                    }
                }
            var p = Path.Combine(dir, PlayersFile);
            if (File.Exists(p))
                foreach (var line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                    var c = line.Split('\t');
                    if (c.Length < 6) continue;
                    var s = new BmdPlayerStats { Name = c[0] };
                    int.TryParse(c[1], out s.HordeKills); int.TryParse(c[2], out s.Hordes); int.TryParse(c[3], out s.Survived);
                    int.TryParse(c[4], out s.Streak); int.TryParse(c[5], out s.BestStreak);
                    Players[s.Name] = s;
                }
            var st = Path.Combine(dir, StateFile);
            if (File.Exists(st))
                foreach (var line in File.ReadAllLines(st, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) State[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
        }
        catch (Exception e) { Log.Warning("[BloodMoonDiscord] Could not read blood moon records: " + e.Message); }
        return true;
    }

    public static void SetState(string key, string value)
    {
        if (!EnsureLoaded()) return;
        if (value == null) State.Remove(key); else State[key] = value;
        var sb = new StringBuilder();
        foreach (var kv in State) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
        Write(StateFile, sb.ToString());
    }

    public static string GetState(string key) => EnsureLoaded() && State.TryGetValue(key, out var v) ? v : null;

    static void Write(string file, string text)
    {
        try
        {
            Directory.CreateDirectory(loadedDir);
            var path = Path.Combine(loadedDir, file);
            File.WriteAllText(path + ".tmp", text, Encoding.UTF8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
        }
        catch (Exception e) { Log.Warning($"[BloodMoonDiscord] Could not save {file}: {e.Message}"); }
    }

    static void SavePlayers()
    {
        var sb = new StringBuilder("# name\thorde kills\tblood moons\tsurvived\tstreak now\tbest streak\n");
        foreach (var s in Players.Values)
            sb.Append(Clean(s.Name)).Append('\t').Append(s.HordeKills).Append('\t').Append(s.Hordes).Append('\t')
              .Append(s.Survived).Append('\t').Append(s.Streak).Append('\t').Append(s.BestStreak).Append('\n');
        Write(PlayersFile, sb.ToString());
    }

    static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace(',', ' ').Replace(':', ' ').Trim();

    static Dictionary<string, string> Fields(string line)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in line.Split('\t'))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) d[part.Substring(0, eq)] = part.Substring(eq + 1);
        }
        return d;
    }

    static string Pairs(Dictionary<string, int> d)
    {
        var parts = new List<string>();
        foreach (var kv in d) parts.Add(Clean(kv.Key) + ":" + kv.Value);
        return string.Join(",", parts);
    }

    static BmdPlayerStats Get(string name)
    {
        if (!Players.TryGetValue(name, out var s)) Players[name] = s = new BmdPlayerStats { Name = name };
        return s;
    }

    // Called once at dawn. Saves the horde and returns the extra dawn lines (record, streaks).
    public static string RecordHorde(int bmDay)
    {
        // Nobody was on: nothing worth keeping, and no streaks to change.
        if (BmdRecap.Present.Count == 0 && BmdRecap.Total == 0) return "";
        if (!EnsureLoaded()) return "";
        var lines = new List<string>();
        bool history = BmdSettings.History, streaks = BmdSettings.Streaks;

        int oldBest = BestTotal, oldBestDay = BestTotalDay;
        History.Add($"day={bmDay}\tdate={DateTime.Now:yyyy-MM-dd HH:mm}\ttotal={BmdRecap.Total}\tkills={Pairs(BmdRecap.Kills)}\tdeaths={Pairs(BmdRecap.Deaths)}\tplayers={string.Join(",", new List<string>(BmdRecap.Present).ConvertAll(Clean))}");
        var sb = new StringBuilder("# one line per blood moon horde (newest last)\n");
        foreach (var h in History) sb.Append(h).Append('\n');
        Write(HistoryFile, sb.ToString());
        if (BmdRecap.Total > BestTotal) { BestTotal = BmdRecap.Total; BestTotalDay = bmDay; }
        if (history && BmdRecap.Total > 0 && BmdRecap.Total > oldBest && History.Count > 1)
            lines.Add($"\U0001F3C6 **New server record:** {BmdRecap.Total} kills in one blood moon (was {oldBest}, day {oldBestDay})");

        var ended = new List<string>();
        var going = new List<BmdPlayerStats>();
        foreach (var name in BmdRecap.Present)
        {
            var s = Get(name);
            s.Hordes++;
            BmdRecap.Kills.TryGetValue(name, out var k);
            s.HordeKills += k;
            if (BmdRecap.Deaths.ContainsKey(name))
            {
                if (s.Streak >= 2) ended.Add($"{BmdNotifier.EscapeMarkdown(name)} ({s.Streak})");
                s.Streak = 0;
            }
            else
            {
                s.Survived++;
                s.Streak++;
                if (s.Streak > s.BestStreak) s.BestStreak = s.Streak;
                if (s.Streak >= 2) going.Add(s);
            }
        }
        SavePlayers();
        if (streaks)
        {
            going.Sort((a, b) => b.Streak.CompareTo(a.Streak));
            if (going.Count > 0)
                lines.Add("\U0001F525 Streaks: " + string.Join(", ", going.ConvertAll(s => $"{BmdNotifier.EscapeMarkdown(s.Name)} {s.Streak} in a row")));
            if (ended.Count > 0) lines.Add("\U0001F494 Streak ended: " + string.Join(", ", ended));
        }
        return string.Join("\n", lines);
    }

    public static List<string> LastHordes(int max)
    {
        var lines = new List<string>();
        if (!EnsureLoaded()) return lines;
        for (int i = History.Count - 1; i >= 0 && lines.Count < max; i--)
        {
            var f = Fields(History[i]);
            f.TryGetValue("day", out var day); f.TryGetValue("date", out var date); f.TryGetValue("total", out var total);
            f.TryGetValue("deaths", out var deaths); f.TryGetValue("kills", out var kills);
            lines.Add($"Day {day} ({date}): {total} kills" + (string.IsNullOrEmpty(kills) ? "" : $" [{kills.Replace(":", " ").Replace(",", ", ")}]")
                      + (string.IsNullOrEmpty(deaths) ? ", no deaths" : $", deaths {deaths.Replace(":", " ").Replace(",", ", ")}"));
        }
        return lines;
    }

    // Leaderboard body (no headline). Empty when nobody has been through a blood moon yet.
    public static string Leaderboard(int top = 5)
    {
        if (!EnsureLoaded() || Players.Count == 0) return "";
        var all = new List<BmdPlayerStats>(Players.Values);
        var sb = new StringBuilder();
        Section(sb, "\U0001F9DF Most horde kills", all, s => s.HordeKills, top);
        Section(sb, "\U0001F6E1️ Most blood moons survived", all, s => s.Survived, top);
        Section(sb, "\U0001F525 Longest streak", all, s => s.BestStreak, top);
        if (BestTotal > 0) sb.Append($"\U0001F3C6 Server record: {BestTotal} kills in one blood moon (day {BestTotalDay})\n");
        sb.Append($"{History.Count} blood moon{(History.Count == 1 ? "" : "s")} recorded on this world.");
        return sb.ToString();
    }

    static void Section(StringBuilder sb, string title, List<BmdPlayerStats> all, Func<BmdPlayerStats, int> value, int top)
    {
        var list = all.FindAll(s => value(s) > 0);
        if (list.Count == 0) return;
        list.Sort((a, b) => value(b) != value(a) ? value(b).CompareTo(value(a)) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var parts = new List<string>();
        for (int i = 0; i < list.Count && i < top; i++) parts.Add($"{i + 1}. {BmdNotifier.EscapeMarkdown(list[i].Name)} {value(list[i])}");
        sb.Append("**").Append(title).Append(":** ").Append(string.Join(" · ", parts)).Append('\n');
    }

    public static string Me(string name)
    {
        if (!EnsureLoaded() || string.IsNullOrEmpty(name) || !Players.TryGetValue(name, out var s))
            return "You haven't been through a blood moon on this world yet.";
        return $"Your blood moons: {s.Survived} of {s.Hordes} survived, {s.HordeKills} horde kills, streak {s.Streak} (best {s.BestStreak}).";
    }
}
