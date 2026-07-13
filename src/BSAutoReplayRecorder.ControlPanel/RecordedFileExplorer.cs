using System.Diagnostics;

namespace BSAutoReplayRecorder.ControlPanel;

internal static class RecordedFileExplorer
{
    public static string Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException("Recorded file was not found: " + fullPath);
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Opening recordings in File Explorer is only supported on Windows.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = BuildArguments(fullPath),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not open File Explorer for recorded file: " + ex.Message, ex);
        }

        return fullPath;
    }

    internal static string BuildArguments(string fullPath)
    {
        return "/select,\"" + fullPath.Replace("\"", "\\\"") + "\"";
    }
}
