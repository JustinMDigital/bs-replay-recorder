using System;

namespace BSAutoReplayRecorder.Core;

public sealed class RecordingPlan
{
    public RecordingPlan(
        ReplayQueueItem queueItem,
        string outputBaseName,
        TimeSpan preRoll,
        TimeSpan postRoll,
        string overlayContext = "")
    {
        QueueItem = queueItem ?? throw new ArgumentNullException(nameof(queueItem));
        OutputBaseName = outputBaseName ?? throw new ArgumentNullException(nameof(outputBaseName));
        PreRoll = preRoll;
        PostRoll = postRoll;
        OverlayContext = overlayContext ?? "";
    }

    public ReplayQueueItem QueueItem { get; }

    public string OutputBaseName { get; }

    public TimeSpan PreRoll { get; }

    public TimeSpan PostRoll { get; }

    public string OverlayContext { get; }

    public TimeSpan EstimatedRecordingLength => PreRoll + QueueItem.EstimatedPlaybackLength + PostRoll;
}
