namespace AiQuotaTray.Ui;

/// <summary>Shared traffic-light coloring for a usage percentage, used by both the details flyout and the taskbar overlay.</summary>
internal static class UsageColors
{
    public static Color ForPercent(double percent) => percent switch
    {
        < 70 => Color.FromArgb(96, 189, 104),
        < 90 => Color.FromArgb(249, 183, 61),
        _ => Color.FromArgb(224, 90, 90),
    };
}
