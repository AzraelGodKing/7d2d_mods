using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>Newer versions of installed mods on GitHub: tick the ones to update (each is downloaded and installed over the old copy, after a backup).</summary>
sealed class ModUpdatesForm : Form
{
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, CheckBoxes = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, AccessibleName = "Mod updates" };

    public IReadOnlyList<ModUpdate> Picked => _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (ModUpdate)i.Tag!).ToList();

    public ModUpdatesForm(IReadOnlyList<ModUpdate> updates)
    {
        Text = "Mod updates - 7 Days Mod Manager";
        Font = Theme.BodyFont;
        Size = new Size(820, 460);
        StartPosition = FormStartPosition.CenterParent;
        foreach (var (name, w) in new[] { ("Mod", 280), ("Installed", 100), ("Newest", 100), ("", 260) }) _list.Columns.Add(name, (int)(w * Theme.TextScale));
        foreach (var u in updates)
            _list.Items.Add(new ListViewItem(new[] { u.Name, u.Current, u.Latest, u.ZipUrl != null ? "downloads and installs" : "no zip in the release: opens its page" })
            { Tag = u, Checked = true, ToolTipText = u.PageUrl });
        _list.DoubleClick += (_, _) => { if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is ModUpdate u) Ui.Open(this, u.PageUrl); };

        var update = Btn("Update ticked", (_, _) => DialogResult = DialogResult.OK).Role(ThemeRole.Accent);
        var close = Btn("Close", (_, _) => DialogResult = DialogResult.Cancel);
        var notes = SmallBtn("Release page", (_, _) => { if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is ModUpdate u) Ui.Open(this, u.PageUrl); });
        var card = Card("Newer versions on GitHub", new Control[] { notes }, _list,
            Row(Muted("Each old copy is zipped into the mod backups first, and your edited settings are put back into the new version.")));
        card.Dock = DockStyle.Fill;
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Padding = new Padding(12, 0, 12, 12) };
        buttons.Controls.Add(close);
        buttons.Controls.Add(update);
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        host.Controls.Add(card);
        Controls.Add(host);
        Controls.Add(buttons);
        AcceptButton = update;
        CancelButton = close;
        Theme.Apply(this);
    }
}
