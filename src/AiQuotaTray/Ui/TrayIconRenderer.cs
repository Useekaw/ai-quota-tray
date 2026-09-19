namespace AiQuotaTray.Ui;

/// <summary>
/// Draws the tray glyph: left half is Codex (split 2/3 session, 1/3 weekly),
/// right half is Copilot (one bar) — each a vertical meter that fills from
/// the bottom as usage climbs, color-coded green/amber/red by threshold.
/// </summary>
internal static class TrayIconRenderer
{
    private const int Size = 32;
    private const int BarGap = 1;

    private static readonly Color TrackColor = Color.FromArgb(70, 70, 70);
    private static readonly Color UnknownColor = Color.FromArgb(120, 120, 120);
    private static readonly Color BorderColor = Color.FromArgb(160, Color.Black);

    public static Icon Render(double? codexSessionPercent, double? codexWeeklyPercent, double? copilotPercent)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.Clear(Color.Transparent);

            var leftRegion = new Rectangle(1, 1, Size / 2 - 2, Size - 2);
            var rightRegion = new Rectangle(Size / 2 + 1, 1, Size / 2 - 2, Size - 2);

            // Codex: session gets 2/3 of the left region's width, weekly gets 1/3.
            var sessionWidth = (int)Math.Round((leftRegion.Width - BarGap) * 2.0 / 3.0);
            var weeklyWidth = leftRegion.Width - BarGap - sessionWidth;
            var sessionRect = new Rectangle(leftRegion.X, leftRegion.Y, sessionWidth, leftRegion.Height);
            var weeklyRect = new Rectangle(sessionRect.Right + BarGap, leftRegion.Y, weeklyWidth, leftRegion.Height);

            DrawBar(g, sessionRect, codexSessionPercent);
            DrawBar(g, weeklyRect, codexWeeklyPercent);
            DrawBar(g, rightRegion, copilotPercent);
        }

        var handle = bitmap.GetHicon();
        // Icon.FromHandle keeps a reference to the HICON; wrapping it in a clone
        // lets us destroy the original handle without invalidating the icon.
        using var live = Icon.FromHandle(handle);
        var owned = (Icon)live.Clone();
        NativeMethods.DestroyIcon(handle);
        return owned;
    }

    private static void DrawBar(Graphics g, Rectangle bounds, double? percent)
    {
        if (bounds.Width <= 0)
        {
            return;
        }

        using var trackBrush = new SolidBrush(TrackColor);
        g.FillRectangle(trackBrush, bounds);

        if (percent is { } value)
        {
            var filledHeight = (int)Math.Round(bounds.Height * Math.Clamp(value, 0, 100) / 100.0);
            if (filledHeight > 0)
            {
                var filledRect = new Rectangle(bounds.X, bounds.Bottom - filledHeight, bounds.Width, filledHeight);
                using var fillBrush = new SolidBrush(ColorFor(value));
                g.FillRectangle(fillBrush, filledRect);
            }
        }
        else
        {
            using var unknownBrush = new SolidBrush(UnknownColor);
            g.FillRectangle(unknownBrush, bounds);
        }

        using var border = new Pen(BorderColor, 1f);
        g.DrawRectangle(border, bounds);
    }

    private static Color ColorFor(double percent) => percent switch
    {
        < 70 => Color.FromArgb(46, 125, 50),
        < 90 => Color.FromArgb(249, 168, 37),
        _ => Color.FromArgb(198, 40, 40),
    };

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
