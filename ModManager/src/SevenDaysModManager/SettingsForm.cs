using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>
/// Edits a mod's settings files. A file of "Key=value" lines is shown as a form: each setting with the author's
/// comment above it, on/off switches as drop-downs. Any file can also be edited as text. Saving writes the file into
/// the mod and keeps the player's copy, which is put back when the mod is updated or reinstalled.
/// </summary>
sealed class SettingsForm : Form
{
    readonly InstalledMod _mod;
    readonly ModSettingsStore _store;
    readonly List<string> _files;
    readonly ListBox _fileList = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Settings files" };
    readonly Panel _formHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    readonly TextBox _text = new() { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), AcceptsTab = true, Visible = false };
    readonly Button _formView, _textView, _save;
    readonly Label _status = Muted();
    SettingsDoc? _doc;
    string _original = "";
    int _current = -1;
    bool _loading;

    public SettingsForm(InstalledMod mod, ModSettingsStore store)
    {
        _mod = mod;
        _store = store;
        _files = ModSettingsStore.FilesOf(mod.Folder);
        Text = $"{mod.DisplayName} settings - 7 Days Mod Manager";
        Font = Theme.BodyFont;
        Size = new Size(980, 700);
        MinimumSize = new Size(700, 460);
        StartPosition = FormStartPosition.CenterParent;

        foreach (var f in _files) _fileList.Items.Add(Path.GetFileName(f));
        _fileList.SelectedIndexChanged += (_, _) => { if (!_loading) Pick(_fileList.SelectedIndex); };
        _formView = SmallBtn("Form", (_, _) => ShowForm(true));
        _textView = SmallBtn("Text", (_, _) => ShowForm(false));
        _save = SmallBtn("Save", (_, _) => Save()).Role(ThemeRole.Primary);
        var revert = SmallBtn("Undo changes", (_, _) => { if (_current >= 0) LoadText(_original); });
        var folder = SmallBtn("Open folder", (_, _) => Ui.Open(this, mod.Folder));
        _text.TextChanged += (_, _) => { if (!_loading) Dirty(); };

        var editor = new Panel { Dock = DockStyle.Fill };
        editor.Controls.Add(_formHost);
        editor.Controls.Add(_text);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Margin = Padding.Empty };
        split.Panel1.Controls.Add(_fileList);
        split.Panel2.Controls.Add(editor);
        split.SplitterDistance = 200;
        var card = Card(mod.DisplayName, new Control[] { _status, _formView, _textView, revert, folder, _save }, split);
        card.Dock = DockStyle.Fill;
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        host.Controls.Add(card);
        Controls.Add(host);
        Theme.Apply(this);
        FormClosing += OnClosing;
        Shown += (_, _) =>
        {
            if (_files.Count == 0) { _status.Text = "This mod has no settings files."; _save.Enabled = false; return; }
            _fileList.SelectedIndex = 0;
        };
    }

    bool IsDirty => _current >= 0 && Norm(CurrentText()) != Norm(_original);

    static string Norm(string s) => s.Replace("\r\n", "\n");

    string CurrentText() => _text.Visible || _doc is not { HasEntries: true } ? _text.Text : _doc.ToText();

    void Pick(int index)
    {
        if (index == _current) return;
        if (IsDirty)
        {
            var r = MessageBox.Show(this, $"Save your changes to {Path.GetFileName(_files[_current])}?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel || (r == DialogResult.Yes && !Save()))
            {
                _loading = true; _fileList.SelectedIndex = _current; _loading = false;
                return;
            }
        }
        _current = index;
        try { _original = File.ReadAllText(_files[index]); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = "Couldn't read it: " + e.Message; return; }
        LoadText(_original);
    }

    void LoadText(string text)
    {
        _loading = true;
        _doc = SettingsDoc.Parse(text);
        _text.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _loading = false;
        BuildForm();
        ShowForm(_doc.HasEntries);
        _formView.Enabled = _doc.HasEntries;
        Dirty();
    }

    void ShowForm(bool form)
    {
        if (_doc == null) return;
        if (form && _text.Visible)
        {
            // Text edits carry over into the form.
            _doc = SettingsDoc.Parse(_text.Text);
            BuildForm();
        }
        else if (!form && !_text.Visible)
        {
            _loading = true;
            _text.Text = _doc.ToText().Replace("\r\n", "\n").Replace("\n", "\r\n");
            _loading = false;
        }
        _formHost.Visible = form && _doc.HasEntries;
        _text.Visible = !_formHost.Visible;
    }

    void BuildForm()
    {
        _formHost.SuspendLayout();
        _formHost.Controls.Clear();
        if (_doc is not { HasEntries: true }) { _formHost.ResumeLayout(); return; }
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(6, 4, 6, 4) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var e in _doc.Entries)
        {
            var key = new Label { Text = e.Key, AutoSize = true, Font = Theme.HeadingFont, UseMnemonic = false, Margin = new Padding(0, 10, 16, 0) };
            Control input;
            if (e.Choices != null)
            {
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, AccessibleName = e.Key };
                cb.Items.AddRange(e.Choices);
                cb.SelectedItem = e.Choices.FirstOrDefault(c => c.Equals(e.Value.Trim(), StringComparison.OrdinalIgnoreCase));
                cb.SelectedIndexChanged += (_, _) => { if (cb.SelectedItem is string v) { _doc.Set(e.Key, v); Dirty(); } };
                input = cb;
            }
            else
            {
                var tb = new TextBox { Text = e.Value, Width = 520, AccessibleName = e.Key, Anchor = AnchorStyles.Left | AnchorStyles.Right };
                tb.TextChanged += (_, _) => { _doc.Set(e.Key, tb.Text); Dirty(); };
                input = tb;
            }
            input.Margin = new Padding(0, 8, 0, 0);
            grid.Controls.Add(key);
            grid.Controls.Add(input);
            if (e.Description.Length > 0)
            {
                var desc = new Label { Text = e.Description, AutoSize = true, MaximumSize = new Size(640, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 4) }.Role(ThemeRole.Muted);
                grid.Controls.Add(new Label { AutoSize = true, Margin = Padding.Empty });
                grid.Controls.Add(desc);
            }
        }
        _formHost.Controls.Add(grid);
        Theme.Apply(grid);
        _formHost.ResumeLayout();
    }

    void Dirty()
    {
        if (_current < 0) return;
        var name = Path.GetFileName(_files[_current]);
        _status.Text = IsDirty ? $"{name}: not saved" : _store.HasSaved(_mod.Key) ? $"{name}: your settings are kept for updates" : name;
        _save.Enabled = IsDirty;
    }

    bool Save()
    {
        if (_current < 0) return false;
        var text = CurrentText().Replace("\r\n", "\n");
        // Keep the file's own line endings.
        if (_original.Contains("\r\n")) text = text.Replace("\n", "\r\n");
        try
        {
            _store.Save(_mod.Key, _files[_current], text);
            _original = text;
            Dirty();
            _status.Text = $"Saved. Your settings will be put back when {_mod.DisplayName} is updated.";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, "Couldn't save: " + e.Message); return false; }
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!IsDirty) return;
        var r = MessageBox.Show(this, "Save your changes?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel || (r == DialogResult.Yes && !Save())) e.Cancel = true;
    }
}
