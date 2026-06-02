using System;
using System.IO;
using BSAutoReplayRecorder.Core;
using IPA.Logging;
using Newtonsoft.Json;

namespace BSAutoReplayRecorder.Plugin;

public static class PluginSettingsStore
{
    private const string ObsPasswordEnvironmentVariable = "BSARR_OBS_PASSWORD";
    private const string LegacyObsPasswordEnvironmentVariable = "BSWCR_OBS_PASSWORD";
    private const string CurrentRecorderDataPath = "UserData/BSAutoReplayRecorder";
    private const string LegacyRecorderDataPath = "UserData/BSWorldCupReplayRecorder";

    public static BatchRecorderSettings LoadOrCreate(Logger logger)
    {
        var path = GamePaths.GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GamePaths.GetRecorderUserDataDirectory());

        BatchRecorderSettings settings;
        var shouldSave = false;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            settings = JsonConvert.DeserializeObject<BatchRecorderSettings>(json) ?? CreateDefaultSettings();
            if (!json.Contains("\"SettingsLockMode\"") ||
                !json.Contains("\"UseSessionFolders\"") ||
                !json.Contains("\"ActiveSessionName\"") ||
                !json.Contains("\"AutoImportReplays\""))
            {
                shouldSave = true;
            }
        }
        else
        {
            settings = CreateDefaultSettings();
            shouldSave = true;
            logger.Info("Created default recorder settings at " + path);
        }

        if (NormalizeLegacyRecorderPaths(settings))
        {
            shouldSave = true;
        }

        if (ApplySettingsLock(settings, logger))
        {
            shouldSave = true;
        }

        var obsPassword = Environment.GetEnvironmentVariable(ObsPasswordEnvironmentVariable);
        var obsPasswordSource = ObsPasswordEnvironmentVariable;
        if (string.IsNullOrEmpty(obsPassword))
        {
            obsPassword = Environment.GetEnvironmentVariable(LegacyObsPasswordEnvironmentVariable);
            obsPasswordSource = LegacyObsPasswordEnvironmentVariable;
        }

        if (!string.IsNullOrEmpty(obsPassword))
        {
            settings.Obs.Password = obsPassword;
            logger.Info("Using OBS password from " + obsPasswordSource + ".");
        }

        if (shouldSave && settings.PersistLockedSettings)
        {
            Save(settings);
        }

        return settings;
    }

    public static void Save(BatchRecorderSettings settings)
    {
        var path = GamePaths.GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GamePaths.GetRecorderUserDataDirectory());
        BackupExistingSettings(path);
        File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
    }

    private static bool ApplySettingsLock(BatchRecorderSettings settings, Logger logger)
    {
        if (!IsStandardLockMode(settings.SettingsLockMode))
        {
            return false;
        }

        var changed = false;
        // Standard lock deliberately does not touch OBS host, port, or password.
        changed |= SetValue(settings.PreRollSeconds, 0, value => settings.PreRollSeconds = value);
        changed |= SetValue(settings.PostRollSeconds, 0, value => settings.PostRollSeconds = value);
        changed |= SetValue(settings.DelayBetweenRecordingsSeconds, 5, value => settings.DelayBetweenRecordingsSeconds = value);
        changed |= SetValue(settings.StartRecordingRetryCount, 5, value => settings.StartRecordingRetryCount = value);
        changed |= SetValue(settings.StartRecordingRetryDelaySeconds, 2, value => settings.StartRecordingRetryDelaySeconds = value);
        changed |= SetValue(settings.MaxReplayCount, 0, value => settings.MaxReplayCount = value);
        changed |= SetValue(settings.IncludeSubdirectories, false, value => settings.IncludeSubdirectories = value);
        changed |= SetValue(settings.DryRun, false, value => settings.DryRun = value);
        changed |= SetValue(settings.MoveProcessedReplays, false, value => settings.MoveProcessedReplays = value);
        changed |= SetValue(settings.MoveRecordingsToOutputDirectory, false, value => settings.MoveRecordingsToOutputDirectory = value);
        changed |= SetValue(settings.SkipCompletedReplays, true, value => settings.SkipCompletedReplays = value);
        changed |= SetValue(settings.ContinueAfterFailure, true, value => settings.ContinueAfterFailure = value);
        changed |= SetValue(settings.AutoImportReplays, true, value => settings.AutoImportReplays = value);
        changed |= SetValue(settings.MoveImportedReplayFiles, true, value => settings.MoveImportedReplayFiles = value);
        changed |= SetValue(settings.RequirePreflightReplayValidation, true, value => settings.RequirePreflightReplayValidation = value);
        changed |= SetValue(settings.UseSessionFolders, true, value => settings.UseSessionFolders = value);

        if (changed)
        {
            logger.Info("Applied Standard settings lock.");
        }

        return changed;
    }

    private static bool IsStandardLockMode(string lockMode)
    {
        return string.Equals(lockMode, "Standard", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(lockMode, "Tournament", StringComparison.OrdinalIgnoreCase);
    }

    private static bool NormalizeLegacyRecorderPaths(BatchRecorderSettings settings)
    {
        var changed = false;
        changed |= NormalizePath(settings.SessionRootDirectory, value => settings.SessionRootDirectory = value);
        changed |= NormalizePath(settings.ImportInboxDirectory, value => settings.ImportInboxDirectory = value);
        changed |= NormalizePath(settings.ReplayInputDirectory, value => settings.ReplayInputDirectory = value);
        changed |= NormalizePath(settings.CompletedReplayDirectory, value => settings.CompletedReplayDirectory = value);
        changed |= NormalizePath(settings.FailedReplayDirectory, value => settings.FailedReplayDirectory = value);
        changed |= NormalizePath(settings.RecordingOutputDirectory, value => settings.RecordingOutputDirectory = value);
        changed |= NormalizePath(settings.CompletedReplayStatePath, value => settings.CompletedReplayStatePath = value);
        return changed;
    }

    private static bool NormalizePath(string path, Action<string> setValue)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            (path.IndexOf(LegacyRecorderDataPath, StringComparison.OrdinalIgnoreCase) < 0 &&
             path.IndexOf("BSWorldCupReplayRecorder", StringComparison.OrdinalIgnoreCase) < 0))
        {
            return false;
        }

        var normalized = path
            .Replace(LegacyRecorderDataPath, CurrentRecorderDataPath)
            .Replace("BSWorldCupReplayRecorder", "BSAutoReplayRecorder");
        setValue(normalized);
        return true;
    }

    private static bool SetValue<T>(T currentValue, T lockedValue, Action<T> setValue)
        where T : IEquatable<T>
    {
        if (currentValue.Equals(lockedValue))
        {
            return false;
        }

        setValue(lockedValue);
        return true;
    }

    private static BatchRecorderSettings CreateDefaultSettings()
    {
        return new BatchRecorderSettings
        {
            SettingsLockMode = "Standard",
            ReplayInputDirectory = GamePaths.GetBeatLeaderReplaysPath(),
            DryRun = true,
            Obs = new ObsConnectionSettings
            {
                Host = "127.0.0.1",
                Port = 4455
            }
        };
    }

    private static void BackupExistingSettings(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path) ?? GamePaths.GetRecorderUserDataDirectory();
        var fileName = Path.GetFileName(path);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(directory, fileName + "." + timestamp + ".bak");

        for (var index = 2; File.Exists(backupPath); index++)
        {
            backupPath = Path.Combine(directory, fileName + "." + timestamp + "." + index + ".bak");
        }

        File.Copy(path, backupPath, overwrite: false);
    }
}
