using BSAutoReplayRecorder.Core;

namespace BSAutoReplayRecorder.ControlPanel;

public sealed class ControlPanelState
{
    public ControlPanelSettings Settings { get; set; } = new ControlPanelSettings();

    public List<ReplayQueueRecord> Queue { get; set; } = new List<ReplayQueueRecord>();

    public List<WorkerInstanceRecord> Instances { get; set; } = new List<WorkerInstanceRecord>();

    public InstanceProvisionReport InstanceProvision { get; set; } = new InstanceProvisionReport();

    public InstanceBaselineReport InstanceBaseline { get; set; } = new InstanceBaselineReport();

    public SongFolderLinkReport SongFolders { get; set; } = new SongFolderLinkReport();

    public DiskSpaceReport DiskSpace { get; set; } = new DiskSpaceReport();

    public List<ControlPanelEventRecord> Events { get; set; } = new List<ControlPanelEventRecord>();

    public RunState Run { get; set; } = new RunState();
}

public sealed class ReplayQueueRecord
{
    public string Id { get; set; } = "";

    public int SequenceNumber { get; set; }

    public string FileName { get; set; } = "";

    public string Path { get; set; } = "";

    public string SongName { get; set; } = "";

    public string Mapper { get; set; } = "";

    public string PlayerName { get; set; } = "";

    public string Difficulty { get; set; } = "";

    public string LevelHash { get; set; } = "";

    public string CoverArtUrl { get; set; } = "";

    public string MapStatus { get; set; } = "Unchecked";

    public string MapStatusDetail { get; set; } = "";

    public string MapInstallPath { get; set; } = "";

    public double EstimatedSeconds { get; set; }

    public string Status { get; set; } = "Queued";

    public int? AssignedInstance { get; set; }

    public string? AssignmentId { get; set; }

    public DateTimeOffset? AssignedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? OutputPath { get; set; }

    public string? Error { get; set; }

    public string SyncStatus { get; set; } = "";

    public double? SyncCorrectionMilliseconds { get; set; }

    public double? TrimStartSeconds { get; set; }

    public string SyncReportPath { get; set; } = "";

    public ReplayCalibrationRecord Calibration { get; set; } = new ReplayCalibrationRecord();

    public bool IsMetadataEdited { get; set; }
}

public sealed class ReplayCalibrationRecord
{
    public string Status { get; set; } = "Unset";

    public double? SyncOffsetMilliseconds { get; set; }

    public double? TrimStartSeconds { get; set; }

    public string Notes { get; set; } = "";

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

public sealed class WorkerInstanceRecord
{
    public int Index { get; set; }

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Status { get; set; } = "Idle";

    public string RecorderHostUrl { get; set; } = "";

    public string OutputDirectory { get; set; } = "";

    public string? CurrentReplayId { get; set; }

    public string? WorkerId { get; set; }

    public string? GameDirectory { get; set; }

    public string LaunchDirectory { get; set; } = "";

    public bool LaunchDirectoryReady { get; set; }

    public string LaunchArguments { get; set; } = "";

    public int? GameProcessId { get; set; }

    public string GameLaunchStatus { get; set; } = "Idle";

    public DateTimeOffset? GameLaunchedAtUtc { get; set; }

    public string? GameLaunchError { get; set; }

    public string AudioRoutingStatus { get; set; } = "Idle";

    public string? AudioRoutingError { get; set; }

    public string? PluginVersion { get; set; }

    public DateTimeOffset? RegisteredAtUtc { get; set; }

    public DateTimeOffset? LastHeartbeatUtc { get; set; }

    public string? ActiveAssignmentId { get; set; }

    public int LastForceStopCommandId { get; set; }

    public int AppliedGamePresentationSettingsVersion { get; set; }

    public string GamePresentationSyncStatus { get; set; } = "Pending";

    public string GamePresentationSyncError { get; set; } = "";
}

public sealed class RunState
{
    public bool IsRunning { get; set; }

    public bool CancellationRequested { get; set; }

    public string? CancellationReason { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public int CompletedCount { get; set; }

    public int FailedCount { get; set; }

    public string Status { get; set; } = "Idle";

    public int ForceStopCommandId { get; set; }
}

public sealed class DiskSpaceReport
{
    public string Status { get; set; } = "Unchecked";

    public string Summary { get; set; } = "Disk space has not been checked.";

    public DateTimeOffset? CheckedAtUtc { get; set; }

    public string Path { get; set; } = "";

    public string DriveName { get; set; } = "";

    public long TotalBytes { get; set; }

    public long AvailableFreeBytes { get; set; }

    public double PercentFree { get; set; }
}

public sealed class ControlPanelEventRecord
{
    public string Id { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string Kind { get; set; } = "Info";

    public string Tag { get; set; } = "";

    public string Text { get; set; } = "";

    public string? ReplayId { get; set; }

    public int? InstanceIndex { get; set; }
}

public sealed class InstanceBaselineReport
{
    public string Status { get; set; } = "Unchecked";

    public string Summary { get; set; } = "Baseline has not been checked.";

    public DateTimeOffset? CheckedAtUtc { get; set; }

    public int BaselineInstanceIndex { get; set; }

    public string BaselineInstanceName { get; set; } = "";

    public List<InstanceBaselineRecord> Instances { get; set; } = new List<InstanceBaselineRecord>();
}

public sealed class InstanceProvisionReport
{
    public string Status { get; set; } = "Unchecked";

    public string Summary { get; set; } = "Managed instances have not been created.";

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string SourceDirectory { get; set; } = "";

    public string TargetRootDirectory { get; set; } = "";

    public bool CopyExistingSongs { get; set; }

    public int DesiredInstanceCount { get; set; }

    public int CreatedInstanceCount { get; set; }

    public int MissingInstanceCount { get; set; }

    public List<InstanceProvisionRecord> Instances { get; set; } = new List<InstanceProvisionRecord>();
}

public sealed class InstanceProvisionRecord
{
    public int Index { get; set; }

    public string Name { get; set; } = "";

    public string Directory { get; set; } = "";

    public string Status { get; set; } = "Unchecked";

    public string Detail { get; set; } = "";
}

public sealed class InstanceBaselineRecord
{
    public int Index { get; set; }

    public string Name { get; set; } = "";

    public string Directory { get; set; } = "";

    public string Status { get; set; } = "Unchecked";

    public int CheckedFileCount { get; set; }

    public List<string> Issues { get; set; } = new List<string>();
}

public sealed class SettingsUpdateRequest
{
    public string RecordingOutputDirectory { get; set; } = "";

    public int InstanceCount { get; set; }

    public int MaxConcurrentRecordings { get; set; }

    public bool RequireAllWorkersReady { get; set; }

    public bool RequireMatchingInstanceBaseline { get; set; }

    public int TargetFps { get; set; }

    public int CaptureWidth { get; set; }

    public int CaptureHeight { get; set; }

    public string Encoder { get; set; } = "";

    public int VideoBitrateKbps { get; set; }

    public string OutputFormat { get; set; } = "";

    public int MonitorIndex { get; set; }

    public string QualityMode { get; set; } = "";

    public string AudioMode { get; set; } = "";

    public bool RequireAudioForRun { get; set; }

    public int AudioBitrateKbps { get; set; }

    public int AudioSampleRate { get; set; }

    public int AudioChannels { get; set; }

    public string AudioLevelMode { get; set; } = "";

    public double AudioTargetLevelDb { get; set; }

    public string BeatSaberInstancesRoot { get; set; } = "";

    public string BeatSaberInstanceNamePrefix { get; set; } = "";

    public string BeatSaberLaunchPreset { get; set; } = "";

    public string BeatSaberLaunchArguments { get; set; } = "";

    public bool ManageDisplayScale { get; set; }

    public int RecordingDisplayScalePercent { get; set; }

    public int RestoreDisplayScalePercent { get; set; }

    public bool HideTaskbarDuringRun { get; set; }

    public double DelayBetweenRecordingsSeconds { get; set; }

    public GamePresentationSettings? GamePresentation { get; set; }

    public string SharedCustomLevelsDirectory { get; set; } = "";

    public string SharedCustomWipLevelsDirectory { get; set; } = "";

    public bool ShareCustomSabers { get; set; }

    public string SharedCustomSabersDirectory { get; set; } = "";

    public bool ShareCustomNotes { get; set; }

    public string SharedCustomNotesDirectory { get; set; } = "";

    public bool ShareCustomPlatforms { get; set; }

    public string SharedCustomPlatformsDirectory { get; set; } = "";

    public bool ShareCustomAvatars { get; set; }

    public string SharedCustomAvatarsDirectory { get; set; } = "";

    public bool ShareCustomWalls { get; set; }

    public string SharedCustomWallsDirectory { get; set; } = "";

    public bool ShareCustomBombs { get; set; }

    public string SharedCustomBombsDirectory { get; set; } = "";
}

public sealed class InstanceProvisionRequest
{
    public string SourceBeatSaberPath { get; set; } = "";

    public int InstanceCount { get; set; }

    public bool OverwriteExisting { get; set; }

    public bool CopyExistingSongs { get; set; }

    public bool CreateMissingOnly { get; set; }
}

public sealed class InstanceEnabledRequest
{
    public bool Enabled { get; set; } = true;
}

public sealed class SongFolderLinkReport
{
    public string Status { get; set; } = "Unchecked";

    public string Summary { get; set; } = "Song folders have not been checked.";

    public DateTimeOffset? CheckedAtUtc { get; set; }

    public string SharedCustomLevelsDirectory { get; set; } = "";

    public string SharedCustomWipLevelsDirectory { get; set; } = "";

    public List<SongFolderLinkRecord> Links { get; set; } = new List<SongFolderLinkRecord>();
}

public sealed class SongFolderLinkRecord
{
    public int InstanceIndex { get; set; }

    public string InstanceName { get; set; } = "";

    public string FolderKind { get; set; } = "";

    public string InstanceFolderPath { get; set; } = "";

    public string SharedFolderPath { get; set; } = "";

    public string Status { get; set; } = "Unchecked";

    public string Detail { get; set; } = "";
}

public sealed class QueueItemUpdateRequest
{
    public string? SongName { get; set; }

    public string? Mapper { get; set; }

    public string? Difficulty { get; set; }

    public double? EstimatedSeconds { get; set; }
}

public sealed class ReplayCalibrationRequest
{
    public string? Status { get; set; }

    public double? SyncOffsetMilliseconds { get; set; }

    public double? TrimStartSeconds { get; set; }

    public string? Notes { get; set; }
}
