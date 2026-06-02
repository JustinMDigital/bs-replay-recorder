using System;

namespace BSAutoReplayRecorder.Core;

public sealed class BatchRecorderSettings
{
    public string SettingsLockMode { get; set; } = "None";

    public bool PersistLockedSettings { get; set; } = true;

    public bool UseSessionFolders { get; set; } = true;

    public string ActiveSessionName { get; set; } = "Default";

    public string SessionRootDirectory { get; set; } = "UserData/BSAutoReplayRecorder/Sessions";

    public bool AutoImportReplays { get; set; } = true;

    public bool MoveImportedReplayFiles { get; set; } = true;

    public string ImportInboxDirectory { get; set; } = "UserData/BSAutoReplayRecorder/Import";

    public bool RequirePreflightReplayValidation { get; set; } = true;

    public bool ShowControlPanelOnStart { get; set; }

    public string ReplayInputDirectory { get; set; } = "UserData/BSAutoReplayRecorder/Input";

    public string CompletedReplayDirectory { get; set; } = "UserData/BSAutoReplayRecorder/Completed";

    public string FailedReplayDirectory { get; set; } = "UserData/BSAutoReplayRecorder/Failed";

    public string RecordingOutputDirectory { get; set; } = "UserData/BSAutoReplayRecorder/Recordings";

    public string OutputNameTemplate { get; set; } = "{index:00} - {song} [{difficulty}]";

    public double PreRollSeconds { get; set; } = 2;

    public double PostRollSeconds { get; set; } = 4;

    public bool IncludeSubdirectories { get; set; }

    public bool SkipCompletedReplays { get; set; } = true;

    public string CompletedReplayStatePath { get; set; } = "UserData/BSAutoReplayRecorder/completed-replays.json";

    public bool AutoStartBatch { get; set; }

    public bool RecordManualReplayStarts { get; set; }

    public double AutoStartDelaySeconds { get; set; } = 5;

    public int MaxReplayCount { get; set; }

    public double ReplayFinishTimeoutPaddingSeconds { get; set; } = 30;

    public double DelayBetweenRecordingsSeconds { get; set; } = 3;

    public int StartRecordingRetryCount { get; set; } = 5;

    public double StartRecordingRetryDelaySeconds { get; set; } = 2;

    public bool MoveRecordingsToOutputDirectory { get; set; } = true;

    public bool MoveProcessedReplays { get; set; }

    public bool OverwriteExistingRecordings { get; set; }

    public bool ContinueAfterFailure { get; set; } = true;

    public bool DryRun { get; set; } = true;

    public ObsConnectionSettings Obs { get; set; } = new ObsConnectionSettings();

    public ReplayQueueOptions ToQueueOptions()
    {
        return new ReplayQueueOptions
        {
            InputDirectory = ReplayInputDirectory,
            IncludeSubdirectories = IncludeSubdirectories
        };
    }

    public RecordingPlanOptions ToRecordingPlanOptions()
    {
        return new RecordingPlanOptions
        {
            OutputNameTemplate = OutputNameTemplate,
            PreRoll = TimeSpan.FromSeconds(Math.Max(0, PreRollSeconds)),
            PostRoll = TimeSpan.FromSeconds(Math.Max(0, PostRollSeconds))
        };
    }
}
