using BSAutoReplayRecorder.Core;

namespace BSAutoReplayRecorder.ControlPanel;

public sealed class ReplayQueueSidecar
{
    public ReplayProvider Provider { get; set; } = ReplayProvider.Unknown;

    public ReplayReferenceKind ReferenceKind { get; set; } = ReplayReferenceKind.Unknown;

    public string ReplayFormat { get; set; } = "";

    public string Path { get; set; } = "";

    public string SourceUrl { get; set; } = "";

    public string ScoreId { get; set; } = "";

    public bool HasReplay { get; set; } = true;

    public string PlayerName { get; set; } = "";

    public string PlayerId { get; set; } = "";

    public string SongName { get; set; } = "";

    public string Mapper { get; set; } = "";

    public string Difficulty { get; set; } = "";

    public string Mode { get; set; } = "";

    public string LevelHash { get; set; } = "";

    public string CoverArtUrl { get; set; } = "";

    public double EstimatedSeconds { get; set; }
}
