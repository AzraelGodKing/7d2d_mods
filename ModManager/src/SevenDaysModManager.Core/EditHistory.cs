namespace SevenDaysModManager.Core;

/// <summary>Undo and redo for the ticks in the mod list (Ctrl+Z, Ctrl+Y): each step is the whole set of enabled ids.</summary>
public sealed class EditHistory
{
    readonly Stack<HashSet<string>> _undo = new(), _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Call before a change, with the state it changes from.</summary>
    public void Push(IEnumerable<string> before)
    {
        _undo.Push(new HashSet<string>(before));
        _redo.Clear();
    }

    public HashSet<string>? Undo(IEnumerable<string> current)
    {
        if (!CanUndo) return null;
        _redo.Push(new HashSet<string>(current));
        return _undo.Pop();
    }

    public HashSet<string>? Redo(IEnumerable<string> current)
    {
        if (!CanRedo) return null;
        _undo.Push(new HashSet<string>(current));
        return _redo.Pop();
    }

    public void Clear() { _undo.Clear(); _redo.Clear(); }
}
