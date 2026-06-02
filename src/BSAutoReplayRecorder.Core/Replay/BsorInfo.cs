using System;

namespace BSAutoReplayRecorder.Core.Replay;

public sealed class BsorInfo
{
    public int FormatVersion { get; set; }

    public string FileVersion { get; set; } = "";

    public string GameVersion { get; set; } = "";

    public string Timestamp { get; set; } = "";

    public string PlayerId { get; set; } = "";

    public string PlayerName { get; set; } = "";

    public string Platform { get; set; } = "";

    public string TrackingSystem { get; set; } = "";

    public string Hmd { get; set; } = "";

    public string Controller { get; set; } = "";

    public string LevelHash { get; set; } = "";

    public string SongName { get; set; } = "";

    public string Mapper { get; set; } = "";

    public string Difficulty { get; set; } = "";

    public int Score { get; set; }

    public string Mode { get; set; } = "";

    public string Environment { get; set; } = "";

    public string Modifiers { get; set; } = "";

    public float JumpDistance { get; set; }

    public bool LeftHanded { get; set; }

    public float Height { get; set; }

    public float StartTime { get; set; }

    public float FailTime { get; set; }

    public float Speed { get; set; } = 1;

    public int FrameCount { get; set; }

    public float LastFrameTime { get; set; }

    public TimeSpan ReplayLength
    {
        get
        {
            var length = LastFrameTime > 0 ? LastFrameTime : FailTime;
            return TimeSpan.FromSeconds(Math.Max(0, length));
        }
    }

    public TimeSpan EstimatedPlaybackLength
    {
        get
        {
            var rawSeconds = Math.Max(0, ReplayLength.TotalSeconds - Math.Max(0, StartTime));
            var speed = Speed <= 0 ? 1 : Speed;
            return TimeSpan.FromSeconds(rawSeconds / speed);
        }
    }
}

