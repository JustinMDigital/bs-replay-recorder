using System.Runtime.InteropServices;

namespace BSAutoReplayRecorder.ControlPanel;

internal static class TaskbarVisibilityController
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    public static void Hide()
    {
        SetTaskbarVisibility(SwHide);
    }

    public static void Restore()
    {
        SetTaskbarVisibility(SwShow);
    }

    private static void SetTaskbarVisibility(int command)
    {
        SetWindowVisibility("Shell_TrayWnd", command);
        SetWindowVisibility("Shell_SecondaryTrayWnd", command);
    }

    private static void SetWindowVisibility(string className, int command)
    {
        var hWnd = IntPtr.Zero;
        while (true)
        {
            hWnd = FindWindowEx(IntPtr.Zero, hWnd, className, null);
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            ShowWindow(hWnd, command);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr hWndParent,
        IntPtr hWndChildAfter,
        string lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
