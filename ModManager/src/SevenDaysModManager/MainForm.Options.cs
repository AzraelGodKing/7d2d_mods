using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>The OPTIONS tab: the theme and text size, where the game and its data are, and how Play starts it.</summary>
public sealed partial class MainForm
{
    readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    readonly ComboBox _textSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly Label _gameFolderText = new() { AutoSize = true, UseMnemonic = false };
    readonly Label _dataFolderText = new() { AutoSize = true, UseMnemonic = false };
    readonly ComboBox _installTo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    readonly ComboBox _eac = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    readonly TextBox _launchOptions = new() { Width = 420, PlaceholderText = "for example  -skipintro -screen-fullscreen 0" };
    readonly CheckBox _backupSave = new() { Text = "Back up the newest save before each Play", AutoSize = true };
    bool _fillingOptions;

    static readonly (EacMode Mode, string Label)[] EacChoices =
    {
        (EacMode.Auto, "Auto: off when an enabled mod has code, else on"),
        (EacMode.Off, "Always off (start 7DaysToDie.exe directly)"),
        (EacMode.On, "Always on (through Steam and the game's launcher)"),
    };

    Control BuildOptionsPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 12, 14, 12), Visible = false };
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var (_, label) in Display.Themes) _theme.Items.Add(label);
        foreach (var size in Display.TextSizes) _textSize.Items.Add($"{size * 100:0}%");
        _theme.SelectedIndexChanged += (_, _) =>
        {
            if (_fillingOptions || _theme.SelectedIndex < 0) return;
            _s.Theme = Display.Themes[_theme.SelectedIndex].Name;
            SaveSettings();
            if (Theme.Set(_s.Theme)) ApplyTheme();
        };
        _textSize.SelectedIndexChanged += (_, _) =>
        {
            if (_fillingOptions || _textSize.SelectedIndex < 0) return;
            _s.TextScale = Display.TextSizes[_textSize.SelectedIndex];
            SaveSettings();
            Info(this, "The new text size is used the next time the app starts.");
        };
        stack.Controls.Add(Card("Appearance", null,
            Row(Lbl("Theme"), _theme),
            Row(Lbl("Text size"), _textSize, Muted("(after a restart)"))));

        _installTo.Items.AddRange(new object[] { "The game's Mods folder", "%AppData%\\7DaysToDie\\Mods (kept when the game is reinstalled)" });
        _installTo.SelectedIndexChanged += (_, _) => { if (_fillingOptions) return; _s.InstallToUserData = _installTo.SelectedIndex == 1; SaveSettings(); };
        var gameBrowse = Btn("Change...", (_, _) => { PickGameFolder(); FillOptions(); });
        var gameAuto = Btn("Find through Steam", (_, _) => { _s.GameFolder = null; SaveSettings(); FillOptions(); _ = ScanAsync(); });
        var dataBrowse = Btn("Change...", (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Pick the game's data folder (it holds Saves and logs)", UseDescriptionForTitle = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            _s.UserDataFolder = d.SelectedPath;
            SaveSettings();
            FillOptions();
            _ = ScanAsync();
        });
        var dataDefault = Btn("Default", (_, _) => { _s.UserDataFolder = null; SaveSettings(); FillOptions(); _ = ScanAsync(); });
        stack.Controls.Add(Card("Game", null,
            Row(Lbl("Game folder"), _gameFolderText, gameBrowse, gameAuto),
            Row(Lbl("Data folder"), _dataFolderText, dataBrowse, dataDefault),
            Row(Lbl("Install new mods into"), _installTo)));

        foreach (var (_, label) in EacChoices) _eac.Items.Add(label);
        _eac.SelectedIndexChanged += (_, _) =>
        {
            if (_fillingOptions || _eac.SelectedIndex < 0) return;
            _s.Eac = EacChoices[_eac.SelectedIndex].Mode;
            SaveSettings();
            UpdateState();
        };
        _launchOptions.Leave += (_, _) => { if (_s.LaunchOptions != _launchOptions.Text.Trim()) { _s.LaunchOptions = _launchOptions.Text.Trim(); SaveSettings(); } };
        _backupSave.CheckedChanged += (_, _) => { if (_fillingOptions) return; _s.BackupSaveBeforePlay = _backupSave.Checked; SaveSettings(); };
        Tip(_eac, "EasyAntiCheat blocks mods with code (DLLs). XML-only mods work either way; online servers with EAC need it on");
        stack.Controls.Add(Card("Play", null,
            Row(Lbl("EasyAntiCheat"), _eac),
            Row(Lbl("Launch options"), _launchOptions),
            Row(_backupSave),
            Row(Muted($"Save backups go to {AppSettings.SaveBackupFolder} (the newest 10 of each save are kept)."))));

        var settings = new LinkLabel { Text = AppSettings.Folder, AutoSize = true, UseMnemonic = false };
        settings.LinkClicked += (_, _) => { Directory.CreateDirectory(AppSettings.Folder); Ui.Open(this, AppSettings.Folder); };
        stack.Controls.Add(Card("About", null,
            Row(Lbl("7 Days Mod Manager " + Version)),
            Row(Lbl("Settings, modlists and backups"), settings),
            Row(Muted("Command line: run SevenDaysModManager help in a terminal for list, problems, switch, play and install."))));

        page.Controls.Add(stack);
        return page;
    }

    void FillOptions()
    {
        _fillingOptions = true;
        try
        {
            _theme.SelectedIndex = Math.Max(0, Array.FindIndex(Display.Themes, t => t.Name == Display.NormalizeTheme(_s.Theme)));
            _textSize.SelectedIndex = Math.Max(0, Array.FindIndex(Display.TextSizes, t => Math.Abs(t - _s.TextScale) < 0.01f));
            var install = _scan?.Install ?? SevenDays.Detect(_steam, _s.GameFolder, _s.UserDataFolder);
            _gameFolderText.Text = (install?.GameFolder ?? "not found") + (_s.GameFolder == null ? "  (found through Steam)" : "");
            _dataFolderText.Text = (install?.UserDataFolder ?? _s.UserDataFolder ?? SevenDays.DefaultUserDataFolder()) + (_s.UserDataFolder == null ? "  (default)" : "");
            _installTo.SelectedIndex = _s.InstallToUserData ? 1 : 0;
            _eac.SelectedIndex = Math.Max(0, Array.FindIndex(EacChoices, c => c.Mode == _s.Eac));
            _launchOptions.Text = _s.LaunchOptions;
            _backupSave.Checked = _s.BackupSaveBeforePlay;
        }
        finally { _fillingOptions = false; }
    }
}
