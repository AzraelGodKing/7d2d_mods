namespace SevenDaysModManager.Core;

/// <summary>
/// "Find the broken mod for me": halves the suspects each round. Each round enables only half of them (and the game's
/// own mods); the player starts the game and says whether the problem is still there. That half, or the other, are the
/// suspects for the next round, until one is left. The mods enabled before it started are put back at the end.
/// </summary>
public sealed class Bisect
{
    /// <summary>The ids that were enabled when it started, put back by the caller when it ends.</summary>
    public IReadOnlyList<string> Original { get; }
    public List<string> Suspects { get; private set; }
    /// <summary>The suspects enabled in this round.</summary>
    public List<string> Testing { get; private set; } = new();
    public int Round { get; private set; }

    public Bisect(IEnumerable<string> originalEnabled, IEnumerable<string> suspects)
    {
        Original = originalEnabled.ToList();
        Suspects = suspects.Distinct().ToList();
        if (Suspects.Count < 2) throw new InvalidOperationException("Finding the broken mod needs at least two enabled mods.");
        Next();
    }

    public bool Done => Suspects.Count == 1;
    public string? Culprit => Done ? Suspects[0] : null;

    /// <summary>About how many more rounds it takes.</summary>
    public int RoundsLeft => Done ? 0 : (int)Math.Ceiling(Math.Log2(Suspects.Count));

    /// <summary>The ids to enable for this round: the half being tested, and <paramref name="alwaysOn"/> (the game's own).</summary>
    public HashSet<string> EnabledThisRound(IEnumerable<string> alwaysOn) => new(Testing.Concat(alwaysOn));

    /// <summary>The player's answer for this round: was the problem there with only <see cref="Testing"/> enabled?</summary>
    public void Answer(bool problemHappened)
    {
        if (Done) return;
        Suspects = problemHappened ? Testing : Suspects.Except(Testing).ToList();
        Next();
    }

    void Next()
    {
        if (Done) { Testing = new List<string>(Suspects); return; }
        Round++;
        Testing = Suspects.Take(Suspects.Count / 2).ToList();
    }
}
