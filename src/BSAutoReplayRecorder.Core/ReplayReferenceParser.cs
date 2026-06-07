using System;
using System.Collections.Generic;
using System.IO;

namespace BSAutoReplayRecorder.Core;

public sealed class ReplayReferenceParser
{
    private static readonly HashSet<string> BeatLeaderHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "beatleader.com",
        "www.beatleader.com",
        "replay.beatleader.com",
        "replay.beatleader.xyz",
        "cdn.replays.beatleader.xyz"
    };

    private static readonly HashSet<string> ScoreSaberHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "scoresaber.com",
        "www.scoresaber.com",
        "new.scoresaber.com"
    };

    public ReplayReference Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Replay reference is required.", nameof(value));
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            return ParseUri(trimmed, uri);
        }

        return ParseLocalPath(trimmed);
    }

    private static ReplayReference ParseLocalPath(string value)
    {
        var extension = Path.GetExtension(value);
        if (string.Equals(extension, ".bsor", StringComparison.OrdinalIgnoreCase))
        {
            return new ReplayReference(
                ReplayProvider.BeatLeader,
                ReplayReferenceKind.LocalBsorFile,
                value,
                value,
                null,
                null);
        }

        if (string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase))
        {
            return new ReplayReference(
                ReplayProvider.ScoreSaber2,
                ReplayReferenceKind.LocalScoreSaberDatFile,
                value,
                value,
                null,
                null);
        }

        return new ReplayReference(
            ReplayProvider.Unknown,
            ReplayReferenceKind.Unknown,
            value,
            value,
            null,
            null);
    }

    private static ReplayReference ParseUri(string originalValue, Uri uri)
    {
        if (BeatLeaderHosts.Contains(uri.Host))
        {
            return ParseBeatLeaderUri(originalValue, uri);
        }

        if (ScoreSaberHosts.Contains(uri.Host))
        {
            return ParseScoreSaberUri(originalValue, uri);
        }

        return new ReplayReference(
            ReplayProvider.Unknown,
            ReplayReferenceKind.Unknown,
            originalValue,
            null,
            uri,
            null);
    }

    private static ReplayReference ParseBeatLeaderUri(string originalValue, Uri uri)
    {
        if (uri.AbsolutePath.EndsWith(".bsor", StringComparison.OrdinalIgnoreCase))
        {
            return new ReplayReference(
                ReplayProvider.BeatLeader,
                ReplayReferenceKind.BeatLeaderCdnBsorUrl,
                originalValue,
                null,
                uri,
                TryExtractBeatLeaderScoreIdFromPath(uri.AbsolutePath));
        }

        var scoreId = QueryString.Get(uri.Query, "scoreId") ??
                      TryExtractNumericPathSegment(uri.AbsolutePath);

        return new ReplayReference(
            ReplayProvider.BeatLeader,
            ReplayReferenceKind.BeatLeaderScoreUrl,
            originalValue,
            null,
            uri,
            scoreId);
    }

    private static ReplayReference ParseScoreSaberUri(string originalValue, Uri uri)
    {
        var isNewApiSurface = string.Equals(uri.Host, "new.scoresaber.com", StringComparison.OrdinalIgnoreCase) ||
                              uri.AbsolutePath.IndexOf("/api/v2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              uri.AbsolutePath.IndexOf("/scores/", StringComparison.OrdinalIgnoreCase) >= 0;

        return new ReplayReference(
            isNewApiSurface ? ReplayProvider.ScoreSaber2 : ReplayProvider.ScoreSaberLegacy,
            isNewApiSurface ? ReplayReferenceKind.ScoreSaber2ScoreUrl : ReplayReferenceKind.ScoreSaberScoreUrl,
            originalValue,
            null,
            uri,
            TryExtractNumericPathSegment(uri.AbsolutePath));
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

    private static string? TryExtractNumericPathSegment(string path)
    {
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            if (IsAllDigits(segments[index]))
            {
                return segments[index];
            }
        }

        return null;
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
}
