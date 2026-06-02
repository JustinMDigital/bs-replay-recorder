using System;

namespace BSAutoReplayRecorder.Core;

public sealed class RecordingPlan
{
    public RecordingPlan(
        ReplayQueueItem queueItem,
        string outputBaseName,
        TimeSpan preRoll,
        TimeSpan postRoll)
    {
        QueueItem = queueItem ?? throw new ArgumentNullException(nameof(queueItem));
        OutputBaseName = outputBaseName ?? throw new ArgumentNullException(nameof(outputBaseName));
        PreRoll = preRoll;
        PostRoll = postRoll;
    }

    public ReplayQueueItem QueueItem { get; }

    public string OutputBaseName { get; }

    public TimeSpan PreRoll { get; }

    public TimeSpan PostRoll { get; }

    public TimeSpan EstimatedRecordingLength => PreRoll + QueueItem.EstimatedPlaybackLength + PostRoll;
}

