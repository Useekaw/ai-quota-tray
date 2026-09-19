namespace AiQuotaTray.Ui;

/// <summary>Draws the small two-tone glyph shown in the taskbar notification area.</summary>
internal static class TrayIconRenderer
{
    private const int Size = 32;

    public static Icon Render(double? codexPercent, double? copilotPercent)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var leftRect = new Rectangle(1, 1, Size / 2 - 2, Size - 2);
            var rightRect = new Rectangle(Size / 2 + 1, 1, Size / 2 - 2, Size - 2);

            using var leftBrush = new SolidBrush(ColorFor(codexPercent));
            using var rightBrush = new SolidBrush(ColorFor(copilotPercent));
            g.FillRectangle(leftBrush, leftRect);
            g.FillRectangle(rightBrush, rightRect);

            using var border = new Pen(Color.FromArgb(160, Color.Black), 1f);
            g.DrawRectangle(border, leftRect);
            g.DrawRectangle(border, rightRect);
        }

        var handle = bitmap.GetHicon();
        // Icon.FromHandle keeps a reference to the HICON; wrapping it in a clone
        // lets us destroy the original handle without invalidating the icon.
        using var live = Icon.FromHandle(handle);
        var owned = (Icon)live.Clone();
        NativeMethods.DestroyIcon(handle);
        return owned;
    }

    private static Color ColorFor(double? percent) => percent switch
    {
        null => Color.FromArgb(120, 120, 120),
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
