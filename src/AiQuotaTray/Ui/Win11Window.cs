using System.Runtime.InteropServices;

namespace AiQuotaTray.Ui;

/// <summary>
/// Thin DWM P/Invoke wrapper so a plain borderless WinForms window can pick up
/// native Windows 11 chrome (rounded corners, dark-mode-aware frame) that
/// GDI+/WinForms has no managed API for.
/// </summary>
internal static class Win11Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;

    // Flyout-sized popups (volume, wifi, battery, …) use the smaller corner
    // radius; DWMWCP_ROUND (2) is the larger radius regular app windows get.
    private const int DwmwcpRoundSmall = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    public static void ApplyFlyoutChrome(IntPtr handle, bool isDarkMode)
    {
        var cornerPreference = DwmwcpRoundSmall;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

        var darkMode = isDarkMode ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
    }
}
