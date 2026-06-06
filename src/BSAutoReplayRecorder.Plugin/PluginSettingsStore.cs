using System;
using System.IO;
using BSAutoReplayRecorder.Core;
using IPA.Logging;
using Newtonsoft.Json;

namespace BSAutoReplayRecorder.Plugin;

public static class PluginSettingsStore
{
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
            if (!json.Contains("\"ControlPanelWorker\"") ||
                !json.Contains("\"WindowPlacement\"") ||
                !json.Contains("\"LagSpikeStartupGraceSeconds\"") ||
                !json.Contains("\"DelayBetweenRecordingsSeconds\"") ||
                !json.Contains("\"RefreshSongCoreBeforeReplayValidation\"") ||
                !json.Contains("\"SongCoreRefreshTimeoutSeconds\""))
            {
                shouldSave = true;
            }
        }
        else
        {
            settings = CreateDefaultSettings();
            shouldSave = true;
            logger.Info("Created default recorder worker settings at " + path);
        }

        if (ApplyControlPanelWorkerMode(settings, logger))
        {
            shouldSave = true;
        }

        if (NormalizeLegacyRecorderPaths(settings))
        {
            shouldSave = true;
        }

        if (shouldSave)
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

    private static bool ApplyControlPanelWorkerMode(BatchRecorderSettings settings, Logger logger)
    {
        var changed = false;

        if (settings.ControlPanelWorker == null)
        {
            settings.ControlPanelWorker = new ControlPanelWorkerSettings();
            changed = true;
        }

        if (!settings.ControlPanelWorker.Enabled)
        {
            settings.ControlPanelWorker.Enabled = true;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.ControlPanelWorker.BaseUrl))
        {
            settings.ControlPanelWorker.BaseUrl = "http://127.0.0.1:5770";
            changed = true;
        }

        if (settings.RecorderHost == null)
        {
            settings.RecorderHost = new RecorderHostConnectionSettings();
            changed = true;
        }

        if (settings.WindowPlacement == null)
        {
            settings.WindowPlacement = new WindowPlacementSettings();
            changed = true;
        }

        if (settings.WindowPlacement.Enabled)
        {
            changed |= SetValue(settings.WindowPlacement.UseNativeWindowMove, true, value => settings.WindowPlacement.UseNativeWindowMove = value);
            changed |= SetValue(settings.WindowPlacement.UseBorderlessWindow, true, value => settings.WindowPlacement.UseBorderlessWindow = value);
        }

        if (changed)
        {
            logger.Info("Applied control panel worker settings.");
        }

        return changed;
    }

    private static bool NormalizeLegacyRecorderPaths(BatchRecorderSettings settings)
    {
        var changed = false;
        changed |= NormalizePath(settings.RecordingOutputDirectory, value => settings.RecordingOutputDirectory = value);
        changed |= NormalizePath(settings.RecorderHost.OutputDirectory, value => settings.RecorderHost.OutputDirectory = value);
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
            RecorderHost = new RecorderHostConnectionSettings
            {
                BaseUrl = "http://127.0.0.1:5757",
                WindowTitle = "Beat Saber"
            },
            ControlPanelWorker = new ControlPanelWorkerSettings
            {
                Enabled = true,
                BaseUrl = "http://127.0.0.1:5770"
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
