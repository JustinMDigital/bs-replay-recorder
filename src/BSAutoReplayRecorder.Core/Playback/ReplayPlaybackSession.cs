using System;

namespace BSAutoReplayRecorder.Core.Playback;

public sealed class ReplayPlaybackSession
{
    public ReplayPlaybackSession(ReplayQueueItem queueItem, ReplayReference replayReference, DateTimeOffset startedAt)
    {
        QueueItem = queueItem ?? throw new ArgumentNullException(nameof(queueItem));
        ReplayReference = replayReference ?? throw new ArgumentNullException(nameof(replayReference));
        StartedAt = startedAt;
    }

    public ReplayQueueItem QueueItem { get; }

    public ReplayReference ReplayReference { get; }

    public DateTimeOffset StartedAt { get; }
}

