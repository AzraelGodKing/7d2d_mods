namespace SevenDaysModManager.Core;

public enum Severity { Info, Warning, Error }

/// <summary>What a problem's fix button does.</summary>
public enum ProblemFix { None, MoveNestedUp, VerifyGameFiles }

/// <summary>One thing wrong with the mods as they would be saved.</summary>
/// <param name="ModId">The mod it is about (an <see cref="InstalledMod.Id"/>), or null for the whole setup.</param>
public sealed record ModProblem(Severity Severity, string? ModId, string Text, ProblemFix Fix = ProblemFix.None);

/// <summary>Checks the mods that would be enabled for what stops them loading or makes the game complain.</summary>
public static class Problems
{
    /// <summary>The problems with <paramref name="enabledIds"/>; with an <paramref name="analysis"/>, also XML conflicts, missing requirements and old game versions.</summary>
    public static List<ModProblem> Check(LibraryScan scan, IReadOnlySet<string> enabledIds, EacMode eac, ModAnalysis? analysis = null)
    {
        var list = new List<ModProblem>();
        var enabled = scan.Mods.Where(m => enabledIds.Contains(m.Id)).ToList();

        foreach (var m in enabled)
        {
            if (m.NestedFolder != null)
                list.Add(new ModProblem(Severity.Error, m.Id, $"{m.DisplayName}: ModInfo.xml is one folder too deep ({Path.GetRelativePath(m.Folder, m.NestedFolder)}), so the game won't load it. Fix moves that folder up.", ProblemFix.MoveNestedUp));
            else if (m.Info == null)
                list.Add(new ModProblem(m.Error?.StartsWith("No ModInfo") == true ? Severity.Warning : Severity.Error, m.Id, $"{m.FolderName}: {m.Error}"));
            else if (m.Info.Format == 1)
                list.Add(new ModProblem(Severity.Warning, m.Id, $"{m.DisplayName}: its ModInfo.xml is the old format (before Alpha 21). It may be made for an older game version."));
        }

        // The game loads only the first mod with a given Name, in load order.
        foreach (var g in scan.LoadOrder(enabledIds).Where(m => m.Info != null && m.NestedFolder == null).GroupBy(m => m.Info!.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var first = g.First();
            foreach (var other in g.Skip(1))
                list.Add(new ModProblem(Severity.Error, other.Id, $"{other.DisplayName} ({Where(other)}) has the same Name as {first.FolderName} ({Where(first)}). The game loads only {first.FolderName}; disable or remove one."));
        }

        var code = enabled.Where(m => m.HasCode && !m.Locked).ToList();
        if (code.Count > 0)
        {
            if (eac == EacMode.On)
                list.Add(new ModProblem(Severity.Error, null, $"{code.Count} enabled mod{(code.Count == 1 ? " has" : "s have")} code ({string.Join(", ", code.Take(4).Select(m => m.DisplayName))}{(code.Count > 4 ? ", ..." : "")}), but Play is set to use EasyAntiCheat, which blocks them. Set EAC to Auto or Off in Options."));
            if (!scan.Mods.Any(m => m.Locked && m.Enabled && m.FolderName.Contains("Harmony", StringComparison.OrdinalIgnoreCase)))
                list.Add(new ModProblem(Severity.Error, null, "The game's own 0_TFP_Harmony mod is missing, and code mods need it. Verify the game files in Steam to get it back.", ProblemFix.VerifyGameFiles));
        }
        if (analysis != null) list.AddRange(analysis.Check(scan, enabledIds));
        return list.OrderByDescending(p => p.Severity).ToList();
    }

    static string Where(InstalledMod m) => m.Location == ModLocation.Game ? "game Mods folder" : "AppData Mods folder";
}
