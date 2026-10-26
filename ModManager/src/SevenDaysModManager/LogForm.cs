using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>
/// The game's log, read for problems. Two views: errors and warnings grouped by message, each with the mod most likely
/// at fault and its first occurrence with the stack trace; and every line, filtered by mod, level, XML patch problems
/// and text, with XML patch failures highlighted. Following it re-reads the newest log every few seconds while the game runs.
/// </summary>
sealed class LogForm : Form
{
    readonly LibraryScan _scan;
    readonly Action<string> _selectMod;
    readonly ListView _groups = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, AccessibleName = "Errors and warnings in the log" };
    readonly ListView _lines = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, VirtualMode = true, Visible = false, AccessibleName = "Lines of the log" };
    readonly TextBox _detail = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
    readonly Label _status = Muted();
    readonly CheckBox _follow = new() { Text = "Follow while the game runs", AutoSize = true };
    readonly ComboBox _view = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, AccessibleName = "View" };
    readonly ComboBox _mod = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "Mod" };
    readonly ComboBox _level = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, AccessibleName = "Level" };
    readonly CheckBox _xmlOnly = new() { Text = "XML patch problems only", AutoSize = true };
    readonly TextBox _find = new() { Width = 200, PlaceholderText = "Find text", AccessibleName = "Find text" };
    readonly TableLayoutPanel _filters;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    readonly List<InstalledMod> _mods;
    List<LogLine> _all = new();
    List<LogLine> _shown = new();
    string? _path;
    long _lastSize = -1;

    /// <param name="modFilter">Opens on every line, filtered to this mod (an <see cref="InstalledMod.Id"/>).</param>
    public LogForm(LibraryScan scan, bool follow, Action<string> selectMod, string? modFilter = null)
    {
        _scan = scan;
        _selectMod = selectMod;
        _mods = scan.Mods.Where(m => m.Enabled && !m.Locked).OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Text = "Game log - 7 Days Mod Manager";
        Font = Theme.BodyFont;
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterParent;

        foreach (var (name, w) in new[] { ("", 80), ("Count", 60), ("Mod", 180), ("Message", 760) }) _groups.Columns.Add(name, (int)(w * Theme.TextScale));
        _groups.SelectedIndexChanged += (_, _) => _detail.Text = _groups.SelectedItems.Count > 0 && _groups.SelectedItems[0].Tag is LogGroup g ? Detail(g) : "";
        _groups.DoubleClick += (_, _) => { if (_groups.SelectedItems.Count > 0 && _groups.SelectedItems[0].Tag is LogGroup { ModId: { } id }) _selectMod(id); };
        Tip(_groups, "Double-click a row to pick its mod in the main window");

        foreach (var (name, w) in new[] { ("Line", 70), ("Mod", 170), ("Text", 1400) }) _lines.Columns.Add(name, (int)(w * Theme.TextScale));
        _lines.RetrieveVirtualItem += (_, e) => e.Item = LineItem(_shown[e.ItemIndex]);
        _lines.SelectedIndexChanged += (_, _) => _detail.Text = _lines.SelectedIndices.Count > 0 ? EntryText(_shown[_lines.SelectedIndices[0]]) : "";
        _lines.DoubleClick += (_, _) => { if (_lines.SelectedIndices.Count > 0 && _shown[_lines.SelectedIndices[0]].ModId is { } id) _selectMod(id); };
        Tip(_lines, "Lines about XML patching are highlighted. Double-click a line to pick its mod in the main window");

        _view.Items.AddRange(new object[] { "Errors and warnings, grouped", "Every line" });
        _mod.Items.Add("All mods");
        foreach (var m in _mods) _mod.Items.Add(m.DisplayName);
        _level.Items.AddRange(new object[] { "Every level", "Warnings and errors", "Errors only" });
        _mod.SelectedIndex = Math.Max(0, modFilter == null ? 0 : _mods.FindIndex(m => m.Id == modFilter) + 1);
        _level.SelectedIndex = 0;
        _mod.SelectedIndexChanged += (_, _) => ApplyFilter();
        _level.SelectedIndexChanged += (_, _) => ApplyFilter();
        _xmlOnly.CheckedChanged += (_, _) => ApplyFilter();
        var findTimer = new System.Windows.Forms.Timer { Interval = 300 };
        findTimer.Tick += (_, _) => { findTimer.Stop(); ApplyFilter(); };
        _find.TextChanged += (_, _) => { findTimer.Stop(); findTimer.Start(); };
        _filters = Row(Lbl("Mod"), _mod, Lbl("Level"), _level, _xmlOnly, _find);
        _filters.Visible = false;
        _view.SelectedIndexChanged += (_, _) => ShowView();

        var refresh = SmallBtn("Refresh", (_, _) => Read(force: true));
        var open = SmallBtn("Open the log file", (_, _) => { if (_path != null) Ui.Open(this, _path); });
        var copy = SmallBtn("Copy", (_, _) => Copy());
        Tip(copy, "Copy the selected entry; in Every line, the selected lines (or every line shown)");
        _follow.Checked = follow;
        _follow.CheckedChanged += (_, _) => _timer.Enabled = _follow.Checked;
        _timer.Tick += (_, _) => Read(force: false);
        _timer.Enabled = follow;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Margin = Padding.Empty };
        split.Panel1.Controls.Add(_lines);
        split.Panel1.Controls.Add(_groups);
        split.Panel2.Controls.Add(_detail);
        var placed = false;
        split.SizeChanged += (_, _) =>
        {
            if (placed || split.Height < 200) return;
            placed = true;
            try { split.SplitterDistance = (int)(split.Height * 0.62); } catch (ArgumentException) { } catch (InvalidOperationException) { }
        };
        var card = Card("Game log", new Control[] { _status, _view, _follow, refresh, open, copy }, _filters, split);
        card.Dock = DockStyle.Fill;
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        host.Controls.Add(card);
        Controls.Add(host);
        Theme.Apply(this);
        _view.SelectedIndex = modFilter != null ? 1 : 0;
        FormClosed += (_, _) => { _timer.Dispose(); findTimer.Dispose(); };
        Shown += (_, _) => Read(force: true);
    }

    readonly ToolTip _tip = new();
    void Tip(Control c, string t) => _tip.SetToolTip(c, t);

    bool EveryLine => _view.SelectedIndex == 1;

    void ShowView()
    {
        _groups.Visible = !EveryLine;
        _lines.Visible = _filters.Visible = EveryLine;
        _detail.Text = "";
        if (EveryLine) ApplyFilter();
        UpdateStatus();
    }

    void Read(bool force)
    {
        var path = SevenDays.LatestLog(_scan.Install);
        if (path == null) { _status.Text = "Waiting for the game to write its log..."; return; }
        long size;
        try { size = new FileInfo(path).Length; } catch (IOException) { return; }
        if (!force && path == _path && size == _lastSize) return;
        _path = path;
        _lastSize = size;
        List<LogGroup> groups;
        try
        {
            var lines = LogAnalysis.ReadLines(path);
            var enabled = _scan.Mods.Where(m => m.Enabled).ToList();
            groups = LogAnalysis.Analyze(lines, enabled);
            _all = LogAnalysis.Lines(lines, enabled);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = "Couldn't read the log: " + e.Message; return; }

        var selected = _groups.SelectedItems.Count > 0 ? (_groups.SelectedItems[0].Tag as LogGroup)?.Message : null;
        _groups.BeginUpdate();
        _groups.Items.Clear();
        foreach (var g in groups)
        {
            var mod = g.ModId == null ? "" : _scan.ById(g.ModId)?.DisplayName ?? g.ModId;
            var item = new ListViewItem(new[] { g.Severity == Severity.Error ? "ERROR" : "WARNING", g.Count.ToString(), mod, g.Message })
            {
                Tag = g, UseItemStyleForSubItems = true, ToolTipText = g.Why == null ? g.Message : $"{mod}: {g.Why}",
                ForeColor = Theme.Of(g.Severity == Severity.Error ? Tone.Error : Tone.Warn),
            };
            _groups.Items.Add(item);
            if (g.Message == selected) item.Selected = true;
        }
        _groups.EndUpdate();
        if (groups.Count == 0 && !EveryLine) _detail.Text = "No errors or warnings in this log.";
        _errors = groups.Where(g => g.Severity == Severity.Error).Sum(g => g.Count);
        _warnings = groups.Where(g => g.Severity == Severity.Warning).Sum(g => g.Count);
        ApplyFilter(follow: true);
    }

    int _errors, _warnings;

    void UpdateStatus()
    {
        if (_path == null) return;
        var head = $"{Path.GetFileName(_path)}: {_errors} error{(_errors == 1 ? "" : "s")}, {_warnings} warning{(_warnings == 1 ? "" : "s")}";
        _status.Text = EveryLine ? $"{head}  ·  {_shown.Count:N0} of {_all.Count:N0} lines" : head;
    }

    /// <param name="follow">After a re-read: stay at the end when the view was at the end.</param>
    void ApplyFilter(bool follow = false)
    {
        if (!EveryLine) { UpdateStatus(); return; }
        var atEnd = _shown.Count == 0 || _lines.TopItem == null || _lines.TopItem.Index + _lines.ClientSize.Height / Math.Max(1, _lines.Font.Height + 4) >= _shown.Count - 1;
        var modId = _mod.SelectedIndex > 0 ? _mods[_mod.SelectedIndex - 1].Id : null;
        var level = _level.SelectedIndex switch { 1 => LogLevel.Warning, 2 => LogLevel.Error, _ => LogLevel.Info };
        _shown = LogAnalysis.Filter(_all, modId, level, _find.Text, _xmlOnly.Checked);
        _lines.BeginUpdate();
        _lines.SelectedIndices.Clear();
        _lines.VirtualListSize = _shown.Count;
        _lines.EndUpdate();
        _lines.Invalidate();
        if (follow && atEnd && _shown.Count > 0) _lines.EnsureVisible(_shown.Count - 1);
        UpdateStatus();
    }

    ListViewItem LineItem(LogLine l)
    {
        var mod = l.ModId == null ? "" : _scan.ById(l.ModId)?.DisplayName ?? "";
        var item = new ListViewItem(new[] { l.Number.ToString(), l.Start ? mod : "", l.Text }) { UseItemStyleForSubItems = true };
        item.ForeColor = l.Level switch { LogLevel.Error => Theme.Of(Tone.Error), LogLevel.Warning => Theme.Of(Tone.Warn), _ => Theme.Current.Text };
        if (l.XmlPatch) item.BackColor = Theme.Blend(Theme.Current.Input, Theme.Of(Tone.Error), 0.18);
        return item;
    }

    /// <summary>The whole entry a line belongs to: its first line and the stack trace after it.</summary>
    string EntryText(LogLine line)
    {
        var i = line.Number - 1;
        while (i > 0 && !_all[i].Start) i--;
        var lines = new List<string> { _all[i].Text };
        for (var j = i + 1; j < _all.Count && !_all[j].Start; j++) lines.Add(_all[j].Text);
        var mod = line.ModId == null ? null : _scan.ById(line.ModId);
        var head = mod != null ? $"Points at {mod.DisplayName} ({mod.FolderName}).\r\n" : "";
        if (line.XmlPatch) head += "An XML patch problem: a mod's xpath that matched nothing, or XML the game couldn't read.\r\n";
        return head + (head.Length > 0 ? "\r\n" : "") + $"Line {i + 1}:\r\n" + string.Join("\r\n", lines);
    }

    void Copy()
    {
        string text;
        if (EveryLine && _lines.SelectedIndices.Count == 0) text = string.Join("\r\n", _shown.Select(l => l.Text));
        else if (EveryLine) text = string.Join("\r\n", _lines.SelectedIndices.Cast<int>().OrderBy(i => i).Select(i => _shown[i].Text));
        else text = _detail.Text;
        if (text.Length > 0) Clipboard.SetText(text);
    }

    string Detail(LogGroup g)
    {
        var mod = g.ModId == null ? null : _scan.ById(g.ModId);
        var head = mod != null ? $"Most likely {mod.DisplayName} ({mod.FolderName}): {g.Why}.\r\n" : "No mod points at this one.\r\n";
        return head + $"Seen {g.Count} time{(g.Count == 1 ? "" : "s")}, first on line {g.FirstLine}.\r\n\r\n" + g.Detail.Replace("\n", "\r\n");
    }
}
