using System.Collections.Generic;

namespace BSAutoReplayRecorder.Core;

public sealed class ReplayQueueLoadResult
{
    public ReplayQueueLoadResult(
        IReadOnlyList<ReplayQueueItem> items,
        IReadOnlyList<ReplayQueueLoadFailure> failures)
    {
        Items = items;
        Failures = failures;
    }

    public IReadOnlyList<ReplayQueueItem> Items { get; }

    public IReadOnlyList<ReplayQueueLoadFailure> Failures { get; }
}

