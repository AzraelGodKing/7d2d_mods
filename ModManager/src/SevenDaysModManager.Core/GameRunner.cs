using System.Diagnostics;

namespace SevenDaysModManager.Core;

/// <summary>What Play did: whether EAC is on and why, and the save backup made first.</summary>
public sealed record PlayResult(bool Eac, string Why, string? SaveBackup);

/// <summary>Starts the game and tells whether it is running.</summary>
public static class GameRunner
{
    public static bool IsRunning()
    {
        foreach (var name in SevenDays.ProcessNames)
        {
            var ps = Process.GetProcessesByName(name);
            var running = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            if (running) return true;
        }
        return false;
    }

    /// <summary>
    /// Starts the game with the enabled mods of <paramref name="scan"/>: without EAC when the setting (or, on Auto, a
    /// code mod) says so, after backing up the newest save when the setting asks for it.
    /// </summary>
    public static PlayResult Play(LibraryScan scan, AppSettings s, string? steamFolder)
    {
        var code = scan.Mods.Where(m => m.Enabled && m.HasCode && !m.Locked).ToList();
        var eac = SevenDays.UsesEac(s.Eac, code.Count > 0);
        var why = s.Eac switch
        {
            EacMode.On => "EasyAntiCheat on (set in Options)",
            EacMode.Off => "EasyAntiCheat off (set in Options)",
            _ when code.Count > 0 => $"EasyAntiCheat off: {code.Count} enabled mod{(code.Count == 1 ? " has" : "s have")} code",
            _ => "EasyAntiCheat on: no enabled mod has code",
        };
        string? backup = null;
        if (s.BackupSaveBeforePlay && Saves.List(scan.Install).FirstOrDefault() is { } newest)
            backup = Saves.Backup(newest, AppSettings.SaveBackupFolder);
        Start(SevenDays.Launch(scan.Install, eac, s.LaunchOptions, steamFolder));
        return new PlayResult(eac, why, backup);
    }

    public static void Start(GameLaunch launch)
    {
        var psi = new ProcessStartInfo(launch.Exe) { UseShellExecute = launch.ThroughShell };
        foreach (var a in launch.Args) psi.ArgumentList.Add(a);
        if (launch.WorkingDirectory != null) psi.WorkingDirectory = launch.WorkingDirectory;
        if (!launch.ThroughShell && !File.Exists(launch.Exe)) throw new FileNotFoundException($"{Path.GetFileName(launch.Exe)} was not found in {Path.GetDirectoryName(launch.Exe)}.", launch.Exe);
        Process.Start(psi)?.Dispose();
    }

    /// <summary>Opens a folder, file or link the way Windows does.</summary>
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    /// <summary>Asks Steam to check the game's files (and put back missing ones such as 0_TFP_Harmony).</summary>
    public static void VerifyGameFiles() => Open("steam://validate/" + SevenDays.AppId);
}
