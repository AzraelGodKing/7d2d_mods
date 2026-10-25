using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>The saved games (Saves\World\Save), with backups to a zip.</summary>
sealed class SavesForm : Form
{
    readonly GameInstall _install;
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Saved games" };
    readonly Label _status = Muted();

    public SavesForm(GameInstall install)
    {
        _install = install;
        Text = "Saves - 7 Days Mod Manager";
        Font = Theme.BodyFont;
        Size = new Size(820, 520);
        StartPosition = FormStartPosition.CenterParent;
        foreach (var (name, w) in new[] { ("World", 200), ("Save", 200), ("Last played", 160), ("Size", 90) }) _list.Columns.Add(name, (int)(w * Theme.TextScale));
        _list.DoubleClick += (_, _) => { if (Picked() is { } s) Ui.Open(this, s.Folder); };
        var open = SmallBtn("Open folder", (_, _) => { if (Picked() is { } s) Ui.Open(this, s.Folder); });
        var backup = SmallBtn("Back up", async (_, _) =>
        {
            if (Picked() is not { } s) return;
            try { var zip = await Task.Run(() => Saves.Backup(s, AppSettings.SaveBackupFolder)); _status.Text = "Backed up to " + zip; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, "Couldn't back up the save: " + e.Message); }
        });
        var backups = SmallBtn("Open backups", (_, _) => { Directory.CreateDirectory(AppSettings.SaveBackupFolder); Ui.Open(this, AppSettings.SaveBackupFolder); });
        var card = Card("Saves", new Control[] { _status, open, backup, backups }, _list);
        card.Dock = DockStyle.Fill;
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        host.Controls.Add(card);
        Controls.Add(host);
        Theme.Apply(this);
        Shown += (_, _) => Fill();
    }

    SaveGame? Picked() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as SaveGame : null;

    void Fill()
    {
        var saves = Saves.List(_install);
        _list.Items.Clear();
        foreach (var s in saves)
            _list.Items.Add(new ListViewItem(new[] { s.World, s.Name, s.LastPlayed.ToString("g"), Display.Size(s.Size) }) { Tag = s });
        _status.Text = saves.Count == 0 ? $"No saves in {_install.Saves}" : $"{saves.Count} save{(saves.Count == 1 ? "" : "s")}";
    }
}
