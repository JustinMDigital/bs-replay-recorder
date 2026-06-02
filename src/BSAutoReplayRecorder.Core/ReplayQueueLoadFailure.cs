namespace BSAutoReplayRecorder.Core;

public sealed class ReplayQueueLoadFailure
{
    public ReplayQueueLoadFailure(string replayPath, string reason)
    {
        ReplayPath = replayPath;
        Reason = reason;
    }

    public string ReplayPath { get; }

    public string Reason { get; }
}

