using System;

namespace BSAutoReplayRecorder.Core;

public sealed class ReplayReference
{
    public ReplayReference(
        ReplayProvider provider,
        ReplayReferenceKind kind,
        string originalValue,
        string? localPath,
        Uri? uri,
        string? scoreId)
    {
        Provider = provider;
        Kind = kind;
        OriginalValue = originalValue ?? throw new ArgumentNullException(nameof(originalValue));
        LocalPath = localPath;
        Uri = uri;
        ScoreId = scoreId;
    }

    public ReplayProvider Provider { get; }

    public ReplayReferenceKind Kind { get; }

    public string OriginalValue { get; }

    public string? LocalPath { get; }

    public Uri? Uri { get; }

    public string? ScoreId { get; }

    public bool IsLocalFile => LocalPath != null;

    public bool IsDirectBsor => Kind == ReplayReferenceKind.LocalBsorFile ||
                                Kind == ReplayReferenceKind.BeatLeaderCdnBsorUrl;
}

