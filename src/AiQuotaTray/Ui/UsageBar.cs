using System.Drawing.Drawing2D;

namespace AiQuotaTray.Ui;

/// <summary>
/// A flat, fully-rounded fill bar in the Fluent style. WinForms' stock
/// <see cref="ProgressBar"/> renders with the classic (pre-Vista) visual
/// style regardless of app theme, so this is a small owner-drawn stand-in.
/// </summary>
internal sealed class UsageBar : Control
{
    public double Percent { get; set; }
    public Color FillColor { get; set; } = Color.Gray;
    public Color TrackColor { get; set; } = Color.LightGray;

    public UsageBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);
        Height = 6;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var radius = Height / 2f;
        using var trackPath = RoundedRect(new RectangleF(0, 0, Width, Height), radius);
        using var trackBrush = new SolidBrush(TrackColor);
        g.FillPath(trackBrush, trackPath);

        var fillWidth = (float)(Width * Math.Clamp(Percent, 0, 100) / 100.0);
        if (fillWidth <= 0.5f)
        {
            return;
        }

        using var fillPath = RoundedRect(new RectangleF(0, 0, fillWidth, Height), radius);
        using var fillBrush = new SolidBrush(FillColor);
        g.FillPath(fillBrush, fillPath);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();

        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
