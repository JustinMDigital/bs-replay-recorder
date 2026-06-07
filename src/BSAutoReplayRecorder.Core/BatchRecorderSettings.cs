namespace BSAutoReplayRecorder.Core;

public sealed class BatchRecorderSettings
{
    public bool DisableScoreSubmissions { get; set; } = true;

    public bool SuppressScoreSaberReplayUi { get; set; } = true;

    public bool RequirePreflightReplayValidation { get; set; } = true;

    public bool RefreshSongCoreBeforeReplayValidation { get; set; } = true;

    public double SongCoreRefreshTimeoutSeconds { get; set; } = 45;

    public string RecordingOutputDirectory { get; set; } = "UserData/BSAutoReplayRecorder/Recordings";

    public double ReplayFinishTimeoutPaddingSeconds { get; set; } = 30;

    public bool LagSpikeDetectionEnabled { get; set; } = true;

    public double LagSpikeThresholdMilliseconds { get; set; } = 250;

    public int LagSpikeConsecutiveFrameCount { get; set; } = 1;

    public double LagSpikeStartupGraceSeconds { get; set; } = 3;

    public double DelayBetweenRecordingsSeconds { get; set; } = 5;

    public int StartRecordingRetryCount { get; set; } = 5;

    public double StartRecordingRetryDelaySeconds { get; set; } = 2;

    public RecorderHostConnectionSettings RecorderHost { get; set; } = new RecorderHostConnectionSettings();

    public ControlPanelWorkerSettings ControlPanelWorker { get; set; } = new ControlPanelWorkerSettings();

    public WindowPlacementSettings WindowPlacement { get; set; } = new WindowPlacementSettings();
}
