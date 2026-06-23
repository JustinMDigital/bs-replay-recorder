using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BSAutoReplayRecorder.Core.Replay;

namespace BSAutoReplayRecorder.ControlPanel;

public interface IRecordingChapterEmbedder
{
    RecordingChapterEmbedResult Embed(string recordingPath, IReadOnlyList<RecordingChapter> chapters);
}

public sealed class RecordingChapter
{
    public double StartSeconds { get; set; }

    public double EndSeconds { get; set; }

    public string Title { get; set; } = "";
}

public sealed class RecordingChapterEmbedResult
{
    public bool Succeeded { get; set; }

    public bool Skipped { get; set; }

    public int ChapterCount { get; set; }

    public string Detail { get; set; } = "";

    public string Error { get; set; } = "";

    public static RecordingChapterEmbedResult Success(int chapterCount, string detail = "")
    {
        return new RecordingChapterEmbedResult
        {
            Succeeded = true,
            ChapterCount = chapterCount,
            Detail = detail
        };
    }

    public static RecordingChapterEmbedResult Skip(string detail = "")
    {
        return new RecordingChapterEmbedResult
        {
            Succeeded = true,
            Skipped = true,
            Detail = detail
        };
    }

    public static RecordingChapterEmbedResult Failure(string error, int chapterCount = 0)
    {
        return new RecordingChapterEmbedResult
        {
            Succeeded = false,
            ChapterCount = chapterCount,
            Error = error
        };
    }
}

public sealed class FfmpegRecordingChapterEmbedder : IRecordingChapterEmbedder
{
    private const string EnvironmentVariableName = "BSARR_FFMPEG_PATH";
    private static readonly TimeSpan RemuxTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] WindowsCommonPaths =
    {
        @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
        @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
        @"C:\Program Files\ShareX\ffmpeg.exe"
    };

    public RecordingChapterEmbedResult Embed(string recordingPath, IReadOnlyList<RecordingChapter> chapters)
    {
        if (chapters == null || chapters.Count == 0)
        {
            return RecordingChapterEmbedResult.Skip("No recording chapters to embed.");
        }

        if (string.IsNullOrWhiteSpace(recordingPath))
        {
            return RecordingChapterEmbedResult.Failure("Completed recording did not include an output path.", chapters.Count);
        }

        if (!File.Exists(recordingPath))
        {
            return RecordingChapterEmbedResult.Failure("Completed recording file was not found: " + recordingPath, chapters.Count);
        }

        var extension = Path.GetExtension(recordingPath);
        if (!string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return RecordingChapterEmbedResult.Failure(
                "Completed recording is not an .mkv or .mp4 file: " + recordingPath,
                chapters.Count);
        }

        var ffmpegPath = ResolveExecutablePath("ffmpeg");
        if (ffmpegPath == null)
        {
            return RecordingChapterEmbedResult.Failure(
                "ffmpeg was not found, so bookmark chapters could not be embedded. Install FFmpeg or set BSARR_FFMPEG_PATH.",
                chapters.Count);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(recordingPath)) ?? Directory.GetCurrentDirectory();
        var nonce = Guid.NewGuid().ToString("N");
        var metadataPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(recordingPath) + ".chapters-" + nonce + ".ffmetadata");
        var tempOutputPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(recordingPath) + ".chapters-" + nonce + extension);

        try
        {
            File.WriteAllText(metadataPath, BuildMetadata(chapters), new UTF8Encoding(false));
            var result = RunFfmpeg(ffmpegPath, recordingPath, metadataPath, tempOutputPath);
            if (!result.Succeeded)
            {
                return RecordingChapterEmbedResult.Failure(result.Error, chapters.Count);
            }

            ReplaceRecording(recordingPath, tempOutputPath);
            return RecordingChapterEmbedResult.Success(chapters.Count, "Embedded " + chapters.Count + " bookmark chapter(s).");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return RecordingChapterEmbedResult.Failure("ffmpeg chapter embedding failed: " + ex.Message, chapters.Count);
        }
        finally
        {
            TryDelete(metadataPath);
            TryDelete(tempOutputPath);
        }
    }

    private static string BuildMetadata(IReadOnlyList<RecordingChapter> chapters)
    {
        var builder = new StringBuilder();
        builder.AppendLine(";FFMETADATA1");
        foreach (var chapter in chapters)
        {
            var start = ToMilliseconds(chapter.StartSeconds);
            var end = ToMilliseconds(chapter.EndSeconds);
            if (end <= start)
            {
                end = start + 100;
            }

            builder.AppendLine("[CHAPTER]");
            builder.AppendLine("TIMEBASE=1/1000");
            builder.AppendLine("START=" + start.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("END=" + end.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("title=" + EscapeMetadataValue(chapter.Title));
        }

        return builder.ToString();
    }

    private static long ToMilliseconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return 0;
        }

        return Math.Max(0, (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
    }

    private static string EscapeMetadataValue(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "Bookmark" : value.Trim();
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == '\r' || character == '\n')
            {
                builder.Append(' ');
                continue;
            }

            if (character == '\\' || character == '=' || character == ';' || character == '#')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static RecordingChapterEmbedResult RunFfmpeg(
        string ffmpegPath,
        string recordingPath,
        string metadataPath,
        string tempOutputPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(recordingPath);
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(metadataPath);
        process.StartInfo.ArgumentList.Add("-map");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("-map_metadata");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("-map_chapters");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("copy");
        process.StartInfo.ArgumentList.Add(tempOutputPath);

        if (!process.Start())
        {
            return RecordingChapterEmbedResult.Failure("ffmpeg could not be started.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(RemuxTimeout))
        {
            TryKill(process);
            return RecordingChapterEmbedResult.Failure("ffmpeg timed out while embedding bookmark chapters.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            return RecordingChapterEmbedResult.Failure("ffmpeg failed while embedding bookmark chapters: " + NormalizeProcessText(stderr, stdout));
        }

        if (!File.Exists(tempOutputPath))
        {
            return RecordingChapterEmbedResult.Failure("ffmpeg did not create a chaptered output file.");
        }

        return RecordingChapterEmbedResult.Success(0);
    }

    private static void ReplaceRecording(string recordingPath, string tempOutputPath)
    {
        try
        {
            File.Replace(tempOutputPath, recordingPath, null);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            var backupPath = recordingPath + ".pre-chapters-" + Guid.NewGuid().ToString("N") + ".bak";
            File.Move(recordingPath, backupPath);
            try
            {
                File.Move(tempOutputPath, recordingPath);
                TryDelete(backupPath);
            }
            catch
            {
                if (!File.Exists(recordingPath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, recordingPath);
                }

                throw;
            }
        }
    }

    private static string NormalizeProcessText(string primary, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        text = text.Trim();
        return string.IsNullOrWhiteSpace(text) ? "no ffmpeg error output" : text;
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

        if (!string.IsNullOrWhiteSpace(configuredPath))
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

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary files are best-effort cleanup.
        }
    }
}

internal static class RecordingChapterPlanner
{
    public static IReadOnlyList<RecordingChapter> Create(
        IReadOnlyList<BeatmapBookmark> bookmarks,
        ReplayQueueRecord replay,
        BsorInfo replayInfo)
    {
        if (bookmarks.Count == 0)
        {
            return Array.Empty<RecordingChapter>();
        }

        var songStartSeconds = Math.Max(0, replayInfo.StartTime);
        var speed = replayInfo.Speed <= 0 ? 1 : replayInfo.Speed;
        var playbackLengthSeconds = ResolvePlaybackLengthSeconds(replay, replayInfo);
        var ordered = bookmarks
            .Select((bookmark, index) => new
            {
                StartSeconds = (bookmark.SongSeconds - songStartSeconds) / speed,
                Title = string.IsNullOrWhiteSpace(bookmark.Name)
                    ? "Bookmark " + (index + 1).ToString(CultureInfo.InvariantCulture)
                    : bookmark.Name.Trim()
            })
            .Where(item => item.StartSeconds >= -0.001)
            .Where(item => playbackLengthSeconds <= 0 || item.StartSeconds <= playbackLengthSeconds + 0.001)
            .OrderBy(item => item.StartSeconds)
            .ToList();

        var chapters = new List<RecordingChapter>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var start = Math.Max(0, ordered[index].StartSeconds);
            var nextStart = ordered
                .Skip(index + 1)
                .Select(item => item.StartSeconds)
                .FirstOrDefault(value => value > start + 0.001);
            var end = nextStart > 0
                ? nextStart
                : playbackLengthSeconds > start
                    ? playbackLengthSeconds
                    : start + 0.1;
            if (playbackLengthSeconds > 0)
            {
                end = Math.Min(end, playbackLengthSeconds);
            }

            if (end <= start)
            {
                end = start + 0.1;
            }

            chapters.Add(new RecordingChapter
            {
                StartSeconds = start,
                EndSeconds = end,
                Title = ordered[index].Title
            });
        }

        return chapters;
    }

    private static double ResolvePlaybackLengthSeconds(ReplayQueueRecord replay, BsorInfo replayInfo)
    {
        if (replay.EstimatedSeconds > 0)
        {
            return replay.EstimatedSeconds;
        }

        var estimated = replayInfo.EstimatedPlaybackLength.TotalSeconds;
        return estimated > 0 ? estimated : 0;
    }
}

internal sealed class BeatmapBookmark
{
    public double Beat { get; set; }

    public double SongSeconds { get; set; }

    public string Name { get; set; } = "";
}

internal static class BeatmapBookmarkReader
{
    public static IReadOnlyList<BeatmapBookmark> TryRead(
        string levelDirectory,
        string requestedDifficulty,
        string requestedMode)
    {
        var infoPath = FindFile(levelDirectory, "Info.dat");
        if (infoPath == null)
        {
            return Array.Empty<BeatmapBookmark>();
        }

        try
        {
            using var infoStream = File.OpenRead(infoPath);
            using var infoDocument = JsonDocument.Parse(infoStream);
            var info = infoDocument.RootElement;
            var difficulty = FindDifficulty(info, requestedDifficulty, requestedMode);
            if (!difficulty.HasValue)
            {
                return Array.Empty<BeatmapBookmark>();
            }

            var beatmapFileName = GetString(
                difficulty.Value.Difficulty,
                "_beatmapFilename",
                "beatmapDataFilename",
                "_beatmapDataFilename");
            if (string.IsNullOrWhiteSpace(beatmapFileName))
            {
                return Array.Empty<BeatmapBookmark>();
            }

            var beatmapPath = Path.Combine(levelDirectory, beatmapFileName);
            if (!File.Exists(beatmapPath) || !IsPathInside(levelDirectory, beatmapPath))
            {
                return Array.Empty<BeatmapBookmark>();
            }

            using var beatmapStream = File.OpenRead(beatmapPath);
            using var beatmapDocument = JsonDocument.Parse(beatmapStream);
            var beatmap = beatmapDocument.RootElement;
            if (!TryGetBookmarks(beatmap, out var bookmarksElement))
            {
                return Array.Empty<BeatmapBookmark>();
            }

            var baseBpm = GetDouble(info, "_beatsPerMinute", "beatsPerMinute") ?? 120;
            var bpmEvents = ReadBpmEvents(beatmap);
            var bookmarks = new List<BeatmapBookmark>();
            var index = 0;
            foreach (var bookmarkElement in bookmarksElement.EnumerateArray())
            {
                var beat = GetDouble(bookmarkElement, "_time", "b");
                if (!beat.HasValue || beat.Value < 0)
                {
                    continue;
                }

                index++;
                bookmarks.Add(new BeatmapBookmark
                {
                    Beat = beat.Value,
                    SongSeconds = BeatToSeconds(beat.Value, baseBpm, bpmEvents),
                    Name = GetString(bookmarkElement, "_name", "n") ?? "Bookmark " + index.ToString(CultureInfo.InvariantCulture)
                });
            }

            return bookmarks
                .OrderBy(bookmark => bookmark.Beat)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return Array.Empty<BeatmapBookmark>();
        }
    }

    private static (JsonElement Difficulty, string Mode)? FindDifficulty(
        JsonElement info,
        string requestedDifficulty,
        string requestedMode)
    {
        if (!TryGetArray(info, out var sets, "_difficultyBeatmapSets", "difficultyBeatmapSets"))
        {
            return null;
        }

        var normalizedDifficulty = MapCardMetadataText.NormalizeDifficulty(requestedDifficulty);
        var normalizedMode = MapCardMetadataText.NormalizeMode(requestedMode);
        (JsonElement Difficulty, string Mode)? fallback = null;

        foreach (var set in sets.EnumerateArray())
        {
            var mode = GetString(set, "_beatmapCharacteristicName", "beatmapCharacteristicName") ?? "";
            var setMode = MapCardMetadataText.NormalizeMode(mode);
            if (normalizedMode.Length > 0 && setMode.Length > 0 && setMode != normalizedMode)
            {
                if (!fallback.HasValue && TryFirstDifficulty(set, mode, out var first))
                {
                    fallback = first;
                }

                continue;
            }

            if (!TryGetArray(set, out var difficulties, "_difficultyBeatmaps", "difficultyBeatmaps"))
            {
                continue;
            }

            foreach (var difficulty in difficulties.EnumerateArray())
            {
                var value = GetString(difficulty, "_difficulty", "difficulty") ?? "";
                if (MapCardMetadataText.NormalizeDifficulty(value) == normalizedDifficulty)
                {
                    return (difficulty, mode);
                }
            }

            if (!fallback.HasValue && TryFirstDifficulty(set, mode, out var fallbackDifficulty))
            {
                fallback = fallbackDifficulty;
            }
        }

        return fallback;
    }

    private static bool TryFirstDifficulty(
        JsonElement set,
        string mode,
        out (JsonElement Difficulty, string Mode) difficulty)
    {
        difficulty = default;
        if (!TryGetArray(set, out var difficulties, "_difficultyBeatmaps", "difficultyBeatmaps"))
        {
            return false;
        }

        foreach (var item in difficulties.EnumerateArray())
        {
            difficulty = (item, mode);
            return true;
        }

        return false;
    }

    private static bool TryGetBookmarks(JsonElement beatmap, out JsonElement bookmarks)
    {
        bookmarks = default;
        foreach (var customDataName in new[] { "_customData", "customData" })
        {
            if (!beatmap.TryGetProperty(customDataName, out var customData) ||
                customData.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryGetArray(customData, out bookmarks, "_bookmarks", "bookmarks"))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<BpmEvent> ReadBpmEvents(JsonElement beatmap)
    {
        var events = new List<BpmEvent>();
        if (TryGetArray(beatmap, out var bpmEvents, "bpmEvents"))
        {
            foreach (var bpmEvent in bpmEvents.EnumerateArray())
            {
                var beat = GetDouble(bpmEvent, "b", "_time");
                var bpm = GetDouble(bpmEvent, "m", "_BPM", "bpm");
                if (beat.HasValue && bpm.HasValue && beat.Value >= 0 && bpm.Value > 0)
                {
                    events.Add(new BpmEvent(beat.Value, bpm.Value));
                }
            }
        }

        if (TryGetArray(beatmap, out var bpmChanges, "_BPMChanges"))
        {
            foreach (var bpmChange in bpmChanges.EnumerateArray())
            {
                var beat = GetDouble(bpmChange, "_time", "time", "b");
                var bpm = GetDouble(bpmChange, "_BPM", "bpm", "m");
                if (beat.HasValue && bpm.HasValue && beat.Value >= 0 && bpm.Value > 0)
                {
                    events.Add(new BpmEvent(beat.Value, bpm.Value));
                }
            }
        }

        return events
            .OrderBy(item => item.Beat)
            .ToList();
    }

    private static double BeatToSeconds(double beat, double baseBpm, IReadOnlyList<BpmEvent> bpmEvents)
    {
        var bpm = baseBpm > 0 ? baseBpm : 120;
        var previousBeat = 0.0;
        var seconds = 0.0;
        foreach (var bpmEvent in bpmEvents)
        {
            if (bpmEvent.Beat > beat)
            {
                break;
            }

            if (bpmEvent.Beat > previousBeat)
            {
                seconds += (bpmEvent.Beat - previousBeat) * 60.0 / bpm;
                previousBeat = bpmEvent.Beat;
            }

            bpm = bpmEvent.Bpm;
        }

        if (beat > previousBeat)
        {
            seconds += (beat - previousBeat) * 60.0 / bpm;
        }

        return seconds;
    }

    private static bool TryGetArray(JsonElement element, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        array = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static double? GetDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return null;
    }

    private static string? FindFile(string directory, string fileName)
    {
        var directPath = Path.Combine(directory, fileName);
        if (File.Exists(directPath))
        {
            return directPath;
        }

        try
        {
            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsPathInside(string rootDirectory, string candidatePath)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct BpmEvent(double Beat, double Bpm);
}
