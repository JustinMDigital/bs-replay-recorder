using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BSAutoReplayRecorder.Core;
using UnityEngine;
using IpaLogger = IPA.Logging.Logger;

namespace BSAutoReplayRecorder.Plugin;

public sealed class InstanceWindowPlacementController : MonoBehaviour
{
    private const int GwlStyle = -16;
    private const int SwRestore = 9;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const long WsPopup = 0x80000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsOverlappedWindow = 0x00CF0000L;

    private static InstanceWindowPlacementController? _instance;

    private WindowPlacementSettings _settings = new WindowPlacementSettings();
    private IpaLogger? _logger;
    private int _instanceIndex;
    private bool _loggedPlan;

    public static void EnsureCreated(BatchRecorderSettings settings, IpaLogger logger)
    {
        if (!settings.WindowPlacement.Enabled)
        {
            return;
        }

        DestroyInstance();

        var gameObject = new GameObject("Auto Replay Recorder Window Placement");
        DontDestroyOnLoad(gameObject);
        _instance = gameObject.AddComponent<InstanceWindowPlacementController>();
        _instance.Configure(settings, logger);
    }

    public static void DestroyInstance()
    {
        var instance = _instance;
        _instance = null;
        if (instance != null)
        {
            Destroy(instance.gameObject);
        }
    }

    private void Configure(BatchRecorderSettings settings, IpaLogger logger)
    {
        _settings = settings.WindowPlacement;
        _logger = logger;
        _instanceIndex = _settings.InstanceIndex ??
                         settings.ControlPanelWorker.PreferredInstanceIndex ??
                         0;
        StartCoroutine(ApplyUntilStable());
    }

    private IEnumerator ApplyUntilStable()
    {
        var delay = (float)_settings.ApplyDelay.TotalSeconds;
        if (delay > 0)
        {
            yield return new WaitForSeconds(delay);
        }

        var retryCount = Math.Max(1, _settings.RetryCount);
        var wait = new WaitForSeconds((float)_settings.RetryInterval.TotalSeconds);
        PlacementPlan? lastPlan = null;

        for (var attempt = 1; attempt <= retryCount; attempt++)
        {
            var plan = CreatePlacementPlan();
            if (plan == null)
            {
                yield return wait;
                continue;
            }

            lastPlan = plan;
            ApplyUnityWindowMode(plan.Value);
            if (_settings.UseBorderlessWindow || _settings.UseNativeWindowMove)
            {
                ApplyNativeWindowPlacement(plan.Value, moveWindow: _settings.UseNativeWindowMove);
            }

            if (!_loggedPlan)
            {
                _loggedPlan = true;
                _logger?.Info("Window placement target for instance " + (_instanceIndex + 1) +
                              ": monitor " + _settings.MonitorIndex +
                              ", rect " + plan.Value.Left + "," + plan.Value.Top +
                              " " + plan.Value.Width + "x" + plan.Value.Height + ".");
            }

            yield return wait;
        }

        if (lastPlan.HasValue)
        {
            _logger?.Info("Finished window placement attempts for instance " + (_instanceIndex + 1) + ".");
        }
        else
        {
            _logger?.Warn("Window placement could not find monitor index " + _settings.MonitorIndex + ".");
        }
    }

    private PlacementPlan? CreatePlacementPlan()
    {
        var monitors = NativeMethods.GetMonitorBounds();
        if (_settings.MonitorIndex < 0 || _settings.MonitorIndex >= monitors.Count)
        {
            return null;
        }

        var monitor = monitors[_settings.MonitorIndex];
        var columns = Math.Max(1, _settings.Columns);
        var rows = Math.Max(1, _settings.Rows);
        var tileWidth = _settings.Width > 0
            ? _settings.Width
            : Math.Max(1, (monitor.Right - monitor.Left) / columns);
        var tileHeight = _settings.Height > 0
            ? _settings.Height
            : Math.Max(1, (monitor.Bottom - monitor.Top) / rows);
        var column = Math.Max(0, _instanceIndex) % columns;
        var row = Math.Max(0, _instanceIndex) / columns;

        return new PlacementPlan(
            monitor.Left + column * tileWidth,
            monitor.Top + row * tileHeight,
            tileWidth,
            tileHeight);
    }

    private static void ApplyUnityWindowMode(PlacementPlan plan)
    {
        if (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.fullScreen)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(plan.Width, plan.Height, false);
        }
    }

    private void ApplyNativeWindowPlacement(PlacementPlan plan, bool moveWindow)
    {
        var hWnd = NativeMethods.FindCurrentProcessWindow();
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ShowWindow(hWnd, SwRestore);
        var currentStyle = NativeMethods.GetWindowLongPtr(hWnd, GwlStyle).ToInt64();
        var windowedStyle = _settings.UseBorderlessWindow
            ? (currentStyle & ~WsOverlappedWindow) | WsPopup | WsVisible
            : (currentStyle & ~WsPopup) | WsVisible | WsOverlappedWindow;
        if (windowedStyle != currentStyle)
        {
            NativeMethods.SetWindowLongPtr(hWnd, GwlStyle, new IntPtr(windowedStyle));
        }

        if (!moveWindow)
        {
            NativeMethods.SetWindowPos(
                hWnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            return;
        }

        NativeMethods.SetWindowPos(
            hWnd,
            IntPtr.Zero,
            plan.Left,
            plan.Top,
            plan.Width,
            plan.Height,
            SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
    }

    private readonly struct PlacementPlan
    {
        public PlacementPlan(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }

        public int Top { get; }

        public int Width { get; }

        public int Height { get; }
    }

    private static class NativeMethods
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        public static IntPtr FindCurrentProcessWindow()
        {
            var currentProcessId = Process.GetCurrentProcess().Id;
            var match = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                GetWindowThreadProcessId(hWnd, out var processId);
                if (processId != currentProcessId)
                {
                    return true;
                }

                match = hWnd;
                return false;
            }, IntPtr.Zero);

            return match;
        }

        public static IReadOnlyList<Rect> GetMonitorBounds()
        {
            var monitors = new List<Rect>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect monitorRect, IntPtr data) =>
            {
                var info = new MonitorInfo
                {
                    Size = Marshal.SizeOf(typeof(MonitorInfo))
                };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    monitors.Add(info.Monitor);
                }

                return true;
            }, IntPtr.Zero);

            return monitors;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;
        }
    }
}
