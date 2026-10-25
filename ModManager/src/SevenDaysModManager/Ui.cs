namespace SevenDaysModManager;

/// <summary>The building blocks every window uses, the same as ModWright's: cards, rows, buttons and small dialogs.</summary>
static class Ui
{
    public static Label Lbl(string t) => new() { Text = t, AutoSize = true };

    public static Label Muted(string t = "") => new Label { Text = t, AutoSize = true, UseMnemonic = false }.Role(ThemeRole.Muted);

    public static Button Btn(string text, EventHandler h)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2) };
        b.Click += h;
        return b;
    }

    /// <summary>A compact button for a card's title row.</summary>
    public static Button SmallBtn(string text, EventHandler h)
    {
        var b = Btn(text, h);
        b.Padding = new Padding(4, 0, 4, 0);
        b.Font = Theme.SmallFont;
        return b;
    }

    public static TableLayoutPanel Row(params Control[] cs)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = cs.Length, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
        string? label = null;
        foreach (var c in cs)
        {
            // Screen readers name an input by the label before it in its row.
            if (c is Label { Text.Length: > 0 } l and not LinkLabel) label = Display.StripMnemonic(l.Text);
            else if (label != null && c is TextBox or ComboBox && string.IsNullOrEmpty(c.AccessibleName)) c.AccessibleName = label;
            var fill = (c.Tag as string) == "fill";
            t.ColumnStyles.Add(fill ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            c.Anchor = fill ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
            c.Margin = new Padding(0, 3, 8, 3);
            t.Controls.Add(c);
        }
        return t;
    }

    /// <summary>A raised panel with an optional title (and controls to the right of it) over its rows.</summary>
    public static TableLayoutPanel Card(string? title, Control[]? titleExtras, params Control[] rows)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 0, 10) }.Role(ThemeRole.Surface);
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (title != null)
        {
            var extrasFill = titleExtras?.Any(c => (c.Tag as string) == "fill") == true;
            var head = new Label { Text = title.ToUpperInvariant(), AccessibleName = Display.StripMnemonic(title), AutoSize = true, Font = Theme.SectionFont, Tag = extrasFill ? null : "fill", Margin = new Padding(0, 3, 16, 3) }.Role(ThemeRole.Title);
            var r = Row(new Control[] { head }.Concat(titleExtras ?? Array.Empty<Control>()).ToArray());
            r.Margin = new Padding(0, 0, 0, 2);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(r);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, Margin = new Padding(0, 0, 0, 8) }.Role(ThemeRole.Rule));
        }
        foreach (var c in rows)
        {
            var fill = c.Dock == DockStyle.Fill;
            card.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            card.Controls.Add(c);
        }
        return card;
    }

    /// <summary>Shows a menu under a button.</summary>
    public static void DropDown(Control under, ContextMenuStrip menu)
    {
        Theme.StyleMenu(menu);
        menu.Show(under, new Point(0, under.Height));
    }

    /// <summary>A small themed dialog asking for one line of text; null when cancelled.</summary>
    public static string? Ask(IWin32Window? owner, string title, string prompt, string value = "", bool multiline = false)
    {
        using var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = Theme.BodyFont, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var box = new TextBox { Text = value, Width = 460, Multiline = multiline, Height = multiline ? 120 : 0, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
        var ok = Btn("OK", (_, _) => { f.DialogResult = DialogResult.OK; });
        var cancel = Btn("Cancel", (_, _) => { f.DialogResult = DialogResult.Cancel; });
        ok.Role(ThemeRole.Accent);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(14), Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(0, 0, 0, 6) });
        layout.Controls.Add(box);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons);
        f.Controls.Add(layout);
        f.AcceptButton = multiline ? null : ok;
        f.CancelButton = cancel;
        Theme.Apply(f);
        return f.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }

    public static void Error(IWin32Window? owner, string text) =>
        MessageBox.Show(owner, text, "7 Days Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void Info(IWin32Window? owner, string text) =>
        MessageBox.Show(owner, text, "7 Days Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirm(IWin32Window? owner, string text, MessageBoxIcon icon = MessageBoxIcon.Question) =>
        MessageBox.Show(owner, text, "7 Days Mod Manager", MessageBoxButtons.OKCancel, icon) == DialogResult.OK;

    /// <summary>Opens a folder, file or link; says so when Windows can't.</summary>
    public static void Open(IWin32Window? owner, string target)
    {
        try { GameRunner.Open(target); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException) { Error(owner, $"Couldn't open {target}: {e.Message}"); }
    }

    /// <summary>The named colour tags for mod rows, as Options and the right-click menu offer them.</summary>
    public static readonly (string Name, Color Color)[] Tags =
    {
        ("Red", Color.FromArgb(214, 69, 65)), ("Orange", Color.FromArgb(230, 126, 34)), ("Yellow", Color.FromArgb(241, 196, 15)),
        ("Green", Color.FromArgb(46, 184, 92)), ("Blue", Color.FromArgb(52, 152, 219)), ("Purple", Color.FromArgb(155, 89, 182)),
    };

    public static Color? TagColor(string? name) => Tags.FirstOrDefault(t => t.Name == name) is { Name: not null } t ? t.Color : null;
}
