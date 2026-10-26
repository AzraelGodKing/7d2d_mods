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
    readonly CheckBox _backupSaveOnChange = new() { Text = "Back up the newest save before mods are enabled, disabled, switched or updated", AutoSize = true };
    readonly Label _serverFolderText = new() { AutoSize = true, UseMnemonic = false };
    readonly TextBox _serverConfig = new() { Width = 200 };
    readonly TextBox _serverOptions = new() { Width = 420, PlaceholderText = "added after -batchmode -nographics -configfile=... -dedicated" };
    readonly CheckBox _backupConfig = new() { Text = "Back up the config file each time the server is started from here", AutoSize = true };
    readonly Label _serverInfo = Muted();
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
        _backupSaveOnChange.CheckedChanged += (_, _) => { if (_fillingOptions) return; _s.BackupSaveBeforeModChange = _backupSaveOnChange.Checked; SaveSettings(); };
        Tip(_eac, "EasyAntiCheat blocks mods with code (DLLs). XML-only mods work either way; online servers with EAC need it on");
        stack.Controls.Add(Card("Play", null,
            Row(Lbl("EasyAntiCheat"), _eac),
            Row(Lbl("Launch options"), _launchOptions),
            Row(_backupSave),
            Row(_backupSaveOnChange),
            Row(Muted($"Save backups go to {AppSettings.SaveBackupFolder} (the newest 10 of each save are kept)."))));

        var serverBrowse = Btn("Change...", (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Pick the dedicated server's folder (the one with 7DaysToDieServer.exe and serverconfig.xml)", UseDescriptionForTitle = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            _s.ServerFolder = d.SelectedPath;
            SaveSettings();
            FillOptions();
        });
        var serverAuto = Btn("Find through Steam", (_, _) => { _s.ServerFolder = null; SaveSettings(); FillOptions(); });
        _serverConfig.Leave += (_, _) =>
        {
            var v = _serverConfig.Text.Trim().Length == 0 ? DedicatedServer.DefaultConfig : _serverConfig.Text.Trim();
            if (v != _s.ServerConfigFile) { _s.ServerConfigFile = v; SaveSettings(); FillOptions(); }
        };
        _serverOptions.Leave += (_, _) => { if (_s.ServerLaunchOptions != _serverOptions.Text.Trim()) { _s.ServerLaunchOptions = _serverOptions.Text.Trim(); SaveSettings(); } };
        _backupConfig.CheckedChanged += (_, _) => { if (_fillingOptions) return; _s.BackupConfigBeforeServer = _backupConfig.Checked; SaveSettings(); };
        var openConfig = Btn("Open", (_, _) => { if (ServerConfig() is { } p && File.Exists(p)) Ui.Open(this, p); else Error(this, "The config file wasn't found."); });
        var backupConfig = Btn("Back up now", (_, _) =>
        {
            if (ServerConfig() is not { } p || !File.Exists(p)) { Error(this, "The config file wasn't found."); return; }
            try { Info(this, "Backed up to " + DedicatedServer.BackupConfig(p, AppSettings.ServerConfigBackupFolder)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, "Couldn't back it up: " + e.Message); }
        });
        var restoreConfig = Btn("Restore...", (_, _) => RestoreServerConfig());
        var configBackups = Btn("Backups", (_, _) => { Directory.CreateDirectory(AppSettings.ServerConfigBackupFolder); Ui.Open(this, AppSettings.ServerConfigBackupFolder); });
        Tip(_serverOptions, "For example  -port=26901  (the game's startdedicated.bat arguments are always used)");
        stack.Controls.Add(Card("Dedicated server", null,
            Row(Lbl("Server folder"), _serverFolderText, serverBrowse, serverAuto),
            Row(Lbl("Config file"), _serverConfig, openConfig, backupConfig, restoreConfig, configBackups),
            Row(_serverInfo),
            Row(Lbl("Server launch options"), _serverOptions),
            Row(_backupConfig),
            Row(Muted("Start it from the ▾ beside Play. Steam's \"7 Days to Die Dedicated Server\" (under Tools) is found by itself; the game can also run as a server."))));

        var checkUpdates = new CheckBox { Text = "Check for a new version when the app starts", AutoSize = true, Checked = _s.CheckForUpdates };
        checkUpdates.CheckedChanged += (_, _) => { _s.CheckForUpdates = checkUpdates.Checked; SaveSettings(); };
        var checkNow = Btn("Check now", async (_, _) => await CheckForUpdateAsync(quiet: false));
        Tip(checkUpdates, "Only GitHub's public list of releases is read; nothing about you or your mods is sent");
        var settings = new LinkLabel { Text = AppSettings.Folder, AutoSize = true, UseMnemonic = false };
        settings.LinkClicked += (_, _) => { Directory.CreateDirectory(AppSettings.Folder); Ui.Open(this, AppSettings.Folder); };
        stack.Controls.Add(Card("About", null,
            Row(Lbl("7 Days Mod Manager " + Version), checkNow),
            Row(checkUpdates),
            Row(Lbl("Settings, modlists and backups"), settings),
            Row(Muted("Command line: run SevenDaysModManager help in a terminal for list, problems, switch, play and install."))));

        page.Controls.Add(stack);
        return page;
    }

    string? ServerConfig()
    {
        var install = _scan?.Install ?? SevenDays.Detect(_steam, _s.GameFolder, _s.UserDataFolder);
        return DedicatedServer.Find(_steam, _s.ServerFolder, install)?.ConfigPath(_s.ServerConfigFile);
    }

    /// <summary>A line about the server's config: its name, port, world and EAC, or why it can't be read.</summary>
    string ServerSummary(ServerInstall? server)
    {
        if (server == null) return "";
        var path = server.ConfigPath(_s.ServerConfigFile);
        if (!File.Exists(path)) return $"{Path.GetFileName(path)} is not in the server folder.";
        try
        {
            var p = DedicatedServer.ReadConfig(path).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
            string V(string k) => p.TryGetValue(k, out var v) ? v : "?";
            var eac = V("EACEnabled");
            var code = _scan?.Mods.Any(m => m.Enabled && m.HasCode && !m.Locked) == true;
            return $"\"{V("ServerName")}\", port {V("ServerPort")}, world {V("GameWorld")}, game {V("GameName")}, EAC {eac}"
                + (code && eac.Equals("true", StringComparison.OrdinalIgnoreCase) ? ".  Code mods need EACEnabled false on the server." : "");
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { return e.Message; }
    }

    void RestoreServerConfig()
    {
        if (ServerConfig() is not { } path) { Error(this, "No dedicated server found."); return; }
        var stem = Path.GetFileNameWithoutExtension(path);
        if (DedicatedServer.ConfigBackups(AppSettings.ServerConfigBackupFolder, stem).Count == 0) { Info(this, $"There are no backups of {Path.GetFileName(path)} yet."); return; }
        using var d = new OpenFileDialog { Title = $"Restore {Path.GetFileName(path)} from a backup", InitialDirectory = AppSettings.ServerConfigBackupFolder, Filter = $"Backups ({stem}-*.xml)|{stem}-*.xml" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (!Confirm(this, $"Put {Path.GetFileName(d.FileName)} back as {path}? The file there now is backed up first.")) return;
        try
        {
            DedicatedServer.RestoreConfig(d.FileName, path, AppSettings.ServerConfigBackupFolder);
            FillOptions();
            Info(this, $"Restored {Path.GetFileName(path)}. Restart the server to use it.");
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { Error(this, "Couldn't restore it: " + e.Message); }
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
            _backupSaveOnChange.Checked = _s.BackupSaveBeforeModChange;
            var server = DedicatedServer.Find(_steam, _s.ServerFolder, install);
            _serverFolderText.Text = (server?.Folder ?? "not found") + (_s.ServerFolder == null && server != null ? "  (found by itself)" : "");
            _serverConfig.Text = _s.ServerConfigFile;
            _serverOptions.Text = _s.ServerLaunchOptions;
            _backupConfig.Checked = _s.BackupConfigBeforeServer;
            _serverInfo.Text = ServerSummary(server);
        }
        finally { _fillingOptions = false; }
    }
}
