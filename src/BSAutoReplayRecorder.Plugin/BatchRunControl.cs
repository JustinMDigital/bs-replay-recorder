using System.Threading;

namespace BSAutoReplayRecorder.Plugin;

public sealed class BatchRunControl
{
    private int _stopAfterCurrentRequested;

    public bool StopAfterCurrentRequested => Volatile.Read(ref _stopAfterCurrentRequested) != 0;

    public void RequestStopAfterCurrent()
    {
        Interlocked.Exchange(ref _stopAfterCurrentRequested, 1);
    }
}
