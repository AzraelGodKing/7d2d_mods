using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>
/// The game's log, read for problems: errors and warnings grouped by message, each with the mod most likely at fault
/// and its first occurrence with the stack trace. Following it re-reads the newest log every few seconds while the
/// game runs.
/// </summary>
sealed class LogForm : Form
{
    readonly LibraryScan _scan;
    readonly Action<string> _selectMod;
    readonly ListView _groups = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, AccessibleName = "Errors and warnings in the log" };
    readonly TextBox _detail = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
    readonly Label _status = Muted();
    readonly CheckBox _follow = new() { Text = "Follow while the game runs", AutoSize = true };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    string? _path;
    long _lastSize = -1;

    public LogForm(LibraryScan scan, bool follow, Action<string> selectMod)
    {
        _scan = scan;
        _selectMod = selectMod;
        Text = "Game log - 7 Days Mod Manager";
        Font = Theme.BodyFont;
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterParent;
        foreach (var (name, w) in new[] { ("", 80), ("Count", 60), ("Mod", 180), ("Message", 760) }) _groups.Columns.Add(name, (int)(w * Theme.TextScale));
        _groups.SelectedIndexChanged += (_, _) => _detail.Text = _groups.SelectedItems.Count > 0 && _groups.SelectedItems[0].Tag is LogGroup g ? Detail(g) : "";
        _groups.DoubleClick += (_, _) => { if (_groups.SelectedItems.Count > 0 && _groups.SelectedItems[0].Tag is LogGroup { ModId: { } id }) _selectMod(id); };
        Tip(_groups, "Double-click a row to pick its mod in the main window");

        var refresh = SmallBtn("Refresh", (_, _) => Read(force: true));
        var open = SmallBtn("Open the log file", (_, _) => { if (_path != null) Ui.Open(this, _path); });
        var copy = SmallBtn("Copy", (_, _) => { if (_detail.TextLength > 0) Clipboard.SetText(_detail.Text); });
        _follow.Checked = follow;
        _follow.CheckedChanged += (_, _) => _timer.Enabled = _follow.Checked;
        _timer.Tick += (_, _) => Read(force: false);
        _timer.Enabled = follow;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Margin = Padding.Empty };
        split.Panel1.Controls.Add(_groups);
        split.Panel2.Controls.Add(_detail);
        var placed = false;
        split.SizeChanged += (_, _) =>
        {
            if (placed || split.Height < 200) return;
            placed = true;
            try { split.SplitterDistance = (int)(split.Height * 0.55); } catch (ArgumentException) { } catch (InvalidOperationException) { }
        };
        var card = Card("Errors and warnings", new Control[] { _status, _follow, refresh, open, copy }, split);
        card.Dock = DockStyle.Fill;
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        host.Controls.Add(card);
        Controls.Add(host);
        Theme.Apply(this);
        FormClosed += (_, _) => _timer.Dispose();
        Shown += (_, _) => Read(force: true);
    }

    readonly ToolTip _tip = new();
    void Tip(Control c, string t) => _tip.SetToolTip(c, t);

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
            groups = LogAnalysis.Analyze(lines, _scan.Mods.Where(m => m.Enabled).ToList());
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
        var errors = groups.Where(g => g.Severity == Severity.Error).Sum(g => g.Count);
        var warnings = groups.Where(g => g.Severity == Severity.Warning).Sum(g => g.Count);
        _status.Text = $"{Path.GetFileName(path)}: {errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}";
        if (groups.Count == 0) _detail.Text = "No errors or warnings in this log.";
    }

    string Detail(LogGroup g)
    {
        var mod = g.ModId == null ? null : _scan.ById(g.ModId);
        var head = mod != null ? $"Most likely {mod.DisplayName} ({mod.FolderName}): {g.Why}.\r\n" : "No mod points at this one.\r\n";
        return head + $"Seen {g.Count} time{(g.Count == 1 ? "" : "s")}, first on line {g.FirstLine}.\r\n\r\n" + g.Detail.Replace("\n", "\r\n");
    }
}
