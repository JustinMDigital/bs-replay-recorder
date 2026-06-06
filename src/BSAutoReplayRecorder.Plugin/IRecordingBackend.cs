using System;
using System.Threading;
using System.Threading.Tasks;
using BSAutoReplayRecorder.Core;

namespace BSAutoReplayRecorder.Plugin;

public interface IRecordingBackend : IDisposable
{
    string DisplayName { get; }

    string Summary { get; }

    Task StartRecordingAsync(RecordingPlan plan, CancellationToken cancellationToken);

    Task<RecordingStatus> GetRecordingStatusAsync(CancellationToken cancellationToken);

    Task<RecordingStopResult> StopRecordingAsync(
        RecordingPlan plan,
        DateTimeOffset? contentStartUtc,
        CancellationToken cancellationToken);
}
