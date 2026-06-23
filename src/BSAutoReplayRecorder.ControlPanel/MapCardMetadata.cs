using System.Globalization;
using System.Text.Json;

namespace BSAutoReplayRecorder.ControlPanel;

public sealed class MapCardExport
{
    public string CollectionId { get; set; } = "";

    public string CollectionName { get; set; } = "";

    public List<MapCardExportItem> Items { get; set; } = new List<MapCardExportItem>();
}

public sealed class MapCardExportItem
{
    public string Id { get; set; } = "";

    public int SequenceNumber { get; set; }

    public string SongName { get; set; } = "";

    public string Artist { get; set; } = "";

    public string MapAuthor { get; set; } = "";

    public string Difficulty { get; set; } = "";

    public string Mode { get; set; } = "";

    public double? NotesPerSecond { get; set; }

    public double? BeatsPerMinute { get; set; }

    public int? NoteCount { get; set; }

    public double LengthSeconds { get; set; }

    public string BeatSaverKey { get; set; } = "";

    public string LevelHash { get; set; } = "";

    public string CoverArtUrl { get; set; } = "";

    public string Category { get; set; } = "";

    public string MetadataStatus { get; set; } = "";

    public string MetadataDetail { get; set; } = "";
}

public sealed class BeatSaverMapCardMetadata
{
    public string SongName { get; set; } = "";

    public string Artist { get; set; } = "";

    public string MapAuthor { get; set; } = "";

    public string Difficulty { get; set; } = "";

    public string Mode { get; set; } = "";

    public double? NotesPerSecond { get; set; }

    public double? BeatsPerMinute { get; set; }

    public int? NoteCount { get; set; }

    public double? LengthSeconds { get; set; }

    public string BeatSaverKey { get; set; } = "";

    public string CoverArtUrl { get; set; } = "";
}

internal sealed class LocalMapCardMetadata
{
    public string SongName { get; set; } = "";

    public string Artist { get; set; } = "";

    public string MapAuthor { get; set; } = "";

    public string Difficulty { get; set; } = "";

    public string Mode { get; set; } = "";

    public double? NotesPerSecond { get; set; }

    public double? BeatsPerMinute { get; set; }

    public int? NoteCount { get; set; }

    public double? LengthSeconds { get; set; }
}

internal static class MapCardMetadataText
{
    public static string DisplayDifficulty(string? difficulty)
    {
        var normalized = NormalizeDifficulty(difficulty);
        if (normalized == "expertplus") return "Expert+";
        if (normalized == "expert") return "Expert";
        if (normalized == "hard") return "Hard";
        if (normalized == "normal") return "Normal";
        if (normalized == "easy") return "Easy";
        return NormalizeNullable(difficulty) ?? "";
    }

    public static string NormalizeDifficulty(string? difficulty)
    {
        var raw = difficulty ?? "";
        var text = NormalizeSearch(difficulty);
        if (text.Contains("expertplus", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("Expert+", StringComparison.OrdinalIgnoreCase))
        {
            return "expertplus";
        }

        if (text.Contains("expert", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("plus", StringComparison.OrdinalIgnoreCase))
        {
            return "expertplus";
        }

        if (text.Contains("expert", StringComparison.OrdinalIgnoreCase)) return "expert";
        if (text.Contains("hard", StringComparison.OrdinalIgnoreCase)) return "hard";
        if (text.Contains("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
        if (text.Contains("easy", StringComparison.OrdinalIgnoreCase)) return "easy";
        return text;
    }

    public static string NormalizeMode(string? mode)
    {
        var text = NormalizeSearch(mode);
        if (text.Contains("standard", StringComparison.OrdinalIgnoreCase)) return "standard";
        if (text.Contains("onesaber", StringComparison.OrdinalIgnoreCase)) return "onesaber";
        if (text.Contains("noarrows", StringComparison.OrdinalIgnoreCase)) return "noarrows";
        if (text.Contains("lawless", StringComparison.OrdinalIgnoreCase)) return "lawless";
        return text;
    }

    public static string? NormalizeNullable(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string NormalizeSearch(string? value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in value ?? "")
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}

internal static class LocalMapCardMetadataReader
{
    public static LocalMapCardMetadata? TryRead(
        string levelDirectory,
        string requestedDifficulty,
        string requestedMode,
        double fallbackLengthSeconds)
    {
        var infoPath = FindFile(levelDirectory, "Info.dat");
        if (infoPath == null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(infoPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var result = new LocalMapCardMetadata
            {
                SongName = GetString(root, "_songName", "songName") ?? "",
                Artist = GetString(root, "_songAuthorName", "songAuthorName") ?? "",
                MapAuthor = GetString(root, "_levelAuthorName", "levelAuthorName") ?? "",
                BeatsPerMinute = GetDouble(root, "_beatsPerMinute", "beatsPerMinute")
            };

            var difficulty = FindDifficulty(root, requestedDifficulty, requestedMode);
            if (difficulty.HasValue)
            {
                var difficultyElement = difficulty.Value.Difficulty;
                result.Difficulty = GetString(difficultyElement, "_difficulty", "difficulty") ?? "";
                result.Mode = difficulty.Value.Mode;
                var beatmapFileName = GetString(
                    difficultyElement,
                    "_beatmapFilename",
                    "beatmapDataFilename",
                    "_beatmapDataFilename");
                var noteCount = CountColorNotes(levelDirectory, beatmapFileName);
                if (noteCount.HasValue)
                {
                    result.NoteCount = noteCount;
                    if (fallbackLengthSeconds > 0)
                    {
                        result.NotesPerSecond = Math.Round(noteCount.Value / fallbackLengthSeconds, 2);
                    }
                }
            }

            if (fallbackLengthSeconds > 0)
            {
                result.LengthSeconds = fallbackLengthSeconds;
            }

            return result;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
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

                fallback ??= (difficulty, mode);
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

        var first = difficulties.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined)
        {
            return false;
        }

        difficulty = (first, mode);
        return true;
    }

    private static int? CountColorNotes(string levelDirectory, string? beatmapFileName)
    {
        if (string.IsNullOrWhiteSpace(beatmapFileName))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(levelDirectory, beatmapFileName));
        if (!IsPathInsideDirectory(path, levelDirectory) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (TryGetArray(root, out var colorNotes, "colorNotes"))
            {
                return colorNotes.GetArrayLength();
            }

            if (TryGetArray(root, out var notes, "_notes", "notes"))
            {
                var count = 0;
                foreach (var note in notes.EnumerateArray())
                {
                    var type = GetInt(note, "_type", "type");
                    if (!type.HasValue || type == 0 || type == 1)
                    {
                        count++;
                    }
                }

                return count;
            }

            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindFile(string directory, string fileName)
    {
        var exact = Path.Combine(directory, fileName);
        if (File.Exists(exact))
        {
            return exact;
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

    private static string? GetString(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int? GetInt(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (element.TryGetProperty(property, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String &&
                    int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    return number;
                }
            }
        }

        return null;
    }

    private static double? GetDouble(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (element.TryGetProperty(property, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String &&
                    double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    return number;
                }
            }
        }

        return null;
    }

    private static bool TryGetArray(JsonElement element, out JsonElement array, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                array = value;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if (!fullDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
            !fullDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            fullDirectory += Path.DirectorySeparatorChar;
        }

        return Path.GetFullPath(path).StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
