namespace SevenDaysModManager;

/// <summary>Beside the mod list: the picked mod's ModInfo, where it is, whether it has code, and the player's note.</summary>
sealed class ModDetailsPanel : Panel
{
    readonly Label _title = new Label { AutoSize = true, Font = Theme.HeadingFont, UseMnemonic = false, MaximumSize = new Size(320, 0) }.Role(ThemeRole.Title);
    readonly Label _sub = Ui.Muted();
    readonly Label _desc = new() { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(320, 0), Margin = new Padding(0, 6, 0, 6) };
    readonly Label _facts = new() { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(320, 0) };
    readonly LinkLabel _website = new() { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(320, 0) };
    readonly TextBox _note = new() { Multiline = true, Height = 70, Width = 300, ScrollBars = ScrollBars.Vertical, AccessibleName = "Your note on this mod", PlaceholderText = "Your note on this mod" };
    readonly FlowLayoutPanel _box = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(10, 8, 10, 8) };
    readonly FlowLayoutPanel _buttons = new() { AutoSize = true, WrapContents = true, MaximumSize = new Size(320, 0), Margin = new Padding(0, 6, 0, 0) };
    InstalledMod? _mod;

    /// <summary>Raised when the note is changed (on leaving the box), with the mod's Name and the new note.</summary>
    public event Action<string, string>? NoteChanged;
    public event Action<InstalledMod>? OpenFolder, Uninstall;

    public ModDetailsPanel()
    {
        AutoScroll = true;
        _website.LinkClicked += (_, _) => { if (_website.Tag is string url) Ui.Open(FindForm(), url); };
        _note.Leave += (_, _) => { if (_mod != null) NoteChanged?.Invoke(_mod.Key, _note.Text.Trim()); };
        _buttons.Controls.Add(Ui.SmallBtn("Open folder", (_, _) => { if (_mod != null) OpenFolder?.Invoke(_mod); }));
        _buttons.Controls.Add(Ui.SmallBtn("Uninstall...", (_, _) => { if (_mod != null) Uninstall?.Invoke(_mod); }));
        _box.Controls.AddRange(new Control[] { _title, _sub, _desc, _facts, _website, Ui.Muted("Note"), _note, _buttons });
        Controls.Add(_box);
        Show(null, null);
    }

    public void Show(InstalledMod? mod, string? note)
    {
        _mod = mod;
        _box.Visible = true;
        if (mod == null)
        {
            _title.Text = "No mod picked";
            _sub.Text = "Pick a mod in the list to see its details.";
            _desc.Text = _facts.Text = "";
            _website.Visible = _note.Visible = _buttons.Visible = false;
            return;
        }
        _title.Text = mod.DisplayName;
        _sub.Text = string.Join("  ·  ", new[] { mod.Info?.Version is { Length: > 0 } v ? "v" + v : null, mod.Info?.Author is { Length: > 0 } a ? "by " + a : null }.Where(s => s != null));
        _desc.Text = mod.Info?.Description ?? mod.Error ?? "";
        var facts = new List<string>
        {
            $"Name: {mod.Key}",
            $"Folder: {mod.FolderName} ({(mod.Location == ModLocation.Game ? "game Mods folder" : "AppData Mods folder")}{(mod.Enabled ? "" : ", disabled")})",
            mod.HasCode ? $"Code: {mod.Dlls.Count} DLL{(mod.Dlls.Count == 1 ? "" : "s")}, so EasyAntiCheat must be off" : "No code: works with EasyAntiCheat on",
            $"Size: {Display.Size(mod.Size)}",
        };
        if (mod.Locked) facts.Add("Part of the game: always on.");
        _facts.Text = string.Join("\n", facts);
        var site = mod.Info?.Website;
        _website.Visible = !string.IsNullOrWhiteSpace(site);
        _website.Text = site ?? "";
        _website.Tag = site != null && !site.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "https://" + site : site;
        _note.Visible = _buttons.Visible = true;
        _note.Text = note ?? "";
        _buttons.Controls[1].Enabled = !mod.Locked;
    }
}
