using System.Globalization;
using System.Text.Json;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Replay;
using BSAutoReplayRecorder.Core.Utility;

namespace BSAutoReplayRecorder.ControlPanel;

public interface IScoreSaberReplayDownloader
{
    Task<ScoreSaberReplayDownload> DownloadAsync(
        ReplayReference reference,
        string queueDirectory,
        Func<string, string> createImportPath,
        CancellationToken cancellationToken);

    Task<ReplayQueueSidecar?> GetReplayMetadataByScoreIdAsync(
        string scoreId,
        CancellationToken cancellationToken);

    Task<string?> GetPlayerNameByPlayerIdAsync(
        string playerId,
        CancellationToken cancellationToken);
}

public sealed class ScoreSaberReplayDownload
{
    public string LocalPath { get; set; } = "";

    public ReplayQueueSidecar Metadata { get; set; } = new ReplayQueueSidecar();
}

public sealed class ScoreSaberReplayDownloader : IScoreSaberReplayDownloader
{
    private readonly HttpClient _httpClient;

    public ScoreSaberReplayDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BSAutoReplayRecorder/1.0");
        }
    }

    public async Task<ScoreSaberReplayDownload> DownloadAsync(
        ReplayReference reference,
        string queueDirectory,
        Func<string, string> createImportPath,
        CancellationToken cancellationToken)
    {
        if (reference == null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        if (reference.Provider != ReplayProvider.ScoreSaber2 || string.IsNullOrWhiteSpace(reference.ScoreId))
        {
            throw new InvalidOperationException("Only ScoreSaber 2 score URLs can be downloaded.");
        }

        Directory.CreateDirectory(queueDirectory);

        var scoreId = reference.ScoreId!;
        using var scoreResponse = await _httpClient
            .GetAsync("https://scoresaber.com/api/v2/scores/" + Uri.EscapeDataString(scoreId), cancellationToken)
            .ConfigureAwait(false);
        if (!scoreResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "ScoreSaber score lookup failed for " + scoreId + ": HTTP " + (int)scoreResponse.StatusCode + ".");
        }

        await using var scoreStream = await scoreResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var scoreJson = await JsonDocument.ParseAsync(scoreStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var metadata = ReadScoreMetadata(reference, scoreJson.RootElement);
        if (!metadata.HasReplay)
        {
            throw new InvalidOperationException("ScoreSaber score " + scoreId + " does not have a downloadable replay.");
        }

        using var replayResponse = await _httpClient
            .GetAsync("https://scoresaber.com/api/v2/scores/" + Uri.EscapeDataString(scoreId) + "/replay", cancellationToken)
            .ConfigureAwait(false);
        if (!replayResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "ScoreSaber replay download failed for " + scoreId + ": HTTP " + (int)replayResponse.StatusCode + ".");
        }

        var targetPath = createImportPath(CreateReplayFileName(metadata));
        await using (var targetStream = File.Create(targetPath))
        {
            await replayResponse.Content.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
        }

        new ScoreSaberReplayInfoReader().Validate(targetPath);

        metadata.Path = targetPath;
        return new ScoreSaberReplayDownload
        {
            LocalPath = targetPath,
            Metadata = metadata
        };
    }

    public async Task<ReplayQueueSidecar?> GetReplayMetadataByScoreIdAsync(
        string scoreId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scoreId))
        {
            return null;
        }

        var response = await _httpClient
            .GetAsync("https://scoresaber.com/api/v2/scores/" + Uri.EscapeDataString(scoreId), cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var scoreStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var scoreJson = await JsonDocument.ParseAsync(scoreStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var reference = new ReplayReference(
            ReplayProvider.ScoreSaber2,
            ReplayReferenceKind.ScoreSaber2ScoreUrl,
            "https://scoresaber.com/api/v2/scores/" + scoreId + "/replay",
            null,
            null,
            scoreId);
        var metadata = ReadScoreMetadata(reference, scoreJson.RootElement);
        if (!metadata.HasReplay)
        {
            return null;
        }

        metadata.Path = "";
        return metadata;
    }

    public async Task<string?> GetPlayerNameByPlayerIdAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        var response = await _httpClient
            .GetAsync("https://scoresaber.com/api/player/" + Uri.EscapeDataString(playerId) + "/full", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var scoreStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var scoreJson = await JsonDocument.ParseAsync(scoreStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return StripRichTextTags(GetString(scoreJson.RootElement, "name"))?.Trim();
    }

    private static ReplayQueueSidecar ReadScoreMetadata(ReplayReference reference, JsonElement root)
    {
        var score = RequireObject(root, "score");
        var leaderboard = RequireObject(root, "leaderboard");
        var map = RequireObject(leaderboard, "map");
        var difficulty = RequireObject(leaderboard, "difficulty");
        var player = TryGetObject(score, "player");
        var scoreStats = TryGetObject(root, "scoreStats");

        var hasReplay = GetBoolean(score, "hasReplay");
        var scoreId = GetNumberAsString(score, "id") ?? reference.ScoreId ?? "";
        var playerName = StripRichTextTags(
            player.HasValue
                ? GetString(player.Value, "playerNameInGame") ?? GetString(player.Value, "name") ?? ""
                : "");
        var playerId = player.HasValue ? GetString(player.Value, "id") ?? "" : "";
        var songName = GetString(map, "songName") ?? "ScoreSaber " + scoreId;
        var mapper = GetString(map, "levelAuthorName") ?? "";
        var levelHash = GetString(map, "hash") ?? "";
        var difficultyName = GetString(difficulty, "difficulty") ?? "";
        var rawDifficulty = GetString(difficulty, "rawDifficulty");
        var mode = GetString(difficulty, "gameMode") ?? "Standard";
        var estimatedSeconds = GetDouble(scoreStats, "endTime") ??
                               GetDouble(score, "playOutcomeTime") ??
                               180;

        return new ReplayQueueSidecar
        {
            Provider = ReplayProvider.ScoreSaber2,
            ReferenceKind = ReplayReferenceKind.ScoreSaber2ScoreUrl,
            ReplayFormat = "ScoreSaber",
            SourceUrl = reference.OriginalValue,
            ScoreId = scoreId,
            HasReplay = hasReplay,
            PlayerName = playerName,
            PlayerId = playerId,
            SongName = songName,
            Mapper = mapper,
            Difficulty = string.IsNullOrWhiteSpace(rawDifficulty) ? difficultyName : rawDifficulty!,
            Mode = mode,
            LevelHash = levelHash,
            CoverArtUrl = GetString(map, "coverUrl") ?? "",
            EstimatedSeconds = Math.Max(1, estimatedSeconds)
        };
    }

    private static string CreateReplayFileName(ReplayQueueSidecar metadata)
    {
        var player = string.IsNullOrWhiteSpace(metadata.PlayerName) ? metadata.ScoreId : metadata.PlayerName;
        var baseName = FileNameSanitizer.SanitizeBaseName(
            "scoresaber-" + metadata.ScoreId + "-" + player + "-" + metadata.SongName + "-" +
            metadata.Difficulty + "-" + metadata.Mode + "-" + metadata.LevelHash);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "scoresaber-" + metadata.ScoreId;
        }

        return baseName + ".dat";
    }

    private static string StripRichTextTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Trim();
        var builder = new System.Text.StringBuilder(text.Length);
        var insideTag = false;
        foreach (var character in text)
        {
            if (character == '<')
            {
                insideTag = true;
                continue;
            }

            if (insideTag)
            {
                if (character == '>')
                {
                    insideTag = false;
                }

                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("ScoreSaber response did not include " + name + ".");
        }

        return value;
    }

    private static JsonElement? TryGetObject(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return value;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetRawText();
        }

        return null;
    }

    private static string? GetNumberAsString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.GetRawText();
    }

    private static bool GetBoolean(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        return value.GetBoolean();
    }

    private static double? GetDouble(JsonElement? element, string name)
    {
        if (!element.HasValue ||
            element.Value.ValueKind != JsonValueKind.Object ||
            !element.Value.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
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
}
