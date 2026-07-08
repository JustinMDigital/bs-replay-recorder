using System.Runtime.InteropServices;

namespace BSAutoReplayRecorder.ControlPanel;

internal static class TaskbarVisibilityController
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private static readonly string[] TaskbarWindowClasses =
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    public static void Hide(int monitorIndex)
    {
        Hide(monitorIndex, retryCount: 1, retryDelayMilliseconds: 0);
    }

    public static void HideWithRetries(int monitorIndex)
    {
        Hide(monitorIndex, retryCount: 5, retryDelayMilliseconds: 200);
    }

    private static void Hide(int monitorIndex, int retryCount, int retryDelayMilliseconds)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var targetMonitor = TryGetMonitorBounds(monitorIndex);
        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            SetTaskbarVisibility(SwHide, targetMonitor);
            if (retryDelayMilliseconds > 0 && attempt < retryCount - 1)
            {
                Thread.Sleep(retryDelayMilliseconds);
            }
        }
    }

    public static void Restore()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SetTaskbarVisibility(SwShow, targetMonitor: null);
    }

    private static void SetTaskbarVisibility(int command, Rect? targetMonitor)
    {
        foreach (var className in TaskbarWindowClasses)
        {
            SetWindowVisibility(className, command, targetMonitor);
        }
    }

    private static void SetWindowVisibility(string className, int command, Rect? targetMonitor)
    {
        var hWnd = IntPtr.Zero;
        while (true)
        {
            hWnd = FindWindowEx(IntPtr.Zero, hWnd, className, null);
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            if (!targetMonitor.HasValue ||
                !GetWindowRect(hWnd, out var taskbarBounds) ||
                Intersects(taskbarBounds, targetMonitor.Value))
            {
                ShowWindow(hWnd, command);
            }
        }
    }

    private static Rect? TryGetMonitorBounds(int monitorIndex)
    {
        var monitors = new List<Rect>();
        var success = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect monitorRect, IntPtr data) =>
            {
                var monitorInfo = new MonitorInfo
                {
                    Size = Marshal.SizeOf<MonitorInfo>()
                };
                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    monitors.Add(monitorInfo.Monitor);
                }

                return true;
            },
            IntPtr.Zero);

        if (!success || monitorIndex < 0 || monitorIndex >= monitors.Count)
        {
            return null;
        }

        return monitors[monitorIndex];
    }

    private static bool Intersects(Rect first, Rect second)
    {
        return first.Left < second.Right &&
               first.Right > second.Left &&
               first.Top < second.Bottom &&
               first.Bottom > second.Top;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr hWndParent,
        IntPtr hWndChildAfter,
        string lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }
}
