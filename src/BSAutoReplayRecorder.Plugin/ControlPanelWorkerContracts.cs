using System.Collections.Generic;
using BSAutoReplayRecorder.Core;

namespace BSAutoReplayRecorder.Plugin;

internal sealed class ControlPanelWorkerRegisterRequest
{
    public string? WorkerId { get; set; }

    public string? WorkerName { get; set; }

    public int? PreferredInstanceIndex { get; set; }

    public string? GameDirectory { get; set; }

    public string? PluginVersion { get; set; }
}

internal sealed class ControlPanelWorkerRegisterResponse
{
    public string WorkerId { get; set; } = "";

    public int InstanceIndex { get; set; }

    public string RecorderHostUrl { get; set; } = "";

    public string OutputDirectory { get; set; } = "";

    public ControlPanelSettingsSnapshot Settings { get; set; } = new ControlPanelSettingsSnapshot();
}

internal sealed class ControlPanelWorkerHeartbeatRequest
{
    public string WorkerId { get; set; } = "";

    public string Status { get; set; } = "Online";

    public string? CurrentReplayId { get; set; }

    public int AppliedGamePresentationSettingsVersion { get; set; }

    public string? GamePresentationSyncStatus { get; set; }

    public string? GamePresentationSyncError { get; set; }
}

internal sealed class ControlPanelWorkerHeartbeatResponse
{
    public bool ShouldCancelAssignment { get; set; }

    public string? CancellationReason { get; set; }

    public bool ShouldOpenPauseMenu { get; set; }

    public int GamePresentationSettingsVersion { get; set; }

    public GamePresentationSettings GamePresentation { get; set; } = new GamePresentationSettings();

    public ControlPanelWorkerRunProgress Progress { get; set; } = new ControlPanelWorkerRunProgress();
}

internal sealed class ControlPanelWorkerAssignmentResponse
{
    public bool HasAssignment { get; set; }

    public string? AssignmentId { get; set; }

    public string? ReplayId { get; set; }

    public string? ReplayPath { get; set; }

    public ReplayProvider Provider { get; set; } = ReplayProvider.BeatLeader;

    public ReplayReferenceKind ReferenceKind { get; set; } = ReplayReferenceKind.LocalBsorFile;

    public string ReplayFormat { get; set; } = "";

    public string? SourceUrl { get; set; }

    public string? ScoreId { get; set; }

    public bool? DisableScoreSubmissions { get; set; }

    public bool? SuppressScoreSaberReplayUi { get; set; }

    public string? SongName { get; set; }

    public string? Mapper { get; set; }

    public string? PlayerName { get; set; }

    public string? Difficulty { get; set; }

    public string? Mode { get; set; }

    public string? LevelHash { get; set; }

    public double EstimatedSeconds { get; set; }

    public string AssignmentKind { get; set; } = "";

    public string? OutputBaseName { get; set; }

    public string? RecorderHostUrl { get; set; }

    public string? OutputDirectory { get; set; }

    public int? InstanceIndex { get; set; }

    public int? TargetProcessId { get; set; }

    public int TargetFps { get; set; }

    public int CaptureWidth { get; set; }

    public int CaptureHeight { get; set; }

    public string Encoder { get; set; } = "";

    public int VideoBitrateKbps { get; set; }

    public string OutputFormat { get; set; } = "";

    public int MonitorIndex { get; set; }

    public string QualityMode { get; set; } = "";

    public string AudioMode { get; set; } = "";

    public string AudioDeviceName { get; set; } = "";

    public int AudioBitrateKbps { get; set; }

    public int AudioSampleRate { get; set; }

    public int AudioChannels { get; set; }

    public string AudioLevelMode { get; set; } = "";

    public double AudioTargetLevelDb { get; set; }

    public double DelayBetweenRecordingsSeconds { get; set; }

    public double? LagSpikeStartupGraceSeconds { get; set; }

    public int GamePresentationSettingsVersion { get; set; }

    public GamePresentationSettings GamePresentation { get; set; } = new GamePresentationSettings();

    public ControlPanelWorkerRunProgress Progress { get; set; } = new ControlPanelWorkerRunProgress();
}

internal sealed class ControlPanelWorkerRunProgress
{
    public int TotalCount { get; set; }

    public int CompletedCount { get; set; }

    public int FailedCount { get; set; }

    public bool IsRunning { get; set; }

    public string Status { get; set; } = "Idle";
}

internal sealed class ControlPanelWorkerReportRequest
{
    public string WorkerId { get; set; } = "";

    public string AssignmentId { get; set; } = "";

    public string Status { get; set; } = "";

    public string? OutputPath { get; set; }

    public string? Error { get; set; }

    public string? Warning { get; set; }

    public string? SyncStatus { get; set; }

    public double? SyncCorrectionMilliseconds { get; set; }

    public double? TrimStartSeconds { get; set; }

    public string? SyncReportPath { get; set; }
}

internal sealed class ControlPanelSettingsSnapshot
{
    public int InstanceCount { get; set; }

    public int TargetFps { get; set; }

    public int CaptureWidth { get; set; }

    public int CaptureHeight { get; set; }

    public string Encoder { get; set; } = "";

    public int VideoBitrateKbps { get; set; }

    public string OutputFormat { get; set; } = "";

    public int MonitorIndex { get; set; }

    public string QualityMode { get; set; } = "";

    public string AudioMode { get; set; } = "";

    public int AudioBitrateKbps { get; set; }

    public int AudioSampleRate { get; set; }

    public int AudioChannels { get; set; }

    public string AudioLevelMode { get; set; } = "";

    public double AudioTargetLevelDb { get; set; }

    public bool? DisableScoreSubmissions { get; set; }

    public bool? SuppressScoreSaberReplayUi { get; set; }

    public int GamePresentationSettingsVersion { get; set; }

    public GamePresentationSettings GamePresentation { get; set; } = new GamePresentationSettings();
}
