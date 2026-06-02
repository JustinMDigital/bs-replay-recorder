namespace BSAutoReplayRecorder.Core.Obs;

public sealed class RecordingStopResult
{
    public RecordingStopResult(string? outputPath)
    {
        OutputPath = outputPath;
    }

    public string? OutputPath { get; }
}

