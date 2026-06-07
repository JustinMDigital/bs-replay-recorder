using System;
using System.Threading;
using System.Threading.Tasks;

namespace BSAutoReplayRecorder.Core.Playback;

public interface IReplayPlaybackWait : IDisposable
{
    Task<DateTimeOffset?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
