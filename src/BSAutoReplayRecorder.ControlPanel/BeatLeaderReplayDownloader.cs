using System.Text.Json;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Replay;
using BSAutoReplayRecorder.Core.Utility;

namespace BSAutoReplayRecorder.ControlPanel;

public interface IBeatLeaderReplayDownloader
{
    Task<BeatLeaderReplayDownload> DownloadAsync(
        ReplayReference reference,
        string queueDirectory,
        Func<string, string> createImportPath,
        CancellationToken cancellationToken);
}

public sealed class BeatLeaderReplayDownload
{
    public string LocalPath { get; set; } = "";

    public ReplayQueueSidecar Metadata { get; set; } = new ReplayQueueSidecar();
}

public sealed class BeatLeaderReplayDownloader : IBeatLeaderReplayDownloader
{
    private readonly HttpClient _httpClient;

    public BeatLeaderReplayDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BSAutoReplayRecorder/1.0");
        }
    }

    public async Task<BeatLeaderReplayDownload> DownloadAsync(
        ReplayReference reference,
        string queueDirectory,
        Func<string, string> createImportPath,
        CancellationToken cancellationToken)
    {
        if (reference == null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        if (reference.Provider != ReplayProvider.BeatLeader)
        {
            throw new InvalidOperationException("Only BeatLeader replay links can be downloaded by this downloader.");
        }

        var resolvedReplay = await ResolveReplayUriAsync(reference, cancellationToken).ConfigureAwait(false)
                             ?? throw new InvalidOperationException(
                                 "BeatLeader link import needs a score URL, a direct .bsor URL, or a web-replayer link= URL.");
        var replayUri = resolvedReplay.ReplayUri;

        Directory.CreateDirectory(queueDirectory);
        var targetPath = createImportPath(CreateReplayFileName(reference, replayUri));
        using var response = await _httpClient.GetAsync(replayUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "BeatLeader replay download failed: HTTP " + (int)response.StatusCode + ".");
        }

        await using (var targetStream = File.Create(targetPath))
        {
            await response.Content.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
        }

        var info = new BsorInfoReader().Read(targetPath);
        var metadata = new ReplayQueueSidecar
        {
            Provider = ReplayProvider.BeatLeader,
            ReferenceKind = ReplayReferenceKind.BeatLeaderCdnBsorUrl,
            ReplayFormat = "BSOR",
            Path = targetPath,
            SourceUrl = reference.OriginalValue,
            ScoreId = resolvedReplay.ScoreId ?? reference.ScoreId ?? TryExtractBeatLeaderScoreIdFromPath(replayUri.AbsolutePath) ?? "",
            HasReplay = true,
            PlayerName = info.PlayerName,
            PlayerId = info.PlayerId,
            SongName = info.SongName,
            Mapper = info.Mapper,
            Difficulty = info.Difficulty,
            Mode = info.Mode,
            LevelHash = info.LevelHash,
            EstimatedSeconds = Math.Max(1, info.EstimatedPlaybackLength.TotalSeconds)
        };

        return new BeatLeaderReplayDownload
        {
            LocalPath = targetPath,
            Metadata = metadata
        };
    }

    private async Task<BeatLeaderReplayResolution?> ResolveReplayUriAsync(
        ReplayReference reference,
        CancellationToken cancellationToken)
    {
        if (reference.Kind == ReplayReferenceKind.BeatLeaderCdnBsorUrl &&
            reference.Uri != null &&
            reference.Uri.AbsolutePath.EndsWith(".bsor", StringComparison.OrdinalIgnoreCase))
        {
            return new BeatLeaderReplayResolution(
                reference.Uri,
                reference.ScoreId ?? TryExtractBeatLeaderScoreIdFromPath(reference.Uri.AbsolutePath));
        }

        if (reference.Uri != null)
        {
            var linkedReplay = GetQueryValue(reference.Uri.Query, "link");
            if (Uri.TryCreate(linkedReplay, UriKind.Absolute, out var linkedUri) &&
                linkedUri.AbsolutePath.EndsWith(".bsor", StringComparison.OrdinalIgnoreCase))
            {
                return new BeatLeaderReplayResolution(
                    linkedUri,
                    reference.ScoreId ?? TryExtractBeatLeaderScoreIdFromPath(linkedUri.AbsolutePath));
            }
        }

        if (reference.Kind == ReplayReferenceKind.BeatLeaderScoreUrl &&
            !string.IsNullOrWhiteSpace(reference.ScoreId))
        {
            return await ResolveReplayUriByScoreIdAsync(reference.ScoreId!, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<BeatLeaderReplayResolution> ResolveReplayUriByScoreIdAsync(
        string scoreId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync("https://api.beatleader.xyz/score/" + Uri.EscapeDataString(scoreId), cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "BeatLeader score lookup failed for " + scoreId + ": HTTP " + (int)response.StatusCode + ".");
        }

        await using var scoreStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var scoreJson = await JsonDocument.ParseAsync(scoreStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var replayUrl = GetString(scoreJson.RootElement, "replay");
        if (!Uri.TryCreate(replayUrl, UriKind.Absolute, out var replayUri) ||
            !replayUri.AbsolutePath.EndsWith(".bsor", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "BeatLeader score " + scoreId + " does not have a downloadable replay.");
        }

        return new BeatLeaderReplayResolution(
            replayUri,
            GetNumberAsString(scoreJson.RootElement, "id") ?? scoreId);
    }

    private static string CreateReplayFileName(ReplayReference reference, Uri replayUri)
    {
        var fileName = Path.GetFileName(replayUri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fileName) &&
            fileName.EndsWith(".bsor", StringComparison.OrdinalIgnoreCase))
        {
            return FileNameSanitizer.SanitizeBaseName(Path.GetFileNameWithoutExtension(fileName)) + ".bsor";
        }

        var baseName = string.IsNullOrWhiteSpace(reference.ScoreId)
            ? "beatleader-replay"
            : "beatleader-" + reference.ScoreId;
        return FileNameSanitizer.SanitizeBaseName(baseName) + ".bsor";
    }

    private static string? TryExtractBeatLeaderScoreIdFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var dashIndex = fileName.IndexOf('-');
        if (dashIndex <= 0)
        {
            return null;
        }

        var candidate = fileName.Substring(0, dashIndex);
        return IsAllDigits(candidate) ? candidate : null;
    }

    private static bool IsAllDigits(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetQueryValue(string query, string name)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair.Substring(0, separator);
            if (!string.Equals(Uri.UnescapeDataString(key.Replace("+", " ")), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separator < 0 ? "" : pair.Substring(separator + 1);
            return Uri.UnescapeDataString(value.Replace("+", " "));
        }

        return null;
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

    private sealed class BeatLeaderReplayResolution
    {
        public BeatLeaderReplayResolution(Uri replayUri, string? scoreId)
        {
            ReplayUri = replayUri;
            ScoreId = scoreId;
        }

        public Uri ReplayUri { get; }

        public string? ScoreId { get; }
    }
}
