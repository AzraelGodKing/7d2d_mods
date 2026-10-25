using Microsoft.VisualBasic.FileIO;
using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>
/// The MODS tab: every mod in the game's Mods folder and %AppData%\7DaysToDie\Mods, enabled or disabled, with the game
/// and modlists above and problems below. Ticks are only an edit until Save to game moves the folders.
/// </summary>
public sealed partial class MainForm
{
    static readonly string[] Columns = { "Name", "Version", "Author", "Side", "Folder", "Where", "Code", "Load order", "Note" };
    const int NoteColumn = 8, LoadOrderColumn = 7;

    readonly Label _gameText = new() { AutoSize = true, UseMnemonic = false, Tag = "fill" };
    readonly Label _note = new() { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(1100, 0), Visible = false };
    readonly ComboBox _modlist = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, AccessibleName = "Modlist" };
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, CheckBoxes = true, AccessibleName = "Installed mods; ticked mods are enabled" };
    readonly TextBox _filter = new() { Width = 170, PlaceholderText = "Filter mods  (Ctrl+F)", AccessibleName = "Filter mods" };
    readonly Label _count = Muted();
    readonly ListView _problems = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.None, AccessibleName = "Problems with the enabled mods" };
    readonly Label _problemCount = Muted();
    readonly ModDetailsPanel _details = new() { Dock = DockStyle.Fill };
    readonly FlowLayoutPanel _bisectBar = new() { AutoSize = true, WrapContents = true, Visible = false, Margin = Padding.Empty };
    readonly Label _bisectText = new() { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(900, 0) };
    Button _save = null!, _undo = null!, _play = null!, _fix = null!;

    readonly SteamLibraries _steam = SteamLibraries.Find();
    LibraryScan? _scan;
    /// <summary>The mods ticked in the list (their ids), as edited here; Save to game makes the folders match.</summary>
    HashSet<string> _enabled = new();
    readonly EditHistory _history = new();
    List<ModProblem> _found = new();
    bool _dirty, _filling, _busy;
    Bisect? _bisect;

    ModlistStore Store => new(AppSettings.ModlistFolder);

    Control BuildModsPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 12, 14, 12) };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

        // ---- Game
        var pick = Btn("Game folder...", (_, _) => PickGameFolder());
        var rescan = SmallBtn("Rescan", async (_, _) => await ScanAsync());
        Button tools = null!;
        tools = SmallBtn("Tools ▾", (_, _) => DropDown(tools, ToolsMenu()));
        _play = Btn("Play", async (_, _) => await PlayAsync(watch: false)).Role(ThemeRole.Accent);
        var test = Btn("Test", async (_, _) => await PlayAsync(watch: true));
        var readLog = Btn("Read the log", (_, _) => OpenLog(watch: false));
        var saves = Btn("Saves...", (_, _) => { if (_scan != null) new SavesForm(_scan.Install).Show(this); });
        var switchTo = Btn("Switch to it", async (_, _) => await SwitchModlistAsync());
        var saveAs = Btn("Save as...", (_, _) => SaveModlistAs());
        Button more = null!;
        more = Btn("More ▾", (_, _) => DropDown(more, ModlistMenu()));
        Tip(pick, "Pick the game's install folder by hand, when it isn't found through Steam");
        Tip(rescan, "Read every Mods folder again (F5)");
        Tip(tools, "Find the broken mod, install from a zip, verify the game files, backups");
        Tip(_play, "Start the game. EasyAntiCheat is turned off when an enabled mod has code (see Options)");
        Tip(test, "Start the game and follow its log: errors and warnings are grouped as they come, with the mod most likely at fault");
        Tip(readLog, "Read the game's last log: errors and warnings grouped, with the mod most likely at fault");
        Tip(saves, "Your saved games, and backups of them");
        Tip(switchTo, "Enable exactly this modlist's mods (moves the folders now)");
        Tip(saveAs, "Save the ticked mods as a named modlist");
        Tip(more, "Import or export a modlist, copy or paste a share code, delete a modlist");
        _bisectBar.Controls.Add(_bisectText);
        _bisectBar.Controls.Add(SmallBtn("Problem still there", async (_, _) => await BisectAnswerAsync(true)));
        _bisectBar.Controls.Add(SmallBtn("Problem gone", async (_, _) => await BisectAnswerAsync(false)));
        _bisectBar.Controls.Add(SmallBtn("Stop", async (_, _) => await BisectStopAsync(null)));
        page.Controls.Add(Card("Game", new Control[] { tools, rescan },
            Row(_gameText, pick),
            Row(_play, test, readLog, saves),
            Row(Lbl("Modlist"), _modlist, switchTo, saveAs, more),
            Row(_note), Row(_bisectBar)), 0, 0);

        // ---- Mods
        for (var i = 0; i < Columns.Length; i++)
            _list.Columns.Add(Columns[i], (int)(new[] { 250, 80, 130, 80, 170, 90, 60, 80, 200 }[i] * Theme.TextScale));
        _list.ItemCheck += (_, e) =>
        {
            if (_filling) return;
            if (_list.Items[e.Index].Tag is InstalledMod { Locked: true }) e.NewValue = CheckState.Checked;
        };
        _list.ItemChecked += (_, e) => Ticked(e.Item);
        _list.SelectedIndexChanged += (_, _) => ShowDetails();
        _list.ColumnClick += (_, e) => { _sortColumn = e.Column == _sortColumn ? -1 : e.Column; FillList(); };
        _list.ContextMenuStrip = new ContextMenuStrip();
        _list.ContextMenuStrip.Opening += (_, e) => { e.Cancel = !FillRowMenu(_list.ContextMenuStrip); };
        _list.DoubleClick += (_, _) => { if (Selected() is { } m) Ui.Open(this, m.Folder); };
        _filter.TextChanged += (_, _) => FillList();
        _filter.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { _filter.Text = ""; e.SuppressKeyPress = true; } };
        var install = SmallBtn("Install...", (_, _) => PickArchive());
        _undo = SmallBtn("Undo changes", (_, _) => { if (_scan == null) return; _history.Push(_enabled); SetEnabled(EnabledOnDisk()); });
        _save = SmallBtn("Save to game", async (_, _) => await SaveToGameAsync()).Role(ThemeRole.Primary);
        Tip(install, "Install a mod from a .zip or a folder (Ctrl+O). You can also drop it onto the window");
        Tip(_undo, "Go back to the mods as they are in the game folders (Ctrl+Z steps back one change at a time)");
        Tip(_save, "Move the folders: ticked mods into Mods, the others into Mods-disabled (Ctrl+S)");
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, Margin = Padding.Empty };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_details);
        var placed = false;
        split.SizeChanged += (_, _) =>
        {
            // Once, when the window has its real size: the details panel keeps its width and the list takes the rest.
            if (placed || split.Width < 600) return;
            placed = true;
            try { split.SplitterDistance = split.Width - (int)(340 * Theme.TextScale); } catch (ArgumentException) { } catch (InvalidOperationException) { }
        };
        _details.NoteChanged += SetNote;
        _details.OpenFolder += m => Ui.Open(this, m.Folder);
        _details.Uninstall += async m => await UninstallAsync(m);
        _details.EditSettings += EditSettings;
        var modsCard = Card("Mods", new Control[] { _count, install, _undo, _save, _filter }, split);
        modsCard.Dock = DockStyle.Fill;
        page.Controls.Add(modsCard, 0, 1);

        // ---- Problems
        _problems.Columns.Add("", (int)(80 * Theme.TextScale));
        _problems.Columns.Add("", (int)(1000 * Theme.TextScale));
        _problems.DoubleClick += (_, _) => SelectProblemMod();
        _problems.SelectedIndexChanged += (_, _) => _fix.Enabled = SelectedProblem()?.Fix is ProblemFix.MoveNestedUp or ProblemFix.VerifyGameFiles;
        _fix = SmallBtn("Fix", async (_, _) => await FixAsync());
        _fix.Enabled = false;
        Tip(_fix, "Fix the selected problem: move a mod's folder up a level, or ask Steam to verify the game files");
        var problemsCard = Card("Problems", new Control[] { _problemCount, _fix }, _problems);
        problemsCard.Dock = DockStyle.Fill;
        page.Controls.Add(problemsCard, 0, 2);

        _modlist.SelectedIndexChanged += (_, _) => { if (!_filling && _modlist.SelectedItem is string n) { _s.Modlist = n; SaveSettings(); } };
        return page;
    }

    int _sortColumn = -1;

    // ------------------------------------------------------------ scanning

    async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        UseWaitCursor = true;
        try
        {
            var install = SevenDays.Detect(_steam, _s.GameFolder, _s.UserDataFolder);
            if (install == null)
            {
                _scan = null;
                _gameText.Text = "7 Days to Die was not found through Steam. Press Game folder... and pick its install folder.";
                _gameText.ForeColor = Theme.Of(Tone.Warn);
                _enabled.Clear();
                FillList();
                return;
            }
            _scan = await Task.Run(() => ModLibrary.Scan(install));
            _sides.Clear();
            _gameText.Text = $"{install.GameFolder}    ·    data: {install.UserDataFolder}";
            _gameText.ForeColor = Theme.Of(Tone.Normal);
            _history.Clear();
            SetEnabled(EnabledOnDisk(), dirty: false);
            FillModlists();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, "Couldn't read the Mods folders: " + e.Message); }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
        }
    }

    HashSet<string> EnabledOnDisk() => _scan?.Mods.Where(m => m.Enabled).Select(m => m.Id).ToHashSet() ?? new();

    void SetEnabled(HashSet<string> ids, bool? dirty = null)
    {
        _enabled = ids;
        if (_scan != null) foreach (var m in _scan.Mods) if (m.Locked && m.Enabled) _enabled.Add(m.Id);
        _dirty = dirty ?? !_enabled.SetEquals(EnabledOnDisk());
        FillList();
    }

    void PickGameFolder()
    {
        using var d = new FolderBrowserDialog { Description = "Pick the 7 Days to Die install folder (the one with 7DaysToDie.exe)", UseDescriptionForTitle = true, SelectedPath = _scan?.Install.GameFolder ?? "" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (!SevenDays.LooksLikeGame(d.SelectedPath) && !Confirm(this, $"{d.SelectedPath} has no 7DaysToDie.exe. Use it anyway?", MessageBoxIcon.Warning)) return;
        _s.GameFolder = d.SelectedPath;
        SaveSettings();
        _ = ScanAsync();
    }

    // ------------------------------------------------------------ the list

    void FillList()
    {
        _filling = true;
        _list.BeginUpdate();
        try
        {
            var selected = Selected()?.Id;
            _list.Items.Clear();
            if (_scan == null) { _count.Text = ""; return; }
            var order = _scan.LoadOrder(_enabled).Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i + 1);
            var f = _filter.Text.Trim();
            IEnumerable<InstalledMod> mods = _scan.Mods.Where(m => f.Length == 0
                || m.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) || m.FolderName.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (m.Info?.Author.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) || Note(m).Contains(f, StringComparison.OrdinalIgnoreCase));
            var rows = mods.Select(m => (Mod: m, Cells: new[]
            {
                m.DisplayName, m.Info?.Version ?? "", m.Info?.Author ?? "", m.Locked ? "" : Side(m).Short, m.FolderName,
                m.Location == ModLocation.Game ? "Game" : "AppData", m.HasCode ? "Yes" : "",
                order.TryGetValue(m.Id, out var o) ? o.ToString() : "", Note(m),
            })).ToList();
            rows = _sortColumn switch
            {
                < 0 => rows.OrderByDescending(r => r.Mod.Locked).ThenBy(r => r.Mod.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
                LoadOrderColumn => rows.OrderBy(r => r.Cells[LoadOrderColumn].Length == 0 ? int.MaxValue : int.Parse(r.Cells[LoadOrderColumn])).ToList(),
                var c => rows.OrderBy(r => r.Cells[c], StringComparer.OrdinalIgnoreCase).ToList(),
            };
            var problemIds = _found.Where(p => p.ModId != null).GroupBy(p => p.ModId!).ToDictionary(g => g.Key, g => g.Max(p => p.Severity));
            foreach (var (m, cells) in rows)
            {
                var item = new ListViewItem(cells) { Tag = m, Checked = _enabled.Contains(m.Id), UseItemStyleForSubItems = true };
                item.ToolTipText = m.Locked ? "Part of the game; always on" : m.Error ?? $"{Side(m).Label}: {Side(m).Why}";
                if (TagColor(_s.Colours.GetValueOrDefault(m.Key)) is { } tag) item.BackColor = Theme.Blend(Theme.Current.Input, tag, 0.28);
                if (problemIds.TryGetValue(m.Id, out var sev)) item.ForeColor = Theme.Of(sev == Severity.Error ? Tone.Error : Tone.Warn);
                else if (!_enabled.Contains(m.Id)) item.ForeColor = Theme.Current.Muted;
                _list.Items.Add(item);
                if (m.Id == selected) { item.Selected = true; item.Focused = true; }
            }
        }
        finally
        {
            _list.EndUpdate();
            _filling = false;
        }
        UpdateState();
    }

    void Ticked(ListViewItem item)
    {
        if (_filling || item.Tag is not InstalledMod m || m.Locked) return;
        if (item.Checked == _enabled.Contains(m.Id)) return;
        _history.Push(_enabled);
        if (item.Checked) _enabled.Add(m.Id); else _enabled.Remove(m.Id);
        _dirty = !_enabled.SetEquals(EnabledOnDisk());
        item.ForeColor = item.Checked ? Theme.Current.Text : Theme.Current.Muted;
        // The load order column changes for every row after this one; cheap enough to refill once typing stops.
        BeginInvoke(FillList);
    }

    void UndoRedo(bool undo)
    {
        var to = undo ? _history.Undo(_enabled) : _history.Redo(_enabled);
        if (to != null) SetEnabled(to);
    }

    /// <summary>Counts, problems, and which buttons can be pressed now.</summary>
    void UpdateState()
    {
        if (_scan == null)
        {
            _found.Clear();
            _problems.Items.Clear();
            _problemCount.Text = "";
            _save.Enabled = _undo.Enabled = _play.Enabled = false;
            ShowDetails();
            return;
        }
        var on = _scan.Mods.Count(m => _enabled.Contains(m.Id) && !m.Locked);
        var code = _scan.Mods.Count(m => _enabled.Contains(m.Id) && !m.Locked && m.HasCode);
        _count.Text = $"{on} of {_scan.Mods.Count(m => !m.Locked)} enabled{(code > 0 ? $", {code} with code" : "")}{(_dirty ? "  ·  not saved" : "")}";
        _save.Enabled = _undo.Enabled = _dirty && _bisect == null;
        _play.Enabled = true;

        var before = _found;
        _found = Problems.Check(_scan, _enabled, _s.Eac);
        _problems.BeginUpdate();
        _problems.Items.Clear();
        foreach (var p in _found)
            _problems.Items.Add(new ListViewItem(new[] { p.Severity.ToString().ToUpperInvariant(), p.Text })
            {
                Tag = p, ToolTipText = p.Text, UseItemStyleForSubItems = true,
                ForeColor = Theme.Of(p.Severity switch { Severity.Error => Tone.Error, Severity.Warning => Tone.Warn, _ => Tone.Info }),
            });
        if (_found.Count == 0) _problems.Items.Add(new ListViewItem(new[] { "OK", "No problems with the enabled mods." }) { ForeColor = Theme.Of(Tone.Ok), UseItemStyleForSubItems = true });
        _problems.EndUpdate();
        var errors = _found.Count(p => p.Severity == Severity.Error);
        _problemCount.Text = _found.Count == 0 ? "" : $"{errors} error{(errors == 1 ? "" : "s")}, {_found.Count - errors} warning{(_found.Count - errors == 1 ? "" : "s")}";
        _fix.Enabled = false;
        // Problem colours on the rows follow the problems; refill once when they change.
        if (!before.Select(p => p.ModId).SequenceEqual(_found.Select(p => p.ModId)) && !_filling) BeginInvoke(FillList);
        ShowDetails();
    }

    InstalledMod? Selected() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as InstalledMod : null;
    ModProblem? SelectedProblem() => _problems.SelectedItems.Count > 0 ? _problems.SelectedItems[0].Tag as ModProblem : null;

    void ShowDetails()
    {
        var m = Selected();
        _details.Show(m, m == null ? null : Note(m), m == null || m.Locked ? null : Side(m), m != null && ModSettingsStore.FilesOf(m.Folder).Count > 0);
    }

    void SelectMod(string id)
    {
        if (_filter.TextLength > 0) _filter.Text = "";
        foreach (ListViewItem item in _list.Items)
            if (item.Tag is InstalledMod m && m.Id == id)
            {
                _list.SelectedItems.Clear();
                item.Selected = item.Focused = true;
                item.EnsureVisible();
                _list.Focus();
                return;
            }
    }

    void SelectProblemMod()
    {
        if (SelectedProblem()?.ModId is { } id) SelectMod(id);
    }

    string Note(InstalledMod m) => _s.Notes.GetValueOrDefault(m.Key) ?? "";

    /// <summary>Each mod's side, worked out once per scan (a guess reads the mod's files).</summary>
    readonly Dictionary<string, SideInfo> _sides = new();

    SideInfo Side(InstalledMod m)
    {
        if (!_sides.TryGetValue(m.Id, out var side))
            _sides[m.Id] = side = ModSides.Of(m, _s.Sides.TryGetValue(m.Key, out var chosen) ? chosen : null);
        return side;
    }

    ModSettingsStore SettingsStore => new(AppSettings.ModSettingsFolder);

    void EditSettings(InstalledMod m)
    {
        if (ModSettingsStore.FilesOf(m.Folder).Count == 0) { Info(this, $"{m.DisplayName} has no settings files."); return; }
        using var f = new SettingsForm(m, SettingsStore);
        f.ShowDialog(this);
        ShowDetails();
    }

    void SetSide(InstalledMod m, ModSide? side)
    {
        if (side is { } s) _s.Sides[m.Key] = s; else _s.Sides.Remove(m.Key);
        SaveSettings();
        _sides.Clear();
        FillList();
    }

    void SetNote(string key, string note)
    {
        if (note.Length == 0) _s.Notes.Remove(key); else _s.Notes[key] = note;
        SaveSettings();
        foreach (ListViewItem item in _list.Items)
            if (item.Tag is InstalledMod m && m.Key == key) item.SubItems[NoteColumn].Text = note;
    }

    bool FillRowMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        if (Selected() is not { } m) return false;
        menu.Items.Add("Open folder", null, (_, _) => Ui.Open(this, m.Folder));
        if (ModSettingsStore.FilesOf(m.Folder).Count > 0) menu.Items.Add("Edit settings...", null, (_, _) => EditSettings(m));
        if (!m.Locked)
        {
            var side = new ToolStripMenuItem("Side");
            var found = ModSides.Of(m, null);
            var chosen = _s.Sides.TryGetValue(m.Key, out var c) ? c : (ModSide?)null;
            side.DropDownItems.Add(new ToolStripMenuItem($"As detected: {found.Label}", null, (_, _) => SetSide(m, null)) { Checked = chosen == null });
            side.DropDownItems.Add(new ToolStripSeparator());
            foreach (var (s, label) in new[] { (ModSide.Server, "Server only"), (ModSide.Client, "Client only"), (ModSide.Both, "Server and clients") })
                side.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => SetSide(m, s)) { Checked = chosen == s });
            menu.Items.Add(side);
        }
        if (m.Info?.Website is { Length: > 0 } site) menu.Items.Add("Open website", null, (_, _) => Ui.Open(this, site.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? site : "https://" + site));
        menu.Items.Add("Note...", null, (_, _) => { if (Ask(this, "Note", $"Your note on {m.DisplayName}:", Note(m), multiline: true) is { } n) { SetNote(m.Key, n); ShowDetails(); } });
        var colour = new ToolStripMenuItem("Colour");
        foreach (var (name, _) in Tags)
            colour.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => { _s.Colours[m.Key] = name; SaveSettings(); FillList(); }) { Checked = _s.Colours.GetValueOrDefault(m.Key) == name });
        colour.DropDownItems.Add(new ToolStripSeparator());
        colour.DropDownItems.Add("None", null, (_, _) => { _s.Colours.Remove(m.Key); SaveSettings(); FillList(); });
        menu.Items.Add(colour);
        if (m.NestedFolder != null) menu.Items.Add("Fix: move its folder up a level", null, async (_, _) => await FixNestedAsync(m));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Uninstall (to the Recycle Bin)...", null, async (_, _) => await UninstallAsync(m)) { Enabled = !m.Locked });
        Theme.StyleMenu(menu);
        return true;
    }

    // ------------------------------------------------------------ saving, installing, removing

    bool CanChangeFiles()
    {
        if (!GameRunner.IsRunning()) return true;
        Error(this, "7 Days to Die is running. Close it first: mods can't be moved while the game has them open.");
        return false;
    }

    async Task SaveToGameAsync()
    {
        if (!_dirty) return;
        SaveToGame();
        await ScanAsync();
    }

    /// <summary>Moves the folders to match the ticks. False when nothing could be saved.</summary>
    bool SaveToGame()
    {
        if (_scan == null || !CanChangeFiles()) return false;
        try
        {
            ModLibrary.Apply(_scan, _enabled);
            _dirty = false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error(this, "Not every mod could be moved: " + e.Message + "\n\nThe list is read again to show where each mod is now.");
            return false;
        }
    }

    void PickArchive()
    {
        using var d = new OpenFileDialog { Title = "Install a mod", Filter = "Mod archives (*.zip)|*.zip|All files|*.*", Multiselect = true };
        if (d.ShowDialog(this) == DialogResult.OK) _ = InstallAsync(d.FileNames);
    }

    async Task InstallAsync(string[] paths)
    {
        if (_scan == null) { Error(this, "Find the game first (Game folder...)."); return; }
        if (!CanChangeFiles()) return;
        if (_dirty && !SaveToGame()) return;
        var lines = new List<string>();
        var target = _s.InstallToUserData ? _scan.Install.UserMods : _scan.Install.GameMods;
        foreach (var p in paths)
        {
            try
            {
                var scan = _scan;
                var done = await Task.Run(() => ModInstaller.Install(p, scan, target, AppSettings.BackupFolder));
                foreach (var d in done)
                {
                    var kept = SettingsStore.Restore(d.Key, d.Folder);
                    lines.Add((d.Replaced != null ? "Updated " : "Installed ") + d.Name + (d.Replaced != null ? " (the old copy is zipped in the mod backups)" : "")
                        + (kept.Count > 0 ? $"; your settings put back in {string.Join(", ", kept)}" : ""));
                }
                await ScanAsync();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
            {
                lines.Add($"{Path.GetFileName(p)}: {e.Message}");
            }
        }
        ShowNote(string.Join("\n", lines), lines.Any(l => l.Contains(": ")) ? Tone.Warn : Tone.Ok);
    }

    async Task UninstallAsync(InstalledMod m)
    {
        if (m.Locked || !CanChangeFiles()) return;
        if (!Confirm(this, $"Uninstall {m.DisplayName}? Its folder goes to the Recycle Bin.\n\n{m.Folder}")) return;
        try { FileSystem.DeleteDirectory(m.Folder, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { Error(this, $"Couldn't remove {m.FolderName}: {e.Message}"); }
        await ScanAsync();
    }

    async Task FixAsync()
    {
        switch (SelectedProblem())
        {
            case { Fix: ProblemFix.MoveNestedUp, ModId: { } id } when _scan?.ById(id) is { } m:
                await FixNestedAsync(m);
                break;
            case { Fix: ProblemFix.VerifyGameFiles }:
                Ui.Open(this, "steam://validate/" + SevenDays.AppId);
                break;
        }
    }

    async Task FixNestedAsync(InstalledMod m)
    {
        if (!CanChangeFiles()) return;
        try
        {
            var now = ModLibrary.FixNested(m, AppSettings.BackupFolder);
            ShowNote($"Moved {Path.GetFileName(now)} up a level: it is now {now}", Tone.Ok);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, e.Message); }
        await ScanAsync();
    }

    void ShowNote(string text, Tone tone)
    {
        _note.Text = text;
        _note.ForeColor = Theme.Of(tone);
        _note.Visible = text.Length > 0;
    }

    // ------------------------------------------------------------ playing and the log

    async Task PlayAsync(bool watch)
    {
        if (_scan == null) { Error(this, "Find the game first (Game folder...)."); return; }
        if (GameRunner.IsRunning()) { Error(this, "7 Days to Die is already running."); return; }
        if (_dirty)
        {
            var r = MessageBox.Show(this, "Save your changes to the game before playing?\n\nNo plays with the mods as they are in the folders now.", "7 Days Mod Manager", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes) { if (!SaveToGame()) return; await ScanAsync(); }
        }
        if (_found.Any(p => p.Severity == Severity.Error) && !Confirm(this, "The enabled mods have problems (see Problems). Play anyway?", MessageBoxIcon.Warning)) return;
        try
        {
            var scan = _scan;
            var r = await Task.Run(() => GameRunner.Play(scan, _s, _steam.SteamFolder));
            ShowNote($"Starting 7 Days to Die. {r.Why}.{(r.SaveBackup != null ? " Newest save backed up to " + r.SaveBackup : "")}", Tone.Ok);
            if (watch) OpenLog(watch: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Error(this, "Couldn't start the game: " + e.Message); }
    }

    void OpenLog(bool watch)
    {
        if (_scan == null) return;
        var log = SevenDays.LatestLog(_scan.Install);
        if (log == null && !watch) { Error(this, $"No game log found yet. The game writes it to {Path.Combine(_scan.Install.UserDataFolder, "logs")} once it has run."); return; }
        new LogForm(_scan, watch, id => { Activate(); SelectMod(id); }).Show(this);
    }

    // ------------------------------------------------------------ modlists

    void FillModlists()
    {
        _filling = true;
        try
        {
            _modlist.Items.Clear();
            foreach (var l in Store.All()) _modlist.Items.Add(l.Name);
            var pick = _modlist.Items.Cast<string>().FirstOrDefault(n => string.Equals(n, _s.Modlist, StringComparison.OrdinalIgnoreCase));
            if (pick != null) _modlist.SelectedItem = pick;
            else if (_modlist.Items.Count > 0) _modlist.SelectedIndex = 0;
        }
        finally { _filling = false; }
    }

    Modlist? PickedModlist() => _modlist.SelectedItem is string n ? Store.Get(n) : null;

    async Task SwitchModlistAsync()
    {
        if (_scan == null) return;
        if (PickedModlist() is not { } list) { Info(this, "There are no modlists yet. Tick the mods you want and press Save as..."); return; }
        if (!CanChangeFiles()) return;
        var (_, missing) = list.Match(_scan);
        if (missing.Count > 0 && !Confirm(this, $"{missing.Count} mod{(missing.Count == 1 ? " in this list isn't" : "s in this list aren't")} installed:\n\n{string.Join("\n", missing.Take(15).Select(e => "  " + (e.DisplayName ?? e.Name)))}{(missing.Count > 15 ? "\n  ..." : "")}\n\nSwitch anyway, with the rest?")) return;
        _history.Push(_enabled);
        _enabled = list.EnabledIds(_scan);
        _dirty = true;
        if (SaveToGame()) ShowNote($"Switched to \"{list.Name}\".", Tone.Ok);
        _s.Modlist = list.Name;
        SaveSettings();
        await ScanAsync();
    }

    void SaveModlistAs()
    {
        if (_scan == null) return;
        var name = Ask(this, "Save modlist", "Name for a modlist of the ticked mods:", PickedModlist()?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Store.Get(name) != null && !Confirm(this, $"Replace the modlist \"{name}\"?")) return;
        SaveModlist(Modlist.From(name, _scan.LoadOrder(_enabled)));
    }

    void SaveModlist(Modlist list)
    {
        try
        {
            Store.Save(list);
            _s.Modlist = list.Name;
            SaveSettings();
            FillModlists();
            ShowNote($"Saved the modlist \"{list.Name}\" ({list.Mods.Count} mods).", Tone.Ok);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, "Couldn't save the modlist: " + e.Message); }
    }

    ContextMenuStrip ModlistMenu()
    {
        var menu = new ContextMenuStrip();
        var picked = PickedModlist();
        menu.Items.Add("Import from a file...", null, (_, _) =>
        {
            using var d = new OpenFileDialog { Title = "Import a modlist", Filter = "Modlists (*.json)|*.json|All files|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try { SaveModlist(Modlist.Parse(File.ReadAllText(d.FileName), Path.GetFileNameWithoutExtension(d.FileName))); }
            catch (Exception e) when (e is FormatException or IOException) { Error(this, e.Message); }
        });
        menu.Items.Add(new ToolStripMenuItem("Export to a file...", null, (_, _) =>
        {
            using var d = new SaveFileDialog { Title = "Export the modlist", Filter = "Modlists (*.json)|*.json", FileName = Backups.Safe(picked!.Name) + ".json" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(d.FileName, picked.ToJson()); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, e.Message); }
        }) { Enabled = picked != null });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Copy share code", null, (_, _) =>
        {
            Clipboard.SetText(picked!.ToShareCode());
            ShowNote($"Copied a share code for \"{picked.Name}\". Paste it in a chat; your friend uses More > Add from a share code.", Tone.Ok);
        }) { Enabled = picked != null });
        menu.Items.Add("Add from a share code...", null, (_, _) =>
        {
            var code = Ask(this, "Add a modlist", $"Paste the share code ({Modlist.SharePrefix}...):", Clipboard.ContainsText() && Clipboard.GetText().StartsWith(Modlist.SharePrefix) ? Clipboard.GetText() : "");
            if (string.IsNullOrWhiteSpace(code)) return;
            try
            {
                var list = Modlist.FromShareCode(code);
                if (Ask(this, "Add a modlist", "Name for it:", list.Name) is { Length: > 0 } name) { list.Name = name; SaveModlist(list); }
            }
            catch (FormatException e) { Error(this, e.Message); }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Delete this modlist...", null, (_, _) =>
        {
            if (!Confirm(this, $"Delete the modlist \"{picked!.Name}\"? Mods aren't touched.")) return;
            Store.Delete(picked.Name);
            FillModlists();
        }) { Enabled = picked != null });
        return menu;
    }

    // ------------------------------------------------------------ tools

    ContextMenuStrip ToolsMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Find the broken mod for me...", null, async (_, _) => await BisectStartAsync()) { Enabled = _scan != null && _bisect == null });
        menu.Items.Add("Install a mod from a zip...", null, (_, _) => PickArchive());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open the game's Mods folder", null, (_, _) => { Directory.CreateDirectory(_scan!.Install.GameMods); Ui.Open(this, _scan.Install.GameMods); }) { Enabled = _scan != null });
        menu.Items.Add(new ToolStripMenuItem("Open the AppData Mods folder", null, (_, _) => { Directory.CreateDirectory(_scan!.Install.UserMods); Ui.Open(this, _scan.Install.UserMods); }) { Enabled = _scan != null });
        menu.Items.Add("Open the mod backups", null, (_, _) => { Directory.CreateDirectory(AppSettings.BackupFolder); Ui.Open(this, AppSettings.BackupFolder); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Verify the game files in Steam", null, (_, _) => Ui.Open(this, "steam://validate/" + SevenDays.AppId));
        return menu;
    }

    async Task BisectStartAsync()
    {
        if (_scan == null || !CanChangeFiles()) return;
        if (_dirty && !SaveToGame()) return;
        await ScanAsync();
        var suspects = _scan.LoadOrder().Where(m => !m.Locked).Select(m => m.Id).ToList();
        if (suspects.Count < 2) { Info(this, "Finding the broken mod needs at least two enabled mods."); return; }
        if (!Confirm(this, $"This finds which of your {suspects.Count} enabled mods causes a problem, in about {(int)Math.Ceiling(Math.Log2(suspects.Count))} rounds.\n\n" +
            "Each round turns on half of the suspects. Start the game, see whether the problem happens, close the game, then press Problem still there or Problem gone.\n\n" +
            "Your mods are put back the way they are now at the end, or when you press Stop.")) return;
        _bisect = new Bisect(EnabledOnDisk(), suspects);
        await BisectApplyAsync();
    }

    async Task BisectApplyAsync()
    {
        if (_scan == null || _bisect == null) return;
        var locked = _scan.Mods.Where(m => m.Locked).Select(m => m.Id);
        _enabled = _bisect.EnabledThisRound(locked);
        _dirty = true;
        if (!SaveToGame()) { _bisect = null; _bisectBar.Visible = false; await ScanAsync(); return; }
        await ScanAsync();
        _bisectText.Text = $"Finding the broken mod: round {_bisect.Round}, about {_bisect.RoundsLeft} to go. {_bisect.Testing.Count} of {_bisect.Suspects.Count} suspects are on. Play, check, close the game, then:";
        _bisectBar.Visible = true;
    }

    async Task BisectAnswerAsync(bool problemHappened)
    {
        if (_bisect == null) return;
        if (!CanChangeFiles()) return;
        _bisect.Answer(problemHappened);
        if (_bisect.Done) await BisectStopAsync(_bisect.Culprit);
        else await BisectApplyAsync();
    }

    async Task BisectStopAsync(string? culprit)
    {
        if (_bisect == null || _scan == null || !CanChangeFiles()) return;
        _enabled = _bisect.Original.ToHashSet();
        _bisect = null;
        _bisectBar.Visible = false;
        _dirty = true;
        SaveToGame();
        await ScanAsync();
        if (culprit != null && _scan?.ById(culprit) is { } m)
        {
            SelectMod(m.Id);
            ShowNote($"Found it: {m.DisplayName} ({m.FolderName}). Your mods are back the way they were; untick it and Save to game to leave it out.", Tone.Warn);
        }
        else ShowNote("Stopped. Your mods are back the way they were.", Tone.Normal);
    }
}
