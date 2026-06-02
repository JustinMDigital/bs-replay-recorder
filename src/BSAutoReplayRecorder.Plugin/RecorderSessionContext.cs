using System;
using System.IO;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Utility;

namespace BSAutoReplayRecorder.Plugin;

public sealed class RecorderSessionContext
{
    private RecorderSessionContext(
        string sessionName,
        string sessionDirectory,
        string importInboxDirectory,
        BatchRecorderSettings effectiveSettings)
    {
        SessionName = sessionName;
        SessionDirectory = sessionDirectory;
        ImportInboxDirectory = importInboxDirectory;
        EffectiveSettings = effectiveSettings;
    }

    public string SessionName { get; }

    public string SessionDirectory { get; }

    public string ImportInboxDirectory { get; }

    public BatchRecorderSettings EffectiveSettings { get; }

    public string QueueDirectory => EffectiveSettings.ReplayInputDirectory;

    public string ImportedDirectory => Path.Combine(SessionDirectory, "Imported");

    public string DuplicateImportDirectory => Path.Combine(SessionDirectory, "Duplicate Imports");

    public string FailedImportDirectory => Path.Combine(SessionDirectory, "Failed Imports");

    public static RecorderSessionContext Create(BatchRecorderSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!settings.UseSessionFolders)
        {
            var legacyImportDirectory = GamePaths.ResolveGamePath(settings.ImportInboxDirectory);
            Directory.CreateDirectory(legacyImportDirectory);
            Directory.CreateDirectory(GamePaths.ResolveGamePath(settings.ReplayInputDirectory));

            return new RecorderSessionContext(
                "Legacy",
                GamePaths.GetRecorderUserDataDirectory(),
                legacyImportDirectory,
                settings);
        }

        var sessionName = FileNameSanitizer.SanitizeBaseName(
            string.IsNullOrWhiteSpace(settings.ActiveSessionName) ? "Default" : settings.ActiveSessionName);
        var sessionRoot = GamePaths.ResolveGamePath(settings.SessionRootDirectory);
        var sessionDirectory = Path.Combine(sessionRoot, sessionName);
        var importDirectory = Path.Combine(sessionDirectory, "Import");

        var effectiveSettings = CopySettings(settings);
        effectiveSettings.ReplayInputDirectory = Path.Combine(sessionDirectory, "Queue");
        effectiveSettings.CompletedReplayDirectory = Path.Combine(sessionDirectory, "Completed Replays");
        effectiveSettings.FailedReplayDirectory = Path.Combine(sessionDirectory, "Failed Replays");
        effectiveSettings.CompletedReplayStatePath = Path.Combine(sessionDirectory, "completed-replays.json");
        effectiveSettings.ImportInboxDirectory = importDirectory;

        if (settings.MoveRecordingsToOutputDirectory)
        {
            effectiveSettings.RecordingOutputDirectory = Path.Combine(sessionDirectory, "Recordings");
        }

        Directory.CreateDirectory(sessionDirectory);
        Directory.CreateDirectory(importDirectory);
        Directory.CreateDirectory(effectiveSettings.ReplayInputDirectory);
        Directory.CreateDirectory(effectiveSettings.CompletedReplayDirectory);
        Directory.CreateDirectory(effectiveSettings.FailedReplayDirectory);
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "Recordings"));

        return new RecorderSessionContext(
            sessionName,
            sessionDirectory,
            importDirectory,
            effectiveSettings);
    }

    private static BatchRecorderSettings CopySettings(BatchRecorderSettings settings)
    {
        return new BatchRecorderSettings
        {
            SettingsLockMode = settings.SettingsLockMode,
            PersistLockedSettings = settings.PersistLockedSettings,
            UseSessionFolders = settings.UseSessionFolders,
            ActiveSessionName = settings.ActiveSessionName,
            SessionRootDirectory = settings.SessionRootDirectory,
            AutoImportReplays = settings.AutoImportReplays,
            MoveImportedReplayFiles = settings.MoveImportedReplayFiles,
            ImportInboxDirectory = settings.ImportInboxDirectory,
            RequirePreflightReplayValidation = settings.RequirePreflightReplayValidation,
            ShowControlPanelOnStart = settings.ShowControlPanelOnStart,
            ReplayInputDirectory = settings.ReplayInputDirectory,
            CompletedReplayDirectory = settings.CompletedReplayDirectory,
            FailedReplayDirectory = settings.FailedReplayDirectory,
            RecordingOutputDirectory = settings.RecordingOutputDirectory,
            OutputNameTemplate = settings.OutputNameTemplate,
            PreRollSeconds = settings.PreRollSeconds,
            PostRollSeconds = settings.PostRollSeconds,
            IncludeSubdirectories = settings.IncludeSubdirectories,
            SkipCompletedReplays = settings.SkipCompletedReplays,
            CompletedReplayStatePath = settings.CompletedReplayStatePath,
            AutoStartBatch = settings.AutoStartBatch,
            RecordManualReplayStarts = settings.RecordManualReplayStarts,
            AutoStartDelaySeconds = settings.AutoStartDelaySeconds,
            MaxReplayCount = settings.MaxReplayCount,
            ReplayFinishTimeoutPaddingSeconds = settings.ReplayFinishTimeoutPaddingSeconds,
            DelayBetweenRecordingsSeconds = settings.DelayBetweenRecordingsSeconds,
            StartRecordingRetryCount = settings.StartRecordingRetryCount,
            StartRecordingRetryDelaySeconds = settings.StartRecordingRetryDelaySeconds,
            MoveRecordingsToOutputDirectory = settings.MoveRecordingsToOutputDirectory,
            MoveProcessedReplays = settings.MoveProcessedReplays,
            OverwriteExistingRecordings = settings.OverwriteExistingRecordings,
            ContinueAfterFailure = settings.ContinueAfterFailure,
            DryRun = settings.DryRun,
            Obs = new ObsConnectionSettings
            {
                Host = settings.Obs.Host,
                Port = settings.Obs.Port,
                Password = settings.Obs.Password
            }
        };
    }
}
