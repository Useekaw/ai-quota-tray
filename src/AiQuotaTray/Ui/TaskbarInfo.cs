using System.Runtime.InteropServices;

namespace AiQuotaTray.Ui;

/// <summary>Locates the real taskbar window, so the overlay can match its height and sit flush against it.</summary>
internal static class TaskbarInfo
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Screen-coordinate bounds of the taskbar, or null if it can't be found (e.g. Explorer restarting).</summary>
    public static Rectangle? GetBounds()
    {
        var handle = FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return null;
        }
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
}
