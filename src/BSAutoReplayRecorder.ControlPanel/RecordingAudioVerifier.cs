using System.Diagnostics;
using System.Text.Json;

namespace BSAutoReplayRecorder.ControlPanel;

public interface IRecordingAudioVerifier
{
    RecordingAudioVerificationResult Verify(string recordingPath);
}

public sealed class RecordingAudioVerificationResult
{
    public bool HasAudio { get; set; }

    public int VideoStreams { get; set; }

    public int AudioStreams { get; set; }

    public string AudioCodecs { get; set; } = "";

    public string Error { get; set; } = "";
}

public sealed class FfprobeRecordingAudioVerifier : IRecordingAudioVerifier
{
    private const string EnvironmentVariableName = "BSARR_FFPROBE_PATH";
    private const string FfmpegEnvironmentVariableName = "BSARR_FFMPEG_PATH";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] WindowsCommonPaths =
    {
        @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
        @"C:\Program Files (x86)\ffmpeg\bin\ffprobe.exe",
        @"C:\ProgramData\chocolatey\bin\ffprobe.exe",
        @"C:\Program Files\ShareX\ffprobe.exe"
    };

    public RecordingAudioVerificationResult Verify(string recordingPath)
    {
        if (string.IsNullOrWhiteSpace(recordingPath))
        {
            return Failure("Completed recording did not include an output path.");
        }

        if (!File.Exists(recordingPath))
        {
            return Failure("Completed recording file was not found: " + recordingPath);
        }

        var extension = Path.GetExtension(recordingPath);
        if (!string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return Failure("Completed recording is not an .mkv or .mp4 file: " + recordingPath);
        }

        var ffprobePath = ResolveExecutablePath("ffprobe");
        if (ffprobePath == null)
        {
            return Failure(
                "ffprobe was not found, so the completed recording audio stream could not be verified. " +
                "Install FFmpeg/ffprobe or set BSARR_FFPROBE_PATH.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-show_entries");
        process.StartInfo.ArgumentList.Add("stream=index,codec_type,codec_name");
        process.StartInfo.ArgumentList.Add("-of");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add(recordingPath);

        try
        {
            if (!process.Start())
            {
                return Failure("ffprobe could not be started.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProbeTimeout))
            {
                TryKill(process);
                return Failure("ffprobe timed out while checking the completed recording.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return Failure("ffprobe failed while checking the completed recording: " + NormalizeProbeText(stderr, stdout));
            }

            return ParseProbeJson(stdout);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failure("ffprobe failed while checking the completed recording: " + ex.Message);
        }
    }

    private static RecordingAudioVerificationResult ParseProbeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failure("ffprobe returned no stream data for the completed recording.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("streams", out var streams) ||
                streams.ValueKind != JsonValueKind.Array)
            {
                return Failure("ffprobe stream data did not contain a streams array.");
            }

            var audioStreams = 0;
            var videoStreams = 0;
            var audioCodecs = new List<string>();

            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = ReadStringProperty(stream, "codec_type");
                if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                {
                    audioStreams++;
                    var codecName = ReadStringProperty(stream, "codec_name");
                    if (!string.IsNullOrWhiteSpace(codecName))
                    {
                        audioCodecs.Add(codecName);
                    }
                }
                else if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase))
                {
                    videoStreams++;
                }
            }

            return new RecordingAudioVerificationResult
            {
                HasAudio = audioStreams > 0,
                AudioStreams = audioStreams,
                VideoStreams = videoStreams,
                AudioCodecs = string.Join(",", audioCodecs.Distinct(StringComparer.OrdinalIgnoreCase)),
                Error = audioStreams > 0
                    ? ""
                    : "Completed recording is missing an audio stream."
            };
        }
        catch (JsonException ex)
        {
            return Failure("ffprobe returned invalid stream data: " + ex.Message);
        }
    }

    private static string ReadStringProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private static RecordingAudioVerificationResult Failure(string error)
    {
        return new RecordingAudioVerificationResult
        {
            HasAudio = false,
            Error = error
        };
    }

    private static string NormalizeProbeText(string primary, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        text = text.Trim();
        return string.IsNullOrWhiteSpace(text) ? "no ffprobe error output" : text;
    }

    private static string? ResolveExecutablePath(string configuredPath)
    {
        foreach (var candidate in EnumerateCandidates(configuredPath))
        {
            var resolved = ResolveCandidate(candidate);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string configuredPath)
    {
        var environmentPath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            yield return environmentPath;
        }

        foreach (var siblingPath in EnumerateFfmpegSiblingCandidates())
        {
            yield return siblingPath;
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        if (!OperatingSystem.IsWindows())
        {
            yield return "ffprobe";
            yield break;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var packageDirectory = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packageDirectory))
            {
                foreach (var path in Directory
                             .EnumerateFiles(packageDirectory, "ffprobe.exe", SearchOption.AllDirectories)
                             .Where(path => path.IndexOf("Gyan.FFmpeg", StringComparison.OrdinalIgnoreCase) >= 0)
                             .OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    yield return path;
                }
            }

            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffprobe.exe");
        }

        yield return "ffprobe";

        foreach (var path in WindowsCommonPaths)
        {
            yield return path;
        }
    }

    private static IEnumerable<string> EnumerateFfmpegSiblingCandidates()
    {
        var ffmpegEnvironmentPath = Environment.GetEnvironmentVariable(FfmpegEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(ffmpegEnvironmentPath))
        {
            yield break;
        }

        string? directory;
        try
        {
            directory = Path.GetDirectoryName(ffmpegEnvironmentPath);
        }
        catch (ArgumentException)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(directory))
        {
            yield return Path.Combine(directory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        }
    }

    private static string? ResolveCandidate(string candidate)
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
                var pathCandidate = Path.Combine(directory, fileName);
                if (File.Exists(pathCandidate))
                {
                    return Path.GetFullPath(pathCandidate);
                }
            }
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may have exited between the timeout check and Kill.
        }
    }
}
