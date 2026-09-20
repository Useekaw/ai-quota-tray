using System.Drawing.Text;

namespace AiQuotaTray.Ui;

/// <summary>
/// A borderless, click-through sticker glued to the taskbar's bottom-left
/// corner (over the Start button) showing the current usage percentages.
/// Windows gives third-party apps no way to dock an actual taskbar widget
/// (that's what the built-in Weather/date widget is), so this fakes one:
/// an always-on-top window sized to the taskbar's own height, transparent
/// except for the text, with clicks passed straight through to whatever
/// taskbar button is underneath.
/// </summary>
internal sealed class TaskbarOverlayForm : Form
{
    private const int MinWidth = 90;
    private const int MaxWidth = 280;
    private const int HorizontalPadding = 10;

    // Arbitrary, unlikely-to-be-drawn color used purely as the transparency
    // key — every pixel this color becomes see-through.
    private static readonly Color KeyColor = Color.FromArgb(1, 2, 3);

    private string _text = string.Empty;

    public TaskbarOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = KeyColor;
        TransparencyKey = KeyColor;
        Width = MinWidth;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExLayered = 0x00080000;
            const int WsExTransparent = 0x00000020;
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;

            var cp = base.CreateParams;
            cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    // Never take focus or a taskbar/alt-tab entry — it's read-only decoration.
    protected override bool ShowWithoutActivation => true;

    public void SetText(string text)
    {
        if (_text == text)
        {
            return;
        }
        _text = text;

        var measured = TextRenderer.MeasureText(text, Font).Width + (HorizontalPadding * 2);
        Width = Math.Clamp(measured, MinWidth, MaxWidth);

        Invalidate();
    }

    /// <summary>Re-syncs position and height to the real taskbar. Cheap enough to call on every refresh in case Explorer restarts, the taskbar auto-hides, or DPI/resolution changes.</summary>
    public void SnapToTaskbar()
    {
        if (TaskbarInfo.GetBounds() is not { } bounds)
        {
            return;
        }

        Height = bounds.Height;
        Location = new Point(bounds.Left, bounds.Top);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_text.Length == 0)
        {
            return;
        }

        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var rect = new Rectangle(HorizontalPadding, 0, Width - (HorizontalPadding * 2), Height);

        // Faux outline (black copies offset by a pixel in every direction,
        // white fill on top) so the text stays legible over whatever
        // wallpaper/accent color shows through the transparent background.
        foreach (var (dx, dy) in OutlineOffsets)
        {
            var offsetRect = new Rectangle(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
            TextRenderer.DrawText(e.Graphics, _text, Font, offsetRect, Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
        TextRenderer.DrawText(e.Graphics, _text, Font, rect, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private static readonly (int Dx, int Dy)[] OutlineOffsets =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1),
    ];
}
