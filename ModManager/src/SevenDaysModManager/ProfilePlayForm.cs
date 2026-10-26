using static SevenDaysModManager.Ui;

namespace SevenDaysModManager;

/// <summary>How Play starts the game with one profile: its own EasyAntiCheat setting and launch options, or the ones in Options.</summary>
sealed class ProfilePlayForm : Form
{
    static readonly (EacMode? Mode, string Label)[] Choices =
    {
        (null, "As in Options"),
        (EacMode.Auto, "Auto: off when an enabled mod has code, else on"),
        (EacMode.Off, "Always off (start 7DaysToDie.exe directly)"),
        (EacMode.On, "Always on (through Steam and the game's launcher)"),
    };

    readonly ComboBox _eac = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    readonly CheckBox _ownOptions = new() { Text = "Its own launch options", AutoSize = true };
    readonly TextBox _options = new() { Width = 420, PlaceholderText = "for example  -skipintro -screen-fullscreen 0" };

    public EacMode? Eac => Choices[Math.Max(0, _eac.SelectedIndex)].Mode;
    public string? LaunchOptions => _ownOptions.Checked ? _options.Text.Trim() : null;

    public ProfilePlayForm(Modlist list)
    {
        Text = $"Play settings for {list.Name}";
        Font = Theme.BodyFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        foreach (var (_, label) in Choices) _eac.Items.Add(label);
        _eac.SelectedIndex = Math.Max(0, Array.FindIndex(Choices, c => c.Mode == list.Eac));
        _ownOptions.Checked = list.LaunchOptions != null;
        _options.Text = list.LaunchOptions ?? "";
        _options.Enabled = _ownOptions.Checked;
        _ownOptions.CheckedChanged += (_, _) => _options.Enabled = _ownOptions.Checked;

        var ok = Btn("OK", (_, _) => DialogResult = DialogResult.OK).Role(ThemeRole.Accent);
        var cancel = Btn("Cancel", (_, _) => DialogResult = DialogResult.Cancel);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Top };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        var card = Card("Play with this profile", null,
            Row(Lbl("EasyAntiCheat"), _eac),
            Row(_ownOptions),
            Row(Lbl("Launch options"), _options),
            Row(Muted("Switching to the profile and pressing Play, or its desktop shortcut, uses these.")));
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
        layout.Controls.Add(card);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
        Theme.Apply(this);
    }
}
