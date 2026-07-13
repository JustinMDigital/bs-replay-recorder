using System.Diagnostics;

namespace BSAutoReplayRecorder.ControlPanel;

public interface IFfmpegSetupService
{
    FfmpegSetupReport Check(string configuredPath);

    FfmpegSetupReport Install(string configuredPath);
}

public sealed class FfmpegSetupService : IFfmpegSetupService
{
    private const string WingetPackageId = "Gyan.FFmpeg";

    public FfmpegSetupReport Check(string configuredPath)
    {
        var report = new FfmpegSetupReport
        {
            CheckedAtUtc = DateTimeOffset.UtcNow,
            CanInstall = FindOnPath("winget") != null
        };

        var ffmpegPath = ResolveFfmpeg(configuredPath);
        if (ffmpegPath == null)
        {
            report.Status = "Missing";
            report.Summary = "FFmpeg and ffprobe are required before recording.";
            report.Detail = report.CanInstall
                ? "Install the tested FFmpeg package from this setup screen, or set a local ffmpeg.exe path in Advanced Settings."
                : "Install FFmpeg with ffprobe, then set the full ffmpeg.exe path in Advanced Settings.";
            return report;
        }

        var ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe.exe");
        if (!File.Exists(ffprobePath))
        {
            report.Status = "Missing";
            report.Summary = "ffprobe is missing next to FFmpeg.";
            report.Detail = "Use a full FFmpeg install that contains both ffmpeg.exe and ffprobe.exe.";
            report.FfmpegPath = ffmpegPath;
            return report;
        }

        report.Status = "Ready";
        report.Summary = "FFmpeg and ffprobe are ready.";
        report.Detail = "Capture verification will test the selected monitor, NVENC encoder, and NVIDIA driver.";
        report.FfmpegPath = ffmpegPath;
        report.FfprobePath = ffprobePath;
        return report;
    }

    public FfmpegSetupReport Install(string configuredPath)
    {
        var existing = Check(configuredPath);
        if (string.Equals(existing.Status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        var wingetPath = FindOnPath("winget");
        if (wingetPath == null)
        {
            return new FfmpegSetupReport
            {
                Status = "Missing",
                Summary = "FFmpeg was not installed because WinGet is unavailable.",
                Detail = "Install FFmpeg with ffprobe manually, then set the full ffmpeg.exe path in Advanced Settings.",
                CheckedAtUtc = DateTimeOffset.UtcNow,
                CanInstall = false
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = wingetPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("install");
        startInfo.ArgumentList.Add("--id");
        startInfo.ArgumentList.Add(WingetPackageId);
        startInfo.ArgumentList.Add("--exact");
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add("winget");
        startInfo.ArgumentList.Add("--accept-package-agreements");
        startInfo.ArgumentList.Add("--accept-source-agreements");

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Windows did not start WinGet.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("WinGet did not finish installing FFmpeg within five minutes.");
            }

            var output = (stdoutTask.GetAwaiter().GetResult() + "\n" + stderrTask.GetAwaiter().GetResult()).Trim();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "WinGet could not install FFmpeg" +
                    (string.IsNullOrWhiteSpace(output) ? "." : ": " + CollapseWhitespace(output)));
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not install FFmpeg with WinGet: " + ex.Message, ex);
        }

        var installed = Check("");
        if (!string.Equals(installed.Status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "WinGet finished, but FFmpeg and ffprobe were not found afterward. Restart Replay Recorder or set ffmpeg.exe in Advanced Settings.");
        }

        installed.Summary = "FFmpeg was installed and is ready.";
        return installed;
    }

    private static string? ResolveFfmpeg(string configuredPath)
    {
        foreach (var candidate in EnumerateCandidates(configuredPath))
        {
            var resolved = ResolveExecutable(candidate);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        var environmentPath = Environment.GetEnvironmentVariable("BSARR_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            yield return environmentPath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetPackages))
            {
                foreach (var path in Directory.EnumerateFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories)
                             .Where(path => path.Contains(WingetPackageId, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    yield return path;
                }
            }

            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
        }

        yield return "ffmpeg";
        yield return @"C:\Program Files\ffmpeg\bin\ffmpeg.exe";
        yield return @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe";
        yield return @"C:\ProgramData\chocolatey\bin\ffmpeg.exe";
        yield return @"C:\Program Files\ShareX\ffmpeg.exe";
    }

    private static string? ResolveExecutable(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed) || trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(trimmed) ? Path.GetFullPath(trimmed) : null;
        }

        return FindOnPath(trimmed);
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory.Trim(), fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : fileName + ".exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string CollapseWhitespace(string value)
    {
        return System.Text.RegularExpressions.Regex.Replace(value, "\\s+", " ").Trim();
    }
}
