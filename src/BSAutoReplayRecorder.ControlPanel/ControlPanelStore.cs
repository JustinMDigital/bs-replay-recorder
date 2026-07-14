using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Replay;
using BSAutoReplayRecorder.Core.Utility;
using Microsoft.Win32;

namespace BSAutoReplayRecorder.ControlPanel;

public sealed class ControlPanelStore
{
    private static readonly TimeSpan WorkerStaleAfter = TimeSpan.FromSeconds(30);
    private static readonly string[] RecordingRenameFormats =
    {
        "Default",
        "Key",
        "KeySong",
        "Song",
        "SongArtist",
        "SongArtistPlayer",
        "SongPlayer",
        "SongMapper",
        "SongDifficulty",
        "PlayerSong"
    };
    private const string BeatSaberExecutableName = "Beat Saber.exe";
    private const string BeatSaberProcessName = "Beat Saber";
    private const string BeatSaberSteamAppId = "620980";
    private const string SteamApiInitFailedLogText = "SteamAPI_Init() failed";
    private const string SteamUnavailableLaunchMessage =
        "Beat Saber exited because Steam was not available. Open Steam, make sure you are logged in, then launch the worker again.";
    private const string VrControllerThumbstickFailureLogText = "VRController.get_thumbstick";
    private const string VrControllerTriggerFailureLogText = "VRController.get_triggerValue";
    private const string VrControllerFocusFailureLogText = "DeactivateVRControllersOnFocusCapture";
    private const string BeatSaberBlackScreenLaunchMessage =
        "Beat Saber appears stuck on a black screen after launch. The log is repeatedly throwing Unity VR controller errors before BeatLeader's replay loader comes online; the recorder closed the stuck game. Relaunch this worker, or rebuild the instance if it repeats.";
    private const int VrControllerFailureMinimumOccurrences = 20;
    private const double LagSpikeFramesPerSecondThreshold = 50;
    private const double MinimumRecordingLagSpikeStartupGraceSeconds = 12;
    private static readonly TimeSpan LagSpikeLowFpsDurationThreshold = TimeSpan.FromSeconds(10);
    private const int BenchmarkMaximumConcurrency = 4;
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
        "UserData/LocalLeaderboard/Replays",
        "UserData/ScoreSaber/Replays",
        "UserData/BSWorldCupReplayRecorder/Recordings",
        "UserData/BSAutoReplayRecorder/Recordings"
    };
    private static readonly string[] WorkerConflictingModRelativePaths =
    {
        "Plugins/BeatSaverDownloader.dll",
        "Plugins/DataPuller.dll",
        "UserData/BeatSaverDownloader.ini",
        "UserData/DataPuller.json"
    };

    private readonly object _sync = new object();
    private readonly string _statePath;
    private readonly string _queueDirectory;
    private readonly string _collectionsDirectory;
    private readonly IRecordingAudioVerifier _recordingAudioVerifier;
    private readonly IRecordingChapterEmbedder _recordingChapterEmbedder;
    private readonly IRecorderHostHealthChecker _recorderHostHealthChecker;
    private readonly IDisplayInfoProvider _displayInfoProvider;
    private readonly ICapturePreflightRunner _capturePreflightRunner;
    private readonly IFfmpegSetupService _ffmpegSetupService;
    private readonly IBeatSaverMapDownloader _mapDownloader;
    private readonly IWorkerPluginInstaller _workerPluginInstaller;
    private readonly IBeatLeaderReplayDownloader _beatLeaderReplayDownloader;
    private readonly IScoreSaberReplayDownloader _scoreSaberReplayDownloader;
    private readonly ControlPanelState _state;
    private int? _detectedRestoreDisplayScalePercent;

    public ControlPanelStore(
        ControlPanelSettings settings,
        IRecordingAudioVerifier? recordingAudioVerifier = null,
        IRecorderHostHealthChecker? recorderHostHealthChecker = null,
        IBeatSaverMapDownloader? mapDownloader = null,
        IWorkerPluginInstaller? workerPluginInstaller = null,
        IBeatLeaderReplayDownloader? beatLeaderReplayDownloader = null,
        IScoreSaberReplayDownloader? scoreSaberReplayDownloader = null,
        IRecordingChapterEmbedder? recordingChapterEmbedder = null,
        IDisplayInfoProvider? displayInfoProvider = null,
        ICapturePreflightRunner? capturePreflightRunner = null,
        IFfmpegSetupService? ffmpegSetupService = null)
    {
        settings.Normalize();
        var workspaceDirectory = Path.GetFullPath(settings.WorkspaceDirectory);
        Directory.CreateDirectory(workspaceDirectory);

        _statePath = Path.Combine(workspaceDirectory, "control-panel-state.json");
        _queueDirectory = Path.Combine(workspaceDirectory, "Queue");
        _collectionsDirectory = Path.Combine(workspaceDirectory, "Collections");
        _recordingAudioVerifier = recordingAudioVerifier ?? new FfprobeRecordingAudioVerifier();
        _recordingChapterEmbedder = recordingChapterEmbedder ?? new FfmpegRecordingChapterEmbedder();
        _recorderHostHealthChecker = recorderHostHealthChecker ?? new HttpRecorderHostHealthChecker();
        _displayInfoProvider = displayInfoProvider ?? new UnavailableDisplayInfoProvider();
        _capturePreflightRunner = capturePreflightRunner ?? new NullCapturePreflightRunner();
        _ffmpegSetupService = ffmpegSetupService ?? new FfmpegSetupService();
        _mapDownloader = mapDownloader ?? new BeatSaverMapDownloader(new HttpClient());
        _workerPluginInstaller = workerPluginInstaller ?? new DotNetWorkerPluginInstaller();
        _beatLeaderReplayDownloader = beatLeaderReplayDownloader ?? new BeatLeaderReplayDownloader(new HttpClient());
        _scoreSaberReplayDownloader = scoreSaberReplayDownloader ?? new ScoreSaberReplayDownloader(new HttpClient());
        Directory.CreateDirectory(_queueDirectory);
        Directory.CreateDirectory(_collectionsDirectory);

        _state = LoadState(settings);
        _state.LastActivityUtc = DateTimeOffset.UtcNow;
        TaskbarVisibilityController.Restore();
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
            changed |= NormalizeBenchmarkNoLock(DateTimeOffset.UtcNow);
            if (changed)
            {
                SaveNoLock();
            }

            return Clone(_state);
        }
    }

    public GameColorPresetCatalog GetGameColorPresets()
    {
        lock (_sync)
        {
            _state.Settings.Normalize();
            return GameColorPresetCatalogReader.Create(_state.Settings);
        }
    }

    public SetupSourcePathReport GetSetupSourcePath(string? bsManagerRoot = null)
    {
        lock (_sync)
        {
            _state.Settings.Normalize();
            return string.IsNullOrWhiteSpace(bsManagerRoot)
                ? SetupSourcePathDetector.Detect(
                    _state.Settings.SourceBeatSaberPath,
                    _state.Settings.SourceBeatSaberStore)
                : SetupSourcePathDetector.DetectWithBsManagerRoots(
                    _state.Settings.SourceBeatSaberPath,
                    steamLibraryCandidates: null,
                    metaLibraryCandidates: null,
                    bsManagerRootCandidates: new[] { bsManagerRoot },
                    configuredStore: _state.Settings.SourceBeatSaberStore);
        }
    }

    public ControlPanelState RequestMetaSideloadedAppsEnable()
    {
        lock (_sync)
        {
            if (MetaSideloadedApps.IsEnabled())
            {
                AddEventNoLock("Good", "Meta", "Meta sideloaded apps are already enabled.");
            }
            else
            {
                MetaSideloadedApps.RequestEnable();
                AddEventNoLock("Info", "Meta", "Windows approval requested to enable Meta sideloaded apps. Approve it, then recheck the source.");
            }

            SaveNoLock();
            return Clone(_state);
        }
    }

    public GameColorPresetCatalog SaveGameColorPreset(SaveGameColorPresetRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Color preset request is required.");
        }

        lock (_sync)
        {
            var name = NormalizeNullable(request.Name) ?? "Color preset";
            var preset = GameColorPresetCatalogReader.CreateSavedPreset(name, request.Colors ?? new GamePresentationSettings());
            _state.Settings.GameColorPresets ??= new List<GameColorPreset>();
            _state.Settings.GameColorPresets.Add(preset);
            _state.Settings.Normalize();
            AddEventNoLock("Info", "Colors", "Saved color preset: " + preset.Name + ".");
            SaveNoLock();
            return GameColorPresetCatalogReader.Create(_state.Settings);
        }
    }

    public GameColorPresetCatalog DeleteGameColorPreset(string id)
    {
        lock (_sync)
        {
            var normalizedId = NormalizeNullable(id);
            if (normalizedId == null)
            {
                throw new InvalidOperationException("Color preset id is required.");
            }

            _state.Settings.GameColorPresets ??= new List<GameColorPreset>();
            var removed = _state.Settings.GameColorPresets.RemoveAll(preset =>
                string.Equals(preset.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                throw new InvalidOperationException("Saved color preset was not found.");
            }

            AddEventNoLock("Warn", "Colors", "Deleted saved color preset.");
            SaveNoLock();
            return GameColorPresetCatalogReader.Create(_state.Settings);
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
            _state.Settings.FfmpegPath = request.FfmpegPath;
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
            if (!string.IsNullOrWhiteSpace(request.CaptureEngine))
            {
                _state.Settings.CaptureEngine = request.CaptureEngine;
            }

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
            _state.Settings.SourceBeatSaberPath = request.SourceBeatSaberPath;
            _state.Settings.SourceBeatSaberStore = request.SourceBeatSaberStore;
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
            if (GetCreatedManagedInstancesNoLock().Count == GetConfiguredInstancesNoLock().Count)
            {
                RepairSongFolderLinksNoLock();
            }
            else
            {
                _state.SongFolders = ScanSongFolderLinksNoLock();
            }

            RefreshInstanceProvisionCountsNoLock();
            RefreshDiskSpaceNoLock();
            AddEventNoLock("Info", "Settings", "Settings saved.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public FfmpegSetupReport CheckFfmpegSetup()
    {
        lock (_sync)
        {
            _state.FfmpegSetup = _ffmpegSetupService.Check(_state.Settings.FfmpegPath);
            return CloneFfmpegSetup(_state.FfmpegSetup);
        }
    }

    public ControlPanelState InstallFfmpeg()
    {
        lock (_sync)
        {
            var report = _ffmpegSetupService.Install(_state.Settings.FfmpegPath);
            _state.FfmpegSetup = report;
            if (string.Equals(report.Status, "Ready", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(report.FfmpegPath))
            {
                _state.Settings.FfmpegPath = report.FfmpegPath;
                _state.Settings.Normalize();
            }

            AddEventNoLock(
                string.Equals(report.Status, "Ready", StringComparison.OrdinalIgnoreCase) ? "Good" : "Bad",
                "FFmpeg",
                report.Detail);
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
                    !IsSupportedReplayFileName(file.FileName))
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

    public async Task<IReadOnlyList<ReplayQueueRecord>> ImportReferencesAsync(
        ReplayReferenceImportRequest request,
        CancellationToken cancellationToken)
    {
        var import = await DownloadReferenceImportsAsync(
            request,
            _queueDirectory,
            CreateImportPath,
            cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            var imported = new List<ReplayQueueRecord>();
            if (import.ImportedPaths.Count > 0)
            {
                var importedSet = new HashSet<string>(import.ImportedPaths, StringComparer.OrdinalIgnoreCase);
                ReloadQueueNoLock();
                RedistributeQueuedReplayPlansNoLock();
                imported.AddRange(_state.Queue.Where(item => importedSet.Contains(Path.GetFullPath(item.Path))));
                RefreshQueueMapAvailabilityNoLock(allowDownload: true, imported.Select(item => item.Id));
                AddEventNoLock(
                    "Info",
                    "Import",
                    "Imported " + imported.Count + " linked replay" + (imported.Count == 1 ? "" : "s") + ".");
            }

            if (import.Failures.Count > 0)
            {
                AddEventNoLock(
                    imported.Count > 0 ? "Warn" : "Error",
                    "Import",
                    "Replay link import skipped " + import.Failures.Count + ": " + string.Join("; ", import.Failures.Take(3)) +
                    (import.Failures.Count > 3 ? "; and " + (import.Failures.Count - 3) + " more." : ""));
            }

            SaveNoLock();
            return imported;
        }
    }

    public async Task<MapCollectionImportResult> ImportReferencesToMapCollectionAsync(
        string id,
        ReplayReferenceImportRequest request,
        CancellationToken cancellationToken)
    {
        string collectionDirectory;
        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            collectionDirectory = Path.Combine(_collectionsDirectory, collection.Id);
            Directory.CreateDirectory(collectionDirectory);
        }

        var import = await DownloadReferenceImportsAsync(
            request,
            collectionDirectory,
            fileName => CreateUniqueFilePath(collectionDirectory, fileName),
            cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            return AddImportedReplayPathsToMapCollectionNoLock(
                collection,
                collectionDirectory,
                import.ImportedPaths,
                0,
                import.Failures);
        }
    }

    public IReadOnlyList<MapCollectionRecord> GetMapCollections()
    {
        lock (_sync)
        {
            return Clone(_state).Collections;
        }
    }

    public MapCollectionRecord SaveMapCollection(SaveMapCollectionRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Collection request is required.");
        }

        lock (_sync)
        {
            var name = NormalizeNullable(request.Name);
            if (name == null)
            {
                throw new InvalidOperationException("Collection name is required.");
            }

            var selectedIds = new HashSet<string>(
                (request.ReplayIds ?? new List<string>())
                .Select(value => NormalizeNullable(value))
                .Where(value => value != null)
                .Select(value => value!),
                StringComparer.OrdinalIgnoreCase);
            var replays = request.CreateEmpty
                ? new List<ReplayQueueRecord>()
                : _state.Queue
                    .Where(replay => selectedIds.Count == 0 || selectedIds.Contains(replay.Id))
                    .OrderBy(replay => replay.SequenceNumber)
                    .ToList();
            if (replays.Count == 0 && !request.CreateEmpty)
            {
                throw new InvalidOperationException("Import replays before saving a collection.");
            }

            var now = DateTimeOffset.UtcNow;
            var collectionId = "collection-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var collectionDirectory = Path.Combine(_collectionsDirectory, collectionId);
            Directory.CreateDirectory(collectionDirectory);

            try
            {
                var items = new List<MapCollectionItemRecord>();
                foreach (var replay in replays)
                {
                    if (!File.Exists(replay.Path))
                    {
                        throw new InvalidOperationException(
                            "Cannot save collection because a replay file is missing: " + replay.FileName + ".");
                    }

                    var collectionReplayPath = CreateUniqueFilePath(collectionDirectory, replay.FileName);
                    File.Copy(replay.Path, collectionReplayPath);
                    WriteReplaySidecar(collectionReplayPath, CreateReplaySidecar(replay, collectionReplayPath));
                    items.Add(CreateMapCollectionItem(replay, collectionReplayPath, items.Count + 1));
                }

                var collection = new MapCollectionRecord
                {
                    Id = collectionId,
                    Name = name,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Items = items
                };
                _state.Collections.Add(collection);
                AddEventNoLock(
                    "Info",
                    "Collections",
                    (items.Count == 0 ? "Created collection: " : "Saved collection: ") +
                    name + " (" + items.Count + " replay" + (items.Count == 1 ? "" : "s") + ").");
                SaveNoLock();
                return CloneMapCollection(collection);
            }
            catch
            {
                try
                {
                    if (Directory.Exists(collectionDirectory))
                    {
                        Directory.Delete(collectionDirectory, recursive: true);
                    }
                }
                catch
                {
                }

                throw;
            }
        }
    }

    public MapCollectionImportResult ImportFilesToMapCollection(string id, IFormFileCollection files)
    {
        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            var collectionDirectory = Path.Combine(_collectionsDirectory, collection.Id);
            Directory.CreateDirectory(collectionDirectory);

            var copiedPaths = new List<string>();
            var skippedCount = 0;
            if (files == null || files.Count == 0)
            {
                return new MapCollectionImportResult
                {
                    State = Clone(_state),
                    Collection = CloneMapCollection(collection)
                };
            }

            foreach (var file in files)
            {
                if (file.Length <= 0 || !IsSupportedReplayFileName(file.FileName))
                {
                    skippedCount++;
                    continue;
                }

                var targetPath = CreateUniqueFilePath(collectionDirectory, file.FileName);
                using (var stream = File.Create(targetPath))
                {
                    file.CopyTo(stream);
                }

                copiedPaths.Add(Path.GetFullPath(targetPath));
            }

            return AddImportedReplayPathsToMapCollectionNoLock(
                collection,
                collectionDirectory,
                copiedPaths,
                skippedCount,
                new List<string>());
        }
    }

    public MapCollectionRecord RenameMapCollection(string id, RenameMapCollectionRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Collection rename request is required.");
        }

        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            var name = NormalizeNullable(request.Name);
            if (name == null)
            {
                throw new InvalidOperationException("Collection name is required.");
            }

            if (string.Equals(collection.Name, name, StringComparison.Ordinal))
            {
                return CloneMapCollection(collection);
            }

            var previousName = collection.Name;
            collection.Name = name;
            collection.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddEventNoLock("Info", "Collections", "Renamed collection: " + previousName + " to " + name + ".");
            SaveNoLock();
            return CloneMapCollection(collection);
        }
    }

    public MapCollectionRecord RemoveMapCollectionItem(string id, string itemId)
    {
        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            var item = FindMapCollectionItemNoLock(collection, itemId);
            collection.Items.Remove(item);
            ResequenceMapCollectionItemsNoLock(collection);
            collection.UpdatedAtUtc = DateTimeOffset.UtcNow;
            TryDeleteCollectionItemFilesNoLock(item);
            AddEventNoLock(
                "Warn",
                "Collections",
                "Removed replay from collection: " +
                Prefer(item.SongName, item.FileName) +
                " from " + collection.Name + ".");
            SaveNoLock();
            return CloneMapCollection(collection);
        }
    }

    private async Task<ReferenceImportDownloadResult> DownloadReferenceImportsAsync(
        ReplayReferenceImportRequest request,
        string targetDirectory,
        Func<string, string> createImportPath,
        CancellationToken cancellationToken)
    {
        var result = new ReferenceImportDownloadResult();
        if (request == null || request.References == null || request.References.Count == 0)
        {
            return result;
        }

        var parser = new ReplayReferenceParser();
        foreach (var value in request.References)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            ReplayReference reference;
            try
            {
                reference = parser.Parse(value);
            }
            catch (Exception ex)
            {
                result.Failures.Add(value + ": " + ex.Message);
                continue;
            }

            try
            {
                switch (reference.Provider)
                {
                    case ReplayProvider.BeatLeader:
                    {
                        var download = await _beatLeaderReplayDownloader
                            .DownloadAsync(reference, targetDirectory, createImportPath, cancellationToken)
                            .ConfigureAwait(false);
                        WriteReplaySidecar(download.LocalPath, download.Metadata);
                        result.ImportedPaths.Add(Path.GetFullPath(download.LocalPath));
                        break;
                    }
                    case ReplayProvider.ScoreSaber2:
                    {
                        var download = await _scoreSaberReplayDownloader
                            .DownloadAsync(reference, targetDirectory, createImportPath, cancellationToken)
                            .ConfigureAwait(false);
                        WriteReplaySidecar(download.LocalPath, download.Metadata);
                        result.ImportedPaths.Add(Path.GetFullPath(download.LocalPath));
                        break;
                    }
                    default:
                        result.Failures.Add(value + ": only BeatLeader and ScoreSaber 2 replay links are supported.");
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                result.Failures.Add(value + ": " + ex.Message);
            }
        }

        return result;
    }

    private MapCollectionImportResult AddImportedReplayPathsToMapCollectionNoLock(
        MapCollectionRecord collection,
        string collectionDirectory,
        IReadOnlyList<string> importedPaths,
        int skippedCount,
        IReadOnlyList<string> failures)
    {
        var importedCount = 0;
        if (importedPaths.Count > 0)
        {
            var importedSet = new HashSet<string>(importedPaths, StringComparer.OrdinalIgnoreCase);
            var loadedReplays = new ReplayQueue()
                .Load(new ReplayQueueOptions
                {
                    InputDirectory = collectionDirectory,
                    SkipInvalidReplays = true
                })
                .Items
                .ToDictionary(item => Path.GetFullPath(item.ReplayPath), StringComparer.OrdinalIgnoreCase);

            foreach (var path in importedPaths)
            {
                if (!loadedReplays.TryGetValue(Path.GetFullPath(path), out var loadedReplay))
                {
                    skippedCount++;
                    TryDeleteFile(path);
                    continue;
                }

                var replay = CreateReplayRecordFromQueueItemNoLock(loadedReplay);
                WriteReplaySidecar(path, CreateReplaySidecar(replay, path));
                collection.Items.Add(CreateMapCollectionItem(replay, path, collection.Items.Count + 1));
                importedCount++;
            }

            foreach (var path in importedSet)
            {
                if (!loadedReplays.ContainsKey(Path.GetFullPath(path)) && File.Exists(path))
                {
                    TryDeleteFile(path);
                }
            }
        }

        if (importedCount > 0)
        {
            collection.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddEventNoLock(
                "Info",
                "Collections",
                "Added " + importedCount + " replay" + (importedCount == 1 ? "" : "s") +
                " to collection: " + collection.Name + ".");
        }

        if (failures.Count > 0)
        {
            AddEventNoLock(
                importedCount > 0 ? "Warn" : "Error",
                "Collections",
                "Collection link import skipped " + failures.Count + ": " + string.Join("; ", failures.Take(3)) +
                (failures.Count > 3 ? "; and " + (failures.Count - 3) + " more." : ""));
        }

        SaveNoLock();
        return new MapCollectionImportResult
        {
            State = Clone(_state),
            Collection = CloneMapCollection(collection),
            ImportedCount = importedCount,
            SkippedCount = skippedCount + failures.Count
        };
    }

    public MapCollectionLoadResult LoadMapCollection(string id, LoadMapCollectionRequest request)
    {
        lock (_sync)
        {
            EnsureQueueCanLoadCollectionNoLock();
            var collection = FindMapCollectionNoLock(id);
            var overwriteRecorded = request?.OverwriteRecorded == true;
            var copiedPaths = new List<string>();
            var changedReplayIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var loadedCount = 0;
            var requeuedCount = 0;
            var skippedRecordedCount = 0;
            var missingCount = 0;

            foreach (var item in collection.Items.OrderBy(item => item.SequenceNumber))
            {
                var existing = FindMatchingQueueItemNoLock(item);
                if (existing != null)
                {
                    if (IsRecordedReplay(existing) && !overwriteRecorded)
                    {
                        skippedRecordedCount++;
                        continue;
                    }

                    if (!string.Equals(existing.Status, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        RequeueReplayNoLock(existing);
                        requeuedCount++;
                    }

                    changedReplayIds.Add(existing.Id);
                    loadedCount++;
                    continue;
                }

                if (IsRecordedCollectionItem(item) && !overwriteRecorded)
                {
                    skippedRecordedCount++;
                    continue;
                }

                if (!File.Exists(item.Path))
                {
                    missingCount++;
                    continue;
                }

                var importPath = CreateImportPath(item.FileName);
                File.Copy(item.Path, importPath);
                WriteReplaySidecar(importPath, CreateReplaySidecar(item, importPath));
                copiedPaths.Add(Path.GetFullPath(importPath));
                loadedCount++;
            }

            if (copiedPaths.Count > 0)
            {
                ReloadQueueNoLock();
                ApplyCollectionImportOrderNoLock(copiedPaths);
                foreach (var path in copiedPaths)
                {
                    var replay = _state.Queue.FirstOrDefault(item =>
                        string.Equals(Path.GetFullPath(item.Path), path, StringComparison.OrdinalIgnoreCase));
                    if (replay != null)
                    {
                        changedReplayIds.Add(replay.Id);
                    }
                }
            }

            if (changedReplayIds.Count > 0)
            {
                RefreshQueueMapAvailabilityNoLock(allowDownload: true, changedReplayIds);
            }

            RecalculateRunCountsNoLock();
            RedistributeQueuedReplayPlansNoLock();
            AddEventNoLock(
                "Info",
                "Collections",
                "Loaded collection: " + collection.Name + " (" + loadedCount + " queued, " +
                skippedRecordedCount + " skipped recorded" +
                (missingCount > 0 ? ", " + missingCount + " missing" : "") + ").");
            SaveNoLock();
            return new MapCollectionLoadResult
            {
                State = Clone(_state),
                CollectionId = collection.Id,
                CollectionName = collection.Name,
                LoadedCount = loadedCount,
                RequeuedCount = requeuedCount,
                SkippedRecordedCount = skippedRecordedCount,
                MissingCount = missingCount
            };
        }
    }

    public ControlPanelState DeleteMapCollection(string id)
    {
        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            _state.Collections.Remove(collection);
            var collectionDirectory = Path.Combine(_collectionsDirectory, collection.Id);
            if (Directory.Exists(collectionDirectory) &&
                IsPathInsideDirectory(Path.GetFullPath(collectionDirectory), _collectionsDirectory))
            {
                Directory.Delete(collectionDirectory, recursive: true);
            }

            AddEventNoLock("Warn", "Collections", "Deleted collection: " + collection.Name + ".");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public MapCardExport GetMapCardExport(string id)
    {
        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            return CreateMapCardExportNoLock(collection);
        }
    }

    public MapCardExport UpdateMapCardCategories(string id, UpdateMapCollectionCardCategoriesRequest request)
    {
        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            foreach (var update in request.Items ?? new List<MapCollectionCardCategoryUpdate>())
            {
                var normalizedItemId = NormalizeNullable(update.ItemId);
                if (normalizedItemId == null)
                {
                    continue;
                }

                var item = collection.Items.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, normalizedItemId, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                {
                    continue;
                }

                item.MapCardCategory = NormalizeMapCardCategory(update.Category);
            }

            collection.UpdatedAtUtc = DateTimeOffset.UtcNow;
            SaveNoLock();
            return CreateMapCardExportNoLock(collection);
        }
    }

    public string GetCollectionItemCoverPath(string collectionId, string itemId)
    {
        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(collectionId);
            var item = FindMapCollectionItemNoLock(collection, itemId);
            var replay = CreateReplayRecordForMapLookup(item);
            return ResolveCoverArtPathNoLock(replay)
                   ?? throw new InvalidOperationException("No cover art was found for " + item.FileName + ".");
        }
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
            foreach (var file in Directory.EnumerateFiles(_queueDirectory, "*.*"))
            {
                if (IsSupportedReplayFileName(file))
                {
                    File.Delete(file);
                    var sidecarPath = GetReplaySidecarPath(file);
                    if (File.Exists(sidecarPath))
                    {
                        File.Delete(sidecarPath);
                    }
                }
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

            RequeueReplayNoLock(replay);
            RecalculateRunCountsNoLock();
            RedistributeQueuedReplayPlansNoLock();

            AddEventNoLock("Info", "Queue", "Replay requeued: " + CreateReplayLabel(replay), replay.Id);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState RequeueAllQueueItems()
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            var replays = _state.Queue
                .Where(IsRequeueableReplay)
                .ToList();
            if (replays.Count == 0)
            {
                return Clone(_state);
            }

            foreach (var replay in replays)
            {
                RequeueReplayNoLock(replay);
            }

            RecalculateRunCountsNoLock();
            RedistributeQueuedReplayPlansNoLock();

            AddEventNoLock(
                "Info",
                "Queue",
                "Requeued " + replays.Count + " replay" + (replays.Count == 1 ? "" : "s") + ".",
                null);
            SaveNoLock();
            return Clone(_state);
        }
    }

    public RecordingFileRenameResult RenameCompletedQueueRecordings(RecordingFileRenameRequest? request = null)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            EnsureRecordingFilesCanBeRenamedNoLock();
            return RenameRecordingFilesNoLock(
                _state.Queue
                    .Where(IsRecordedReplay)
                    .Select(CreateRecordingRenameTarget),
                NormalizeRecordingRenameFormat(request?.Format),
                "completed queue recording",
                "Queue");
        }
    }

    public RecordingFileRenameResult RenameCollectionRecordings(string id, RecordingFileRenameRequest? request = null)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            EnsureRecordingFilesCanBeRenamedNoLock();
            var collection = FindMapCollectionNoLock(id);
            return RenameRecordingFilesNoLock(
                collection.Items
                    .Where(IsRecordedCollectionItem)
                    .Select(CreateRecordingRenameTarget),
                NormalizeRecordingRenameFormat(request?.Format),
                "collection recording",
                "Collections");
        }
    }

    public RecordingFileRenamePreviewResult GetCompletedQueueRecordingNamePreview()
    {
        lock (_sync)
        {
            var target =
                _state.Queue
                    .Where(IsRecordedReplay)
                    .Select(CreateRecordingRenameTarget)
                    .FirstOrDefault() ??
                _state.Queue
                    .Select(CreateRecordingRenameTarget)
                    .FirstOrDefault();

            return CreateRecordingRenamePreviewNoLock(target);
        }
    }

    public RecordingFileRenamePreviewResult GetCollectionRecordingNamePreview(string id)
    {
        lock (_sync)
        {
            var collection = FindMapCollectionNoLock(id);
            var target =
                collection.Items
                    .Where(IsRecordedCollectionItem)
                    .Select(CreateRecordingRenameTarget)
                    .FirstOrDefault() ??
                collection.Items
                    .Select(CreateRecordingRenameTarget)
                    .FirstOrDefault();

            return CreateRecordingRenamePreviewNoLock(target);
        }
    }

    private void EnsureRecordingFilesCanBeRenamedNoLock()
    {
        if (_state.Run.IsRunning ||
            _state.Run.CancellationRequested ||
            _state.Queue.Any(replay => IsActiveReplayStatus(replay.Status)))
        {
            throw new InvalidOperationException("Stop the active queue before renaming recording files.");
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

    public ControlPanelState CheckCapturePreflight()
    {
        lock (_sync)
        {
            _state.Settings.Normalize();
            RunCapturePreflightNoLock();
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState StartRun(StartRunRequest? request = null)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            ExpireStaleWorkersNoLock(now);
            if (_state.Benchmark.IsRunning || _state.Benchmark.CancellationRequested)
            {
                throw new InvalidOperationException("Stop the active benchmark before starting the queue.");
            }

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
            ValidateReplayProviderReadinessForRunNoLock(now);
            ValidateAudioSettingsForRunNoLock();
            ValidateRecorderHostsForRunNoLock();
            ValidateCapturePreflightForRunNoLock();
            ApplyRecordingDisplayScaleNoLock();
            ApplyTaskbarVisibilityForRunNoLock();

            var collectionName = NormalizeNullable(request?.CollectionName) ??
                                 ResolveCurrentQueueCollectionNameNoLock();
            var runOutputDirectory = CreateRunRecordingOutputDirectoryNoLock(now, collectionName);
            Directory.CreateDirectory(runOutputDirectory);

            ResetAssignmentsNoLock();
            _state.Run.IsRunning = true;
            _state.Run.CancellationRequested = false;
            _state.Run.CancellationReason = null;
            _state.Run.StartedAtUtc = now;
            _state.Run.FinishedAtUtc = null;
            _state.Run.RecordingOutputDirectory = runOutputDirectory;
            _state.Run.CollectionName = collectionName ?? "";
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

            AddEventNoLock(
                "Info",
                "Run",
                "Run started with " + _state.Queue.Count + " replay" + (_state.Queue.Count == 1 ? "" : "s") +
                " in " + Path.GetFileName(runOutputDirectory) + ".");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState StopRun()
    {
        lock (_sync)
        {
            EnsureInstancesNoLock();
            _state.Run.ForceStopCommandId++;
            _state.Run.CloseGamesWhenFinishedRequested = false;
            RequestRunCancellationNoLock("Stopped by operator.", failQueued: false);
            TryFinalizeCanceledRunNoLock(DateTimeOffset.UtcNow);
            AddEventNoLock("Warn", "Run", "Run stop requested.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState StartBenchmark(BenchmarkStartRequest? request = null)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            ExpireStaleWorkersNoLock(now);
            RefreshLaunchProcessesNoLock();
            EnsureInstancesNoLock();

            if (_state.Run.IsRunning || _state.Run.CancellationRequested)
            {
                throw new InvalidOperationException("Stop the current run before starting a benchmark.");
            }

            if (_state.Benchmark.IsRunning || _state.Benchmark.CancellationRequested)
            {
                throw new InvalidOperationException("A benchmark is already running.");
            }

            RefreshQueueMapAvailabilityNoLock(allowDownload: false);
            var sourceReplays = GetBenchmarkSourceReplaysNoLock();
            if (sourceReplays.Count == 0)
            {
                throw new InvalidOperationException(
                    "No benchmark-ready queue replays were found. Import at least one replay with a local replay file and an available song folder.");
            }

            var readyInstances = GetBenchmarkReadyInstancesNoLock(now);
            if (readyInstances.Count == 0)
            {
                throw new InvalidOperationException("No enabled online workers are available for benchmarking.");
            }

            var maxConcurrency = Math.Min(BenchmarkMaximumConcurrency, readyInstances.Count);
            var selectedConcurrencies = NormalizeBenchmarkConcurrencyLevels(request?.ConcurrencyLevels, maxConcurrency);
            ValidateInstanceBaselineForRunNoLock();
            ValidateReplayProviderReadinessForBenchmarkNoLock(sourceReplays, readyInstances, now);
            ValidateAudioSettingsForBenchmarkNoLock(readyInstances);
            ValidateRecorderHostsForBenchmarkNoLock(readyInstances);
            ValidateCapturePreflightForRunNoLock();

            var runId = "benchmark-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var outputDirectory = Path.Combine(Path.GetDirectoryName(_statePath)!, "Benchmarks", runId);
            Directory.CreateDirectory(outputDirectory);

            _state.Benchmark = new BenchmarkState
            {
                IsRunning = true,
                CancellationRequested = false,
                Status = "Running",
                RunId = runId,
                StartedAtUtc = now,
                FinishedAtUtc = null,
                ActiveConcurrency = 0,
                MaxConcurrency = maxConcurrency,
                SelectedConcurrencies = selectedConcurrencies,
                RecommendedWorkerCount = null,
                FailureReason = "",
                OutputDirectory = outputDirectory,
                ReportPath = Path.Combine(outputDirectory, "benchmark-report.json"),
                SourceQueueItemIds = sourceReplays.Select(replay => replay.Id).ToList(),
                SettingsSnapshot = CreateBenchmarkSettingsSnapshotNoLock(readyInstances.Count)
            };

            StartNextBenchmarkPassNoLock(now);
            AddEventNoLock(
                "Info",
                "Benchmark",
                "Benchmark started with " + sourceReplays.Count + " source replay" +
                (sourceReplays.Count == 1 ? "" : "s") +
                " and " + selectedConcurrencies.Count + " selected pass" +
                (selectedConcurrencies.Count == 1 ? "" : "es") + ".");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState StopBenchmark()
    {
        lock (_sync)
        {
            EnsureInstancesNoLock();
            var now = DateTimeOffset.UtcNow;
            if (!_state.Benchmark.IsRunning && !_state.Benchmark.CancellationRequested)
            {
                return Clone(_state);
            }

            _state.Run.ForceStopCommandId++;
            _state.Benchmark.IsRunning = false;
            _state.Benchmark.CancellationRequested = true;
            _state.Benchmark.Status = "Stopping";
            _state.Benchmark.FailureReason = "Stopped by operator.";

            foreach (var assignment in EnumerateActiveBenchmarkAssignmentsNoLock())
            {
                if (string.Equals(assignment.Status, "Queued", StringComparison.OrdinalIgnoreCase))
                {
                    assignment.Status = "Stopped";
                    assignment.Error = "Stopped by operator.";
                    assignment.CompletedAtUtc = now;
                }
            }

            TryAdvanceBenchmarkNoLock(now);
            AddEventNoLock("Warn", "Benchmark", "Benchmark stop requested.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public ControlPanelState SetCloseGamesWhenFinished(bool enabled)
    {
        lock (_sync)
        {
            EnsureInstancesNoLock();
            _state.Run.CloseGamesWhenFinishedRequested = enabled;
            AddEventNoLock(
                enabled ? "Info" : "Warn",
                "Run",
                enabled
                    ? "Managed games will close when the queue finishes."
                    : "Close games after queue was canceled.");
            SaveNoLock();
            return Clone(_state);
        }
    }

    public bool TryRequestIdleShutdown(DateTimeOffset now, TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            return false;
        }

        lock (_sync)
        {
            EnsureInstancesNoLock();
            var changed = ExpireStaleWorkersNoLock(now);
            changed |= RefreshLaunchProcessesNoLock();
            changed |= NormalizeRunSummaryNoLock();
            if (changed)
            {
                SaveNoLock();
            }

            if (IsActiveForIdleShutdownNoLock() ||
                now - _state.LastActivityUtc < idleTimeout)
            {
                return false;
            }

            AddEventNoLock(
                "Warn",
                "Idle",
                "No recorder activity for " + FormatIdleTimeout(idleTimeout) + "; stopping the recorder stack.",
                nowOverride: now);
            SaveNoLock();
            return true;
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
                    var replacePartialDirectory = Directory.Exists(target.Directory);
                    CopyManagedInstanceDirectory(
                        sourceDirectory,
                        target.Directory,
                        overwriteExisting: replacePartialDirectory,
                        copyExistingSongs: false,
                        skipManagedSharedFolders: true);
                    records.Add(CreateProvisionRecord(
                        target,
                        "Copied",
                        (replacePartialDirectory
                            ? "Recreated partial folder from "
                            : "Copied from ") + baseline.Name + ", excluding shared folders already managed by the recorder."));
                }
            }
            else
            {
                sourceDirectory = ResolveProvisionSourceDirectory(request.SourceBeatSaberPath);
                var sourceStore = SetupSourcePathDetector.InferStoreFromDirectory(
                    sourceDirectory,
                    request.SourceBeatSaberStore);
                _state.Settings.SourceBeatSaberPath = sourceDirectory;
                _state.Settings.SourceBeatSaberStore = sourceStore;
                ValidateProvisionSourceAndTargets(sourceDirectory, targetRootDirectory, targets, request.OverwriteExisting);

                CopyManagedInstanceDirectory(
                    sourceDirectory,
                    baseline.Directory,
                    request.OverwriteExisting,
                    copyExistingSongs);
                foreach (var instance in _state.Instances)
                {
                    instance.SourceBeatSaberStore = sourceStore;
                }
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

            var baselineStore = SetupSourcePathDetector.InferStoreFromDirectory(
                baseline.Directory,
                _state.Instances.FirstOrDefault(instance => instance.Index == baseline.Index)?.SourceBeatSaberStore);
            foreach (var instance in _state.Instances.Where(instance =>
                         File.Exists(Path.Combine(instance.LaunchDirectory, BeatSaberExecutableName))))
            {
                if (BeatSaberStore.Normalize(instance.SourceBeatSaberStore) == BeatSaberStore.Unknown)
                {
                    instance.SourceBeatSaberStore = baselineStore;
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

            if (!FindBeatSaberProcessIdForInstance(instance).HasValue)
            {
                _workerPluginInstaller.Install(_state.Instances, _state.Settings, new[] { instance });
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

    public ControlPanelState QuitAllInstances()
    {
        lock (_sync)
        {
            if (_state.Run.IsRunning || _state.Run.CancellationRequested)
            {
                throw new InvalidOperationException("Stop the current run or use close after queue before closing all games.");
            }

            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            RefreshLaunchProcessesNoLock();
            EnsureInstancesNoLock();
            var result = CloseAllGamesNoLock();
            AddCloseAllGamesEventNoLock("Close requested for all games", result);
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

    public ControlPanelState SetActiveInstanceCount(int count)
    {
        lock (_sync)
        {
            ExpireStaleWorkersNoLock(DateTimeOffset.UtcNow);
            EnsureInstancesNoLock();

            if (count < ControlPanelSettings.MinimumManagedInstanceCount ||
                count > ControlPanelSettings.MaximumManagedInstanceCount)
            {
                throw new InvalidOperationException(
                    "Active instance count must be between " +
                    ControlPanelSettings.MinimumManagedInstanceCount +
                    " and " +
                    ControlPanelSettings.MaximumManagedInstanceCount +
                    ".");
            }

            var availableCount = GetAvailableManagedInstanceCountNoLock();
            if (count > availableCount)
            {
                throw new InvalidOperationException(
                    "Only " + availableCount + " managed instance" +
                    (availableCount == 1 ? " is" : "s are") +
                    " available. Create the missing instance before enabling " + count + " active lanes.");
            }

            if (count > _state.Settings.InstanceCount)
            {
                _state.Settings.InstanceCount = count;
                _state.Settings.Normalize();
                EnsureInstancesNoLock();
            }

            var configured = GetConfiguredInstancesNoLock();
            foreach (var instance in configured.Where(instance => instance.Index >= count && instance.Enabled))
            {
                var activeReplay = FindActiveReplayNoLock(instance);
                if (activeReplay != null || IsActiveReplayStatus(instance.Status) || IsRecordingStatus(instance.Status))
                {
                    throw new InvalidOperationException("Cannot disable " + instance.Name + " while it is recording.");
                }
            }

            var changed = false;
            foreach (var instance in configured)
            {
                var shouldEnable = instance.Index < count;
                if (instance.Enabled == shouldEnable)
                {
                    continue;
                }

                instance.Enabled = shouldEnable;
                changed = true;
                if (!shouldEnable)
                {
                    ReleaseActiveAssignmentNoLock(instance, "Queued");
                    instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
                }
            }

            RedistributeQueuedReplayPlansNoLock();
            SynchronizeMaxConcurrentRecordingsNoLock();
            RefreshInstanceProvisionCountsNoLock();
            if (changed)
            {
                AddEventNoLock(
                    "Info",
                    "Instance",
                    "Active recording instances set to " + count + ". Queue plan redistributed.");
            }

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
            var instances = GetRunInstancesNoLock().ToList();
            var deployTargets = instances
                .Where(instance => !FindBeatSaberProcessIdForInstance(instance).HasValue)
                .ToList();
            _workerPluginInstaller.Install(_state.Instances, _state.Settings, deployTargets);
            foreach (var instance in instances)
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
            requestedWorkerId = NormalizeRequestedManagedWorkerIdNoLock(request, requestedWorkerId, now);
            var instance = FindRegistrationSlotNoLock(request, requestedWorkerId, now);
            var assignedWorkerId = requestedWorkerId ?? CreateWorkerId();
            var replacingWorker = !string.Equals(instance.WorkerId, assignedWorkerId, StringComparison.OrdinalIgnoreCase);

            if (replacingWorker)
            {
                ReleaseActiveAssignmentNoLock(instance, "Queued");
                instance.RegisteredAtUtc = now;
                instance.LastReportedFramesPerSecond = null;
                instance.LastForceStopCommandId = _state.Run.ForceStopCommandId;
                instance.AppliedGamePresentationSettingsVersion = 0;
                instance.GamePresentationSyncStatus = "Pending";
                instance.GamePresentationSyncError = "";
                ResetReplayProviderStatusNoLock(instance);
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
            UpdateReplayProviderStatusNoLock(
                instance,
                request.ReplayProviderStatusReported,
                request.BeatLeaderReady,
                request.BeatLeaderStatus,
                request.ScoreSaberReady,
                request.ScoreSaberStatus);

            var workerName = NormalizeNullable(request.WorkerName);
            if (workerName != null)
            {
                instance.Name = NormalizeManagedInstanceName(workerName, instance.Index);
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
            KeepTaskbarHiddenDuringRunNoLock();
            instance.Status = NormalizeStatus(request.Status, "Online");
            instance.CurrentReplayId = NormalizeNullable(request.CurrentReplayId);
            instance.LastReportedFramesPerSecond = NormalizeFramesPerSecond(request.FramesPerSecond);
            instance.LastReportedAverageFramesPerSecond = NormalizeFramesPerSecond(request.AverageFramesPerSecond);
            instance.LastReportedFrameSampleCount = Math.Max(0, request.SampledFrameCount);
            if (request.AppliedGamePresentationSettingsVersion > 0)
            {
                instance.AppliedGamePresentationSettingsVersion = request.AppliedGamePresentationSettingsVersion;
            }

            instance.GamePresentationSyncStatus =
                NormalizeNullable(request.GamePresentationSyncStatus) ?? instance.GamePresentationSyncStatus;
            instance.GamePresentationSyncError = NormalizeNullable(request.GamePresentationSyncError) ?? "";
            UpdateReplayProviderStatusNoLock(
                instance,
                request.ReplayProviderStatusReported,
                request.BeatLeaderReady,
                request.BeatLeaderStatus,
                request.ScoreSaberReady,
                request.ScoreSaberStatus);

            var replay = FindActiveReplayNoLock(instance);
            var benchmarkAssignment = FindActiveBenchmarkAssignmentNoLock(instance);
            if (replay != null && IsRecordingStatus(instance.Status))
            {
                if (!string.Equals(replay.Status, "Recording", StringComparison.OrdinalIgnoreCase) ||
                    !replay.RecordingStartedAtUtc.HasValue)
                {
                    replay.RecordingStartedAtUtc = now;
                }

                replay.Status = "Recording";
            }

            if (benchmarkAssignment != null)
            {
                UpdateBenchmarkAssignmentHeartbeatNoLock(benchmarkAssignment, instance, now);
            }

            var shouldOpenPauseMenu = _state.Run.ForceStopCommandId > instance.LastForceStopCommandId;
            if (shouldOpenPauseMenu)
            {
                instance.LastForceStopCommandId = _state.Run.ForceStopCommandId;
            }

            var lagSpikeCancellationReason = replay == null
                ? null
                : CreateHeartbeatLagSpikeCancellationReasonNoLock(
                    instance,
                    replay,
                    now,
                    _state.Settings.LagSpikeStartupGraceSeconds);
            var benchmarkLagSpikeCancellationReason = CreateBenchmarkHeartbeatLagSpikeCancellationReasonNoLock(
                instance,
                benchmarkAssignment,
                now);
            var benchmarkCancellationReason = _state.Benchmark.CancellationRequested && benchmarkAssignment != null
                ? NormalizeNullable(_state.Benchmark.FailureReason) ?? "Benchmark stopped by operator."
                : null;
            SaveNoLock();
            return new WorkerHeartbeatResponse
            {
                ShouldCancelAssignment =
                    lagSpikeCancellationReason != null ||
                    benchmarkLagSpikeCancellationReason != null ||
                    benchmarkCancellationReason != null ||
                    (_state.Run.CancellationRequested &&
                     (!string.IsNullOrWhiteSpace(instance.ActiveAssignmentId) ||
                      !string.IsNullOrWhiteSpace(request.CurrentReplayId))),
                CancellationReason =
                    lagSpikeCancellationReason ??
                    benchmarkLagSpikeCancellationReason ??
                    benchmarkCancellationReason ??
                    _state.Run.CancellationReason,
                CancellationFailsAssignment =
                    lagSpikeCancellationReason != null ||
                    benchmarkLagSpikeCancellationReason != null,
                ShouldOpenPauseMenu = shouldOpenPauseMenu,
                GamePresentationSettingsVersion = _state.Settings.GamePresentationSettingsVersion,
                GamePresentation = CloneGamePresentationSettings(_state.Settings.GamePresentation),
                Progress = CreateWorkerProgressNoLock()
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
            KeepTaskbarHiddenDuringRunNoLock();

            if (!instance.Enabled)
            {
                ReleaseActiveAssignmentNoLock(instance, "Queued");
                instance.Status = "Online";
                RedistributeQueuedReplayPlansNoLock();
                SaveNoLock();
                return CreateEmptyAssignment(instance);
            }

            var activeBenchmarkAssignment = FindActiveBenchmarkAssignmentNoLock(instance);
            if (activeBenchmarkAssignment != null)
            {
                instance.Status = "Assigned";
                SaveNoLock();
                return CreateBenchmarkAssignmentResponse(activeBenchmarkAssignment, instance);
            }

            if (_state.Benchmark.IsRunning && !_state.Benchmark.CancellationRequested)
            {
                var benchmarkAssignment = FindQueuedBenchmarkAssignmentForInstanceNoLock(instance);
                if (benchmarkAssignment != null)
                {
                    benchmarkAssignment.Status = "Assigned";
                    benchmarkAssignment.AssignedAtUtc = now;
                    benchmarkAssignment.WorkerId = instance.WorkerId ?? "";
                    benchmarkAssignment.InstanceName = CreateManagedInstanceName(instance.Index);
                    instance.ActiveAssignmentId = benchmarkAssignment.AssignmentId;
                    instance.CurrentReplayId = benchmarkAssignment.SourceReplayId;
                    instance.Status = "Assigned";
                    ResetRecordingLagSpikeTrackerNoLock(instance);

                    AddEventNoLock(
                        "Info",
                        "Benchmark",
                        "Benchmark recording started: " + benchmarkAssignment.ReplayLabel +
                        " on " + CreateManagedInstanceName(instance.Index),
                        benchmarkAssignment.SourceReplayId,
                        instance.Index);
                    SaveNoLock();
                    return CreateBenchmarkAssignmentResponse(benchmarkAssignment, instance);
                }
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
            replay.RecordingStartedAtUtc = null;
            replay.CompletedAtUtc = null;
            replay.AssignedInstance = instance.Index;
            replay.Status = "Assigned";
            replay.OutputPath = null;
            replay.Error = null;
            replay.Warning = null;

            instance.ActiveAssignmentId = assignmentId;
            instance.CurrentReplayId = replay.Id;
            instance.Status = "Assigned";
            ResetRecordingLagSpikeTrackerNoLock(instance);

            AddEventNoLock(
                "Info",
                "Run",
                "Recording started: " + CreateReplayLabel(replay) + " on " + CreateManagedInstanceName(instance.Index),
                replay.Id,
                instance.Index);
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
            var status = NormalizeReplayStatus(request.Status);
            instance.LastHeartbeatUtc = now;

            var benchmarkAssignment = FindBenchmarkAssignmentByIdNoLock(request.AssignmentId);
            if (benchmarkAssignment != null)
            {
                ReportBenchmarkAssignmentNoLock(request, status, instance, benchmarkAssignment, now);
                SaveNoLock();
                return Clone(_state);
            }

            var replay = _state.Queue.FirstOrDefault(item =>
                string.Equals(item.AssignmentId, request.AssignmentId, StringComparison.OrdinalIgnoreCase));
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
                var warning = NormalizeNullable(request.Warning);
                var verified = audioVerification.HasAudio && syncVerification.Verified;
                var chapterEmbedding = verified
                    ? TryEmbedRecordingChaptersNoLock(replay, outputPath)
                    : RecordingChapterEmbedResult.Skip();
                if (!chapterEmbedding.Succeeded)
                {
                    warning = CombineWarnings(
                        warning,
                        "Bookmark chapters were not embedded: " + chapterEmbedding.Error);
                }

                replay.Status = verified ? "Completed" : "Failed";
                replay.OutputPath = outputPath;
                replay.Error = audioVerification.HasAudio
                    ? syncVerification.Error
                    : audioVerification.Error;
                replay.Warning = warning;
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
                if (replay.Status == "Completed")
                {
                    UpdateMatchingCollectionCompletionNoLock(replay, now);
                }
            }
            else if (status == "Failed" || status == "Stopped")
            {
                reportedAssignmentFailure = status == "Failed";
                replay.Status = "Failed";
                replay.OutputPath = NormalizeNullable(request.OutputPath);
                replay.Error = NormalizeNullable(request.Error) ?? "Worker reported " + status.ToLowerInvariant() + ".";
                replay.Warning = null;
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
                replay.Warning = NormalizeNullable(request.Warning);
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

    private RecordingChapterEmbedResult TryEmbedRecordingChaptersNoLock(ReplayQueueRecord replay, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return RecordingChapterEmbedResult.Skip("Completed recording did not include an output path.");
        }

        try
        {
            var levelDirectory = ResolveLevelDirectoryNoLock(replay);
            if (string.IsNullOrWhiteSpace(levelDirectory))
            {
                return RecordingChapterEmbedResult.Skip("No matching song folder was found for bookmark chapters.");
            }

            var bookmarks = BeatmapBookmarkReader.TryRead(levelDirectory, replay.Difficulty, replay.Mode);
            if (bookmarks.Count == 0)
            {
                return RecordingChapterEmbedResult.Skip("No beatmap bookmarks were found.");
            }

            var replayInfo = ReadReplayInfoForChapters(replay);
            var chapters = RecordingChapterPlanner.Create(bookmarks, replay, replayInfo);
            if (chapters.Count == 0)
            {
                return RecordingChapterEmbedResult.Skip("Beatmap bookmarks were outside the recorded replay range.");
            }

            var result = _recordingChapterEmbedder.Embed(outputPath, chapters);
            if (result.Succeeded && !result.Skipped)
            {
                AddEventNoLock(
                    "Good",
                    "Chapters",
                    "Embedded " + result.ChapterCount + " bookmark chapter" + (result.ChapterCount == 1 ? "" : "s") +
                    ": " + CreateReplayLabel(replay),
                    replay.Id);
            }
            else if (!result.Succeeded)
            {
                AddEventNoLock(
                    "Warn",
                    "Chapters",
                    "Bookmark chapters were not embedded for " + CreateReplayLabel(replay) + ": " + result.Error,
                    replay.Id);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var error = "bookmark chapter planning failed: " + ex.Message;
            AddEventNoLock(
                "Warn",
                "Chapters",
                "Bookmark chapters were not embedded for " + CreateReplayLabel(replay) + ": " + error,
                replay.Id);
            return RecordingChapterEmbedResult.Failure(error);
        }
    }

    private static BsorInfo ReadReplayInfoForChapters(ReplayQueueRecord replay)
    {
        if (!string.IsNullOrWhiteSpace(replay.Path) && File.Exists(replay.Path))
        {
            if (IsScoreSaberReplayForChapters(replay))
            {
                try
                {
                    return new ScoreSaberReplayInfoReader().Read(replay.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                {
                    return CreateReplayInfoFallback(replay);
                }
            }

            try
            {
                return new BsorInfoReader().Read(replay.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                return CreateReplayInfoFallback(replay);
            }
        }

        return CreateReplayInfoFallback(replay);
    }

    private static BsorInfo CreateReplayInfoFallback(ReplayQueueRecord replay)
    {
        return new BsorInfo
        {
            Speed = 1,
            LastFrameTime = (float)Math.Max(0, replay.EstimatedSeconds)
        };
    }

    private static bool IsScoreSaberReplayForChapters(ReplayQueueRecord replay)
    {
        return string.Equals(Path.GetExtension(replay.Path), ".dat", StringComparison.OrdinalIgnoreCase) ||
               replay.ReplayFormat.Contains("ScoreSaber", StringComparison.OrdinalIgnoreCase);
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
        if (string.IsNullOrWhiteSpace(state.Settings.SourceBeatSaberPath))
        {
            state.Settings.SourceBeatSaberPath = settings.SourceBeatSaberPath;
        }

        state.Settings.LagSpikeStartupGraceSeconds = settings.LagSpikeStartupGraceSeconds;
        state.Settings.Normalize();
        state.Queue ??= new List<ReplayQueueRecord>();
        state.Collections ??= new List<MapCollectionRecord>();
        state.Instances ??= new List<WorkerInstanceRecord>();
        foreach (var instance in state.Instances)
        {
            instance.SourceBeatSaberStore = SetupSourcePathDetector.InferStoreFromDirectory(
                instance.LaunchDirectory,
                instance.SourceBeatSaberStore);
        }
        state.InstanceProvision ??= new InstanceProvisionReport();
        state.InstanceBaseline ??= new InstanceBaselineReport();
        state.SongFolders ??= new SongFolderLinkReport();
        state.DiskSpace ??= new DiskSpaceReport();
        state.Events ??= new List<ControlPanelEventRecord>();
        state.Run ??= new RunState();
        NormalizeLoadedRunState(state);
        foreach (var replay in state.Queue)
        {
            replay.Calibration ??= new ReplayCalibrationRecord();
            NormalizeReplayProviderFields(replay);
        }

        foreach (var collection in state.Collections)
        {
            collection.Items ??= new List<MapCollectionItemRecord>();
            for (var index = 0; index < collection.Items.Count; index++)
            {
                var item = collection.Items[index];
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    item.Id = CreateStableId(collection.Id + ":" + index.ToString(CultureInfo.InvariantCulture));
                }

                if (item.SequenceNumber < 1)
                {
                    item.SequenceNumber = index + 1;
                }

                NormalizeMapCollectionItemProviderFields(item);
                item.MapCardCategory = NormalizeMapCardCategory(item.MapCardCategory);
            }
        }

        return state;
    }

    private static void NormalizeLoadedRunState(ControlPanelState state)
    {
        if (!state.Run.CancellationRequested)
        {
            return;
        }

        var hasActiveAssignment = state.Instances.Any(instance =>
            !string.IsNullOrWhiteSpace(instance.ActiveAssignmentId));
        if (hasActiveAssignment)
        {
            return;
        }

        state.Run.IsRunning = false;
        state.Run.CancellationRequested = false;
        state.Run.FinishedAtUtc ??= DateTimeOffset.UtcNow;
        state.Run.Status = state.Run.FailedCount > 0 ? "Stopped with errors" : "Stopped";
        foreach (var instance in state.Instances)
        {
            instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
        }
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
            var sidecar = ReadReplaySidecar(item.ReplayPath) ??
                          TryReadOrDownloadScoreSaberReplayMetadata(
                              item.ReplayPath,
                              item.ScoreId,
                              item.ReplayInfo.PlayerId,
                              item.ReplayInfo.PlayerName,
                              item.ReplayInfo.LevelHash);
            var isMetadataEdited = existing?.IsMetadataEdited == true;
            var recordId = existing?.Id ?? CreateStableId(fullPath);
            records.Add(new ReplayQueueRecord
            {
                Id = recordId,
                SequenceNumber = item.SequenceNumber,
                Provider = ResolveProvider(existing, sidecar, item),
                ReferenceKind = ResolveReferenceKind(existing, sidecar, item),
                ReplayFormat = ResolveReplayFormat(existing, sidecar, item),
                SourceUrl = existing?.SourceUrl ?? sidecar?.SourceUrl ?? item.SourceUrl ?? "",
                ScoreId = existing?.ScoreId ?? sidecar?.ScoreId ?? item.ScoreId ?? "",
                FileName = Path.GetFileName(item.ReplayPath),
                Path = item.ReplayPath,
                SongName = isMetadataEdited ? existing!.SongName : Prefer(sidecar?.SongName, item.ReplayInfo.SongName),
                Mapper = isMetadataEdited ? existing!.Mapper : Prefer(sidecar?.Mapper, item.ReplayInfo.Mapper),
                PlayerName = Prefer(sidecar?.PlayerName, ResolveReplayPlayerName(item.ReplayInfo.PlayerName, item.ReplayInfo.PlayerId, item.ReplayPath)),
                Difficulty = isMetadataEdited ? existing!.Difficulty : Prefer(sidecar?.Difficulty, item.ReplayInfo.Difficulty),
                Mode = isMetadataEdited ? existing!.Mode : Prefer(sidecar?.Mode, item.ReplayInfo.Mode),
                LevelHash = Prefer(sidecar?.LevelHash, item.ReplayInfo.LevelHash),
                CoverArtUrl = "/api/queue/" + Uri.EscapeDataString(recordId) + "/cover",
                MapStatus = existing?.MapStatus ?? "Unchecked",
                MapStatusDetail = existing?.MapStatusDetail ?? "",
                MapInstallPath = existing?.MapInstallPath ?? "",
                EstimatedSeconds = isMetadataEdited
                    ? existing!.EstimatedSeconds
                    : Math.Round(ResolveEstimatedSeconds(sidecar, item.EstimatedPlaybackLength.TotalSeconds), 2),
                Status = existing?.Status ?? "Queued",
                AssignedInstance = existing?.AssignedInstance,
                AssignmentId = existing?.AssignmentId,
                AssignedAtUtc = existing?.AssignedAtUtc,
                RecordingStartedAtUtc = existing?.RecordingStartedAtUtc,
                CompletedAtUtc = existing?.CompletedAtUtc,
                OutputPath = existing?.OutputPath,
                Error = existing?.Error,
                Warning = existing?.Warning,
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

    private ReplayQueueRecord CreateReplayRecordFromQueueItemNoLock(ReplayQueueItem item)
    {
        var sidecar = ReadReplaySidecar(item.ReplayPath) ??
                      TryReadOrDownloadScoreSaberReplayMetadata(
                          item.ReplayPath,
                          item.ScoreId,
                          item.ReplayInfo.PlayerId,
                          item.ReplayInfo.PlayerName,
                          item.ReplayInfo.LevelHash);
        var recordId = CreateStableId(Path.GetFullPath(item.ReplayPath));
        return new ReplayQueueRecord
        {
            Id = recordId,
            SequenceNumber = item.SequenceNumber,
            Provider = ResolveProvider(null, sidecar, item),
            ReferenceKind = ResolveReferenceKind(null, sidecar, item),
            ReplayFormat = ResolveReplayFormat(null, sidecar, item),
            SourceUrl = sidecar?.SourceUrl ?? item.SourceUrl ?? "",
            ScoreId = sidecar?.ScoreId ?? item.ScoreId ?? "",
            FileName = Path.GetFileName(item.ReplayPath),
            Path = item.ReplayPath,
            SongName = Prefer(sidecar?.SongName, item.ReplayInfo.SongName),
            Mapper = Prefer(sidecar?.Mapper, item.ReplayInfo.Mapper),
            PlayerName = Prefer(sidecar?.PlayerName, ResolveReplayPlayerName(
                item.ReplayInfo.PlayerName,
                item.ReplayInfo.PlayerId,
                item.ReplayPath)),
            Difficulty = Prefer(sidecar?.Difficulty, item.ReplayInfo.Difficulty),
            Mode = Prefer(sidecar?.Mode, item.ReplayInfo.Mode),
            LevelHash = Prefer(sidecar?.LevelHash, item.ReplayInfo.LevelHash),
            CoverArtUrl = "",
            MapStatus = "Unchecked",
            EstimatedSeconds = Math.Round(ResolveEstimatedSeconds(sidecar, item.EstimatedPlaybackLength.TotalSeconds), 2),
            Status = "Queued",
            Calibration = new ReplayCalibrationRecord()
        };
    }

    private bool RefreshQueueMetadataNoLock()
    {
        var changed = false;
        var bsorReader = new BsorInfoReader();
        var scoreSaberReader = new ScoreSaberReplayInfoReader();
        foreach (var replay in _state.Queue)
        {
            NormalizeReplayProviderFields(replay);
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
                var info = replay.Provider == ReplayProvider.ScoreSaber2
                    ? scoreSaberReader.Read(replay.Path)
                    : bsorReader.Read(replay.Path);
                var sidecar = ReadReplaySidecar(replay.Path) ??
                              TryReadOrDownloadScoreSaberReplayMetadata(replay.Path, replay.ScoreId, info.PlayerId, info.PlayerName, info.LevelHash);
                replay.LevelHash = Prefer(sidecar?.LevelHash, info.LevelHash);
                replay.PlayerName = Prefer(sidecar?.PlayerName, ResolveReplayPlayerName(info.PlayerName, info.PlayerId, replay.Path));
                replay.ScoreId = Prefer(replay.ScoreId, sidecar?.ScoreId);
                replay.SourceUrl = Prefer(replay.SourceUrl, sidecar?.SourceUrl);
                if (!replay.IsMetadataEdited)
                {
                    replay.SongName = Prefer(sidecar?.SongName, info.SongName);
                    replay.Mapper = Prefer(sidecar?.Mapper, info.Mapper);
                    replay.Difficulty = Prefer(sidecar?.Difficulty, info.Difficulty);
                    replay.Mode = Prefer(sidecar?.Mode, info.Mode);
                    replay.EstimatedSeconds = Math.Round(ResolveEstimatedSeconds(sidecar, info.EstimatedPlaybackLength.TotalSeconds), 2);
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

            if (string.IsNullOrWhiteSpace(replay.LevelHash))
            {
                changed |= SetQueueMapStatusNoLock(
                    replay,
                    "Missing",
                    "Replay metadata did not include a level hash.",
                    "");
                continue;
            }

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

            instance.Name = NormalizeManagedInstanceName(instance.Name, index);

            if (string.IsNullOrWhiteSpace(instance.RecorderHostUrl))
            {
                instance.RecorderHostUrl = "http://127.0.0.1:" + (5757 + index);
            }

            instance.OutputDirectory = outputDirectory;
            instance.LaunchDirectory = ResolveManagedLaunchDirectory(instance, index);
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

    private int GetAvailableManagedInstanceCountNoLock()
    {
        var availableCount = Math.Clamp(
            _state.Settings.InstanceCount,
            ControlPanelSettings.MinimumManagedInstanceCount,
            ControlPanelSettings.MaximumManagedInstanceCount);
        for (var index = 0; index < ControlPanelSettings.MaximumManagedInstanceCount; index++)
        {
            var instance = _state.Instances.FirstOrDefault(item => item.Index == index) ?? new WorkerInstanceRecord
            {
                Index = index,
                Name = CreateManagedInstanceName(index),
                RecorderHostUrl = "http://127.0.0.1:" + (5757 + index)
            };
            var launchDirectory = ResolveManagedLaunchDirectory(instance, index);
            if (IsManagedInstanceReady(launchDirectory))
            {
                availableCount = Math.Max(availableCount, index + 1);
            }
        }

        return availableCount;
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
        return IsManagedInstanceReady(instance.LaunchDirectory);
    }

    private static bool IsManagedInstanceReady(string? launchDirectory)
    {
        return !string.IsNullOrWhiteSpace(launchDirectory) &&
               File.Exists(Path.Combine(launchDirectory, BeatSaberExecutableName));
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

    private static void ResetReplayProviderStatusNoLock(WorkerInstanceRecord instance)
    {
        instance.ReplayProviderStatusReported = false;
        instance.BeatLeaderReady = false;
        instance.BeatLeaderStatus = "Not reported";
        instance.ScoreSaberReady = false;
        instance.ScoreSaberStatus = "Not reported";
    }

    private static void UpdateReplayProviderStatusNoLock(
        WorkerInstanceRecord instance,
        bool statusReported,
        bool beatLeaderReady,
        string? beatLeaderStatus,
        bool scoreSaberReady,
        string? scoreSaberStatus)
    {
        if (!statusReported)
        {
            return;
        }

        instance.ReplayProviderStatusReported = true;
        instance.BeatLeaderReady = beatLeaderReady;
        instance.BeatLeaderStatus = NormalizeNullable(beatLeaderStatus) ??
                                    (beatLeaderReady ? "Ready" : "Not ready");
        instance.ScoreSaberReady = scoreSaberReady;
        instance.ScoreSaberStatus = NormalizeNullable(scoreSaberStatus) ??
                                    (scoreSaberReady ? "Ready" : "Not ready");
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
        return ModIntegrationCatalog.CreateSharedFolderDefinitions(_state.Settings);
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

        var sourceReport = SetupSourcePathDetector.Detect(sourceDirectory);
        if (!sourceReport.ConfiguredSourceRecorderReady)
        {
            var missing = sourceReport.ConfiguredSourceMissingPrerequisites;
            throw new InvalidOperationException(
                "Selected Beat Saber source needs " +
                (missing.Count > 0 ? string.Join(" + ", missing) : "BSIPA and BeatLeader") +
                " before it can create a recorder worker.");
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

    private void CopyManagedInstanceDirectory(
        string sourceDirectory,
        string targetDirectory,
        bool overwriteExisting,
        bool copyExistingSongs)
    {
        CopyManagedInstanceDirectory(
            sourceDirectory,
            targetDirectory,
            overwriteExisting,
            copyExistingSongs,
            skipManagedSharedFolders: false);
    }

    private void CopyManagedInstanceDirectory(
        string sourceDirectory,
        string targetDirectory,
        bool overwriteExisting,
        bool copyExistingSongs,
        bool skipManagedSharedFolders)
    {
        var targetHadEntries = Directory.Exists(targetDirectory) &&
                               Directory.EnumerateFileSystemEntries(targetDirectory).Any();
        var cleanupPartialOnFailure = overwriteExisting || !targetHadEntries;
        if (Directory.Exists(targetDirectory))
        {
            if (Directory.EnumerateFileSystemEntries(targetDirectory).Any() && !overwriteExisting)
            {
                throw new InvalidOperationException("Target instance folder is not empty: " + targetDirectory);
            }

            if (overwriteExisting)
            {
                DeleteDirectoryWithoutFollowingReparsePoints(targetDirectory);
            }
        }

        try
        {
            var managedSharedFolderPaths = skipManagedSharedFolders
                ? CreateSharedFolderDefinitionsNoLock()
                    .Select(definition => NormalizeProvisionRelativePath(definition.InstanceRelativePath))
                    .Where(relativePath => !string.IsNullOrWhiteSpace(relativePath))
                    .ToList()
                : new List<string>();
            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory) ?? targetDirectory);
            CopyDirectory(
                sourceDirectory,
                targetDirectory,
                overwrite: true,
                relativePath => IsProvisionTransientRelativePath(relativePath) ||
                                IsPreviousSongLibraryBackupRelativePath(relativePath) ||
                                IsWorkerConflictingModRelativePath(relativePath) ||
                                (!copyExistingSongs && IsPreviousSongLibraryRelativePath(relativePath)) ||
                                (skipManagedSharedFolders && IsManagedSharedFolderRelativePath(relativePath, managedSharedFolderPaths)));
        }
        catch (Exception ex)
        {
            var cleanupNote = "";
            if (cleanupPartialOnFailure && Directory.Exists(targetDirectory))
            {
                try
                {
                    DeleteDirectoryWithoutFollowingReparsePoints(targetDirectory);
                }
                catch (Exception cleanupException)
                {
                    cleanupNote = " Setup could not remove the incomplete folder. Delete only '" + targetDirectory +
                                  "' before retrying. Cleanup details: " + cleanupException.Message;
                }
            }

            throw new InvalidOperationException(
                CreateManagedInstanceCopyFailureMessage(sourceDirectory, targetDirectory, ex) + cleanupNote,
                ex);
        }
    }

    private static string CreateManagedInstanceCopyFailureMessage(
        string sourceDirectory,
        string targetDirectory,
        Exception exception)
    {
        var detail = exception.Message;
        if (exception is UnauthorizedAccessException ||
            detail.Contains("access", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("used by another process", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("sharing violation", StringComparison.OrdinalIgnoreCase))
        {
            return "Could not copy Beat Saber from '" + sourceDirectory + "' to '" + targetDirectory +
                   "'. Close Beat Saber, BSManager, and File Explorer windows open in either folder, then retry setup. Details: " + detail;
        }

        if (detail.Contains("not enough space", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("disk full", StringComparison.OrdinalIgnoreCase))
        {
            return "Could not copy Beat Saber because the destination drive is out of space. Free space on the drive containing '" +
                   targetDirectory + "', then retry setup. Details: " + detail;
        }

        if (exception is PathTooLongException ||
            detail.Contains("path too long", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("path length", StringComparison.OrdinalIgnoreCase))
        {
            return "Could not copy Beat Saber because a path is too long. Move the extracted Replay Recorder folder closer to the drive root, such as C:\\Replay Recorder, then retry setup. Details: " + detail;
        }

        return "Could not copy Beat Saber from '" + sourceDirectory + "' to '" + targetDirectory +
               "'. Retry once, then send this message if it happens again. Details: " + detail;
    }

    private static bool IsManagedSharedFolderRelativePath(string relativePath, IReadOnlyList<string> managedSharedFolderPaths)
    {
        var normalized = NormalizeProvisionRelativePath(relativePath);
        foreach (var managedPath in managedSharedFolderPaths)
        {
            if (string.Equals(normalized, managedPath, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(managedPath + "/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(managedPath + ".local-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeProvisionRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').Trim('/');
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

    private string ResolveManagedLaunchDirectory(WorkerInstanceRecord instance, int index)
    {
        var targetRootDirectory = Path.GetFullPath(_state.Settings.BeatSaberInstancesRoot);
        var desiredDirectory = Path.GetFullPath(CreateLaunchDirectory(index));
        if (IsManagedInstanceReady(desiredDirectory))
        {
            return desiredDirectory;
        }

        var existingDirectory = NormalizeNullable(instance.LaunchDirectory);
        if (existingDirectory != null)
        {
            existingDirectory = Path.GetFullPath(existingDirectory);
            if (IsPathInsideDirectory(existingDirectory, targetRootDirectory) &&
                IsManagedInstanceReady(existingDirectory))
            {
                return existingDirectory;
            }
        }

        var legacyDirectory = Path.Combine(targetRootDirectory, "I-" + (index + 1));
        if (IsManagedInstanceReady(legacyDirectory))
        {
            return legacyDirectory;
        }

        if (!Directory.Exists(targetRootDirectory))
        {
            return desiredDirectory;
        }

        var suffix = (index + 1).ToString(CultureInfo.InvariantCulture);
        foreach (var candidate in Directory.EnumerateDirectories(targetRootDirectory).OrderBy(path => path))
        {
            var name = Path.GetFileName(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                IsManagedInstanceReady(candidate))
            {
                return candidate;
            }
        }

        return desiredDirectory;
    }

    private string CreateManagedInstanceName(int index)
    {
        return "Instance " + (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    private string NormalizeManagedInstanceName(string? name, int index)
    {
        var normalized = NormalizeNullable(name);
        if (normalized == null || IsGeneratedManagedInstanceName(normalized, index))
        {
            return CreateManagedInstanceName(index);
        }

        return normalized;
    }

    private bool IsGeneratedManagedInstanceName(string name, int index)
    {
        var displayIndex = (index + 1).ToString(CultureInfo.InvariantCulture);
        var candidates = new[]
        {
            CreateManagedInstanceName(index),
            "I-" + displayIndex,
            "BSARR I-" + displayIndex,
            _state.Settings.BeatSaberInstanceNamePrefix + displayIndex,
            "BSARR " + _state.Settings.BeatSaberInstanceNamePrefix + displayIndex
        };

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Any(candidate => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
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

        var store = SetupSourcePathDetector.InferStoreFromDirectory(
            launchDirectory,
            instance.SourceBeatSaberStore);
        if (store == BeatSaberStore.MetaPc && !IsMetaRuntimeAvailable())
        {
            SetLaunchFailureNoLock(
                instance,
                "Meta/Oculus PC Beat Saber needs Meta Quest Link running. Open the Meta Quest Link desktop app, sign in, then launch this worker again.");
            return;
        }

        if (store == BeatSaberStore.MetaPc && !MetaSideloadedApps.IsEnabled())
        {
            SetLaunchFailureNoLock(
                instance,
                "Meta/Oculus PC workers need Meta sideloaded apps enabled. Open Setup, approve the Meta sideloaded-apps request, then launch this worker again.");
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

            var placementPlan = CreateBeatSaberWindowPlacementPlan(
                _state.Settings.MonitorIndex,
                instance.Index,
                _state.Settings.BeatSaberLaunchArguments);
            ApplyBeatSaberWindowedRegistryState(_state.Settings.BeatSaberLaunchArguments, placementPlan);
            ApplyBeatSaberWindowedSettingsFile(
                GetBeatSaberSettingsFilePath(),
                _state.Settings.BeatSaberLaunchArguments);
            ApplyStoreLaunchEnvironment(startInfo, store);

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

    private static bool IsMetaRuntimeAvailable()
    {
        try
        {
            return Process.GetProcessesByName("OVRServer_x64").Length > 0 ||
                   Process.GetProcessesByName("OVRServiceLauncher").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    internal static void ApplyStoreLaunchEnvironment(ProcessStartInfo startInfo, string? store)
    {
        if (BeatSaberStore.Normalize(store) != BeatSaberStore.Steam)
        {
            return;
        }

        startInfo.Environment["SteamAppId"] = BeatSaberSteamAppId;
        startInfo.Environment["SteamOverlayGameId"] = BeatSaberSteamAppId;
        startInfo.Environment["SteamGameId"] = BeatSaberSteamAppId;
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
                if (!TryRequestGracefulProcessExit(process))
                {
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000))
                    {
                        throw new InvalidOperationException("Beat Saber did not exit within 5 seconds.");
                    }
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

    private static bool TryRequestGracefulProcessExit(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            if (!process.CloseMainWindow())
            {
                return false;
            }

            return process.WaitForExit(8000);
        }
        catch
        {
            return false;
        }
    }

    private (int ClosedCount, int ClearedCount, int FailedCount) CloseAllGamesNoLock()
    {
        var closedCount = 0;
        var clearedCount = 0;
        var failedCount = 0;
        foreach (var instance in _state.Instances.Where(ShouldCloseInstanceNoLock))
        {
            var hadKnownRuntime = HasKnownGameRuntime(instance);
            try
            {
                if (QuitInstanceProcessNoLock(instance))
                {
                    closedCount++;
                }
                else if (hadKnownRuntime)
                {
                    clearedCount++;
                }
            }
            catch (InvalidOperationException ex)
            {
                failedCount++;
                instance.GameLaunchStatus = "Failed";
                instance.GameLaunchError = ex.Message;
                AddEventNoLock("Bad", "Instance", ex.Message, instanceIndex: instance.Index);
            }
        }

        return (closedCount, clearedCount, failedCount);
    }

    private void TryCloseGamesWhenFinishedNoLock()
    {
        if (!_state.Run.CloseGamesWhenFinishedRequested)
        {
            return;
        }

        _state.Run.CloseGamesWhenFinishedRequested = false;
        var result = CloseAllGamesNoLock();
        AddCloseAllGamesEventNoLock("Queue finished; close requested for all games", result);
    }

    private void AddCloseAllGamesEventNoLock(
        string prefix,
        (int ClosedCount, int ClearedCount, int FailedCount) result)
    {
        var affectedCount = result.ClosedCount + result.ClearedCount;
        var text = prefix + ": " +
                   (affectedCount == 0
                       ? "no running games were found"
                       : affectedCount.ToString(CultureInfo.InvariantCulture) +
                         " game" +
                         (affectedCount == 1 ? "" : "s") +
                         " closed or cleared");
        if (result.FailedCount > 0)
        {
            text += "; " +
                    result.FailedCount.ToString(CultureInfo.InvariantCulture) +
                    " failed.";
        }
        else
        {
            text += ".";
        }

        AddEventNoLock(
            result.FailedCount > 0 ? "Bad" : affectedCount > 0 ? "Warn" : "Info",
            "Instance",
            text);
    }

    private static bool ShouldCloseInstanceNoLock(WorkerInstanceRecord instance)
    {
        return instance.Enabled ||
               instance.GameProcessId.HasValue ||
               !string.IsNullOrWhiteSpace(instance.WorkerId);
    }

    private static bool HasKnownGameRuntime(WorkerInstanceRecord instance)
    {
        return instance.GameProcessId.HasValue ||
               !string.IsNullOrWhiteSpace(instance.WorkerId) ||
               !string.IsNullOrWhiteSpace(instance.CurrentReplayId) ||
               !string.IsNullOrWhiteSpace(instance.ActiveAssignmentId);
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

    private void MarkInstanceLaunchFailedAfterExitNoLock(WorkerInstanceRecord instance, string message)
    {
        ReleaseActiveAssignmentNoLock(instance, "Queued");
        instance.WorkerId = null;
        instance.GameProcessId = null;
        instance.GameLaunchStatus = "Failed";
        instance.GameLaunchError = message;
        instance.AudioRoutingStatus = "Stopped";
        instance.AudioRoutingError = null;
        ResetInactiveInstanceIdentityNoLock(instance);
        instance.CurrentReplayId = null;
        instance.ActiveAssignmentId = null;
        instance.Status = "Idle";
        AddEventNoLock("Bad", "Launch", instance.Name + ": " + message, instanceIndex: instance.Index);
    }

    private void FailRunningInstanceLaunchNoLock(WorkerInstanceRecord instance, string message)
    {
        try
        {
            using var process = FindBeatSaberProcessForInstance(instance);
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                {
                    throw new InvalidOperationException("Beat Saber did not exit within 5 seconds.");
                }
            }

            MarkInstanceLaunchFailedAfterExitNoLock(instance, message);
        }
        catch (Exception ex)
        {
            var failureMessage = message + " Could not close the stuck game: " + ex.Message;
            instance.GameLaunchStatus = "Failed";
            instance.GameLaunchError = failureMessage;
            AddEventNoLock("Bad", "Launch", instance.Name + ": " + failureMessage, instanceIndex: instance.Index);
        }
    }

    private static NativeWindowPlacement.PlacementPlan? CreateBeatSaberWindowPlacementPlan(
        int monitorIndex,
        int instanceIndex,
        string launchArguments)
    {
        var monitor = NativeWindowPlacement.TryGetMonitorBounds(monitorIndex);
        if (!monitor.HasValue)
        {
            return null;
        }

        var (width, height) = GetLaunchResolution(launchArguments);
        return NativeWindowPlacement.CreateFixedSizePlacementPlan(
            monitor.Value,
            instanceIndex,
            width,
            height);
    }

    private static void ApplyBeatSaberWindowedRegistryState(
        string launchArguments,
        NativeWindowPlacement.PlacementPlan? placementPlan)
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
        if (placementPlan.HasValue)
        {
            SetDwordValue(key, "Screenmanager Window Position X_h4088080503", placementPlan.Value.Left);
            SetDwordValue(key, "Screenmanager Window Position Y_h4088080502", placementPlan.Value.Top);
        }
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

    internal static bool ApplyBeatSaberWindowedSettingsFile(string settingsPath, string launchArguments)
    {
        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
        {
            return false;
        }

        var (width, height) = GetLaunchResolution(launchArguments);
        var lines = File.ReadAllLines(settingsPath).ToList();
        var changed = false;
        changed |= SetIniValue(lines, "window.fullscreen", "false");
        changed |= SetIniValue(lines, "window.resolution.x", width.ToString(CultureInfo.InvariantCulture));
        changed |= SetIniValue(lines, "window.resolution.y", height.ToString(CultureInfo.InvariantCulture));
        if (!changed)
        {
            return false;
        }

        File.WriteAllLines(settingsPath, lines);
        return true;
    }

    private static string GetBeatSaberSettingsFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.GetFullPath(Path.Combine(
            localAppData,
            "..",
            "LocalLow",
            "Hyperbolic Magnetism",
            "Beat Saber",
            "settings.ini"));
    }

    private static bool SetIniValue(List<string> lines, string key, string value)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var separatorIndex = lines[index].IndexOf('=');
            if (separatorIndex < 0 ||
                !string.Equals(lines[index].Substring(0, separatorIndex).Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var updatedLine = key + "=" + value;
            if (string.Equals(lines[index], updatedLine, StringComparison.Ordinal))
            {
                return false;
            }

            lines[index] = updatedLine;
            return true;
        }

        lines.Add(key + "=" + value);
        return true;
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

                var runningLaunchFailureMessage = DetectKnownLaunchFailureMessage(instance);
                if (runningLaunchFailureMessage != null)
                {
                    FailRunningInstanceLaunchNoLock(instance, runningLaunchFailureMessage);
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

            var wasLaunchAttempt =
                string.Equals(instance.GameLaunchStatus, "Started", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(instance.GameLaunchStatus, "Already running", StringComparison.OrdinalIgnoreCase);
            var launchFailureMessage = wasLaunchAttempt ? DetectKnownLaunchFailureMessage(instance) : null;
            if (launchFailureMessage != null)
            {
                MarkInstanceLaunchFailedAfterExitNoLock(instance, launchFailureMessage);
                changed = true;
                continue;
            }

            instance.GameProcessId = null;
            if (wasLaunchAttempt)
            {
                instance.GameLaunchStatus = "Exited";
            }

            changed = true;
        }

        return changed;
    }

    private static string? DetectKnownLaunchFailureMessage(WorkerInstanceRecord instance)
    {
        if (!instance.GameLaunchedAtUtc.HasValue)
        {
            return null;
        }

        var logCutoff = instance.GameLaunchedAtUtc.Value.UtcDateTime.AddSeconds(-5);
        foreach (var log in GetRecentInstanceLogs(instance))
        {
            if (log.LastWriteTimeUtc < logCutoff)
            {
                continue;
            }

            if (FileContainsText(log.FullName, SteamApiInitFailedLogText))
            {
                return SteamUnavailableLaunchMessage;
            }

            if (FileContainsVrControllerFailureLoop(log.FullName))
            {
                return BeatSaberBlackScreenLaunchMessage;
            }
        }

        return null;
    }

    private static IReadOnlyList<FileInfo> GetRecentInstanceLogs(WorkerInstanceRecord instance)
    {
        var launchDirectory = NormalizeNullable(instance.LaunchDirectory);
        if (launchDirectory == null)
        {
            return Array.Empty<FileInfo>();
        }

        var logDirectory = Path.Combine(launchDirectory, "Logs");
        if (!Directory.Exists(logDirectory))
        {
            return Array.Empty<FileInfo>();
        }

        try
        {
            return Directory
                .EnumerateFiles(logDirectory)
                .Where(IsBeatSaberLogFile)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(8)
                .ToList();
        }
        catch (IOException)
        {
            return Array.Empty<FileInfo>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<FileInfo>();
        }
    }

    private static bool IsBeatSaberLogFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".log.gz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool FileContainsText(string path, string text)
    {
        return VisitLogLines(path, line => line.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static bool FileContainsVrControllerFailureLoop(string path)
    {
        var inputFailures = 0;
        var focusFailures = 0;
        var nullReferences = 0;
        return VisitLogLines(
            path,
            line =>
            {
                if (line.Contains("NullReferenceException", StringComparison.OrdinalIgnoreCase))
                {
                    nullReferences++;
                }

                if (line.Contains(VrControllerThumbstickFailureLogText, StringComparison.OrdinalIgnoreCase) ||
                    line.Contains(VrControllerTriggerFailureLogText, StringComparison.OrdinalIgnoreCase))
                {
                    inputFailures++;
                }

                if (line.Contains(VrControllerFocusFailureLogText, StringComparison.OrdinalIgnoreCase))
                {
                    focusFailures++;
                }

                if (nullReferences >= VrControllerFailureMinimumOccurrences &&
                    inputFailures >= VrControllerFailureMinimumOccurrences &&
                    focusFailures >= VrControllerFailureMinimumOccurrences)
                {
                    return true;
                }

                return false;
            });
    }

    private static bool VisitLogLines(string path, Func<string, bool> visitLine)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return VisitReaderLines(reader, visitLine);
            }

            using var plainReader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return VisitReaderLines(plainReader, visitLine);
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool VisitReaderLines(TextReader reader, Func<string, bool> visitLine)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (visitLine(line))
            {
                return true;
            }
        }

        return false;
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

    private string? NormalizeRequestedManagedWorkerIdNoLock(
        WorkerRegisterRequest request,
        string? requestedWorkerId,
        DateTimeOffset now)
    {
        if (requestedWorkerId == null ||
            !request.PreferredInstanceIndex.HasValue ||
            !IsManagedWorkerId(requestedWorkerId))
        {
            return requestedWorkerId;
        }

        var expectedWorkerId = CreateManagedWorkerId(request.PreferredInstanceIndex.Value);
        if (string.Equals(requestedWorkerId, expectedWorkerId, StringComparison.OrdinalIgnoreCase))
        {
            return requestedWorkerId;
        }

        var preferred = _state.Instances.FirstOrDefault(instance => instance.Index == request.PreferredInstanceIndex.Value);
        if (preferred == null || !CanClaimWorkerSlot(preferred, expectedWorkerId, now))
        {
            return requestedWorkerId;
        }

        var expectedWorkerIdInUse = _state.Instances.Any(instance =>
            instance.Index != preferred.Index &&
            !IsWorkerStale(instance, now) &&
            string.Equals(instance.WorkerId, expectedWorkerId, StringComparison.OrdinalIgnoreCase));
        if (expectedWorkerIdInUse)
        {
            return null;
        }

        return expectedWorkerId;
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

    private MapCollectionRecord FindMapCollectionNoLock(string id)
    {
        var normalizedId = NormalizeNullable(id);
        if (normalizedId == null)
        {
            throw new InvalidOperationException("Collection id is required.");
        }

        var collection = _state.Collections.FirstOrDefault(item =>
            string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (collection == null)
        {
            throw new InvalidOperationException("Collection was not found: " + normalizedId);
        }

        return collection;
    }

    private static MapCollectionItemRecord FindMapCollectionItemNoLock(MapCollectionRecord collection, string id)
    {
        var normalizedId = NormalizeNullable(id);
        if (normalizedId == null)
        {
            throw new InvalidOperationException("Collection item id is required.");
        }

        var item = collection.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            throw new InvalidOperationException("Collection item was not found: " + normalizedId);
        }

        return item;
    }

    private MapCardExport CreateMapCardExportNoLock(MapCollectionRecord collection)
    {
        return new MapCardExport
        {
            CollectionId = collection.Id,
            CollectionName = collection.Name,
            Items = collection.Items
                .OrderBy(item => item.SequenceNumber)
                .Select(item => CreateMapCardExportItemNoLock(collection, item))
                .ToList()
        };
    }

    private void EnsureQueueCanLoadCollectionNoLock()
    {
        if (_state.Run.IsRunning || _state.Run.CancellationRequested ||
            _state.Queue.Any(replay => IsActiveReplayStatus(replay.Status)))
        {
            throw new InvalidOperationException("Stop the current run before loading a collection.");
        }
    }

    private ReplayQueueRecord? FindMatchingQueueItemNoLock(MapCollectionItemRecord item)
    {
        var targetKey = CreateReplayTargetKey(item);
        return _state.Queue.FirstOrDefault(replay =>
            string.Equals(CreateReplayTargetKey(replay), targetKey, StringComparison.OrdinalIgnoreCase));
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

    private static string? CreateHeartbeatLagSpikeCancellationReasonNoLock(
        WorkerInstanceRecord instance,
        ReplayQueueRecord? replay,
        DateTimeOffset now,
        double startupGraceSeconds)
    {
        var framesPerSecond = GetLagGuardFramesPerSecondNoLock(instance, out var fpsMetricLabel);
        if (replay == null ||
            !framesPerSecond.HasValue ||
            !IsRecordingStatus(instance.Status) ||
            IsWithinHeartbeatLagSpikeStartupGrace(replay, now, startupGraceSeconds))
        {
            ResetRecordingLagSpikeTrackerNoLock(instance);
            return null;
        }

        if (framesPerSecond.Value >= LagSpikeFramesPerSecondThreshold)
        {
            ResetRecordingLagSpikeTrackerNoLock(instance);
            return null;
        }

        instance.LowFpsRecordingStartedAtUtc ??= now;
        instance.ConsecutiveLowFpsRecordingHeartbeatCount++;
        var lowFpsDuration = now - instance.LowFpsRecordingStartedAtUtc.Value;
        if (lowFpsDuration < LagSpikeLowFpsDurationThreshold)
        {
            return null;
        }

        return "Lag spike detected during replay recording: worker " +
               fpsMetricLabel +
               " FPS " +
               FormatFramesPerSecond(framesPerSecond.Value) +
               " stayed below " +
               LagSpikeFramesPerSecondThreshold.ToString("0", CultureInfo.InvariantCulture) +
               " FPS for " +
               FormatDurationSeconds(lowFpsDuration) +
               ". Recording is invalid.";
    }

    private static void ResetRecordingLagSpikeTrackerNoLock(WorkerInstanceRecord instance)
    {
        instance.ConsecutiveLowFpsRecordingHeartbeatCount = 0;
        instance.LowFpsRecordingStartedAtUtc = null;
    }

    private static double? GetLagGuardFramesPerSecondNoLock(
        WorkerInstanceRecord instance,
        out string metricLabel)
    {
        if (instance.LastReportedFrameSampleCount > 0 &&
            instance.LastReportedAverageFramesPerSecond.HasValue)
        {
            metricLabel = "average";
            return instance.LastReportedAverageFramesPerSecond;
        }

        metricLabel = "minimum";
        return instance.LastReportedFramesPerSecond;
    }

    private static string FormatDurationSeconds(TimeSpan duration)
    {
        return Math.Max(0, duration.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture) + " seconds";
    }

    private static bool IsWithinHeartbeatLagSpikeStartupGrace(
        ReplayQueueRecord replay,
        DateTimeOffset now,
        double startupGraceSeconds)
    {
        var graceAnchor = replay.RecordingStartedAtUtc ?? replay.AssignedAtUtc;
        if (!graceAnchor.HasValue)
        {
            return false;
        }

        var gracePeriod = TimeSpan.FromSeconds(ResolveEffectiveLagSpikeStartupGraceSeconds(startupGraceSeconds));
        return now - graceAnchor.Value < gracePeriod;
    }

    private static double ResolveEffectiveLagSpikeStartupGraceSeconds(double startupGraceSeconds)
    {
        if (double.IsNaN(startupGraceSeconds) || double.IsInfinity(startupGraceSeconds))
        {
            return MinimumRecordingLagSpikeStartupGraceSeconds;
        }

        return Math.Min(30, Math.Max(MinimumRecordingLagSpikeStartupGraceSeconds, startupGraceSeconds));
    }

    private void ResetAssignmentsNoLock()
    {
        foreach (var replay in _state.Queue)
        {
            replay.AssignedInstance = null;
            replay.AssignmentId = null;
            replay.AssignedAtUtc = null;
            replay.RecordingStartedAtUtc = null;
            replay.CompletedAtUtc = null;
            replay.OutputPath = null;
            replay.Error = null;
            replay.Warning = null;
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
        var benchmarkAssignment = FindActiveBenchmarkAssignmentNoLock(instance);
        if (benchmarkAssignment != null && IsPendingReplayStatus(benchmarkAssignment.Status))
        {
            benchmarkAssignment.Status = "Failed";
            benchmarkAssignment.Passed = false;
            benchmarkAssignment.Error = "Worker disconnected before the benchmark assignment finished.";
            benchmarkAssignment.CompletedAtUtc = DateTimeOffset.UtcNow;
        }

        var replay = FindActiveReplayNoLock(instance);
        if (replay != null && IsPendingReplayStatus(replay.Status))
        {
            replay.Status = replayStatus;
            replay.AssignedInstance = null;
            replay.AssignmentId = null;
            replay.AssignedAtUtc = null;
            replay.RecordingStartedAtUtc = null;
        }

        ClearWorkerAssignmentNoLock(instance);
    }

    private static void ClearWorkerAssignmentNoLock(WorkerInstanceRecord instance)
    {
        instance.ActiveAssignmentId = null;
        instance.CurrentReplayId = null;
        ResetRecordingLagSpikeTrackerNoLock(instance);
    }

    private static bool IsRequeueableReplay(ReplayQueueRecord replay)
    {
        return !IsActiveReplayStatus(replay.Status) &&
               !string.Equals(replay.Status, "Queued", StringComparison.OrdinalIgnoreCase);
    }

    private static void RequeueReplayNoLock(ReplayQueueRecord replay)
    {
        replay.Status = "Queued";
        replay.AssignedInstance = null;
        replay.AssignmentId = null;
        replay.AssignedAtUtc = null;
        replay.RecordingStartedAtUtc = null;
        replay.CompletedAtUtc = null;
        replay.OutputPath = null;
        replay.Error = null;
        replay.Warning = null;
    }

    private void ResequenceQueueNoLock()
    {
        for (var index = 0; index < _state.Queue.Count; index++)
        {
            _state.Queue[index].SequenceNumber = index + 1;
        }
    }

    private static void ResequenceMapCollectionItemsNoLock(MapCollectionRecord collection)
    {
        for (var index = 0; index < collection.Items.Count; index++)
        {
            collection.Items[index].SequenceNumber = index + 1;
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

            if (replay.RecordingStartedAtUtc != null)
            {
                replay.RecordingStartedAtUtc = null;
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
                replay.Warning = null;
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
        _state.Run.CloseGamesWhenFinishedRequested = false;

        foreach (var instance in _state.Instances)
        {
            instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
        }

        RestoreTaskbarVisibilityNoLock();
        RestoreDisplayScaleNoLock();
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

        RestoreTaskbarVisibilityNoLock();
        RestoreDisplayScaleNoLock();
        TryCloseGamesWhenFinishedNoLock();
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
            TaskbarVisibilityController.HideWithRetries(_state.Settings.MonitorIndex);
        }
    }

    private void KeepTaskbarHiddenDuringRunNoLock()
    {
        if (_state.Run.IsRunning &&
            !_state.Run.CancellationRequested &&
            _state.Settings.HideTaskbarDuringRun)
        {
            TaskbarVisibilityController.Hide(_state.Settings.MonitorIndex);
        }
    }

    private void RestoreTaskbarVisibilityNoLock()
    {
        TaskbarVisibilityController.Restore();
    }

    private void ValidateRecorderHostsForRunNoLock()
    {
        var runInstances = GetRunInstancesNoLock();
        var unhealthy = runInstances
            .Where(instance => !_recorderHostHealthChecker.IsHealthy(instance.RecorderHostUrl))
            .Select(instance => (instance.Index + 1) + " (" + instance.RecorderHostUrl + ")")
            .ToList();
        if (unhealthy.Count > 0)
        {
            throw new InvalidOperationException(
                "These recorder hosts are not ready: " + string.Join(", ", unhealthy) +
                ". Start the recorder stack again before starting the queue.");
        }

        var captureEngine = ControlPanelSettings.NormalizeCaptureEngine(_state.Settings.CaptureEngine);
        var audioMode = string.Equals(_state.Settings.AudioMode, "ProcessLoopback", StringComparison.OrdinalIgnoreCase)
            ? "ProcessLoopback"
            : "None";
        var unsupported = new List<string>();
        foreach (var instance in runInstances)
        {
            var capabilities = _recorderHostHealthChecker.GetCapabilities(instance.RecorderHostUrl);
            if (!capabilities.SupportsCaptureEngine(captureEngine))
            {
                unsupported.Add(
                    CreateManagedInstanceName(instance.Index) + " capture " + captureEngine + ": " +
                    (NormalizeNullable(capabilities.DescribeCaptureEngine(captureEngine)) ??
                     NormalizeNullable(capabilities.Detail) ??
                     "not supported"));
            }

            if (!capabilities.SupportsAudioMode(audioMode))
            {
                unsupported.Add(
                    CreateManagedInstanceName(instance.Index) + " audio " + audioMode + ": " +
                    (NormalizeNullable(capabilities.DescribeAudioMode(audioMode)) ??
                     NormalizeNullable(capabilities.Detail) ??
                     "not supported"));
            }
        }

        if (unsupported.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Recorder host capabilities do not match the selected recording settings. " +
            string.Join("; ", unsupported.Take(5)) +
            (unsupported.Count > 5 ? "; and " + (unsupported.Count - 5) + " more" : "") + ".");
    }

    private void ValidateCapturePreflightForRunNoLock()
    {
        var report = RunCapturePreflightNoLock();
        if (!string.Equals(report.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var detail = NormalizeNullable(report.Detail) ??
                     NormalizeNullable(report.Summary) ??
                     "Capture preflight failed.";
        throw new InvalidOperationException("Capture preflight failed. " + detail);
    }

    private CapturePreflightReport RunCapturePreflightNoLock()
    {
        var displayInfo = _displayInfoProvider.GetDisplays();
        var report = _capturePreflightRunner.Check(
            _state.Settings,
            displayInfo,
            GetCapturePreflightInstanceIndexesNoLock());
        _state.CapturePreflight = report;

        var kind = string.Equals(report.Status, "Failed", StringComparison.OrdinalIgnoreCase)
            ? "Bad"
            : string.Equals(report.Status, "Ready", StringComparison.OrdinalIgnoreCase)
                ? "Good"
                : "Info";
        AddEventNoLock(
            kind,
            "Capture",
            NormalizeNullable(report.Detail) ??
            NormalizeNullable(report.Summary) ??
            "Capture preflight checked.");
        return report;
    }

    private IReadOnlyList<int> GetCapturePreflightInstanceIndexesNoLock()
    {
        var configuredCount = Math.Clamp(
            _state.Settings.InstanceCount,
            ControlPanelSettings.MinimumManagedInstanceCount,
            ControlPanelSettings.MaximumManagedInstanceCount);
        var indexes = _state.Instances
            .Where(instance => instance.Enabled && instance.Index < configuredCount)
            .Select(instance => instance.Index)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
        if (indexes.Count > 0)
        {
            return indexes;
        }

        return Enumerable.Range(0, configuredCount).ToArray();
    }

    private void ValidateReplayProviderReadinessForRunNoLock(DateTimeOffset now)
    {
        var queuedProviders = _state.Queue
            .Where(replay =>
                !string.Equals(replay.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(replay.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .Select(replay => replay.Provider == ReplayProvider.Unknown ? ReplayProvider.BeatLeader : replay.Provider)
            .Distinct()
            .ToHashSet();
        if (queuedProviders.Count == 0)
        {
            return;
        }

        var runInstances = GetRunInstancesNoLock()
            .Where(instance =>
                !string.IsNullOrWhiteSpace(instance.WorkerId) &&
                !IsWorkerStale(instance, now) &&
                instance.ReplayProviderStatusReported)
            .ToList();
        if (runInstances.Count == 0)
        {
            return;
        }

        var issues = new List<string>();
        if (queuedProviders.Contains(ReplayProvider.BeatLeader))
        {
            issues.AddRange(runInstances
                .Where(instance => !instance.BeatLeaderReady && !IsReplayProviderInitializing(instance.BeatLeaderStatus))
                .Select(instance =>
                    CreateManagedInstanceName(instance.Index) + " BeatLeader: " +
                    (NormalizeNullable(instance.BeatLeaderStatus) ?? "not ready")));
        }

        if (queuedProviders.Contains(ReplayProvider.ScoreSaber2))
        {
            issues.AddRange(runInstances
                .Where(instance => !instance.ScoreSaberReady)
                .Select(instance =>
                    CreateManagedInstanceName(instance.Index) + " ScoreSaber: " +
                    (NormalizeNullable(instance.ScoreSaberStatus) ?? "not ready")));
        }

        if (issues.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Replay provider readiness failed. " + string.Join("; ", issues.Take(5)) +
            (issues.Count > 5 ? "; and " + (issues.Count - 5) + " more" : "") + ".");
    }

    private static bool IsReplayProviderInitializing(string? status)
    {
        return !string.IsNullOrWhiteSpace(status) &&
               status.Contains("not available yet", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyRecordingDisplayScaleNoLock()
    {
        if (!CaptureRestoreDisplayScaleNoLock())
        {
            return;
        }

        ApplyDisplayScaleNoLock(
            _state.Settings.RecordingDisplayScalePercent,
            _state.Settings.MonitorIndex,
            throwOnFailure: true);
        ReapplyRunningWindowPlacementAfterDisplayScaleNoLock();
    }

    private void ReapplyRunningWindowPlacementAfterDisplayScaleNoLock()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var monitor = NativeWindowPlacement.TryGetMonitorBounds(_state.Settings.MonitorIndex);
        if (!monitor.HasValue)
        {
            AddEventNoLock(
                "Warn",
                "Display",
                "Display scale changed, but window placement could not find monitor " +
                (_state.Settings.MonitorIndex + 1).ToString(CultureInfo.InvariantCulture) +
                ".");
            return;
        }

        var (windowWidth, windowHeight) = GetLaunchResolution(_state.Settings.BeatSaberLaunchArguments);
        var appliedCount = 0;
        foreach (var instance in _state.Instances.Where(instance => instance.Enabled))
        {
            using var process = FindBeatSaberProcessForInstance(instance);
            if (process == null || process.HasExited)
            {
                continue;
            }

            var windowHandle = NativeWindowPlacement.FindWindowForProcess(process.Id);
            if (windowHandle == IntPtr.Zero)
            {
                continue;
            }

            var plan = NativeWindowPlacement.CreateFixedSizePlacementPlan(
                monitor.Value,
                instance.Index,
                windowWidth,
                windowHeight);
            NativeWindowPlacement.Apply(windowHandle, plan);
            appliedCount++;
        }

        if (appliedCount > 0)
        {
            AddEventNoLock(
                "Info",
                "Display",
                "Reapplied window placement after display scale change for " +
                appliedCount.ToString(CultureInfo.InvariantCulture) +
                " running game" +
                (appliedCount == 1 ? "." : "s."));
        }
    }

    internal static (int Left, int Top, int Width, int Height) CalculateFixedWindowPlacement(
        int monitorLeft,
        int monitorTop,
        int monitorRight,
        int monitorBottom,
        int instanceIndex,
        int requestedWidth,
        int requestedHeight)
    {
        var plan = NativeWindowPlacement.CreateFixedSizePlacementPlan(
            new NativeWindowPlacement.Rect
            {
                Left = monitorLeft,
                Top = monitorTop,
                Right = monitorRight,
                Bottom = monitorBottom
            },
            instanceIndex,
            requestedWidth,
            requestedHeight);
        return (plan.Left, plan.Top, plan.Width, plan.Height);
    }

    private void RestoreDisplayScaleNoLock()
    {
        try
        {
            if (!_state.Run.DisplayScaleRestorePending && !_detectedRestoreDisplayScalePercent.HasValue)
            {
                return;
            }

            var restoreScalePercent = _state.Run.DisplayScaleRestorePending && _state.Run.DisplayScaleRestorePercent > 0
                ? _state.Run.DisplayScaleRestorePercent
                : _detectedRestoreDisplayScalePercent ?? _state.Settings.RestoreDisplayScalePercent;
            var monitorIndex = _state.Run.DisplayScaleRestorePending
                ? _state.Run.DisplayScaleMonitorIndex
                : _state.Settings.MonitorIndex;
            ApplyDisplayScaleNoLock(restoreScalePercent, monitorIndex, throwOnFailure: false);
        }
        finally
        {
            _detectedRestoreDisplayScalePercent = null;
            _state.Run.DisplayScaleRestorePending = false;
            _state.Run.DisplayScaleRestorePercent = 0;
            _state.Run.DisplayScaleMonitorIndex = 0;
        }
    }

    private bool CaptureRestoreDisplayScaleNoLock()
    {
        if (!_state.Settings.ManageDisplayScale)
        {
            return false;
        }

        if (_detectedRestoreDisplayScalePercent.HasValue || _state.Run.DisplayScaleRestorePending)
        {
            return true;
        }

        var scalePercent = ReadDisplayScaleNoLock(throwOnFailure: true);
        if (!scalePercent.HasValue)
        {
            return false;
        }

        var restoreScalePercent = scalePercent.Value;
        if (restoreScalePercent == _state.Settings.RecordingDisplayScalePercent)
        {
            return false;
        }

        _detectedRestoreDisplayScalePercent = restoreScalePercent;
        _state.Run.DisplayScaleRestorePending = true;
        _state.Run.DisplayScaleRestorePercent = restoreScalePercent;
        _state.Run.DisplayScaleMonitorIndex = _state.Settings.MonitorIndex;
        _state.Settings.RestoreDisplayScalePercent = restoreScalePercent;
        SaveNoLock();
        return true;
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

    private void ApplyDisplayScaleNoLock(int scalePercent, int monitorIndex, bool throwOnFailure)
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

        var monitorNumber = Math.Clamp(monitorIndex, 0, 16) + 1;
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
        if (configuredPath != null)
        {
            return File.Exists(configuredPath) ? configuredPath : null;
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

    private string CreateRunRecordingOutputDirectoryNoLock(DateTimeOffset startedAtUtc, string? collectionName)
    {
        var recordingRoot = Path.GetFullPath(_state.Settings.RecordingOutputDirectory);
        var folderName = startedAtUtc
            .ToLocalTime()
            .ToString("MM-dd-yyyy HH-mm-ss", CultureInfo.InvariantCulture);
        var normalizedCollectionName = NormalizeNullable(collectionName);
        if (normalizedCollectionName != null)
        {
            folderName += " - " + normalizedCollectionName;
        }

        return CreateUniqueDirectoryPath(
            recordingRoot,
            FileNameSanitizer.SanitizeBaseName(folderName));
    }

    private string? ResolveCurrentQueueCollectionNameNoLock()
    {
        var queue = _state.Queue
            .OrderBy(replay => replay.SequenceNumber)
            .ToList();
        if (queue.Count == 0)
        {
            return null;
        }

        foreach (var collection in _state.Collections.OrderByDescending(item => item.UpdatedAtUtc))
        {
            var items = collection.Items
                .OrderBy(item => item.SequenceNumber)
                .ToList();
            if (items.Count != queue.Count)
            {
                continue;
            }

            var matches = true;
            for (var index = 0; index < queue.Count; index++)
            {
                if (!string.Equals(
                        CreateReplayTargetKey(queue[index]),
                        CreateReplayTargetKey(items[index]),
                        StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return NormalizeNullable(collection.Name);
            }
        }

        return null;
    }

    private static string CreateUniqueDirectoryPath(string directory, string folderName)
    {
        var baseDirectory = Path.GetFullPath(directory);
        var safeFolderName = FileNameSanitizer.SanitizeBaseName(folderName);
        var candidate = Path.Combine(baseDirectory, safeFolderName);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 2; index < 10_000; index++)
        {
            candidate = Path.Combine(
                baseDirectory,
                safeFolderName + " (" + index.ToString(CultureInfo.InvariantCulture) + ")");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find an available recording folder for " + safeFolderName + ".");
    }

    private bool NormalizeBenchmarkNoLock(DateTimeOffset now)
    {
        _state.Benchmark ??= new BenchmarkState();
        _state.Benchmark.SourceQueueItemIds ??= new List<string>();
        _state.Benchmark.SelectedConcurrencies ??= new List<int>();
        _state.Benchmark.Passes ??= new List<BenchmarkPassResult>();
        _state.Benchmark.SettingsSnapshot ??= new BenchmarkSettingsSnapshot();
        foreach (var pass in _state.Benchmark.Passes)
        {
            pass.Assignments ??= new List<BenchmarkAssignmentResult>();
        }

        if (!_state.Benchmark.IsRunning && !_state.Benchmark.CancellationRequested)
        {
            return false;
        }

        var beforeStatus = _state.Benchmark.Status;
        TryAdvanceBenchmarkNoLock(now);
        return !string.Equals(beforeStatus, _state.Benchmark.Status, StringComparison.Ordinal);
    }

    private List<ReplayQueueRecord> GetBenchmarkSourceReplaysNoLock()
    {
        return _state.Queue
            .Where(IsBenchmarkReadyReplay)
            .OrderBy(replay => replay.SequenceNumber)
            .ToList();
    }

    private static bool IsBenchmarkReadyReplay(ReplayQueueRecord replay)
    {
        if (string.IsNullOrWhiteSpace(replay.Path) || !File.Exists(replay.Path))
        {
            return false;
        }

        return string.Equals(replay.MapStatus, "Found", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(replay.MapStatus, "Downloaded", StringComparison.OrdinalIgnoreCase);
    }

    private List<WorkerInstanceRecord> GetBenchmarkReadyInstancesNoLock(DateTimeOffset now)
    {
        return GetRunInstancesNoLock()
            .Where(instance =>
                instance.Enabled &&
                !string.IsNullOrWhiteSpace(instance.WorkerId) &&
                !IsWorkerStale(instance, now))
            .OrderBy(instance => instance.Index)
            .Take(BenchmarkMaximumConcurrency)
            .ToList();
    }

    private BenchmarkSettingsSnapshot CreateBenchmarkSettingsSnapshotNoLock(int enabledWorkerCount)
    {
        return new BenchmarkSettingsSnapshot
        {
            EnabledWorkerCount = enabledWorkerCount,
            TargetFps = _state.Settings.TargetFps,
            CaptureWidth = _state.Settings.CaptureWidth,
            CaptureHeight = _state.Settings.CaptureHeight,
            Encoder = _state.Settings.Encoder,
            VideoBitrateKbps = _state.Settings.VideoBitrateKbps,
            OutputFormat = _state.Settings.OutputFormat,
            MonitorIndex = _state.Settings.MonitorIndex,
            QualityMode = _state.Settings.QualityMode,
            CaptureEngine = _state.Settings.CaptureEngine,
            AudioMode = _state.Settings.AudioMode,
            RequireAudioForRun = _state.Settings.RequireAudioForRun,
            AudioBitrateKbps = _state.Settings.AudioBitrateKbps,
            AudioSampleRate = _state.Settings.AudioSampleRate,
            AudioChannels = _state.Settings.AudioChannels,
            AudioLevelMode = _state.Settings.AudioLevelMode,
            AudioTargetLevelDb = _state.Settings.AudioTargetLevelDb,
            LagSpikeStartupGraceSeconds = ResolveEffectiveLagSpikeStartupGraceSeconds(_state.Settings.LagSpikeStartupGraceSeconds),
            DelayBetweenRecordingsSeconds = _state.Settings.DelayBetweenRecordingsSeconds
        };
    }

    private static List<int> NormalizeBenchmarkConcurrencyLevels(
        IReadOnlyList<int>? requestedLevels,
        int maxConcurrency)
    {
        if (maxConcurrency <= 0)
        {
            return new List<int>();
        }

        if (requestedLevels == null || requestedLevels.Count == 0)
        {
            return Enumerable.Range(1, maxConcurrency).ToList();
        }

        var selected = requestedLevels
            .Distinct()
            .OrderBy(level => level)
            .ToList();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("Select at least one benchmark concurrency level.");
        }

        var invalidLevel = selected.FirstOrDefault(level => level < 1 || level > BenchmarkMaximumConcurrency);
        if (invalidLevel != 0)
        {
            throw new InvalidOperationException(
                "Benchmark concurrency levels must be between 1 and " +
                BenchmarkMaximumConcurrency.ToString(CultureInfo.InvariantCulture) + ".");
        }

        var unavailableLevel = selected.FirstOrDefault(level => level > maxConcurrency);
        if (unavailableLevel != 0)
        {
            throw new InvalidOperationException(
                "Cannot benchmark " +
                unavailableLevel.ToString(CultureInfo.InvariantCulture) +
                " workers because only " +
                maxConcurrency.ToString(CultureInfo.InvariantCulture) +
                " enabled online worker" + (maxConcurrency == 1 ? " is" : "s are") +
                " available.");
        }

        return selected;
    }

    private List<int> GetSelectedBenchmarkConcurrenciesNoLock()
    {
        if (_state.Benchmark.SelectedConcurrencies != null &&
            _state.Benchmark.SelectedConcurrencies.Count > 0)
        {
            return _state.Benchmark.SelectedConcurrencies
                .Where(level => level >= 1 && level <= Math.Max(1, _state.Benchmark.MaxConcurrency))
                .Distinct()
                .OrderBy(level => level)
                .ToList();
        }

        var maxConcurrency = Math.Max(0, _state.Benchmark.MaxConcurrency);
        return maxConcurrency <= 0
            ? new List<int>()
            : Enumerable.Range(1, maxConcurrency).ToList();
    }

    private void ValidateReplayProviderReadinessForBenchmarkNoLock(
        IReadOnlyList<ReplayQueueRecord> sourceReplays,
        IReadOnlyList<WorkerInstanceRecord> readyInstances,
        DateTimeOffset now)
    {
        var providers = sourceReplays
            .Select(replay => replay.Provider == ReplayProvider.Unknown ? ReplayProvider.BeatLeader : replay.Provider)
            .Distinct()
            .ToHashSet();
        var reportedInstances = readyInstances
            .Where(instance => instance.ReplayProviderStatusReported && !IsWorkerStale(instance, now))
            .ToList();
        if (providers.Count == 0 || reportedInstances.Count == 0)
        {
            return;
        }

        var issues = new List<string>();
        if (providers.Contains(ReplayProvider.BeatLeader))
        {
            issues.AddRange(reportedInstances
                .Where(instance => !instance.BeatLeaderReady)
                .Select(instance =>
                    CreateManagedInstanceName(instance.Index) + " BeatLeader: " +
                    (NormalizeNullable(instance.BeatLeaderStatus) ?? "not ready")));
        }

        if (providers.Contains(ReplayProvider.ScoreSaber2))
        {
            issues.AddRange(reportedInstances
                .Where(instance => !instance.ScoreSaberReady)
                .Select(instance =>
                    CreateManagedInstanceName(instance.Index) + " ScoreSaber: " +
                    (NormalizeNullable(instance.ScoreSaberStatus) ?? "not ready")));
        }

        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                "Benchmark replay provider readiness failed. " + string.Join("; ", issues.Take(5)) +
                (issues.Count > 5 ? "; and " + (issues.Count - 5) + " more" : "") + ".");
        }
    }

    private void ValidateAudioSettingsForBenchmarkNoLock(IReadOnlyList<WorkerInstanceRecord> readyInstances)
    {
        if (string.Equals(_state.Settings.AudioMode, "ProcessLoopback", StringComparison.OrdinalIgnoreCase))
        {
            RefreshLaunchProcessesNoLock();
            var missingProcessIndexes = readyInstances
                .Where(instance => !instance.GameProcessId.HasValue)
                .Select(instance => instance.Index + 1)
                .ToList();
            if (missingProcessIndexes.Count > 0)
            {
                throw new InvalidOperationException(
                    "Benchmark audio mode is ProcessLoopback, but these instances do not have known Beat Saber process IDs: " +
                    string.Join(", ", missingProcessIndexes) +
                    ". Use Launch Games or wait for the workers to reconnect before benchmarking.");
            }

            return;
        }

        if (_state.Settings.RequireAudioForRun)
        {
            throw new InvalidOperationException(
                "Benchmark audio is required, but Audio mode is None. Use ProcessLoopback or turn off Require audio.");
        }
    }

    private void ValidateRecorderHostsForBenchmarkNoLock(IReadOnlyList<WorkerInstanceRecord> readyInstances)
    {
        var unhealthy = readyInstances
            .Where(instance => !_recorderHostHealthChecker.IsHealthy(instance.RecorderHostUrl))
            .Select(instance => (instance.Index + 1) + " (" + instance.RecorderHostUrl + ")")
            .ToList();
        if (unhealthy.Count > 0)
        {
            throw new InvalidOperationException(
                "These recorder hosts are not ready for benchmarking: " + string.Join(", ", unhealthy) +
                ". Start the recorder stack again before benchmarking.");
        }

        var captureEngine = ControlPanelSettings.NormalizeCaptureEngine(_state.Settings.CaptureEngine);
        var audioMode = string.Equals(_state.Settings.AudioMode, "ProcessLoopback", StringComparison.OrdinalIgnoreCase)
            ? "ProcessLoopback"
            : "None";
        var unsupported = new List<string>();
        foreach (var instance in readyInstances)
        {
            var capabilities = _recorderHostHealthChecker.GetCapabilities(instance.RecorderHostUrl);
            if (!capabilities.SupportsCaptureEngine(captureEngine))
            {
                unsupported.Add(
                    CreateManagedInstanceName(instance.Index) + " capture " + captureEngine + ": " +
                    (NormalizeNullable(capabilities.DescribeCaptureEngine(captureEngine)) ??
                     NormalizeNullable(capabilities.Detail) ??
                     "not supported"));
            }

            if (!capabilities.SupportsAudioMode(audioMode))
            {
                unsupported.Add(
                    CreateManagedInstanceName(instance.Index) + " audio " + audioMode + ": " +
                    (NormalizeNullable(capabilities.DescribeAudioMode(audioMode)) ??
                     NormalizeNullable(capabilities.Detail) ??
                     "not supported"));
            }
        }

        if (unsupported.Count > 0)
        {
            throw new InvalidOperationException(
                "Recorder host capabilities do not match the selected benchmark settings. " +
                string.Join("; ", unsupported.Take(5)) +
                (unsupported.Count > 5 ? "; and " + (unsupported.Count - 5) + " more" : "") + ".");
        }
    }

    private void StartNextBenchmarkPassNoLock(DateTimeOffset now)
    {
        if (!_state.Benchmark.IsRunning || _state.Benchmark.CancellationRequested)
        {
            return;
        }

        var selectedConcurrencies = GetSelectedBenchmarkConcurrenciesNoLock();
        var passIndex = _state.Benchmark.Passes.Count;
        if (passIndex >= selectedConcurrencies.Count)
        {
            CompleteBenchmarkNoLock("Complete", "", now);
            return;
        }

        var concurrency = selectedConcurrencies[passIndex];
        var sourceReplays = GetBenchmarkSourceReplaysNoLock()
            .Where(replay => _state.Benchmark.SourceQueueItemIds.Contains(replay.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (sourceReplays.Count == 0)
        {
            CompleteBenchmarkNoLock("Failed", "No benchmark source replays are still available.", now);
            return;
        }

        var readyInstances = GetBenchmarkReadyInstancesNoLock(now);
        if (readyInstances.Count < concurrency)
        {
            CompleteBenchmarkNoLock(
                "Failed",
                "Only " + readyInstances.Count + "/" + concurrency + " benchmark workers are still online.",
                now);
            return;
        }

        var pass = new BenchmarkPassResult
        {
            Concurrency = concurrency,
            Status = "Running",
            StartedAtUtc = now
        };

        for (var index = 0; index < concurrency; index++)
        {
            var instance = readyInstances[index];
            var replay = sourceReplays[index % sourceReplays.Count];
            pass.Assignments.Add(new BenchmarkAssignmentResult
            {
                AssignmentId = CreateBenchmarkAssignmentId(),
                SourceReplayId = replay.Id,
                ReplayLabel = CreateReplayLabel(replay),
                InstanceIndex = instance.Index,
                InstanceName = CreateManagedInstanceName(instance.Index),
                WorkerId = instance.WorkerId ?? "",
                Status = "Queued"
            });
        }

        _state.Benchmark.ActiveConcurrency = concurrency;
        _state.Benchmark.Passes.Add(pass);
        AddEventNoLock(
            "Info",
            "Benchmark",
            "Benchmark pass " + concurrency + " started (" +
            (passIndex + 1).ToString(CultureInfo.InvariantCulture) +
            "/" +
            selectedConcurrencies.Count.ToString(CultureInfo.InvariantCulture) +
            ").");
    }

    private BenchmarkAssignmentResult? FindActiveBenchmarkAssignmentNoLock(WorkerInstanceRecord instance)
    {
        var activeAssignmentId = NormalizeNullable(instance.ActiveAssignmentId);
        if (activeAssignmentId == null)
        {
            return null;
        }

        var assignment = FindBenchmarkAssignmentByIdNoLock(activeAssignmentId);
        return assignment != null && IsPendingReplayStatus(assignment.Status) ? assignment : null;
    }

    private BenchmarkAssignmentResult? FindBenchmarkAssignmentByIdNoLock(string? assignmentId)
    {
        var normalizedAssignmentId = NormalizeNullable(assignmentId);
        if (normalizedAssignmentId == null)
        {
            return null;
        }

        return _state.Benchmark.Passes
            .SelectMany(pass => pass.Assignments)
            .FirstOrDefault(assignment =>
                string.Equals(assignment.AssignmentId, normalizedAssignmentId, StringComparison.OrdinalIgnoreCase));
    }

    private BenchmarkAssignmentResult? FindQueuedBenchmarkAssignmentForInstanceNoLock(WorkerInstanceRecord instance)
    {
        var pass = _state.Benchmark.Passes
            .LastOrDefault(item => string.Equals(item.Status, "Running", StringComparison.OrdinalIgnoreCase));
        return pass?.Assignments.FirstOrDefault(assignment =>
            assignment.InstanceIndex == instance.Index &&
            string.Equals(assignment.Status, "Queued", StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<BenchmarkAssignmentResult> EnumerateActiveBenchmarkAssignmentsNoLock()
    {
        return _state.Benchmark.Passes
            .SelectMany(pass => pass.Assignments)
            .Where(assignment => IsPendingReplayStatus(assignment.Status));
    }

    private void UpdateBenchmarkAssignmentHeartbeatNoLock(
        BenchmarkAssignmentResult assignment,
        WorkerInstanceRecord instance,
        DateTimeOffset now)
    {
        assignment.HeartbeatCount++;
        assignment.WorkerId = instance.WorkerId ?? assignment.WorkerId;
        if (IsRecordingStatus(instance.Status))
        {
            assignment.Status = "Recording";
            assignment.RecordingStartedAtUtc ??= now;
            if (instance.LastReportedFramesPerSecond.HasValue)
            {
                assignment.MinimumFramesPerSecond = assignment.MinimumFramesPerSecond.HasValue
                    ? Math.Min(assignment.MinimumFramesPerSecond.Value, instance.LastReportedFramesPerSecond.Value)
                    : instance.LastReportedFramesPerSecond.Value;
            }

            var sampleCount = Math.Max(0, instance.LastReportedFrameSampleCount);
            if (sampleCount > 0 && instance.LastReportedAverageFramesPerSecond.HasValue)
            {
                var existingCount = Math.Max(0, assignment.SampledFrameCount);
                var existingTotal = assignment.AverageFramesPerSecond.GetValueOrDefault() * existingCount;
                var newTotal = instance.LastReportedAverageFramesPerSecond.Value * sampleCount;
                assignment.SampledFrameCount = existingCount + sampleCount;
                assignment.AverageFramesPerSecond = Math.Round(
                    (existingTotal + newTotal) / assignment.SampledFrameCount,
                    1);
            }
        }
        else if (IsFinalizingStatus(instance.Status))
        {
            assignment.Status = "Finalizing";
            assignment.FinalizingStartedAtUtc ??= now;
            ResetRecordingLagSpikeTrackerNoLock(instance);
        }
    }

    private static string? CreateBenchmarkHeartbeatLagSpikeCancellationReasonNoLock(
        WorkerInstanceRecord instance,
        BenchmarkAssignmentResult? assignment,
        DateTimeOffset now)
    {
        var framesPerSecond = GetLagGuardFramesPerSecondNoLock(instance, out var fpsMetricLabel);
        if (assignment == null ||
            !framesPerSecond.HasValue ||
            !IsRecordingStatus(instance.Status) ||
            IsWithinBenchmarkLagSpikeStartupGrace(assignment, now))
        {
            ResetRecordingLagSpikeTrackerNoLock(instance);
            return null;
        }

        if (framesPerSecond.Value >= LagSpikeFramesPerSecondThreshold)
        {
            ResetRecordingLagSpikeTrackerNoLock(instance);
            return null;
        }

        instance.LowFpsRecordingStartedAtUtc ??= now;
        instance.ConsecutiveLowFpsRecordingHeartbeatCount++;
        var lowFpsDuration = now - instance.LowFpsRecordingStartedAtUtc.Value;
        if (lowFpsDuration < LagSpikeLowFpsDurationThreshold)
        {
            return null;
        }

        return "Benchmark FPS drop detected: worker " +
               fpsMetricLabel +
               " FPS " +
               FormatFramesPerSecond(framesPerSecond.Value) +
               " stayed below " +
               LagSpikeFramesPerSecondThreshold.ToString("0", CultureInfo.InvariantCulture) +
               " FPS for " +
               FormatDurationSeconds(lowFpsDuration) +
               ". Benchmark assignment is invalid.";
    }

    private static bool IsWithinBenchmarkLagSpikeStartupGrace(
        BenchmarkAssignmentResult assignment,
        DateTimeOffset now)
    {
        var graceAnchor = assignment.RecordingStartedAtUtc ?? assignment.AssignedAtUtc;
        if (!graceAnchor.HasValue)
        {
            return false;
        }

        var gracePeriod = TimeSpan.FromSeconds(MinimumRecordingLagSpikeStartupGraceSeconds);
        return now - graceAnchor.Value < gracePeriod;
    }

    private void ReportBenchmarkAssignmentNoLock(
        WorkerReportRequest request,
        string status,
        WorkerInstanceRecord instance,
        BenchmarkAssignmentResult assignment,
        DateTimeOffset now)
    {
        if (status == "Completed")
        {
            CompleteBenchmarkAssignmentFinalizationNoLock(assignment, now);
            var outputPath = NormalizeNullable(request.OutputPath);
            var audioVerification = VerifyCompletedRecordingAudioNoLock(outputPath);
            var syncVerification = VerifyCompletedRecordingSyncNoLock(request);
            var verified = audioVerification.HasAudio && syncVerification.Verified;
            assignment.Status = verified ? "Completed" : "Failed";
            assignment.Passed = verified;
            assignment.OutputPath = outputPath ?? "";
            assignment.Error = verified
                ? ""
                : audioVerification.HasAudio
                    ? syncVerification.Error ?? "Required sync verification failed."
                    : audioVerification.Error ?? "Required audio verification failed.";
            assignment.Warning = NormalizeNullable(request.Warning) ?? "";
            assignment.SyncStatus = NormalizeNullable(request.SyncStatus) ?? "";
            assignment.SyncCorrectionMilliseconds = request.SyncCorrectionMilliseconds;
            assignment.TrimStartSeconds = request.TrimStartSeconds;
            assignment.SyncReportPath = NormalizeNullable(request.SyncReportPath) ?? "";
            assignment.CompletedAtUtc = now;
            ClearWorkerAssignmentNoLock(instance);
            instance.Status = "Online";
        }
        else if (status == "Failed" || status == "Stopped")
        {
            CompleteBenchmarkAssignmentFinalizationNoLock(assignment, now);
            assignment.Status = status == "Stopped" ? "Stopped" : "Failed";
            assignment.Passed = false;
            assignment.OutputPath = NormalizeNullable(request.OutputPath) ?? "";
            assignment.Error = NormalizeNullable(request.Error) ?? "Worker reported " + status.ToLowerInvariant() + ".";
            assignment.Warning = NormalizeNullable(request.Warning) ?? "";
            assignment.SyncStatus = NormalizeNullable(request.SyncStatus) ?? "";
            assignment.SyncCorrectionMilliseconds = request.SyncCorrectionMilliseconds;
            assignment.TrimStartSeconds = request.TrimStartSeconds;
            assignment.SyncReportPath = NormalizeNullable(request.SyncReportPath) ?? "";
            assignment.CompletedAtUtc = now;
            ClearWorkerAssignmentNoLock(instance);
            instance.Status = "Online";
        }
        else
        {
            assignment.Status = status;
            if (IsFinalizingStatus(status))
            {
                assignment.Status = "Finalizing";
                assignment.FinalizingStartedAtUtc ??= now;
                ResetRecordingLagSpikeTrackerNoLock(instance);
            }

            assignment.OutputPath = NormalizeNullable(request.OutputPath) ?? assignment.OutputPath;
            assignment.Error = NormalizeNullable(request.Error) ?? "";
            assignment.Warning = NormalizeNullable(request.Warning) ?? "";
            instance.Status = assignment.Status;
            instance.CurrentReplayId = assignment.SourceReplayId;
        }

        if (assignment.CompletedAtUtc.HasValue)
        {
            AddEventNoLock(
                assignment.Passed ? "Good" : "Bad",
                "Benchmark",
                assignment.Passed
                    ? "Benchmark recording complete: " + assignment.ReplayLabel
                    : "Benchmark recording failed: " + assignment.ReplayLabel + " - " + assignment.Error,
                assignment.SourceReplayId,
                assignment.InstanceIndex);
        }

        TryAdvanceBenchmarkNoLock(now);
    }

    private static void CompleteBenchmarkAssignmentFinalizationNoLock(
        BenchmarkAssignmentResult assignment,
        DateTimeOffset now)
    {
        if (!assignment.FinalizingStartedAtUtc.HasValue)
        {
            return;
        }

        assignment.FinalizingCompletedAtUtc ??= now;
        var elapsed = assignment.FinalizingCompletedAtUtc.Value - assignment.FinalizingStartedAtUtc.Value;
        assignment.FinalizationSeconds = Math.Round(Math.Max(0, elapsed.TotalSeconds), 1);
    }

    private void TryAdvanceBenchmarkNoLock(DateTimeOffset now)
    {
        if (_state.Benchmark.Passes.Count == 0)
        {
            if (_state.Benchmark.CancellationRequested)
            {
                CompleteBenchmarkNoLock("Stopped", NormalizeNullable(_state.Benchmark.FailureReason) ?? "Stopped by operator.", now);
            }

            return;
        }

        var currentPass = _state.Benchmark.Passes
            .LastOrDefault(pass => string.Equals(pass.Status, "Running", StringComparison.OrdinalIgnoreCase));
        if (currentPass == null)
        {
            if (_state.Benchmark.CancellationRequested)
            {
                CompleteBenchmarkNoLock("Stopped", NormalizeNullable(_state.Benchmark.FailureReason) ?? "Stopped by operator.", now);
            }

            return;
        }

        if (currentPass.Assignments.Any(assignment => !IsBenchmarkAssignmentTerminal(assignment)))
        {
            return;
        }

        SummarizeBenchmarkPassNoLock(currentPass, now);
        if (_state.Benchmark.CancellationRequested)
        {
            CompleteBenchmarkNoLock("Stopped", NormalizeNullable(_state.Benchmark.FailureReason) ?? "Stopped by operator.", now);
            return;
        }

        if (!currentPass.Passed)
        {
            CompleteBenchmarkNoLock(
                _state.Benchmark.RecommendedWorkerCount.HasValue ? "Complete" : "Failed",
                currentPass.FailureReason,
                now);
            return;
        }

        _state.Benchmark.RecommendedWorkerCount = currentPass.Concurrency;
        if (_state.Benchmark.Passes.Count >= GetSelectedBenchmarkConcurrenciesNoLock().Count)
        {
            CompleteBenchmarkNoLock("Complete", "", now);
            return;
        }

        StartNextBenchmarkPassNoLock(now);
    }

    private void SummarizeBenchmarkPassNoLock(BenchmarkPassResult pass, DateTimeOffset now)
    {
        pass.FinishedAtUtc ??= now;
        pass.MinimumFramesPerSecond = pass.Assignments
            .Where(assignment => assignment.MinimumFramesPerSecond.HasValue)
            .Select(assignment => assignment.MinimumFramesPerSecond!.Value)
            .DefaultIfEmpty()
            .Min();
        if (pass.MinimumFramesPerSecond <= 0)
        {
            pass.MinimumFramesPerSecond = null;
        }

        var sampledCount = pass.Assignments.Sum(assignment => Math.Max(0, assignment.SampledFrameCount));
        if (sampledCount > 0)
        {
            var total = pass.Assignments.Sum(assignment =>
                assignment.AverageFramesPerSecond.GetValueOrDefault() * Math.Max(0, assignment.SampledFrameCount));
            pass.AverageFramesPerSecond = Math.Round(total / sampledCount, 1);
        }

        var validCount = pass.Assignments.Count(assignment => assignment.Passed);
        pass.Passed = validCount == pass.Assignments.Count && pass.Assignments.Count > 0;
        pass.OutputSummary = validCount + "/" + pass.Assignments.Count + " recordings valid";
        pass.FailureReason = pass.Passed
            ? ""
            : pass.Assignments
                  .Select(assignment => NormalizeNullable(assignment.Error))
                  .FirstOrDefault(error => error != null) ??
              "At least one benchmark assignment failed.";
        pass.Status = pass.Passed ? "Passed" : _state.Benchmark.CancellationRequested ? "Stopped" : "Failed";
    }

    private static bool IsBenchmarkAssignmentTerminal(BenchmarkAssignmentResult assignment)
    {
        return string.Equals(assignment.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(assignment.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(assignment.Status, "Stopped", StringComparison.OrdinalIgnoreCase);
    }

    private void CompleteBenchmarkNoLock(string status, string failureReason, DateTimeOffset now)
    {
        if (!_state.Benchmark.IsRunning && !_state.Benchmark.CancellationRequested &&
            !_state.Benchmark.StartedAtUtc.HasValue)
        {
            return;
        }

        _state.Benchmark.IsRunning = false;
        _state.Benchmark.CancellationRequested = false;
        _state.Benchmark.Status = status;
        _state.Benchmark.ActiveConcurrency = 0;
        _state.Benchmark.FinishedAtUtc ??= now;
        _state.Benchmark.FailureReason = NormalizeNullable(failureReason) ?? "";
        foreach (var instance in _state.Instances.Where(instance => FindActiveBenchmarkAssignmentNoLock(instance) != null))
        {
            ClearWorkerAssignmentNoLock(instance);
            instance.Status = string.IsNullOrWhiteSpace(instance.WorkerId) ? "Idle" : "Online";
        }

        WriteBenchmarkReportNoLock();
        AddEventNoLock(
            status == "Complete" ? "Good" : status == "Stopped" ? "Warn" : "Bad",
            "Benchmark",
            CreateBenchmarkCompletionMessageNoLock(),
            nowOverride: now);
    }

    private string CreateBenchmarkCompletionMessageNoLock()
    {
        if (_state.Benchmark.RecommendedWorkerCount.HasValue)
        {
            return "Benchmark finished. Recommended worker count: " +
                   _state.Benchmark.RecommendedWorkerCount.Value.ToString(CultureInfo.InvariantCulture) + ".";
        }

        var reason = NormalizeNullable(_state.Benchmark.FailureReason);
        return reason == null
            ? "Benchmark finished without a passing worker count."
            : "Benchmark finished without a passing worker count: " + reason;
    }

    private void WriteBenchmarkReportNoLock()
    {
        var reportPath = NormalizeNullable(_state.Benchmark.ReportPath);
        if (reportPath == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(_state.Benchmark, JsonOptions.Default));
        }
        catch
        {
            // The persisted control-panel state remains the source of truth if report export fails.
        }
    }

    private WorkerRunProgress CreateBenchmarkProgressNoLock()
    {
        var assignments = _state.Benchmark.Passes.SelectMany(pass => pass.Assignments).ToList();
        return new WorkerRunProgress
        {
            TotalCount = assignments.Count,
            CompletedCount = assignments.Count(assignment => assignment.Passed),
            FailedCount = assignments.Count(assignment =>
                string.Equals(assignment.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assignment.Status, "Stopped", StringComparison.OrdinalIgnoreCase)),
            IsRunning = _state.Benchmark.IsRunning || _state.Benchmark.CancellationRequested,
            Status = string.IsNullOrWhiteSpace(_state.Benchmark.Status) ? "Idle" : _state.Benchmark.Status
        };
    }

    private WorkerAssignmentResponse CreateBenchmarkAssignmentResponse(
        BenchmarkAssignmentResult assignment,
        WorkerInstanceRecord instance)
    {
        var replay = _state.Queue.FirstOrDefault(item =>
                         string.Equals(item.Id, assignment.SourceReplayId, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException("Benchmark source replay was not found: " + assignment.SourceReplayId);
        var snapshot = _state.Benchmark.SettingsSnapshot;
        var outputDirectory = NormalizeNullable(_state.Benchmark.OutputDirectory) ?? instance.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);
        return new WorkerAssignmentResponse
        {
            HasAssignment = true,
            AssignmentId = assignment.AssignmentId,
            ReplayId = replay.Id,
            ReplayPath = Path.GetFullPath(replay.Path),
            DisableScoreSubmissions = true,
            SuppressScoreSaberReplayUi = true,
            Provider = replay.Provider,
            ReferenceKind = replay.ReferenceKind,
            ReplayFormat = replay.ReplayFormat,
            SourceUrl = replay.SourceUrl,
            ScoreId = replay.ScoreId,
            SongName = replay.SongName,
            Mapper = replay.Mapper,
            PlayerName = replay.PlayerName,
            Difficulty = replay.Difficulty,
            Mode = replay.Mode,
            LevelHash = replay.LevelHash,
            EstimatedSeconds = replay.EstimatedSeconds,
            AssignmentKind = "Benchmark",
            OutputBaseName = CreateBenchmarkOutputBaseName(assignment, replay),
            RecorderHostUrl = instance.RecorderHostUrl,
            OutputDirectory = outputDirectory,
            InstanceIndex = instance.Index,
            TargetProcessId = instance.GameProcessId,
            TargetFps = snapshot.TargetFps,
            CaptureWidth = snapshot.CaptureWidth,
            CaptureHeight = snapshot.CaptureHeight,
            Encoder = snapshot.Encoder,
            VideoBitrateKbps = snapshot.VideoBitrateKbps,
            OutputFormat = snapshot.OutputFormat,
            MonitorIndex = snapshot.MonitorIndex,
            QualityMode = snapshot.QualityMode,
            CaptureEngine = snapshot.CaptureEngine,
            AudioMode = snapshot.AudioMode,
            AudioDeviceName = GetAudioCaptureDeviceName(instance.Index),
            AudioBitrateKbps = snapshot.AudioBitrateKbps,
            AudioSampleRate = snapshot.AudioSampleRate,
            AudioChannels = snapshot.AudioChannels,
            AudioLevelMode = snapshot.AudioLevelMode,
            AudioTargetLevelDb = snapshot.AudioTargetLevelDb,
            DelayBetweenRecordingsSeconds = snapshot.DelayBetweenRecordingsSeconds,
            LagSpikeStartupGraceSeconds = snapshot.LagSpikeStartupGraceSeconds,
            GamePresentationSettingsVersion = _state.Settings.GamePresentationSettingsVersion,
            GamePresentation = CloneGamePresentationSettings(_state.Settings.GamePresentation),
            Progress = CreateBenchmarkProgressNoLock()
        };
    }

    private static string CreateBenchmarkOutputBaseName(BenchmarkAssignmentResult assignment, ReplayQueueRecord replay)
    {
        var suffix = assignment.AssignmentId.StartsWith("benchmark-", StringComparison.OrdinalIgnoreCase)
            ? assignment.AssignmentId.Substring("benchmark-".Length, Math.Min(8, assignment.AssignmentId.Length - "benchmark-".Length))
            : assignment.AssignmentId.Substring(0, Math.Min(8, assignment.AssignmentId.Length));
        return FileNameSanitizer.SanitizeBaseName(
            "benchmark-c" + (assignment.InstanceIndex + 1).ToString(CultureInfo.InvariantCulture) +
            "-" + suffix +
            " - " + CreateOutputBaseName(replay));
    }

    private static string CreateBenchmarkAssignmentId()
    {
        return "benchmark-" + Guid.NewGuid().ToString("N");
    }

    private WorkerAssignmentResponse CreateAssignmentResponse(ReplayQueueRecord replay, WorkerInstanceRecord instance)
    {
        var outputDirectory = ResolveAssignmentOutputDirectoryNoLock(instance);
        Directory.CreateDirectory(outputDirectory);
        return new WorkerAssignmentResponse
        {
            HasAssignment = true,
            AssignmentId = replay.AssignmentId,
            ReplayId = replay.Id,
            ReplayPath = Path.GetFullPath(replay.Path),
            DisableScoreSubmissions = true,
            SuppressScoreSaberReplayUi = true,
            Provider = replay.Provider,
            ReferenceKind = replay.ReferenceKind,
            ReplayFormat = replay.ReplayFormat,
            SourceUrl = replay.SourceUrl,
            ScoreId = replay.ScoreId,
            SongName = replay.SongName,
            Mapper = replay.Mapper,
            PlayerName = replay.PlayerName,
            Difficulty = replay.Difficulty,
            Mode = replay.Mode,
            LevelHash = replay.LevelHash,
            EstimatedSeconds = replay.EstimatedSeconds,
            AssignmentKind = "Replay",
            OutputBaseName = CreateOutputBaseName(replay),
            RecorderHostUrl = instance.RecorderHostUrl,
            OutputDirectory = outputDirectory,
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
            CaptureEngine = _state.Settings.CaptureEngine,
            AudioMode = _state.Settings.AudioMode,
            AudioDeviceName = GetAudioCaptureDeviceName(instance.Index),
            AudioBitrateKbps = _state.Settings.AudioBitrateKbps,
            AudioSampleRate = _state.Settings.AudioSampleRate,
            AudioChannels = _state.Settings.AudioChannels,
            AudioLevelMode = _state.Settings.AudioLevelMode,
            AudioTargetLevelDb = _state.Settings.AudioTargetLevelDb,
            DelayBetweenRecordingsSeconds = _state.Settings.DelayBetweenRecordingsSeconds,
            LagSpikeStartupGraceSeconds = ResolveEffectiveLagSpikeStartupGraceSeconds(_state.Settings.LagSpikeStartupGraceSeconds),
            GamePresentationSettingsVersion = _state.Settings.GamePresentationSettingsVersion,
            GamePresentation = CloneGamePresentationSettings(_state.Settings.GamePresentation),
            Progress = CreateWorkerProgressNoLock()
        };
    }

    private string ResolveAssignmentOutputDirectoryNoLock(WorkerInstanceRecord instance)
    {
        var runOutputDirectory = NormalizeNullable(_state.Run.RecordingOutputDirectory);
        return _state.Run.IsRunning && runOutputDirectory != null
            ? runOutputDirectory
            : instance.OutputDirectory;
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
            CaptureEngine = _state.Settings.CaptureEngine,
            AudioMode = _state.Settings.AudioMode,
            AudioDeviceName = "",
            AudioBitrateKbps = _state.Settings.AudioBitrateKbps,
            AudioSampleRate = _state.Settings.AudioSampleRate,
            AudioChannels = _state.Settings.AudioChannels,
            AudioLevelMode = _state.Settings.AudioLevelMode,
            AudioTargetLevelDb = _state.Settings.AudioTargetLevelDb,
            DelayBetweenRecordingsSeconds = _state.Settings.DelayBetweenRecordingsSeconds,
            LagSpikeStartupGraceSeconds = ResolveEffectiveLagSpikeStartupGraceSeconds(_state.Settings.LagSpikeStartupGraceSeconds),
            GamePresentationSettingsVersion = _state.Settings.GamePresentationSettingsVersion,
            GamePresentation = CloneGamePresentationSettings(_state.Settings.GamePresentation),
            Progress = CreateWorkerProgressNoLock()
        };
    }

    private WorkerRunProgress CreateWorkerProgressNoLock()
    {
        if (_state.Benchmark.IsRunning || _state.Benchmark.CancellationRequested)
        {
            return CreateBenchmarkProgressNoLock();
        }

        return CreateRunProgressNoLock();
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
        return CreateUniqueFilePath(_queueDirectory, fileName);
    }

    private static string CreateUniqueFilePath(string directory, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var targetPath = Path.Combine(directory, safeFileName);
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var extension = Path.GetExtension(safeFileName);
        for (var index = 2; index < 10_000; index++)
        {
            targetPath = Path.Combine(directory, baseName + " (" + index + ")" + extension);
            if (!File.Exists(targetPath))
            {
                return targetPath;
            }
        }

        throw new InvalidOperationException("Could not create a filename for " + safeFileName + ".");
    }

    private static string CreateUniqueRecordingFilePath(string directory, string fileName, string currentPath)
    {
        var currentFullPath = Path.GetFullPath(currentPath);
        var safeFileName = Path.GetFileName(fileName);
        var targetPath = Path.Combine(directory, safeFileName);
        if (IsRecordingFileRenameTargetAvailable(targetPath, currentFullPath))
        {
            return targetPath;
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var extension = Path.GetExtension(safeFileName);
        for (var index = 2; index < 10_000; index++)
        {
            targetPath = Path.Combine(directory, baseName + " (" + index + ")" + extension);
            if (IsRecordingFileRenameTargetAvailable(targetPath, currentFullPath))
            {
                return targetPath;
            }
        }

        throw new InvalidOperationException("Could not create a filename for " + safeFileName + ".");
    }

    private static bool IsRecordingFileRenameTargetAvailable(string targetPath, string currentFullPath)
    {
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (string.Equals(fullTargetPath, currentFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !File.Exists(fullTargetPath);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private void TryDeleteCollectionItemFilesNoLock(MapCollectionItemRecord item)
    {
        var path = NormalizeNullable(item.Path);
        if (path == null)
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!IsPathInsideDirectory(fullPath, _collectionsDirectory))
        {
            return;
        }

        TryDeleteFile(fullPath);
        TryDeleteFile(GetReplaySidecarPath(fullPath));
    }

    private static bool IsSupportedReplayFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".bsor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase);
    }

    private static string Prefer(string? preferred, string? fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback ?? "" : preferred!;
    }

    private ReplayQueueSidecar? TryReadOrDownloadScoreSaberReplayMetadata(
        string replayPath,
        string? scoreId,
        string? playerId,
        string? playerName,
        string? levelHash)
    {
        if (string.IsNullOrWhiteSpace(scoreId))
        {
            if (TryResolveExternalPlayerId(playerId, playerName, replayPath, out var resolvedPlayerId))
            {
                try
                {
                    var resolvedPlayerName = _scoreSaberReplayDownloader
                        .GetPlayerNameByPlayerIdAsync(resolvedPlayerId, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    if (!string.IsNullOrWhiteSpace(resolvedPlayerName))
                    {
                        var sidecar = new ReplayQueueSidecar
                        {
                            Provider = ReplayProvider.ScoreSaber2,
                            ReferenceKind = ReplayReferenceKind.LocalScoreSaberDatFile,
                            ReplayFormat = "ScoreSaber",
                            PlayerName = resolvedPlayerName!,
                            PlayerId = resolvedPlayerId,
                            LevelHash = levelHash ?? "",
                            Path = replayPath
                        };
                        TrySetBeatSaverEstimatedSeconds(sidecar);
                        WriteReplaySidecar(replayPath, sidecar);
                        return sidecar;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (JsonException)
                {
                }
                catch (System.Net.Http.HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }

            return null;
        }

        try
        {
            var metadata = _scoreSaberReplayDownloader
                .GetReplayMetadataByScoreIdAsync(scoreId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (metadata == null)
            {
                return null;
            }

            metadata.Path = replayPath;
            if (string.IsNullOrWhiteSpace(metadata.LevelHash))
            {
                metadata.LevelHash = levelHash ?? "";
            }

            TrySetBeatSaverEstimatedSeconds(metadata);
            WriteReplaySidecar(replayPath, metadata);
            return metadata;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (System.Net.Http.HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void TrySetBeatSaverEstimatedSeconds(ReplayQueueSidecar sidecar)
    {
        if (string.IsNullOrWhiteSpace(sidecar.LevelHash))
        {
            return;
        }

        try
        {
            var songLength = _mapDownloader.GetSongLengthSecondsByHash(sidecar.LevelHash);
            if (songLength > 0)
            {
                sidecar.EstimatedSeconds = songLength.Value;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Net.Http.HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private static bool TryResolveExternalPlayerId(string? playerId, string? playerName, out string resolvedPlayerId)
    {
        resolvedPlayerId = "";
        var candidateId = playerId;
        if (!IsLikelyNumericId(candidateId))
        {
            candidateId = playerName;
            if (!IsLikelyNumericId(candidateId))
            {
                return false;
            }
        }

        resolvedPlayerId = candidateId!;
        return string.IsNullOrWhiteSpace(playerName) || IsLikelyNumericId(playerName);
    }

    private static bool TryResolveExternalPlayerId(string? playerId, string? playerName, string replayPath, out string resolvedPlayerId)
    {
        if (TryResolveExternalPlayerId(playerId, playerName, out var valueFromMetadata))
        {
            resolvedPlayerId = valueFromMetadata;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(playerName) && !IsLikelyNumericId(playerName))
        {
            resolvedPlayerId = "";
            return false;
        }

        return TryResolveExternalPlayerIdFromReplayPath(replayPath, out resolvedPlayerId);
    }

    private static bool TryResolveExternalPlayerIdFromReplayPath(string replayPath, out string resolvedPlayerId)
    {
        resolvedPlayerId = "";
        var fileName = Path.GetFileNameWithoutExtension(replayPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = StripDuplicateSuffix(fileName);
        var tokens = name.Split('-');
        if (tokens.Length < 3)
        {
            return false;
        }

        // Ignore trailing hash segment if present.
        var lastIndex = tokens.Length - 1;
        if (LooksLikeSha1(tokens[lastIndex]))
        {
            lastIndex -= 1;
        }

        // Ignore difficulty/mode fields if they are present.
        lastIndex -= 2;
        if (lastIndex <= 0)
        {
            return false;
        }

        var startIndex = 0;
        if (string.Equals(tokens[0], "scoresaber", StringComparison.OrdinalIgnoreCase))
        {
            startIndex = 1;
        }

        for (var index = startIndex; index <= lastIndex; index++)
        {
            if (IsLikelyNumericId(tokens[index]))
            {
                resolvedPlayerId = tokens[index];
                return true;
            }
        }

        return false;
    }

    private static string StripDuplicateSuffix(string value)
    {
        var suffixStart = value.LastIndexOf(" (", StringComparison.Ordinal);
        if (suffixStart <= 0 || !value.EndsWith(")", StringComparison.Ordinal))
        {
            return value;
        }

        for (var index = suffixStart + 2; index < value.Length - 1; index++)
        {
            var character = value[index];
            if (character < '0' || character > '9')
            {
                return value;
            }
        }

        return value.Substring(0, suffixStart);
    }

    private static bool LooksLikeSha1(string value)
    {
        if (value.Length != 40)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLikelyNumericId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value!.Length <= 8)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character < '0' || character > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static double ResolveEstimatedSeconds(ReplayQueueSidecar? sidecar, double fallback)
    {
        return sidecar != null && sidecar.EstimatedSeconds > 0
            ? sidecar.EstimatedSeconds
            : fallback;
    }

    private static ReplayProvider ResolveProvider(
        ReplayQueueRecord? existing,
        ReplayQueueSidecar? sidecar,
        ReplayQueueItem item)
    {
        if (existing != null && existing.Provider != ReplayProvider.Unknown)
        {
            return existing.Provider;
        }

        if (sidecar != null && sidecar.Provider != ReplayProvider.Unknown)
        {
            return sidecar.Provider;
        }

        return item.Provider == ReplayProvider.Unknown ? ReplayProvider.BeatLeader : item.Provider;
    }

    private static ReplayReferenceKind ResolveReferenceKind(
        ReplayQueueRecord? existing,
        ReplayQueueSidecar? sidecar,
        ReplayQueueItem item)
    {
        if (existing != null && existing.ReferenceKind != ReplayReferenceKind.Unknown)
        {
            return existing.ReferenceKind;
        }

        if (sidecar != null && sidecar.ReferenceKind != ReplayReferenceKind.Unknown)
        {
            return sidecar.ReferenceKind;
        }

        return item.ReferenceKind == ReplayReferenceKind.Unknown ? ReplayReferenceKind.LocalBsorFile : item.ReferenceKind;
    }

    private static string ResolveReplayFormat(
        ReplayQueueRecord? existing,
        ReplayQueueSidecar? sidecar,
        ReplayQueueItem item)
    {
        if (existing != null && !string.IsNullOrWhiteSpace(existing.ReplayFormat))
        {
            return existing.ReplayFormat;
        }

        if (sidecar != null && !string.IsNullOrWhiteSpace(sidecar.ReplayFormat))
        {
            return sidecar.ReplayFormat;
        }

        return item.Provider == ReplayProvider.ScoreSaber2 ? "ScoreSaber" : "BSOR";
    }

    private static void NormalizeReplayProviderFields(ReplayQueueRecord replay)
    {
        if (replay.Provider == ReplayProvider.Unknown)
        {
            var extension = Path.GetExtension(replay.Path);
            replay.Provider = string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase)
                ? ReplayProvider.ScoreSaber2
                : ReplayProvider.BeatLeader;
        }

        if (replay.ReferenceKind == ReplayReferenceKind.Unknown)
        {
            replay.ReferenceKind = replay.Provider == ReplayProvider.ScoreSaber2
                ? ReplayReferenceKind.LocalScoreSaberDatFile
                : ReplayReferenceKind.LocalBsorFile;
        }

        if (string.IsNullOrWhiteSpace(replay.ReplayFormat))
        {
            replay.ReplayFormat = replay.Provider == ReplayProvider.ScoreSaber2 ? "ScoreSaber" : "BSOR";
        }
    }

    private static void NormalizeMapCollectionItemProviderFields(MapCollectionItemRecord item)
    {
        if (item.Provider == ReplayProvider.Unknown)
        {
            var extension = Path.GetExtension(item.Path);
            item.Provider = string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase)
                ? ReplayProvider.ScoreSaber2
                : ReplayProvider.BeatLeader;
        }

        if (item.ReferenceKind == ReplayReferenceKind.Unknown)
        {
            item.ReferenceKind = item.Provider == ReplayProvider.ScoreSaber2
                ? ReplayReferenceKind.LocalScoreSaberDatFile
                : ReplayReferenceKind.LocalBsorFile;
        }

        if (string.IsNullOrWhiteSpace(item.ReplayFormat))
        {
            item.ReplayFormat = item.Provider == ReplayProvider.ScoreSaber2 ? "ScoreSaber" : "BSOR";
        }
    }

    private static MapCollectionItemRecord CreateMapCollectionItem(
        ReplayQueueRecord replay,
        string collectionReplayPath,
        int sequenceNumber)
    {
        return new MapCollectionItemRecord
        {
            Id = CreateStableId(collectionReplayPath),
            SequenceNumber = sequenceNumber,
            Provider = replay.Provider,
            ReferenceKind = replay.ReferenceKind,
            ReplayFormat = replay.ReplayFormat,
            SourceUrl = replay.SourceUrl,
            ScoreId = replay.ScoreId,
            FileName = Path.GetFileName(collectionReplayPath),
            Path = collectionReplayPath,
            SongName = replay.SongName,
            Mapper = replay.Mapper,
            PlayerName = replay.PlayerName,
            Difficulty = replay.Difficulty,
            Mode = replay.Mode,
            LevelHash = replay.LevelHash,
            CoverArtUrl = replay.CoverArtUrl,
            EstimatedSeconds = replay.EstimatedSeconds,
            CompletedOutputPath = string.Equals(replay.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                ? replay.OutputPath
                : null,
            CompletedAtUtc = string.Equals(replay.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                ? replay.CompletedAtUtc
                : null
        };
    }

    private MapCardExportItem CreateMapCardExportItemNoLock(
        MapCollectionRecord collection,
        MapCollectionItemRecord item)
    {
        var replay = CreateReplayRecordForMapLookup(item);
        var levelDirectory = ResolveLevelDirectoryNoLock(replay);
        var local = levelDirectory == null
            ? null
            : LocalMapCardMetadataReader.TryRead(levelDirectory, item.Difficulty, item.Mode, item.EstimatedSeconds);
        var beatSaver = TryGetBeatSaverMapCardMetadata(item);
        var lengthSeconds = FirstPositive(
            local?.LengthSeconds,
            beatSaver?.LengthSeconds,
            item.EstimatedSeconds);
        var coverArtUrl = CreateCollectionCardCoverUrl(collection, item, levelDirectory, beatSaver);
        var metadataSources = new List<string>();
        if (local != null)
        {
            metadataSources.Add("local map");
        }

        if (beatSaver != null)
        {
            metadataSources.Add("BeatSaver");
        }

        return new MapCardExportItem
        {
            Id = item.Id,
            SequenceNumber = item.SequenceNumber,
            SongName = Prefer(item.SongName, Prefer(local?.SongName, beatSaver?.SongName)),
            Artist = Prefer(local?.Artist, beatSaver?.Artist),
            MapAuthor = Prefer(item.Mapper, Prefer(local?.MapAuthor, beatSaver?.MapAuthor)),
            Difficulty = MapCardMetadataText.DisplayDifficulty(Prefer(item.Difficulty, Prefer(local?.Difficulty, beatSaver?.Difficulty))),
            Mode = Prefer(item.Mode, Prefer(local?.Mode, beatSaver?.Mode)),
            NotesPerSecond = local?.NotesPerSecond ?? beatSaver?.NotesPerSecond,
            BeatsPerMinute = FirstPositive(local?.BeatsPerMinute, beatSaver?.BeatsPerMinute),
            NoteCount = local?.NoteCount ?? beatSaver?.NoteCount,
            LengthSeconds = lengthSeconds,
            BeatSaverKey = Prefer(TryExtractBeatSaverKey(item.SourceUrl), beatSaver?.BeatSaverKey),
            LevelHash = item.LevelHash,
            CoverArtUrl = coverArtUrl,
            Category = NormalizeMapCardCategory(item.MapCardCategory),
            MetadataStatus = metadataSources.Count > 0 ? "Ready" : "Partial",
            MetadataDetail = metadataSources.Count > 0
                ? "Enriched from " + string.Join(" and ", metadataSources) + "."
                : "Only replay metadata was available."
        };
    }

    private BeatSaverMapCardMetadata? TryGetBeatSaverMapCardMetadata(MapCollectionItemRecord item)
    {
        if (string.IsNullOrWhiteSpace(item.LevelHash))
        {
            return null;
        }

        try
        {
            return _mapDownloader.GetMapCardMetadataByHash(item.LevelHash, item.Difficulty, item.Mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string CreateCollectionCardCoverUrl(
        MapCollectionRecord collection,
        MapCollectionItemRecord item,
        string? levelDirectory,
        BeatSaverMapCardMetadata? beatSaver)
    {
        if (!string.IsNullOrWhiteSpace(levelDirectory) &&
            !string.IsNullOrWhiteSpace(FindCoverInDirectory(levelDirectory)))
        {
            return "/api/collections/" + Uri.EscapeDataString(collection.Id) +
                   "/items/" + Uri.EscapeDataString(item.Id) + "/cover";
        }

        return Prefer(beatSaver?.CoverArtUrl, item.CoverArtUrl);
    }

    private static ReplayQueueRecord CreateReplayRecordForMapLookup(MapCollectionItemRecord item)
    {
        return new ReplayQueueRecord
        {
            Id = item.Id,
            SequenceNumber = item.SequenceNumber,
            Provider = item.Provider,
            ReferenceKind = item.ReferenceKind,
            ReplayFormat = item.ReplayFormat,
            SourceUrl = item.SourceUrl,
            ScoreId = item.ScoreId,
            FileName = item.FileName,
            Path = item.Path,
            SongName = item.SongName,
            Mapper = item.Mapper,
            PlayerName = item.PlayerName,
            Difficulty = item.Difficulty,
            Mode = item.Mode,
            LevelHash = item.LevelHash,
            CoverArtUrl = item.CoverArtUrl,
            EstimatedSeconds = item.EstimatedSeconds
        };
    }

    private static double FirstPositive(params double?[] values)
    {
        foreach (var value in values)
        {
            if (value.HasValue && value.Value > 0)
            {
                return Math.Round(value.Value, 2);
            }
        }

        return 0;
    }

    private static string TryExtractBeatSaverKey(string? url)
    {
        var text = NormalizeNullable(url);
        if (text == null || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return "";
        }

        if (!uri.Host.Contains("beatsaver", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "beatmap", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segments[index], "maps", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return segments.LastOrDefault() ?? "";
    }

    private static ReplayQueueSidecar CreateReplaySidecar(ReplayQueueRecord replay, string replayPath)
    {
        return new ReplayQueueSidecar
        {
            Provider = replay.Provider,
            ReferenceKind = replay.ReferenceKind,
            ReplayFormat = replay.ReplayFormat,
            Path = replayPath,
            SourceUrl = replay.SourceUrl,
            ScoreId = replay.ScoreId,
            HasReplay = true,
            PlayerName = replay.PlayerName,
            SongName = replay.SongName,
            Mapper = replay.Mapper,
            Difficulty = replay.Difficulty,
            Mode = replay.Mode,
            LevelHash = replay.LevelHash,
            CoverArtUrl = replay.CoverArtUrl,
            EstimatedSeconds = replay.EstimatedSeconds
        };
    }

    private static ReplayQueueSidecar CreateReplaySidecar(MapCollectionItemRecord item, string replayPath)
    {
        return new ReplayQueueSidecar
        {
            Provider = item.Provider,
            ReferenceKind = item.ReferenceKind,
            ReplayFormat = item.ReplayFormat,
            Path = replayPath,
            SourceUrl = item.SourceUrl,
            ScoreId = item.ScoreId,
            HasReplay = true,
            PlayerName = item.PlayerName,
            SongName = item.SongName,
            Mapper = item.Mapper,
            Difficulty = item.Difficulty,
            Mode = item.Mode,
            LevelHash = item.LevelHash,
            CoverArtUrl = item.CoverArtUrl,
            EstimatedSeconds = item.EstimatedSeconds
        };
    }

    private void ApplyCollectionImportOrderNoLock(IReadOnlyList<string> importedPaths)
    {
        if (importedPaths.Count == 0)
        {
            return;
        }

        var importOrder = importedPaths
            .Select((path, index) => new { Path = path, Index = index })
            .ToDictionary(item => item.Path, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var existing = _state.Queue
            .Where(item => !importOrder.ContainsKey(Path.GetFullPath(item.Path)))
            .ToList();
        var imported = _state.Queue
            .Where(item => importOrder.ContainsKey(Path.GetFullPath(item.Path)))
            .OrderBy(item => importOrder[Path.GetFullPath(item.Path)])
            .ToList();

        _state.Queue = existing.Concat(imported).ToList();
        ResequenceQueueNoLock();
    }

    private static bool IsRecordedReplay(ReplayQueueRecord replay)
    {
        return string.Equals(replay.Status, "Completed", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateMatchingCollectionCompletionNoLock(ReplayQueueRecord replay, DateTimeOffset completedAtUtc)
    {
        var replayKey = CreateReplayTargetKey(replay);
        if (string.IsNullOrWhiteSpace(replayKey))
        {
            return;
        }

        var outputPath = NormalizeNullable(replay.OutputPath);
        foreach (var collection in _state.Collections)
        {
            var changed = false;
            foreach (var item in collection.Items)
            {
                if (!string.Equals(CreateReplayTargetKey(item), replayKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                item.CompletedOutputPath = outputPath;
                item.CompletedAtUtc = completedAtUtc;
                changed = true;
            }

            if (changed)
            {
                collection.UpdatedAtUtc = completedAtUtc;
            }
        }
    }

    private static bool IsRecordedCollectionItem(MapCollectionItemRecord item)
    {
        var outputPath = NormalizeNullable(item.CompletedOutputPath);
        return item.CompletedAtUtc != null &&
               (outputPath == null || File.Exists(outputPath));
    }

    private static string CreateReplayTargetKey(ReplayQueueRecord replay)
    {
        return CreateReplayTargetKey(
            replay.LevelHash,
            replay.Difficulty,
            replay.Mode,
            replay.Provider,
            replay.ScoreId,
            replay.SourceUrl,
            replay.FileName);
    }

    private static string CreateReplayTargetKey(MapCollectionItemRecord item)
    {
        return CreateReplayTargetKey(
            item.LevelHash,
            item.Difficulty,
            item.Mode,
            item.Provider,
            item.ScoreId,
            item.SourceUrl,
            item.FileName);
    }

    private static string CreateReplayTargetKey(
        string levelHash,
        string difficulty,
        string mode,
        ReplayProvider provider,
        string scoreId,
        string sourceUrl,
        string fileName)
    {
        var normalizedHash = NormalizeKeyPart(levelHash);
        if (!string.IsNullOrWhiteSpace(normalizedHash))
        {
            return "map|" + normalizedHash + "|" + NormalizeKeyPart(difficulty) + "|" + NormalizeKeyPart(mode);
        }

        var normalizedScoreId = NormalizeKeyPart(scoreId);
        if (!string.IsNullOrWhiteSpace(normalizedScoreId))
        {
            return "score|" + ((int)provider).ToString(CultureInfo.InvariantCulture) + "|" + normalizedScoreId;
        }

        var normalizedSourceUrl = NormalizeKeyPart(sourceUrl);
        if (!string.IsNullOrWhiteSpace(normalizedSourceUrl))
        {
            return "source|" + normalizedSourceUrl;
        }

        return "file|" + NormalizeKeyPart(fileName);
    }

    private RecordingFileRenameResult RenameRecordingFilesNoLock(
        IEnumerable<RecordingRenameTarget> targets,
        string format,
        string eventLabel,
        string eventTag)
    {
        var renamedCount = 0;
        var skippedCount = 0;
        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            var outputPath = NormalizeNullable(target.OutputPath);
            if (outputPath == null)
            {
                skippedCount++;
                continue;
            }

            var fullOutputPath = Path.GetFullPath(outputPath);
            if (!visitedPaths.Add(fullOutputPath))
            {
                continue;
            }

            if (TryRenameRecordingFileNoLock(target, format, fullOutputPath, out var renamedPath))
            {
                UpdateRecordingOutputPathReferencesNoLock(fullOutputPath, renamedPath);
                renamedCount++;
            }
            else
            {
                skippedCount++;
            }
        }

        if (renamedCount > 0)
        {
            AddEventNoLock(
                "Info",
                eventTag,
                "Renamed " + renamedCount + " " + eventLabel + (renamedCount == 1 ? "" : "s") +
                " using " + DescribeRecordingRenameFormat(format) +
                (skippedCount > 0 ? " (" + skippedCount + " skipped)." : "."),
                null);
            SaveNoLock();
        }

        return new RecordingFileRenameResult
        {
            State = Clone(_state),
            RenamedCount = renamedCount,
            SkippedCount = skippedCount
        };
    }

    private RecordingFileRenamePreviewResult CreateRecordingRenamePreviewNoLock(RecordingRenameTarget? target)
    {
        var result = new RecordingFileRenamePreviewResult();
        if (target == null)
        {
            return result;
        }

        var metadata = ResolveRecordingRenameMetadataNoLock(target);
        result.SourceLabel = NormalizeNullable(metadata.SongName) ??
                             NormalizeNullable(target.SongName) ??
                             NormalizeNullable(Path.GetFileNameWithoutExtension(target.FileName)) ??
                             "Recording";

        foreach (var format in RecordingRenameFormats)
        {
            var baseName = NormalizeNullable(CreateRecordingRenameBaseName(target, format, metadata)) ??
                           CreateDefaultRecordingBaseName(target);
            result.Examples[format] = FileNameSanitizer.SanitizeBaseName(baseName);
        }

        return result;
    }

    private bool TryRenameRecordingFileNoLock(
        RecordingRenameTarget target,
        string format,
        string fullOutputPath,
        out string renamedPath)
    {
        renamedPath = "";
        if (!File.Exists(fullOutputPath) || !IsSupportedRecordingFileName(fullOutputPath))
        {
            return false;
        }

        var baseName = NormalizeNullable(CreateRecordingRenameBaseNameNoLock(target, format));
        if (baseName == null)
        {
            return false;
        }

        var extension = Path.GetExtension(fullOutputPath);
        var targetFileName = FileNameSanitizer.SanitizeBaseName(baseName) + extension;
        var targetPath = CreateUniqueRecordingFilePath(
            Path.GetDirectoryName(fullOutputPath) ?? _state.Settings.RecordingOutputDirectory,
            targetFileName,
            fullOutputPath);
        if (string.Equals(fullOutputPath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            File.Move(fullOutputPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        renamedPath = Path.GetFullPath(targetPath);
        return true;
    }

    private void UpdateRecordingOutputPathReferencesNoLock(string oldOutputPath, string newOutputPath)
    {
        foreach (var replay in _state.Queue)
        {
            if (PathEquals(replay.OutputPath, oldOutputPath))
            {
                replay.OutputPath = newOutputPath;
            }
        }

        foreach (var collection in _state.Collections)
        {
            var changed = false;
            foreach (var item in collection.Items)
            {
                if (!PathEquals(item.CompletedOutputPath, oldOutputPath))
                {
                    continue;
                }

                item.CompletedOutputPath = newOutputPath;
                changed = true;
            }

            if (changed)
            {
                collection.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    private RecordingRenameTarget CreateRecordingRenameTarget(ReplayQueueRecord replay)
    {
        return new RecordingRenameTarget
        {
            SequenceNumber = replay.SequenceNumber,
            OutputPath = replay.OutputPath,
            FileName = replay.FileName,
            SongName = replay.SongName,
            Mapper = replay.Mapper,
            PlayerName = replay.PlayerName,
            SourceUrl = replay.SourceUrl,
            LevelHash = replay.LevelHash,
            Difficulty = replay.Difficulty,
            Mode = replay.Mode,
            EstimatedSeconds = replay.EstimatedSeconds
        };
    }

    private RecordingRenameTarget CreateRecordingRenameTarget(MapCollectionItemRecord item)
    {
        return new RecordingRenameTarget
        {
            SequenceNumber = item.SequenceNumber,
            OutputPath = item.CompletedOutputPath,
            FileName = item.FileName,
            SongName = item.SongName,
            Mapper = item.Mapper,
            PlayerName = item.PlayerName,
            SourceUrl = item.SourceUrl,
            LevelHash = item.LevelHash,
            Difficulty = item.Difficulty,
            Mode = item.Mode,
            EstimatedSeconds = item.EstimatedSeconds
        };
    }

    private string CreateRecordingRenameBaseNameNoLock(RecordingRenameTarget target, string format)
    {
        var metadata = ResolveRecordingRenameMetadataNoLock(target);
        return CreateRecordingRenameBaseName(target, format, metadata);
    }

    private static string CreateRecordingRenameBaseName(
        RecordingRenameTarget target,
        string format,
        RecordingRenameMetadata metadata)
    {
        var candidate = format switch
        {
            "Key" => metadata.Key,
            "KeySong" => JoinRecordingNameParts(metadata.Key, metadata.SongName),
            "Song" => metadata.SongName,
            "SongArtist" => JoinRecordingNameParts(metadata.SongName, metadata.Artist),
            "SongArtistPlayer" => JoinRecordingNameParts(metadata.SongName, metadata.Artist, metadata.PlayerName),
            "SongPlayer" => JoinRecordingNameParts(metadata.SongName, metadata.PlayerName),
            "SongMapper" => JoinRecordingNameParts(metadata.SongName, metadata.Mapper),
            "SongDifficulty" => JoinRecordingNameParts(metadata.SongName, metadata.Difficulty),
            "PlayerSong" => JoinRecordingNameParts(metadata.PlayerName, metadata.SongName),
            _ => CreateDefaultRecordingBaseName(target)
        };

        return NormalizeNullable(candidate) ?? CreateDefaultRecordingBaseName(target);
    }

    private RecordingRenameMetadata ResolveRecordingRenameMetadataNoLock(RecordingRenameTarget target)
    {
        BeatSaverMapCardMetadata? beatSaver = null;
        if (!string.IsNullOrWhiteSpace(target.LevelHash))
        {
            try
            {
                beatSaver = _mapDownloader.GetMapCardMetadataByHash(target.LevelHash, target.Difficulty, target.Mode);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException)
            {
            }
        }

        var replay = new ReplayQueueRecord
        {
            FileName = target.FileName,
            SongName = target.SongName,
            Mapper = target.Mapper,
            Difficulty = target.Difficulty,
            Mode = target.Mode,
            LevelHash = target.LevelHash,
            EstimatedSeconds = target.EstimatedSeconds
        };
        var levelDirectory = ResolveLevelDirectoryNoLock(replay);
        var local = levelDirectory == null
            ? null
            : LocalMapCardMetadataReader.TryRead(
                levelDirectory,
                target.Difficulty,
                target.Mode,
                target.EstimatedSeconds);

        return new RecordingRenameMetadata
        {
            Key = ResolveRecordingSongKey(target, beatSaver),
            SongName = Prefer(target.SongName, Prefer(local?.SongName, beatSaver?.SongName)),
            Artist = Prefer(local?.Artist, beatSaver?.Artist),
            Mapper = Prefer(target.Mapper, Prefer(local?.MapAuthor, beatSaver?.MapAuthor)),
            PlayerName = NormalizeNullable(target.PlayerName) ?? "",
            Difficulty = MapCardMetadataText.DisplayDifficulty(target.Difficulty)
        };
    }

    private static string CreateDefaultRecordingBaseName(RecordingRenameTarget target)
    {
        var songName = NormalizeNullable(target.SongName) ??
                       NormalizeNullable(Path.GetFileNameWithoutExtension(target.FileName)) ??
                       "recording";
        var difficulty = NormalizeNullable(target.Difficulty) == null
            ? ""
            : " [" + target.Difficulty + "]";
        var prefix = target.SequenceNumber > 0
            ? target.SequenceNumber.ToString("000", CultureInfo.InvariantCulture) + " - "
            : "";
        return prefix + songName + difficulty;
    }

    private static string ResolveRecordingSongKey(
        RecordingRenameTarget target,
        BeatSaverMapCardMetadata? beatSaver)
    {
        var beatSaverKey = NormalizeNullable(TryExtractBeatSaverKey(target.SourceUrl));
        if (beatSaverKey != null)
        {
            return beatSaverKey;
        }

        beatSaverKey = NormalizeNullable(beatSaver?.BeatSaverKey);
        if (beatSaverKey != null)
        {
            return beatSaverKey;
        }

        if (!string.IsNullOrWhiteSpace(target.LevelHash))
        {
            return target.LevelHash.Trim().ToUpperInvariant();
        }

        return "";
    }

    private static string JoinRecordingNameParts(params string?[] parts)
    {
        return string.Join(
            " - ",
            parts
                .Select(NormalizeNullable)
                .Where(value => value != null)
                .Cast<string>());
    }

    private static string NormalizeRecordingRenameFormat(string? format)
    {
        var normalized = (format ?? "")
            .Trim()
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "key" or "keyonly" or "songkey" => "Key",
            "keysong" or "keyandsong" => "KeySong",
            "song" or "songonly" => "Song",
            "songartist" or "songartistname" => "SongArtist",
            "songartistplayer" or "songartistplayername" => "SongArtistPlayer",
            "songplayer" or "songplayername" => "SongPlayer",
            "songmapper" or "songmapauthor" => "SongMapper",
            "songdifficulty" or "songdiff" => "SongDifficulty",
            "playersong" or "playernamesong" => "PlayerSong",
            _ => "Default"
        };
    }

    private static string DescribeRecordingRenameFormat(string format)
    {
        return format switch
        {
            "Key" => "key only",
            "KeySong" => "key + song",
            "Song" => "song only",
            "SongArtist" => "song + artist",
            "SongArtistPlayer" => "song + artist + player",
            "SongPlayer" => "song + player",
            "SongMapper" => "song + mapper",
            "SongDifficulty" => "song + difficulty",
            "PlayerSong" => "player + song",
            _ => "default names"
        };
    }

    private static bool IsSupportedRecordingFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathEquals(string? left, string right)
    {
        var normalizedLeft = NormalizeNullable(left);
        return normalizedLeft != null &&
               string.Equals(Path.GetFullPath(normalizedLeft), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKeyPart(string? value)
    {
        return (value ?? "").Trim().ToUpperInvariant();
    }

    private static string GetReplaySidecarPath(string replayPath)
    {
        return replayPath + ".metadata.json";
    }

    private static ReplayQueueSidecar? ReadReplaySidecar(string replayPath)
    {
        var sidecarPath = GetReplaySidecarPath(replayPath);
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(sidecarPath);
            return JsonSerializer.Deserialize<ReplayQueueSidecar>(json, JsonOptions.Default);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteReplaySidecar(string replayPath, ReplayQueueSidecar metadata)
    {
        metadata.Path = replayPath;
        File.WriteAllText(GetReplaySidecarPath(replayPath), JsonSerializer.Serialize(metadata, JsonOptions.Default));
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

    private static bool IsWorkerConflictingModRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return WorkerConflictingModRelativePaths.Any(conflictingPath =>
            string.Equals(normalized, conflictingPath, StringComparison.OrdinalIgnoreCase));
    }

    private void AddEventNoLock(
        string kind,
        string tag,
        string text,
        string? replayId = null,
        int? instanceIndex = null,
        DateTimeOffset? nowOverride = null)
    {
        _state.Events ??= new List<ControlPanelEventRecord>();
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        _state.LastActivityUtc = now;
        _state.Events.Insert(0, new ControlPanelEventRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now,
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

    private bool IsActiveForIdleShutdownNoLock()
    {
        if (_state.Run.IsRunning || _state.Run.CancellationRequested)
        {
            return true;
        }

        return _state.Queue.Any(replay => IsActiveReplayStatus(replay.Status)) ||
               _state.Instances.Any(instance =>
                   !string.IsNullOrWhiteSpace(instance.ActiveAssignmentId) ||
                   !string.IsNullOrWhiteSpace(instance.CurrentReplayId) ||
                   IsRecordingStatus(instance.Status));
    }

    private static string FormatIdleTimeout(TimeSpan timeout)
    {
        var totalMinutes = Math.Max(1, (int)Math.Round(timeout.TotalMinutes));
        return totalMinutes.ToString(CultureInfo.InvariantCulture) +
               " minute" +
               (totalMinutes == 1 ? "" : "s");
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

    private static MapCollectionRecord CloneMapCollection(MapCollectionRecord collection)
    {
        var json = JsonSerializer.Serialize(collection, JsonOptions.Default);
        return JsonSerializer.Deserialize<MapCollectionRecord>(json, JsonOptions.Default)
               ?? new MapCollectionRecord();
    }

    private static ControlPanelSettings CloneSettings(ControlPanelSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions.Default);
        return JsonSerializer.Deserialize<ControlPanelSettings>(json, JsonOptions.Default)
               ?? new ControlPanelSettings();
    }

    private static FfmpegSetupReport CloneFfmpegSetup(FfmpegSetupReport report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions.Default);
        return JsonSerializer.Deserialize<FfmpegSetupReport>(json, JsonOptions.Default)
               ?? new FfmpegSetupReport();
    }

    private static GamePresentationSettings CloneGamePresentationSettings(GamePresentationSettings? settings)
    {
        var clone = new GamePresentationSettings
        {
            NoHud = settings?.NoHud ?? true,
            LoadPlayerEnvironment = settings?.LoadPlayerEnvironment ?? false,
            LoadPlayerJumpDistance = settings?.LoadPlayerJumpDistance ?? false,
            OverrideReplayPlayerSettings = settings?.OverrideReplayPlayerSettings ?? false,
            RestorePlayerSettingsOnExit = settings?.RestorePlayerSettingsOnExit ?? false,
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
            LeftSaberColor = settings?.LeftSaberColor ?? "#a82020",
            RightSaberColor = settings?.RightSaberColor ?? "#2064a8",
            LightColorA = settings?.LightColorA ?? "#ff3030",
            LightColorB = settings?.LightColorB ?? "#c03030",
            BoostLightColorA = settings?.BoostLightColorA ?? "#ff3030",
            BoostLightColorB = settings?.BoostLightColorB ?? "#c03030",
            WallColor = settings?.WallColor ?? "#3098ff",
            NoteJumpDurationType = settings?.NoteJumpDurationType ?? GamePresentationSettings.NoteJumpDurationTypeDynamic,
            NoteJumpFixedDuration = settings?.NoteJumpFixedDuration ?? 0.2f,
            NoteJumpStartBeatOffset = settings?.NoteJumpStartBeatOffset ?? 0f,
            ApplyJdFixerSettings = settings?.ApplyJdFixerSettings ?? false,
            JdFixerMode = settings?.JdFixerMode ?? GamePresentationSettings.JdFixerModeReactionTime,
            JdFixerJumpDistance = settings?.JdFixerJumpDistance ?? 18f,
            JdFixerReactionTime = settings?.JdFixerReactionTime ?? 450f,
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
               normalizedLeft.OverrideReplayPlayerSettings == normalizedRight.OverrideReplayPlayerSettings &&
               normalizedLeft.RestorePlayerSettingsOnExit == normalizedRight.RestorePlayerSettingsOnExit &&
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
               string.Equals(normalizedLeft.LeftSaberColor, normalizedRight.LeftSaberColor, StringComparison.Ordinal) &&
               string.Equals(normalizedLeft.RightSaberColor, normalizedRight.RightSaberColor, StringComparison.Ordinal) &&
               string.Equals(normalizedLeft.LightColorA, normalizedRight.LightColorA, StringComparison.Ordinal) &&
               string.Equals(normalizedLeft.LightColorB, normalizedRight.LightColorB, StringComparison.Ordinal) &&
               string.Equals(normalizedLeft.BoostLightColorA, normalizedRight.BoostLightColorA, StringComparison.Ordinal) &&
               string.Equals(normalizedLeft.BoostLightColorB, normalizedRight.BoostLightColorB, StringComparison.Ordinal) &&
               string.Equals(normalizedLeft.WallColor, normalizedRight.WallColor, StringComparison.Ordinal) &&
               string.Equals(normalizedLeft.NoteJumpDurationType, normalizedRight.NoteJumpDurationType, StringComparison.Ordinal) &&
               normalizedLeft.NoteJumpFixedDuration == normalizedRight.NoteJumpFixedDuration &&
               normalizedLeft.NoteJumpStartBeatOffset == normalizedRight.NoteJumpStartBeatOffset &&
               normalizedLeft.ApplyJdFixerSettings == normalizedRight.ApplyJdFixerSettings &&
               string.Equals(normalizedLeft.JdFixerMode, normalizedRight.JdFixerMode, StringComparison.Ordinal) &&
               normalizedLeft.JdFixerJumpDistance == normalizedRight.JdFixerJumpDistance &&
               normalizedLeft.JdFixerReactionTime == normalizedRight.JdFixerReactionTime &&
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
               string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase) ||
               IsFinalizingStatus(status);
    }

    private static bool IsActiveReplayStatus(string status)
    {
        return string.Equals(status, "Assigned", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase) ||
               IsFinalizingStatus(status);
    }

    private static bool IsRecordingStatus(string status)
    {
        return string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Playing", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Started", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFinalizingStatus(string status)
    {
        return string.Equals(status, "Finalizing", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Saving", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Stopping", StringComparison.OrdinalIgnoreCase);
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

        if (IsFinalizingStatus(normalized))
        {
            return "Finalizing";
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

    private static double? NormalizeFramesPerSecond(double? value)
    {
        if (!value.HasValue ||
            double.IsNaN(value.Value) ||
            double.IsInfinity(value.Value) ||
            value.Value <= 0)
        {
            return null;
        }

        return Math.Round(value.Value, 1);
    }

    private static string FormatFramesPerSecond(double value)
    {
        return Math.Round(value, 1).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? CombineWarnings(params string?[] warnings)
    {
        var normalized = warnings
            .Select(NormalizeNullable)
            .Where(value => value != null)
            .Cast<string>()
            .ToList();
        return normalized.Count == 0 ? null : string.Join(" ", normalized);
    }

    private static string NormalizeMapCardCategory(string? value)
    {
        var normalized = NormalizeNullable(value)?.ToLowerInvariant();
        return normalized switch
        {
            "standard" => "standard",
            "accuracy" => "accuracy",
            "tech" => "tech",
            "speed" => "speed",
            "extreme" => "extreme",
            _ => ""
        };
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

    private static string CreateManagedWorkerId(int index)
    {
        return "managed-worker-" + index.ToString("00", CultureInfo.InvariantCulture);
    }

    private static bool IsManagedWorkerId(string workerId)
    {
        return workerId.StartsWith("managed-worker-", StringComparison.OrdinalIgnoreCase);
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

    private static class NativeWindowPlacement
    {
        private const int GwlStyle = -16;
        private const int SwRestore = 9;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const long WsPopup = 0x80000000L;
        private const long WsVisible = 0x10000000L;
        private const long WsOverlappedWindow = 0x00CF0000L;

        public static Rect? TryGetMonitorBounds(int monitorIndex)
        {
            var monitors = new List<Rect>();
            var success = NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect monitorRect, IntPtr data) =>
                {
                    var monitorInfo = new MonitorInfo
                    {
                        Size = Marshal.SizeOf<MonitorInfo>()
                    };
                    if (NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
                    {
                        monitors.Add(monitorInfo.Monitor);
                    }

                    return true;
                },
                IntPtr.Zero);

            if (!success || monitorIndex < 0 || monitorIndex >= monitors.Count)
            {
                return null;
            }

            return monitors[monitorIndex];
        }

        public static IntPtr FindWindowForProcess(int processId)
        {
            var found = IntPtr.Zero;
            NativeMethods.EnumWindows(
                (hWnd, lParam) =>
                {
                    if (!NativeMethods.IsWindowVisible(hWnd))
                    {
                        return true;
                    }

                    NativeMethods.GetWindowThreadProcessId(hWnd, out var windowProcessId);
                    if (windowProcessId != processId)
                    {
                        return true;
                    }

                    found = hWnd;
                    return false;
                },
                IntPtr.Zero);
            return found;
        }

        public static PlacementPlan CreatePlacementPlan(Rect monitor, int instanceIndex, int columns, int rows)
        {
            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);
            var tileWidth = Math.Max(1, (monitor.Right - monitor.Left) / columns);
            var tileHeight = Math.Max(1, (monitor.Bottom - monitor.Top) / rows);
            var column = Math.Max(0, instanceIndex) % columns;
            var row = Math.Max(0, instanceIndex) / columns;
            return new PlacementPlan(
                monitor.Left + column * tileWidth,
                monitor.Top + row * tileHeight,
                tileWidth,
                tileHeight);
        }

        public static PlacementPlan CreateFixedSizePlacementPlan(
            Rect monitor,
            int instanceIndex,
            int requestedWidth,
            int requestedHeight)
        {
            var monitorWidth = Math.Max(1, monitor.Right - monitor.Left);
            var monitorHeight = Math.Max(1, monitor.Bottom - monitor.Top);
            var width = Math.Clamp(requestedWidth, 1, monitorWidth);
            var height = Math.Clamp(requestedHeight, 1, monitorHeight);
            var columns = Math.Max(1, monitorWidth / width);
            var rows = Math.Max(1, monitorHeight / height);
            var slotCount = Math.Max(1, columns * rows);
            var slot = Math.Clamp(instanceIndex, 0, slotCount - 1);
            var column = slot % columns;
            var row = slot / columns;
            return new PlacementPlan(
                monitor.Left + column * width,
                monitor.Top + row * height,
                width,
                height);
        }

        public static void Apply(IntPtr hWnd, PlacementPlan plan)
        {
            NativeMethods.ShowWindow(hWnd, SwRestore);
            var currentStyle = NativeMethods.GetWindowLongPtr(hWnd, GwlStyle).ToInt64();
            var windowedStyle = (currentStyle & ~WsOverlappedWindow) | WsPopup | WsVisible;
            if (windowedStyle != currentStyle)
            {
                NativeMethods.SetWindowLongPtr(hWnd, GwlStyle, new IntPtr(windowedStyle));
            }

            NativeMethods.SetWindowPos(
                hWnd,
                IntPtr.Zero,
                plan.Left,
                plan.Top,
                plan.Width,
                plan.Height,
                SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
        }

        public readonly record struct PlacementPlan(int Left, int Top, int Width, int Height);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;
        }

        private static class NativeMethods
        {
            public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

            public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool EnumDisplayMonitors(
                IntPtr hdc,
                IntPtr lprcClip,
                MonitorEnumProc lpfnEnum,
                IntPtr dwData);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

            [DllImport("user32.dll")]
            public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

            [DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
            public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

            [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
            public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(
                IntPtr hWnd,
                IntPtr hWndInsertAfter,
                int x,
                int y,
                int cx,
                int cy,
                uint flags);

        }
    }

    private sealed class ReferenceImportDownloadResult
    {
        public List<string> ImportedPaths { get; } = new List<string>();

        public List<string> Failures { get; } = new List<string>();
    }

    private sealed class RecordingRenameTarget
    {
        public int SequenceNumber { get; set; }

        public string? OutputPath { get; set; }

        public string FileName { get; set; } = "";

        public string SongName { get; set; } = "";

        public string Mapper { get; set; } = "";

        public string PlayerName { get; set; } = "";

        public string SourceUrl { get; set; } = "";

        public string LevelHash { get; set; } = "";

        public string Difficulty { get; set; } = "";

        public string Mode { get; set; } = "";

        public double EstimatedSeconds { get; set; }
    }

    private sealed class RecordingRenameMetadata
    {
        public string Key { get; set; } = "";

        public string SongName { get; set; } = "";

        public string Artist { get; set; } = "";

        public string Mapper { get; set; } = "";

        public string PlayerName { get; set; } = "";

        public string Difficulty { get; set; } = "";
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
