using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AiQuotaTray.Ui;

/// <summary>One run of overlay text; a null color renders as plain white (labels, separators).</summary>
internal readonly record struct OverlaySegment(string Text, Color? Color = null);

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
    private const int MaxWidth = 320;
    private const int HorizontalPadding = 10;

    // The app's ambient default font is Segoe UI 9pt; ~17% larger reads
    // clearly against a busy taskbar/wallpaper without dwarfing the tray icons.
    private static readonly Font TextFont = new("Segoe UI", 10.5f);

    private const TextFormatFlags SegmentFormat =
        TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;

    // The real taskbar (Shell_TrayWnd) is itself a topmost window; clicking
    // empty taskbar space (not an icon — that's handled fine) makes Explorer
    // re-assert its own topmost position, which silently drops ours *below*
    // it. WS_EX_TOPMOST alone doesn't survive that. Periodically re-pushing
    // HWND_TOPMOST is the standard workaround other taskbar-overlay tools use.
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private readonly System.Windows.Forms.Timer _keepOnTopTimer;
    private IReadOnlyList<OverlaySegment> _segments = [];

    // Arbitrary, unlikely-to-be-drawn color used purely as the transparency
    // key — every pixel this color becomes see-through.
    private static readonly Color KeyColor = Color.FromArgb(1, 2, 3);

    public TaskbarOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = KeyColor;
        TransparencyKey = KeyColor;
        Font = TextFont;
        Width = MinWidth;

        _keepOnTopTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _keepOnTopTimer.Tick += (_, _) => KeepOnTop();
        _keepOnTopTimer.Start();
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

    private void KeepOnTop()
    {
        if (!IsHandleCreated)
        {
            return;
        }
        SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void SetSegments(IReadOnlyList<OverlaySegment> segments)
    {
        if (segments.SequenceEqual(_segments))
        {
            return;
        }
        _segments = segments;

        var contentWidth = segments.Sum(s => TextRenderer.MeasureText(s.Text, Font, Size.Empty, SegmentFormat).Width);
        Width = Math.Clamp(contentWidth + (HorizontalPadding * 2), MinWidth, MaxWidth);

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
        KeepOnTop();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_segments.Count == 0)
        {
            return;
        }

        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var x = HorizontalPadding;
        foreach (var segment in _segments)
        {
            var width = TextRenderer.MeasureText(e.Graphics, segment.Text, Font, Size.Empty, SegmentFormat).Width;
            var rect = new Rectangle(x, 0, width, Height);

            // Faux outline (black copies offset by a pixel in every direction)
            // so the text stays legible over whatever wallpaper/accent color
            // shows through the transparent background.
            foreach (var (dx, dy) in OutlineOffsets)
            {
                var offsetRect = new Rectangle(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
                TextRenderer.DrawText(e.Graphics, segment.Text, Font, offsetRect, Color.Black, SegmentFormat);
            }
            TextRenderer.DrawText(e.Graphics, segment.Text, Font, rect, segment.Color ?? Color.White, SegmentFormat);

            x += width;
        }
    }

    private static readonly (int Dx, int Dy)[] OutlineOffsets =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1),
    ];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepOnTopTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
