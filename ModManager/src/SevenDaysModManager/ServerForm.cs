using System.Security.Cryptography;
using System.Text;
using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>
/// Server sync: where the dedicated server's Mods folder is (a folder or FTP), a compare of it with the mods enabled
/// here that the server needs, and Push, which does the ticked rows.
/// </summary>
sealed class ServerForm : Form
{
    readonly AppSettings _s;
    readonly LibraryScan _scan;
    readonly Func<InstalledMod, SideInfo> _side;
    readonly Action _saveSettings;

    readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    readonly TextBox _folder = new() { Width = 460, PlaceholderText = @"for example  \\server\7DaysToDie\Mods" };
    readonly TextBox _host = new() { Width = 260, PlaceholderText = "ftp.example.com" };
    readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 21, Width = 80 };
    readonly TextBox _user = new() { Width = 180 };
    readonly TextBox _password = new() { Width = 180, UseSystemPasswordChar = true };
    readonly TextBox _remotePath = new() { Width = 260, PlaceholderText = "Mods" };
    readonly TableLayoutPanel _folderRow, _hostRow, _loginRow, _pathRow;
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, CheckBoxes = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, AccessibleName = "Mods to send to or remove from the server" };
    readonly Label _status = Muted();
    readonly Button _compare, _push, _stop;
    CancellationTokenSource? _cts;
    bool _filling;

    public ServerForm(AppSettings s, LibraryScan scan, Func<InstalledMod, SideInfo> side, Action saveSettings)
    {
        _s = s;
        _scan = scan;
        _side = side;
        _saveSettings = saveSettings;
        Text = "Server sync - 7 Days Mod Manager";
        Font = Theme.BodyFont;
        Size = new Size(1000, 720);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;

        _kind.Items.AddRange(new object[] { "A folder (shared folder, mapped drive, or a server on this PC)", "FTP", "FTPS (FTP over TLS)" });
        var browse = Btn("Browse...", (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Pick the dedicated server's Mods folder", UseDescriptionForTitle = true, SelectedPath = _folder.Text };
            if (d.ShowDialog(this) == DialogResult.OK) _folder.Text = d.SelectedPath;
        });
        _folderRow = Row(Lbl("Server's Mods folder"), _folder, browse);
        _hostRow = Row(Lbl("Host"), _host, Lbl("Port"), _port);
        _loginRow = Row(Lbl("User"), _user, Lbl("Password"), _password);
        _pathRow = Row(Lbl("Mods folder on the server"), _remotePath, Muted("from the login's home folder"));
        _kind.SelectedIndexChanged += (_, _) => { if (!_filling) ShowRows(); };
        var setup = Card("Server", null, Row(Lbl("Reach it through"), _kind), _folderRow, _hostRow, _loginRow, _pathRow,
            Row(Muted("The FTP password is kept encrypted for your Windows account. SFTP isn't supported; most hosts also offer FTP.")));

        foreach (var (name, w) in new[] { ("", 90), ("Mod", 260), ("Here", 80), ("Server", 80), ("Why", 380) }) _list.Columns.Add(name, (int)(w * Theme.TextScale));
        _compare = SmallBtn("Compare", async (_, _) => await CompareAsync());
        _push = SmallBtn("Push to server", async (_, _) => await PushAsync()).Role(ThemeRole.Primary);
        _stop = SmallBtn("Stop", (_, _) => _cts?.Cancel());
        _push.Enabled = _stop.Enabled = false;
        var tip = new ToolTip();
        tip.SetToolTip(_compare, "Read the server's Mods folder and compare it with the mods enabled here that the server needs");
        tip.SetToolTip(_push, "Do the ticked rows: send new and changed files, and remove what is ticked for removal");
        var compareCard = Card("Compare", new Control[] { _status, _compare, _push, _stop }, _list,
            Row(Muted("The server gets the enabled mods whose Side is Server or Both. Only files that differ are sent. Mods that are only for players, or disabled here, can be removed (tick them). Mods the server has that this PC doesn't are left alone.")));
        compareCard.Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(setup, 0, 0);
        layout.Controls.Add(compareCard, 0, 1);
        Controls.Add(layout);
        Fill();
        Theme.Apply(this);
        FormClosing += (_, e) =>
        {
            if (_cts != null && !Confirm(this, "Server sync is still working. Stop it and close?")) { e.Cancel = true; return; }
            _cts?.Cancel();
            Keep();
        };
    }

    void Fill()
    {
        _filling = true;
        var t = _s.Server;
        _kind.SelectedIndex = t.Kind == ServerKind.Folder ? 0 : t.Tls ? 2 : 1;
        _folder.Text = t.Folder;
        _host.Text = t.Host;
        _port.Value = Math.Clamp(t.Port, 1, 65535);
        _user.Text = t.User;
        _password.Text = Unprotect(t.ProtectedPassword);
        _remotePath.Text = t.RemotePath;
        _filling = false;
        ShowRows();
    }

    void ShowRows()
    {
        var folder = _kind.SelectedIndex == 0;
        _folderRow.Visible = folder;
        _hostRow.Visible = _loginRow.Visible = _pathRow.Visible = !folder;
    }

    /// <summary>Saves what is in the boxes to the settings.</summary>
    void Keep()
    {
        var t = _s.Server;
        t.Kind = _kind.SelectedIndex == 0 ? ServerKind.Folder : ServerKind.Ftp;
        t.Tls = _kind.SelectedIndex == 2;
        t.Folder = _folder.Text.Trim();
        t.Host = _host.Text.Trim();
        t.Port = (int)_port.Value;
        t.User = _user.Text.Trim();
        t.ProtectedPassword = _password.Text.Length == 0 ? null : Protect(_password.Text);
        t.RemotePath = _remotePath.Text.Trim().Length == 0 ? "Mods" : _remotePath.Text.Trim();
        _saveSettings();
    }

    void Busy(bool busy)
    {
        _compare.Enabled = !busy;
        _push.Enabled = !busy && _list.Items.Cast<ListViewItem>().Any(i => i.Checked);
        _stop.Enabled = busy;
        _kind.Enabled = _folder.Enabled = _host.Enabled = _port.Enabled = _user.Enabled = _password.Enabled = _remotePath.Enabled = !busy;
        UseWaitCursor = busy;
    }

    async Task CompareAsync()
    {
        Keep();
        if (!_s.Server.IsSet) { Error(this, _s.Server.Kind == ServerKind.Folder ? "Pick the server's Mods folder first." : "Fill in the FTP host first."); return; }
        var target = _s.Server;
        var password = _password.Text;
        var wanted = ServerSync.ServerMods(_scan, _side);
        var where = Describe();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Busy(true);
        var progress = new Progress<string>(t => _status.Text = t);
        try
        {
            var items = await Task.Run(() =>
            {
                using var server = target.Open(password);
                return ServerSync.Compare(wanted, _scan.Mods, _side, server, progress, ct);
            }, ct);
            FillList(items);
            var send = items.Count(i => i.OnByDefault);
            _status.Text = $"{where}: {send} to send, {items.Count(i => i.Action == SyncAction.Same)} the same, {items.Count(i => i.Action == SyncAction.Remove)} could be removed";
        }
        catch (OperationCanceledException) { _status.Text = "Stopped."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Net.WebException or InvalidOperationException or UriFormatException)
        {
            _status.Text = "Couldn't read the server.";
            Error(this, "Couldn't read the server's Mods folder: " + e.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            Busy(false);
        }
    }

    void FillList(List<SyncItem> items)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var i in items)
        {
            var what = i.Action switch
            {
                SyncAction.Upload => "SEND", SyncAction.Update => "UPDATE", SyncAction.Same => "SAME",
                SyncAction.Remove => "REMOVE", _ => "LEAVE",
            };
            var size = i.SendBytes > 0 ? $"  ({i.Send.Count} file{(i.Send.Count == 1 ? "" : "s")}, {Display.Size(i.SendBytes)})" : "";
            var item = new ListViewItem(new[] { what, i.Name, i.LocalVersion ?? "", i.ServerVersion ?? "", i.Why + size })
            {
                Tag = i, Checked = i.OnByDefault, UseItemStyleForSubItems = true,
                ToolTipText = i.Delete.Count > 0 ? "Removes from the server: " + string.Join(", ", i.Delete.Take(10)) + (i.Delete.Count > 10 ? ", ..." : "") : i.Why,
                ForeColor = i.Action switch
                {
                    SyncAction.Upload or SyncAction.Update => Theme.Of(Tone.Info),
                    SyncAction.Remove => Theme.Of(Tone.Warn),
                    _ => Theme.Current.Muted,
                },
            };
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        _list.ItemCheck -= OnCheck;
        _list.ItemCheck += OnCheck;
        _list.ItemChecked -= OnChecked;
        _list.ItemChecked += OnChecked;
    }

    // Same and Leave rows have nothing to do.
    void OnCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_list.Items[e.Index].Tag is SyncItem { Action: SyncAction.Same or SyncAction.Leave }) e.NewValue = CheckState.Unchecked;
    }

    void OnChecked(object? sender, ItemCheckedEventArgs e) => _push.Enabled = _cts == null && _list.Items.Cast<ListViewItem>().Any(i => i.Checked);

    async Task PushAsync()
    {
        var picked = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (SyncItem)i.Tag!).ToList();
        if (picked.Count == 0) return;
        var send = picked.Where(i => i.Action is SyncAction.Upload or SyncAction.Update).ToList();
        var remove = picked.Where(i => i.Action == SyncAction.Remove).ToList();
        var text = $"Push to {Describe()}?\n\n"
            + (send.Count > 0 ? $"Send {send.Count} mod{(send.Count == 1 ? "" : "s")} ({send.Sum(i => i.Send.Count)} files, {Display.Size(send.Sum(i => i.SendBytes))})\n" : "")
            + (remove.Count > 0 ? $"Remove {remove.Count} mod{(remove.Count == 1 ? "" : "s")} from the server: {string.Join(", ", remove.Select(i => i.Name))}\n" : "")
            + "\nStop the server first, or restart it after: it reads its mods when it starts.";
        if (!Confirm(this, text, remove.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question)) return;
        Keep();
        var target = _s.Server;
        var password = _password.Text;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Busy(true);
        var progress = new Progress<string>(t => _status.Text = t);
        try
        {
            var r = await Task.Run(() =>
            {
                using var server = target.Open(password);
                return ServerSync.Push(picked, server, progress, ct);
            }, ct);
            _cts.Dispose();
            _cts = null;
            Busy(false);
            await CompareAsync();
            _status.Text = $"Pushed: {r.Sent} file{(r.Sent == 1 ? "" : "s")} sent ({Display.Size(r.Bytes)}), {r.Deleted} removed{(r.ModsRemoved > 0 ? $", {r.ModsRemoved} mod{(r.ModsRemoved == 1 ? "" : "s")} removed" : "")}. Restart the server to load them.";
            return;
        }
        catch (OperationCanceledException) { _status.Text = "Stopped part way; press Compare to see where it got to."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Net.WebException or InvalidOperationException)
        {
            _status.Text = "The push stopped with an error.";
            Error(this, "The push stopped: " + e.Message + "\n\nPress Compare to see what is on the server now.");
        }
        _cts?.Dispose();
        _cts = null;
        Busy(false);
    }

    string Describe()
    {
        using var f = _s.Server.Open(_password.Text);
        return f.Describe;
    }

    static string Protect(string password) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));

    static string Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedPassword), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return ""; }
    }
}
