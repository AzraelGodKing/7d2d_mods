using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>
/// "A new version is out": its change notes, with Update now (download, check, hand off to the new exe and restart),
/// Release notes (the release page) and Later, and a box to stop reminding about this version.
/// </summary>
sealed class UpdateForm : Form
{
    readonly ReleaseInfo _release;
    readonly FlatProgressBar _bar = new() { Height = 6, Dock = DockStyle.Top, Visible = false, Margin = new Padding(0, 6, 0, 6) };
    readonly Label _status = Muted();
    readonly CheckBox _skip = new() { Text = "Don't remind me about this version", AutoSize = true };
    readonly Button _update, _later;
    CancellationTokenSource? _cts;

    /// <summary>True when the box to skip this version was ticked as the window closed.</summary>
    public bool SkipThisVersion => _skip.Checked;

    public UpdateForm(ReleaseInfo release)
    {
        _release = release;
        Text = "Update - 7 Days Mod Manager";
        Font = Theme.BodyFont;
        Size = new Size(640, 460);
        MinimumSize = new Size(480, 340);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var exe = SelfUpdate.RunningExe();
        var cannot = SelfUpdate.CannotUpdateReason(release, exe);
        var head = new Label { Text = $"Version {release.Version} is out. You have {UpdateCheck.CurrentVersion}.", AutoSize = true, Font = Theme.HeadingFont, UseMnemonic = false }.Role(ThemeRole.Title);
        var notes = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            Text = release.Notes.Trim().Length > 0 ? release.Notes.Trim().Replace("\r\n", "\n").Replace("\n", "\r\n") : "No change notes with this release.",
        };
        _update = Btn("Update now", async (_, _) => await UpdateAsync(exe!)).Role(ThemeRole.Primary);
        var page = Btn("Release notes", (_, _) => Ui.Open(this, release.PageUrl));
        _later = Btn("Later", (_, _) => { _cts?.Cancel(); Close(); });
        _update.Enabled = cannot == null;
        if (cannot != null) _status.Text = cannot + " Use Release notes to download it.";

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(head);
        layout.Controls.Add(notes);
        layout.Controls.Add(_bar);
        layout.Controls.Add(Row(_status));
        layout.Controls.Add(Row(_update, page, _later, _skip));
        Controls.Add(layout);
        CancelButton = _later;
        Theme.Apply(this);
        FormClosing += (_, _) => _cts?.Cancel();
    }

    async Task UpdateAsync(string exe)
    {
        _update.Enabled = _skip.Enabled = false;
        _bar.Visible = true;
        _status.Text = "Downloading...";
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p => { _bar.Value = p; _status.Text = $"Downloading... {p}%"; });
            var u = await SelfUpdate.PrepareAsync(_release, progress, _cts.Token);
            _status.Text = "Checked. Restarting to install...";
            SelfUpdate.HandOff(u, exe, UpdateCheck.CurrentVersion);
            DialogResult = DialogResult.OK;
            Application.Exit();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _bar.Visible = false;
            _update.Enabled = _skip.Enabled = true;
            _status.Text = "The update failed: " + e.Message;
            _status.ForeColor = Theme.Of(Tone.Error);
        }
    }
}
