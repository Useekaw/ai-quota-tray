using Microsoft.Win32;

namespace AiQuotaTray.Ui;

/// <summary>Colors pulled from the current Windows app theme (light/dark), read fresh each time — the user can flip theme while the popup is closed.</summary>
internal readonly record struct ThemePalette(
    bool IsDark,
    Color WindowBackground,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color BarTrack)
{
    private static readonly ThemePalette Dark = new(
        IsDark: true,
        WindowBackground: Color.FromArgb(44, 44, 44),
        Border: Color.FromArgb(70, 70, 70),
        TextPrimary: Color.FromArgb(255, 255, 255),
        TextSecondary: Color.FromArgb(180, 180, 180),
        BarTrack: Color.FromArgb(75, 75, 75));

    private static readonly ThemePalette Light = new(
        IsDark: false,
        WindowBackground: Color.FromArgb(249, 249, 249),
        Border: Color.FromArgb(220, 220, 220),
        TextPrimary: Color.FromArgb(32, 32, 32),
        TextSecondary: Color.FromArgb(97, 97, 97),
        BarTrack: Color.FromArgb(224, 224, 224));

    public static ThemePalette Resolve() => IsAppsLightTheme() ? Light : Dark;

    private static bool IsAppsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // Missing key/value (older Windows, fresh profile) defaults to light, matching Windows' own default.
            return key?.GetValue("AppsUseLightTheme") is int value ? value != 0 : true;
        }
        catch
        {
            return true;
        }
    }
}
