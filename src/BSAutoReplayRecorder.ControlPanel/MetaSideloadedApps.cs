using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BSAutoReplayRecorder.ControlPanel;

internal static class MetaSideloadedApps
{
    private const string RegistryPath = @"SOFTWARE\Wow6432Node\Oculus VR, LLC\Oculus";
    private const string RegistryValue = "AllowDevSideloaded";

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        return IsEnabledWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEnabledWindows()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
            return Convert.ToInt32(key?.GetValue(RegistryValue) ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static void RequestEnable()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Meta sideloaded apps can only be enabled on Windows.");
        }

        if (IsEnabled()) return;

        const string command = "New-Item -Path 'HKLM:\\SOFTWARE\\Wow6432Node\\Oculus VR, LLC\\Oculus' -Force | Out-Null; " +
                               "New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Wow6432Node\\Oculus VR, LLC\\Oculus' -Name AllowDevSideloaded -PropertyType DWord -Value 1 -Force | Out-Null";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"",
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Meta sideloaded-app approval was canceled or could not be requested.", ex);
        }
    }
}
