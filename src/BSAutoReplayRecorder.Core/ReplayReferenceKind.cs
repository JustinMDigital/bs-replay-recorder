namespace BSAutoReplayRecorder.Core;

public enum ReplayReferenceKind
{
    Unknown = 0,
    LocalBsorFile = 1,
    LocalScoreSaberDatFile = 2,
    BeatLeaderScoreUrl = 3,
    BeatLeaderCdnBsorUrl = 4,
    ScoreSaberScoreUrl = 5,
    ScoreSaber2ScoreUrl = 6
}

