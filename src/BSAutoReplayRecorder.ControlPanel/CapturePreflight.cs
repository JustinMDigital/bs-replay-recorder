using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BSAutoReplayRecorder.ControlPanel;

public interface ICapturePreflightRunner
{
    CapturePreflightReport Check(
        ControlPanelSettings settings,
        DisplayInfoSnapshot displayInfo,
        IReadOnlyList<int> instanceIndexes);
}

public sealed class CapturePreflightRunner : ICapturePreflightRunner
{
    private const int ProbeWidth = 320;
    private const int ProbeHeight = 180;
    private const int ProbeFrames = 10;
    private const int ProbeFps = 10;

    public CapturePreflightReport Check(
        ControlPanelSettings settings,
        DisplayInfoSnapshot displayInfo,
        IReadOnlyList<int> instanceIndexes)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var report = CreateBaseReport(settings);
        var layoutIssue = CaptureLayoutValidator.Validate(settings, displayInfo, instanceIndexes);
        if (layoutIssue != null)
        {
            return Failed(report, layoutIssue);
        }

        if (!string.Equals(settings.CaptureEngine, "FFmpegDdagrab", StringComparison.OrdinalIgnoreCase))
        {
            report.Status = "Skipped";
            report.Summary = "Capture preflight skipped for " + settings.CaptureEngine + ".";
            report.Detail = "The FFmpeg desktop duplication probe only applies to FFmpeg ddagrab.";
            return report;
        }

        var ffmpegPath = ResolveFfmpegPath(settings.FfmpegPath);
        report.FfmpegPath = ffmpegPath ?? "";
        if (ffmpegPath == null)
        {
            return Failed(report, "FFmpeg was not found. Install FFmpeg or set ffmpegPath in settings.json.");
        }

        var tempOutput = Path.Combine(
            Path.GetTempPath(),
            "bsarr-capture-preflight-" + Guid.NewGuid().ToString("N") + ".mkv");
        try
        {
            var result = RunFfmpegProbe(ffmpegPath, settings, tempOutput);
            if (result.ExitCode == 0 && File.Exists(tempOutput))
            {
                report.Status = "Ready";
                report.Summary = "Capture preflight passed.";
                report.Detail = "FFmpeg ddagrab and " + settings.Encoder + " started on Monitor " +
                                (settings.MonitorIndex + 1).ToString(CultureInfo.InvariantCulture) + ".";
                return report;
            }

            return Failed(report, ClassifyFfmpegFailure(result.Output));
        }
        finally
        {
            TryDelete(tempOutput);
        }
    }

    private static CapturePreflightReport CreateBaseReport(ControlPanelSettings settings)
    {
        return new CapturePreflightReport
        {
            Status = "Checking",
            Summary = "Checking capture preflight...",
            CheckedAtUtc = DateTimeOffset.UtcNow,
            CaptureEngine = settings.CaptureEngine,
            Encoder = settings.Encoder,
            MonitorIndex = settings.MonitorIndex,
            CaptureWidth = settings.CaptureWidth,
            CaptureHeight = settings.CaptureHeight
        };
    }

    private static CapturePreflightReport Failed(CapturePreflightReport report, string issue)
    {
        report.Status = "Failed";
        report.Summary = "Capture preflight failed.";
        report.Detail = issue;
        report.Issues.Add(issue);
        return report;
    }

    private static FfmpegProbeResult RunFfmpegProbe(
        string ffmpegPath,
        ControlPanelSettings settings,
        string outputPath)
    {
        var arguments =
            "-hide_banner -y -f lavfi -i " +
            QuoteArgument(
                "ddagrab=output_idx=" + settings.MonitorIndex.ToString(CultureInfo.InvariantCulture) +
                ":draw_mouse=0:framerate=" + ProbeFps.ToString(CultureInfo.InvariantCulture) +
                ":video_size=" + ProbeWidth.ToString(CultureInfo.InvariantCulture) +
                "x" + ProbeHeight.ToString(CultureInfo.InvariantCulture)) +
            " -frames:v " + ProbeFrames.ToString(CultureInfo.InvariantCulture) +
            " -an -c:v " + NormalizeArgumentToken(settings.Encoder, "h264_nvenc") +
            " -preset " + ResolveEncoderPreset(settings.Encoder, settings.QualityMode) +
            " -b:v 1000k " +
            QuoteArgument(outputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return new FfmpegProbeResult(-1, "Windows did not start FFmpeg.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(8000))
        {
            process.Kill(entireProcessTree: true);
            return new FfmpegProbeResult(-1, "FFmpeg capture preflight timed out.");
        }

        var output = (stdoutTask.GetAwaiter().GetResult() + "\n" + stderrTask.GetAwaiter().GetResult()).Trim();
        return new FfmpegProbeResult(process.ExitCode, output);
    }

    internal static string ClassifyFfmpegFailure(string output)
    {
        var text = output?.Trim() ?? "";
        var lower = text.ToLowerInvariant();

        if (lower.Contains("required nvenc") ||
            lower.Contains("nvenc api") ||
            lower.Contains("minimum required nvidia driver") ||
            lower.Contains("driver does not support"))
        {
            return "NVIDIA driver is too old for this FFmpeg NVENC build. Update the NVIDIA driver, then run Launch + Verify again.";
        }

        if (lower.Contains("cannot load nvcuda") ||
            lower.Contains("no capable devices") ||
            lower.Contains("no nvenc capable devices") ||
            lower.Contains("unsupported device"))
        {
            return "NVENC is not available on this machine. Use a supported NVIDIA GPU/driver before using NVENC H.264.";
        }

        if (lower.Contains("output_idx") ||
            lower.Contains("duplicateoutput") ||
            lower.Contains("failed to duplicate") ||
            lower.Contains("failed to enumerate") ||
            lower.Contains("invalid output"))
        {
            return "FFmpeg could not capture the selected monitor. Choose the detected Monitor 1/2/3 entry that matches the recording display.";
        }

        if (lower.Contains("hardware frames") ||
            lower.Contains("impossible to convert") ||
            lower.Contains("function not implemented"))
        {
            return "The selected encoder is not compatible with the FFmpeg ddagrab frame path. Use NVENC H.264 with a current NVIDIA driver.";
        }

        return string.IsNullOrWhiteSpace(text)
            ? "FFmpeg capture preflight failed without error output."
            : "FFmpeg capture preflight failed: " + CollapseWhitespace(text);
    }

    private static string? ResolveFfmpegPath(string configuredPath)
    {
        foreach (var candidate in EnumerateFfmpegCandidates(configuredPath))
        {
            var resolved = ResolveExecutable(candidate);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFfmpegCandidates(string configuredPath)
    {
        var environmentPath = Environment.GetEnvironmentVariable("BSARR_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            yield return environmentPath;
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var packageDirectory = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(packageDirectory))
                {
                    foreach (var path in Directory
                                 .EnumerateFiles(packageDirectory, "ffmpeg.exe", SearchOption.AllDirectories)
                                 .Where(path => path.IndexOf("Gyan.FFmpeg", StringComparison.OrdinalIgnoreCase) >= 0)
                                 .OrderByDescending(File.GetLastWriteTimeUtc))
                    {
                        yield return path;
                    }
                }

                yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            }

            yield return @"C:\Program Files\ffmpeg\bin\ffmpeg.exe";
            yield return @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe";
            yield return @"C:\ProgramData\chocolatey\bin\ffmpeg.exe";
            yield return @"C:\Program Files\ShareX\ffmpeg.exe";
        }

        yield return "ffmpeg";
    }

    private static string? ResolveExecutable(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (Path.IsPathRooted(candidate) ||
            candidate.Contains(Path.DirectorySeparatorChar) ||
            candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };
        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                var fileName = candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    ? candidate
                    : candidate + extension;
                var executablePath = Path.Combine(directory, fileName);
                if (File.Exists(executablePath))
                {
                    return Path.GetFullPath(executablePath);
                }
            }
        }

        return null;
    }

    private static string ResolveEncoderPreset(string encoder, string qualityMode)
    {
        if (encoder.IndexOf("nvenc", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (string.Equals(qualityMode, "Performance", StringComparison.OrdinalIgnoreCase))
            {
                return "p1";
            }

            return string.Equals(qualityMode, "Quality", StringComparison.OrdinalIgnoreCase) ? "p6" : "p4";
        }

        if (string.Equals(qualityMode, "Performance", StringComparison.OrdinalIgnoreCase))
        {
            return "ultrafast";
        }

        return string.Equals(qualityMode, "Quality", StringComparison.OrdinalIgnoreCase) ? "medium" : "veryfast";
    }

    private static string NormalizeArgumentToken(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) &&
               Regex.IsMatch(trimmed, "^[A-Za-z0-9_.+-]+$")
            ? trimmed
            : fallback;
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string CollapseWhitespace(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp capture cleanup must not hide the preflight result.
        }
    }

    private readonly record struct FfmpegProbeResult(int ExitCode, string Output);
}

public sealed class NullCapturePreflightRunner : ICapturePreflightRunner
{
    public CapturePreflightReport Check(
        ControlPanelSettings settings,
        DisplayInfoSnapshot displayInfo,
        IReadOnlyList<int> instanceIndexes)
    {
        return new CapturePreflightReport
        {
            Status = "Skipped",
            Summary = "Capture preflight skipped.",
            Detail = "No capture preflight runner is configured.",
            CheckedAtUtc = DateTimeOffset.UtcNow,
            CaptureEngine = settings.CaptureEngine,
            Encoder = settings.Encoder,
            MonitorIndex = settings.MonitorIndex,
            CaptureWidth = settings.CaptureWidth,
            CaptureHeight = settings.CaptureHeight
        };
    }
}

public sealed class UnavailableDisplayInfoProvider : IDisplayInfoProvider
{
    public DisplayInfoSnapshot GetDisplays()
    {
        return new DisplayInfoSnapshot
        {
            Status = "Unavailable",
            Summary = "Display detection is not configured."
        };
    }
}

public static class CaptureLayoutValidator
{
    public static string? Validate(
        ControlPanelSettings settings,
        DisplayInfoSnapshot displayInfo,
        IReadOnlyList<int> instanceIndexes)
    {
        if (displayInfo == null ||
            !string.Equals(displayInfo.Status, "Ready", StringComparison.OrdinalIgnoreCase) ||
            displayInfo.Displays.Count == 0)
        {
            return null;
        }

        var display = displayInfo.Displays
            .FirstOrDefault(item => item.Index == settings.MonitorIndex);
        if (display == null)
        {
            return "Monitor " + (settings.MonitorIndex + 1).ToString(CultureInfo.InvariantCulture) +
                   " is selected, but Windows only reported " +
                   displayInfo.Displays.Count.ToString(CultureInfo.InvariantCulture) +
                   " display" + (displayInfo.Displays.Count == 1 ? "." : "s.");
        }

        var width = Math.Max(1, display.Width);
        var height = Math.Max(1, display.Height);
        var captureWidth = Math.Max(1, settings.CaptureWidth);
        var captureHeight = Math.Max(1, settings.CaptureHeight);
        var indexes = instanceIndexes.Count > 0
            ? instanceIndexes
            : Enumerable.Range(0, Math.Max(1, settings.InstanceCount)).ToArray();

        foreach (var index in indexes)
        {
            var offsetX = (index % 2) * captureWidth;
            var offsetY = (index / 2) * captureHeight;
            if (offsetX + captureWidth <= width && offsetY + captureHeight <= height)
            {
                continue;
            }

            return "The selected layout does not fit " +
                   WindowsDisplayInfoProvider.BuildDisplayLabel(
                       display.Index,
                       display.FriendlyName,
                       display.Width,
                       display.Height,
                       display.IsPrimary) +
                   ". Instance " + (index + 1).ToString(CultureInfo.InvariantCulture) +
                   " would capture " + captureWidth.ToString(CultureInfo.InvariantCulture) +
                   "x" + captureHeight.ToString(CultureInfo.InvariantCulture) +
                   " at offset " + offsetX.ToString(CultureInfo.InvariantCulture) +
                   "," + offsetY.ToString(CultureInfo.InvariantCulture) +
                   ". Choose fewer workers or a smaller feed preset.";
        }

        return null;
    }
}
