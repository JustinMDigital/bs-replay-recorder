namespace BSAutoReplayRecorder.RecorderHost;

public sealed class FfmpegExecutableResolver
{
    private const string EnvironmentVariableName = "BSARR_FFMPEG_PATH";

    private static readonly string[] WindowsCommonPaths =
    {
        @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
        @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
        @"C:\Program Files\ShareX\ffmpeg.exe"
    };

    public string? Resolve(string configuredPath)
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

    public IReadOnlyList<string> FindCandidates(string configuredPath)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates(configuredPath))
        {
            var resolved = ResolveCandidate(candidate);
            if (resolved != null && seen.Add(resolved))
            {
                results.Add(resolved);
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateCandidates(string configuredPath)
    {
        var hasExplicitConfiguredPath = !string.IsNullOrWhiteSpace(configuredPath) &&
                                        !string.Equals(configuredPath.Trim(), "ffmpeg", StringComparison.OrdinalIgnoreCase);

        if (hasExplicitConfiguredPath)
        {
            yield return configuredPath;
        }

        var environmentPath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            yield return environmentPath;
        }

        if (!hasExplicitConfiguredPath && !string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        if (!OperatingSystem.IsWindows())
        {
            yield return "ffmpeg";
            yield break;
        }

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

        yield return "ffmpeg";

        foreach (var path in WindowsCommonPaths)
        {
            yield return path;
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
}
