using System.Runtime.InteropServices;

internal static class TaskbarVisibilityController
{
    private const int SwShow = 5;

    public static void Restore()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SetWindowVisibility("Shell_TrayWnd", SwShow);
        SetWindowVisibility("Shell_SecondaryTrayWnd", SwShow);
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
