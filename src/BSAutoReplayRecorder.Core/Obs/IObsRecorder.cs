using System.Threading;
using System.Threading.Tasks;

namespace BSAutoReplayRecorder.Core.Obs;

public interface IObsRecorder
{
    Task StartRecordingAsync(RecordingPlan plan, CancellationToken cancellationToken);

    Task<RecordingStopResult> StopRecordingAsync(RecordingPlan plan, CancellationToken cancellationToken);
}

