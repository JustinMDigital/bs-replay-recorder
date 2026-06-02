using System;
using BSAutoReplayRecorder.Core.Replay;

namespace BSAutoReplayRecorder.Core;

public sealed class ReplayQueueItem
{
    public ReplayQueueItem(int sequenceNumber, string replayPath, BsorInfo replayInfo)
    {
        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "Sequence numbers are one-based.");
        }

        SequenceNumber = sequenceNumber;
        ReplayPath = replayPath ?? throw new ArgumentNullException(nameof(replayPath));
        ReplayInfo = replayInfo ?? throw new ArgumentNullException(nameof(replayInfo));
    }

    public int SequenceNumber { get; }

    public string ReplayPath { get; }

    public BsorInfo ReplayInfo { get; }

    public TimeSpan EstimatedPlaybackLength => ReplayInfo.EstimatedPlaybackLength;
}

