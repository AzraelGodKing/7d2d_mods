namespace SevenDaysModManager;

/// <summary>
/// The app's name in the brand bar as one piece: "7 Days" in the title colour and font, "Mod Manager" straight after
/// it in the light brand font and colour (two labels would leave WinForms' label padding between them).
/// </summary>
sealed class BrandName : Label
{
    const string First = "7 Days ", Second = "Mod Manager";
    const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    public BrandName()
    {
        Text = First + Second;
        AccessibleName = Text;
        UseMnemonic = false;
        AutoSize = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    static Size Part(string text, Font font) => TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), Flags);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var a = Part(First, Theme.TitleFont);
        var b = Part(Second, Theme.BrandFont);
        return new Size(a.Width + b.Width + Padding.Horizontal, Math.Max(a.Height, b.Height) + Padding.Vertical);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var back = new SolidBrush(BackColor)) e.Graphics.FillRectangle(back, ClientRectangle);
        var a = Part(First, Theme.TitleFont);
        var b = Part(Second, Theme.BrandFont);
        var height = Math.Max(a.Height, b.Height);
        TextRenderer.DrawText(e.Graphics, First, Theme.TitleFont, new Point(Padding.Left, Padding.Top + height - a.Height), ForeColor, Flags);
        TextRenderer.DrawText(e.Graphics, Second, Theme.BrandFont, new Point(Padding.Left + a.Width, Padding.Top + height - b.Height), Theme.Banner.Brand, Flags);
    }
}
