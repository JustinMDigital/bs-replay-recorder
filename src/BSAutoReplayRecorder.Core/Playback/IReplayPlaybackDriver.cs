using System.Threading;
using System.Threading.Tasks;

namespace BSAutoReplayRecorder.Core.Playback;

public interface IReplayPlaybackDriver
{
    string DriverName { get; }

    bool CanPlay(ReplayReference replayReference);

    IReplayPlaybackWait CreateStartWait();

    IReplayPlaybackWait CreateFinishWait();

    Task ValidateReplayAsync(
        ReplayReference replayReference,
        CancellationToken cancellationToken);

    Task<ReplayPlaybackSession> StartReplayAsync(
        ReplayQueueItem queueItem,
        ReplayReference replayReference,
        CancellationToken cancellationToken);
}
