namespace BSAutoReplayRecorder.Core;

public sealed class ReplayQueueOptions
{
    public string InputDirectory { get; set; } = "";

    public bool IncludeSubdirectories { get; set; }

    public bool SkipInvalidReplays { get; set; } = true;
}

