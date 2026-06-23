using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using BSAutoReplayRecorder.Core.Utility;

namespace BSAutoReplayRecorder.ControlPanel;

public interface IBeatSaverMapDownloader
{
    BeatSaverMapDownloadResult DownloadByHash(string levelHash, string targetRoot);

    double? GetSongLengthSecondsByHash(string levelHash);

    BeatSaverMapCardMetadata? GetMapCardMetadataByHash(string levelHash, string difficulty, string mode);
}

public sealed class BeatSaverMapDownloadResult
{
    public bool Installed { get; set; }

    public bool NotFound { get; set; }

    public string InstallPath { get; set; } = "";

    public string Detail { get; set; } = "";
}

public sealed class BeatSaverMapDownloader : IBeatSaverMapDownloader
{
    private readonly HttpClient _httpClient;

    public BeatSaverMapDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BSAutoReplayRecorder/1.0");
        }
    }

    public BeatSaverMapDownloadResult DownloadByHash(string levelHash, string targetRoot)
    {
        var hash = NormalizeHash(levelHash);
        if (hash == null)
        {
            return new BeatSaverMapDownloadResult
            {
                NotFound = true,
                Detail = "Replay did not include a level hash."
            };
        }

        Directory.CreateDirectory(targetRoot);
        using var response = _httpClient.GetAsync("https://api.beatsaver.com/maps/hash/" + Uri.EscapeDataString(hash))
            .GetAwaiter()
            .GetResult();
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new BeatSaverMapDownloadResult
            {
                NotFound = true,
                Detail = "BeatSaver does not have this map. It is probably WIP or unreleased."
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            return new BeatSaverMapDownloadResult
            {
                Detail = "BeatSaver lookup failed with HTTP " + (int)response.StatusCode + "."
            };
        }

        using var stream = response.Content.ReadAsStream();
        using var document = JsonDocument.Parse(stream);
        var version = FindMatchingVersion(document.RootElement, hash);
        var downloadUrl = GetString(version, "downloadURL");
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return new BeatSaverMapDownloadResult
            {
                Detail = "BeatSaver response did not include a download URL."
            };
        }

        var key = GetString(version, "key") ?? GetString(document.RootElement, "id") ?? hash;
        var name = GetString(document.RootElement, "name") ?? hash;
        var targetDirectory = CreateUniqueLevelDirectory(targetRoot, key + " (" + name + ")");
        var tempZip = Path.Combine(Path.GetTempPath(), "bsarr-map-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            using (var zipResponse = _httpClient.GetAsync(downloadUrl).GetAwaiter().GetResult())
            {
                if (!zipResponse.IsSuccessStatusCode)
                {
                    return new BeatSaverMapDownloadResult
                    {
                        Detail = "BeatSaver download failed with HTTP " + (int)zipResponse.StatusCode + "."
                    };
                }

                using var zipStream = zipResponse.Content.ReadAsStream();
                using var output = File.Create(tempZip);
                zipStream.CopyTo(output);
            }

            ExtractMapZip(tempZip, targetDirectory);
            return new BeatSaverMapDownloadResult
            {
                Installed = true,
                InstallPath = targetDirectory,
                Detail = "Downloaded from BeatSaver."
            };
        }
        finally
        {
            try
            {
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }
            }
            catch
            {
            }
        }
    }

    public double? GetSongLengthSecondsByHash(string levelHash)
    {
        var hash = NormalizeHash(levelHash);
        if (hash == null)
        {
            return null;
        }

        using var response = _httpClient.GetAsync("https://api.beatsaver.com/maps/hash/" + Uri.EscapeDataString(hash))
            .GetAwaiter()
            .GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = response.Content.ReadAsStream();
        using var document = JsonDocument.Parse(stream);
        var map = document.RootElement;

        var metadataDuration = GetDouble(map, "metadata", "duration");
        if (metadataDuration.HasValue && metadataDuration > 0)
        {
            return metadataDuration;
        }

        var versions = FindMatchingVersion(map, hash);
        var versionDuration = GetDouble(versions, "duration");
        return versionDuration <= 0 ? null : versionDuration;
    }

    public BeatSaverMapCardMetadata? GetMapCardMetadataByHash(string levelHash, string difficulty, string mode)
    {
        var hash = NormalizeHash(levelHash);
        if (hash == null)
        {
            return null;
        }

        using var response = _httpClient.GetAsync("https://api.beatsaver.com/maps/hash/" + Uri.EscapeDataString(hash))
            .GetAwaiter()
            .GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = response.Content.ReadAsStream();
        using var document = JsonDocument.Parse(stream);
        var map = document.RootElement;
        var version = FindMatchingVersion(map, hash);
        var metadata = map.TryGetProperty("metadata", out var metadataElement) &&
                       metadataElement.ValueKind == JsonValueKind.Object
            ? metadataElement
            : map;
        var result = new BeatSaverMapCardMetadata
        {
            SongName = GetString(metadata, "songName") ?? GetString(map, "name") ?? "",
            Artist = GetString(metadata, "songAuthorName") ?? "",
            MapAuthor = GetString(metadata, "levelAuthorName") ?? "",
            BeatSaverKey = GetString(version, "key") ?? GetString(map, "id") ?? "",
            CoverArtUrl = GetString(version, "coverURL") ?? GetString(version, "coverUrl") ?? "",
            LengthSeconds = GetDouble(metadata, "duration") ?? GetDouble(version, "duration"),
            BeatsPerMinute = GetDouble(metadata, "bpm") ?? GetDouble(metadata, "beatsPerMinute")
        };

        var selectedDiff = FindMatchingDiff(version, difficulty, mode);
        if (selectedDiff.HasValue)
        {
            var diff = selectedDiff.Value;
            result.Difficulty = MapCardMetadataText.DisplayDifficulty(GetString(diff, "difficulty") ?? "");
            result.Mode = GetString(diff, "characteristic") ?? "";
            result.NotesPerSecond = GetDouble(diff, "nps");
            var noteCount = GetInt(diff, "notes");
            if (noteCount.HasValue)
            {
                result.NoteCount = noteCount;
            }
        }

        return result;
    }

    internal static void ExtractMapZip(string zipPath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));
            if (!IsPathInsideDirectory(destinationPath, targetDirectory))
            {
                throw new InvalidOperationException("Song zip contains a file outside the map folder: " + entry.FullName);
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? targetDirectory);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        if (!ContainsInfoDat(targetDirectory))
        {
            var nested = Directory.EnumerateDirectories(targetDirectory)
                .FirstOrDefault(ContainsInfoDat);
            if (nested != null)
            {
                FlattenNestedMapDirectory(nested, targetDirectory);
            }
        }

        if (!ContainsInfoDat(targetDirectory))
        {
            throw new InvalidOperationException("Song zip did not contain Info.dat at the map root.");
        }
    }

    private static JsonElement FindMatchingVersion(JsonElement map, string hash)
    {
        if (map.TryGetProperty("versions", out var versions) &&
            versions.ValueKind == JsonValueKind.Array)
        {
            foreach (var version in versions.EnumerateArray())
            {
                if (string.Equals(GetString(version, "hash"), hash, StringComparison.OrdinalIgnoreCase))
                {
                    return version;
                }
            }

            var latest = versions.EnumerateArray().FirstOrDefault();
            if (latest.ValueKind != JsonValueKind.Undefined)
            {
                return latest;
            }
        }

        return map;
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? GetDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static double? GetDouble(JsonElement element, string propertyA, string propertyB)
    {
        if (!element.TryGetProperty(propertyA, out var nested) ||
            nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetDouble(nested, propertyB);
    }

    private static int? GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static JsonElement? FindMatchingDiff(JsonElement version, string difficulty, string mode)
    {
        if (!version.TryGetProperty("diffs", out var diffs) ||
            diffs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var normalizedDifficulty = MapCardMetadataText.NormalizeDifficulty(difficulty);
        var normalizedMode = MapCardMetadataText.NormalizeMode(mode);
        JsonElement? fallback = null;
        foreach (var diff in diffs.EnumerateArray())
        {
            var diffDifficulty = MapCardMetadataText.NormalizeDifficulty(GetString(diff, "difficulty"));
            var diffMode = MapCardMetadataText.NormalizeMode(GetString(diff, "characteristic"));
            var modeMatches = normalizedMode.Length == 0 || diffMode.Length == 0 || diffMode == normalizedMode;
            if (diffDifficulty == normalizedDifficulty && modeMatches)
            {
                return diff;
            }

            fallback ??= diff;
        }

        return fallback;
    }

    private static string CreateUniqueLevelDirectory(string targetRoot, string folderName)
    {
        var safeName = FileNameSanitizer.SanitizeBaseName(folderName);
        var target = Path.Combine(targetRoot, safeName);
        if (!Directory.Exists(target))
        {
            return target;
        }

        for (var index = 2; index < 10_000; index++)
        {
            target = Path.Combine(targetRoot, safeName + " (" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
            if (!Directory.Exists(target))
            {
                return target;
            }
        }

        throw new InvalidOperationException("Could not create a target folder for " + safeName + ".");
    }

    private static bool ContainsInfoDat(string directory)
    {
        return File.Exists(Path.Combine(directory, "Info.dat")) ||
               File.Exists(Path.Combine(directory, "info.dat"));
    }

    private static void FlattenNestedMapDirectory(string nestedDirectory, string targetDirectory)
    {
        var tempDirectory = targetDirectory + ".extract-" + Guid.NewGuid().ToString("N");
        Directory.Move(nestedDirectory, tempDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(targetDirectory))
        {
            if (!string.Equals(entry, tempDirectory, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(tempDirectory))
        {
            var target = Path.Combine(targetDirectory, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                Directory.Move(entry, target);
            }
            else
            {
                File.Move(entry, target);
            }
        }

        Directory.Delete(tempDirectory, recursive: true);
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

    private static string? NormalizeHash(string? hash)
    {
        var normalized = hash?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.ToLowerInvariant();
    }
}
