using System.Runtime.InteropServices;

namespace BSAutoReplayRecorder.ControlPanel;

public interface IDisplayInfoProvider
{
    DisplayInfoSnapshot GetDisplays();
}

public sealed class WindowsDisplayInfoProvider : IDisplayInfoProvider
{
    public DisplayInfoSnapshot GetDisplays()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new DisplayInfoSnapshot
            {
                Status = "Unsupported",
                Summary = "Display details are only available on Windows."
            };
        }

        try
        {
            var displays = EnumerateDisplays();
            return new DisplayInfoSnapshot
            {
                Status = displays.Count == 0 ? "Unavailable" : "Ready",
                Summary = displays.Count == 0
                    ? "Windows did not return any display details."
                    : "Detected " + displays.Count + " display" + (displays.Count == 1 ? "." : "s."),
                Displays = displays
            };
        }
        catch (Exception ex) when (ex is ExternalException ||
                                   ex is InvalidOperationException ||
                                   ex is System.ComponentModel.Win32Exception)
        {
            return new DisplayInfoSnapshot
            {
                Status = "Failed",
                Summary = "Could not read Windows display details.",
                Error = ex.Message
            };
        }
    }

    public static string BuildDisplayLabel(int index, string? friendlyName, int width, int height, bool isPrimary)
    {
        var parts = new List<string>
        {
            "Monitor " + (index + 1)
        };

        var cleanName = NormalizeText(friendlyName);
        if (!string.IsNullOrWhiteSpace(cleanName))
        {
            parts.Add(cleanName);
        }

        var resolution = FormatResolution(width, height);
        if (!string.IsNullOrWhiteSpace(resolution))
        {
            parts.Add(resolution);
        }

        if (isPrimary)
        {
            parts.Add("Primary");
        }

        return string.Join(" - ", parts);
    }

    private static List<DisplayInfoRecord> EnumerateDisplays()
    {
        var displays = new List<DisplayInfoRecord>();
        var success = NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.Rect monitorRect, IntPtr data) =>
            {
                var monitorInfo = new NativeMethods.MonitorInfoEx
                {
                    Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
                };

                if (!NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    return true;
                }

                var deviceName = NormalizeText(monitorInfo.DeviceName);
                var displayDevice = GetDisplayDevice(deviceName);
                var mode = GetCurrentDisplayMode(deviceName);
                var width = mode.Width > 0
                    ? mode.Width
                    : Math.Max(0, monitorInfo.Monitor.Right - monitorInfo.Monitor.Left);
                var height = mode.Height > 0
                    ? mode.Height
                    : Math.Max(0, monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top);
                var index = displays.Count;
                var friendlyName = ChooseFriendlyName(displayDevice.MonitorName, displayDevice.AdapterName, deviceName);
                var isPrimary = (monitorInfo.Flags & NativeMethods.MonitorInfoPrimary) != 0;

                displays.Add(new DisplayInfoRecord
                {
                    Index = index,
                    MonitorNumber = index + 1,
                    DeviceName = deviceName,
                    FriendlyName = friendlyName,
                    AdapterName = displayDevice.AdapterName,
                    Left = monitorInfo.Monitor.Left,
                    Top = monitorInfo.Monitor.Top,
                    Width = width,
                    Height = height,
                    IsPrimary = isPrimary,
                    Label = BuildDisplayLabel(index, friendlyName, width, height, isPrimary)
                });

                return true;
            },
            IntPtr.Zero);

        if (!success)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return displays;
    }

    private static DisplayDeviceNames GetDisplayDevice(string deviceName)
    {
        var monitorName = "";
        for (var index = 0u; index < 16; index++)
        {
            var monitor = NativeMethods.CreateDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(deviceName, index, ref monitor, 0))
            {
                break;
            }

            var candidate = NormalizeText(monitor.DeviceString);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                monitorName = candidate;
                if (!IsGenericMonitorName(candidate))
                {
                    break;
                }
            }
        }

        var adapterName = "";
        for (var index = 0u; index < 32; index++)
        {
            var adapter = NativeMethods.CreateDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref adapter, 0))
            {
                break;
            }

            if (!string.Equals(NormalizeText(adapter.DeviceName), deviceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            adapterName = NormalizeText(adapter.DeviceString);
            break;
        }

        return new DisplayDeviceNames(monitorName, adapterName);
    }

    private static DisplayMode GetCurrentDisplayMode(string deviceName)
    {
        var mode = new NativeMethods.DevMode
        {
            Size = (ushort)Marshal.SizeOf<NativeMethods.DevMode>()
        };

        if (!NativeMethods.EnumDisplaySettings(deviceName, NativeMethods.EnumCurrentSettings, ref mode))
        {
            return new DisplayMode(0, 0);
        }

        return new DisplayMode((int)mode.PelsWidth, (int)mode.PelsHeight);
    }

    private static string ChooseFriendlyName(string monitorName, string adapterName, string deviceName)
    {
        var cleanMonitor = NormalizeText(monitorName);
        if (!string.IsNullOrWhiteSpace(cleanMonitor))
        {
            return cleanMonitor;
        }

        var cleanAdapter = NormalizeText(adapterName);
        if (!string.IsNullOrWhiteSpace(cleanAdapter))
        {
            return cleanAdapter;
        }

        return NormalizeText(deviceName);
    }

    private static string FormatResolution(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return "";
        }

        var suffix = height switch
        {
            1080 when width == 1920 => "1080p",
            1440 when width == 2560 => "1440p",
            2160 when width == 3840 => "4K",
            _ => ""
        };

        var dimensions = width + " x " + height;
        return string.IsNullOrWhiteSpace(suffix) ? dimensions : suffix + " (" + dimensions + ")";
    }

    private static bool IsGenericMonitorName(string value)
    {
        var text = NormalizeText(value);
        return string.Equals(text, "Generic PnP Monitor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "Generic Non-PnP Monitor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "Default Monitor", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Replace("\0", "").Trim();
    }

    private readonly record struct DisplayDeviceNames(string MonitorName, string AdapterName);

    private readonly record struct DisplayMode(int Width, int Height);

    private static class NativeMethods
    {
        public const int MonitorInfoPrimary = 1;
        public const int EnumCurrentSettings = -1;

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool EnumDisplayDevices(
            string? lpDevice,
            uint iDevNum,
            ref DisplayDevice lpDisplayDevice,
            uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettings(
            string? lpszDeviceName,
            int iModeNum,
            ref DevMode lpDevMode);

        public static DisplayDevice CreateDisplayDevice()
        {
            return new DisplayDevice
            {
                Size = Marshal.SizeOf<DisplayDevice>()
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DisplayDevice
        {
            public int Size;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public uint StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            public ushort SpecVersion;
            public ushort DriverVersion;
            public ushort Size;
            public ushort DriverExtra;
            public uint Fields;
            public int PositionX;
            public int PositionY;
            public uint DisplayOrientation;
            public uint DisplayFixedOutput;
            public short Color;
            public short Duplex;
            public short YResolution;
            public short TTOption;
            public short Collate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FormName;

            public ushort LogPixels;
            public uint BitsPerPel;
            public uint PelsWidth;
            public uint PelsHeight;
            public uint DisplayFlags;
            public uint DisplayFrequency;
            public uint ICMMethod;
            public uint ICMIntent;
            public uint MediaType;
            public uint DitherType;
            public uint Reserved1;
            public uint Reserved2;
            public uint PanningWidth;
            public uint PanningHeight;
        }
    }
}

public sealed class DisplayInfoSnapshot
{
    public string Status { get; set; } = "Unchecked";

    public string Summary { get; set; } = "";

    public string? Error { get; set; }

    public List<DisplayInfoRecord> Displays { get; set; } = new List<DisplayInfoRecord>();
}

public sealed class DisplayInfoRecord
{
    public int Index { get; set; }

    public int MonitorNumber { get; set; }

    public string DeviceName { get; set; } = "";

    public string FriendlyName { get; set; } = "";

    public string AdapterName { get; set; } = "";

    public int Left { get; set; }

    public int Top { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsPrimary { get; set; }

    public string Label { get; set; } = "";
}
