namespace BSAutoReplayRecorder.Core;

public sealed class RecordingStatus
{
    public RecordingStatus(bool outputActive, bool outputPaused)
    {
        OutputActive = outputActive;
        OutputPaused = outputPaused;
    }

    public bool OutputActive { get; }

    public bool OutputPaused { get; }
}
