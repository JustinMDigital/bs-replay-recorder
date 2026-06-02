using System;

namespace BSAutoReplayRecorder.Core;

public sealed class RecordingPlanOptions
{
    public string OutputNameTemplate { get; set; } = "{index:00} - {song} [{difficulty}]";

    public TimeSpan PreRoll { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan PostRoll { get; set; } = TimeSpan.FromSeconds(4);
}

