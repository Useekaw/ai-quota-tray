using System.Runtime.InteropServices;

namespace AiQuotaTray.Ui;

/// <summary>
/// Thin DWM P/Invoke wrapper so a plain borderless WinForms window can pick up
/// native Windows 11 chrome (rounded corners, dark-mode-aware frame, and a
/// best-effort acrylic/Mica backdrop) that GDI+/WinForms has no managed API for.
/// </summary>
internal static class Win11Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    // Flyout-sized popups (volume, wifi, battery, …) use the smaller corner
    // radius; DWMWCP_ROUND (2) is the larger radius regular app windows get.
    private const int DwmwcpRoundSmall = 3;
    private const int DwmsbtTransientWindow = 3; // the acrylic-ish material flyouts/context menus use

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    // Acrylic/Mica backdrop composition (DWMWA_SYSTEMBACKDROP_TYPE) only
    // shipped reliably from the 22H2 update (build 22621) onward; older
    // Windows 11 builds either ignore the attribute or render it oddly, so
    // this is gated rather than assumed on a machine we can't test on here.
    private static readonly bool BackdropMaySucceed = Environment.OSVersion.Version.Build >= 22621;

    public static void ApplyFlyoutChrome(IntPtr handle, bool isDarkMode)
    {
        var cornerPreference = DwmwcpRoundSmall;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

        var darkMode = isDarkMode ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
    }

    /// <summary>
    /// Best-effort acrylic/Mica flyout backdrop. Returns true only when DWM
    /// actually accepted the backdrop request. Callers must keep an opaque
    /// themed background as the default and only switch to a transparency
    /// key when this returns true — an unsupported/failed backdrop with a
    /// transparency key already applied renders as a hole into the desktop
    /// instead of degrading gracefully back to a flat color.
    /// </summary>
    public static bool TryApplyAcrylicBackdrop(IntPtr handle)
    {
        if (!BackdropMaySucceed)
        {
            return false;
        }

        try
        {
            var backdropType = DwmsbtTransientWindow;
            if (DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdropType, sizeof(int)) != 0)
            {
                return false;
            }

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            return DwmExtendFrameIntoClientArea(handle, ref margins) == 0;
        }
        catch
        {
            return false;
        }
    }
}
