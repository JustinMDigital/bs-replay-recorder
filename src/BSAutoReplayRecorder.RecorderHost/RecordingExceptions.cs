namespace BSAutoReplayRecorder.RecorderHost;

public sealed class RecordingAlreadyActiveException : InvalidOperationException
{
    public RecordingAlreadyActiveException(string message)
        : base(message)
    {
    }
}

public sealed class RecordingNotActiveException : InvalidOperationException
{
    public RecordingNotActiveException(string message)
        : base(message)
    {
    }
}
