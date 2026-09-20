namespace AiQuotaTray.Configuration;

/// <summary>
/// User-editable settings, persisted as JSON. New fields must have a default
/// value so an older config file on disk (missing that property) still
/// deserializes cleanly.
/// </summary>
public sealed record AppConfig(
    double RefreshIntervalMinutes = 5,
    float OverlayFontSize = 10.5f,
    bool OverlayEnabled = true,
    string LogLevel = "Information")
{
    public static AppConfig Default { get; } = new();

    /// <summary>Clamps values a hand-edited file could set out of range, so a typo can't wedge the app (e.g. a 0-minute refresh loop or an unreadably tiny/huge overlay).</summary>
    public AppConfig Sanitized() => this with
    {
        RefreshIntervalMinutes = Math.Clamp(RefreshIntervalMinutes, 1, 1440),
        OverlayFontSize = Math.Clamp(OverlayFontSize, 6f, 24f),
    };
}
