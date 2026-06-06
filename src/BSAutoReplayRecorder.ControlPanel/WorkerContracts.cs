using BSAutoReplayRecorder.Core;

namespace BSAutoReplayRecorder.ControlPanel;

public sealed class WorkerRegisterRequest
{
    public string? WorkerId { get; set; }

    public string? WorkerName { get; set; }

    public int? PreferredInstanceIndex { get; set; }

    public string? GameDirectory { get; set; }

    public string? PluginVersion { get; set; }
}

public sealed class WorkerRegisterResponse
{
    public string WorkerId { get; set; } = "";

    public int InstanceIndex { get; set; }

    public string RecorderHostUrl { get; set; } = "";

    public string OutputDirectory { get; set; } = "";

    public ControlPanelSettings Settings { get; set; } = new ControlPanelSettings();
}

public sealed class WorkerHeartbeatRequest
{
    public string WorkerId { get; set; } = "";

    public string Status { get; set; } = "Online";

    public string? CurrentReplayId { get; set; }

    public int AppliedGamePresentationSettingsVersion { get; set; }

    public string? GamePresentationSyncStatus { get; set; }

    public string? GamePresentationSyncError { get; set; }
}

public sealed class WorkerHeartbeatResponse
{
    public bool ShouldCancelAssignment { get; set; }

    public string? CancellationReason { get; set; }

    public bool ShouldOpenPauseMenu { get; set; }

    public int GamePresentationSettingsVersion { get; set; }

    public GamePresentationSettings GamePresentation { get; set; } = new GamePresentationSettings();

    public WorkerRunProgress Progress { get; set; } = new WorkerRunProgress();
}

public sealed class WorkerAssignmentResponse
{
    public bool HasAssignment { get; set; }

    public string? AssignmentId { get; set; }

    public string? ReplayId { get; set; }

    public string? ReplayPath { get; set; }

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

    public double LagSpikeStartupGraceSeconds { get; set; }

    public int GamePresentationSettingsVersion { get; set; }

    public GamePresentationSettings GamePresentation { get; set; } = new GamePresentationSettings();

    public WorkerRunProgress Progress { get; set; } = new WorkerRunProgress();
}

public sealed class WorkerRunProgress
{
    public int TotalCount { get; set; }

    public int CompletedCount { get; set; }

    public int FailedCount { get; set; }

    public bool IsRunning { get; set; }

    public string Status { get; set; } = "Idle";
}

public sealed class WorkerReportRequest
{
    public string WorkerId { get; set; } = "";

    public string AssignmentId { get; set; } = "";

    public string Status { get; set; } = "";

    public string? OutputPath { get; set; }

    public string? Error { get; set; }

    public string? SyncStatus { get; set; }

    public double? SyncCorrectionMilliseconds { get; set; }

    public double? TrimStartSeconds { get; set; }

    public string? SyncReportPath { get; set; }
}
