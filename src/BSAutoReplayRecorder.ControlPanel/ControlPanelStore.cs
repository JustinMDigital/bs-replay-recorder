using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Utility;
using Microsoft.Win32;

namespace BSAutoReplayRecorder.ControlPanel;

public sealed class ControlPanelStore
{
    private static readonly TimeSpan WorkerStaleAfter = TimeSpan.FromSeconds(30);
    private const string BeatSaberExecutableName = "Beat Saber.exe";
    private const string BeatSaberProcessName = "Beat Saber";
    private const string BeatSaberSteamAppId = "620980";
    private static readonly string[] BeatSaberSettingsIniRelativePaths =
    {
        "settings.ini",
        "UserData/settings.ini"
    };
    private static readonly string[] PreviousSongLibraryRelativePaths =
    {
        "Beat Saber_Data/CustomLevels",
        "Beat Saber_Data/CustomWIPLevels"
    };
    private static readonly string[] PreviousSongLibraryBackupRelativePathPrefixes =
    {
        "Beat Saber_Data/CustomLevels.local-",
        "Beat Saber_Data/CustomWIPLevels.local-"
    };
    private static readonly string[] ProvisionTransientRelativePaths =
    {
        "BSWC Recording Files",
        "Logs",
        "UserData/BeatLeader/Replays",
        "UserData/BSWorldCupReplayRecorder/Recordings",
        "UserData/BSAutoReplayRecorder/Recordings"
    };

    private readonly object _sync = new object();
    private readonly string _statePath;
    private readonly string _queueDirectory;
    private readonly IRecordingAudioVerifier _recordingAudioVerifier;
    private readonly IRecorderHostHealthChecker _recorderHostHealthChecker;
    private readonly IBeatSaverMapDownloader _mapDownloader;
    private readonly IWorkerPluginInstaller _workerPluginInstaller;
    private readonly ControlPanelState _state;
    private bool _taskbarHiddenForRun;
    private int? _detectedRestoreDisplayScalePercent;

    public ControlPanelStore(
        ControlPanelSettings settings,
        IRecordingAudioVerifier? recordingAudioVerifier = null,
        IRecorderHostHealthChecker? recorderHostHealthChecker = null,
        IBeatSaverMapDownloader? mapDownloader = null,
        IWorkerPluginInstaller? workerPluginInstaller = null)
    {
        settings.Normalize();
        var workspaceDirectory = Path.GetFullPath(settings.WorkspaceDirectory);
        Directory.CreateDirectory(workspaceDirectory);

        _statePath = Path.Combine(workspaceDirectory, "control-panel-state.json");
        _queueDirectory = Path.Combine(workspaceDirectory, "Queue");
        _recordingAudioVerifier = recordingAudioVerifier ?? new FfprobeRecordingAudioVerifier();
        _recorderHostHealthChecker = recorderHostHealthChecker ?? new HttpRecorderHostHealthChecker();
        _mapDownloader = mapDownloader ?? new BeatSaverMapDownloader(new HttpClient());
        _workerPluginInstaller = workerPluginInstaller ?? new DotNetWorkerPluginInstaller();
        Directory.CreateDirectory(_queueDirectory);

        _state = LoadState(settings);
        TaskbarVisibilityController.Restore();
        _taskbarHiddenForRun = false;
        EnsureInstancesNoLock();
        RedistributeQueuedReplayPlansNoLock();
        SynchronizeMaxConcurrentRecordingsNoLock();
        _state.SongFolders = ScanSongFolderLinksNoLock();
        RefreshDiskSpaceNoLock();
        SaveNoLock();
    }

    public string QueueDirectory => _queueDirectory;

    public ControlPanelState Snapshot()
    {
        lock (_sync)
        {
            EnsureInstancesNoLock();
            var changed = ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            changed |= RefreshLaunchProcessesNoLock();
            changed |= RefreshQueueMetadataNoLock();
            changed |= RefreshQueueMapAvailabilityNoLock(allowDownload: false);
            changed |= RefreshInstanceProvisionCountsNoLock();
            changed |= RedistributeQueuedReplayPlansNoLock();
            changed |= SynchronizeMaxConcurrentRecordingsNoLock();
            changed |= RefreshDiskSpaceNoLock();
            changed |= NormalizeRunSummaryNoLock();
            if (changed)
            {
                SaveNoLock();
            }

            return Clone(_state);
        }
    }

    public ControlPanelState UpdateSettings(SettingsUpdateRequest request)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            if ((_state.Run.IsRunning || _state.Run.CancellationRequested) &&
                request.InstanceCount != _state.Settings.InstanceCount)
            {
                throw new InvalidOperationException(
                    "Stop the current run before changing instance count.");
            }

            var previousGamePresentation = CloneGamePresentationSettings(_state.Settings.GamePresentation);
            var previousGamePresentationVersion = Math.Max(1, _state.Settings.GamePresentationSettingsVersion);

            _state.Settings.RecordingOutputDirectory = request.RecordingOutputDirectory;
            _state.Settings.InstanceCount = request.InstanceCount;
            _state.Settings.MaxConcurrentRecordings = request.MaxConcurrentRecordings;
            _state.Settings.RequireAllWorkersReady = request.RequireAllWorkersReady;
            _state.Settings.RequireMatchingInstanceBaseline = request.RequireMatchingInstanceBaseline;
            _state.Settings.TargetFps = request.TargetFps;
            _state.Settings.CaptureWidth = request.CaptureWidth;
            _state.Settings.CaptureHeight = request.CaptureHeight;
            _state.Settings.Encoder = request.Encoder;
            _state.Settings.VideoBitrateKbps = request.VideoBitrateKbps;
            _state.Settings.OutputFormat = request.OutputFormat;
            _state.Settings.MonitorIndex = request.MonitorIndex;
            _state.Settings.QualityMode = request.QualityMode;
            _state.Settings.AudioMode = request.AudioMode;
            _state.Settings.RequireAudioForRun = request.RequireAudioForRun;
            _state.Settings.AudioBitrateKbps = request.AudioBitrateKbps;
            _state.Settings.AudioSampleRate = request.AudioSampleRate;
            _state.Settings.AudioChannels = request.AudioChannels;
            _state.Settings.AudioLevelMode = request.AudioLevelMode;
            _state.Settings.AudioTargetLevelDb = request.AudioTargetLevelDb;
            _state.Settings.SharedCustomLevelsDirectory = request.SharedCustomLevelsDirectory;
            _state.Settings.SharedCustomWipLevelsDirectory = request.SharedCustomWipLevelsDirectory;
            _state.Settings.ShareCustomSabers = request.ShareCustomSabers;
            _state.Settings.SharedCustomSabersDirectory = request.SharedCustomSabersDirectory;
            _state.Settings.ShareCustomNotes = request.ShareCustomNotes;
            _state.Settings.SharedCustomNotesDirectory = request.SharedCustomNotesDirectory;
            _state.Settings.ShareCustomPlatforms = request.ShareCustomPlatforms;
            _state.Settings.SharedCustomPlatformsDirectory = request.SharedCustomPlatformsDirectory;
            _state.Settings.ShareCustomAvatars = request.ShareCustomAvatars;
            _state.Settings.SharedCustomAvatarsDirectory = request.SharedCustomAvatarsDirectory;
            _state.Settings.ShareCustomWalls = request.ShareCustomWalls;
            _state.Settings.SharedCustomWallsDirectory = request.SharedCustomWallsDirectory;
            _state.Settings.ShareCustomBombs = request.ShareCustomBombs;
            _state.Settings.SharedCustomBombsDirectory = request.SharedCustomBombsDirectory;
            _state.Settings.BeatSaberInstancesRoot = request.BeatSaberInstancesRoot;
            _state.Settings.BeatSaberInstanceNamePrefix = request.BeatSaberInstanceNamePrefix;
            _state.Settings.BeatSaberLaunchPreset = request.BeatSaberLaunchPreset;
            _state.Settings.BeatSaberLaunchArguments = request.BeatSaberLaunchArguments;
            _state.Settings.ManageDisplayScale = request.ManageDisplayScale;
            _state.Settings.RecordingDisplayScalePercent = request.RecordingDisplayScalePercent;
            _state.Settings.RestoreDisplayScalePercent = request.RestoreDisplayScalePercent;
            _state.Settings.HideTaskbarDuringRun = request.HideTaskbarDuringRun;
            _state.Settings.DelayBetweenRecordingsSeconds = request.DelayBetweenRecordingsSeconds;
            if (request.GamePresentation != null)
            {
                _state.Settings.GamePresentation = CloneGamePresentationSettings(request.GamePresentation);
            }

            _state.Settings.Normalize();
            if (request.GamePresentation != null)
            {
                _state.Settings.GamePresentationSettingsVersion = GamePresentationSettingsEqual(
                        previousGamePresentation,
                        _state.Settings.GamePresentation)
                    ? previousGamePresentationVersion
                    : NextGamePresentationSettingsVersion(previousGamePresentationVersion);
            }

            EnsureInstancesNoLock();
            SynchronizeMaxConcurrentRecordingsNoLock();
            _state.InstanceBaseline = new InstanceBaselineReport();
            _state.SongFolders = ScanSongFolderLinksNoLock();
            RefreshInstanceProvisionCountsNoLock();
            RefreshDiskSpaceNoLock();
            AddEventNoLock("Info", "Settings", "Settings saved.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public IReadOnlyList<ReplayQueueRecord> ImportFiles(IFormFileCollection files)
    {
        if (files == null || files.Count == 0)
        {
            return Array.Empty<ReplayQueueRecord>();
        }

        var imported = new List<ReplayQueueRecord>();
        var importedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            foreach (var file in files)
            {
                if (file.Length <= 0 ||
                    !string.Equals(Path.GetExtension(file.FileName), ".bsor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var targetPath = CreateImportPath(file.FileName);
                using (var stream = File.Create(targetPath))
                {
                    file.CopyTo(stream);
                }

                importedPaths.Add(Path.GetFullPath(targetPath));
            }

            if (importedPaths.Count == 0)
            {
                return imported;
            }

            ReloadQueueNoLock();
            RedistributeQueuedReplayPlansNoLock();
            imported.AddRange(_state.Queue.Where(item => importedPaths.Contains(Path.GetFullPath(item.Path))));
            RefreshQueueMapAvailabilityNoLock(allowDownload: true, imported.Select(item => item.Id));
            AddEventNoLock(
                "Info",
                "Import",
                "Imported " + imported.Count + " replay" + (imported.Count == 1 ? "" : "s") + ".");
            SaveNoLock();
        }

        return imported;
    }

    public ControlPanelState DownloadQueueMap(string id)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            var replay = FindQueueItemNoLock(id);
            EnsureQueueItemCanChangeNoLock(replay, "download map for");
            RefreshQueueMapAvailabilityNoLock(allowDownload: true, new[] { replay.Id });
            AddEventNoLock("Info", "Map", "Map repair checked: " + CreateReplayLabel(replay), replay.Id);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState ImportQueueMap(string id, IFormFile file)
    {
        if (file == null || file.Length <= 0)
        {
            throw new InvalidOperationException("Choose a song zip first.");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Song uploads must be .zip files.");
        }

        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            var replay = FindQueueItemNoLock(id);
            EnsureQueueItemCanChangeNoLock(replay, "upload a map for");

            var targetRoot = ResolveManualMapUploadRootNoLock();
            Directory.CreateDirectory(targetRoot);
            var targetDirectory = CreateUniqueMapDirectory(targetRoot, CreateMapFolderName(replay, file.FileName));
            var tempZip = Path.Combine(Path.GetTempPath(), "bsarr-upload-map-" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                using (var stream = File.Create(tempZip))
                {
                    file.CopyTo(stream);
                }

                BeatSaverMapDownloader.ExtractMapZip(tempZip, targetDirectory);
                replay.MapStatus = "Found";
                replay.MapStatusDetail = "Uploaded song zip to WIP songs.";
                replay.MapInstallPath = targetDirectory;
                replay.CoverArtUrl = "/api/queue/" + Uri.EscapeDataString(replay.Id) + "/cover";
                AddEventNoLock("Good", "Map", "Map uploaded: " + CreateReplayLabel(replay), replay.Id);
                SaveNoLock();
                return Clone(_state);
            }
            catch
            {
                if (Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, recursive: true);
                }

                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip))
                    {
                        File.Delete(tempZip);
                    }
                }
                catch
                {
                }
            }
        }
    }

    public ControlPanelState ClearQueue()
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            foreach (var file in Directory.EnumerateFiles(_queueDirectory, "*.bsor"))
            {
                File.Delete(file);
            }

            _state.Queue.Clear();
            ResetRunNoLock("Idle");
            foreach (var instance in _state.Instances)
            {
                instance.ActiveAssignmentId = null;
                instance.CurrentReplayId = null;
                instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
            }

            AddEventNoLock("Info", "Queue", "Queue cleared.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState UpdateQueueItem(string id, QueueItemUpdateRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Queue update request is required.");
        }

        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            var replay = FindQueueItemNoLock(id);
            EnsureQueueItemCanChangeNoLock(replay, "edit");

            replay.SongName = NormalizeNullable(request.SongName) ?? Path.GetFileNameWithoutExtension(replay.FileName);
            replay.Mapper = NormalizeNullable(request.Mapper) ?? "";
            replay.Difficulty = NormalizeNullable(request.Difficulty) ?? "";
            replay.EstimatedSeconds = Math.Round(Math.Max(0, request.EstimatedSeconds ?? replay.EstimatedSeconds), 2);
            replay.IsMetadataEdited = true;
            AddEventNoLock("Info", "Replay", "Replay metadata updated: " + CreateReplayLabel(replay), replay.Id);

            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState UpdateReplayCalibration(string id, ReplayCalibrationRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Replay calibration request is required.");
        }

        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            var replay = FindQueueItemNoLock(id);
            EnsureQueueItemCanChangeNoLock(replay, "calibrate");

            var status = NormalizeNullable(request.Status) ?? "Manual";
            var offset = request.SyncOffsetMilliseconds;
            var trim = request.TrimStartSeconds;
            if (offset.HasValue && (double.IsNaN(offset.Value) || double.IsInfinity(offset.Value)))
            {
                throw new InvalidOperationException("Sync offset must be a finite number.");
            }

            if (trim.HasValue && (double.IsNaN(trim.Value) || double.IsInfinity(trim.Value) || trim.Value < 0))
            {
                throw new InvalidOperationException("Trim start must be zero or greater.");
            }

            replay.Calibration = new ReplayCalibrationRecord
            {
                Status = status,
                SyncOffsetMilliseconds = offset.HasValue ? Math.Round(offset.Value, 2) : null,
                TrimStartSeconds = trim.HasValue ? Math.Round(trim.Value, 3) : null,
                Notes = NormalizeNullable(request.Notes) ?? "",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            if (offset.HasValue || trim.HasValue)
            {
                replay.SyncStatus = status;
                replay.SyncCorrectionMilliseconds = replay.Calibration.SyncOffsetMilliseconds;
                replay.TrimStartSeconds = replay.Calibration.TrimStartSeconds;
            }

            AddEventNoLock("Info", "Calibration", "Calibration updated: " + CreateReplayLabel(replay), replay.Id);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState MoveQueueItem(string id, int delta)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            var index = FindQueueItemIndexNoLock(id);
            var replay = _state.Queue[index];
            EnsureQueueItemCanChangeNoLock(replay, "move");

            var targetIndex = Math.Clamp(index + delta, 0, _state.Queue.Count - 1);
            if (targetIndex == index)
            {
                return Clone(_state);
            }

            _state.Queue.RemoveAt(index);
            _state.Queue.Insert(targetIndex, replay);
            ResequenceQueueNoLock();
            RedistributeQueuedReplayPlansNoLock();
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState RequeueQueueItem(string id)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            var replay = FindQueueItemNoLock(id);
            EnsureQueueItemCanChangeNoLock(replay, "requeue");

            replay.Status = "Queued";
            replay.AssignedInstance = null;
            replay.AssignmentId = null;
            replay.AssignedAtUtc = null;
            replay.CompletedAtUtc = null;
            replay.OutputPath = null;
            replay.Error = null;
            RecalculateRunCountsNoLock();
            RedistributeQueuedReplayPlansNoLock();

            AddEventNoLock("Info", "Queue", "Replay requeued: " + CreateReplayLabel(replay), replay.Id);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState RemoveQueueItem(string id)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            var index = FindQueueItemIndexNoLock(id);
            var replay = _state.Queue[index];
            EnsureQueueItemCanChangeNoLock(replay, "remove");

            var replayPath = Path.GetFullPath(replay.Path);
            if (IsPathInsideDirectory(replayPath, _queueDirectory) && File.Exists(replayPath))
            {
                File.Delete(replayPath);
            }

            _state.Queue.RemoveAt(index);
            ResequenceQueueNoLock();
            RedistributeQueuedReplayPlansNoLock();
            RecalculateRunCountsNoLock();
            TryCompleteRunNoLock(DateTimeOffset.UtcNow);

            AddEventNoLock("Info", "Queue", "Replay removed: " + CreateReplayLabel(replay), replay.Id);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public string GetRecordedFileUri(string id)
    {
        return new Uri(GetRecordedFilePath(id)).AbsoluteUri;
    }

    public string GetRecordedFilePath(string id)
    {
        lock (_sync)
        {
            var replay = FindQueueItemNoLock(id);
            if (!string.Equals(replay.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Replay is not completed yet.");
            }

            var outputPath = NormalizeNullable(replay.OutputPath);
            if (outputPath == null)
            {
                throw new InvalidOperationException("Completed replay does not have a recorded file path.");
            }

            return Path.GetFullPath(outputPath);
        }
    }

    public ControlPanelState StartRun()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            ExpireStaleWorkersNoLock(now);
            if (_state.Settings.RequireAllWorkersReady)
            {
                var runInstances = GetRunInstancesNoLock();
                var requiredWorkers = runInstances.Count;
                var readyWorkers = runInstances.Count(instance =>
                    !string.IsNullOrWhiteSpace(instance.WorkerId) &&
                    !IsWorkerStale(instance, now));
                if (readyWorkers < requiredWorkers)
                {
                    throw new InvalidOperationException(
                        "Only " + readyWorkers + "/" + requiredWorkers +
                        " workers are online. Start the missing instances or turn off Require all workers.");
                }
            }

            ValidateInstanceBaselineForRunNoLock();
            ValidateQueueMapsForRunNoLock();
            ValidateAudioSettingsForRunNoLock();
            ValidateRecorderHostsForRunNoLock();
            ApplyTaskbarVisibilityForRunNoLock();

            ResetAssignmentsNoLock();
            _state.Run.IsRunning = true;
            _state.Run.CancellationRequested = false;
            _state.Run.CancellationReason = null;
            _state.Run.StartedAtUtc = now;
            _state.Run.FinishedAtUtc = null;
            _state.Run.CompletedCount = 0;
            _state.Run.FailedCount = 0;
            _state.Run.Status = "Running";

            foreach (var replay in _state.Queue)
            {
                replay.Status = "Queued";
            }

            RedistributeQueuedReplayPlansNoLock();

            foreach (var instance in _state.Instances)
            {
                instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
            }

            AddEventNoLock("Info", "Run", "Run started with " + _state.Queue.Count + " replay" + (_state.Queue.Count == 1 ? "" : "s") + ".");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState StopRun()
    {
        lock (_sync)
        {
            RequestRunCancellationNoLock("Stopped by operator.", failQueued: false);
            TryFinalizeCanceledRunNoLock(DateTimeOffset.UtcNow);
            AddEventNoLock("Warn", "Run", "Run stop requested.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState ForceStopAllGames()
    {
        lock (_sync)
        {
            EnsureInstancesNoLock();
            _state.Run.ForceStopCommandId++;
            RequestRunCancellationNoLock("Force stopped by operator.", failQueued: false);
            TryFinalizeCanceledRunNoLock(DateTimeOffset.UtcNow);
            AddEventNoLock("Bad", "Instance", "Force stop requested for all instances.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState CheckInstanceBaseline()
    {
        lock (_sync)
        {
            EnsureInstancesNoLock();
            _state.InstanceBaseline = new InstanceBaselineScanner().Scan(_state.Instances);
            AddEventNoLock("Info", "Baseline", "Baseline check " + _state.InstanceBaseline.Status + ".");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState ProvisionManagedInstances(InstanceProvisionRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Instance provision request is required.");
        }

        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            ApplyProvisionInstanceCountNoLock(request.InstanceCount);
            EnsureInstancesNoLock();
            var targetRootDirectory = Path.GetFullPath(_state.Settings.BeatSaberInstancesRoot);
            var targets = ResolveManagedInstanceTargetsNoLock(targetRootDirectory);

            Directory.CreateDirectory(targetRootDirectory);
            var records = new List<InstanceProvisionRecord>();
            var baseline = targets[0];
            string sourceDirectory;
            var copyExistingSongs = request.CopyExistingSongs && !request.CreateMissingOnly;
            if (request.CreateMissingOnly)
            {
                if (!File.Exists(Path.Combine(baseline.Directory, BeatSaberExecutableName)))
                {
                    throw new InvalidOperationException(
                        "Instance 1 must exist before creating missing instances: " + baseline.Directory);
                }

                sourceDirectory = baseline.Directory;
                foreach (var target in targets.Skip(1)
                             .Where(target => !File.Exists(Path.Combine(target.Directory, BeatSaberExecutableName))))
                {
                    CopyManagedInstanceDirectory(
                        sourceDirectory,
                        target.Directory,
                        overwriteExisting: false,
                        copyExistingSongs: false);
                    records.Add(CreateProvisionRecord(
                        target,
                        "Copied",
                        "Copied from " + baseline.Name + ", excluding existing songs."));
                }
            }
            else
            {
                sourceDirectory = ResolveProvisionSourceDirectory(request.SourceBeatSaberPath);
                ValidateProvisionSourceAndTargets(sourceDirectory, targetRootDirectory, targets, request.OverwriteExisting);

                CopyManagedInstanceDirectory(
                    sourceDirectory,
                    baseline.Directory,
                    request.OverwriteExisting,
                    copyExistingSongs);
                records.Add(CreateProvisionRecord(
                    baseline,
                    "Copied",
                    copyExistingSongs
                        ? "Copied from " + sourceDirectory + ", including existing songs."
                        : "Copied from " + sourceDirectory + ", excluding existing songs."));

                for (var index = 1; index < targets.Count; index++)
                {
                    var target = targets[index];
                    CopyManagedInstanceDirectory(
                        baseline.Directory,
                        target.Directory,
                        request.OverwriteExisting,
                        copyExistingSongs: false);
                    records.Add(CreateProvisionRecord(
                        target,
                        "Copied",
                        copyExistingSongs
                            ? "Copied game files from " + baseline.Name + "; existing songs stay on the baseline for shared-folder import."
                            : "Copied from " + baseline.Name + ", excluding existing songs."));
                }
            }

            BackupBeatSaberSettingsIniNoLock(_state.Instances);
            _workerPluginInstaller.Install(_state.Instances, _state.Settings);
            foreach (var record in records)
            {
                record.Detail += " Worker plugin installed.";
            }

            _state.InstanceProvision = new InstanceProvisionReport
            {
                Status = "Ready",
                Summary = CreateProvisionSummary(targets.Count, records.Count, request.CreateMissingOnly, copyExistingSongs),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                SourceDirectory = sourceDirectory,
                TargetRootDirectory = targetRootDirectory,
                CopyExistingSongs = copyExistingSongs,
                DesiredInstanceCount = _state.Settings.InstanceCount,
                CreatedInstanceCount = GetCreatedManagedInstancesNoLock().Count,
                MissingInstanceCount = Math.Max(0, _state.Settings.InstanceCount - GetCreatedManagedInstancesNoLock().Count),
                Instances = records
            };
            _state.InstanceBaseline = new InstanceBaselineScanner().Scan(_state.Instances);
            RepairSongFolderLinksNoLock();
            RefreshInstanceProvisionCountsNoLock();
            SynchronizeMaxConcurrentRecordingsNoLock();
            RefreshDiskSpaceNoLock();
            AddEventNoLock("Good", "Setup", _state.InstanceProvision.Summary);
            SaveNoLock();
            return Clone(_state);
        }
    }

    private void ApplyProvisionInstanceCountNoLock(int requestedInstanceCount)
    {
        if (requestedInstanceCount == 0)
        {
            _state.Settings.Normalize();
            return;
        }

        if (requestedInstanceCount < ControlPanelSettings.MinimumManagedInstanceCount ||
            requestedInstanceCount > ControlPanelSettings.MaximumManagedInstanceCount)
        {
            throw new InvalidOperationException(
                "Instance count must be between " +
                ControlPanelSettings.MinimumManagedInstanceCount +
                " and " +
                ControlPanelSettings.MaximumManagedInstanceCount +
                ".");
        }

        _state.Settings.InstanceCount = requestedInstanceCount;
        _state.Settings.MaxConcurrentRecordings = requestedInstanceCount;
        if (requestedInstanceCount == 1)
        {
            _state.Settings.RequireMatchingInstanceBaseline = false;
            _state.Settings.ManageDisplayScale = false;
            _state.Settings.HideTaskbarDuringRun = false;
        }

        _state.Settings.Normalize();
    }

    private static void BackupBeatSaberSettingsIniNoLock(IEnumerable<WorkerInstanceRecord> instances)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        foreach (var instance in instances)
        {
            foreach (var relativePath in BeatSaberSettingsIniRelativePaths)
            {
                var path = Path.Combine(instance.LaunchDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                BackupFileIfExists(path, timestamp);
            }
        }
    }

    private static void BackupFileIfExists(string path, string timestamp)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backupPath = path + "." + timestamp + ".bak";
        for (var index = 2; File.Exists(backupPath); index++)
        {
            backupPath = path + "." + timestamp + "." + index + ".bak";
        }

        File.Copy(path, backupPath, overwrite: false);
    }

    private static string CreateProvisionSummary(
        int desiredCount,
        int copiedCount,
        bool createMissingOnly,
        bool copyExistingSongs)
    {
        if (createMissingOnly)
        {
            if (copiedCount == 0)
            {
                return "All " + desiredCount + " managed Beat Saber instance" +
                       (desiredCount == 1 ? "" : "s") +
                       " are already ready with the worker plugin installed.";
            }

            return "Created " + copiedCount + " missing managed Beat Saber instance" +
                   (copiedCount == 1 ? "" : "s") +
                   " from instance 1, with the worker plugin installed.";
        }

        return "Created " + desiredCount + " managed Beat Saber instance" +
               (desiredCount == 1 ? "" : "s") +
               ", installed the worker plugin," +
               (copyExistingSongs ? " and imported existing songs." : " without importing existing songs.");
    }

    public ControlPanelState CheckSongFolderLinks()
    {
        lock (_sync)
        {
            EnsureInstancesNoLock();
            _state.SongFolders = ScanSongFolderLinksNoLock();
            AddEventNoLock("Info", "Files", "Shared folder check " + _state.SongFolders.Status + ".");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState RepairSongFolderLinks()
    {
        lock (_sync)
        {
            EnsureInstancesNoLock();
            RepairSongFolderLinksNoLock();
            AddEventNoLock("Good", "Files", "Shared folder repair " + _state.SongFolders.Status + ".");
            SaveNoLock();
            return Clone(_state);
        }
    }

    private void RepairSongFolderLinksNoLock()
    {
        var definitions = CreateSharedFolderDefinitionsNoLock().ToList();
        foreach (var definition in definitions)
        {
            Directory.CreateDirectory(definition.SharedFolderPath);
            SeedSharedFolderNoLock(definition);
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        foreach (var instance in _state.Instances)
        {
            foreach (var definition in definitions)
            {
                EnsureSongFolderLinkNoLock(instance, definition, timestamp);
            }
        }

        _state.SongFolders = ScanSongFolderLinksNoLock();
    }

    public ControlPanelState LaunchInstance(int index)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            RefreshLaunchProcessesNoLock();
            EnsureInstancesNoLock();
            var instance = _state.Instances.FirstOrDefault(item => item.Index == index);
            if (instance == null)
            {
                throw new InvalidOperationException("Instance index is outside the configured instance count.");
            }

            if (!instance.Enabled)
            {
                throw new InvalidOperationException("Enable " + instance.Name + " before launching it.");
            }

            LaunchInstanceNoLock(instance);
            if (!string.Equals(instance.GameLaunchStatus, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                AddEventNoLock(
                    "Info",
                    "Launch",
                    "Launch " + instance.GameLaunchStatus.ToLowerInvariant() + ": " + instance.Name,
                    instanceIndex: instance.Index);
            }
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState QuitInstance(int index)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            RefreshLaunchProcessesNoLock();
            EnsureInstancesNoLock();
            var instance = _state.Instances.FirstOrDefault(item => item.Index == index);
            if (instance == null)
            {
                throw new InvalidOperationException("Instance index is outside the configured instance count.");
            }

            var quitProcess = QuitInstanceProcessNoLock(instance);
            AddEventNoLock(
                quitProcess ? "Warn" : "Info",
                "Instance",
                quitProcess
                    ? "Quit requested for " + instance.Name + "."
                    : "No running game was found for " + instance.Name + ".",
                instanceIndex: instance.Index);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState SetInstanceEnabled(int index, bool enabled)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            EnsureInstancesNoLock();
            var instance = _state.Instances.FirstOrDefault(item => item.Index == index);
            if (instance == null)
            {
                throw new InvalidOperationException("Instance index is outside the configured instance count.");
            }

            if (!enabled)
            {
                var enabledCount = GetEnabledConfiguredInstancesNoLock().Count;
                if (enabledCount <= 1)
                {
                    throw new InvalidOperationException("At least one managed instance must stay enabled.");
                }

                var activeReplay = FindActiveReplayNoLock(instance);
                if (activeReplay != null || IsActiveReplayStatus(instance.Status) || IsRecordingStatus(instance.Status))
                {
                    throw new InvalidOperationException("Cannot disable " + instance.Name + " while it is recording.");
                }
            }

            if (instance.Enabled == enabled)
            {
                return Clone(_state);
            }

            instance.Enabled = enabled;
            if (!enabled)
            {
                ReleaseActiveAssignmentNoLock(instance, "Queued");
                instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
            }

            RedistributeQueuedReplayPlansNoLock();
            SynchronizeMaxConcurrentRecordingsNoLock();
            AddEventNoLock(
                enabled ? "Info" : "Warn",
                "Instance",
                instance.Name + (enabled ? " enabled for scheduling." : " disabled for scheduling."),
                instanceIndex: instance.Index);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState RemoveManagedInstance(int index)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            EnsureInstancesNoLock();

            if (_state.Run.IsRunning || _state.Run.CancellationRequested)
            {
                throw new InvalidOperationException("Stop the current run before removing a managed instance.");
            }

            var currentCount = _state.Settings.InstanceCount;
            if (currentCount <= ControlPanelSettings.MinimumManagedInstanceCount)
            {
                throw new InvalidOperationException("At least one managed instance must stay configured.");
            }

            var lastIndex = currentCount - 1;
            if (index != lastIndex)
            {
                throw new InvalidOperationException("Remove the highest-numbered managed instance first.");
            }

            var instance = _state.Instances.FirstOrDefault(item => item.Index == index);
            if (instance == null)
            {
                throw new InvalidOperationException("Instance index is outside the configured instance count.");
            }

            if (!string.IsNullOrWhiteSpace(instance.WorkerId) && !IsWorkerStale(instance, DateTimeOffset.UtcNow))
            {
                throw new InvalidOperationException("Close " + instance.Name + " before removing it.");
            }

            var activeReplay = FindActiveReplayNoLock(instance);
            if (activeReplay != null || IsActiveReplayStatus(instance.Status) || IsRecordingStatus(instance.Status))
            {
                throw new InvalidOperationException("Cannot remove " + instance.Name + " while it is recording.");
            }

            if (FindBeatSaberProcessIdForInstance(instance).HasValue)
            {
                throw new InvalidOperationException("Close " + instance.Name + " before removing it.");
            }

            DeleteManagedInstanceDirectoryNoLock(instance);
            ReleaseActiveAssignmentNoLock(instance, "Queued");
            _state.Settings.InstanceCount = currentCount - 1;
            _state.Settings.Normalize();
            EnsureInstancesNoLock();
            RedistributeQueuedReplayPlansNoLock();
            SynchronizeMaxConcurrentRecordingsNoLock();
            RefreshInstanceProvisionCountsNoLock();
            RefreshDiskSpaceNoLock();
            AddEventNoLock("Warn", "Instance", instance.Name + " removed from managed instances.", instanceIndex: index);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState LaunchAllInstances()
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            RefreshLaunchProcessesNoLock();
            EnsureInstancesNoLock();
            foreach (var instance in GetRunInstancesNoLock())
            {
                LaunchInstanceNoLock(instance);
            }

            AddEventNoLock("Info", "Launch", "Launch requested for all enabled instances.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public WorkerRegisterResponse RegisterWorker(WorkerRegisterRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Worker registration request is required.");
        }

        lock (_sync)
        {
            EnsureInstancesNoLock();
            var now = DateTimeOffset.UtcNow;
            ExpireStaleWorkersNoLock(now);
            var requestedWorkerId = NormalizeNullable(request.WorkerId);
            var instance = FindRegistrationSlotNoLock(request, requestedWorkerId, now);
            var assignedWorkerId = requestedWorkerId ?? CreateWorkerId();
            var replacingWorker = !string.Equals(instance.WorkerId, assignedWorkerId, StringComparison.OrdinalIgnoreCase);

            if (replacingWorker)
            {
                ReleaseActiveAssignmentNoLock(instance, "Queued");
                instance.RegisteredAtUtc = now;
                instance.LastForceStopCommandId = _state.Run.ForceStopCommandId;
                instance.AppliedGamePresentationSettingsVersion = 0;
                instance.GamePresentationSyncStatus = "Pending";
                instance.GamePresentationSyncError = "";
            }
            else
            {
                instance.RegisteredAtUtc ??= now;
            }

            instance.WorkerId = assignedWorkerId;
            instance.LastHeartbeatUtc = now;
            instance.Status = "Online";
            instance.GameDirectory = NormalizeNullable(request.GameDirectory);
            instance.GameLaunchStatus = "Worker online";
            instance.GameLaunchError = null;
            instance.PluginVersion = NormalizeNullable(request.PluginVersion);

            var workerName = NormalizeNullable(request.WorkerName);
            if (workerName != null)
            {
                instance.Name = workerName;
            }

            AddEventNoLock("Good", "Worker", instance.Name + " registered.", instanceIndex: instance.Index);
            SaveNoLock();
            return new WorkerRegisterResponse
            {
                WorkerId = assignedWorkerId,
                InstanceIndex = instance.Index,
                RecorderHostUrl = instance.RecorderHostUrl,
                OutputDirectory = instance.OutputDirectory,
                Settings = CloneSettings(_state.Settings)
            };
        }
    }

    public WorkerHeartbeatResponse Heartbeat(WorkerHeartbeatRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Worker heartbeat request is required.");
        }

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var instance = FindWorkerNoLock(request.WorkerId);
            instance.LastHeartbeatUtc = now;
            instance.Status = NormalizeStatus(request.Status, "Online");
            instance.CurrentReplayId = NormalizeNullable(request.CurrentReplayId);
            if (request.AppliedGamePresentationSettingsVersion > 0)
            {
                instance.AppliedGamePresentationSettingsVersion = request.AppliedGamePresentationSettingsVersion;
            }

            instance.GamePresentationSyncStatus =
                NormalizeNullable(request.GamePresentationSyncStatus) ?? instance.GamePresentationSyncStatus;
            instance.GamePresentationSyncError = NormalizeNullable(request.GamePresentationSyncError) ?? "";

            var replay = FindActiveReplayNoLock(instance);
            if (replay != null && IsRecordingStatus(instance.Status))
            {
                replay.Status = "Recording";
            }

            var shouldOpenPauseMenu = _state.Run.ForceStopCommandId > instance.LastForceStopCommandId;
            if (shouldOpenPauseMenu)
            {
                instance.LastForceStopCommandId = _state.Run.ForceStopCommandId;
            }

            SaveNoLock();
            return new WorkerHeartbeatResponse
            {
                ShouldCancelAssignment =
                    (_state.Run.CancellationRequested &&
                     (!string.IsNullOrWhiteSpace(instance.ActiveAssignmentId) ||
                      !string.IsNullOrWhiteSpace(request.CurrentReplayId))),
                CancellationReason = _state.Run.CancellationReason,
                ShouldOpenPauseMenu = shouldOpenPauseMenu,
                GamePresentationSettingsVersion = _state.Settings.GamePresentationSettingsVersion,
                GamePresentation = CloneGamePresentationSettings(_state.Settings.GamePresentation),
                Progress = CreateRunProgressNoLock()
            };
        }
    }

    public WorkerAssignmentResponse GetAssignment(string workerId)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var instance = FindWorkerNoLock(workerId);
            instance.LastHeartbeatUtc = now;

            if (!instance.Enabled)
            {
                ReleaseActiveAssignmentNoLock(instance, "Queued");
                instance.Status = "Online";
                RedistributeQueuedReplayPlansNoLock();
                SaveNoLock();
                return CreateEmptyAssignment(instance);
            }

            if (!_state.Run.IsRunning)
            {
                ReleaseActiveAssignmentNoLock(instance, "Queued");
                instance.Status = "Online";
                SaveNoLock();
                return CreateEmptyAssignment(instance);
            }

            var activeReplay = FindActiveReplayNoLock(instance);
            if (activeReplay != null)
            {
                instance.Status = "Assigned";
                SaveNoLock();
                return CreateAssignmentResponse(activeReplay, instance);
            }

            instance.ActiveAssignmentId = null;
            instance.CurrentReplayId = null;

            if (CountActiveRecordingsNoLock() >= _state.Settings.MaxConcurrentRecordings)
            {
                instance.Status = "Online";
                SaveNoLock();
                return CreateEmptyAssignment(instance);
            }

            var replay = FindNextReplayForInstanceNoLock(instance);
            if (replay == null)
            {
                instance.Status = "Online";
                TryCompleteRunNoLock(now);
                SaveNoLock();
                return CreateEmptyAssignment(instance);
            }

            var assignmentId = CreateAssignmentId();
            replay.AssignmentId = assignmentId;
            replay.AssignedAtUtc = now;
            replay.CompletedAtUtc = null;
            replay.AssignedInstance = instance.Index;
            replay.Status = "Assigned";
            replay.OutputPath = null;
            replay.Error = null;

            instance.ActiveAssignmentId = assignmentId;
            instance.CurrentReplayId = replay.Id;
            instance.Status = "Assigned";

            AddEventNoLock("Info", "Run", "Recording started: " + CreateReplayLabel(replay) + " on I-" + (instance.Index + 1), replay.Id, instance.Index);
            SaveNoLock();
            return CreateAssignmentResponse(replay, instance);
        }
    }

    public ControlPanelState ReportAssignment(WorkerReportRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Worker report request is required.");
        }

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var instance = FindWorkerNoLock(request.WorkerId);
            var replay = _state.Queue.FirstOrDefault(item =>
                string.Equals(item.AssignmentId, request.AssignmentId, StringComparison.OrdinalIgnoreCase));
            var status = NormalizeReplayStatus(request.Status);
            instance.LastHeartbeatUtc = now;

            if (replay == null)
            {
                throw new InvalidOperationException("Assignment was not found: " + request.AssignmentId);
            }

            var reportedAssignmentFailure = false;
            if (status == "Completed")
            {
                var outputPath = NormalizeNullable(request.OutputPath);
                var audioVerification = VerifyCompletedRecordingAudioNoLock(outputPath);
                var syncVerification = VerifyCompletedRecordingSyncNoLock(request);
                replay.Status = audioVerification.HasAudio && syncVerification.Verified ? "Completed" : "Failed";
                replay.OutputPath = outputPath;
                replay.Error = audioVerification.HasAudio
                    ? syncVerification.Error
                    : audioVerification.Error;
                replay.SyncStatus = NormalizeNullable(request.SyncStatus) ?? "";
                replay.SyncCorrectionMilliseconds = request.SyncCorrectionMilliseconds;
                replay.TrimStartSeconds = request.TrimStartSeconds;
                replay.SyncReportPath = NormalizeNullable(request.SyncReportPath) ?? "";
                replay.CompletedAtUtc = now;
                ClearWorkerAssignmentNoLock(instance);
                instance.Status = "Online";
                AddEventNoLock(
                    replay.Status == "Completed" ? "Good" : "Bad",
                    replay.Status == "Completed" ? "Output" : "Error",
                    replay.Status == "Completed"
                        ? "Recording complete: " + CreateReplayLabel(replay)
                        : "Recording failed: " + CreateReplayLabel(replay) + " - " + replay.Error,
                    replay.Id,
                    instance.Index);
            }
            else if (status == "Failed" || status == "Stopped")
            {
                reportedAssignmentFailure = status == "Failed";
                replay.Status = "Failed";
                replay.OutputPath = NormalizeNullable(request.OutputPath);
                replay.Error = NormalizeNullable(request.Error) ?? "Worker reported " + status.ToLowerInvariant() + ".";
                replay.SyncStatus = NormalizeNullable(request.SyncStatus) ?? "";
                replay.SyncCorrectionMilliseconds = request.SyncCorrectionMilliseconds;
                replay.TrimStartSeconds = request.TrimStartSeconds;
                replay.SyncReportPath = NormalizeNullable(request.SyncReportPath) ?? "";
                replay.CompletedAtUtc = now;
                ClearWorkerAssignmentNoLock(instance);
                instance.Status = "Online";
                AddEventNoLock(
                    "Bad",
                    status,
                    "Recording " + status.ToLowerInvariant() + ": " + CreateReplayLabel(replay) + " - " + replay.Error,
                    replay.Id,
                    instance.Index);
            }
            else
            {
                replay.Status = status;
                replay.OutputPath = NormalizeNullable(request.OutputPath) ?? replay.OutputPath;
                replay.Error = NormalizeNullable(request.Error);
                instance.Status = status;
                instance.CurrentReplayId = replay.Id;
            }

            RecalculateRunCountsNoLock();
            if (reportedAssignmentFailure)
            {
                RequestRunCancellationIfAllConcurrentInstancesFailedNoLock(now);
            }

            TryFinalizeCanceledRunNoLock(now);
            TryCompleteRunNoLock(now);
            SaveNoLock();
            return Clone(_state);
        }
    }

    private RecordingAudioVerificationResult VerifyCompletedRecordingAudioNoLock(string? outputPath)
    {
        if (!ShouldVerifyCompletedRecordingAudioNoLock())
        {
            return new RecordingAudioVerificationResult { HasAudio = true };
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new RecordingAudioVerificationResult
            {
                HasAudio = false,
                Error = "Required audio verification failed: completed recording did not include an output path."
            };
        }

        var result = _recordingAudioVerifier.Verify(outputPath);
        if (result.HasAudio)
        {
            return result;
        }

        var error = string.IsNullOrWhiteSpace(result.Error)
            ? "completed recording is missing an audio stream."
            : result.Error.Trim();
        result.Error = "Required audio verification failed: " + error;
        return result;
    }

    private RecordingSyncVerificationResult VerifyCompletedRecordingSyncNoLock(WorkerReportRequest request)
    {
        if (!string.Equals(_state.Settings.AudioMode, "ProcessLoopback", StringComparison.OrdinalIgnoreCase))
        {
            return new RecordingSyncVerificationResult { Verified = true };
        }

        if (!string.Equals(request.SyncStatus, "Corrected", StringComparison.OrdinalIgnoreCase))
        {
            return new RecordingSyncVerificationResult
            {
                Verified = false,
                Error = "Required sync verification failed: automatic sync marker was not corrected."
            };
        }

        if (!request.SyncCorrectionMilliseconds.HasValue || !request.TrimStartSeconds.HasValue)
        {
            return new RecordingSyncVerificationResult
            {
                Verified = false,
                Error = "Required sync verification failed: automatic sync metadata was missing."
            };
        }

        return new RecordingSyncVerificationResult { Verified = true };
    }

    private bool ShouldVerifyCompletedRecordingAudioNoLock()
    {
        return _state.Settings.RequireAudioForRun &&
               !string.Equals(_state.Settings.AudioMode, "None", StringComparison.OrdinalIgnoreCase);
    }

    private ControlPanelState LoadState(ControlPanelSettings settings)
    {
        if (!File.Exists(_statePath))
        {
            return new ControlPanelState { Settings = settings };
        }

        var json = File.ReadAllText(_statePath);
        var state = JsonSerializer.Deserialize<ControlPanelState>(json, JsonOptions.Default)
                    ?? new ControlPanelState();
        state.Settings ??= settings;
        state.Settings.Normalize();
        state.Queue ??= new List<ReplayQueueRecord>();
        state.Instances ??= new List<WorkerInstanceRecord>();
        state.InstanceProvision ??= new InstanceProvisionReport();
        state.InstanceBaseline ??= new InstanceBaselineReport();
        state.SongFolders ??= new SongFolderLinkReport();
        state.DiskSpace ??= new DiskSpaceReport();
        state.Events ??= new List<ControlPanelEventRecord>();
        state.Run ??= new RunState();
        foreach (var replay in state.Queue)
        {
            replay.Calibration ??= new ReplayCalibrationRecord();
        }

        return state;
    }

    private void ReloadQueueNoLock()
    {
        var loadResult = new ReplayQueue().Load(new ReplayQueueOptions
        {
            InputDirectory = _queueDirectory,
            SkipInvalidReplays = true
        });

        var existingByPath = _state.Queue.ToDictionary(
            item => Path.GetFullPath(item.Path),
            StringComparer.OrdinalIgnoreCase);
        var existingOrderById = _state.Queue
            .Select((item, index) => new { item.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var records = new List<ReplayQueueRecord>();
        foreach (var item in loadResult.Items)
        {
            var fullPath = Path.GetFullPath(item.ReplayPath);
            existingByPath.TryGetValue(fullPath, out var existing);
            var isMetadataEdited = existing?.IsMetadataEdited == true;
            records.Add(new ReplayQueueRecord
            {
                Id = existing?.Id ?? CreateStableId(fullPath),
                SequenceNumber = item.SequenceNumber,
                FileName = Path.GetFileName(item.ReplayPath),
                Path = item.ReplayPath,
                SongName = isMetadataEdited ? existing!.SongName : item.ReplayInfo.SongName,
                Mapper = isMetadataEdited ? existing!.Mapper : item.ReplayInfo.Mapper,
                PlayerName = ResolveReplayPlayerName(item.ReplayInfo.PlayerName, item.ReplayInfo.PlayerId, item.ReplayPath),
                Difficulty = isMetadataEdited ? existing!.Difficulty : item.ReplayInfo.Difficulty,
                LevelHash = item.ReplayInfo.LevelHash,
                CoverArtUrl = "/api/queue/" + Uri.EscapeDataString(existing?.Id ?? CreateStableId(fullPath)) + "/cover",
                MapStatus = existing?.MapStatus ?? "Unchecked",
                MapStatusDetail = existing?.MapStatusDetail ?? "",
                MapInstallPath = existing?.MapInstallPath ?? "",
                EstimatedSeconds = isMetadataEdited
                    ? existing!.EstimatedSeconds
                    : Math.Round(item.EstimatedPlaybackLength.TotalSeconds, 2),
                Status = existing?.Status ?? "Queued",
                AssignedInstance = existing?.AssignedInstance,
                AssignmentId = existing?.AssignmentId,
                AssignedAtUtc = existing?.AssignedAtUtc,
                CompletedAtUtc = existing?.CompletedAtUtc,
                OutputPath = existing?.OutputPath,
                Error = existing?.Error,
                SyncStatus = existing?.SyncStatus ?? "",
                SyncCorrectionMilliseconds = existing?.SyncCorrectionMilliseconds,
                TrimStartSeconds = existing?.TrimStartSeconds,
                SyncReportPath = existing?.SyncReportPath ?? "",
                Calibration = existing?.Calibration ?? new ReplayCalibrationRecord(),
                IsMetadataEdited = isMetadataEdited
            });
        }

        _state.Queue = records
            .OrderBy(item => existingOrderById.TryGetValue(item.Id, out var index) ? index : int.MaxValue)
            .ThenBy(item => item.SequenceNumber)
            .ToList();
        ResequenceQueueNoLock();
    }

    private bool RefreshQueueMetadataNoLock()
    {
        var changed = false;
        var reader = new BSAutoReplayRecorder.Core.Replay.BsorInfoReader();
        foreach (var replay in _state.Queue)
        {
            if (!File.Exists(replay.Path))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(replay.CoverArtUrl))
            {
                replay.CoverArtUrl = "/api/queue/" + Uri.EscapeDataString(replay.Id) + "/cover";
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(replay.LevelHash) &&
                !string.IsNullOrWhiteSpace(replay.PlayerName))
            {
                continue;
            }

            try
            {
                var info = reader.Read(replay.Path);
                replay.LevelHash = info.LevelHash;
                replay.PlayerName = ResolveReplayPlayerName(info.PlayerName, info.PlayerId, replay.Path);
                if (!replay.IsMetadataEdited)
                {
                    replay.SongName = info.SongName;
                    replay.Mapper = info.Mapper;
                    replay.Difficulty = info.Difficulty;
                    replay.EstimatedSeconds = Math.Round(info.EstimatedPlaybackLength.TotalSeconds, 2);
                }

                changed = true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (InvalidDataException)
            {
            }
        }

        return changed;
    }

    public string GetQueueCoverPath(string id)
    {
        lock (_sync)
        {
            var replay = FindQueueItemNoLock(id);
            return ResolveCoverArtPathNoLock(replay)
                   ?? throw new InvalidOperationException("No cover art was found for " + replay.FileName + ".");
        }
    }

    private bool RefreshQueueMapAvailabilityNoLock(bool allowDownload, IEnumerable<string>? replayIds = null)
    {
        var changed = false;
        var idFilter = replayIds == null
            ? null
            : new HashSet<string>(replayIds, StringComparer.OrdinalIgnoreCase);

        foreach (var replay in _state.Queue)
        {
            if (idFilter != null && !idFilter.Contains(replay.Id))
            {
                continue;
            }

            var existingDirectory = ResolveLevelDirectoryNoLock(replay);
            if (!string.IsNullOrWhiteSpace(existingDirectory))
            {
                changed |= SetQueueMapStatusNoLock(
                    replay,
                    "Found",
                    "Song folder is available.",
                    existingDirectory);
                continue;
            }

            if (!allowDownload)
            {
                if (string.Equals(replay.MapStatus, "Found", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(replay.MapStatus, "Downloaded", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= SetQueueMapStatusNoLock(
                        replay,
                        "Missing",
                        "Hey! There is no song folder here yet. Upload the WIP song zip or retry BeatSaver.",
                        "");
                }

                continue;
            }

            changed |= SetQueueMapStatusNoLock(
                replay,
                "Downloading",
                "Checking BeatSaver for the replay's level hash.",
                "");

            var targetRoot = ResolveAutomaticMapDownloadRootNoLock();
            BeatSaverMapDownloadResult result;
            try
            {
                result = _mapDownloader.DownloadByHash(replay.LevelHash, targetRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                changed |= SetQueueMapStatusNoLock(
                    replay,
                    "Missing",
                    "Hey! There is no song folder here yet. BeatSaver download failed: " + ex.Message,
                    "");
                continue;
            }

            if (result.Installed)
            {
                changed |= SetQueueMapStatusNoLock(
                    replay,
                    "Downloaded",
                    string.IsNullOrWhiteSpace(result.Detail) ? "Downloaded from BeatSaver." : result.Detail,
                    result.InstallPath);
                continue;
            }

            changed |= SetQueueMapStatusNoLock(
                replay,
                "Missing",
                "Hey! There is no song folder here yet. " +
                (string.IsNullOrWhiteSpace(result.Detail)
                    ? "Upload the WIP song zip or retry BeatSaver."
                    : result.Detail),
                "");
        }

        return changed;
    }

    private bool SetQueueMapStatusNoLock(
        ReplayQueueRecord replay,
        string status,
        string detail,
        string installPath)
    {
        var changed =
            !string.Equals(replay.MapStatus, status, StringComparison.Ordinal) ||
            !string.Equals(replay.MapStatusDetail, detail, StringComparison.Ordinal) ||
            !string.Equals(replay.MapInstallPath, installPath, StringComparison.Ordinal);
        if (!changed)
        {
            return false;
        }

        replay.MapStatus = status;
        replay.MapStatusDetail = detail;
        replay.MapInstallPath = installPath;
        return true;
    }

    private string? ResolveLevelDirectoryNoLock(ReplayQueueRecord replay)
    {
        var installedPath = NormalizeNullable(replay.MapInstallPath);
        if (installedPath != null && Directory.Exists(installedPath))
        {
            return installedPath;
        }

        return EnumerateLevelDirectoriesNoLock()
            .Select(directory => new
            {
                Directory = directory,
                Score = ScoreLevelDirectoryMatch(directory, replay)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Directory, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Directory)
            .FirstOrDefault();
    }

    private string? ResolveCoverArtPathNoLock(ReplayQueueRecord replay)
    {
        var primaryDirectory = NormalizeNullable(replay.MapInstallPath);
        if (primaryDirectory != null && Directory.Exists(primaryDirectory))
        {
            var cover = FindCoverInDirectory(primaryDirectory);
            if (!string.IsNullOrWhiteSpace(cover))
            {
                return cover;
            }
        }

        var candidates = EnumerateLevelDirectoriesNoLock()
            .Select(directory => new
            {
                Directory = directory,
                Score = ScoreLevelDirectoryMatch(directory, replay)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Directory, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var cover = FindCoverInDirectory(candidate.Directory);
            if (!string.IsNullOrWhiteSpace(cover))
            {
                return cover;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateLevelDirectoriesNoLock()
    {
        var roots = new[]
        {
            _state.Settings.SharedCustomLevelsDirectory,
            _state.Settings.SharedCustomWipLevelsDirectory
        }
        .Concat(_state.Instances.SelectMany(instance => new[]
        {
            Path.Combine(instance.LaunchDirectory, "Beat Saber_Data", "CustomLevels"),
            Path.Combine(instance.LaunchDirectory, "Beat Saber_Data", "CustomWIPLevels")
        }))
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                yield return directory;
            }
        }
    }

    private static int ScoreLevelDirectoryMatch(string directory, ReplayQueueRecord replay)
    {
        var folderName = NormalizeSearchText(Path.GetFileName(directory));
        var hash = NormalizeSearchText(replay.LevelHash);
        var songName = NormalizeSearchText(replay.SongName);
        var mapper = NormalizeSearchText(replay.Mapper);
        var score = 0;

        if (!string.IsNullOrWhiteSpace(hash) && folderName.Contains(hash, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(songName) && folderName.Contains(songName, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(mapper) && folderName.Contains(mapper, StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static string? FindCoverInDirectory(string directory)
    {
        var preferred = new[]
        {
            "cover.png",
            "cover.jpg",
            "cover.jpeg"
        };

        foreach (var name in preferred)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        try
        {
            return Directory.EnumerateFiles(directory)
                .Where(IsSupportedCoverImage)
                .OrderByDescending(path => Path.GetFileNameWithoutExtension(path).Contains("cover", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsSupportedCoverImage(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string ResolveReplayPlayerName(string? playerName, string? playerId, string? replayPath)
    {
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            return playerName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return playerId.Trim();
        }

        var fileName = Path.GetFileNameWithoutExtension(replayPath ?? "");
        var dashIndex = fileName.IndexOf('-');
        return dashIndex > 0 ? fileName.Substring(0, dashIndex) : "";
    }

    private void EnsureInstancesNoLock()
    {
        var now = DateTimeOffset.UtcNow;
        var count = Math.Max(1, _state.Settings.InstanceCount);
        var recordingRoot = Path.GetFullPath(_state.Settings.RecordingOutputDirectory);
        var instances = new List<WorkerInstanceRecord>(count);
        for (var index = 0; index < count; index++)
        {
            var outputDirectory = Path.Combine(recordingRoot, "Instance " + (index + 1));
            var instance = _state.Instances.FirstOrDefault(item => item.Index == index) ?? new WorkerInstanceRecord
            {
                Index = index,
                Name = CreateManagedInstanceName(index),
                RecorderHostUrl = "http://127.0.0.1:" + (5757 + index)
            };

            if (string.IsNullOrWhiteSpace(instance.WorkerId) || IsWorkerStale(instance, now))
            {
                ResetInactiveInstanceIdentityNoLock(instance);
            }

            if (string.IsNullOrWhiteSpace(instance.Name))
            {
                instance.Name = CreateManagedInstanceName(index);
            }

            if (string.IsNullOrWhiteSpace(instance.RecorderHostUrl))
            {
                instance.RecorderHostUrl = "http://127.0.0.1:" + (5757 + index);
            }

            instance.OutputDirectory = outputDirectory;
            instance.LaunchDirectory = CreateLaunchDirectory(index);
            instance.LaunchDirectoryReady = IsManagedInstanceReady(instance);
            instance.LaunchArguments = _state.Settings.BeatSaberLaunchArguments;
            instances.Add(instance);
        }

        if (instances.Count > 0 && !instances.Any(instance => instance.Enabled))
        {
            instances[0].Enabled = true;
        }

        _state.Instances = instances;
    }

    private List<WorkerInstanceRecord> GetConfiguredInstancesNoLock()
    {
        return _state.Instances
            .Where(instance => instance.Index < _state.Settings.InstanceCount)
            .OrderBy(instance => instance.Index)
            .ToList();
    }

    private List<WorkerInstanceRecord> GetEnabledConfiguredInstancesNoLock()
    {
        return GetConfiguredInstancesNoLock()
            .Where(instance => instance.Enabled)
            .ToList();
    }

    private List<WorkerInstanceRecord> GetCreatedManagedInstancesNoLock()
    {
        return GetConfiguredInstancesNoLock()
            .Where(IsManagedInstanceReady)
            .ToList();
    }

    private List<WorkerInstanceRecord> GetRunInstancesNoLock()
    {
        var enabledConfigured = GetEnabledConfiguredInstancesNoLock();
        var created = enabledConfigured
            .Where(IsManagedInstanceReady)
            .ToList();
        if (created.Count > 0)
        {
            return created;
        }

        return enabledConfigured;
    }

    private static bool IsManagedInstanceReady(WorkerInstanceRecord instance)
    {
        return !string.IsNullOrWhiteSpace(instance.LaunchDirectory) &&
               File.Exists(Path.Combine(instance.LaunchDirectory, BeatSaberExecutableName));
    }

    private bool SynchronizeMaxConcurrentRecordingsNoLock()
    {
        var enabledConfiguredCount = Math.Max(1, GetEnabledConfiguredInstancesNoLock().Count);
        if (_state.Settings.MaxConcurrentRecordings == enabledConfiguredCount)
        {
            return false;
        }

        _state.Settings.MaxConcurrentRecordings = enabledConfiguredCount;
        return true;
    }

    private bool RefreshInstanceProvisionCountsNoLock()
    {
        var changed = false;
        foreach (var instance in GetConfiguredInstancesNoLock())
        {
            var ready = IsManagedInstanceReady(instance);
            if (instance.LaunchDirectoryReady != ready)
            {
                instance.LaunchDirectoryReady = ready;
                changed = true;
            }
        }

        _state.InstanceProvision ??= new InstanceProvisionReport();
        var desiredCount = _state.Settings.InstanceCount;
        var createdCount = GetCreatedManagedInstancesNoLock().Count;
        var missingCount = Math.Max(0, desiredCount - createdCount);
        var countsChanged = false;

        if (_state.InstanceProvision.DesiredInstanceCount != desiredCount)
        {
            _state.InstanceProvision.DesiredInstanceCount = desiredCount;
            changed = true;
            countsChanged = true;
        }

        if (_state.InstanceProvision.CreatedInstanceCount != createdCount)
        {
            _state.InstanceProvision.CreatedInstanceCount = createdCount;
            changed = true;
            countsChanged = true;
        }

        if (_state.InstanceProvision.MissingInstanceCount != missingCount)
        {
            _state.InstanceProvision.MissingInstanceCount = missingCount;
            changed = true;
            countsChanged = true;
        }

        if (createdCount > 0 && missingCount > 0)
        {
            var summary = createdCount + "/" + desiredCount +
                          " managed instances are ready. Create missing instances to expand.";
            if (!string.Equals(_state.InstanceProvision.Status, "Missing", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_state.InstanceProvision.Summary, summary, StringComparison.Ordinal))
            {
                _state.InstanceProvision.Status = "Missing";
                _state.InstanceProvision.Summary = summary;
                changed = true;
            }
        }
        else if (createdCount > 0)
        {
            var summary = createdCount + "/" + desiredCount + " managed instance" +
                          (desiredCount == 1 ? " is" : "s are") + " ready.";
            var shouldRefreshSummary =
                countsChanged ||
                string.Equals(_state.InstanceProvision.Status, "Missing", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(_state.InstanceProvision.Summary) ||
                _state.InstanceProvision.Summary.StartsWith("All ", StringComparison.Ordinal) ||
                _state.InstanceProvision.Summary.Contains("/", StringComparison.Ordinal);
            if (shouldRefreshSummary &&
                (!string.Equals(_state.InstanceProvision.Status, "Ready", StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(_state.InstanceProvision.Summary, summary, StringComparison.Ordinal)))
            {
                _state.InstanceProvision.Status = "Ready";
                _state.InstanceProvision.Summary = summary;
                changed = true;
            }
        }

        return changed;
    }

    private bool RefreshDiskSpaceNoLock()
    {
        var previous = _state.DiskSpace ?? new DiskSpaceReport();
        var next = CreateDiskSpaceReportNoLock();
        var freeDelta = Math.Abs(previous.AvailableFreeBytes - next.AvailableFreeBytes);
        var changed =
            !string.Equals(previous.Status, next.Status, StringComparison.Ordinal) ||
            !string.Equals(previous.Path, next.Path, StringComparison.Ordinal) ||
            !string.Equals(previous.DriveName, next.DriveName, StringComparison.Ordinal) ||
            previous.TotalBytes != next.TotalBytes ||
            freeDelta >= 10L * 1024 * 1024 ||
            Math.Abs(previous.PercentFree - next.PercentFree) >= 0.1;
        if (changed)
        {
            _state.DiskSpace = next;
        }

        return changed;
    }

    private DiskSpaceReport CreateDiskSpaceReportNoLock()
    {
        var path = NormalizeNullable(_state.Settings.RecordingOutputDirectory) ??
                   Path.Combine(Path.GetFullPath(_state.Settings.WorkspaceDirectory), "Recordings");
        path = Path.GetFullPath(path);

        try
        {
            Directory.CreateDirectory(path);
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException("Could not resolve the recording drive.");
            }

            var drive = new DriveInfo(root);
            var total = Math.Max(0, drive.TotalSize);
            var free = Math.Max(0, drive.AvailableFreeSpace);
            var percent = total <= 0 ? 0 : free * 100d / total;
            var status = free < 20L * 1024 * 1024 * 1024 || percent < 10
                ? "Low"
                : free < 60L * 1024 * 1024 * 1024 || percent < 20
                    ? "Watch"
                    : "Ready";

            return new DiskSpaceReport
            {
                Status = status,
                Summary = FormatBytes(free) + " free on " + drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                CheckedAtUtc = DateTimeOffset.UtcNow,
                Path = path,
                DriveName = drive.Name,
                TotalBytes = total,
                AvailableFreeBytes = free,
                PercentFree = Math.Round(percent, 1)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new DiskSpaceReport
            {
                Status = "Unavailable",
                Summary = "Disk check failed: " + ex.Message,
                CheckedAtUtc = DateTimeOffset.UtcNow,
                Path = path
            };
        }
    }

    private void ValidateInstanceBaselineForRunNoLock()
    {
        if (!_state.Settings.RequireMatchingInstanceBaseline)
        {
            return;
        }

        _state.InstanceBaseline = new InstanceBaselineScanner().Scan(GetRunInstancesNoLock());
        if (string.Equals(_state.InstanceBaseline.Status, "Matched", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SaveNoLock();
        throw new InvalidOperationException(
            "Instance baseline is " + _state.InstanceBaseline.Status +
            ". Check Baseline and fix instance drift before starting.");
    }

    private SongFolderLinkReport ScanSongFolderLinksNoLock()
    {
        var links = new List<SongFolderLinkRecord>();
        var definitions = CreateSharedFolderDefinitionsNoLock().ToList();
        foreach (var instance in _state.Instances)
        {
            foreach (var definition in definitions)
            {
                links.Add(CreateSongFolderLinkRecord(instance, definition));
            }
        }

        var missing = links.Count(item => string.Equals(item.Status, "Missing", StringComparison.OrdinalIgnoreCase));
        var linked = links.Count(item => string.Equals(item.Status, "Linked", StringComparison.OrdinalIgnoreCase));
        var needsRepair = links.Count - linked;
        var status = links.Count == 0
            ? "Unchecked"
            : needsRepair == 0
                ? "Linked"
                : missing > 0
                    ? "Missing"
                    : "Repair needed";

        return new SongFolderLinkReport
        {
            Status = status,
            Summary = links.Count == 0
                ? "No shared folders are enabled."
                : needsRepair == 0
                    ? "All configured instances use the shared folders."
                    : needsRepair + "/" + links.Count + " shared folders need repair.",
            CheckedAtUtc = DateTimeOffset.UtcNow,
            SharedCustomLevelsDirectory = _state.Settings.SharedCustomLevelsDirectory,
            SharedCustomWipLevelsDirectory = _state.Settings.SharedCustomWipLevelsDirectory,
            Links = links
        };
    }

    private SongFolderLinkRecord CreateSongFolderLinkRecord(
        WorkerInstanceRecord instance,
        SharedFolderDefinition definition)
    {
        var instanceFolderPath = GetInstanceSongFolderPath(instance, definition.InstanceRelativePath);
        var sharedFullPath = Path.GetFullPath(definition.SharedFolderPath);
        var record = new SongFolderLinkRecord
        {
            InstanceIndex = instance.Index,
            InstanceName = instance.Name,
            FolderKind = definition.DisplayName,
            InstanceFolderPath = instanceFolderPath,
            SharedFolderPath = sharedFullPath
        };

        if (!Directory.Exists(instance.LaunchDirectory))
        {
            record.Status = "Missing";
            record.Detail = "Instance folder was not found.";
            return record;
        }

        if (!Directory.Exists(sharedFullPath))
        {
            record.Status = "Missing";
            record.Detail = "Shared folder has not been created yet.";
            return record;
        }

        if (!Directory.Exists(instanceFolderPath))
        {
            record.Status = "Missing";
            record.Detail = definition.DisplayName + " is missing from this instance.";
            return record;
        }

        if (!IsReparsePoint(instanceFolderPath))
        {
            record.Status = "Local folder";
            record.Detail = definition.DisplayName + " is a normal local folder.";
            return record;
        }

        var linkTarget = ResolveLinkTarget(instanceFolderPath);
        if (linkTarget == null)
        {
            record.Status = "Repair needed";
            record.Detail = "Link target could not be read.";
            return record;
        }

        if (PathsEqual(linkTarget, sharedFullPath))
        {
            record.Status = "Linked";
            record.Detail = "Linked to the shared folder.";
            return record;
        }

        record.Status = "Wrong target";
        record.Detail = "Currently points to " + linkTarget + ".";
        return record;
    }

    private void EnsureSongFolderLinkNoLock(
        WorkerInstanceRecord instance,
        SharedFolderDefinition definition,
        string timestamp)
    {
        if (!Directory.Exists(instance.LaunchDirectory))
        {
            throw new InvalidOperationException("Instance folder was not found: " + instance.LaunchDirectory);
        }

        var sharedFullPath = Path.GetFullPath(definition.SharedFolderPath);
        var instanceFolderPath = GetInstanceSongFolderPath(instance, definition.InstanceRelativePath);
        var parentPath = Path.GetDirectoryName(instanceFolderPath)
                         ?? throw new InvalidOperationException("Could not resolve parent folder for " + definition.DisplayName + ".");
        Directory.CreateDirectory(parentPath);

        if (Directory.Exists(instanceFolderPath))
        {
            var linkTarget = IsReparsePoint(instanceFolderPath)
                ? ResolveLinkTarget(instanceFolderPath)
                : null;
            if (linkTarget != null && PathsEqual(linkTarget, sharedFullPath))
            {
                return;
            }

            var backupPath = CreateBackupSongFolderPath(instanceFolderPath, timestamp);
            Directory.Move(instanceFolderPath, backupPath);
        }
        else if (File.Exists(instanceFolderPath))
        {
            var backupPath = CreateBackupSongFolderPath(instanceFolderPath, timestamp);
            File.Move(instanceFolderPath, backupPath);
        }

        CreateDirectoryJunction(instanceFolderPath, sharedFullPath);
    }

    private void SeedSharedFolderNoLock(SharedFolderDefinition definition)
    {
        var sharedFullPath = Path.GetFullPath(definition.SharedFolderPath);
        if (Directory.EnumerateFileSystemEntries(sharedFullPath).Any())
        {
            return;
        }

        var sourcePath = _state.Instances
            .Select(instance => GetInstanceSongFolderPath(instance, definition.InstanceRelativePath))
            .FirstOrDefault(path => Directory.Exists(path) && !IsReparsePoint(path) && Directory.EnumerateFileSystemEntries(path).Any());
        if (sourcePath == null)
        {
            return;
        }

        CopyDirectory(sourcePath, sharedFullPath);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDirectory);
            File.Copy(file, targetPath, overwrite: false);
        }
    }

    private IReadOnlyList<SharedFolderDefinition> CreateSharedFolderDefinitionsNoLock()
    {
        var definitions = new List<SharedFolderDefinition>
        {
            new SharedFolderDefinition(
                "CustomLevels",
                Path.Combine("Beat Saber_Data", "CustomLevels"),
                _state.Settings.SharedCustomLevelsDirectory),
            new SharedFolderDefinition(
                "CustomWIPLevels",
                Path.Combine("Beat Saber_Data", "CustomWIPLevels"),
                _state.Settings.SharedCustomWipLevelsDirectory)
        };

        AddOptionalSharedFolder(definitions, _state.Settings.ShareCustomSabers, "CustomSabers", "CustomSabers", _state.Settings.SharedCustomSabersDirectory);
        AddOptionalSharedFolder(definitions, _state.Settings.ShareCustomNotes, "CustomNotes", "CustomNotes", _state.Settings.SharedCustomNotesDirectory);
        AddOptionalSharedFolder(definitions, _state.Settings.ShareCustomPlatforms, "CustomPlatforms", "CustomPlatforms", _state.Settings.SharedCustomPlatformsDirectory);
        AddOptionalSharedFolder(definitions, _state.Settings.ShareCustomAvatars, "CustomAvatars", "CustomAvatars", _state.Settings.SharedCustomAvatarsDirectory);
        AddOptionalSharedFolder(definitions, _state.Settings.ShareCustomWalls, "CustomWalls", "CustomWalls", _state.Settings.SharedCustomWallsDirectory);
        AddOptionalSharedFolder(definitions, _state.Settings.ShareCustomBombs, "CustomBombs", "CustomBombs", _state.Settings.SharedCustomBombsDirectory);
        return definitions;
    }

    private static void AddOptionalSharedFolder(
        List<SharedFolderDefinition> definitions,
        bool enabled,
        string displayName,
        string instanceRelativePath,
        string sharedFolderPath)
    {
        if (!enabled)
        {
            return;
        }

        definitions.Add(new SharedFolderDefinition(displayName, instanceRelativePath, sharedFolderPath));
    }

    private static string GetInstanceSongFolderPath(WorkerInstanceRecord instance, string relativePath)
    {
        return Path.Combine(instance.LaunchDirectory, relativePath);
    }

    private static string CreateBackupSongFolderPath(string originalPath, string timestamp)
    {
        var directory = Path.GetDirectoryName(originalPath) ?? "";
        var name = Path.GetFileName(originalPath);
        var candidate = Path.Combine(directory, name + ".local-" + timestamp);
        for (var index = 2; Directory.Exists(candidate) || File.Exists(candidate); index++)
        {
            candidate = Path.Combine(directory, name + ".local-" + timestamp + "-" + index);
        }

        return candidate;
    }

    private static void CreateDirectoryJunction(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Windows did not start mklink.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Creating a song folder junction timed out.");
        }

        if (process.ExitCode != 0)
        {
            var detail = NormalizeNullable(error) ?? NormalizeNullable(output) ?? "exit code " + process.ExitCode;
            throw new InvalidOperationException("Could not create song folder junction: " + detail);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveLinkTarget(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            var target = NormalizeNullable(directory.LinkTarget);
            if (target == null)
            {
                return null;
            }

            return Path.GetFullPath(Path.IsPathRooted(target)
                ? target
                : Path.Combine(directory.Parent?.FullName ?? "", target));
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveProvisionSourceDirectory(string? sourceBeatSaberPath)
    {
        var sourceDirectory = NormalizeNullable(sourceBeatSaberPath);
        if (sourceDirectory == null)
        {
            throw new InvalidOperationException("Choose the Beat Saber folder to copy from first.");
        }

        sourceDirectory = Path.GetFullPath(sourceDirectory.Trim('"'));
        if (!Directory.Exists(sourceDirectory))
        {
            throw new InvalidOperationException("Source Beat Saber folder was not found: " + sourceDirectory);
        }

        if (!File.Exists(Path.Combine(sourceDirectory, BeatSaberExecutableName)))
        {
            throw new InvalidOperationException(BeatSaberExecutableName + " was not found in source folder: " + sourceDirectory);
        }

        return sourceDirectory;
    }

    private List<ManagedInstanceTarget> ResolveManagedInstanceTargetsNoLock(string targetRootDirectory)
    {
        var targets = _state.Instances
            .OrderBy(instance => instance.Index)
            .Select(instance => new ManagedInstanceTarget(
                instance.Index,
                string.IsNullOrWhiteSpace(instance.Name) ? "Instance " + (instance.Index + 1) : instance.Name,
                Path.GetFullPath(NormalizeNullable(instance.LaunchDirectory) ?? CreateLaunchDirectory(instance.Index))))
            .ToList();

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("No managed instances are configured.");
        }

        foreach (var target in targets)
        {
            if (!IsPathInsideDirectory(target.Directory, targetRootDirectory))
            {
                throw new InvalidOperationException(
                    "Managed instance path is outside the configured instance root: " + target.Directory);
            }
        }

        return targets;
    }

    private static void ValidateProvisionSourceAndTargets(
        string sourceDirectory,
        string targetRootDirectory,
        IReadOnlyList<ManagedInstanceTarget> targets,
        bool overwriteExisting)
    {
        if (PathsEqual(sourceDirectory, targetRootDirectory) || IsPathInsideDirectory(sourceDirectory, targetRootDirectory))
        {
            throw new InvalidOperationException("Source Beat Saber folder must be outside the managed instance root.");
        }

        foreach (var target in targets)
        {
            if (PathsEqual(sourceDirectory, target.Directory) ||
                IsPathInsideDirectory(target.Directory, sourceDirectory) ||
                IsPathInsideDirectory(sourceDirectory, target.Directory))
            {
                throw new InvalidOperationException("Source and target instance folders must not overlap: " + target.Directory);
            }

            if (Directory.Exists(target.Directory) &&
                Directory.EnumerateFileSystemEntries(target.Directory).Any() &&
                !overwriteExisting)
            {
                throw new InvalidOperationException(
                    target.Name + " already exists at " + target.Directory +
                    ". Enable overwrite or choose an empty managed instance root.");
            }
        }
    }

    private static void CopyManagedInstanceDirectory(
        string sourceDirectory,
        string targetDirectory,
        bool overwriteExisting,
        bool copyExistingSongs)
    {
        if (Directory.Exists(targetDirectory))
        {
            if (Directory.EnumerateFileSystemEntries(targetDirectory).Any() && !overwriteExisting)
            {
                throw new InvalidOperationException("Target instance folder is not empty: " + targetDirectory);
            }

            if (overwriteExisting)
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory) ?? targetDirectory);
        CopyDirectory(
            sourceDirectory,
            targetDirectory,
            overwrite: true,
            relativePath => IsProvisionTransientRelativePath(relativePath) ||
                            IsPreviousSongLibraryBackupRelativePath(relativePath) ||
                            (!copyExistingSongs && IsPreviousSongLibraryRelativePath(relativePath)));
    }

    private void DeleteManagedInstanceDirectoryNoLock(WorkerInstanceRecord instance)
    {
        var targetRootDirectory = Path.GetFullPath(_state.Settings.BeatSaberInstancesRoot);
        var launchDirectory = Path.GetFullPath(NormalizeNullable(instance.LaunchDirectory) ?? CreateLaunchDirectory(instance.Index));
        if (!IsPathInsideDirectory(launchDirectory, targetRootDirectory))
        {
            throw new InvalidOperationException("Managed instance path is outside the configured instance root: " + launchDirectory);
        }

        if (Directory.Exists(launchDirectory))
        {
            DeleteDirectoryWithoutFollowingReparsePoints(launchDirectory);
        }
    }

    private static void DeleteDirectoryWithoutFollowingReparsePoints(string directory)
    {
        var root = new DirectoryInfo(directory);
        if (!root.Exists)
        {
            return;
        }

        foreach (var entry in root.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                entry.Delete();
                continue;
            }

            if (entry is DirectoryInfo childDirectory)
            {
                DeleteDirectoryWithoutFollowingReparsePoints(childDirectory.FullName);
            }
            else
            {
                entry.Delete();
            }
        }

        root.Delete();
    }

    private static InstanceProvisionRecord CreateProvisionRecord(
        ManagedInstanceTarget target,
        string status,
        string detail)
    {
        return new InstanceProvisionRecord
        {
            Index = target.Index,
            Name = target.Name,
            Directory = target.Directory,
            Status = status,
            Detail = detail
        };
    }

    private string CreateLaunchDirectory(int index)
    {
        return Path.Combine(
            _state.Settings.BeatSaberInstancesRoot,
            _state.Settings.BeatSaberInstanceNamePrefix + (index + 1));
    }

    private string CreateManagedInstanceName(int index)
    {
        return _state.Settings.BeatSaberInstanceNamePrefix + (index + 1);
    }

    private void LaunchInstanceNoLock(WorkerInstanceRecord instance)
    {
        var now = DateTimeOffset.UtcNow;
        var runningProcessId = FindBeatSaberProcessIdForInstance(instance);
        if (runningProcessId.HasValue)
        {
            instance.GameProcessId = runningProcessId;
            instance.GameLaunchStatus = !string.IsNullOrWhiteSpace(instance.WorkerId) && !IsWorkerStale(instance, now)
                ? "Worker online"
                : "Already running";
            instance.GameLaunchError = null;
            instance.AudioRoutingStatus = "Already running";
            instance.AudioRoutingError = null;
            return;
        }

        var launchDirectory = NormalizeNullable(instance.LaunchDirectory) ?? CreateLaunchDirectory(instance.Index);
        var exePath = Path.Combine(launchDirectory, BeatSaberExecutableName);
        if (!Directory.Exists(launchDirectory))
        {
            SetLaunchFailureNoLock(instance, "Instance folder was not found: " + launchDirectory);
            return;
        }

        if (!File.Exists(exePath))
        {
            SetLaunchFailureNoLock(instance, BeatSaberExecutableName + " was not found in: " + launchDirectory);
            return;
        }

        object? restorePlaybackDevice = null;
        try
        {
            restorePlaybackDevice = PrepareAudioRoutingForLaunchNoLock(instance);
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = launchDirectory,
                UseShellExecute = false
            };

            ApplyRecordingDisplayScaleNoLock();
            ApplyBeatSaberWindowedRegistryState(_state.Settings.BeatSaberLaunchArguments);
            startInfo.Environment["SteamAppId"] = BeatSaberSteamAppId;
            startInfo.Environment["SteamOverlayGameId"] = BeatSaberSteamAppId;
            startInfo.Environment["SteamGameId"] = BeatSaberSteamAppId;

            foreach (var argument in SplitCommandLine(_state.Settings.BeatSaberLaunchArguments))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                SetLaunchFailureNoLock(instance, "Windows did not return a Beat Saber process.");
                return;
            }

            instance.GameProcessId = process.Id;
            instance.GameLaunchedAtUtc = now;
            instance.GameLaunchStatus = "Started";
            instance.GameLaunchError = null;
            WaitForAudioRoutingBindingNoLock(instance);
        }
        catch (Exception ex)
        {
            SetLaunchFailureNoLock(instance, ex.Message, ex.ToString());
        }
        finally
        {
            RestoreAudioRoutingAfterLaunchNoLock(instance, restorePlaybackDevice);
        }
    }

    private void SetLaunchFailureNoLock(WorkerInstanceRecord instance, string message, string? eventText = null)
    {
        instance.GameProcessId = null;
        instance.GameLaunchStatus = "Failed";
        instance.GameLaunchError = message;
        AddEventNoLock(
            "Bad",
            "Launch",
            instance.Name + ": " + (NormalizeNullable(eventText) ?? message),
            instanceIndex: instance.Index);
    }

    private bool QuitInstanceProcessNoLock(WorkerInstanceRecord instance)
    {
        using var process = FindBeatSaberProcessForInstance(instance);
        if (process == null)
        {
            if (instance.GameProcessId.HasValue || !string.IsNullOrWhiteSpace(instance.WorkerId))
            {
                MarkInstanceGameExitedNoLock(instance);
            }

            return false;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                {
                    throw new InvalidOperationException("Beat Saber did not exit within 5 seconds.");
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not quit " + instance.Name + ": " + ex.Message, ex);
        }

        MarkInstanceGameExitedNoLock(instance);
        return true;
    }

    private void MarkInstanceGameExitedNoLock(WorkerInstanceRecord instance)
    {
        ReleaseActiveAssignmentNoLock(instance, "Queued");
        instance.WorkerId = null;
        instance.GameProcessId = null;
        instance.GameLaunchStatus = "Exited";
        instance.GameLaunchError = null;
        instance.AudioRoutingStatus = "Stopped";
        instance.AudioRoutingError = null;
        ResetInactiveInstanceIdentityNoLock(instance);
        instance.CurrentReplayId = null;
        instance.ActiveAssignmentId = null;
        instance.Status = "Idle";
    }

    private static void ApplyBeatSaberWindowedRegistryState(string launchArguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Hyperbolic Magnetism\Beat Saber", writable: true);
        if (key == null)
        {
            return;
        }

        var (width, height) = GetLaunchResolution(launchArguments);

        // Unity can persist borderless fullscreen and apply it before command-line args win.
        SetDwordValue(key, "Screenmanager Fullscreen mode Default_h401710285", 3);
        SetDwordValue(key, "Screenmanager Fullscreen mode_h3630240806", 3);
        SetDwordValue(key, "Screenmanager Resolution Use Native Default_h1405981789", 0);
        SetDwordValue(key, "Screenmanager Resolution Use Native_h1405027254", 0);
        SetDwordValue(key, "Screenmanager Resolution Width Default_h680557497", width);
        SetDwordValue(key, "Screenmanager Resolution Height Default_h1380706816", height);
        SetDwordValue(key, "Screenmanager Resolution Width_h182942802", width);
        SetDwordValue(key, "Screenmanager Resolution Height_h2627697771", height);
        SetDwordValue(key, "Screenmanager Resolution Window Width_h2524650974", width);
        SetDwordValue(key, "Screenmanager Resolution Window Height_h1684712807", height);
    }

    private static (int Width, int Height) GetLaunchResolution(string launchArguments)
    {
        var width = 1920;
        var height = 1080;
        var arguments = SplitCommandLine(launchArguments).ToArray();
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], "-screen-width", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arguments[index + 1], out var parsedWidth) &&
                parsedWidth > 0)
            {
                width = parsedWidth;
            }
            else if (string.Equals(arguments[index], "-screen-height", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(arguments[index + 1], out var parsedHeight) &&
                     parsedHeight > 0)
            {
                height = parsedHeight;
            }
        }

        return (width, height);
    }

    [SupportedOSPlatform("windows")]
    private static void SetDwordValue(RegistryKey key, string name, int value)
    {
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private object? PrepareAudioRoutingForLaunchNoLock(WorkerInstanceRecord instance)
    {
        instance.AudioRoutingStatus = "Disabled";
        instance.AudioRoutingError = null;
        return null;
    }

    private void WaitForAudioRoutingBindingNoLock(WorkerInstanceRecord instance)
    {
    }

    private void RestoreAudioRoutingAfterLaunchNoLock(
        WorkerInstanceRecord instance,
        object? restorePlaybackDevice)
    {
    }

    private bool RefreshLaunchProcessesNoLock()
    {
        var changed = false;
        foreach (var instance in _state.Instances)
        {
            var runningProcessId = FindBeatSaberProcessIdForInstance(instance);
            if (runningProcessId.HasValue)
            {
                if (instance.GameProcessId != runningProcessId.Value)
                {
                    instance.GameProcessId = runningProcessId.Value;
                    changed = true;
                }

                continue;
            }

            if (!instance.GameProcessId.HasValue ||
                string.Equals(instance.GameLaunchStatus, "Failed", StringComparison.OrdinalIgnoreCase) ||
                IsKnownBeatSaberProcessRunning(instance.GameProcessId.Value))
            {
                continue;
            }

            instance.GameProcessId = null;
            if (string.Equals(instance.GameLaunchStatus, "Started", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(instance.GameLaunchStatus, "Already running", StringComparison.OrdinalIgnoreCase))
            {
                instance.GameLaunchStatus = "Exited";
            }

            changed = true;
        }

        return changed;
    }

    private static int? FindBeatSaberProcessIdForInstance(WorkerInstanceRecord instance)
    {
        using var process = FindBeatSaberProcessForInstance(instance);
        return process?.Id;
    }

    private static Process? FindBeatSaberProcessForInstance(WorkerInstanceRecord instance)
    {
        var executablePaths = CreateInstanceExecutablePathSet(instance);
        if (executablePaths.Count == 0)
        {
            return null;
        }

        foreach (var process in Process.GetProcessesByName(BeatSaberProcessName))
        {
            try
            {
                if (!process.HasExited)
                {
                    var processPath = process.MainModule?.FileName;
                    if (processPath != null && executablePaths.Contains(Path.GetFullPath(processPath)))
                    {
                        return process;
                    }
                }
            }
            catch
            {
                // MainModule can be unavailable for processes exiting during enumeration.
            }

            process.Dispose();
        }

        return null;
    }

    private static HashSet<string> CreateInstanceExecutablePathSet(WorkerInstanceRecord instance)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddExecutablePath(paths, instance.LaunchDirectory);
        AddExecutablePath(paths, instance.GameDirectory);
        return paths;
    }

    private static void AddExecutablePath(HashSet<string> paths, string? directory)
    {
        var normalizedDirectory = NormalizeNullable(directory);
        if (normalizedDirectory == null)
        {
            return;
        }

        try
        {
            paths.Add(Path.GetFullPath(Path.Combine(normalizedDirectory, BeatSaberExecutableName)));
        }
        catch
        {
        }
    }

    private int CountReadyWorkersNoLock(DateTimeOffset now)
    {
        return _state.Instances.Count(instance =>
            !string.IsNullOrWhiteSpace(instance.WorkerId) &&
            !IsWorkerStale(instance, now));
    }

    private int CountActiveRecordingsNoLock()
    {
        return _state.Queue.Count(replay => IsActiveReplayStatus(replay.Status));
    }

    private bool ExpireStaleWorkersNoLock(DateTimeOffset now)
    {
        var changed = false;
        foreach (var instance in _state.Instances)
        {
            if (string.IsNullOrWhiteSpace(instance.WorkerId) || !IsWorkerStale(instance, now))
            {
                continue;
            }

            ReleaseActiveAssignmentNoLock(instance, "Queued");
            instance.WorkerId = null;
            ResetInactiveInstanceIdentityNoLock(instance);
            instance.CurrentReplayId = null;
            instance.ActiveAssignmentId = null;
            instance.Status = "Idle";
            changed = true;
        }

        return changed;
    }

    private WorkerInstanceRecord FindRegistrationSlotNoLock(
        WorkerRegisterRequest request,
        string? requestedWorkerId,
        DateTimeOffset now)
    {
        if (requestedWorkerId != null)
        {
            var existingWorker = _state.Instances.FirstOrDefault(instance =>
                string.Equals(instance.WorkerId, requestedWorkerId, StringComparison.OrdinalIgnoreCase));
            if (existingWorker != null)
            {
                return existingWorker;
            }
        }

        if (request.PreferredInstanceIndex.HasValue)
        {
            var preferred = _state.Instances.FirstOrDefault(instance => instance.Index == request.PreferredInstanceIndex.Value);
            if (preferred == null)
            {
                throw new InvalidOperationException("Preferred instance index is outside the configured instance count.");
            }

            if (CanClaimWorkerSlot(preferred, requestedWorkerId, now))
            {
                return preferred;
            }
        }

        var emptySlot = _state.Instances.FirstOrDefault(instance => string.IsNullOrWhiteSpace(instance.WorkerId));
        if (emptySlot != null)
        {
            return emptySlot;
        }

        var staleSlot = _state.Instances.FirstOrDefault(instance => IsWorkerStale(instance, now));
        if (staleSlot != null)
        {
            return staleSlot;
        }

        throw new InvalidOperationException("No worker slots are available. Increase the instance count or stop an existing worker.");
    }

    private WorkerInstanceRecord FindWorkerNoLock(string workerId)
    {
        var normalizedWorkerId = NormalizeNullable(workerId);
        if (normalizedWorkerId == null)
        {
            throw new InvalidOperationException("Worker id is required.");
        }

        var instance = _state.Instances.FirstOrDefault(item =>
            string.Equals(item.WorkerId, normalizedWorkerId, StringComparison.OrdinalIgnoreCase));
        if (instance == null)
        {
            throw new InvalidOperationException("Worker is not registered: " + normalizedWorkerId);
        }

        return instance;
    }

    private ReplayQueueRecord FindQueueItemNoLock(string id)
    {
        return _state.Queue[FindQueueItemIndexNoLock(id)];
    }

    private int FindQueueItemIndexNoLock(string id)
    {
        var normalizedId = NormalizeNullable(id);
        if (normalizedId == null)
        {
            throw new InvalidOperationException("Queue item id is required.");
        }

        var index = _state.Queue.FindIndex(item =>
            string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException("Queue item was not found: " + normalizedId);
        }

        return index;
    }

    private static void EnsureQueueItemCanChangeNoLock(ReplayQueueRecord replay, string action)
    {
        if (IsActiveReplayStatus(replay.Status))
        {
            throw new InvalidOperationException(
                "Cannot " + action + " a replay while a worker is using it: " + replay.FileName);
        }
    }

    private ReplayQueueRecord? FindActiveReplayNoLock(WorkerInstanceRecord instance)
    {
        if (string.IsNullOrWhiteSpace(instance.ActiveAssignmentId))
        {
            return null;
        }

        return _state.Queue.FirstOrDefault(item =>
            string.Equals(item.AssignmentId, instance.ActiveAssignmentId, StringComparison.OrdinalIgnoreCase));
    }

    private void ResetAssignmentsNoLock()
    {
        foreach (var replay in _state.Queue)
        {
            replay.AssignedInstance = null;
            replay.AssignmentId = null;
            replay.AssignedAtUtc = null;
            replay.CompletedAtUtc = null;
            replay.OutputPath = null;
            replay.Error = null;
        }

        foreach (var instance in _state.Instances)
        {
            instance.ActiveAssignmentId = null;
            instance.CurrentReplayId = null;
            instance.Status = "Idle";
        }
    }

    private void ReleaseActiveAssignmentNoLock(WorkerInstanceRecord instance, string replayStatus)
    {
        var replay = FindActiveReplayNoLock(instance);
        if (replay != null && IsPendingReplayStatus(replay.Status))
        {
            replay.Status = replayStatus;
            replay.AssignedInstance = null;
            replay.AssignmentId = null;
            replay.AssignedAtUtc = null;
        }

        ClearWorkerAssignmentNoLock(instance);
    }

    private static void ClearWorkerAssignmentNoLock(WorkerInstanceRecord instance)
    {
        instance.ActiveAssignmentId = null;
        instance.CurrentReplayId = null;
    }

    private void ResequenceQueueNoLock()
    {
        for (var index = 0; index < _state.Queue.Count; index++)
        {
            _state.Queue[index].SequenceNumber = index + 1;
        }
    }

    private bool RedistributeQueuedReplayPlansNoLock()
    {
        var laneIndexes = GetRunInstancesNoLock()
            .Select(instance => instance.Index)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        if (laneIndexes.Length == 0)
        {
            laneIndexes = Enumerable.Range(0, Math.Max(1, _state.Settings.InstanceCount)).ToArray();
        }

        var changed = false;
        var queued = _state.Queue
            .Where(replay => string.Equals(replay.Status, "Queued", StringComparison.OrdinalIgnoreCase))
            .OrderBy(replay => replay.SequenceNumber)
            .ToList();
        for (var index = 0; index < queued.Count; index++)
        {
            var replay = queued[index];
            var plannedInstance = laneIndexes[index % laneIndexes.Length];
            if (replay.AssignedInstance != plannedInstance)
            {
                replay.AssignedInstance = plannedInstance;
                changed = true;
            }

            if (replay.AssignmentId != null)
            {
                replay.AssignmentId = null;
                changed = true;
            }

            if (replay.AssignedAtUtc != null)
            {
                replay.AssignedAtUtc = null;
                changed = true;
            }
        }

        return changed;
    }

    private ReplayQueueRecord? FindNextReplayForInstanceNoLock(WorkerInstanceRecord instance)
    {
        var queued = _state.Queue
            .Where(item => string.Equals(item.Status, "Queued", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SequenceNumber)
            .ToList();
        return queued.FirstOrDefault(item => item.AssignedInstance == instance.Index) ??
               queued.FirstOrDefault(item => item.AssignedInstance == null) ??
               queued.FirstOrDefault();
    }

    private void RecalculateRunCountsNoLock()
    {
        _state.Run.CompletedCount = _state.Queue.Count(replay =>
            string.Equals(replay.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        _state.Run.FailedCount = _state.Queue.Count(replay =>
            string.Equals(replay.Status, "Failed", StringComparison.OrdinalIgnoreCase));
    }

    private bool NormalizeRunSummaryNoLock()
    {
        var completedCount = _state.Run.CompletedCount;
        var failedCount = _state.Run.FailedCount;
        var status = _state.Run.Status;

        RecalculateRunCountsNoLock();

        if (!_state.Run.IsRunning && !_state.Run.CancellationRequested)
        {
            if (_state.Run.FailedCount == 0)
            {
                if (string.Equals(_state.Run.Status, "Stopped with errors", StringComparison.OrdinalIgnoreCase))
                {
                    _state.Run.Status = "Stopped";
                }
                else if (string.Equals(_state.Run.Status, "Finished with errors", StringComparison.OrdinalIgnoreCase))
                {
                    _state.Run.Status = "Complete";
                }
            }
            else if (string.Equals(_state.Run.Status, "Stopped", StringComparison.OrdinalIgnoreCase))
            {
                _state.Run.Status = "Stopped with errors";
            }
            else if (string.Equals(_state.Run.Status, "Complete", StringComparison.OrdinalIgnoreCase))
            {
                _state.Run.Status = "Finished with errors";
            }
        }

        return completedCount != _state.Run.CompletedCount ||
               failedCount != _state.Run.FailedCount ||
               !string.Equals(status, _state.Run.Status, StringComparison.Ordinal);
    }

    private void RequestRunCancellationNoLock(string reason, bool failQueued)
    {
        _state.Run.IsRunning = false;
        _state.Run.CancellationRequested = true;
        _state.Run.CancellationReason = reason;
        _state.Run.Status = "Stopping";

        if (failQueued)
        {
            foreach (var replay in _state.Queue.Where(replay =>
                         string.Equals(replay.Status, "Queued", StringComparison.OrdinalIgnoreCase)))
            {
                replay.Status = "Failed";
                replay.CompletedAtUtc = DateTimeOffset.UtcNow;
                replay.Error = reason;
            }
        }

        RecalculateRunCountsNoLock();
    }

    private void TryFinalizeCanceledRunNoLock(DateTimeOffset now)
    {
        if (!_state.Run.CancellationRequested)
        {
            return;
        }

        if (_state.Instances.Any(instance => !string.IsNullOrWhiteSpace(instance.ActiveAssignmentId)))
        {
            return;
        }

        _state.Run.FinishedAtUtc = now;
        _state.Run.Status = _state.Run.FailedCount > 0 ? "Stopped with errors" : "Stopped";
        _state.Run.CancellationRequested = false;

        foreach (var instance in _state.Instances)
        {
            instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
        }

        RestoreDisplayScaleNoLock();
        RestoreTaskbarVisibilityNoLock();
    }

    private void TryCompleteRunNoLock(DateTimeOffset now)
    {
        if (!_state.Run.IsRunning || _state.Run.CancellationRequested)
        {
            return;
        }

        var hasPendingWork = _state.Queue.Any(replay => IsPendingReplayStatus(replay.Status));
        if (hasPendingWork)
        {
            return;
        }

        _state.Run.IsRunning = false;
        _state.Run.FinishedAtUtc = now;
        _state.Run.Status = _state.Run.FailedCount > 0 ? "Finished with errors" : "Complete";

        foreach (var instance in _state.Instances.Where(instance => string.IsNullOrWhiteSpace(instance.ActiveAssignmentId)))
        {
            instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
        }

        RestoreDisplayScaleNoLock();
        RestoreTaskbarVisibilityNoLock();
    }

    private void RequestRunCancellationIfAllConcurrentInstancesFailedNoLock(DateTimeOffset now)
    {
        if (!_state.Run.IsRunning || _state.Run.CancellationRequested)
        {
            return;
        }

        if (_state.Run.CompletedCount > 0 || CountActiveRecordingsNoLock() > 0)
        {
            return;
        }

        if (!_state.Queue.Any(replay => string.Equals(replay.Status, "Queued", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var readyWorkers = GetRunInstancesNoLock().Count(instance =>
            !string.IsNullOrWhiteSpace(instance.WorkerId) &&
            !IsWorkerStale(instance, now));
        var requiredFailedInstances = Math.Min(
            Math.Min(_state.Settings.MaxConcurrentRecordings, readyWorkers),
            _state.Queue.Count);
        if (requiredFailedInstances <= 0)
        {
            return;
        }

        var failedInstanceCount = _state.Queue
            .Where(replay =>
                string.Equals(replay.Status, "Failed", StringComparison.OrdinalIgnoreCase) &&
                replay.AssignedInstance != null &&
                replay.CompletedAtUtc != null &&
                (_state.Run.StartedAtUtc == null || replay.CompletedAtUtc >= _state.Run.StartedAtUtc))
            .Select(replay => replay.AssignedInstance!.Value)
            .Distinct()
            .Count();
        if (failedInstanceCount < requiredFailedInstances)
        {
            return;
        }

        RequestRunCancellationNoLock(
            "All active instances failed; stopping remaining queued replays.",
            failQueued: true);
    }

    private void ApplyTaskbarVisibilityForRunNoLock()
    {
        if (_state.Settings.HideTaskbarDuringRun)
        {
            TaskbarVisibilityController.Hide();
            _taskbarHiddenForRun = true;
        }
    }

    private void RestoreTaskbarVisibilityNoLock()
    {
        if (_taskbarHiddenForRun)
        {
            TaskbarVisibilityController.Restore();
            _taskbarHiddenForRun = false;
        }
    }

    private void ValidateRecorderHostsForRunNoLock()
    {
        var unhealthy = GetRunInstancesNoLock()
            .Where(instance => !_recorderHostHealthChecker.IsHealthy(instance.RecorderHostUrl))
            .Select(instance => (instance.Index + 1) + " (" + instance.RecorderHostUrl + ")")
            .ToList();
        if (unhealthy.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "These recorder hosts are not ready: " + string.Join(", ", unhealthy) +
            ". Start the recorder stack again before starting the queue.");
    }

    private void ApplyRecordingDisplayScaleNoLock()
    {
        CaptureRestoreDisplayScaleNoLock();
        ApplyDisplayScaleNoLock(_state.Settings.RecordingDisplayScalePercent, throwOnFailure: true);
    }

    private void RestoreDisplayScaleNoLock()
    {
        try
        {
            var restoreScalePercent =
                _detectedRestoreDisplayScalePercent ?? _state.Settings.RestoreDisplayScalePercent;
            ApplyDisplayScaleNoLock(restoreScalePercent, throwOnFailure: false);
        }
        finally
        {
            _detectedRestoreDisplayScalePercent = null;
        }
    }

    private void CaptureRestoreDisplayScaleNoLock()
    {
        if (!_state.Settings.ManageDisplayScale || _detectedRestoreDisplayScalePercent.HasValue)
        {
            return;
        }

        var scalePercent = ReadDisplayScaleNoLock(throwOnFailure: true);
        if (!scalePercent.HasValue)
        {
            return;
        }

        _detectedRestoreDisplayScalePercent = scalePercent.Value;
        _state.Settings.RestoreDisplayScalePercent = scalePercent.Value;
    }

    private int? ReadDisplayScaleNoLock(bool throwOnFailure)
    {
        if (!_state.Settings.ManageDisplayScale)
        {
            return null;
        }

        var toolPath = ResolveSetDpiToolPath();
        if (toolPath == null)
        {
            DisableDisplayScaleNoLock(CreateMissingSetDpiMessage());
            return null;
        }

        var monitorNumber = _state.Settings.MonitorIndex + 1;
        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("value");
        startInfo.ArgumentList.Add(monitorNumber.ToString(CultureInfo.InvariantCulture));

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Windows did not start the display scaling helper.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("Display scaling helper timed out.");
            }

            if (process.ExitCode != 0 ||
                output.Contains("Invalid Monitor", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("Invalid Monitor", StringComparison.OrdinalIgnoreCase))
            {
                var detail = NormalizeNullable(error) ?? NormalizeNullable(output) ?? "exit code " + process.ExitCode;
                throw new InvalidOperationException("Display scaling helper failed: " + detail);
            }

            var text = NormalizeNullable(output);
            if (text != null &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scalePercent) &&
                scalePercent is >= 100 and <= 500)
            {
                return scalePercent;
            }

            throw new InvalidOperationException(
                "Display scaling helper returned an invalid scale: " + (text ?? "empty output"));
        }
        catch when (!throwOnFailure)
        {
            return null;
        }
    }

    private void ApplyDisplayScaleNoLock(int scalePercent, bool throwOnFailure)
    {
        if (!_state.Settings.ManageDisplayScale)
        {
            return;
        }

        var toolPath = ResolveSetDpiToolPath();
        if (toolPath == null)
        {
            DisableDisplayScaleNoLock(CreateMissingSetDpiMessage());
            return;
        }

        var monitorNumber = _state.Settings.MonitorIndex + 1;
        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(scalePercent.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(monitorNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Windows did not start the display scaling helper.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("Display scaling helper timed out.");
            }

            if (process.ExitCode != 0 ||
                output.Contains("Invalid Monitor", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("Invalid Monitor", StringComparison.OrdinalIgnoreCase))
            {
                var detail = NormalizeNullable(error) ?? NormalizeNullable(output) ?? "exit code " + process.ExitCode;
                throw new InvalidOperationException("Display scaling helper failed: " + detail);
            }
        }
        catch when (!throwOnFailure)
        {
        }
    }

    private static string? ResolveSetDpiToolPath()
    {
        var configuredPath = NormalizeNullable(Environment.GetEnvironmentVariable("BSARR_SETDPI_PATH"));
        if (configuredPath != null && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "SetDpi", "SetDpi.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "SetDpi", "SetDpi.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private void DisableDisplayScaleNoLock(string reason)
    {
        if (!_state.Settings.ManageDisplayScale)
        {
            return;
        }

        _state.Settings.ManageDisplayScale = false;
        AddEventNoLock("Warn", "Display", "Display scaling disabled: " + reason);
    }

    private static string CreateMissingSetDpiMessage()
    {
        var configuredPath = NormalizeNullable(Environment.GetEnvironmentVariable("BSARR_SETDPI_PATH"));
        if (configuredPath != null)
        {
            return "BSARR_SETDPI_PATH points to a missing file: " + configuredPath;
        }

        return "Display scaling helper was not found. Expected tools\\SetDpi\\SetDpi.exe or BSARR_SETDPI_PATH.";
    }

    private void ResetRunNoLock(string status)
    {
        _state.Run = new RunState
        {
            Status = status
        };
    }

    private WorkerAssignmentResponse CreateAssignmentResponse(ReplayQueueRecord replay, WorkerInstanceRecord instance)
    {
        Directory.CreateDirectory(instance.OutputDirectory);
        return new WorkerAssignmentResponse
        {
            HasAssignment = true,
            AssignmentId = replay.AssignmentId,
            ReplayId = replay.Id,
            ReplayPath = Path.GetFullPath(replay.Path),
            AssignmentKind = "Replay",
            OutputBaseName = CreateOutputBaseName(replay),
            RecorderHostUrl = instance.RecorderHostUrl,
            OutputDirectory = instance.OutputDirectory,
            InstanceIndex = instance.Index,
            TargetProcessId = instance.GameProcessId,
            TargetFps = _state.Settings.TargetFps,
            CaptureWidth = _state.Settings.CaptureWidth,
            CaptureHeight = _state.Settings.CaptureHeight,
            Encoder = _state.Settings.Encoder,
            VideoBitrateKbps = _state.Settings.VideoBitrateKbps,
            OutputFormat = _state.Settings.OutputFormat,
            MonitorIndex = _state.Settings.MonitorIndex,
            QualityMode = _state.Settings.QualityMode,
            AudioMode = _state.Settings.AudioMode,
            AudioDeviceName = GetAudioCaptureDeviceName(instance.Index),
            AudioBitrateKbps = _state.Settings.AudioBitrateKbps,
            AudioSampleRate = _state.Settings.AudioSampleRate,
            AudioChannels = _state.Settings.AudioChannels,
            AudioLevelMode = _state.Settings.AudioLevelMode,
            AudioTargetLevelDb = _state.Settings.AudioTargetLevelDb,
            DelayBetweenRecordingsSeconds = _state.Settings.DelayBetweenRecordingsSeconds,
            GamePresentationSettingsVersion = _state.Settings.GamePresentationSettingsVersion,
            GamePresentation = CloneGamePresentationSettings(_state.Settings.GamePresentation),
            Progress = CreateRunProgressNoLock()
        };
    }

    private WorkerAssignmentResponse CreateEmptyAssignment(WorkerInstanceRecord? instance = null)
    {
        return new WorkerAssignmentResponse
        {
            HasAssignment = false,
            InstanceIndex = instance?.Index,
            TargetProcessId = null,
            TargetFps = _state.Settings.TargetFps,
            CaptureWidth = _state.Settings.CaptureWidth,
            CaptureHeight = _state.Settings.CaptureHeight,
            Encoder = _state.Settings.Encoder,
            VideoBitrateKbps = _state.Settings.VideoBitrateKbps,
            OutputFormat = _state.Settings.OutputFormat,
            MonitorIndex = _state.Settings.MonitorIndex,
            QualityMode = _state.Settings.QualityMode,
            AudioMode = _state.Settings.AudioMode,
            AudioDeviceName = "",
            AudioBitrateKbps = _state.Settings.AudioBitrateKbps,
            AudioSampleRate = _state.Settings.AudioSampleRate,
            AudioChannels = _state.Settings.AudioChannels,
            AudioLevelMode = _state.Settings.AudioLevelMode,
            AudioTargetLevelDb = _state.Settings.AudioTargetLevelDb,
            DelayBetweenRecordingsSeconds = _state.Settings.DelayBetweenRecordingsSeconds,
            GamePresentationSettingsVersion = _state.Settings.GamePresentationSettingsVersion,
            GamePresentation = CloneGamePresentationSettings(_state.Settings.GamePresentation),
            Progress = CreateRunProgressNoLock()
        };
    }

    private WorkerRunProgress CreateRunProgressNoLock()
    {
        return new WorkerRunProgress
        {
            TotalCount = _state.Queue.Count,
            CompletedCount = _state.Run.CompletedCount,
            FailedCount = _state.Run.FailedCount,
            IsRunning = _state.Run.IsRunning,
            Status = string.IsNullOrWhiteSpace(_state.Run.Status) ? "Idle" : _state.Run.Status
        };
    }

    private void ValidateAudioSettingsForRunNoLock()
    {
        if (string.Equals(_state.Settings.AudioMode, "ProcessLoopback", StringComparison.OrdinalIgnoreCase))
        {
            RefreshLaunchProcessesNoLock();
            var missingProcessIndexes = GetRunInstancesNoLock()
                .Where(instance => !instance.GameProcessId.HasValue)
                .Select(instance => instance.Index + 1)
                .ToList();
            if (missingProcessIndexes.Count > 0)
            {
                throw new InvalidOperationException(
                    "Audio mode is ProcessLoopback, but these instances do not have known Beat Saber process IDs: " +
                    string.Join(", ", missingProcessIndexes) +
                    ". Use Launch Games or wait for the workers to reconnect before starting.");
            }

            return;
        }

        if (_state.Settings.RequireAudioForRun)
        {
            throw new InvalidOperationException(
                "Audio is required before start, but Audio mode is None. Use ProcessLoopback or turn off Require audio.");
        }

        return;
    }

    private void ValidateQueueMapsForRunNoLock()
    {
        RefreshQueueMapAvailabilityNoLock(allowDownload: false);
        var missing = _state.Queue
            .Where(replay =>
                !string.Equals(replay.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(replay.Status, "Failed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(replay.MapStatus, "Found", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(replay.MapStatus, "Downloaded", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var labels = missing
            .Take(5)
            .Select(replay => string.IsNullOrWhiteSpace(replay.SongName) ? replay.FileName : replay.SongName)
            .ToList();
        var suffix = missing.Count > labels.Count ? " and " + (missing.Count - labels.Count) + " more" : "";
        throw new InvalidOperationException(
            "Some queued replays are missing their song folder. Upload the WIP song zip or retry BeatSaver for: " +
            string.Join(", ", labels) + suffix + ".");
    }

    private string GetAudioCaptureDeviceName(int instanceIndex)
    {
        return "";
    }

    private string CreateImportPath(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var targetPath = Path.Combine(_queueDirectory, safeFileName);
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var extension = Path.GetExtension(safeFileName);
        for (var index = 2; index < 10_000; index++)
        {
            targetPath = Path.Combine(_queueDirectory, baseName + " (" + index + ")" + extension);
            if (!File.Exists(targetPath))
            {
                return targetPath;
            }
        }

        throw new InvalidOperationException("Could not create an import filename for " + safeFileName + ".");
    }

    private string ResolveAutomaticMapDownloadRootNoLock()
    {
        var root = NormalizeNullable(_state.Settings.SharedCustomLevelsDirectory);
        if (root != null)
        {
            return Path.GetFullPath(root);
        }

        return Path.Combine(_queueDirectory, "..", "SharedSongs", "CustomLevels");
    }

    private string ResolveManualMapUploadRootNoLock()
    {
        var root = NormalizeNullable(_state.Settings.SharedCustomWipLevelsDirectory)
                   ?? NormalizeNullable(_state.Settings.SharedCustomLevelsDirectory);
        if (root != null)
        {
            return Path.GetFullPath(root);
        }

        return Path.Combine(_queueDirectory, "..", "SharedSongs", "CustomWIPLevels");
    }

    private static string CreateMapFolderName(ReplayQueueRecord replay, string uploadFileName)
    {
        if (!string.IsNullOrWhiteSpace(replay.LevelHash) && !string.IsNullOrWhiteSpace(replay.SongName))
        {
            return replay.LevelHash + " " + replay.SongName;
        }

        if (!string.IsNullOrWhiteSpace(replay.SongName))
        {
            return replay.SongName;
        }

        return Path.GetFileNameWithoutExtension(uploadFileName);
    }

    private static string CreateUniqueMapDirectory(string targetRoot, string folderName)
    {
        var safeName = FileNameSanitizer.SanitizeBaseName(folderName);
        var targetPath = Path.Combine(targetRoot, safeName);
        if (!Directory.Exists(targetPath))
        {
            return targetPath;
        }

        for (var index = 2; index < 10_000; index++)
        {
            targetPath = Path.Combine(targetRoot, safeName + " (" + index.ToString(CultureInfo.InvariantCulture) + ")");
            if (!Directory.Exists(targetPath))
            {
                return targetPath;
            }
        }

        throw new InvalidOperationException("Could not create a song folder for " + safeName + ".");
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory, bool overwrite)
    {
        CopyDirectory(sourceDirectory, targetDirectory, overwrite, _ => false);
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        bool overwrite,
        Func<string, bool> shouldSkipRelativePath)
    {
        if (Directory.Exists(targetDirectory) && overwrite)
        {
            Directory.Delete(targetDirectory, recursive: true);
        }

        Directory.CreateDirectory(targetDirectory);
        CopyDirectoryContents(sourceDirectory, targetDirectory, "", shouldSkipRelativePath);
    }

    private static void CopyDirectoryContents(
        string sourceRoot,
        string targetRoot,
        string relativeRoot,
        Func<string, bool> shouldSkipRelativePath)
    {
        var sourceDirectory = string.IsNullOrWhiteSpace(relativeRoot)
            ? sourceRoot
            : Path.Combine(sourceRoot, relativeRoot);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var relativePath = Path.Combine(relativeRoot, Path.GetFileName(directory));
            if (shouldSkipRelativePath(relativePath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(targetRoot, relativePath));
            CopyDirectoryContents(sourceRoot, targetRoot, relativePath, shouldSkipRelativePath);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var relativePath = Path.Combine(relativeRoot, Path.GetFileName(file));
            if (shouldSkipRelativePath(relativePath))
            {
                continue;
            }

            var targetPath = Path.Combine(targetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetRoot);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static bool IsPreviousSongLibraryRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return PreviousSongLibraryRelativePaths.Any(songLibrary =>
            string.Equals(normalized, songLibrary, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(songLibrary + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPreviousSongLibraryBackupRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return PreviousSongLibraryBackupRelativePathPrefixes.Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProvisionTransientRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return ProvisionTransientRelativePaths.Any(transientPath =>
            string.Equals(normalized, transientPath, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(transientPath + "/", StringComparison.OrdinalIgnoreCase));
    }

    private void AddEventNoLock(
        string kind,
        string tag,
        string text,
        string? replayId = null,
        int? instanceIndex = null)
    {
        _state.Events ??= new List<ControlPanelEventRecord>();
        _state.Events.Insert(0, new ControlPanelEventRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Kind = NormalizeNullable(kind) ?? "Info",
            Tag = NormalizeNullable(tag) ?? "Event",
            Text = NormalizeNullable(text) ?? "Event recorded.",
            ReplayId = NormalizeNullable(replayId),
            InstanceIndex = instanceIndex
        });

        if (_state.Events.Count > 200)
        {
            _state.Events.RemoveRange(200, _state.Events.Count - 200);
        }
    }

    private static string CreateReplayLabel(ReplayQueueRecord replay)
    {
        return NormalizeNullable(replay.SongName) ??
               NormalizeNullable(replay.FileName) ??
               "Replay #" + replay.SequenceNumber;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        var display = (double)value;
        while (display >= 1024 && unitIndex < units.Length - 1)
        {
            display /= 1024;
            unitIndex++;
        }

        return (unitIndex == 0 ? display.ToString("0", CultureInfo.InvariantCulture) : display.ToString("0.0", CultureInfo.InvariantCulture)) +
               " " +
               units[unitIndex];
    }

    private void SaveNoLock()
    {
        File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, JsonOptions.Default));
    }

    private static ControlPanelState Clone(ControlPanelState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions.Default);
        return JsonSerializer.Deserialize<ControlPanelState>(json, JsonOptions.Default)
               ?? new ControlPanelState();
    }

    private static ControlPanelSettings CloneSettings(ControlPanelSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions.Default);
        return JsonSerializer.Deserialize<ControlPanelSettings>(json, JsonOptions.Default)
               ?? new ControlPanelSettings();
    }

    private static GamePresentationSettings CloneGamePresentationSettings(GamePresentationSettings? settings)
    {
        var clone = new GamePresentationSettings
        {
            NoHud = settings?.NoHud ?? true,
            LoadPlayerEnvironment = settings?.LoadPlayerEnvironment ?? false,
            LoadPlayerJumpDistance = settings?.LoadPlayerJumpDistance ?? false,
            IgnoreModifiers = settings?.IgnoreModifiers ?? false,
            ShowHead = settings?.ShowHead ?? false,
            ShowLeftSaber = settings?.ShowLeftSaber ?? true,
            ShowRightSaber = settings?.ShowRightSaber ?? true,
            ShowWatermark = settings?.ShowWatermark ?? true,
            ShowTimelineMisses = settings?.ShowTimelineMisses ?? true,
            ShowTimelineBombs = settings?.ShowTimelineBombs ?? true,
            ShowTimelinePauses = settings?.ShowTimelinePauses ?? true,
            SfxVolume = settings?.SfxVolume ?? 0.3f,
            NoTextsAndHuds = settings?.NoTextsAndHuds ?? true,
            AdvancedHud = settings?.AdvancedHud ?? false,
            ReduceDebris = settings?.ReduceDebris ?? true,
            NoFailEffects = settings?.NoFailEffects ?? false,
            SaberTrailIntensity = settings?.SaberTrailIntensity ?? 0f,
            NoteJumpDurationType = settings?.NoteJumpDurationType ?? GamePresentationSettings.NoteJumpDurationTypeDynamic,
            NoteJumpFixedDuration = settings?.NoteJumpFixedDuration ?? 0.2f,
            NoteJumpStartBeatOffset = settings?.NoteJumpStartBeatOffset ?? 0f,
            HideNoteSpawnEffect = settings?.HideNoteSpawnEffect ?? false,
            AdaptiveSfx = settings?.AdaptiveSfx ?? true,
            ArcsHapticFeedback = settings?.ArcsHapticFeedback ?? true,
            ArcVisibility = settings?.ArcVisibility ?? GamePresentationSettings.ArcVisibilityLow,
            EnvironmentEffectsFilterDefaultPreset = settings?.EnvironmentEffectsFilterDefaultPreset ??
                                                    GamePresentationSettings.EnvironmentEffectsAllEffects,
            EnvironmentEffectsFilterExpertPlusPreset = settings?.EnvironmentEffectsFilterExpertPlusPreset ??
                                                       GamePresentationSettings.EnvironmentEffectsAllEffects,
            HeadsetHapticIntensity = settings?.HeadsetHapticIntensity ?? 0.7f
        };
        clone.Normalize();
        return clone;
    }

    private static bool GamePresentationSettingsEqual(
        GamePresentationSettings? left,
        GamePresentationSettings? right)
    {
        var normalizedLeft = CloneGamePresentationSettings(left);
        var normalizedRight = CloneGamePresentationSettings(right);
        return normalizedLeft.NoHud == normalizedRight.NoHud &&
               normalizedLeft.LoadPlayerEnvironment == normalizedRight.LoadPlayerEnvironment &&
               normalizedLeft.LoadPlayerJumpDistance == normalizedRight.LoadPlayerJumpDistance &&
               normalizedLeft.IgnoreModifiers == normalizedRight.IgnoreModifiers &&
               normalizedLeft.ShowHead == normalizedRight.ShowHead &&
               normalizedLeft.ShowLeftSaber == normalizedRight.ShowLeftSaber &&
               normalizedLeft.ShowRightSaber == normalizedRight.ShowRightSaber &&
               normalizedLeft.ShowWatermark == normalizedRight.ShowWatermark &&
               normalizedLeft.ShowTimelineMisses == normalizedRight.ShowTimelineMisses &&
               normalizedLeft.ShowTimelineBombs == normalizedRight.ShowTimelineBombs &&
               normalizedLeft.ShowTimelinePauses == normalizedRight.ShowTimelinePauses &&
               normalizedLeft.SfxVolume == normalizedRight.SfxVolume &&
               normalizedLeft.NoTextsAndHuds == normalizedRight.NoTextsAndHuds &&
               normalizedLeft.AdvancedHud == normalizedRight.AdvancedHud &&
               normalizedLeft.ReduceDebris == normalizedRight.ReduceDebris &&
               normalizedLeft.NoFailEffects == normalizedRight.NoFailEffects &&
               normalizedLeft.SaberTrailIntensity == normalizedRight.SaberTrailIntensity &&
               string.Equals(normalizedLeft.NoteJumpDurationType, normalizedRight.NoteJumpDurationType, StringComparison.Ordinal) &&
               normalizedLeft.NoteJumpFixedDuration == normalizedRight.NoteJumpFixedDuration &&
               normalizedLeft.NoteJumpStartBeatOffset == normalizedRight.NoteJumpStartBeatOffset &&
               normalizedLeft.HideNoteSpawnEffect == normalizedRight.HideNoteSpawnEffect &&
               normalizedLeft.AdaptiveSfx == normalizedRight.AdaptiveSfx &&
               normalizedLeft.ArcsHapticFeedback == normalizedRight.ArcsHapticFeedback &&
               string.Equals(normalizedLeft.ArcVisibility, normalizedRight.ArcVisibility, StringComparison.Ordinal) &&
               string.Equals(
                   normalizedLeft.EnvironmentEffectsFilterDefaultPreset,
                   normalizedRight.EnvironmentEffectsFilterDefaultPreset,
                   StringComparison.Ordinal) &&
               string.Equals(
                   normalizedLeft.EnvironmentEffectsFilterExpertPlusPreset,
                   normalizedRight.EnvironmentEffectsFilterExpertPlusPreset,
                   StringComparison.Ordinal) &&
               normalizedLeft.HeadsetHapticIntensity == normalizedRight.HeadsetHapticIntensity;
    }

    private static int NextGamePresentationSettingsVersion(int currentVersion)
    {
        return currentVersion >= int.MaxValue ? 1 : Math.Max(1, currentVersion) + 1;
    }

    private static bool CanClaimWorkerSlot(WorkerInstanceRecord instance, string? workerId, DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(instance.WorkerId) ||
               (workerId != null && string.Equals(instance.WorkerId, workerId, StringComparison.OrdinalIgnoreCase)) ||
               IsWorkerStale(instance, now);
    }

    private static bool IsWorkerStale(WorkerInstanceRecord instance, DateTimeOffset now)
    {
        return instance.LastHeartbeatUtc == null ||
               now - instance.LastHeartbeatUtc.Value > WorkerStaleAfter;
    }

    private static bool IsPendingReplayStatus(string status)
    {
        return string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Assigned", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveReplayStatus(string status)
    {
        return string.Equals(status, "Assigned", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecordingStatus(string status)
    {
        return string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Playing", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Started", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if (!fullDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
            !fullDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            fullDirectory += Path.DirectorySeparatorChar;
        }

        return Path.GetFullPath(path).StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownBeatSaberProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            return string.Equals(process.ProcessName, BeatSaberProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> SplitCommandLine(string? commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        char quote = '\0';

        foreach (var ch in commandLine ?? "")
        {
            if (inQuote)
            {
                if (ch == quote)
                {
                    inQuote = false;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string NormalizeReplayStatus(string status)
    {
        var normalized = NormalizeStatus(status, "Recording");
        if (string.Equals(normalized, "Complete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Success", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return "Completed";
        }

        if (string.Equals(normalized, "Fail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Error", StringComparison.OrdinalIgnoreCase))
        {
            return "Failed";
        }

        if (string.Equals(normalized, "Stop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Stopped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Canceled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return "Stopped";
        }

        if (IsRecordingStatus(normalized))
        {
            return "Recording";
        }

        return normalized;
    }

    private static string NormalizeStatus(string status, string fallback)
    {
        var trimmed = NormalizeNullable(status);
        if (trimmed == null)
        {
            return fallback;
        }

        return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
    }

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string CreateOutputBaseName(ReplayQueueRecord replay)
    {
        var songName = string.IsNullOrWhiteSpace(replay.SongName)
            ? Path.GetFileNameWithoutExtension(replay.FileName)
            : replay.SongName;
        var difficulty = string.IsNullOrWhiteSpace(replay.Difficulty) ? "" : " [" + replay.Difficulty + "]";
        return FileNameSanitizer.SanitizeBaseName($"{replay.SequenceNumber:000} - {songName}{difficulty}");
    }

    private static string CreateWorkerId()
    {
        return "worker-" + Guid.NewGuid().ToString("N").Substring(0, 12);
    }

    private void ResetInactiveInstanceIdentityNoLock(WorkerInstanceRecord instance)
    {
        instance.Name = CreateManagedInstanceName(instance.Index);
        instance.GameDirectory = null;
        instance.PluginVersion = null;
        instance.RegisteredAtUtc = null;
        instance.LastHeartbeatUtc = null;
        instance.AppliedGamePresentationSettingsVersion = 0;
        instance.GamePresentationSyncStatus = "Pending";
        instance.GamePresentationSyncError = "";
        if (string.Equals(instance.GameLaunchStatus, "Worker online", StringComparison.OrdinalIgnoreCase))
        {
            instance.GameLaunchStatus = instance.GameProcessId.HasValue ? "Started" : "Idle";
        }
    }

    private static string CreateAssignmentId()
    {
        return "assignment-" + Guid.NewGuid().ToString("N").Substring(0, 12);
    }

    private static string CreateStableId(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).Substring(0, 16).ToLowerInvariant();
    }

    private sealed class RecordingSyncVerificationResult
    {
        public bool Verified { get; set; }

        public string? Error { get; set; }
    }
}

internal sealed class SharedFolderDefinition
{
    public SharedFolderDefinition(string displayName, string instanceRelativePath, string sharedFolderPath)
    {
        DisplayName = displayName;
        InstanceRelativePath = instanceRelativePath;
        SharedFolderPath = sharedFolderPath;
    }

    public string DisplayName { get; }

    public string InstanceRelativePath { get; }

    public string SharedFolderPath { get; }
}

internal sealed class ManagedInstanceTarget
{
    public ManagedInstanceTarget(int index, string name, string directory)
    {
        Index = index;
        Name = name;
        Directory = directory;
    }

    public int Index { get; }

    public string Name { get; }

    public string Directory { get; }
}
