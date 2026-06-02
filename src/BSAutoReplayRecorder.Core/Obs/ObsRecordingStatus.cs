namespace BSAutoReplayRecorder.Core.Obs;

public sealed class ObsRecordingStatus
{
    public ObsRecordingStatus(bool outputActive, bool outputPaused)
    {
        OutputActive = outputActive;
        OutputPaused = outputPaused;
    }

    public bool OutputActive { get; }

    public bool OutputPaused { get; }
}
