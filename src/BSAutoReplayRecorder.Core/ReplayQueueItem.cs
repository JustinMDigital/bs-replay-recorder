using System;
using BSAutoReplayRecorder.Core.Replay;

namespace BSAutoReplayRecorder.Core;

public sealed class ReplayQueueItem
{
    public ReplayQueueItem(int sequenceNumber, string replayPath, BsorInfo replayInfo)
        : this(sequenceNumber, replayPath, replayInfo, ReplayProvider.BeatLeader, ReplayReferenceKind.LocalBsorFile, null, null)
    {
    }

    public ReplayQueueItem(
        int sequenceNumber,
        string replayPath,
        BsorInfo replayInfo,
        ReplayProvider provider,
        ReplayReferenceKind referenceKind,
        string? sourceUrl,
        string? scoreId)
    {
        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "Sequence numbers are one-based.");
        }

        SequenceNumber = sequenceNumber;
        ReplayPath = replayPath ?? throw new ArgumentNullException(nameof(replayPath));
        ReplayInfo = replayInfo ?? throw new ArgumentNullException(nameof(replayInfo));
        Provider = provider;
        ReferenceKind = referenceKind;
        SourceUrl = sourceUrl;
        ScoreId = scoreId;
    }

    public int SequenceNumber { get; }

    public string ReplayPath { get; }

    public BsorInfo ReplayInfo { get; }

    public ReplayProvider Provider { get; }

    public ReplayReferenceKind ReferenceKind { get; }

    public string? SourceUrl { get; }

    public string? ScoreId { get; }

    public TimeSpan EstimatedPlaybackLength => ReplayInfo.EstimatedPlaybackLength;
}
