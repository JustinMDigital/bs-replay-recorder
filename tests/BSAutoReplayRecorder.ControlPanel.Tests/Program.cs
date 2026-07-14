using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.ControlPanel;
using Microsoft.AspNetCore.Http;

var tempRoot = Path.Combine(Path.GetTempPath(), "bsarr-control-panel-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    RunStartGuardCheck(Path.Combine(tempRoot, "guard"));
    RunRecorderHostHealthGuardCheck(Path.Combine(tempRoot, "recorder-host-health-guard"));
    RunBeatLeaderReadinessGuardCheck(Path.Combine(tempRoot, "beatleader-readiness-guard"));
    RunBeatLeaderInitializingStartCheck(Path.Combine(tempRoot, "beatleader-initializing-start"));
    RunWindowsGraphicsCaptureCapabilityGuardCheck(Path.Combine(tempRoot, "wgc-capability-guard"));
    RunParallelAssignmentCheck(Path.Combine(tempRoot, "parallel"));
    RunFourInstanceAssignmentCheck(Path.Combine(tempRoot, "parallel-four"));
    RunImportedQueuePlanDistributionCheck(Path.Combine(tempRoot, "queue-plan-distribution"));
    RunEnabledInstanceQueuePlanDistributionCheck(Path.Combine(tempRoot, "enabled-queue-plan-distribution"));
    RunActiveInstanceCountQueuePlanDistributionCheck(Path.Combine(tempRoot, "active-count-queue-plan-distribution"));
    RunConfiguredInstanceAssignmentCheck(Path.Combine(tempRoot, "configured-instances"));
    RunActiveRunInstanceSettingsGuardCheck(Path.Combine(tempRoot, "active-run-settings-guard"));
    RunHeartbeatFinalizingFpsDoesNotCancelCheck(Path.Combine(tempRoot, "heartbeat-finalizing-fps"));
    RunHeartbeatFpsLagSpikeCancellationCheck(Path.Combine(tempRoot, "heartbeat-fps-lag-spike"));
    RunBenchmarkRecommendationAndQueueIsolationCheck(Path.Combine(tempRoot, "benchmark-recommendation"));
    RunBenchmarkStopCheck(Path.Combine(tempRoot, "benchmark-stop"));
    RunBenchmarkHeartbeatFpsCancellationCheck(Path.Combine(tempRoot, "benchmark-fps-cancel"));
    RunBenchmarkHighAverageFpsDoesNotCancelCheck(Path.Combine(tempRoot, "benchmark-fps-average"));
    RunBenchmarkFinalizingFpsDoesNotCancelCheck(Path.Combine(tempRoot, "benchmark-finalizing-fps"));
    RunBenchmarkSelectedConcurrencyCheck(Path.Combine(tempRoot, "benchmark-selected-concurrency"));
    RunBenchmarkStartGuardCheck(Path.Combine(tempRoot, "benchmark-guards"));
    RunSingleReplayFailureDoesNotCancelOtherAssignmentsCheck(Path.Combine(tempRoot, "single-failure"));
    RunAllConcurrentReplayFailuresCancelQueuedRunCheck(Path.Combine(tempRoot, "all-concurrent-failed"));
    RunWorkerProgressContractCheck(Path.Combine(tempRoot, "worker-progress"));
    RunLaunchPlanCheck(Path.Combine(tempRoot, "launch-plan"));
    RunDisplayLabelFormattingCheck();
    RunInstanceDisplayNameNormalizationCheck(Path.Combine(tempRoot, "instance-display-name"));
    RunDefaultLaunchArgumentsCheck(Path.Combine(tempRoot, "default-launch-args"));
    RunStopBroadcastCheck(Path.Combine(tempRoot, "stop-broadcast"));
    RunCloseGamesAfterQueueCheck(Path.Combine(tempRoot, "close-games-after-queue"));
    RunStaleCanceledRunFinalizesOnLoadCheck(Path.Combine(tempRoot, "stale-canceled-run"));
    RunIdleShutdownCheck(Path.Combine(tempRoot, "idle-shutdown"));
    RunRecordingOutputDirectoryCheck(Path.Combine(tempRoot, "recording-output"));
    RunPerRunRecordingOutputDirectoryCheck(Path.Combine(tempRoot, "per-run-recording-output"));
    RunLocalSettingsFileCheck(Path.Combine(tempRoot, "local-settings-file"));
    RunSetupSourcePathDetectorCheck(Path.Combine(tempRoot, "setup-source-path"));
    RunLaunchPresetNormalizationCheck();
    RunFixedWindowPlacementCheck();
    RunBeatSaberWindowedSettingsFileCheck(Path.Combine(tempRoot, "beat-saber-window-settings"));
    RunCaptureLayoutValidatorCheck();
    RunCapturePreflightFailureClassificationCheck();
    RunCapturePreflightStartRunGuardCheck(Path.Combine(tempRoot, "capture-preflight-run-guard"));
    RunFfmpegSetupInstallCheck(Path.Combine(tempRoot, "ffmpeg-setup"));
    RunAudioLevelNormalizationCheck();
    RunAudioLevelSettingsUpdateCheck(Path.Combine(tempRoot, "audio-level-update"));
    RunGamePresentationSettingsSyncCheck(Path.Combine(tempRoot, "game-presentation-sync"));
    RunRequireAudioGuardCheck(Path.Combine(tempRoot, "require-audio-guard"));
    RunProcessLoopbackAudioGuardCheck(Path.Combine(tempRoot, "process-loopback-audio-guard"));
    RunLaunchValidationCheck(Path.Combine(tempRoot, "launch-validation"));
    RunSteamLaunchCrashMessageCheck(Path.Combine(tempRoot, "steam-launch-crash-message"));
    RunBeatSaberBlackScreenLaunchGuardCheck(Path.Combine(tempRoot, "black-screen-launch-guard"));
    RunBeatSaberBlackScreenRotatedLogGuardCheck(Path.Combine(tempRoot, "black-screen-rotated-log-guard"));
    RunSingleInstanceLaunchPluginInstallScopeCheck(Path.Combine(tempRoot, "single-launch-install-scope"));
    RunDisplayScaleOnlyAppliesOnRunCheck(Path.Combine(tempRoot, "display-scale-run-boundary"));
    RunWorkerPluginSettingsIdentityCheck();
    RunWorkerPluginInstallerBeatLeaderGuardCheck(Path.Combine(tempRoot, "worker-plugin-beatleader-guard"));
    RunDuplicateManagedWorkerIdRegistrationCheck(Path.Combine(tempRoot, "duplicate-managed-worker-id"));
    RunManagedInstanceProvisioningCheck(Path.Combine(tempRoot, "managed-instance-provisioning"));
    RunInstanceBaselineCheck(Path.Combine(tempRoot, "instance-baseline"));
    RunModIntegrationCatalogCheck(Path.Combine(tempRoot, "mod-integration-catalog"));
    RunSongFolderLinksCheck(Path.Combine(tempRoot, "song-folder-links"));
    RunQueueCoverArtCheck(Path.Combine(tempRoot, "queue-cover-art"));
    RunQueueMapImportCheck(Path.Combine(tempRoot, "queue-map-import"));
    RunScoreSaberRichTextPlayerNameFormattingCheck();
    RunBeatLeaderReferenceImportCheck(Path.Combine(tempRoot, "beatleader-reference-import"));
    RunBeatLeaderScoreUrlDownloaderCheck(Path.Combine(tempRoot, "beatleader-score-url-downloader"));
    RunScoreSaberReferenceImportCheck(Path.Combine(tempRoot, "scoresaber-reference-import"));
    RunLocalScoreSaberImportMetadataEnrichmentCheck(Path.Combine(tempRoot, "scoresaber-local-metadata-import"));
    RunLocalScoreSaberFilenameFallbackCheck(Path.Combine(tempRoot, "scoresaber-local-filename-fallback"));
    RunLocalScoreSaberLongPlayerIdCheck(Path.Combine(tempRoot, "scoresaber-local-long-player-id"));
    RunMixedProviderReplayIntegrationCheck(Path.Combine(tempRoot, "mixed-provider-integration"));
    RunQueueEditingCheck(Path.Combine(tempRoot, "queue-editing"));
    RunReplayCalibrationCheck(Path.Combine(tempRoot, "replay-calibration"));
    RunDiskSpaceAndEventLogCheck(Path.Combine(tempRoot, "disk-events"));
    RunCompletedRecordingUriCheck(Path.Combine(tempRoot, "recording-uri"));
    RunRenameCompletedQueueRecordingsCheck(Path.Combine(tempRoot, "rename-used-recordings"));
    RunRequeueAllQueueItemsCheck(Path.Combine(tempRoot, "requeue-all"));
    RunMapCollectionSaveLoadCheck(Path.Combine(tempRoot, "map-collections"));
    RunMapCollectionCardExportCheck(Path.Combine(tempRoot, "map-card-export"));
    RunCompletedRecordingAudioVerificationCheck(Path.Combine(tempRoot, "recording-audio-verification"));
    RunCompletedRecordingSyncVerificationCheck(Path.Combine(tempRoot, "recording-sync-verification"));
    RunCompletedRecordingBookmarkChapterEmbeddingCheck(Path.Combine(tempRoot, "recording-bookmark-chapters"));
    Console.WriteLine("All control panel checks passed.");
}
finally
{
    DeleteTempRoot(tempRoot);
}

static void RunStartGuardCheck(string workspace)
{
    var store = CreateStore(workspace);
    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });

    AssertThrows<InvalidOperationException>(
        () => store.StartRun(),
        "require all workers start guard");
}

static void RunRecorderHostHealthGuardCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        recorderHostHealthChecker: new FakeRecorderHostHealthChecker(false));
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });

    AssertThrows<InvalidOperationException>(
        () => store.StartRun(),
        "recorder host health start guard");
}

static void RunBeatLeaderReadinessGuardCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0,
        ReplayProviderStatusReported = true,
        BeatLeaderReady = false,
        BeatLeaderStatus = "BeatLeader test unavailable",
        ScoreSaberReady = true,
        ScoreSaberStatus = "ScoreSaber test ready"
    });

    AssertThrows<InvalidOperationException>(
        () => store.StartRun(),
        "beatleader readiness start guard");
}

static void RunBeatLeaderInitializingStartCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    var worker = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0,
        ReplayProviderStatusReported = true,
        BeatLeaderReady = false,
        BeatLeaderStatus = "BeatLeader ReplayerMenuLoader is not available yet",
        ScoreSaberReady = true,
        ScoreSaberStatus = "ScoreSaber test ready"
    });

    store.StartRun();
    AssertEqual(true, store.GetAssignment(worker.WorkerId).HasAssignment, "beatleader initialization allows run start");
}

static void RunWindowsGraphicsCaptureCapabilityGuardCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        captureEngine: "WindowsGraphicsCapture",
        recorderHostHealthChecker: new FakeRecorderHostHealthChecker(true, wgcSupported: false));
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0,
        ReplayProviderStatusReported = true,
        BeatLeaderReady = true,
        BeatLeaderStatus = "BeatLeader test ready"
    });

    AssertThrows<InvalidOperationException>(
        () => store.StartRun(),
        "wgc capability start guard");
}

static void RunLaunchPlanCheck(string workspace)
{
    var instancesRoot = Path.Combine(workspace, "BSInstances");
    var store = CreateStore(workspace, instancesRoot, "Test I-");
    var snapshot = store.Snapshot();

    AssertEqual(
        Path.GetFullPath(Path.Combine(instancesRoot, "Test I-1")),
        snapshot.Instances[0].LaunchDirectory,
        "first launch directory");
    AssertEqual("--no-yeet fpfc", snapshot.Instances[0].LaunchArguments, "first launch arguments");
}

static void RunLaunchValidationCheck(string workspace)
{
    var instancesRoot = Path.Combine(workspace, "BSInstances");
    var store = CreateStore(
        workspace,
        instancesRoot,
        "Missing I-",
        workerPluginInstaller: new FakeWorkerPluginInstaller());

    var state = store.LaunchInstance(0);
    AssertEqual("Failed", state.Instances[0].GameLaunchStatus, "missing folder launch status");
    AssertContains("Beat Saber.exe was not found", state.Instances[0].GameLaunchError, "missing executable launch error");

    Directory.CreateDirectory(Path.Combine(instancesRoot, "Missing I-1"));
    state = store.LaunchInstance(0);
    AssertEqual("Failed", state.Instances[0].GameLaunchStatus, "missing exe launch status");
    AssertContains("Beat Saber.exe was not found", state.Instances[0].GameLaunchError, "missing exe launch error");
}

static void RunSteamLaunchCrashMessageCheck(string workspace)
{
    var instancesRoot = Path.Combine(workspace, "BSInstances");
    CreateFakeBeatSaberInstance(instancesRoot, "Steam I-1", 0);
    var store = CreateStore(
        workspace,
        instancesRoot,
        "Steam I-",
        instanceCount: 1,
        workerPluginInstaller: new FakeWorkerPluginInstaller());
    var launchDirectory = store.Snapshot().Instances[0].LaunchDirectory;
    WriteFakeFile(
        launchDirectory,
        "Logs/2026.07.01.12.00.00.log",
        """
        [ERROR @ 12:00:03 | UnityEngine] [Steamworks.NET] SteamAPI_Init() failed.
        [CRITICAL @ 12:00:05 | UnityEngine] InvalidOperationException: DLC Promo Panel was not initialized because could not initialize platform.
        """);

    SetStartedGameProcessId(store, 0, 987654, DateTimeOffset.UtcNow);

    var state = store.Snapshot();
    AssertEqual("Failed", state.Instances[0].GameLaunchStatus, "steam launch crash status");
    AssertContains("Open Steam", state.Instances[0].GameLaunchError, "steam launch crash error");
    var launchEvent = state.Events.FirstOrDefault(item =>
        string.Equals(item.Kind, "Bad", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.Tag, "Launch", StringComparison.OrdinalIgnoreCase));
    if (launchEvent == null)
    {
        throw new InvalidOperationException("steam launch crash event failed. Expected bad launch event.");
    }

    AssertContains("Open Steam", launchEvent.Text, "steam launch crash event");
}

static void RunBeatSaberBlackScreenLaunchGuardCheck(string workspace)
{
    var instancesRoot = Path.Combine(workspace, "BSInstances");
    CreateFakeBeatSaberInstance(instancesRoot, "BlackScreen I-1", 0);
    var store = CreateStore(
        workspace,
        instancesRoot,
        "BlackScreen I-",
        instanceCount: 1,
        workerPluginInstaller: new FakeWorkerPluginInstaller());
    var launchDirectory = store.Snapshot().Instances[0].LaunchDirectory;
    var log = new StringBuilder();
    log.AppendLine("[DEBUG @ 12:00:01 | BeatLeader] OnMenuInstaller");
    for (var index = 0; index < 25; index++)
    {
        log.AppendLine("[CRITICAL @ 12:00:02 | UnityEngine] NullReferenceException: Object reference not set to an instance of an object");
        log.AppendLine("[CRITICAL @ 12:00:02 | UnityEngine] VRController.get_thumbstick () (at <game>:0)");
        log.AppendLine("[CRITICAL @ 12:00:02 | UnityEngine] VRController.get_triggerValue () (at <game>:0)");
        log.AppendLine("[CRITICAL @ 12:00:02 | UnityEngine] DeactivateVRControllersOnFocusCapture.UpdateVRControllerActiveState () (at <game>:0)");
    }

    WriteFakeFile(launchDirectory, "Logs/2026.07.01.12.00.01.log", log.ToString());

    SetStartedGameProcessId(store, 0, 987655, DateTimeOffset.UtcNow);

    var state = store.Snapshot();
    AssertEqual("Failed", state.Instances[0].GameLaunchStatus, "black screen launch status");
    AssertContains("black screen", state.Instances[0].GameLaunchError, "black screen launch error");
    AssertContains("VR controller", state.Instances[0].GameLaunchError, "black screen launch diagnosis");
    var launchEvent = state.Events.FirstOrDefault(item =>
        string.Equals(item.Kind, "Bad", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.Tag, "Launch", StringComparison.OrdinalIgnoreCase));
    if (launchEvent == null)
    {
        throw new InvalidOperationException("black screen launch event failed. Expected bad launch event.");
    }

    AssertContains("black screen", launchEvent.Text, "black screen launch event");
}

static void RunBeatSaberBlackScreenRotatedLogGuardCheck(string workspace)
{
    var instancesRoot = Path.Combine(workspace, "BSInstances");
    CreateFakeBeatSaberInstance(instancesRoot, "BlackScreen I-1", 0);
    var store = CreateStore(
        workspace,
        instancesRoot,
        "BlackScreen I-",
        instanceCount: 1,
        workerPluginInstaller: new FakeWorkerPluginInstaller());
    var launchDirectory = store.Snapshot().Instances[0].LaunchDirectory;
    var log = new StringBuilder();
    log.AppendLine("[DEBUG @ 12:00:01 | BeatLeader] OnMenuInstaller");
    for (var index = 0; index < 25; index++)
    {
        log.AppendLine("[CRITICAL @ 12:00:02 | UnityEngine] NullReferenceException: Object reference not set to an instance of an object");
        log.AppendLine("[CRITICAL @ 12:00:02 | UnityEngine] VRController.get_thumbstick () (at <game>:0)");
        log.AppendLine("[CRITICAL @ 12:00:02 | UnityEngine] VRController.get_triggerValue () (at <game>:0)");
        log.AppendLine("[CRITICAL @ 12:00:02 | UnityEngine] DeactivateVRControllersOnFocusCapture.UpdateVRControllerActiveState () (at <game>:0)");
    }

    var logDirectory = Path.Combine(launchDirectory, "Logs");
    Directory.CreateDirectory(logDirectory);
    using (var file = File.Create(Path.Combine(logDirectory, "2026.07.01.12.00.01.log.gz")))
    using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
    using (var writer = new StreamWriter(gzip))
    {
        writer.Write(log.ToString());
    }

    SetStartedGameProcessId(store, 0, 987656, DateTimeOffset.UtcNow);

    var state = store.Snapshot();
    AssertEqual("Failed", state.Instances[0].GameLaunchStatus, "black screen rotated log launch status");
    AssertContains("black screen", state.Instances[0].GameLaunchError, "black screen rotated log launch error");
    AssertContains("VR controller", state.Instances[0].GameLaunchError, "black screen rotated log diagnosis");
}

static void RunSingleInstanceLaunchPluginInstallScopeCheck(string workspace)
{
    var instancesRoot = Path.Combine(workspace, "BSInstances");
    CreateFakeBeatSaberInstance(instancesRoot, "Scope I-1", 0);
    CreateFakeBeatSaberInstance(instancesRoot, "Scope I-2", 1);
    var pluginInstaller = new FakeWorkerPluginInstaller();
    var store = CreateStore(
        workspace,
        instancesRoot,
        "Scope I-",
        instanceCount: 2,
        workerPluginInstaller: pluginInstaller);

    store.LaunchInstance(1);

    AssertEqual(1, pluginInstaller.InstallCount, "single launch worker plugin install count");
    AssertEqual("0,1", string.Join(",", pluginInstaller.LastContextIndexes), "single launch worker plugin context indexes");
    AssertEqual("1", string.Join(",", pluginInstaller.LastDeployTargetIndexes), "single launch worker plugin target indexes");
}

static void RunDisplayScaleOnlyAppliesOnRunCheck(string workspace)
{
    var previousSetDpiPath = Environment.GetEnvironmentVariable("BSARR_SETDPI_PATH");
    try
    {
        Environment.SetEnvironmentVariable(
            "BSARR_SETDPI_PATH",
            Path.Combine(workspace, "missing-setdpi", "SetDpi.exe"));

        var launchWorkspace = Path.Combine(workspace, "manual-launch");
        var launchInstancesRoot = Path.Combine(launchWorkspace, "BSInstances");
        CreateFakeBeatSaberInstance(launchInstancesRoot, "Scale I-1", 0);
        var launchStore = CreateStore(
            launchWorkspace,
            launchInstancesRoot,
            "Scale I-",
            instanceCount: 1,
            workerPluginInstaller: new FakeWorkerPluginInstaller(),
            manageDisplayScale: true);

        var launched = launchStore.LaunchInstance(0);
        AssertEqual(true, launched.Settings.ManageDisplayScale, "manual launch keeps display scale management enabled");
        AssertEqual(false, launched.Run.DisplayScaleRestorePending, "manual launch does not arm display scale restore");

        var runWorkspace = Path.Combine(workspace, "run-start");
        var runStore = CreateStore(
            runWorkspace,
            instanceCount: 1,
            manageDisplayScale: true);
        using var files = CreateReplayFiles(1);
        runStore.ImportFiles(files.Collection);
        RegisterWorkers(runStore, "display-scale-worker", count: 1);
        SetGameProcessIds(runStore, 4100);

        var started = runStore.StartRun();
        AssertEqual(false, started.Settings.ManageDisplayScale, "run start handles missing display scale helper");
        AssertEqual(false, started.Run.DisplayScaleRestorePending, "missing display scale helper does not arm restore");
    }
    finally
    {
        Environment.SetEnvironmentVariable("BSARR_SETDPI_PATH", previousSetDpiPath);
    }
}

static void RunDisplayLabelFormattingCheck()
{
    var primaryLabel = WindowsDisplayInfoProvider.BuildDisplayLabel(0, "Acer VG240Y", 1920, 1080, true);
    AssertEqual(
        "Monitor 1 - Acer VG240Y - 1080p (1920 x 1080) - Primary",
        primaryLabel,
        "primary display label");

    var fallbackLabel = WindowsDisplayInfoProvider.BuildDisplayLabel(1, "", 2560, 1440, false);
    AssertEqual(
        "Monitor 2 - 1440p (2560 x 1440)",
        fallbackLabel,
        "fallback display label");
}

static void RunLocalSettingsFileCheck(string workspace)
{
    Directory.CreateDirectory(workspace);
    var settingsPath = Path.Combine(workspace, "settings.json");
    File.WriteAllText(
        settingsPath,
        """
        {
          "controlPanelUrl": "http://127.0.0.1:5999",
          "workspace": "LocalWorkspace",
          "beatSaberInstancesRoot": "Instances",
          "sourceBeatSaberPath": "Steam/Beat Saber",
          "instanceCount": 2,
          "maxConcurrentRecordings": 1
        }
        """);

    var previousSettingsPath = Environment.GetEnvironmentVariable("BSARR_SETTINGS_PATH");
    var previousDirectory = Directory.GetCurrentDirectory();
    try
    {
        Environment.SetEnvironmentVariable("BSARR_SETTINGS_PATH", settingsPath);
        Directory.SetCurrentDirectory(Path.GetTempPath());

        var settings = LocalSettingsFile.LoadOrDefault();
        settings.Normalize();

        AssertEqual("http://127.0.0.1:5999", settings.BindUrl, "local settings bind URL alias");
        AssertEqual(
            Path.GetFullPath(Path.Combine(workspace, "LocalWorkspace")),
            settings.WorkspaceDirectory,
            "local settings workspace path");
        AssertEqual(
            Path.GetFullPath(Path.Combine(workspace, "Instances")),
            settings.BeatSaberInstancesRoot,
            "local settings instances path");
        AssertEqual(
            Path.GetFullPath(Path.Combine(workspace, "Steam", "Beat Saber")),
            settings.SourceBeatSaberPath,
            "local settings source path");
        AssertEqual(2, settings.InstanceCount, "local settings instance count");
        AssertEqual(2, settings.MaxConcurrentRecordings, "local settings max concurrent follows instance count");
    }
    finally
    {
        Environment.SetEnvironmentVariable("BSARR_SETTINGS_PATH", previousSettingsPath);
        Directory.SetCurrentDirectory(previousDirectory);
    }
}

static void RunSetupSourcePathDetectorCheck(string workspace)
{
    var library = Path.Combine(workspace, "SteamLibrary");
    var detectedSource = Path.Combine(library, "steamapps", "common", "Beat Saber");
    Directory.CreateDirectory(detectedSource);
    File.WriteAllText(Path.Combine(detectedSource, "Beat Saber.exe"), "");
    Directory.CreateDirectory(Path.Combine(detectedSource, "Beat Saber_Data", "Plugins", "x86_64"));
    File.WriteAllText(Path.Combine(detectedSource, "Beat Saber_Data", "Plugins", "x86_64", "steam_api64.dll"), "");

    var detected = SetupSourcePathDetector.Detect("", new[] { library });
    AssertEqual("Detected", detected.Status, "setup source detected status");
    AssertEqual(Path.GetFullPath(detectedSource), detected.DetectedSourceBeatSaberPath, "setup source detected path");
    AssertEqual(Path.GetFullPath(detectedSource), detected.EffectiveSourceBeatSaberPath, "setup source effective detected path");

    var configuredSource = Path.Combine(workspace, "Configured", "Beat Saber");
    Directory.CreateDirectory(configuredSource);
    File.WriteAllText(Path.Combine(configuredSource, "Beat Saber.exe"), "");

    var configured = SetupSourcePathDetector.Detect(configuredSource, new[] { library });
    AssertEqual("PrerequisitesMissing", configured.Status, "setup source configured prerequisite status");
    AssertContains("BSIPA", string.Join(" ", configured.ConfiguredSourceMissingPrerequisites), "setup source configured missing BSIPA");
    AssertContains("BeatLeader", string.Join(" ", configured.ConfiguredSourceMissingPrerequisites), "setup source configured missing BeatLeader");
    AssertEqual(Path.GetFullPath(configuredSource), configured.EffectiveSourceBeatSaberPath, "setup source configured wins");

    Directory.CreateDirectory(Path.Combine(configuredSource, "Plugins"));
    File.WriteAllText(Path.Combine(configuredSource, "IPA.exe"), "");
    File.WriteAllText(Path.Combine(configuredSource, "winhttp.dll"), "");
    File.WriteAllText(Path.Combine(configuredSource, "Plugins", "BeatLeader.dll"), "");
    configured = SetupSourcePathDetector.Detect(configuredSource, new[] { library });
    AssertEqual("Ready", configured.Status, "setup source configured ready status");
    AssertEqual(true, configured.ConfiguredSourceRecorderReady, "setup source configured recorder readiness");

    Directory.CreateDirectory(Path.Combine(configuredSource, "Beat Saber_Data", "Plugins", "x86_64"));
    File.WriteAllText(Path.Combine(configuredSource, "Beat Saber_Data", "Plugins", "x86_64", "steam_api64.dll"), "");
    AssertEqual(
        BeatSaberStore.Steam,
        SetupSourcePathDetector.InferStoreFromDirectory(configuredSource, BeatSaberStore.MetaPc),
        "setup source file evidence overrides stale configured store");

    var bsManagerRoot = Path.Combine(workspace, "BSManager");
    foreach (var version in new[] { "1.39.1", "1.40.6", "1.40.8" })
    {
        var source = Path.Combine(bsManagerRoot, "BSInstances", version);
        Directory.CreateDirectory(Path.Combine(source, "Plugins"));
        Directory.CreateDirectory(Path.Combine(source, "Beat Saber_Data", "Plugins", "x86_64"));
        File.WriteAllText(Path.Combine(source, "Beat Saber.exe"), "");
        File.WriteAllText(Path.Combine(source, "IPA.exe"), "");
        File.WriteAllText(Path.Combine(source, "winhttp.dll"), "");
        File.WriteAllText(Path.Combine(source, "Plugins", "BeatLeader.dll"), "");
        File.WriteAllText(Path.Combine(source, "Beat Saber_Data", "Plugins", "x86_64", "steam_api64.dll"), "");
    }
    var bsManagerDetected = SetupSourcePathDetector.DetectWithBsManagerRoots(
        "",
        Array.Empty<string>(),
        Array.Empty<string>(),
        new[] { bsManagerRoot });
    AssertEqual(3, bsManagerDetected.DetectedSources.Count, "supported BSManager source count");
    var bsManagerCandidate = bsManagerDetected.DetectedSources.Single(candidate => candidate.Version == "1.40.6");
    AssertEqual("BSManager", bsManagerCandidate.SourceType, "BSManager source type");
    AssertEqual("BSManager", bsManagerCandidate.DisplayName, "BSManager source display name");
    AssertEqual("1.40.6", bsManagerCandidate.Version, "BSManager version from instance folder");
    AssertEqual(BeatSaberStore.Steam, bsManagerCandidate.Store, "BSManager source store inference");
    AssertEqual(true, bsManagerCandidate.RecorderReady, "BSManager recorder-ready source");
    AssertEqual(true, bsManagerDetected.DetectedSources.All(candidate => candidate.RecorderReady), "all bundled BSManager versions ready");
    AssertEqual(
        "bs-1.39.1",
        SetupSourcePathDetector.ResolveWorkerPluginBuild(Path.Combine(bsManagerRoot, "BSInstances", "1.39.1")),
        "BSManager 1.39.1 plugin build");
    AssertEqual(
        "bs-1.40.8",
        SetupSourcePathDetector.ResolveWorkerPluginBuild(Path.Combine(bsManagerRoot, "BSInstances", "1.40.8")),
        "BSManager 1.40.8 plugin build");

    var metaLibrary = Path.Combine(workspace, "MetaLibrary");
    var metaSource = Path.Combine(metaLibrary, "Software", "hyperbolic-magnetism-beat-saber");
    Directory.CreateDirectory(Path.Combine(metaSource, "Plugins"));
    File.WriteAllText(Path.Combine(metaSource, "Beat Saber.exe"), "");
    File.WriteAllText(Path.Combine(metaSource, "IPA.exe"), "");
    File.WriteAllText(Path.Combine(metaSource, "winhttp.dll"), "");
    File.WriteAllText(Path.Combine(metaSource, "Plugins", "BeatLeader.dll"), "");
    Directory.CreateDirectory(Path.Combine(metaLibrary, "Manifests"));
    File.WriteAllText(
        Path.Combine(metaLibrary, "Manifests", "hyperbolic-magnetism-beat-saber.json"),
        "{\"version\":\"1.44.1_20239\"}");

    var both = SetupSourcePathDetector.Detect("", new[] { library }, new[] { metaLibrary });
    AssertEqual("SelectionRequired", both.Status, "multiple detected stores require selection");
    AssertEqual(2, both.DetectedSources.Count, "both store candidates reported");
    var meta = both.DetectedSources.Single(candidate => candidate.Store == BeatSaberStore.MetaPc);
    AssertEqual("1.44.1_20239", meta.Version, "meta manifest version");
    AssertEqual(true, meta.VersionSupported, "supported meta version reported");
    AssertEqual("bs-1.44.1", SetupSourcePathDetector.ResolveWorkerPluginBuild(metaSource, BeatSaberStore.MetaPc), "meta worker plugin build");

    var configuredMeta = SetupSourcePathDetector.Detect(
        metaSource,
        new[] { library },
        new[] { metaLibrary },
        BeatSaberStore.MetaPc);
    AssertEqual(BeatSaberStore.MetaPc, configuredMeta.EffectiveSourceStore, "configured Meta source store");
    AssertEqual(BeatSaberStore.MetaPc, configuredMeta.EffectiveSourceStore, "configured Meta source store");
    AssertEqual(BeatSaberStore.Steam, SetupSourcePathDetector.InferStoreFromDirectory(detectedSource), "steam source store inference");

    var steamStartInfo = new System.Diagnostics.ProcessStartInfo();
    ControlPanelStore.ApplyStoreLaunchEnvironment(steamStartInfo, BeatSaberStore.Steam);
    AssertEqual("620980", steamStartInfo.Environment["SteamAppId"], "steam launch environment app id");
    var metaStartInfo = new System.Diagnostics.ProcessStartInfo();
    ControlPanelStore.ApplyStoreLaunchEnvironment(metaStartInfo, BeatSaberStore.MetaPc);
    AssertEqual(false, metaStartInfo.Environment.ContainsKey("SteamAppId"), "Meta launch does not inject Steam app id");

    var missing = SetupSourcePathDetector.Detect(Path.Combine(workspace, "Missing"), Array.Empty<string>());
    AssertEqual("Missing", missing.Status, "setup source missing status");
    AssertEqual("", missing.EffectiveSourceBeatSaberPath, "setup source missing effective path");
}

static void RunManagedInstanceProvisioningCheck(string workspace)
{
    var sourceRoot = Path.Combine(workspace, "Source");
    CreateFakeBeatSaberInstance(sourceRoot, "Beat Saber", 0);
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "settings.ini", "root settings");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/settings.ini", "user settings");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/PlayerData.dat", "player");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "Beat Saber_Data/CustomLevels/source-song.txt", "song");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "Beat Saber_Data/CustomWIPLevels/source-wip.txt", "wip");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "Beat Saber_Data/CustomLevels.local-20260604185400/backup-song.txt", "backup song");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "Beat Saber_Data/CustomWIPLevels.local-20260604185400/backup-wip.txt", "backup wip");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/BSWorldCupReplayRecorder/Recordings/old-legacy.mkv", "legacy recording");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/BSAutoReplayRecorder/Recordings/old-managed.mkv", "managed recording");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/BeatLeader/Replays/long-replay-history-file.bsor", "old replay");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/LocalLeaderboard/Replays/long-local-replay-history-file.bsor", "old local replay");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/ScoreSaber/Replays/long-scoresaber-replay-history-file.dat", "old scoresaber replay");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "Plugins/BeatSaverDownloader.dll", "beatsaver downloader");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "Plugins/DataPuller.dll", "data puller");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/BeatSaverDownloader.ini", "beatsaver downloader settings");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "UserData/DataPuller.json", "data puller settings");
    WriteFakeFile(Path.Combine(sourceRoot, "Beat Saber"), "Logs/output.log", "log");
    var sourceDirectory = Path.Combine(sourceRoot, "Beat Saber");
    var instancesRoot = Path.Combine(workspace, "ManagedInstances");
    var pluginInstaller = new FakeWorkerPluginInstaller();
    var store = CreateStore(
        workspace,
        instancesRoot,
        "Managed I-",
        instanceCount: 3,
        requireMatchingInstanceBaseline: true,
        workerPluginInstaller: pluginInstaller);

    var state = store.ProvisionManagedInstances(new InstanceProvisionRequest
    {
        SourceBeatSaberPath = sourceDirectory
    });

    AssertEqual("Ready", state.InstanceProvision.Status, "provision status");
    AssertEqual(1, pluginInstaller.InstallCount, "worker plugin install count");
    AssertEqual(false, state.InstanceProvision.CopyExistingSongs, "provision clean song import flag");
    AssertEqual(3, state.InstanceProvision.Instances.Count, "provision instance count");
    AssertEqual("Matched", state.InstanceBaseline.Status, "provision baseline status");
    AssertEqual("Linked", state.SongFolders.Status, "provision repairs song folder links");
    for (var index = 0; index < 3; index++)
    {
        var directory = Path.Combine(instancesRoot, "Managed I-" + (index + 1));
        AssertEqual(true, File.Exists(Path.Combine(directory, "Beat Saber.exe")), "provision copied exe " + index);
        AssertEqual("installed plugin", File.ReadAllText(Path.Combine(directory, "Plugins", "BSAutoReplayRecorder.Plugin.dll")), "provision installed plugin " + index);
        AssertEqual("installed core", File.ReadAllText(Path.Combine(directory, "Libs", "BSAutoReplayRecorder.Core.dll")), "provision installed core " + index);
        AssertContains("\"ControlPanelWorker\"", File.ReadAllText(Path.Combine(directory, "UserData", "BSAutoReplayRecorder", "settings.json")), "provision installed worker settings " + index);
        AssertSettingsIniBackup(directory, "settings.ini", "root settings", "provision backed up root settings.ini " + index);
        AssertSettingsIniBackup(directory, Path.Combine("UserData", "settings.ini"), "user settings", "provision backed up UserData settings.ini " + index);
        AssertEqual(true, File.Exists(Path.Combine(directory, "UserData", "PlayerData.dat")), "provision copied user data " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "Beat Saber_Data", "CustomLevels", "source-song.txt")), "provision skipped CustomLevels " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "Beat Saber_Data", "CustomWIPLevels", "source-wip.txt")), "provision skipped CustomWIPLevels " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "Beat Saber_Data", "CustomLevels.local-20260604185400", "backup-song.txt")), "provision skipped CustomLevels local backup " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "Beat Saber_Data", "CustomWIPLevels.local-20260604185400", "backup-wip.txt")), "provision skipped CustomWIPLevels local backup " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "UserData", "BSWorldCupReplayRecorder", "Recordings", "old-legacy.mkv")), "provision skipped legacy recorder output " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "UserData", "BSAutoReplayRecorder", "Recordings", "old-managed.mkv")), "provision skipped managed recorder output " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "UserData", "BeatLeader", "Replays", "long-replay-history-file.bsor")), "provision skipped BeatLeader replay history " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "UserData", "LocalLeaderboard", "Replays", "long-local-replay-history-file.bsor")), "provision skipped LocalLeaderboard replay history " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "UserData", "ScoreSaber", "Replays", "long-scoresaber-replay-history-file.dat")), "provision skipped ScoreSaber replay history " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "Plugins", "BeatSaverDownloader.dll")), "provision skipped BeatSaver Downloader " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "Plugins", "DataPuller.dll")), "provision skipped DataPuller " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "UserData", "BeatSaverDownloader.ini")), "provision skipped BeatSaver Downloader settings " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "UserData", "DataPuller.json")), "provision skipped DataPuller settings " + index);
        AssertEqual(false, File.Exists(Path.Combine(directory, "Logs", "output.log")), "provision skipped logs " + index);
    }

    AssertThrows<InvalidOperationException>(
        () => store.ProvisionManagedInstances(new InstanceProvisionRequest
        {
            SourceBeatSaberPath = sourceDirectory
        }),
        "provision refuses existing folders without overwrite");

    var selectedCountInstancesRoot = Path.Combine(workspace, "ManagedInstancesSelectedCount");
    var selectedCountStore = CreateStore(
        Path.Combine(workspace, "selected-count-workspace"),
        selectedCountInstancesRoot,
        "Managed I-",
        instanceCount: 3,
        workerPluginInstaller: new FakeWorkerPluginInstaller());
    var selectedCountState = selectedCountStore.ProvisionManagedInstances(new InstanceProvisionRequest
    {
        SourceBeatSaberPath = sourceDirectory,
        InstanceCount = 2
    });

    AssertEqual(2, selectedCountState.Settings.InstanceCount, "provision selected instance count setting");
    AssertEqual(2, selectedCountState.Instances.Count, "provision selected instance state count");
    AssertEqual(2, selectedCountState.InstanceProvision.Instances.Count, "provision selected instance report count");
    AssertEqual(true, File.Exists(Path.Combine(selectedCountInstancesRoot, "Managed I-1", "Beat Saber.exe")), "selected count creates first instance");
    AssertEqual(true, File.Exists(Path.Combine(selectedCountInstancesRoot, "Managed I-2", "Beat Saber.exe")), "selected count creates second instance");
    AssertEqual(false, Directory.Exists(Path.Combine(selectedCountInstancesRoot, "Managed I-3")), "selected count does not create third instance");

    var quadInstancesRoot = Path.Combine(workspace, "ManagedInstancesQuad");
    var quadStore = CreateStore(
        Path.Combine(workspace, "quad-workspace"),
        quadInstancesRoot,
        "Managed I-",
        instanceCount: 4,
        workerPluginInstaller: new FakeWorkerPluginInstaller());
    var quadState = quadStore.ProvisionManagedInstances(new InstanceProvisionRequest
    {
        SourceBeatSaberPath = sourceDirectory,
        InstanceCount = 4
    });
    AssertEqual(4, quadState.Settings.InstanceCount, "quad provision desired instance count");
    AssertEqual(4, quadState.InstanceProvision.Instances.Count, "quad provision report count");
    AssertEqual(true, File.Exists(Path.Combine(quadInstancesRoot, "Managed I-4", "Beat Saber.exe")), "quad provision creates fourth instance");

    var upgradeInstancesRoot = Path.Combine(workspace, "ManagedInstancesUpgrade");
    var upgradePluginInstaller = new FakeWorkerPluginInstaller();
    var upgradeStore = CreateStore(
        Path.Combine(workspace, "upgrade-workspace"),
        upgradeInstancesRoot,
        "Managed I-",
        instanceCount: 1,
        maxConcurrentRecordings: 1,
        workerPluginInstaller: upgradePluginInstaller);
    var initialUpgradeState = upgradeStore.ProvisionManagedInstances(new InstanceProvisionRequest
    {
        SourceBeatSaberPath = sourceDirectory,
        InstanceCount = 1
    });
    AssertEqual(1, initialUpgradeState.InstanceProvision.CreatedInstanceCount, "single install created count");
    AssertEqual(0, initialUpgradeState.InstanceProvision.MissingInstanceCount, "single install missing count");

    var upgradeRequest = CreateSettingsUpdateRequest(initialUpgradeState.Settings);
    upgradeRequest.InstanceCount = 3;
    upgradeRequest.MaxConcurrentRecordings = 3;
    var pendingUpgradeState = upgradeStore.UpdateSettings(upgradeRequest);
    AssertEqual(3, pendingUpgradeState.Settings.InstanceCount, "upgrade desired instance count");
    AssertEqual(3, pendingUpgradeState.Settings.MaxConcurrentRecordings, "upgrade concurrency follows instance count");
    AssertEqual(3, pendingUpgradeState.Instances.Count, "upgrade desired instance records");
    AssertEqual(true, pendingUpgradeState.Instances[0].LaunchDirectoryReady, "existing instance remains ready");
    AssertEqual(false, pendingUpgradeState.Instances[1].LaunchDirectoryReady, "missing second instance not ready");
    AssertEqual("Missing", pendingUpgradeState.InstanceProvision.Status, "upgrade provision status is missing");
    AssertEqual(1, pendingUpgradeState.InstanceProvision.CreatedInstanceCount, "upgrade created count before expansion");
    AssertEqual(2, pendingUpgradeState.InstanceProvision.MissingInstanceCount, "upgrade missing count before expansion");
    WriteFakeFile(Path.Combine(upgradeInstancesRoot, "Managed I-3"), "Beat Saber_Data/partial-copy.txt", "partial copy");

    var expandedUpgradeState = upgradeStore.ProvisionManagedInstances(new InstanceProvisionRequest
    {
        InstanceCount = 3,
        CreateMissingOnly = true
    });
    AssertEqual(3, expandedUpgradeState.Settings.InstanceCount, "expanded desired instance count");
    AssertEqual(3, expandedUpgradeState.Settings.MaxConcurrentRecordings, "expanded concurrency follows instance count");
    AssertEqual(2, expandedUpgradeState.InstanceProvision.Instances.Count, "expanded provision only copied missing instances");
    AssertEqual(3, expandedUpgradeState.InstanceProvision.CreatedInstanceCount, "expanded created count");
    AssertEqual(0, expandedUpgradeState.InstanceProvision.MissingInstanceCount, "expanded missing count");
    AssertEqual(2, upgradePluginInstaller.InstallCount, "upgrade worker plugin install count");
    for (var index = 0; index < 3; index++)
    {
        AssertEqual(true, File.Exists(Path.Combine(upgradeInstancesRoot, "Managed I-" + (index + 1), "Beat Saber.exe")), "expanded instance exe " + index);
        AssertEqual(true, File.Exists(Path.Combine(upgradeInstancesRoot, "Managed I-" + (index + 1), "Plugins", "BSAutoReplayRecorder.Plugin.dll")), "expanded plugin " + index);
    }

    AssertEqual(false, File.Exists(Path.Combine(upgradeInstancesRoot, "Managed I-2", "Beat Saber_Data", "CustomLevels", "source-song.txt")), "expanded missing instance skips CustomLevels");
    AssertEqual(false, File.Exists(Path.Combine(upgradeInstancesRoot, "Managed I-3", "Beat Saber_Data", "partial-copy.txt")), "expanded missing instance replaces partial folder");
    AssertEqual(true, IsReparsePoint(Path.Combine(upgradeInstancesRoot, "Managed I-2", "CustomSabers")), "expanded missing instance links CustomSabers");
    AssertEqual(false, Directory.EnumerateDirectories(Path.Combine(upgradeInstancesRoot, "Managed I-2"), "CustomSabers.local-*").Any(), "expanded missing instance does not clone CustomSabers backup");
    AssertEqual(false, Directory.EnumerateDirectories(Path.Combine(upgradeInstancesRoot, "Managed I-3"), "CustomSabers.local-*").Any(), "expanded replaced instance does not clone CustomSabers backup");

    var renamedPrefixInstancesRoot = Path.Combine(workspace, "RenamedPrefixInstances");
    var renamedPrefixPluginInstaller = new FakeWorkerPluginInstaller();
    var renamedPrefixStore = CreateStore(
        Path.Combine(workspace, "renamed-prefix-workspace"),
        renamedPrefixInstancesRoot,
        "I-",
        instanceCount: 1,
        maxConcurrentRecordings: 1,
        workerPluginInstaller: renamedPrefixPluginInstaller);
    var renamedPrefixInitialState = renamedPrefixStore.ProvisionManagedInstances(new InstanceProvisionRequest
    {
        SourceBeatSaberPath = sourceDirectory,
        InstanceCount = 1
    });
    AssertEqual(true, renamedPrefixInitialState.Instances[0].LaunchDirectoryReady, "renamed prefix baseline starts ready");

    var renamedPrefixRequest = CreateSettingsUpdateRequest(renamedPrefixInitialState.Settings);
    renamedPrefixRequest.InstanceCount = 3;
    renamedPrefixRequest.MaxConcurrentRecordings = 3;
    renamedPrefixRequest.BeatSaberInstanceNamePrefix = "BSARR I-";
    var renamedPrefixPendingState = renamedPrefixStore.UpdateSettings(renamedPrefixRequest);
    AssertEqual(true, renamedPrefixPendingState.Instances[0].LaunchDirectoryReady, "renamed prefix keeps old baseline ready");
    AssertEqual(Path.GetFullPath(Path.Combine(renamedPrefixInstancesRoot, "I-1")), renamedPrefixPendingState.Instances[0].LaunchDirectory, "renamed prefix baseline directory adopted");

    var renamedPrefixExpandedState = renamedPrefixStore.ProvisionManagedInstances(new InstanceProvisionRequest
    {
        InstanceCount = 3,
        CreateMissingOnly = true
    });
    AssertEqual(3, renamedPrefixExpandedState.InstanceProvision.CreatedInstanceCount, "renamed prefix expansion created count");
    AssertEqual(true, File.Exists(Path.Combine(renamedPrefixInstancesRoot, "I-1", "Beat Saber.exe")), "renamed prefix keeps old baseline folder");
    AssertEqual(true, File.Exists(Path.Combine(renamedPrefixInstancesRoot, "BSARR I-2", "Beat Saber.exe")), "renamed prefix creates second with new prefix");
    AssertEqual(true, File.Exists(Path.Combine(renamedPrefixInstancesRoot, "BSARR I-3", "Beat Saber.exe")), "renamed prefix creates third with new prefix");

    AssertThrows<InvalidOperationException>(
        () => upgradeStore.RemoveManagedInstance(0),
        "remove refuses non-last instance");
    var removedUpgradeState = upgradeStore.RemoveManagedInstance(2);
    AssertEqual(2, removedUpgradeState.Settings.InstanceCount, "remove managed instance setting count");
    AssertEqual(2, removedUpgradeState.Settings.MaxConcurrentRecordings, "remove managed instance concurrency count");
    AssertEqual(2, removedUpgradeState.Instances.Count, "remove managed instance state count");
    AssertEqual(2, removedUpgradeState.InstanceProvision.DesiredInstanceCount, "remove managed instance provision desired count");
    AssertEqual(2, removedUpgradeState.InstanceProvision.CreatedInstanceCount, "remove managed instance provision created count");
    AssertEqual(0, removedUpgradeState.InstanceProvision.MissingInstanceCount, "remove managed instance missing count");
    AssertEqual("2/2 managed instances are ready.", removedUpgradeState.InstanceProvision.Summary, "remove managed instance summary");
    AssertEqual(true, Directory.Exists(Path.Combine(upgradeInstancesRoot, "Managed I-2")), "remove managed instance keeps previous folder");
    AssertEqual(false, Directory.Exists(Path.Combine(upgradeInstancesRoot, "Managed I-3")), "remove managed instance deletes highest folder");

    var downsizeRequest = CreateSettingsUpdateRequest(expandedUpgradeState.Settings);
    downsizeRequest.InstanceCount = 2;
    downsizeRequest.MaxConcurrentRecordings = 2;
    var downsizedState = upgradeStore.UpdateSettings(downsizeRequest);
    AssertEqual(2, downsizedState.Settings.InstanceCount, "downsize desired instance count");
    AssertEqual(2, downsizedState.Settings.MaxConcurrentRecordings, "downsize concurrency count");
    AssertEqual(2, downsizedState.Instances.Count, "downsize active instance records");
    AssertEqual(2, downsizedState.InstanceProvision.DesiredInstanceCount, "downsize provision desired count");
    AssertEqual(2, downsizedState.InstanceProvision.CreatedInstanceCount, "downsize provision created count");
    AssertEqual(0, downsizedState.InstanceProvision.MissingInstanceCount, "downsize provision missing count");
    AssertEqual("2/2 managed instances are ready.", downsizedState.InstanceProvision.Summary, "downsize provision summary");

    var missingBaselineStore = CreateStore(
        Path.Combine(workspace, "missing-baseline-workspace"),
        Path.Combine(workspace, "ManagedInstancesMissingBaseline"),
        "Managed I-",
        instanceCount: 3,
        workerPluginInstaller: new FakeWorkerPluginInstaller());
    AssertThrows<InvalidOperationException>(
        () => missingBaselineStore.ProvisionManagedInstances(new InstanceProvisionRequest
        {
            InstanceCount = 3,
            CreateMissingOnly = true
        }),
        "create missing refuses missing baseline");

    var invalidCountStore = CreateStore(
        Path.Combine(workspace, "invalid-count-workspace"),
        Path.Combine(workspace, "ManagedInstancesInvalidCount"),
        "Managed I-",
        instanceCount: 3,
        workerPluginInstaller: new FakeWorkerPluginInstaller());
    AssertThrows<InvalidOperationException>(
        () => invalidCountStore.ProvisionManagedInstances(new InstanceProvisionRequest
        {
            SourceBeatSaberPath = sourceDirectory,
            InstanceCount = 5
        }),
        "provision rejects instance count above four");

    var songImportInstancesRoot = Path.Combine(workspace, "ManagedInstancesWithSongs");
    var songImportStore = CreateStore(
        Path.Combine(workspace, "song-import-workspace"),
        songImportInstancesRoot,
        "Managed I-",
        instanceCount: 2,
        workerPluginInstaller: new FakeWorkerPluginInstaller());
    var songImportState = songImportStore.ProvisionManagedInstances(new InstanceProvisionRequest
    {
        SourceBeatSaberPath = sourceDirectory,
        CopyExistingSongs = true
    });

    AssertEqual(true, songImportState.InstanceProvision.CopyExistingSongs, "provision song import flag");
    AssertEqual(true, File.Exists(Path.Combine(songImportInstancesRoot, "Managed I-1", "Beat Saber_Data", "CustomLevels", "source-song.txt")), "baseline imports CustomLevels");
    AssertEqual(true, File.Exists(Path.Combine(songImportInstancesRoot, "Managed I-1", "Beat Saber_Data", "CustomWIPLevels", "source-wip.txt")), "baseline imports CustomWIPLevels");
    AssertEqual(false, File.Exists(Path.Combine(songImportInstancesRoot, "Managed I-1", "Beat Saber_Data", "CustomLevels.local-20260604185400", "backup-song.txt")), "song import skips CustomLevels local backup");
    AssertEqual(false, File.Exists(Path.Combine(songImportInstancesRoot, "Managed I-1", "Beat Saber_Data", "CustomWIPLevels.local-20260604185400", "backup-wip.txt")), "song import skips CustomWIPLevels local backup");
    AssertEqual(false, File.Exists(Path.Combine(songImportInstancesRoot, "Managed I-1", "UserData", "BSWorldCupReplayRecorder", "Recordings", "old-legacy.mkv")), "song import skips legacy recorder output");
    AssertEqual(false, File.Exists(Path.Combine(songImportInstancesRoot, "Managed I-1", "UserData", "BSAutoReplayRecorder", "Recordings", "old-managed.mkv")), "song import skips managed recorder output");
    AssertEqual(true, File.Exists(Path.Combine(songImportInstancesRoot, "Managed I-2", "Beat Saber_Data", "CustomLevels", "source-song.txt")), "non-baseline sees shared CustomLevels");
    AssertEqual(true, File.Exists(Path.Combine(songImportInstancesRoot, "Managed I-2", "Beat Saber_Data", "CustomWIPLevels", "source-wip.txt")), "non-baseline sees shared CustomWIPLevels");
    AssertEqual("Linked", songImportState.SongFolders.Status, "song import repairs song folder links");
}

static void RunStopBroadcastCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 2);
    using var files = CreateReplayFiles(2);
    store.ImportFiles(files.Collection);
    var first = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });
    var second = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-1",
        WorkerName = "Worker 1",
        PreferredInstanceIndex = 1
    });

    store.StartRun();
    var assignment = store.GetAssignment(first.WorkerId);
    AssertEqual(true, assignment.HasAssignment, "stop active assignment exists");

    var stopped = store.StopRun();
    AssertEqual("Stopping", stopped.Run.Status, "stop run status");
    AssertEqual(1, stopped.Run.ForceStopCommandId, "stop command id");

    var activeHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = first.WorkerId,
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId
    });
    AssertEqual(true, activeHeartbeat.ShouldCancelAssignment, "stop cancels active assignment");
    AssertEqual(true, activeHeartbeat.ShouldOpenPauseMenu, "stop exits active worker replay");

    var idleHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = second.WorkerId,
        Status = "Online"
    });
    AssertEqual(false, idleHeartbeat.ShouldCancelAssignment, "stop does not cancel idle worker");
    AssertEqual(true, idleHeartbeat.ShouldOpenPauseMenu, "stop exits idle worker replay");

    var repeatedHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = second.WorkerId,
        Status = "Online"
    });
    AssertEqual(false, repeatedHeartbeat.ShouldOpenPauseMenu, "stop command is delivered once");
}

static void RunCloseGamesAfterQueueCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    var worker = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "close-worker",
        WorkerName = "Close Worker",
        PreferredInstanceIndex = 0
    });
    SetGameProcessIds(store, 4100);

    store.StartRun();
    var armed = store.SetCloseGamesWhenFinished(true);
    AssertEqual(true, armed.Run.CloseGamesWhenFinishedRequested, "close games after queue armed");

    var assignment = store.GetAssignment(worker.WorkerId);
    var outputPath = Path.Combine(workspace, "recording.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, "recorded");

    var completed = store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = worker.WorkerId,
        AssignmentId = assignment.AssignmentId!,
        Status = "Completed",
        OutputPath = outputPath
    });

    AssertEqual("Complete", completed.Run.Status, "close games after queue run complete");
    AssertEqual(false, completed.Run.CloseGamesWhenFinishedRequested, "close games after queue resets request");
    AssertEqual((string?)null, completed.Instances[0].WorkerId, "close games after queue clears worker id");
    AssertEqual((int?)null, completed.Instances[0].GameProcessId, "close games after queue clears process id");
    AssertEqual("Exited", completed.Instances[0].GameLaunchStatus, "close games after queue marks game exited");
    AssertEqual(
        true,
        completed.Events.Any(item => item.Tag == "Instance" &&
                                     item.Text.Contains("Queue finished; close requested for all games", StringComparison.Ordinal)),
        "close games after queue event");
}

static void RunStaleCanceledRunFinalizesOnLoadCheck(string workspace)
{
    Directory.CreateDirectory(workspace);
    var statePath = Path.Combine(workspace, "control-panel-state.json");
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    var state = new ControlPanelState
    {
        Settings = new ControlPanelSettings
        {
            WorkspaceDirectory = workspace,
            RecordingOutputDirectory = Path.Combine(workspace, "Recordings"),
            InstanceCount = 1,
            MaxConcurrentRecordings = 1,
            AudioMode = "None",
            RequireAudioForRun = false,
            BeatSaberInstancesRoot = Path.Combine(workspace, "Instances"),
            BeatSaberLaunchPreset = "custom",
            BeatSaberLaunchArguments = "--no-yeet fpfc"
        },
        Instances =
        {
            new WorkerInstanceRecord
            {
                Index = 0,
                Name = "Instance 1",
                Status = "Assigned",
                WorkerId = null,
                ActiveAssignmentId = null
            }
        },
        Run = new RunState
        {
            IsRunning = false,
            CancellationRequested = true,
            CancellationReason = "Stopped by operator.",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            Status = "Stopping",
            ForceStopCommandId = 4
        }
    };
    File.WriteAllText(statePath, JsonSerializer.Serialize(state, jsonOptions));

    var store = CreateStore(workspace, instanceCount: 1);
    var snapshot = store.Snapshot();
    AssertEqual(false, snapshot.Run.CancellationRequested, "stale canceled run clears cancellation flag");
    AssertEqual("Stopped", snapshot.Run.Status, "stale canceled run status");
    AssertEqual("Idle", snapshot.Instances[0].Status, "stale canceled run resets idle instance");
}

static void RunIdleShutdownCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    var idleTimeout = TimeSpan.FromMinutes(20);
    var startedAt = store.Snapshot().LastActivityUtc;

    AssertEqual(
        false,
        store.TryRequestIdleShutdown(startedAt.Add(idleTimeout).AddSeconds(-1), idleTimeout),
        "idle shutdown waits for full timeout");
    AssertEqual(
        true,
        store.TryRequestIdleShutdown(startedAt.Add(idleTimeout), idleTimeout),
        "idle shutdown trips after timeout");

    using var files = CreateReplayFiles(1);
    var activeStore = CreateStore(Path.Combine(workspace, "active-run"), instanceCount: 1);
    activeStore.ImportFiles(files.Collection);
    var worker = activeStore.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "active-worker",
        WorkerName = "Active Worker",
        PreferredInstanceIndex = 0
    });
    activeStore.StartRun();
    var assignment = activeStore.GetAssignment(worker.WorkerId);
    AssertEqual(true, assignment.HasAssignment, "idle shutdown active assignment exists");
    var activeStartedAt = activeStore.Snapshot().LastActivityUtc;

    AssertEqual(
        false,
        activeStore.TryRequestIdleShutdown(activeStartedAt.AddHours(2), idleTimeout),
        "idle shutdown skips active run");
}

static void RunInstanceBaselineCheck(string workspace)
{
    var instancesRoot = Path.Combine(workspace, "BeatSaberInstances");
    CreateFakeBeatSaberInstance(instancesRoot, "Test I-1", 0);
    CreateFakeBeatSaberInstance(instancesRoot, "Test I-2", 1);
    var store = CreateStore(
        workspace,
        instancesRoot,
        "Test I-",
        instanceCount: 2,
        requireMatchingInstanceBaseline: true);

    var state = store.CheckInstanceBaseline();
    AssertEqual("Matched", state.InstanceBaseline.Status, "matching baseline status");
    AssertEqual("Ok", state.InstanceBaseline.Instances[0].Status, "baseline first instance status");
    AssertEqual("Ok", state.InstanceBaseline.Instances[1].Status, "baseline second instance status");
    var baselineJson = JsonSerializer.Serialize(state.InstanceBaseline);
    AssertEqual(false, baselineJson.Contains("beatleader", StringComparison.OrdinalIgnoreCase), "baseline does not store file contents");
    AssertEqual(false, baselineJson.Contains("recorder plugin", StringComparison.OrdinalIgnoreCase), "baseline does not store plugin contents");
    AssertEqual(false, Regex.IsMatch(baselineJson, "[a-fA-F0-9]{64}"), "baseline does not store file hashes");

    File.WriteAllText(Path.Combine(instancesRoot, "Test I-2", "Plugins", "BeatLeader.dll"), "different beatleader");
    state = store.CheckInstanceBaseline();
    AssertEqual("Mismatch", state.InstanceBaseline.Status, "mismatched baseline status");
    AssertEqual("Mismatch", state.InstanceBaseline.Instances[1].Status, "mismatched instance status");
    AssertContains("Changed Plugins/BeatLeader.dll", string.Join(" ", state.InstanceBaseline.Instances[1].Issues), "baseline issue");

    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0,
        GameDirectory = Path.Combine(instancesRoot, "Test I-1")
    });
    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-1",
        WorkerName = "Worker 1",
        PreferredInstanceIndex = 1,
        GameDirectory = Path.Combine(instancesRoot, "Test I-2")
    });

    AssertThrows<InvalidOperationException>(
        () => store.StartRun(),
        "mismatched baseline start guard");
}

static void RunDefaultLaunchArgumentsCheck(string workspace)
{
    var settings = new ControlPanelSettings
    {
        WorkspaceDirectory = workspace
    };
    var store = new ControlPanelStore(settings);
    var snapshot = store.Snapshot();

    const string expectedArgs = ControlPanelSettings.DefaultBeatSaberLaunchArguments;
    AssertEqual(ControlPanelSettings.DefaultBeatSaberLaunchPreset, snapshot.Settings.BeatSaberLaunchPreset, "default launch preset");
    AssertEqual(expectedArgs, snapshot.Settings.BeatSaberLaunchArguments, "default launch arguments");
    AssertEqual(expectedArgs, snapshot.Instances[0].LaunchArguments, "default instance launch arguments");
    AssertEqual(1, snapshot.Settings.InstanceCount, "default instance count");
    AssertEqual(1, snapshot.Settings.MaxConcurrentRecordings, "default max concurrent recordings");
    AssertEqual(false, snapshot.Settings.ManageDisplayScale, "default display scale management");
    AssertEqual(false, snapshot.Settings.RequireMatchingInstanceBaseline, "default baseline guard");
    AssertEqual(true, snapshot.Settings.RequireAudioForRun, "default audio guard");
    AssertEqual("Loudness", snapshot.Settings.AudioLevelMode, "default audio level mode");
    AssertEqual(-12d, snapshot.Settings.AudioTargetLevelDb, "default audio target level");
    AssertEqual(100, snapshot.Settings.RecordingDisplayScalePercent, "default recording display scale");
    AssertEqual(150, snapshot.Settings.RestoreDisplayScalePercent, "default restore display scale");
    AssertEqual(
        Path.Combine(Path.GetFullPath(workspace), "Recordings"),
        snapshot.Settings.RecordingOutputDirectory,
        "default recording folder");
    AssertEqual(
        Path.Combine(Path.GetFullPath(workspace), "Instances"),
        snapshot.Settings.BeatSaberInstancesRoot,
        "default managed instance root");
    AssertEqual(
        Path.Combine(Path.GetFullPath(workspace), "Recordings", "Instance 1"),
        snapshot.Instances[0].OutputDirectory,
        "default instance output folder");
}

static void RunWorkerPluginSettingsIdentityCheck()
{
    var settings = new ControlPanelSettings
    {
        BindUrl = "http://127.0.0.1:5770",
        InstanceCount = 3,
        MonitorIndex = 1,
        LagSpikeStartupGraceSeconds = 5,
        IdleShutdownMinutes = 7
    };
    settings.Normalize();

    var workerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < 3; index++)
    {
        var instance = new WorkerInstanceRecord
        {
            Index = index,
            RecorderHostUrl = "http://127.0.0.1:" + (5757 + index),
            LaunchDirectory = Path.Combine("ManagedInstances", "BSARR I-" + (index + 1))
        };
        var workerId = DotNetWorkerPluginInstaller.CreateManagedWorkerId(instance);
        var pluginSettings = DotNetWorkerPluginInstaller.CreatePluginSettings(instance, settings, workerId, 2, 2);
        var json = JsonSerializer.Serialize(pluginSettings);
        var parsed = JsonSerializer.Deserialize<BatchRecorderSettings>(json)
                     ?? throw new InvalidOperationException("worker plugin settings identity deserialize failed");

        foreach (var legacyFieldName in WorkerPluginLegacyFieldNames())
        {
            AssertDoesNotContain(legacyFieldName, json, "worker settings omit legacy field " + legacyFieldName + " " + index);
        }

        AssertEqual(true, workerIds.Add(parsed.ControlPanelWorker.WorkerId), "worker id is unique " + index);
        AssertEqual("managed-worker-" + index.ToString("00"), parsed.ControlPanelWorker.WorkerId, "worker id " + index);
        AssertEqual("Instance " + (index + 1), parsed.ControlPanelWorker.WorkerName, "worker name " + index);
        AssertEqual(index, parsed.ControlPanelWorker.PreferredInstanceIndex, "preferred instance index " + index);
        AssertEqual("http://127.0.0.1:" + (5757 + index), parsed.RecorderHost.BaseUrl, "recorder host port " + index);
        AssertEqual(300d, parsed.RecorderHost.TimeoutSeconds, "recorder host timeout " + index);
        AssertEqual(5.0, parsed.LagSpikeStartupGraceSeconds, "lag spike startup grace " + index);
        AssertEqual(5.0, parsed.DelayBetweenRecordingsSeconds, "delay between recordings " + index);
        AssertEqual(7.0, parsed.ControlPanelWorker.IdleShutdownMinutes, "worker idle shutdown timeout " + index);
        AssertEqual(index, parsed.WindowPlacement.InstanceIndex, "window placement index " + index);
        AssertEqual(1920, parsed.WindowPlacement.Width, "window placement width " + index);
        AssertEqual(1080, parsed.WindowPlacement.Height, "window placement height " + index);
    }
}

static void RunWorkerPluginInstallerBeatLeaderGuardCheck(string workspace)
{
    var instanceDirectory = Path.Combine(workspace, "Instances", "I-1");
    WriteFakeFile(instanceDirectory, "Beat Saber.exe", "game exe");

    var settings = new ControlPanelSettings
    {
        WorkspaceDirectory = workspace,
        BeatSaberInstancesRoot = Path.Combine(workspace, "Instances"),
        InstanceCount = 1
    };
    settings.Normalize();

    var instances = new List<WorkerInstanceRecord>
    {
        new WorkerInstanceRecord
        {
            Index = 0,
            Name = "Instance 1",
            LaunchDirectory = instanceDirectory,
            Enabled = true
        }
    };

    try
    {
        new DotNetWorkerPluginInstaller().Install(instances, settings);
    }
    catch (InvalidOperationException ex)
    {
        AssertContains("Plugins/BeatLeader.dll", ex.Message, "worker plugin installer beatleader error path");
        AssertContains("reprovision workers", ex.Message, "worker plugin installer beatleader remediation");
        return;
    }

    throw new InvalidOperationException("worker plugin installer beatleader guard failed. Expected InvalidOperationException.");
}

static void RunDuplicateManagedWorkerIdRegistrationCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 3);
    var workerIds = new List<string>();

    for (var index = 0; index < 3; index++)
    {
        var response = store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "managed-worker-02",
            WorkerName = "Instance " + (index + 1),
            PreferredInstanceIndex = index
        });

        workerIds.Add(response.WorkerId);
        AssertEqual(index, response.InstanceIndex, "duplicate managed worker id registers preferred slot " + index);
        AssertEqual("managed-worker-" + index.ToString("00"), response.WorkerId, "duplicate managed worker id normalizes " + index);
    }

    var snapshot = store.Snapshot();
    AssertEqual(3, workerIds.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "duplicate managed worker ids are repaired");
    for (var index = 0; index < 3; index++)
    {
        AssertEqual("managed-worker-" + index.ToString("00"), snapshot.Instances[index].WorkerId, "normalized snapshot worker id " + index);
    }
}

static void RunInstanceDisplayNameNormalizationCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 2);

    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "legacy-bsarr-1",
        WorkerName = "BSARR I-1",
        PreferredInstanceIndex = 0
    });
    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "legacy-short-2",
        WorkerName = "I-2",
        PreferredInstanceIndex = 1
    });

    var normalized = store.Snapshot();
    AssertEqual("Instance 1", normalized.Instances[0].Name, "legacy BSARR display name normalizes");
    AssertEqual("Instance 2", normalized.Instances[1].Name, "legacy short display name normalizes");

    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "legacy-bsarr-1",
        WorkerName = "Custom Worker",
        PreferredInstanceIndex = 0
    });

    var custom = store.Snapshot();
    AssertEqual("Custom Worker", custom.Instances[0].Name, "custom worker name is preserved");
}

static void RunSongFolderLinksCheck(string workspace)
{
    var instancesRoot = Path.Combine(workspace, "BeatSaberInstances");
    CreateFakeBeatSaberInstance(instancesRoot, "Test I-1", 0);
    CreateFakeBeatSaberInstance(instancesRoot, "Test I-2", 1);
    WriteFakeFile(Path.Combine(instancesRoot, "Test I-1"), "Beat Saber_Data/CustomLevels/local.txt", "local custom");
    WriteFakeFile(Path.Combine(instancesRoot, "Test I-2"), "Beat Saber_Data/CustomWIPLevels/wip.txt", "local wip");

    var store = CreateStore(
        workspace,
        instancesRoot,
        "Test I-",
        instanceCount: 2);

    var checkedState = store.CheckSongFolderLinks();
    AssertEqual("Missing", checkedState.SongFolders.Status, "initial song folder status");
    AssertEqual(16, checkedState.SongFolders.Links.Count, "shared folder link count");

    var repaired = store.RepairSongFolderLinks();
    AssertEqual("Linked", repaired.SongFolders.Status, "repaired song folder status");
    AssertEqual(16, repaired.SongFolders.Links.Count(item => item.Status == "Linked"), "linked folder count");
    AssertEqual(true, Directory.Exists(repaired.Settings.SharedCustomLevelsDirectory), "shared CustomLevels folder exists");
    AssertEqual(true, Directory.Exists(repaired.Settings.SharedCustomWipLevelsDirectory), "shared CustomWIPLevels folder exists");
    AssertEqual(true, Directory.Exists(repaired.Settings.SharedCustomSabersDirectory), "shared CustomSabers folder exists");
    AssertEqual(true, Directory.Exists(repaired.Settings.SharedCustomNotesDirectory), "shared CustomNotes folder exists");
    AssertEqual(true, File.Exists(Path.Combine(repaired.Settings.SharedCustomLevelsDirectory, "local.txt")), "shared CustomLevels was seeded");
    AssertEqual(true, File.Exists(Path.Combine(repaired.Settings.SharedCustomWipLevelsDirectory, "wip.txt")), "shared CustomWIPLevels was seeded");

    var firstCustomLevels = Path.Combine(instancesRoot, "Test I-1", "Beat Saber_Data", "CustomLevels");
    var secondWip = Path.Combine(instancesRoot, "Test I-2", "Beat Saber_Data", "CustomWIPLevels");
    var firstSabers = Path.Combine(instancesRoot, "Test I-1", "CustomSabers");
    AssertEqual(true, IsReparsePoint(firstCustomLevels), "first CustomLevels is a junction");
    AssertEqual(true, IsReparsePoint(secondWip), "second CustomWIPLevels is a junction");
    AssertEqual(true, IsReparsePoint(firstSabers), "first CustomSabers is a junction");
    AssertEqual(true, Directory.EnumerateDirectories(Path.Combine(instancesRoot, "Test I-1", "Beat Saber_Data"), "CustomLevels.local-*").Any(), "local CustomLevels backup exists");
    AssertEqual(true, Directory.EnumerateDirectories(Path.Combine(instancesRoot, "Test I-2", "Beat Saber_Data"), "CustomWIPLevels.local-*").Any(), "local CustomWIPLevels backup exists");

    var settingsRepairInstancesRoot = Path.Combine(workspace, "SettingsRepairInstances");
    CreateFakeBeatSaberInstance(settingsRepairInstancesRoot, "Auto I-1", 0);
    CreateFakeBeatSaberInstance(settingsRepairInstancesRoot, "Auto I-2", 1);
    var settingsRepairStore = CreateStore(
        Path.Combine(workspace, "settings-repair-workspace"),
        settingsRepairInstancesRoot,
        "Auto I-",
        instanceCount: 2);
    var settingsRepairRequest = CreateSettingsUpdateRequest(settingsRepairStore.Snapshot().Settings);
    var settingsRepairState = settingsRepairStore.UpdateSettings(settingsRepairRequest);
    AssertEqual("Linked", settingsRepairState.SongFolders.Status, "settings save repairs song folder links");
}

static void RunModIntegrationCatalogCheck(string workspace)
{
    var settings = new ControlPanelSettings
    {
        WorkspaceDirectory = workspace,
        ShareCustomSabers = true,
        ShareCustomNotes = false,
        ShareCustomPlatforms = true,
        ShareCustomAvatars = false,
        ShareCustomWalls = true,
        ShareCustomBombs = false
    };
    settings.Normalize();

    var definitions = ModIntegrationCatalog.CreateSharedFolderDefinitions(settings);
    AssertEqual(5, definitions.Count, "enabled shared mod folder count");
    AssertEqual(true, definitions.Any(item => item.DisplayName == "CustomLevels"), "catalog includes CustomLevels");
    AssertEqual(true, definitions.Any(item => item.DisplayName == "CustomWIPLevels"), "catalog includes CustomWIPLevels");
    AssertEqual(true, definitions.Any(item => item.DisplayName == "CustomSabers"), "catalog includes CustomSabers");
    AssertEqual(false, definitions.Any(item => item.DisplayName == "CustomNotes"), "catalog skips disabled CustomNotes");
    AssertEqual(
        Path.Combine(Path.GetFullPath(workspace), "SharedContent", "CustomSabers"),
        definitions.First(item => item.DisplayName == "CustomSabers").SharedFolderPath,
        "catalog uses normalized shared CustomSabers path");
    AssertEqual(true, ModIntegrationCatalog.SettingsAdapters.Any(item => item.DisplayName == "Chroma"), "catalog reserves Chroma settings adapter");
    AssertEqual(true, ModIntegrationCatalog.SettingsAdapters.Any(item => item.DisplayName == "Custom Sabers Picker"), "catalog reserves saber picker settings adapter");
}

static void RunRecordingOutputDirectoryCheck(string workspace)
{
    var customRoot = Path.Combine(workspace, "Custom Videos");
    var store = CreateStore(workspace, instanceCount: 2);
    var request = CreateSettingsUpdateRequest(store.Snapshot().Settings);
    request.RecordingOutputDirectory = customRoot;

    var updated = store.UpdateSettings(request);
    AssertEqual(Path.GetFullPath(customRoot), updated.Settings.RecordingOutputDirectory, "custom recording folder");
    AssertEqual(
        Path.Combine(Path.GetFullPath(customRoot), "Instance 1"),
        updated.Instances[0].OutputDirectory,
        "custom first instance output folder");
    AssertEqual(
        Path.Combine(Path.GetFullPath(customRoot), "Instance 2"),
        updated.Instances[1].OutputDirectory,
        "custom second instance output folder");

    request = CreateSettingsUpdateRequest(updated.Settings);
    request.RecordingOutputDirectory = "Relative Videos";
    updated = store.UpdateSettings(request);
    AssertEqual(
        Path.Combine(Path.GetFullPath(workspace), "Relative Videos"),
        updated.Settings.RecordingOutputDirectory,
        "relative recording folder");
}

static void RunPerRunRecordingOutputDirectoryCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 2, maxConcurrentRecordings: 2);
    using var files = CreateReplayFiles(2, distinctLevelHashes: true);
    store.ImportFiles(files.Collection);
    var collection = store.SaveMapCollection(new SaveMapCollectionRequest
    {
        Name = "Friday Night / Pack?"
    });
    RegisterWorkers(store, "run-folder-worker", count: 2);

    var started = store.StartRun();
    AssertEqual("Friday Night / Pack?", started.Run.CollectionName, "run collection name");
    AssertEqual(true, Directory.Exists(started.Run.RecordingOutputDirectory), "run recording folder exists");
    AssertContains("Friday Night Pack", Path.GetFileName(started.Run.RecordingOutputDirectory), "sanitized collection folder name");
    AssertEqual(
        Path.GetFullPath(started.Settings.RecordingOutputDirectory),
        Path.GetFullPath(Path.GetDirectoryName(started.Run.RecordingOutputDirectory) ?? ""),
        "run folder parent");
    AssertEqual(
        true,
        Regex.IsMatch(
            Path.GetFileName(started.Run.RecordingOutputDirectory),
            @"^\d{2}-\d{2}-\d{4} \d{2}-\d{2}-\d{2} - "),
        "run folder timestamp");

    var firstAssignment = store.GetAssignment("run-folder-worker-0");
    var secondAssignment = store.GetAssignment("run-folder-worker-1");
    AssertEqual(true, firstAssignment.HasAssignment, "first run-folder assignment exists");
    AssertEqual(true, secondAssignment.HasAssignment, "second run-folder assignment exists");
    AssertEqual(
        started.Run.RecordingOutputDirectory,
        firstAssignment.OutputDirectory ?? "",
        "first assignment run folder");
    AssertEqual(
        started.Run.RecordingOutputDirectory,
        secondAssignment.OutputDirectory ?? "",
        "second assignment run folder");
    AssertEqual(
        firstAssignment.OutputDirectory ?? "",
        secondAssignment.OutputDirectory ?? "",
        "shared run folder");

    var noCollectionStore = CreateStore(Path.Combine(workspace, "manual"), instanceCount: 1, maxConcurrentRecordings: 1);
    using var manualFiles = CreateReplayFiles(1, distinctLevelHashes: true);
    noCollectionStore.ImportFiles(manualFiles.Collection);
    RegisterWorkers(noCollectionStore, "manual-run-folder-worker", count: 1);
    var manualRun = noCollectionStore.StartRun();
    AssertEqual("", manualRun.Run.CollectionName, "manual run collection name");
    AssertEqual(
        true,
        Regex.IsMatch(
            Path.GetFileName(manualRun.Run.RecordingOutputDirectory),
            @"^\d{2}-\d{2}-\d{4} \d{2}-\d{2}-\d{2}$"),
        "manual run folder timestamp only");
}

static void RunLaunchPresetNormalizationCheck()
{
    var customSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "4k-monitor-2x2",
        BeatSaberLaunchArguments = "--no-yeet fpfc"
    };
    customSettings.Normalize();
    AssertEqual("custom", customSettings.BeatSaberLaunchPreset, "edited launch args force custom preset");

    var presetSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "4k-monitor-2x2",
        InstanceCount = 3,
        MaxConcurrentRecordings = 3,
        TargetFps = 60,
        CaptureWidth = 1920,
        CaptureHeight = 1080,
        VideoBitrateKbps = 12000,
        OutputFormat = "mkv",
        MonitorIndex = 0,
        Encoder = "h264_nvenc",
        QualityMode = "Performance",
        ManageDisplayScale = true,
        RecordingDisplayScalePercent = 100,
        RestoreDisplayScalePercent = 150,
        HideTaskbarDuringRun = true,
        BeatSaberLaunchArguments = ControlPanelSettings.DefaultBeatSaberLaunchArguments
    };
    presetSettings.Normalize();
    AssertEqual("4k-monitor-2x2", presetSettings.BeatSaberLaunchPreset, "known launch preset");

    var migratedPresetSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "windowed-1080p",
        InstanceCount = 4,
        MaxConcurrentRecordings = 4,
        TargetFps = 60,
        CaptureWidth = 1920,
        CaptureHeight = 1080,
        VideoBitrateKbps = 12000,
        OutputFormat = "mkv",
        MonitorIndex = 1,
        Encoder = "h264_nvenc",
        QualityMode = "Performance",
        ManageDisplayScale = true,
        RecordingDisplayScalePercent = 100,
        RestoreDisplayScalePercent = 150,
        HideTaskbarDuringRun = true,
        BeatSaberLaunchArguments = ControlPanelSettings.DefaultBeatSaberLaunchArguments
    };
    migratedPresetSettings.Normalize();
    AssertEqual("4k-monitor-2x2", migratedPresetSettings.BeatSaberLaunchPreset, "saved 4k values restore 4k preset");

    var monitor5kPresetSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "single-5k",
        InstanceCount = 3,
        MaxConcurrentRecordings = 3,
        TargetFps = 60,
        CaptureWidth = 2560,
        CaptureHeight = 1440,
        VideoBitrateKbps = 18000,
        OutputFormat = "mkv",
        MonitorIndex = 1,
        Encoder = "h264_nvenc",
        QualityMode = "Performance",
        ManageDisplayScale = true,
        RecordingDisplayScalePercent = 100,
        RestoreDisplayScalePercent = 150,
        HideTaskbarDuringRun = true,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed5kBeatSaberLaunchArguments
    };
    monitor5kPresetSettings.Normalize();
    AssertEqual("5k-monitor-2x2", monitor5kPresetSettings.BeatSaberLaunchPreset, "saved 5k values restore 5k preset");

    var monitor1440pPresetSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "windowed-720p",
        InstanceCount = 2,
        MaxConcurrentRecordings = 2,
        TargetFps = 60,
        CaptureWidth = 1280,
        CaptureHeight = 720,
        VideoBitrateKbps = 8000,
        OutputFormat = "mkv",
        MonitorIndex = 0,
        Encoder = "h264_nvenc",
        QualityMode = "Performance",
        ManageDisplayScale = true,
        RecordingDisplayScalePercent = 100,
        RestoreDisplayScalePercent = 150,
        HideTaskbarDuringRun = true,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed720pBeatSaberLaunchArguments
    };
    monitor1440pPresetSettings.Normalize();
    AssertEqual("1440p-monitor-2x2", monitor1440pPresetSettings.BeatSaberLaunchPreset, "saved 1440p values restore 1440p preset");

    var monitor720pPresetSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "720p-monitor-2x2",
        InstanceCount = 2,
        MaxConcurrentRecordings = 2,
        TargetFps = 60,
        CaptureWidth = 1280,
        CaptureHeight = 720,
        VideoBitrateKbps = 8000,
        OutputFormat = "mkv",
        MonitorIndex = 0,
        Encoder = "h264_nvenc",
        QualityMode = "Performance",
        ManageDisplayScale = true,
        RecordingDisplayScalePercent = 100,
        RestoreDisplayScalePercent = 150,
        HideTaskbarDuringRun = true,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed720pBeatSaberLaunchArguments
    };
    monitor720pPresetSettings.Normalize();
    AssertEqual("720p-monitor-2x2", monitor720pPresetSettings.BeatSaberLaunchPreset, "saved 720p values restore 720p grid preset");

    var smallWindowSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "windowed-720p",
        CaptureWidth = 1280,
        CaptureHeight = 720,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed720pBeatSaberLaunchArguments
    };
    smallWindowSettings.Normalize();
    AssertEqual("windowed-720p", smallWindowSettings.BeatSaberLaunchPreset, "720p launch preset");

    var single720pSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "single-720p",
        InstanceCount = 1,
        MaxConcurrentRecordings = 1,
        CaptureWidth = 1280,
        CaptureHeight = 720,
        ManageDisplayScale = false,
        HideTaskbarDuringRun = false,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed720pBeatSaberLaunchArguments
    };
    single720pSettings.Normalize();
    AssertEqual("single-720p", single720pSettings.BeatSaberLaunchPreset, "single 720p launch preset");

    var single5kSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "single-5k",
        InstanceCount = 1,
        MaxConcurrentRecordings = 1,
        CaptureWidth = 5120,
        CaptureHeight = 2880,
        ManageDisplayScale = false,
        HideTaskbarDuringRun = false,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed5kBeatSaberLaunchArguments
    };
    single5kSettings.Normalize();
    AssertEqual("single-5k", single5kSettings.BeatSaberLaunchPreset, "single 5k launch preset");

    var singleInstanceSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "single-1080p",
        InstanceCount = 1,
        MaxConcurrentRecordings = 1,
        CaptureWidth = 1920,
        CaptureHeight = 1080,
        ManageDisplayScale = false,
        HideTaskbarDuringRun = false,
        BeatSaberLaunchArguments = ControlPanelSettings.DefaultBeatSaberLaunchArguments
    };
    singleInstanceSettings.Normalize();
    AssertEqual("single-1080p", singleInstanceSettings.BeatSaberLaunchPreset, "single instance launch preset");

    var single1440pSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "single-1440p",
        InstanceCount = 1,
        MaxConcurrentRecordings = 1,
        CaptureWidth = 2560,
        CaptureHeight = 1440,
        ManageDisplayScale = false,
        HideTaskbarDuringRun = false,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed1440pBeatSaberLaunchArguments
    };
    single1440pSettings.Normalize();
    AssertEqual("single-1440p", single1440pSettings.BeatSaberLaunchPreset, "single 1440p launch preset");

    var single4kSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "single-4k",
        InstanceCount = 1,
        MaxConcurrentRecordings = 1,
        CaptureWidth = 3840,
        CaptureHeight = 2160,
        ManageDisplayScale = false,
        HideTaskbarDuringRun = false,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed4kBeatSaberLaunchArguments
    };
    single4kSettings.Normalize();
    AssertEqual("single-4k", single4kSettings.BeatSaberLaunchPreset, "single 4k launch preset");

    var single4kWithInventorySettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "single-4k",
        InstanceCount = 4,
        MaxConcurrentRecordings = 4,
        CaptureWidth = 3840,
        CaptureHeight = 2160,
        ManageDisplayScale = false,
        HideTaskbarDuringRun = false,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed4kBeatSaberLaunchArguments
    };
    single4kWithInventorySettings.Normalize();
    AssertEqual(4, single4kWithInventorySettings.InstanceCount, "single 4k preserves managed inventory count");
    AssertEqual("single-4k", single4kWithInventorySettings.BeatSaberLaunchPreset, "single 4k launch preset with managed inventory");

    var ultrawideSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "ultrawide-1440p-2up",
        InstanceCount = 2,
        MaxConcurrentRecordings = 2,
        TargetFps = 60,
        CaptureWidth = 2560,
        CaptureHeight = 1440,
        VideoBitrateKbps = 18000,
        OutputFormat = "mkv",
        MonitorIndex = 0,
        Encoder = "h264_nvenc",
        QualityMode = "Performance",
        ManageDisplayScale = false,
        RecordingDisplayScalePercent = 100,
        RestoreDisplayScalePercent = 150,
        HideTaskbarDuringRun = true,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed1440pBeatSaberLaunchArguments
    };
    ultrawideSettings.Normalize();
    AssertEqual("ultrawide-1440p-2up", ultrawideSettings.BeatSaberLaunchPreset, "ultrawide 1440p launch preset");

    var instanceCountClampSettings = new ControlPanelSettings
    {
        InstanceCount = 12,
        MaxConcurrentRecordings = 12
    };
    instanceCountClampSettings.Normalize();
    AssertEqual(4, instanceCountClampSettings.InstanceCount, "instance count clamps to managed maximum");
    AssertEqual(4, instanceCountClampSettings.MaxConcurrentRecordings, "max concurrent follows managed instance count");
}

static void RunFixedWindowPlacementCheck()
{
    var topLeft = ControlPanelStore.CalculateFixedWindowPlacement(0, 0, 3840, 2160, 0, 1920, 1080);
    AssertEqual((0, 0, 1920, 1080), topLeft, "single 1080p window stays in 4K top-left quadrant");

    var bottomRight = ControlPanelStore.CalculateFixedWindowPlacement(0, 0, 3840, 2160, 3, 1920, 1080);
    AssertEqual((1920, 1080, 1920, 1080), bottomRight, "fourth 1080p window uses 4K bottom-right quadrant");

    var secondaryMonitor = ControlPanelStore.CalculateFixedWindowPlacement(1920, -200, 5760, 1960, 0, 1920, 1080);
    AssertEqual((1920, -200, 1920, 1080), secondaryMonitor, "window placement includes selected monitor origin");

    var nativeSize = ControlPanelStore.CalculateFixedWindowPlacement(0, 0, 1920, 1080, 0, 1920, 1080);
    AssertEqual((0, 0, 1920, 1080), nativeSize, "1080p window fills only a 1080p monitor");
}

static void RunBeatSaberWindowedSettingsFileCheck(string workspace)
{
    Directory.CreateDirectory(workspace);
    var settingsPath = Path.Combine(workspace, "settings.ini");
    File.WriteAllText(settingsPath, string.Join(Environment.NewLine, new[]
    {
        "# BeatSaber.Settings",
        "window.fullscreen=true",
        "window.resolution.x=3840",
        "window.resolution.y=2160",
        "quality.vsync_count=0"
    }));

    var changed = ControlPanelStore.ApplyBeatSaberWindowedSettingsFile(
        settingsPath,
        "-screen-fullscreen 0 -screen-width 1920 -screen-height 1080");
    var content = File.ReadAllText(settingsPath);
    AssertEqual(true, changed, "Beat Saber settings changed to windowed launch state");
    AssertContains("window.fullscreen=false", content, "Beat Saber settings force windowed mode");
    AssertContains("window.resolution.x=1920", content, "Beat Saber settings width");
    AssertContains("window.resolution.y=1080", content, "Beat Saber settings height");
    AssertContains("quality.vsync_count=0", content, "Beat Saber settings preserve unrelated values");
    AssertEqual(
        false,
        ControlPanelStore.ApplyBeatSaberWindowedSettingsFile(
            settingsPath,
            "-screen-fullscreen 0 -screen-width 1920 -screen-height 1080"),
        "unchanged Beat Saber settings are not rewritten");
}

static void RunCaptureLayoutValidatorCheck()
{
    var displayInfo = CreateDisplayInfo(5120, 1440);
    var twoUpSettings = new ControlPanelSettings
    {
        InstanceCount = 2,
        CaptureWidth = 2560,
        CaptureHeight = 1440,
        MonitorIndex = 0
    };
    twoUpSettings.Normalize();
    AssertEqual(
        null,
        CaptureLayoutValidator.Validate(twoUpSettings, displayInfo, new[] { 0, 1 }),
        "ultrawide 2-up capture layout fits");

    var gridSettings = new ControlPanelSettings
    {
        InstanceCount = 4,
        CaptureWidth = 2560,
        CaptureHeight = 1440,
        MonitorIndex = 0
    };
    gridSettings.Normalize();
    var gridIssue = CaptureLayoutValidator.Validate(gridSettings, displayInfo, new[] { 0, 1, 2, 3 });
    AssertContains("does not fit", gridIssue, "ultrawide 2x2 capture layout blocked");
    AssertContains("Instance 3", gridIssue, "ultrawide layout identifies overflowing instance");

    var invalidMonitorSettings = new ControlPanelSettings
    {
        InstanceCount = 1,
        CaptureWidth = 1920,
        CaptureHeight = 1080,
        MonitorIndex = 1
    };
    invalidMonitorSettings.Normalize();
    AssertContains(
        "Monitor 2 is selected",
        CaptureLayoutValidator.Validate(invalidMonitorSettings, displayInfo, new[] { 0 }),
        "invalid monitor index blocked");
}

static void RunCapturePreflightFailureClassificationCheck()
{
    AssertContains(
        "NVIDIA driver is too old",
        CapturePreflightRunner.ClassifyFfmpegFailure("Driver does not support the required nvenc API version. Required: 13.1 Found: 13.0"),
        "old nvenc driver classification");
    AssertContains(
        "NVENC is not available",
        CapturePreflightRunner.ClassifyFfmpegFailure("No capable devices found"),
        "missing nvenc classification");
    AssertContains(
        "selected monitor",
        CapturePreflightRunner.ClassifyFfmpegFailure("Failed to duplicate output_idx 2"),
        "monitor capture classification");
}

static void RunCapturePreflightStartRunGuardCheck(string workspace)
{
    var preflight = new FakeCapturePreflightRunner(new CapturePreflightReport
    {
        Status = "Failed",
        Summary = "Capture preflight failed.",
        Detail = "NVIDIA driver is too old for this FFmpeg NVENC build."
    });
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        displayInfoProvider: new FakeDisplayInfoProvider(CreateDisplayInfo(1920, 1080)),
        capturePreflightRunner: preflight);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0,
        ReplayProviderStatusReported = true,
        BeatLeaderReady = true,
        BeatLeaderStatus = "BeatLeader test ready",
        ScoreSaberReady = true,
        ScoreSaberStatus = "ScoreSaber test ready"
    });

    try
    {
        store.StartRun();
    }
    catch (InvalidOperationException ex)
    {
        AssertContains("Capture preflight failed", ex.Message, "capture preflight run guard message");
        AssertEqual(1, preflight.CheckCount, "capture preflight run guard check count");
        AssertEqual("Failed", store.Snapshot().CapturePreflight.Status, "capture preflight run guard stored report");
        return;
    }

    throw new InvalidOperationException("capture preflight run guard failed. Expected InvalidOperationException.");
}

static void RunFfmpegSetupInstallCheck(string workspace)
{
    var setup = new FakeFfmpegSetupService(
        new FfmpegSetupReport
        {
            Status = "Missing",
            Summary = "FFmpeg is missing.",
            CanInstall = true
        },
        new FfmpegSetupReport
        {
            Status = "Ready",
            Summary = "FFmpeg was installed and is ready.",
            Detail = "Capture verification will test NVENC.",
            FfmpegPath = Path.Combine(workspace, "ffmpeg.exe"),
            FfprobePath = Path.Combine(workspace, "ffprobe.exe"),
            CanInstall = true
        });
    var store = CreateStore(workspace, ffmpegSetupService: setup);

    var checkedReport = store.CheckFfmpegSetup();
    AssertEqual("Missing", checkedReport.Status, "ffmpeg setup check status");
    AssertEqual(1, setup.CheckCount, "ffmpeg setup check count");

    var state = store.InstallFfmpeg();
    AssertEqual("Ready", state.FfmpegSetup.Status, "ffmpeg setup install state");
    AssertEqual(1, setup.InstallCount, "ffmpeg setup install count");
    AssertEqual(setup.InstalledReport.FfmpegPath, state.Settings.FfmpegPath, "ffmpeg path saved after install");
}

static DisplayInfoSnapshot CreateDisplayInfo(int width, int height)
{
    return new DisplayInfoSnapshot
    {
        Status = "Ready",
        Summary = "1 display detected.",
        Displays = new List<DisplayInfoRecord>
        {
            new DisplayInfoRecord
            {
                Index = 0,
                MonitorNumber = 1,
                FriendlyName = "Test display",
                Width = width,
                Height = height,
                IsPrimary = true
            }
        }
    };
}

static void RunAudioLevelNormalizationCheck()
{
    var loudnessSettings = new ControlPanelSettings
    {
        AudioLevelMode = "loudness",
        AudioTargetLevelDb = -2
    };
    loudnessSettings.Normalize();
    AssertEqual("Loudness", loudnessSettings.AudioLevelMode, "normalized loudness mode");
    AssertEqual(-5d, loudnessSettings.AudioTargetLevelDb, "loudness target clamp");

    var gainSettings = new ControlPanelSettings
    {
        AudioLevelMode = "gain",
        AudioTargetLevelDb = 3
    };
    gainSettings.Normalize();
    AssertEqual("Gain", gainSettings.AudioLevelMode, "normalized gain mode");
    AssertEqual(0d, gainSettings.AudioTargetLevelDb, "gain target clamp");

    var offSettings = new ControlPanelSettings
    {
        AudioLevelMode = "off",
        AudioTargetLevelDb = double.NaN
    };
    offSettings.Normalize();
    AssertEqual("Off", offSettings.AudioLevelMode, "normalized off mode");
    AssertEqual(-12d, offSettings.AudioTargetLevelDb, "invalid target fallback");
}

static void RunAudioLevelSettingsUpdateCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        audioMode: "ProcessLoopback");
    var request = CreateSettingsUpdateRequest(store.Snapshot().Settings);
    request.AudioLevelMode = "gain";
    request.AudioTargetLevelDb = -18.5;

    var updated = store.UpdateSettings(request);
    AssertEqual("Gain", updated.Settings.AudioLevelMode, "updated audio level mode");
    AssertEqual(-18.5d, updated.Settings.AudioTargetLevelDb, "updated audio target level");

    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    var worker = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });

    SetGameProcessIds(store, 4100);
    store.StartRun();
    var assignment = store.GetAssignment(worker.WorkerId);
    AssertEqual("Gain", assignment.AudioLevelMode, "assignment updated audio level mode");
    AssertEqual(-18.5d, assignment.AudioTargetLevelDb, "assignment updated audio target level");
}

static void RunGamePresentationSettingsSyncCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    var initial = store.Snapshot();
    AssertEqual(1, initial.Settings.GamePresentationSettingsVersion, "default game presentation version");
    AssertEqual(true, initial.Settings.GamePresentation.NoHud, "default no HUD setting");
    AssertEqual(false, initial.Settings.GamePresentation.OverrideReplayPlayerSettings, "default replay player settings override");
    AssertEqual(false, initial.Settings.GamePresentation.RestorePlayerSettingsOnExit, "default restore player settings on exit");
    AssertEqual(false, initial.Settings.GamePresentation.ApplyJdFixerSettings, "default JDFixer apply setting");
    AssertEqual(0.3f, initial.Settings.GamePresentation.SfxVolume, "default SFX volume setting");

    var worker = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });

    AssertEqual(
        initial.Settings.GamePresentationSettingsVersion,
        worker.Settings.GamePresentationSettingsVersion,
        "registration game presentation version");
    AssertEqual(true, worker.Settings.GamePresentation.NoHud, "registration no HUD setting");
    AssertEqual(false, worker.Settings.GamePresentation.OverrideReplayPlayerSettings, "registration replay player settings override");
    AssertEqual(false, worker.Settings.GamePresentation.RestorePlayerSettingsOnExit, "registration restore player settings on exit");
    AssertEqual(false, worker.Settings.GamePresentation.ApplyJdFixerSettings, "registration JDFixer apply setting");
    AssertEqual(0.3f, worker.Settings.GamePresentation.SfxVolume, "registration SFX volume setting");

    var request = CreateSettingsUpdateRequest(initial.Settings);
    request.GamePresentation = new GamePresentationSettings
    {
        NoHud = false,
        OverrideReplayPlayerSettings = true,
        RestorePlayerSettingsOnExit = true,
        ShowWatermark = false,
        ShowLeftSaber = true,
        ShowRightSaber = true,
        ShowTimelineMisses = true,
        ShowTimelineBombs = true,
        ShowTimelinePauses = true,
        SfxVolume = 0.45f,
        NoTextsAndHuds = false,
        AdvancedHud = true,
        ReduceDebris = true,
        AdaptiveSfx = true,
        ArcsHapticFeedback = true,
        ArcVisibility = GamePresentationSettings.ArcVisibilityStandard,
        EnvironmentEffectsFilterDefaultPreset = GamePresentationSettings.EnvironmentEffectsStrobeFilter,
        EnvironmentEffectsFilterExpertPlusPreset = GamePresentationSettings.EnvironmentEffectsNoEffects,
        HeadsetHapticIntensity = 0.65f,
        ApplyJdFixerSettings = true,
        JdFixerMode = GamePresentationSettings.JdFixerModeReactionTime,
        JdFixerJumpDistance = 19.25f,
        JdFixerReactionTime = 475f
    };

    var updated = store.UpdateSettings(request);
    AssertEqual(false, updated.Settings.GamePresentation.NoHud, "updated no HUD setting");
    AssertEqual(true, updated.Settings.GamePresentation.OverrideReplayPlayerSettings, "updated replay player settings override");
    AssertEqual(true, updated.Settings.GamePresentation.RestorePlayerSettingsOnExit, "updated restore player settings on exit");
    AssertEqual(false, updated.Settings.GamePresentation.ShowWatermark, "updated watermark setting");
    AssertEqual(0.45f, updated.Settings.GamePresentation.SfxVolume, "updated SFX volume setting");
    AssertEqual(false, updated.Settings.GamePresentation.NoTextsAndHuds, "updated no texts and HUDs setting");
    AssertEqual(GamePresentationSettings.ArcVisibilityStandard, updated.Settings.GamePresentation.ArcVisibility, "updated arc visibility setting");
    AssertEqual(true, updated.Settings.GamePresentation.ApplyJdFixerSettings, "updated JDFixer apply setting");
    AssertEqual(GamePresentationSettings.JdFixerModeReactionTime, updated.Settings.GamePresentation.JdFixerMode, "updated JDFixer mode");
    AssertEqual(19.25f, updated.Settings.GamePresentation.JdFixerJumpDistance, "updated JDFixer jump distance");
    AssertEqual(475f, updated.Settings.GamePresentation.JdFixerReactionTime, "updated JDFixer reaction time");
    AssertEqual(2, updated.Settings.GamePresentationSettingsVersion, "updated game presentation version");

    var heartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = worker.WorkerId,
        Status = "Online",
        AppliedGamePresentationSettingsVersion = initial.Settings.GamePresentationSettingsVersion,
        GamePresentationSyncStatus = "Applied"
    });
    AssertEqual(2, heartbeat.GamePresentationSettingsVersion, "heartbeat game presentation version");
    AssertEqual(false, heartbeat.GamePresentation.NoHud, "heartbeat no HUD setting");
    AssertEqual(true, heartbeat.GamePresentation.OverrideReplayPlayerSettings, "heartbeat replay player settings override");
    AssertEqual(true, heartbeat.GamePresentation.RestorePlayerSettingsOnExit, "heartbeat restore player settings on exit");
    AssertEqual(false, heartbeat.GamePresentation.ShowWatermark, "heartbeat watermark setting");
    AssertEqual(0.45f, heartbeat.GamePresentation.SfxVolume, "heartbeat SFX volume setting");
    AssertEqual(false, heartbeat.GamePresentation.NoTextsAndHuds, "heartbeat no texts and HUDs setting");
    AssertEqual(true, heartbeat.GamePresentation.ApplyJdFixerSettings, "heartbeat JDFixer apply setting");
    AssertEqual(475f, heartbeat.GamePresentation.JdFixerReactionTime, "heartbeat JDFixer reaction time");

    var snapshot = store.Snapshot();
    AssertEqual(1, snapshot.Instances[0].AppliedGamePresentationSettingsVersion, "worker reported applied game presentation version");
    AssertEqual("Applied", snapshot.Instances[0].GamePresentationSyncStatus, "worker game presentation sync status");

    var assignment = store.GetAssignment(worker.WorkerId);
    AssertEqual(2, assignment.GamePresentationSettingsVersion, "assignment game presentation version");
    AssertEqual(false, assignment.GamePresentation.NoHud, "assignment no HUD setting");
    AssertEqual(true, assignment.GamePresentation.OverrideReplayPlayerSettings, "assignment replay player settings override");
    AssertEqual(true, assignment.GamePresentation.RestorePlayerSettingsOnExit, "assignment restore player settings on exit");
    AssertEqual(false, assignment.GamePresentation.ShowWatermark, "assignment watermark setting");
    AssertEqual(0.45f, assignment.GamePresentation.SfxVolume, "assignment SFX volume setting");
    AssertEqual(true, assignment.GamePresentation.ApplyJdFixerSettings, "assignment JDFixer apply setting");
    AssertEqual(475f, assignment.GamePresentation.JdFixerReactionTime, "assignment JDFixer reaction time");
    AssertEqual(GamePresentationSettings.EnvironmentEffectsNoEffects, assignment.GamePresentation.EnvironmentEffectsFilterExpertPlusPreset, "assignment expert plus effects setting");

    var preserveRequest = CreateSettingsUpdateRequest(updated.Settings);
    preserveRequest.GamePresentation = null;
    preserveRequest.TargetFps = 72;
    var preserved = store.UpdateSettings(preserveRequest);
    AssertEqual(false, preserved.Settings.GamePresentation.NoHud, "omitted game presentation keeps no HUD setting");
    AssertEqual(true, preserved.Settings.GamePresentation.OverrideReplayPlayerSettings, "omitted game presentation keeps replay player settings override");
    AssertEqual(true, preserved.Settings.GamePresentation.RestorePlayerSettingsOnExit, "omitted game presentation keeps restore player settings on exit");
    AssertEqual(true, preserved.Settings.GamePresentation.ApplyJdFixerSettings, "omitted game presentation keeps JDFixer apply setting");
    AssertEqual(0.45f, preserved.Settings.GamePresentation.SfxVolume, "omitted game presentation keeps SFX volume");
    AssertEqual(2, preserved.Settings.GamePresentationSettingsVersion, "omitted game presentation keeps version");
}

static void RunRequireAudioGuardCheck(string workspace)
{
    var store = CreateStore(workspace, requireAudioForRun: true);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);

    for (var index = 0; index < 3; index++)
    {
        store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "worker-" + index,
            WorkerName = "Worker " + index,
            PreferredInstanceIndex = index
        });
    }

    AssertThrows<InvalidOperationException>(
        () => store.StartRun(),
        "audio required with audio disabled guard");
}

static void RunProcessLoopbackAudioGuardCheck(string workspace)
{
    var missingPidStore = CreateStore(
        Path.Combine(workspace, "missing-pids"),
        audioMode: "ProcessLoopback",
        requireAudioForRun: true);
    using var missingPidFiles = CreateReplayFiles(1);
    missingPidStore.ImportFiles(missingPidFiles.Collection);
    RegisterWorkers(missingPidStore, "missing-pid-worker");

    AssertThrows<InvalidOperationException>(
        () => missingPidStore.StartRun(),
        "process loopback missing pid guard");

    var readyStore = CreateStore(
        Path.Combine(workspace, "ready"),
        instanceCount: 2,
        audioMode: "ProcessLoopback",
        requireAudioForRun: true);
    using var readyFiles = CreateReplayFiles(2);
    readyStore.ImportFiles(readyFiles.Collection);
    RegisterWorkers(readyStore, "ready-worker", 2);
    SetGameProcessIds(readyStore, 4100, 4101);

    readyStore.StartRun();
    var firstAssignment = readyStore.GetAssignment("ready-worker-0");
    var secondAssignment = readyStore.GetAssignment("ready-worker-1");
    AssertEqual("ProcessLoopback", firstAssignment.AudioMode, "process loopback assignment audio mode");
    AssertEqual(4100, firstAssignment.TargetProcessId, "first process loopback target process");
    AssertEqual(4101, secondAssignment.TargetProcessId, "second process loopback target process");
    AssertEqual("", firstAssignment.AudioDeviceName, "process loopback does not assign dshow device");
}

static void RunParallelAssignmentCheck(string workspace)
{
    var store = CreateStore(workspace);
    using var files = CreateReplayFiles(3);
    var imported = store.ImportFiles(files.Collection);
    AssertEqual(3, imported.Count, "imported replay count");

    var workerIds = Enumerable.Range(0, 3)
        .Select(index =>
        {
            var response = store.RegisterWorker(new WorkerRegisterRequest
            {
                WorkerId = "worker-" + index,
                WorkerName = "Worker " + index,
                PreferredInstanceIndex = index
            });
            AssertEqual(index, response.InstanceIndex, "registered instance " + index);
            return response.WorkerId;
        })
        .ToArray();

    store.StartRun();

    var assignments = Task.WhenAll(workerIds.Select(workerId =>
            Task.Run(() => store.GetAssignment(workerId))))
        .GetAwaiter()
        .GetResult();

    AssertEqual(3, assignments.Count(assignment => assignment.HasAssignment), "active assignment count");
    AssertEqual(3, assignments.Select(assignment => assignment.AssignmentId).Distinct().Count(), "distinct assignment count");
    AssertEqual(3, assignments.Select(assignment => assignment.ReplayId).Distinct().Count(), "distinct replay count");
    AssertEqual(1, assignments.Select(assignment => assignment.OutputDirectory).Distinct().Count(), "shared run output directory count");

    for (var index = 0; index < assignments.Length; index++)
    {
        AssertEqual(index, assignments[index].InstanceIndex.GetValueOrDefault(-1), "assignment instance " + index);
        AssertEqual("mp4", assignments[index].OutputFormat, "assignment output format " + index);
        AssertEqual(1, assignments[index].MonitorIndex, "assignment monitor index " + index);
    }
}

static void RunFourInstanceAssignmentCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 4, maxConcurrentRecordings: 4);
    using var files = CreateReplayFiles(4);
    store.ImportFiles(files.Collection);

    var workerIds = Enumerable.Range(0, 4)
        .Select(index => store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "quad-worker-" + index,
            WorkerName = "Quad Worker " + index,
            PreferredInstanceIndex = index
        }).WorkerId)
        .ToArray();

    store.StartRun();
    var assignments = workerIds
        .Select(workerId => store.GetAssignment(workerId))
        .ToArray();

    AssertEqual(4, assignments.Count(assignment => assignment.HasAssignment), "four active assignment count");
    AssertEqual(4, assignments.Select(assignment => assignment.AssignmentId).Distinct().Count(), "four distinct assignments");
    AssertEqual(4, assignments.Select(assignment => assignment.InstanceIndex).Distinct().Count(), "four distinct instances");
    for (var index = 0; index < assignments.Length; index++)
    {
        AssertEqual(index, assignments[index].InstanceIndex.GetValueOrDefault(-1), "four assignment instance " + index);
    }
}

static void RunImportedQueuePlanDistributionCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 4, maxConcurrentRecordings: 4);
    using var files = CreateReplayFiles(5);
    var imported = store.ImportFiles(files.Collection);
    AssertEqual(5, imported.Count, "planned import replay count");

    var plannedState = store.Snapshot();
    var expectedPlan = new[] { 0, 1, 2, 3, 0 };
    for (var index = 0; index < expectedPlan.Length; index++)
    {
        AssertEqual(
            expectedPlan[index],
            plannedState.Queue[index].AssignedInstance.GetValueOrDefault(-1),
            "imported replay planned instance " + index);
    }

    var workerIds = Enumerable.Range(0, 4)
        .Select(index => store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "planned-worker-" + index,
            WorkerName = "Planned Worker " + index,
            PreferredInstanceIndex = index
        }).WorkerId)
        .ToArray();

    store.StartRun();
    var assignment = store.GetAssignment(workerIds[1]);
    AssertEqual(true, assignment.HasAssignment, "planned worker receives assignment");
    AssertEqual(plannedState.Queue[1].Id, assignment.ReplayId, "planned worker receives matching planned replay");
    AssertEqual(1, assignment.InstanceIndex.GetValueOrDefault(-1), "planned worker assignment instance");
}

static void RunEnabledInstanceQueuePlanDistributionCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 4, maxConcurrentRecordings: 4);
    using var files = CreateReplayFiles(5);
    store.ImportFiles(files.Collection);

    var threeLaneState = store.SetInstanceEnabled(3, enabled: false);
    AssertQueuePlan(threeLaneState, new[] { 0, 1, 2, 0, 1 }, "three enabled instance queue plan");
    AssertEqual(3, threeLaneState.Settings.MaxConcurrentRecordings, "three enabled max concurrent recordings");

    var twoLaneState = store.SetInstanceEnabled(2, enabled: false);
    AssertQueuePlan(twoLaneState, new[] { 0, 1, 0, 1, 0 }, "two enabled instance queue plan");
    AssertEqual(2, twoLaneState.Settings.MaxConcurrentRecordings, "two enabled max concurrent recordings");

    var workerIds = Enumerable.Range(0, 3)
        .Select(index => store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "enabled-worker-" + index,
            WorkerName = "Enabled Worker " + index,
            PreferredInstanceIndex = index
        }).WorkerId)
        .ToArray();

    store.StartRun();
    var firstAssignment = store.GetAssignment(workerIds[0]);
    var secondAssignment = store.GetAssignment(workerIds[1]);
    var disabledAssignment = store.GetAssignment(workerIds[2]);

    AssertEqual(true, firstAssignment.HasAssignment, "first enabled worker receives assignment");
    AssertEqual(true, secondAssignment.HasAssignment, "second enabled worker receives assignment");
    AssertEqual(false, disabledAssignment.HasAssignment, "disabled worker receives no assignment");
    AssertEqual(0, firstAssignment.InstanceIndex.GetValueOrDefault(-1), "first enabled assignment instance");
    AssertEqual(1, secondAssignment.InstanceIndex.GetValueOrDefault(-1), "second enabled assignment instance");
}

static void RunActiveInstanceCountQueuePlanDistributionCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 3, maxConcurrentRecordings: 3);
    using var files = CreateReplayFiles(6);
    store.ImportFiles(files.Collection);

    var threeLaneState = store.SetActiveInstanceCount(3);
    AssertQueuePlan(threeLaneState, new[] { 0, 1, 2, 0, 1, 2 }, "active count three-lane queue plan");
    AssertEqual(3, threeLaneState.Settings.InstanceCount, "active count preserves configured inventory");
    AssertEqual(3, threeLaneState.Settings.MaxConcurrentRecordings, "active count three-lane concurrency");

    var twoLaneState = store.SetActiveInstanceCount(2);
    AssertQueuePlan(twoLaneState, new[] { 0, 1, 0, 1, 0, 1 }, "active count two-lane queue plan");
    AssertEqual(3, twoLaneState.Settings.InstanceCount, "active count downshift keeps managed instance inventory");
    AssertEqual(2, twoLaneState.Settings.MaxConcurrentRecordings, "active count two-lane concurrency");
    AssertEqual(true, twoLaneState.Instances[0].Enabled, "active count two-lane instance 1 enabled");
    AssertEqual(true, twoLaneState.Instances[1].Enabled, "active count two-lane instance 2 enabled");
    AssertEqual(false, twoLaneState.Instances[2].Enabled, "active count two-lane instance 3 disabled");

    var restoredState = store.SetActiveInstanceCount(3);
    AssertQueuePlan(restoredState, new[] { 0, 1, 2, 0, 1, 2 }, "active count restored three-lane queue plan");
    AssertEqual(3, restoredState.Settings.MaxConcurrentRecordings, "active count restored concurrency");
    AssertEqual(true, restoredState.Instances[2].Enabled, "active count restored instance 3 enabled");
}

static void RunConfiguredInstanceAssignmentCheck(string workspace)
{
    var store = CreateStore(workspace);
    using var files = CreateReplayFiles(4);
    store.ImportFiles(files.Collection);

    var workerIds = Enumerable.Range(0, 3)
        .Select(index => store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "limited-worker-" + index,
            WorkerName = "Limited Worker " + index,
            PreferredInstanceIndex = index
        }).WorkerId)
        .ToArray();

    store.StartRun();

    var firstWave = workerIds
        .Select(workerId => store.GetAssignment(workerId))
        .ToArray();

    AssertEqual(3, firstWave.Count(assignment => assignment.HasAssignment), "configured active assignment count");

    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workerIds[0],
        AssignmentId = firstWave[0].AssignmentId!,
        Status = "Completed",
        OutputPath = Path.Combine(workspace, "recorded.mp4")
    });

    var nextAssignment = store.GetAssignment(workerIds[0]);
    AssertEqual(true, nextAssignment.HasAssignment, "worker receives queued assignment after slot opens");
    AssertEqual(false, firstWave.Select(assignment => assignment.ReplayId).Contains(nextAssignment.ReplayId), "waiting worker receives next queued replay");
}

static void RunActiveRunInstanceSettingsGuardCheck(string workspace)
{
    var store = CreateStore(workspace);
    using var files = CreateReplayFiles(3);
    store.ImportFiles(files.Collection);
    RegisterWorkers(store, "settings-guard-worker", 3);
    store.StartRun();

    var request = CreateSettingsUpdateRequest(store.Snapshot().Settings);
    request.InstanceCount = 2;
    request.MaxConcurrentRecordings = 2;
    AssertThrows<InvalidOperationException>(
        () => store.UpdateSettings(request),
        "active run refuses instance count change");
}

static void RunSingleReplayFailureDoesNotCancelOtherAssignmentsCheck(string workspace)
{
    var store = CreateStore(workspace);
    using var files = CreateReplayFiles(4);
    var imported = store.ImportFiles(files.Collection);

    var workerIds = Enumerable.Range(0, 3)
        .Select(index => store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "resilient-worker-" + index,
            WorkerName = "Resilient Worker " + index,
            PreferredInstanceIndex = index
        }).WorkerId)
        .ToArray();

    store.StartRun();
    var firstWave = workerIds
        .Select(workerId => store.GetAssignment(workerId))
        .ToArray();

    AssertEqual(3, firstWave.Count(assignment => assignment.HasAssignment), "resilient active assignment count");

    var failedState = store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workerIds[0],
        AssignmentId = firstWave[0].AssignmentId!,
        Status = "Failed",
        Error = "Lag spike detected during replay recording: frame time 2988ms exceeded 250ms. Recording is invalid."
    });

    AssertEqual(true, failedState.Run.IsRunning, "single failure run remains running");
    AssertEqual(false, failedState.Run.CancellationRequested, "single failure does not request cancellation");
    AssertEqual("Running", failedState.Run.Status, "single failure run status");
    AssertEqual(1, failedState.Run.FailedCount, "single failure failed count");

    foreach (var activeAssignment in firstWave.Where(assignment => assignment.HasAssignment).Skip(1))
    {
        var activeReplay = failedState.Queue.Single(item => item.Id == activeAssignment.ReplayId);
        AssertEqual("Assigned", activeReplay.Status, "other assignment stays active");
    }

    var queuedReplay = failedState.Queue.Single(item => item.Id == imported[3].Id);
    AssertEqual("Queued", queuedReplay.Status, "queued replay stays queued after single failure");

    var replacement = store.GetAssignment(workerIds[0]);
    AssertEqual(true, replacement.HasAssignment, "freed worker receives queued replay after one failure");
    AssertEqual(imported[3].Id, replacement.ReplayId, "freed worker receives original queued replay");
}

static void RunHeartbeatFpsLagSpikeCancellationCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);

    var workerId = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "fps-worker",
        WorkerName = "FPS Worker",
        PreferredInstanceIndex = 0
    }).WorkerId;

    var idleHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Online",
        FramesPerSecond = 42.2
    });
    AssertEqual(false, idleHeartbeat.ShouldCancelAssignment, "idle low fps does not cancel assignment");
    AssertEqual(42.2, store.Snapshot().Instances[0].LastReportedFramesPerSecond ?? 0, "idle fps stored");

    store.StartRun();
    var assignment = store.GetAssignment(workerId);
    AssertEqual(true, assignment.HasAssignment, "fps assignment exists");

    var healthyHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 60
    });
    AssertEqual(false, healthyHeartbeat.ShouldCancelAssignment, "healthy recording fps does not cancel assignment");
    AssertEqual(false, healthyHeartbeat.CancellationFailsAssignment, "healthy recording fps does not fail assignment");

    var graceHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 49.4
    });
    AssertEqual(false, graceHeartbeat.ShouldCancelAssignment, "startup grace low fps does not cancel assignment");
    AssertEqual(false, graceHeartbeat.CancellationFailsAssignment, "startup grace low fps does not fail assignment");

    SetAssignmentAssignedAtUtc(store, assignment.AssignmentId!, DateTimeOffset.UtcNow.AddSeconds(-20));
    var assignmentAgeGraceHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 49.4
    });
    AssertEqual(false, assignmentAgeGraceHeartbeat.ShouldCancelAssignment, "old assignment time does not cancel during recording startup grace");
    AssertEqual(false, assignmentAgeGraceHeartbeat.CancellationFailsAssignment, "old assignment time does not fail during recording startup grace");

    SetAssignmentRecordingStartedAtUtc(store, assignment.AssignmentId!, DateTimeOffset.UtcNow.AddSeconds(-20));
    var firstLagHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 49.4
    });
    AssertEqual(false, firstLagHeartbeat.ShouldCancelAssignment, "single low recording fps heartbeat does not cancel assignment");
    AssertEqual(false, firstLagHeartbeat.CancellationFailsAssignment, "single low recording fps heartbeat does not fail assignment");

    var secondLagHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 49.4
    });
    AssertEqual(false, secondLagHeartbeat.ShouldCancelAssignment, "brief sustained low recording fps does not cancel assignment");
    AssertEqual(false, secondLagHeartbeat.CancellationFailsAssignment, "brief sustained low recording fps does not fail assignment");

    SetLowFpsRecordingStartedAtUtc(store, workerId, DateTimeOffset.UtcNow.AddSeconds(-11));
    var lagHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 49.4
    });
    AssertEqual(true, lagHeartbeat.ShouldCancelAssignment, "low recording fps cancels assignment");
    AssertEqual(true, lagHeartbeat.CancellationFailsAssignment, "low recording fps fails assignment");
    AssertContains("Lag spike detected", lagHeartbeat.CancellationReason, "low recording fps cancellation reason");
    AssertContains("worker minimum FPS 49.4 stayed below 50 FPS", lagHeartbeat.CancellationReason, "low recording fps threshold reason");

    var state = store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workerId,
        AssignmentId = assignment.AssignmentId!,
        Status = lagHeartbeat.CancellationFailsAssignment ? "Failed" : "Stopped",
        Error = lagHeartbeat.CancellationReason
    });
    var replay = state.Queue.Single();
    AssertEqual("Failed", replay.Status, "low recording fps replay failed");
    AssertContains("worker minimum FPS 49.4 stayed below 50 FPS", replay.Error, "low recording fps replay error");
}

static void RunHeartbeatFinalizingFpsDoesNotCancelCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);

    var workerId = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "finalizing-fps-worker",
        WorkerName = "Finalizing FPS Worker",
        PreferredInstanceIndex = 0
    }).WorkerId;

    store.StartRun();
    var assignment = store.GetAssignment(workerId);
    SetAssignmentRecordingStartedAtUtc(store, assignment.AssignmentId!, DateTimeOffset.UtcNow.AddSeconds(-20));
    SetLowFpsRecordingStartedAtUtc(store, workerId, DateTimeOffset.UtcNow.AddSeconds(-11));

    var finalizing = store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workerId,
        AssignmentId = assignment.AssignmentId!,
        Status = "Finalizing"
    });
    AssertEqual("Finalizing", finalizing.Queue.Single().Status, "normal finalizing report keeps replay active");
    AssertEqual("Finalizing", finalizing.Instances[0].Status, "normal finalizing report updates worker status");

    var heartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Finalizing",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 12.3,
        AverageFramesPerSecond = 12.3,
        SampledFrameCount = 60
    });
    AssertEqual(false, heartbeat.ShouldCancelAssignment, "finalizing low fps does not cancel normal assignment");
    AssertEqual(false, heartbeat.CancellationFailsAssignment, "finalizing low fps does not fail normal assignment");
    AssertEqual("Finalizing", store.Snapshot().Queue.Single().Status, "finalizing replay status remains active");
}

static void RunBenchmarkRecommendationAndQueueIsolationCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 3);
    using var files = CreateReplayFiles(1);
    var imported = store.ImportFiles(files.Collection);
    RegisterWorkers(store, "benchmark-worker", 3);

    var started = store.StartBenchmark();
    AssertEqual(true, started.Benchmark.IsRunning, "benchmark starts");
    AssertEqual(1, started.Benchmark.Passes.Count, "benchmark first pass count");
    AssertEqual(1, started.Benchmark.Passes[0].Concurrency, "benchmark first pass concurrency");

    var first = store.GetAssignment("benchmark-worker-0");
    AssertEqual(true, first.HasAssignment, "benchmark first assignment exists");
    AssertEqual("Benchmark", first.AssignmentKind, "benchmark assignment kind");
    AssertEqual("Queued", store.Snapshot().Queue.Single().Status, "benchmark assignment does not mutate queue status");

    store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = "benchmark-worker-0",
        Status = "Recording",
        CurrentReplayId = first.ReplayId,
        FramesPerSecond = 57.2,
        AverageFramesPerSecond = 59.6,
        SampledFrameCount = 120
    });
    var afterFirstHeartbeat = store.Snapshot().Benchmark.Passes[0].Assignments[0];
    AssertEqual(57.2, afterFirstHeartbeat.MinimumFramesPerSecond ?? 0, "benchmark minimum fps stored");
    AssertEqual(59.6, afterFirstHeartbeat.AverageFramesPerSecond ?? 0, "benchmark average fps stored");
    AssertEqual(120, afterFirstHeartbeat.SampledFrameCount, "benchmark sample count stored");

    CompleteBenchmarkAssignment(store, "benchmark-worker-0", first.AssignmentId!, workspace, "pass1.mkv");
    var pass2State = store.Snapshot();
    AssertEqual(true, pass2State.Benchmark.IsRunning, "benchmark continues after pass one");
    AssertEqual(2, pass2State.Benchmark.Passes.Count, "benchmark second pass count");
    AssertEqual(1, pass2State.Benchmark.RecommendedWorkerCount ?? 0, "benchmark recommends first passing count");

    var secondA = store.GetAssignment("benchmark-worker-0");
    var secondB = store.GetAssignment("benchmark-worker-1");
    AssertEqual(2, store.Snapshot().Benchmark.Passes[1].Assignments.Count, "benchmark second pass assignments");
    CompleteBenchmarkAssignment(store, "benchmark-worker-0", secondA.AssignmentId!, workspace, "pass2-a.mkv");
    CompleteBenchmarkAssignment(store, "benchmark-worker-1", secondB.AssignmentId!, workspace, "pass2-b.mkv");

    var thirdA = store.GetAssignment("benchmark-worker-0");
    var thirdB = store.GetAssignment("benchmark-worker-1");
    var thirdC = store.GetAssignment("benchmark-worker-2");
    CompleteBenchmarkAssignment(store, "benchmark-worker-0", thirdA.AssignmentId!, workspace, "pass3-a.mkv");
    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = "benchmark-worker-1",
        AssignmentId = thirdB.AssignmentId!,
        Status = "Failed",
        Error = "Synthetic benchmark failure"
    });
    CompleteBenchmarkAssignment(store, "benchmark-worker-2", thirdC.AssignmentId!, workspace, "pass3-c.mkv");

    var final = store.Snapshot();
    AssertEqual(false, final.Benchmark.IsRunning, "benchmark stops after failed pass");
    AssertEqual("Complete", final.Benchmark.Status, "benchmark final status with recommendation");
    AssertEqual(2, final.Benchmark.RecommendedWorkerCount ?? 0, "benchmark recommends highest passing count");
    AssertContains("Synthetic benchmark failure", final.Benchmark.FailureReason, "benchmark failure reason");
    AssertEqual("Queued", final.Queue.Single().Status, "benchmark leaves queue status unchanged");
    AssertEqual<string?>(null, final.Queue.Single().OutputPath, "benchmark leaves queue output path unchanged");
    AssertEqual(0, final.Run.CompletedCount, "benchmark does not increment run completed count");
    AssertEqual(0, final.Run.FailedCount, "benchmark does not increment run failed count");
    AssertEqual(true, File.Exists(final.Benchmark.ReportPath), "benchmark writes report file");
    AssertEqual(imported[0].Id, final.Benchmark.SourceQueueItemIds.Single(), "benchmark records source replay id");
}

static void RunBenchmarkStopCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 2);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    RegisterWorkers(store, "benchmark-stop-worker", 2);

    store.StartBenchmark();
    var assignment = store.GetAssignment("benchmark-stop-worker-0");
    var stopped = store.StopBenchmark();
    AssertEqual("Stopping", stopped.Benchmark.Status, "benchmark stop status");
    AssertEqual(1, stopped.Run.ForceStopCommandId, "benchmark stop broadcasts force command");

    var heartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = "benchmark-stop-worker-0",
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 60
    });
    AssertEqual(true, heartbeat.ShouldCancelAssignment, "benchmark stop cancels active assignment");
    AssertEqual(true, heartbeat.ShouldOpenPauseMenu, "benchmark stop opens pause menu");
    AssertEqual(false, heartbeat.CancellationFailsAssignment, "operator stop does not fail assignment");

    var final = store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = "benchmark-stop-worker-0",
        AssignmentId = assignment.AssignmentId!,
        Status = "Stopped",
        Error = heartbeat.CancellationReason
    });
    AssertEqual(false, final.Benchmark.IsRunning, "benchmark stopped");
    AssertEqual("Stopped", final.Benchmark.Status, "benchmark final stopped status");
}

static void RunBenchmarkHeartbeatFpsCancellationCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    RegisterWorkers(store, "benchmark-fps-worker", 1);

    store.StartBenchmark();
    var assignment = store.GetAssignment("benchmark-fps-worker-0");
    store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = "benchmark-fps-worker-0",
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 60,
        AverageFramesPerSecond = 60,
        SampledFrameCount = 60
    });

    SetBenchmarkAssignmentRecordingStartedAtUtc(store, assignment.AssignmentId!, DateTimeOffset.UtcNow.AddSeconds(-20));
    SetLowFpsRecordingStartedAtUtc(store, "benchmark-fps-worker-0", DateTimeOffset.UtcNow.AddSeconds(-11));
    var lagHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = "benchmark-fps-worker-0",
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 49.4,
        AverageFramesPerSecond = 49.4,
        SampledFrameCount = 60
    });
    AssertEqual(true, lagHeartbeat.ShouldCancelAssignment, "benchmark low fps cancels assignment");
    AssertEqual(true, lagHeartbeat.CancellationFailsAssignment, "benchmark low fps fails assignment");
    AssertContains("Benchmark FPS drop detected", lagHeartbeat.CancellationReason, "benchmark low fps cancellation reason");

    var final = store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = "benchmark-fps-worker-0",
        AssignmentId = assignment.AssignmentId!,
        Status = "Failed",
        Error = lagHeartbeat.CancellationReason
    });
    AssertEqual(false, final.Benchmark.IsRunning, "benchmark low fps stops benchmark");
    AssertEqual("Failed", final.Benchmark.Status, "benchmark low fps final status");
    AssertEqual<int?>(null, final.Benchmark.RecommendedWorkerCount, "benchmark low fps no recommendation");
}

static void RunBenchmarkHighAverageFpsDoesNotCancelCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    RegisterWorkers(store, "benchmark-average-worker", 1);

    store.StartBenchmark();
    var assignment = store.GetAssignment("benchmark-average-worker-0");
    SetBenchmarkAssignmentRecordingStartedAtUtc(store, assignment.AssignmentId!, DateTimeOffset.UtcNow.AddSeconds(-20));
    SetLowFpsRecordingStartedAtUtc(store, "benchmark-average-worker-0", DateTimeOffset.UtcNow.AddSeconds(-11));

    var heartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = "benchmark-average-worker-0",
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 24.1,
        AverageFramesPerSecond = 140.2,
        SampledFrameCount = 120
    });
    AssertEqual(false, heartbeat.ShouldCancelAssignment, "benchmark high average fps does not cancel assignment");
    var assignmentState = store.Snapshot().Benchmark.Passes.Single().Assignments.Single();
    AssertEqual(24.1, assignmentState.MinimumFramesPerSecond ?? 0, "benchmark still reports minimum fps");
    AssertEqual(140.2, assignmentState.AverageFramesPerSecond ?? 0, "benchmark still reports average fps");
}

static void RunBenchmarkFinalizingFpsDoesNotCancelCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    RegisterWorkers(store, "benchmark-finalizing-worker", 1);

    store.StartBenchmark();
    var assignment = store.GetAssignment("benchmark-finalizing-worker-0");
    store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = "benchmark-finalizing-worker-0",
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 58.1,
        AverageFramesPerSecond = 60.2,
        SampledFrameCount = 60
    });

    SetBenchmarkAssignmentRecordingStartedAtUtc(store, assignment.AssignmentId!, DateTimeOffset.UtcNow.AddSeconds(-20));
    SetLowFpsRecordingStartedAtUtc(store, "benchmark-finalizing-worker-0", DateTimeOffset.UtcNow.AddSeconds(-11));
    var finalizingState = store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = "benchmark-finalizing-worker-0",
        AssignmentId = assignment.AssignmentId!,
        Status = "Finalizing"
    });
    var finalizingAssignment = finalizingState.Benchmark.Passes.Single().Assignments.Single();
    AssertEqual("Finalizing", finalizingAssignment.Status, "benchmark finalizing report updates assignment status");
    AssertEqual(true, finalizingAssignment.FinalizingStartedAtUtc.HasValue, "benchmark finalizing start recorded");

    var heartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = "benchmark-finalizing-worker-0",
        Status = "Finalizing",
        CurrentReplayId = assignment.ReplayId,
        FramesPerSecond = 11.5,
        AverageFramesPerSecond = 11.5,
        SampledFrameCount = 60
    });
    AssertEqual(false, heartbeat.ShouldCancelAssignment, "benchmark finalizing low fps does not cancel assignment");

    var afterFinalizingHeartbeat = store.Snapshot().Benchmark.Passes.Single().Assignments.Single();
    AssertEqual(58.1, afterFinalizingHeartbeat.MinimumFramesPerSecond ?? 0, "benchmark finalizing fps does not lower gameplay min fps");
    AssertEqual(60.2, afterFinalizingHeartbeat.AverageFramesPerSecond ?? 0, "benchmark finalizing fps does not lower gameplay average fps");
    AssertEqual(60, afterFinalizingHeartbeat.SampledFrameCount, "benchmark finalizing fps does not add gameplay samples");

    SetBenchmarkAssignmentFinalizingStartedAtUtc(store, assignment.AssignmentId!, DateTimeOffset.UtcNow.AddSeconds(-3));
    var final = CompleteBenchmarkAssignment(
        store,
        "benchmark-finalizing-worker-0",
        assignment.AssignmentId!,
        workspace,
        "finalizing.mkv");
    var completedAssignment = final.Benchmark.Passes.Single().Assignments.Single();
    AssertEqual("Completed", completedAssignment.Status, "benchmark finalizing assignment completes");
    AssertEqual(true, completedAssignment.FinalizationSeconds >= 2.5, "benchmark finalization duration recorded");
    AssertEqual("Complete", final.Benchmark.Status, "benchmark finalizing low fps still completes");
}

static void RunBenchmarkSelectedConcurrencyCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 3);
    using var files = CreateReplayFiles(2);
    store.ImportFiles(files.Collection);
    RegisterWorkers(store, "benchmark-selected-worker", 3);

    var started = store.StartBenchmark(new BenchmarkStartRequest
    {
        ConcurrencyLevels = new List<int> { 2 }
    });
    AssertEqual(1, started.Benchmark.Passes.Count, "selected benchmark starts one pass");
    AssertEqual(2, started.Benchmark.Passes[0].Concurrency, "selected benchmark skips lower concurrency");
    AssertEqual(1, started.Benchmark.SelectedConcurrencies.Count, "selected benchmark records selected count");
    AssertEqual(2, started.Benchmark.SelectedConcurrencies[0], "selected benchmark records selected concurrency");

    var first = store.GetAssignment("benchmark-selected-worker-0");
    var second = store.GetAssignment("benchmark-selected-worker-1");
    CompleteBenchmarkAssignment(store, "benchmark-selected-worker-0", first.AssignmentId!, workspace, "selected-a.mkv");
    var final = CompleteBenchmarkAssignment(store, "benchmark-selected-worker-1", second.AssignmentId!, workspace, "selected-b.mkv");
    AssertEqual(false, final.Benchmark.IsRunning, "selected benchmark completes after selected pass");
    AssertEqual("Complete", final.Benchmark.Status, "selected benchmark final status");
    AssertEqual(1, final.Benchmark.Passes.Count, "selected benchmark does not run skipped passes");
    AssertEqual(2, final.Benchmark.RecommendedWorkerCount ?? 0, "selected benchmark recommends selected passing count");

    AssertThrows<InvalidOperationException>(
        () => store.StartBenchmark(new BenchmarkStartRequest { ConcurrencyLevels = new List<int> { 4 } }),
        "selected benchmark rejects unavailable concurrency");
}

static void RunBenchmarkStartGuardCheck(string workspace)
{
    var emptyStore = CreateStore(Path.Combine(workspace, "empty"), instanceCount: 1);
    RegisterWorkers(emptyStore, "empty-benchmark-worker", 1);
    AssertThrows<InvalidOperationException>(
        () => emptyStore.StartBenchmark(),
        "benchmark requires source replay");

    var activeRunStore = CreateStore(Path.Combine(workspace, "active-run"), instanceCount: 1);
    using var files = CreateReplayFiles(1);
    activeRunStore.ImportFiles(files.Collection);
    RegisterWorkers(activeRunStore, "active-run-benchmark-worker", 1);
    activeRunStore.StartRun();
    AssertThrows<InvalidOperationException>(
        () => activeRunStore.StartBenchmark(),
        "benchmark cannot overlap active run");
}

static void RunAllConcurrentReplayFailuresCancelQueuedRunCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 3, maxConcurrentRecordings: 3);
    using var files = CreateReplayFiles(4);
    var imported = store.ImportFiles(files.Collection);

    var workerIds = Enumerable.Range(0, 3)
        .Select(index => store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "failing-worker-" + index,
            WorkerName = "Failing Worker " + index,
            PreferredInstanceIndex = index
        }).WorkerId)
        .ToArray();

    store.StartRun();
    var assignments = workerIds
        .Select(workerId => store.GetAssignment(workerId))
        .ToArray();

    AssertEqual(3, assignments.Count(assignment => assignment.HasAssignment), "all-failed active assignment count");

    ControlPanelState? state = null;
    for (var index = 0; index < assignments.Length; index++)
    {
        state = store.ReportAssignment(new WorkerReportRequest
        {
            WorkerId = workerIds[index],
            AssignmentId = assignments[index].AssignmentId!,
            Status = "Failed",
            Error = "Synthetic worker failure " + index
        });

        if (index < assignments.Length - 1)
        {
            AssertEqual(true, state.Run.IsRunning, "run remains active before every instance has failed");
            AssertEqual(false, state.Run.CancellationRequested, "no cancellation before every instance has failed");
        }
    }

    state ??= store.Snapshot();
    AssertEqual(false, state.Run.IsRunning, "all-failed run stops");
    AssertEqual(false, state.Run.CancellationRequested, "all-failed run cancellation finalizes");
    AssertEqual("Stopped with errors", state.Run.Status, "all-failed run status");
    AssertEqual(4, state.Run.FailedCount, "all-failed failed count includes queued replay");

    var queuedReplay = state.Queue.Single(item => item.Id == imported[3].Id);
    AssertEqual("Failed", queuedReplay.Status, "all-failed queued replay is failed");
    AssertContains("All active instances failed", queuedReplay.Error, "all-failed queued replay error");
}

static void RunWorkerProgressContractCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    var delayRequest = CreateSettingsUpdateRequest(store.Snapshot().Settings);
    delayRequest.DelayBetweenRecordingsSeconds = 5;
    store.UpdateSettings(delayRequest);
    using var files = CreateReplayFiles(2);
    store.ImportFiles(files.Collection);
    var workerId = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "progress-worker",
        WorkerName = "Progress Worker",
        PreferredInstanceIndex = 0
    }).WorkerId;

    store.StartRun();
    var firstAssignment = store.GetAssignment(workerId);
    AssertEqual(true, firstAssignment.HasAssignment, "progress first assignment exists");
    AssertEqual(2, firstAssignment.Progress.TotalCount, "assignment progress total count");
    AssertEqual(0, firstAssignment.Progress.CompletedCount, "assignment progress completed count");
    AssertEqual(0, firstAssignment.Progress.FailedCount, "assignment progress failed count");
    AssertEqual(true, firstAssignment.Progress.IsRunning, "assignment progress running");
    AssertEqual(5.0, firstAssignment.DelayBetweenRecordingsSeconds, "assignment delay between recordings");

    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workerId,
        AssignmentId = firstAssignment.AssignmentId!,
        Status = "Completed",
        OutputPath = Path.Combine(workspace, "first.mp4")
    });

    var progressHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Online"
    });
    AssertEqual(2, progressHeartbeat.Progress.TotalCount, "heartbeat progress total count");
    AssertEqual(1, progressHeartbeat.Progress.CompletedCount, "heartbeat progress completed count");
    AssertEqual(0, progressHeartbeat.Progress.FailedCount, "heartbeat progress failed count");
    AssertEqual(true, progressHeartbeat.Progress.IsRunning, "heartbeat progress still running");
    AssertEqual("Running", progressHeartbeat.Progress.Status, "heartbeat progress running status");

    var secondAssignment = store.GetAssignment(workerId);
    AssertEqual(true, secondAssignment.HasAssignment, "progress second assignment exists");
    AssertEqual(1, secondAssignment.Progress.CompletedCount, "second assignment sees completed progress");

    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workerId,
        AssignmentId = secondAssignment.AssignmentId!,
        Status = "Failed",
        Error = "Synthetic failure"
    });

    var finishedHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = workerId,
        Status = "Online"
    });
    AssertEqual(2, finishedHeartbeat.Progress.TotalCount, "finished progress total count");
    AssertEqual(1, finishedHeartbeat.Progress.CompletedCount, "finished progress completed count");
    AssertEqual(1, finishedHeartbeat.Progress.FailedCount, "finished progress failed count");
    AssertEqual(false, finishedHeartbeat.Progress.IsRunning, "finished progress not running");
    AssertEqual("Finished with errors", finishedHeartbeat.Progress.Status, "finished progress status");

    var emptyAssignment = store.GetAssignment(workerId);
    AssertEqual(false, emptyAssignment.HasAssignment, "finished progress no assignment");
    AssertEqual(1, emptyAssignment.Progress.CompletedCount, "empty assignment completed progress");
    AssertEqual(1, emptyAssignment.Progress.FailedCount, "empty assignment failed progress");
}

static void RunQueueEditingCheck(string workspace)
{
    var store = CreateStore(workspace);
    using var files = CreateReplayFiles(3);
    var imported = store.ImportFiles(files.Collection);
    AssertEqual(3, imported.Count, "initial imported replay count");

    var firstId = imported[0].Id;
    var editId = imported[1].Id;
    var edited = store.UpdateQueueItem(editId, new QueueItemUpdateRequest
    {
        SongName = "Edited Song",
        Mapper = "Edited Mapper",
        Difficulty = "Expert",
        EstimatedSeconds = 123.4
    });
    var editedItem = edited.Queue.First(item => item.Id == editId);
    AssertEqual("Edited Song", editedItem.SongName, "edited song name");
    AssertEqual("Edited Mapper", editedItem.Mapper, "edited mapper");
    AssertEqual("Expert", editedItem.Difficulty, "edited difficulty");
    AssertEqual(123.4, editedItem.EstimatedSeconds, "edited seconds");
    AssertEqual(true, editedItem.IsMetadataEdited, "metadata edit flag");

    var moved = store.MoveQueueItem(editId, -1);
    AssertEqual(editId, moved.Queue[0].Id, "moved replay id");
    AssertEqual(1, moved.Queue[0].SequenceNumber, "moved replay sequence");
    AssertEqual(2, moved.Queue.First(item => item.Id == firstId).SequenceNumber, "shifted replay sequence");

    using var extraFiles = CreateReplayFiles(1);
    var extraImported = store.ImportFiles(extraFiles.Collection);
    AssertEqual(1, extraImported.Count, "extra imported replay count");

    var afterReload = store.Snapshot();
    AssertEqual(editId, afterReload.Queue[0].Id, "manual queue order after reload");
    AssertEqual("Edited Song", afterReload.Queue[0].SongName, "manual song after reload");

    var removedPath = extraImported[0].Path;
    var afterRemove = store.RemoveQueueItem(extraImported[0].Id);
    AssertEqual(3, afterRemove.Queue.Count, "queue count after remove");
    AssertEqual(false, File.Exists(removedPath), "removed queue file");
}

static void RunReplayCalibrationCheck(string workspace)
{
    var store = CreateStore(workspace);
    using var files = CreateReplayFiles(1);
    var imported = store.ImportFiles(files.Collection);
    var replayId = imported[0].Id;

    var calibrated = store.UpdateReplayCalibration(replayId, new ReplayCalibrationRequest
    {
        Status = "Manual",
        SyncOffsetMilliseconds = -18.25,
        TrimStartSeconds = 0.625,
        Notes = "Operator nudge"
    });

    var replay = calibrated.Queue.Single(item => item.Id == replayId);
    AssertEqual("Manual", replay.Calibration.Status, "calibration status");
    AssertEqual(-18.25, replay.Calibration.SyncOffsetMilliseconds ?? 0, "calibration sync offset");
    AssertEqual(0.625, replay.Calibration.TrimStartSeconds ?? 0, "calibration trim");
    AssertEqual("Operator nudge", replay.Calibration.Notes, "calibration notes");
    AssertEqual("Manual", replay.SyncStatus, "calibration mirrors sync status");
    AssertEqual(-18.25, replay.SyncCorrectionMilliseconds ?? 0, "calibration mirrors sync offset");

    var reloaded = store.Snapshot();
    var reloadedReplay = reloaded.Queue.Single(item => item.Id == replayId);
    AssertEqual("Manual", reloadedReplay.Calibration.Status, "calibration survives snapshot");
    AssertEqual(true, reloaded.Events.Any(item => item.Tag == "Calibration"), "calibration event recorded");
}

static void RunDiskSpaceAndEventLogCheck(string workspace)
{
    var store = CreateStore(workspace);
    var snapshot = store.Snapshot();
    AssertEqual(true, snapshot.DiskSpace.TotalBytes > 0, "disk space total bytes");
    AssertEqual(true, snapshot.DiskSpace.AvailableFreeBytes > 0, "disk space free bytes");
    AssertEqual(true, !string.IsNullOrWhiteSpace(snapshot.DiskSpace.Summary), "disk space summary");

    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);
    snapshot = store.Snapshot();
    AssertEqual(true, snapshot.Events.Any(item => item.Tag == "Import"), "import event recorded");

    var launchFailure = typeof(ControlPanelStore).GetMethod(
                            "SetLaunchFailureNoLock",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(typeof(ControlPanelStore).FullName, "SetLaunchFailureNoLock");
    var fullError = "Launch failed line 1" + Environment.NewLine + "Launch failed line 2";
    launchFailure.Invoke(
        store,
        new object?[] { snapshot.Instances[0], "Launch failed line 1", fullError });
    snapshot = store.Snapshot();
    var launchEvent = snapshot.Events.First(item => item.Tag == "Launch");
    AssertContains("Launch failed line 1", launchEvent.Text, "full launch event first line");
    AssertContains("Launch failed line 2", launchEvent.Text, "full launch event second line");
}

static void RunQueueCoverArtCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    var launchDirectory = store.Snapshot().Instances[0].LaunchDirectory;
    var levelDirectory = Path.Combine(
        launchDirectory,
        "Beat Saber_Data",
        "CustomLevels",
        "ABCDEF123456 Sample Song");
    Directory.CreateDirectory(levelDirectory);
    var coverPath = Path.Combine(levelDirectory, "cover.png");
    File.WriteAllBytes(coverPath, new byte[] { 137, 80, 78, 71 });

    using var files = CreateReplayFiles(1);
    var imported = store.ImportFiles(files.Collection);
    AssertEqual(1, imported.Count, "cover art imported replay count");
    AssertEqual("Player", imported[0].PlayerName, "imported player name");
    AssertEqual(true, imported[0].CoverArtUrl.EndsWith("/cover", StringComparison.OrdinalIgnoreCase), "cover art url");
    AssertEqual(Path.GetFullPath(coverPath), Path.GetFullPath(store.GetQueueCoverPath(imported[0].Id)), "resolved cover art path");
}

static void RunQueueMapImportCheck(string workspace)
{
    var missingStore = CreateStore(
        Path.Combine(workspace, "missing"),
        instanceCount: 1,
        mapDownloader: new FakeBeatSaverMapDownloader(downloadsMap: false));
    using var missingFiles = CreateReplayFiles(1);
    var missingImported = missingStore.ImportFiles(missingFiles.Collection);
    AssertEqual("Missing", missingImported[0].MapStatus, "missing map status");
    AssertContains("no song folder", missingImported[0].MapStatusDetail, "missing map detail");
    AssertThrows<InvalidOperationException>(
        () => missingStore.StartRun(),
        "missing map start guard");

    using var mapZip = CreateMapZip();
    var uploaded = missingStore.ImportQueueMap(
        missingImported[0].Id,
        new FormFile(mapZip, 0, mapZip.Length, "file", "wip-song.zip"));
    var uploadedReplay = uploaded.Queue.Single(item => item.Id == missingImported[0].Id);
    AssertEqual("Found", uploadedReplay.MapStatus, "uploaded map status");
    AssertEqual(true, File.Exists(Path.Combine(uploadedReplay.MapInstallPath, "Info.dat")), "uploaded map info.dat");

    var downloadedStore = CreateStore(
        Path.Combine(workspace, "downloaded"),
        instanceCount: 1,
        mapDownloader: new FakeBeatSaverMapDownloader(downloadsMap: true));
    using var downloadedFiles = CreateReplayFiles(1);
    var downloaded = downloadedStore.ImportFiles(downloadedFiles.Collection);
    AssertEqual("Downloaded", downloaded[0].MapStatus, "downloaded map status");
    AssertEqual(true, File.Exists(Path.Combine(downloaded[0].MapInstallPath, "Info.dat")), "downloaded map info.dat");
}

static void RunBeatLeaderReferenceImportCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        beatLeaderReplayDownloader: new FakeBeatLeaderReplayDownloader());

    var imported = store.ImportReferencesAsync(new ReplayReferenceImportRequest
    {
        References = new List<string>
        {
            "https://replay.beatleader.xyz/?link=https%3A%2F%2Fcdn.replays.beatleader.xyz%2F9280912-76561198059961776-ExpertPlus-Standard-13400F5FB2FD19F52E8C7AC48815D12E72FA3B4A.bsor"
        }
    }, CancellationToken.None).GetAwaiter().GetResult();

    AssertEqual(1, imported.Count, "beatleader linked replay import count");
    var replay = imported[0];
    AssertEqual(ReplayProvider.BeatLeader, replay.Provider, "beatleader provider");
    AssertEqual(ReplayReferenceKind.BeatLeaderCdnBsorUrl, replay.ReferenceKind, "beatleader reference kind");
    AssertEqual("BSOR", replay.ReplayFormat, "beatleader replay format");
    AssertEqual("9280912", replay.ScoreId, "beatleader score id");
    AssertEqual("Song 1", replay.SongName, "beatleader song");
    AssertEqual("ExpertPlus", replay.Difficulty, "beatleader difficulty");
    AssertEqual("Standard", replay.Mode, "beatleader mode");
    AssertEqual("13400F5FB2FD19F52E8C7AC48815D12E72FA3B4A", replay.LevelHash, "beatleader hash");
    AssertEqual(true, File.Exists(replay.Path), "beatleader replay file exists");
    AssertEqual(true, File.Exists(replay.Path + ".metadata.json"), "beatleader sidecar exists");
}

static void RunBeatLeaderScoreUrlDownloaderCheck(string workspace)
{
    var queueDirectory = Path.Combine(workspace, "Queue");
    var handler = new FakeBeatLeaderScoreHttpMessageHandler();
    using var httpClient = new HttpClient(handler);
    var downloader = new BeatLeaderReplayDownloader(httpClient);
    var reference = new ReplayReferenceParser().Parse("https://beatleader.com/score/30643468");

    var download = downloader.DownloadAsync(
        reference,
        queueDirectory,
        fileName => Path.Combine(queueDirectory, fileName),
        CancellationToken.None).GetAwaiter().GetResult();

    AssertEqual(1, handler.ScoreLookupCount, "beatleader score lookup count");
    AssertEqual(1, handler.ReplayDownloadCount, "beatleader replay download count");
    AssertEqual(true, File.Exists(download.LocalPath), "beatleader score url downloaded file exists");
    AssertEqual(
        "30643468-76561199081029968-ExpertPlus-Standard-D790917A21934DC957352377B204E9C57D97D386.bsor",
        Path.GetFileName(download.LocalPath),
        "beatleader score url imported file name");
    AssertEqual("30643468", download.Metadata.ScoreId, "beatleader score url score id");
    AssertEqual("https://beatleader.com/score/30643468", download.Metadata.SourceUrl, "beatleader score url source");
    AssertEqual("Train of Thought", download.Metadata.SongName, "beatleader score url song");
    AssertEqual("ExpertPlus", download.Metadata.Difficulty, "beatleader score url difficulty");
    AssertEqual("D790917A21934DC957352377B204E9C57D97D386", download.Metadata.LevelHash, "beatleader score url hash");
}

static void RunScoreSaberReferenceImportCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        scoreSaberReplayDownloader: new FakeScoreSaberReplayDownloader());

    var imported = store.ImportReferencesAsync(new ReplayReferenceImportRequest
    {
        References = new List<string> { "https://scoresaber.com/api/v2/scores/88905556/replay" }
    }, CancellationToken.None).GetAwaiter().GetResult();

    AssertEqual(1, imported.Count, "scoresaber linked replay import count");
    var replay = imported[0];
    AssertEqual(ReplayProvider.ScoreSaber2, replay.Provider, "scoresaber provider");
    AssertEqual(ReplayReferenceKind.ScoreSaber2ScoreUrl, replay.ReferenceKind, "scoresaber reference kind");
    AssertEqual("ScoreSaber", replay.ReplayFormat, "scoresaber replay format");
    AssertEqual("88905556", replay.ScoreId, "scoresaber score id");
    AssertEqual("Theatore Creatore", replay.SongName, "scoresaber song");
    AssertEqual("_Expert_SoloStandard", replay.Difficulty, "scoresaber difficulty");
    AssertEqual("SoloStandard", replay.Mode, "scoresaber mode");
    AssertEqual("CC0290E6A16C57889CEF9EF4AF4FC463483497BB", replay.LevelHash, "scoresaber hash");
    AssertEqual(true, File.Exists(replay.Path), "scoresaber replay file exists");
    AssertEqual(true, File.Exists(replay.Path + ".metadata.json"), "scoresaber sidecar exists");
}

static void RunLocalScoreSaberImportMetadataEnrichmentCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        scoreSaberReplayDownloader: new FakeScoreSaberReplayDownloader(),
        mapDownloader: new FakeBeatSaverMapDownloader(downloadsMap: true, songLengthSeconds: 209.0));

    var collection = new FormFileCollection();
    var stream = new MemoryStream();
    stream.Write(Encoding.UTF8.GetBytes("ScoreSaber Replay "));
    stream.WriteByte(0x0D);
    stream.WriteByte(0x0A);
    stream.WriteByte(0x01);
    stream.WriteByte(0x02);
    stream.WriteByte(0x03);
    stream.Position = 0;

    collection.Add(
        new FormFile(
            stream,
            0,
            stream.Length,
            "files",
            "scoresaber-88905556-Matty-Song-_Expert_SoloStandard-CC0290E6A16C57889CEF9EF4AF4FC463483497BB.dat"));

    using var files = new ReplayFormFiles(collection, new List<MemoryStream> { stream });
    var imported = store.ImportFiles(files.Collection);
    AssertEqual(1, imported.Count, "local scoresaber import count");
    var replay = imported[0];
    AssertEqual("Matty", replay.PlayerName, "local scoresaber import player");
    AssertEqual(209.0, replay.EstimatedSeconds, "local scoresaber import duration");
    AssertEqual(true, File.Exists(replay.Path + ".metadata.json"), "local scoresaber import sidecar");
}

static void RunLocalScoreSaberLongPlayerIdCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        scoreSaberReplayDownloader: new FakeScoreSaberReplayDownloader());

    var collection = new FormFileCollection();
    var stream = new MemoryStream();
    stream.Write(Encoding.UTF8.GetBytes("ScoreSaber Replay "));
    stream.WriteByte(0x0D);
    stream.WriteByte(0x0A);
    stream.WriteByte(0x01);
    stream.WriteByte(0x02);
    stream.Position = 0;

    collection.Add(
        new FormFile(
            stream,
            0,
            stream.Length,
            "files",
            "76561198117409561-76561198117409561-Expert-Standard-CC0290E6A16C57889CEF9EF4AF4FC463497BB.dat"));
    using var files = new ReplayFormFiles(collection, new List<MemoryStream> { stream });
    var imported = store.ImportFiles(files.Collection);
    var importedReplay = imported[0];
    AssertEqual(1, imported.Count, "local scoresaber long player-id import count");
    AssertEqual("ResolvedSteamName", importedReplay.PlayerName, "local scoresaber long player-id resolved name");
    AssertEqual(true, File.Exists(importedReplay.Path + ".metadata.json"), "local scoresaber long player-id sidecar");
}

static void RunLocalScoreSaberFilenameFallbackCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        scoreSaberReplayDownloader: new FakeScoreSaberReplayDownloader());

    var collection = new FormFileCollection();
    var streamOne = new MemoryStream();
    streamOne.Write(Encoding.UTF8.GetBytes("ScoreSaber Replay "));
    streamOne.WriteByte(0x0D);
    streamOne.WriteByte(0x0A);
    streamOne.WriteByte(0x01);
    streamOne.WriteByte(0x02);
    streamOne.Position = 0;

    var streamTwo = new MemoryStream();
    streamTwo.Write(Encoding.UTF8.GetBytes("ScoreSaber Replay "));
    streamTwo.WriteByte(0x0D);
    streamTwo.WriteByte(0x0A);
    streamTwo.WriteByte(0x01);
    streamTwo.WriteByte(0x02);
    streamTwo.Position = 0;

    collection.Add(
        new FormFile(
            streamOne,
            0,
            streamOne.Length,
            "files",
            "scoresaber-12345678-Lunaticon-3-29-Expert-Standard-CC0290E6A16C57889CEF9EF4AF4FC463483497BB.dat"));
    collection.Add(
        new FormFile(
            streamTwo,
            0,
            streamTwo.Length,
            "files",
            "76561198117409561-99.9-2-10-Expert-Standard-CC0290E6A16C57889CEF9EF4AF4FC463483497BB.dat"));

    using var files = new ReplayFormFiles(collection, new List<MemoryStream> { streamOne, streamTwo });
    var imported = store.ImportFiles(files.Collection);
    AssertEqual(2, imported.Count, "local scoresaber filename fallback count");

    var lunaticon = imported.First(item => string.Equals(item.PlayerName, "Lunaticon", StringComparison.OrdinalIgnoreCase));

    var ninetyNine = imported.First(item => string.Equals(item.PlayerName, "99.9", StringComparison.OrdinalIgnoreCase));
}

static void RunScoreSaberRichTextPlayerNameFormattingCheck()
{
    var method = typeof(ScoreSaberReplayDownloader).GetMethod(
                     "StripRichTextTags",
                     System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                 ?? throw new MissingMethodException(typeof(ScoreSaberReplayDownloader).FullName, "StripRichTextTags");

    var cleaned = method.Invoke(null, new object?[] { "<color=#F96854>Bizzy825</color>" });
    AssertEqual("Bizzy825", cleaned, "scoresaber rich text player name");
}

static void RunMixedProviderReplayIntegrationCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 2,
        maxConcurrentRecordings: 2,
        scoreSaberReplayDownloader: new FakeScoreSaberReplayDownloader());

    using var files = CreateBeatLeaderAndScoreSaberReplayFiles();
    var imported = store.ImportFiles(files.Collection);
    AssertEqual(2, imported.Count, "mixed provider local import count");

    var importedReference = store.ImportReferencesAsync(
        new ReplayReferenceImportRequest
        {
            References = new List<string> { "https://scoresaber.com/api/v2/scores/88905556/replay" }
        },
        CancellationToken.None).GetAwaiter().GetResult();
    AssertEqual(1, importedReference.Count, "mixed provider linked import count");
    var settingsRequest = CreateSettingsUpdateRequest(store.Snapshot().Settings);
    // Safety settings are forced on, so these false values must be ignored by assignments.
    settingsRequest.DisableScoreSubmissions = false;
    settingsRequest.SuppressScoreSaberReplayUi = false;
    store.UpdateSettings(settingsRequest);

    var queue = store.Snapshot().Queue;
    AssertEqual(3, queue.Count, "mixed provider queue count");
    var providers = queue.Select(item => item.Provider).Distinct().OrderBy(value => value.ToString()).ToArray();
    AssertEqual(2, providers.Length, "mixed provider queue providers");
    AssertContains("BeatLeader", string.Join(",", providers), "mixed provider contains beatleader");
    AssertContains("ScoreSaber2", string.Join(",", providers), "mixed provider contains scoresaber");

    RegisterWorkers(store, "mixed-worker", 2);
    var workerIds = new[] { "mixed-worker-0", "mixed-worker-1" };
    SetGameProcessIds(store, 2100, 2101);
    store.StartRun();

    var workerAssignments = workerIds
        .Select(workerId => new { WorkerId = workerId, Assignment = store.GetAssignment(workerId) })
        .ToArray();

    AssertEqual(2, workerAssignments.Count(item => item.Assignment.HasAssignment), "mixed provider active assignment count");
    var assignedProviders = workerAssignments
        .Where(item => item.Assignment.HasAssignment)
        .Select(item => item.Assignment.Provider)
        .Distinct()
        .ToArray();
    AssertEqual(2, assignedProviders.Length, "mixed provider assignment provider diversity");
    AssertContains("BeatLeader", string.Join(",", assignedProviders), "mixed provider has beatleader assignment");
    AssertContains("ScoreSaber2", string.Join(",", assignedProviders), "mixed provider has scoresaber assignment");
    AssertEqual(
        true,
        workerAssignments
            .Where(item => item.Assignment.HasAssignment)
            .All(item => item.Assignment.DisableScoreSubmissions == true),
        "mixed provider disable score submissions forced true");
    AssertEqual(
        true,
        workerAssignments
            .Where(item => item.Assignment.HasAssignment)
            .All(item => item.Assignment.SuppressScoreSaberReplayUi == true),
        "mixed provider suppress scoresaber replay ui forced true");

    var assignedReferenceKinds = workerAssignments
        .Where(item => item.Assignment.HasAssignment)
        .Select(item => item.Assignment.ReferenceKind)
        .Distinct()
        .ToArray();
    AssertEqual(2, assignedReferenceKinds.Length, "mixed provider assignment reference kind diversity");
    AssertContains("LocalBsorFile", string.Join(",", assignedReferenceKinds), "mixed provider has beatleader local reference kind");
    AssertContains("LocalScoreSaberDatFile", string.Join(",", assignedReferenceKinds), "mixed provider has scoresaber local dat reference kind");

    var reportedReplayIds = new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in workerAssignments.Where(item => item.Assignment.HasAssignment))
    {
        store.ReportAssignment(new WorkerReportRequest
        {
            WorkerId = item.WorkerId,
            AssignmentId = item.Assignment.AssignmentId!,
            Status = "Completed",
            OutputPath = Path.Combine(workspace, item.Assignment.InstanceIndex!.Value.ToString("00") + "-recorded.mp4"),
            SyncStatus = "Corrected"
        });
        reportedReplayIds.Add(item.Assignment.ReplayId);
    }

    var additionalAssignments = workerIds
        .Select(workerId => new { WorkerId = workerId, Assignment = store.GetAssignment(workerId) })
        .Where(assignment => assignment.Assignment.HasAssignment &&
                             assignment.Assignment.ReplayId != null &&
                             !reportedReplayIds.Contains(assignment.Assignment.ReplayId))
        .ToArray();

    foreach (var assignment in additionalAssignments)
    {
        store.ReportAssignment(new WorkerReportRequest
        {
            WorkerId = assignment.WorkerId,
            AssignmentId = assignment.Assignment.AssignmentId!,
            Status = "Completed",
            OutputPath = Path.Combine(workspace, assignment.Assignment.InstanceIndex!.Value.ToString("00") + "-recorded.mp4"),
            SyncStatus = "Corrected"
        });
        reportedReplayIds.Add(assignment.Assignment.ReplayId);
    }

    var finalState = store.Snapshot();
    AssertEqual("Complete", finalState.Run.Status, "mixed provider run status");
    AssertEqual(3, finalState.Run.CompletedCount, "mixed provider completed count");
}

static void RunCompletedRecordingUriCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(1);
    store.ImportFiles(files.Collection);

    var worker = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });

    store.StartRun();
    var assignment = store.GetAssignment(worker.WorkerId);
    var outputPath = Path.Combine(workspace, "Recordings", "Instance 1", "finished.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, "recorded");

    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = worker.WorkerId,
        AssignmentId = assignment.AssignmentId!,
        Status = "Completed",
        OutputPath = outputPath
    });

    AssertEqual(new Uri(outputPath).AbsoluteUri, store.GetRecordedFileUri(assignment.ReplayId!), "recording file URI");
    AssertEqual(Path.GetFullPath(outputPath), store.GetRecordedFilePath(assignment.ReplayId!), "recording file path");

    var requeued = store.RequeueQueueItem(assignment.ReplayId!);
    var replay = requeued.Queue.Single(item => item.Id == assignment.ReplayId);
    AssertEqual("Queued", replay.Status, "requeued status");
    AssertEqual<string?>(null, replay.OutputPath, "requeued output path");
}

static void RunRenameCompletedQueueRecordingsCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        mapDownloader: new FakeBeatSaverMapDownloader(downloadsMap: true));
    using var files = CreateReplayFiles(1, distinctLevelHashes: true);
    var imported = store.ImportFiles(files.Collection);
    var replaySourcePath = imported[0].Path;
    var collection = store.SaveMapCollection(new SaveMapCollectionRequest
    {
        Name = "Rename Pack"
    });

    var worker = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });
    SetGameProcessIds(store, 4100);
    store.StartRun();
    var assignment = store.GetAssignment(worker.WorkerId);
    var originalOutputPath = Path.Combine(workspace, "Recordings", "recorded.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(originalOutputPath)!);
    File.WriteAllText(originalOutputPath, "recorded");
    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = worker.WorkerId,
        AssignmentId = assignment.AssignmentId!,
        Status = "Completed",
        OutputPath = originalOutputPath
    });

    AssertEqual(true, File.Exists(replaySourcePath), "rename recording replay source stays before rename");
    AssertEqual(true, File.Exists(originalOutputPath), "rename recording original output exists");

    var collectionBeforeRename = store.GetMapCollections().Single(item => item.Id == collection.Id);
    AssertEqual(
        Path.GetFullPath(originalOutputPath),
        Path.GetFullPath(collectionBeforeRename.Items.Single().CompletedOutputPath!),
        "rename recording collection starts with original output");

    var queuePreview = store.GetCompletedQueueRecordingNamePreview();
    AssertEqual("Song 0", queuePreview.SourceLabel, "rename recording queue preview source");
    AssertEqual("001 - Song 0 [ExpertPlus]", queuePreview.Examples["Default"], "rename recording default preview");
    AssertEqual("Song 0 - BeatSaver Artist - Player", queuePreview.Examples["SongArtistPlayer"], "rename recording artist player preview");

    var renamed = store.RenameCompletedQueueRecordings(new RecordingFileRenameRequest
    {
        Format = "SongArtistPlayer"
    });
    AssertEqual(1, renamed.RenamedCount, "rename recording queue count");
    AssertEqual(0, renamed.SkippedCount, "rename recording queue skipped count");

    var renamedReplay = renamed.State.Queue.Single(item => item.Id == assignment.ReplayId);
    var expectedOutputPath = Path.Combine(
        Path.GetDirectoryName(originalOutputPath)!,
        "Song 0 - BeatSaver Artist - Player" + Path.GetExtension(originalOutputPath));
    AssertEqual(Path.GetFullPath(expectedOutputPath), Path.GetFullPath(renamedReplay.OutputPath!), "rename recording output path");
    AssertEqual(Path.GetFullPath(replaySourcePath), Path.GetFullPath(renamedReplay.Path), "rename recording source replay path unchanged");
    AssertEqual(true, File.Exists(replaySourcePath), "rename recording source replay still exists");
    AssertEqual(false, File.Exists(originalOutputPath), "rename recording old output removed");
    AssertEqual(true, File.Exists(expectedOutputPath), "rename recording new output exists");
    AssertEqual(Path.GetFullPath(expectedOutputPath), store.GetRecordedFilePath(assignment.ReplayId!), "rename recording recorded file path");

    var collectionAfterRename = renamed.State.Collections.Single(item => item.Id == collection.Id);
    AssertEqual(
        Path.GetFullPath(expectedOutputPath),
        Path.GetFullPath(collectionAfterRename.Items.Single().CompletedOutputPath!),
        "rename recording updates collection output path");

    var defaultRenamed = store.RenameCompletedQueueRecordings(new RecordingFileRenameRequest
    {
        Format = "Default"
    });
    AssertEqual(1, defaultRenamed.RenamedCount, "rename recording default count");
    var defaultOutputPath = Path.Combine(
        Path.GetDirectoryName(originalOutputPath)!,
        "001 - Song 0 [ExpertPlus]" + Path.GetExtension(originalOutputPath));
    var defaultReplay = defaultRenamed.State.Queue.Single(item => item.Id == assignment.ReplayId);
    AssertEqual(Path.GetFullPath(defaultOutputPath), Path.GetFullPath(defaultReplay.OutputPath!), "rename recording default output path");
    AssertEqual(false, File.Exists(expectedOutputPath), "rename recording formatted output removed after default rename");
    AssertEqual(true, File.Exists(defaultOutputPath), "rename recording default output exists");

    var repeated = store.RenameCompletedQueueRecordings(new RecordingFileRenameRequest
    {
        Format = "Default"
    });
    AssertEqual(0, repeated.RenamedCount, "rename recording repeat count");
    AssertEqual(1, repeated.SkippedCount, "rename recording repeat skipped count");

    var collectionStore = CreateStore(
        Path.Combine(workspace, "collection-only"),
        instanceCount: 1,
        mapDownloader: new FakeBeatSaverMapDownloader(downloadsMap: true));
    using var collectionFiles = CreateReplayFiles(1, distinctLevelHashes: true);
    collectionStore.ImportFiles(collectionFiles.Collection);
    var selectedCollection = collectionStore.SaveMapCollection(new SaveMapCollectionRequest
    {
        Name = "Collection Rename Pack"
    });
    var collectionWorker = collectionStore.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "collection-worker-0",
        WorkerName = "Collection Worker 0",
        PreferredInstanceIndex = 0
    });
    SetGameProcessIds(collectionStore, 4200);
    collectionStore.StartRun();
    var collectionAssignment = collectionStore.GetAssignment(collectionWorker.WorkerId);
    var collectionOriginalOutputPath = Path.Combine(workspace, "collection-only", "Recordings", "collection-recorded.mkv");
    Directory.CreateDirectory(Path.GetDirectoryName(collectionOriginalOutputPath)!);
    File.WriteAllText(collectionOriginalOutputPath, "recorded");
    collectionStore.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = collectionWorker.WorkerId,
        AssignmentId = collectionAssignment.AssignmentId!,
        Status = "Completed",
        OutputPath = collectionOriginalOutputPath
    });
    collectionStore.ClearQueue();

    var collectionPreview = collectionStore.GetCollectionRecordingNamePreview(selectedCollection.Id);
    AssertEqual("Song 0", collectionPreview.SourceLabel, "rename collection preview source");
    AssertEqual("4fc4b", collectionPreview.Examples["Key"], "rename collection key preview");

    var collectionRenamed = collectionStore.RenameCollectionRecordings(
        selectedCollection.Id,
        new RecordingFileRenameRequest
        {
            Format = "Key"
        });
    AssertEqual(1, collectionRenamed.RenamedCount, "rename collection recording count");
    AssertEqual(0, collectionRenamed.SkippedCount, "rename collection recording skipped count");

    var collectionExpectedOutputPath = Path.Combine(
        Path.GetDirectoryName(collectionOriginalOutputPath)!,
        "4fc4b" + Path.GetExtension(collectionOriginalOutputPath));
    var updatedCollection = collectionRenamed.State.Collections.Single(item => item.Id == selectedCollection.Id);
    AssertEqual(false, File.Exists(collectionOriginalOutputPath), "rename collection old output removed");
    AssertEqual(true, File.Exists(collectionExpectedOutputPath), "rename collection new output exists");
    AssertEqual(
        Path.GetFullPath(collectionExpectedOutputPath),
        Path.GetFullPath(updatedCollection.Items.Single().CompletedOutputPath!),
        "rename collection updates collection output path");
}

static void RunRequeueAllQueueItemsCheck(string workspace)
{
    var store = CreateStore(workspace, instanceCount: 3);
    using var files = CreateReplayFiles(4);
    var imported = store.ImportFiles(files.Collection);
    var workers = Enumerable.Range(0, 3)
        .Select(index => store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = "worker-" + index,
            WorkerName = "Worker " + index,
            PreferredInstanceIndex = index
        }))
        .ToArray();

    store.StartRun();
    var assignments = workers
        .Select(worker => store.GetAssignment(worker.WorkerId))
        .ToArray();
    AssertEqual(3, assignments.Count(assignment => assignment.HasAssignment), "requeue all active assignment count");

    var completedOutputPath = Path.Combine(workspace, "Recordings", "completed.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(completedOutputPath)!);
    File.WriteAllText(completedOutputPath, "recorded");
    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workers[0].WorkerId,
        AssignmentId = assignments[0].AssignmentId!,
        Status = "Completed",
        OutputPath = completedOutputPath
    });
    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workers[1].WorkerId,
        AssignmentId = assignments[1].AssignmentId!,
        Status = "Failed",
        Error = "Map failed early."
    });

    var requeued = store.RequeueAllQueueItems();
    var completedReplay = requeued.Queue.Single(item => item.Id == imported[0].Id);
    var failedReplay = requeued.Queue.Single(item => item.Id == imported[1].Id);
    var activeReplay = requeued.Queue.Single(item => item.Id == imported[2].Id);
    var queuedReplay = requeued.Queue.Single(item => item.Id == imported[3].Id);

    AssertEqual("Queued", completedReplay.Status, "requeue all completed status");
    AssertEqual<string?>(null, completedReplay.OutputPath, "requeue all completed output path");
    AssertEqual("Queued", failedReplay.Status, "requeue all failed status");
    AssertEqual<string?>(null, failedReplay.Error, "requeue all failed error");
    AssertEqual("Assigned", activeReplay.Status, "requeue all leaves active status");
    AssertEqual(assignments[2].AssignmentId, activeReplay.AssignmentId, "requeue all leaves active assignment");
    AssertEqual("Queued", queuedReplay.Status, "requeue all leaves queued status");
    AssertEqual(0, requeued.Run.CompletedCount, "requeue all completed count");
    AssertEqual(0, requeued.Run.FailedCount, "requeue all failed count");
}

static void RunMapCollectionSaveLoadCheck(string workspace)
{
    var draftStore = CreateStore(
        Path.Combine(workspace, "draft"),
        instanceCount: 1,
        beatLeaderReplayDownloader: new FakeBeatLeaderReplayDownloader());
    var draft = draftStore.SaveMapCollection(new SaveMapCollectionRequest
    {
        Name = "Draft Pack",
        CreateEmpty = true
    });
    AssertEqual("Draft Pack", draft.Name, "empty collection name");
    AssertEqual(0, draft.Items.Count, "empty collection item count");
    using (var draftFiles = CreateReplayFiles(1, distinctLevelHashes: true))
    {
        var draftImport = draftStore.ImportFilesToMapCollection(draft.Id, draftFiles.Collection);
        AssertEqual(1, draftImport.ImportedCount, "collection direct import count");
        AssertEqual(1, draftImport.Collection.Items.Count, "collection direct import item count");
        AssertEqual(0, draftImport.State.Queue.Count, "collection direct import leaves queue alone");
    }
    var draftLinkImport = draftStore.ImportReferencesToMapCollectionAsync(
        draft.Id,
        new ReplayReferenceImportRequest
        {
            References = new List<string>
            {
                "https://beatleader.com/score/9280912"
            }
        },
        CancellationToken.None).GetAwaiter().GetResult();
    AssertEqual(1, draftLinkImport.ImportedCount, "collection link import count");
    AssertEqual(2, draftLinkImport.Collection.Items.Count, "collection link import item count");
    AssertEqual(0, draftLinkImport.State.Queue.Count, "collection link import leaves queue alone");

    var store = CreateStore(workspace, instanceCount: 1);
    using var files = CreateReplayFiles(2, distinctLevelHashes: true);
    var imported = store.ImportFiles(files.Collection);

    var collection = store.SaveMapCollection(new SaveMapCollectionRequest
    {
        Name = "Warmups"
    });
    AssertEqual("Warmups", collection.Name, "saved collection name");
    AssertEqual(2, collection.Items.Count, "saved collection item count");
    AssertEqual(true, collection.Items.All(item => File.Exists(item.Path)), "saved collection copies replay files");
    var removedCollectionItem = collection.Items[0];
    var removedCollectionItemPath = removedCollectionItem.Path;
    var removedCollectionSidecarPath = removedCollectionItemPath + ".metadata.json";
    AssertEqual(true, File.Exists(removedCollectionSidecarPath), "saved collection writes replay sidecar");
    var afterItemRemove = store.RemoveMapCollectionItem(collection.Id, removedCollectionItem.Id);
    AssertEqual(1, afterItemRemove.Items.Count, "collection item remove count");
    AssertEqual("Song 1", afterItemRemove.Items[0].SongName, "collection item remove keeps remaining replay");
    AssertEqual(1, afterItemRemove.Items[0].SequenceNumber, "collection item remove resequences remaining replay");
    AssertEqual(false, File.Exists(removedCollectionItemPath), "collection item remove deletes replay copy");
    AssertEqual(false, File.Exists(removedCollectionSidecarPath), "collection item remove deletes sidecar");
    AssertThrows<InvalidOperationException>(
        () => store.RemoveMapCollectionItem(collection.Id, removedCollectionItem.Id),
        "missing collection item remove");
    collection = afterItemRemove;

    AssertThrows<InvalidOperationException>(
        () => store.RenameMapCollection(collection.Id, new RenameMapCollectionRequest { Name = " " }),
        "blank collection rename");
    var renamed = store.RenameMapCollection(collection.Id, new RenameMapCollectionRequest
    {
        Name = "Warmups Renamed"
    });
    AssertEqual("Warmups Renamed", renamed.Name, "renamed collection name");
    AssertEqual(collection.Id, renamed.Id, "renamed collection id");
    AssertEqual(1, renamed.Items.Count, "renamed collection item count");
    AssertEqual(true, renamed.Items.All(item => File.Exists(item.Path)), "renamed collection keeps replay files");
    collection = renamed;

    var cleared = store.ClearQueue();
    AssertEqual(0, cleared.Queue.Count, "collection queue clear count");
    AssertEqual(1, cleared.Collections.Count, "collection survives queue clear");
    AssertEqual("Warmups Renamed", cleared.Collections[0].Name, "renamed collection survives queue clear");
    AssertEqual(true, collection.Items.All(item => File.Exists(item.Path)), "collection files survive queue clear");

    var loaded = store.LoadMapCollection(collection.Id, new LoadMapCollectionRequest());
    AssertEqual(1, loaded.LoadedCount, "collection load count");
    AssertEqual("Warmups Renamed", loaded.CollectionName, "loaded renamed collection name");
    AssertEqual(0, loaded.SkippedRecordedCount, "collection initial skipped count");
    AssertEqual(1, loaded.State.Queue.Count, "collection loaded queue count");
    AssertEqual("Song 1", loaded.State.Queue[0].SongName, "collection preserves remaining order");

    var worker = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });
    store.StartRun();
    var assignment = store.GetAssignment(worker.WorkerId);
    var outputPath = Path.Combine(workspace, "Recordings", "Instance 1", "already-recorded.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, "recorded");
    store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = worker.WorkerId,
        AssignmentId = assignment.AssignmentId!,
        Status = "Completed",
        OutputPath = outputPath
    });
    store.StopRun();
    var collectionAfterCompletion = store.GetMapCollections().Single(item => item.Id == collection.Id);
    var recordedCollectionItem = collectionAfterCompletion.Items.Single(item =>
        !string.IsNullOrWhiteSpace(item.CompletedOutputPath) &&
        string.Equals(
            Path.GetFullPath(item.CompletedOutputPath),
            Path.GetFullPath(outputPath),
            StringComparison.OrdinalIgnoreCase));
    AssertEqual(true, recordedCollectionItem.CompletedAtUtc.HasValue, "collection item records completed output");

    var skipped = store.LoadMapCollection(collection.Id, new LoadMapCollectionRequest());
    AssertEqual(1, skipped.SkippedRecordedCount, "collection skips recorded map");
    AssertEqual(0, skipped.LoadedCount, "collection keeps no unrecorded maps queued");
    var completedReplay = skipped.State.Queue.Single(item => item.Id == assignment.ReplayId);
    AssertEqual("Completed", completedReplay.Status, "collection skipped replay remains completed");
    AssertEqual(Path.GetFullPath(outputPath), Path.GetFullPath(completedReplay.OutputPath!), "collection skipped replay keeps output path");

    var overwritten = store.LoadMapCollection(collection.Id, new LoadMapCollectionRequest
    {
        OverwriteRecorded = true
    });
    var overwrittenReplay = overwritten.State.Queue.Single(item => item.Id == assignment.ReplayId);
    AssertEqual(0, overwritten.SkippedRecordedCount, "collection overwrite skipped count");
    AssertEqual(1, overwritten.RequeuedCount, "collection overwrite requeue count");
    AssertEqual("Queued", overwrittenReplay.Status, "collection overwrite requeues completed replay");
    AssertEqual<string?>(null, overwrittenReplay.OutputPath, "collection overwrite clears output path");
}

static void RunMapCollectionCardExportCheck(string workspace)
{
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        mapDownloader: new FakeBeatSaverMapDownloader(downloadsMap: true, songLengthSeconds: 60));
    var launchDirectory = store.Snapshot().Instances[0].LaunchDirectory;
    var levelDirectory = Path.Combine(
        launchDirectory,
        "Beat Saber_Data",
        "CustomLevels",
        "ABCDEF123456 Card Export");
    WriteMapCardTestMap(levelDirectory);

    using var files = CreateReplayFiles(1);
    var imported = store.ImportFiles(files.Collection);
    AssertEqual("Found", imported[0].MapStatus, "map card local map status");

    var collection = store.SaveMapCollection(new SaveMapCollectionRequest
    {
        Name = "Card Export"
    });
    var export = store.GetMapCardExport(collection.Id);
    AssertEqual("Card Export", export.CollectionName, "map card collection name");
    AssertEqual(1, export.Items.Count, "map card item count");

    var card = export.Items[0];
    AssertEqual("Song 0", card.SongName, "map card song name");
    AssertEqual("Local Artist", card.Artist, "map card artist");
    AssertEqual("Mapper 0", card.MapAuthor, "map card mapper");
    AssertEqual("Expert+", card.Difficulty, "map card difficulty");
    AssertEqual(120, card.NoteCount ?? 0, "map card note count");
    AssertEqual(2.0, Math.Round(card.NotesPerSecond.GetValueOrDefault(), 2), "map card nps");
    AssertEqual(142.0, card.BeatsPerMinute.GetValueOrDefault(), "map card bpm");
    AssertEqual(60.0, card.LengthSeconds, "map card length");
    AssertEqual("4fc4b", card.BeatSaverKey, "map card beatsaver key");
    AssertEqual("Ready", card.MetadataStatus, "map card metadata status");
    AssertEqual(true, card.CoverArtUrl.Contains("/api/collections/", StringComparison.OrdinalIgnoreCase), "map card cover url");
    AssertEqual(true, File.Exists(store.GetCollectionItemCoverPath(collection.Id, card.Id)), "map card cover path");

    var categorized = store.UpdateMapCardCategories(collection.Id, new UpdateMapCollectionCardCategoriesRequest
    {
        Items = new List<MapCollectionCardCategoryUpdate>
        {
            new MapCollectionCardCategoryUpdate
            {
                ItemId = card.Id,
                Category = "tech"
            }
        }
    });
    AssertEqual("tech", categorized.Items[0].Category, "map card saved category");
    AssertEqual("tech", store.GetMapCardExport(collection.Id).Items[0].Category, "map card category export persists in store");

    var reloaded = CreateStore(
        workspace,
        instanceCount: 1,
        mapDownloader: new FakeBeatSaverMapDownloader(downloadsMap: true, songLengthSeconds: 60));
    AssertEqual("tech", reloaded.GetMapCardExport(collection.Id).Items[0].Category, "map card category survives state reload");

    var normalized = reloaded.UpdateMapCardCategories(collection.Id, new UpdateMapCollectionCardCategoriesRequest
    {
        Items = new List<MapCollectionCardCategoryUpdate>
        {
            new MapCollectionCardCategoryUpdate
            {
                ItemId = card.Id,
                Category = "bogus"
            }
        }
    });
    AssertEqual("", normalized.Items[0].Category, "map card unknown category is not persisted");
}

static void RunCompletedRecordingAudioVerificationCheck(string workspace)
{
    var failedStore = CreateStore(
        Path.Combine(workspace, "missing-audio"),
        instanceCount: 1,
        audioMode: "ProcessLoopback",
        requireAudioForRun: true,
        recordingAudioVerifier: new FakeRecordingAudioVerifier(false, "Completed recording is missing an audio stream."));
    var failedReplay = CompleteSingleReplay(failedStore, Path.Combine(workspace, "missing-audio", "recording.mkv"));
    AssertEqual("Failed", failedReplay.Status, "missing audio completion status");
    AssertContains("Required audio verification failed", failedReplay.Error, "missing audio completion error");
    AssertContains("missing an audio stream", failedReplay.Error, "missing audio reason");

    var completedStore = CreateStore(
        Path.Combine(workspace, "audio-ok"),
        instanceCount: 1,
        audioMode: "ProcessLoopback",
        requireAudioForRun: true,
        recordingAudioVerifier: new FakeRecordingAudioVerifier(true, ""));
    var completedReplay = CompleteSingleReplay(completedStore, Path.Combine(workspace, "audio-ok", "recording.mkv"));
    AssertEqual("Completed", completedReplay.Status, "verified audio completion status");
    AssertEqual<string?>(null, completedReplay.Error, "verified audio completion error");
}

static void RunCompletedRecordingSyncVerificationCheck(string workspace)
{
    var missingSyncStore = CreateStore(
        Path.Combine(workspace, "missing-sync"),
        instanceCount: 1,
        audioMode: "ProcessLoopback",
        recordingAudioVerifier: new FakeRecordingAudioVerifier(true, ""));
    var missingSyncReplay = CompleteSingleReplay(
        missingSyncStore,
        Path.Combine(workspace, "missing-sync", "recording.mkv"),
        includeSyncMetadata: false);
    AssertEqual("Failed", missingSyncReplay.Status, "missing sync completion status");
    AssertContains("automatic sync marker", missingSyncReplay.Error, "missing sync completion error");

    var completedStore = CreateStore(
        Path.Combine(workspace, "sync-ok"),
        instanceCount: 1,
        audioMode: "ProcessLoopback",
        recordingAudioVerifier: new FakeRecordingAudioVerifier(true, ""));
    var completedReplay = CompleteSingleReplay(
        completedStore,
        Path.Combine(workspace, "sync-ok", "recording.mkv"),
        syncCorrectionMilliseconds: 18.5,
        trimStartSeconds: 0.8,
        syncReportPath: Path.Combine(workspace, "sync-ok", "recording.sync.json"));
    AssertEqual("Completed", completedReplay.Status, "verified sync completion status");
    AssertEqual("Corrected", completedReplay.SyncStatus, "verified sync status");
    AssertEqual(18.5, completedReplay.SyncCorrectionMilliseconds ?? 0, "verified sync correction");
    AssertEqual(0.8, completedReplay.TrimStartSeconds ?? 0, "verified sync trim");
    AssertEqual(true, completedReplay.SyncReportPath.EndsWith("recording.sync.json", StringComparison.OrdinalIgnoreCase), "verified sync report path");

    var failedStore = CreateStore(
        Path.Combine(workspace, "sync-failed-replay"),
        instanceCount: 1,
        audioMode: "ProcessLoopback",
        recordingAudioVerifier: new FakeRecordingAudioVerifier(true, ""));
    var failedReplay = CompleteSingleReplay(
        failedStore,
        Path.Combine(workspace, "sync-failed-replay", "recording.mkv"),
        reportStatus: "Failed",
        reportError: "Lag spike detected during replay recording.",
        syncCorrectionMilliseconds: -12.5,
        trimStartSeconds: 1.2,
        syncReportPath: Path.Combine(workspace, "sync-failed-replay", "recording.sync.json"));
    AssertEqual("Failed", failedReplay.Status, "failed replay status");
    AssertContains("Lag spike detected", failedReplay.Error, "failed replay error");
    AssertEqual("Corrected", failedReplay.SyncStatus, "failed replay sync status");
    AssertEqual(-12.5, failedReplay.SyncCorrectionMilliseconds ?? 0, "failed replay sync correction");
    AssertEqual(1.2, failedReplay.TrimStartSeconds ?? 0, "failed replay sync trim");
}

static void RunCompletedRecordingBookmarkChapterEmbeddingCheck(string workspace)
{
    var chapterEmbedder = new FakeRecordingChapterEmbedder();
    var store = CreateStore(
        workspace,
        instanceCount: 1,
        audioMode: "ProcessLoopback",
        recordingAudioVerifier: new FakeRecordingAudioVerifier(true, ""),
        recordingChapterEmbedder: chapterEmbedder);
    WriteBookmarkChapterTestMap(Path.Combine(workspace, "SharedSongs", "CustomLevels", "ABCDEF123456 Song 0 Mapper 0"));

    var replay = CompleteSingleReplay(
        store,
        Path.Combine(workspace, "recording.mkv"),
        syncCorrectionMilliseconds: 0,
        trimStartSeconds: 0,
        syncReportPath: Path.Combine(workspace, "recording.sync.json"));

    AssertEqual("Completed", replay.Status, "bookmark chapter replay status");
    AssertEqual<string?>(null, replay.Warning, "bookmark chapter warning");
    AssertEqual(1, chapterEmbedder.Requests.Count, "bookmark chapter embed request count");

    var request = chapterEmbedder.Requests[0];
    AssertEqual(Path.Combine(workspace, "recording.mkv"), request.RecordingPath, "bookmark chapter output path");
    AssertEqual(1, request.Chapters.Count, "bookmark chapter count");
    AssertEqual("Slow Drop", request.Chapters[0].Title, "bookmark chapter title");
    AssertEqual(60.0, Math.Round(request.Chapters[0].StartSeconds, 3), "bookmark chapter start");
}

static ReplayQueueRecord CompleteSingleReplay(
    ControlPanelStore store,
    string outputPath,
    bool includeSyncMetadata = true,
    double syncCorrectionMilliseconds = 0,
    double trimStartSeconds = 0,
    string? syncReportPath = null,
    string reportStatus = "Completed",
    string? reportError = null)
{
    using var files = CreateReplayFiles(1);
    var imported = store.ImportFiles(files.Collection);
    var worker = store.RegisterWorker(new WorkerRegisterRequest
    {
        WorkerId = "worker-0",
        WorkerName = "Worker 0",
        PreferredInstanceIndex = 0
    });

    SetGameProcessIds(store, 4100);
    store.StartRun();
    var assignment = store.GetAssignment(worker.WorkerId);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, "recorded");

    var report = new WorkerReportRequest
    {
        WorkerId = worker.WorkerId,
        AssignmentId = assignment.AssignmentId!,
        Status = reportStatus,
        OutputPath = outputPath,
        Error = reportError
    };
    if (includeSyncMetadata)
    {
        report.SyncStatus = "Corrected";
        report.SyncCorrectionMilliseconds = syncCorrectionMilliseconds;
        report.TrimStartSeconds = trimStartSeconds;
        report.SyncReportPath = syncReportPath ?? Path.ChangeExtension(outputPath, ".sync.json");
    }

    var state = store.ReportAssignment(report);

    return state.Queue.Single(item => item.Id == imported[0].Id);
}

static ControlPanelStore CreateStore(
    string workspace,
    string? beatSaberInstancesRoot = null,
    string beatSaberInstanceNamePrefix = "1.40.6 BSWC I-",
    int instanceCount = 3,
    int? maxConcurrentRecordings = null,
    string audioMode = "None",
    string captureEngine = "FFmpegDdagrab",
    bool requireAudioForRun = false,
    bool requireMatchingInstanceBaseline = false,
    IRecordingAudioVerifier? recordingAudioVerifier = null,
    IRecorderHostHealthChecker? recorderHostHealthChecker = null,
    IBeatSaverMapDownloader? mapDownloader = null,
    IWorkerPluginInstaller? workerPluginInstaller = null,
    IBeatLeaderReplayDownloader? beatLeaderReplayDownloader = null,
    IScoreSaberReplayDownloader? scoreSaberReplayDownloader = null,
    IRecordingChapterEmbedder? recordingChapterEmbedder = null,
    IDisplayInfoProvider? displayInfoProvider = null,
    ICapturePreflightRunner? capturePreflightRunner = null,
    IFfmpegSetupService? ffmpegSetupService = null,
    bool manageDisplayScale = false)
{
    return new ControlPanelStore(new ControlPanelSettings
    {
        WorkspaceDirectory = workspace,
        RecordingOutputDirectory = Path.Combine(workspace, "Recordings"),
        InstanceCount = instanceCount,
        MaxConcurrentRecordings = maxConcurrentRecordings ?? instanceCount,
        RequireAllWorkersReady = true,
        RequireMatchingInstanceBaseline = requireMatchingInstanceBaseline,
        TargetFps = 60,
        CaptureWidth = 3840,
        CaptureHeight = 2160,
        Encoder = "h264_nvenc",
        VideoBitrateKbps = 22000,
        OutputFormat = "mp4",
        MonitorIndex = 1,
        QualityMode = "Quality",
        CaptureEngine = captureEngine,
        AudioMode = audioMode,
        RequireAudioForRun = requireAudioForRun,
        AudioBitrateKbps = 192,
        AudioSampleRate = 48000,
        AudioChannels = 2,
        BeatSaberInstancesRoot = beatSaberInstancesRoot ?? Path.Combine(workspace, "BSInstances"),
        BeatSaberInstanceNamePrefix = beatSaberInstanceNamePrefix,
        BeatSaberLaunchPreset = "custom",
        BeatSaberLaunchArguments = "--no-yeet fpfc",
        ManageDisplayScale = manageDisplayScale
    }, recordingAudioVerifier,
        recorderHostHealthChecker ?? new FakeRecorderHostHealthChecker(true),
        mapDownloader ?? new FakeBeatSaverMapDownloader(downloadsMap: true),
        workerPluginInstaller,
        beatLeaderReplayDownloader,
        scoreSaberReplayDownloader,
        recordingChapterEmbedder,
        displayInfoProvider,
        capturePreflightRunner,
        ffmpegSetupService);
}

static void RegisterWorkers(ControlPanelStore store, string workerPrefix, int count = 3)
{
    for (var index = 0; index < count; index++)
    {
        store.RegisterWorker(new WorkerRegisterRequest
        {
            WorkerId = workerPrefix + "-" + index,
            WorkerName = workerPrefix + " " + index,
            PreferredInstanceIndex = index
        });
    }
}

static ControlPanelState CompleteBenchmarkAssignment(
    ControlPanelStore store,
    string workerId,
    string assignmentId,
    string workspace,
    string fileName)
{
    var outputPath = Path.Combine(workspace, "BenchmarkOutputs", fileName);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, "benchmark recording");
    return store.ReportAssignment(new WorkerReportRequest
    {
        WorkerId = workerId,
        AssignmentId = assignmentId,
        Status = "Completed",
        OutputPath = outputPath,
        SyncStatus = "Corrected",
        SyncReportPath = Path.ChangeExtension(outputPath, ".sync.json")
    });
}

static void SetAssignmentAssignedAtUtc(ControlPanelStore store, string assignmentId, DateTimeOffset assignedAtUtc)
{
    var field = typeof(ControlPanelStore).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ControlPanelStore).FullName, "_state");
    var state = (ControlPanelState?)field.GetValue(store)
                ?? throw new InvalidOperationException("ControlPanelStore state was null.");
    var replay = state.Queue.Single(item =>
        string.Equals(item.AssignmentId, assignmentId, StringComparison.OrdinalIgnoreCase));
    replay.AssignedAtUtc = assignedAtUtc;
}

static void SetAssignmentRecordingStartedAtUtc(ControlPanelStore store, string assignmentId, DateTimeOffset recordingStartedAtUtc)
{
    var field = typeof(ControlPanelStore).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ControlPanelStore).FullName, "_state");
    var state = (ControlPanelState?)field.GetValue(store)
                ?? throw new InvalidOperationException("ControlPanelStore state was null.");
    var replay = state.Queue.Single(item =>
        string.Equals(item.AssignmentId, assignmentId, StringComparison.OrdinalIgnoreCase));
    replay.RecordingStartedAtUtc = recordingStartedAtUtc;
}

static void SetBenchmarkAssignmentRecordingStartedAtUtc(
    ControlPanelStore store,
    string assignmentId,
    DateTimeOffset recordingStartedAtUtc)
{
    var field = typeof(ControlPanelStore).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ControlPanelStore).FullName, "_state");
    var state = (ControlPanelState?)field.GetValue(store)
                ?? throw new InvalidOperationException("ControlPanelStore state was null.");
    var assignment = state.Benchmark.Passes
        .SelectMany(pass => pass.Assignments)
        .Single(item => string.Equals(item.AssignmentId, assignmentId, StringComparison.OrdinalIgnoreCase));
    assignment.RecordingStartedAtUtc = recordingStartedAtUtc;
}

static void SetBenchmarkAssignmentFinalizingStartedAtUtc(
    ControlPanelStore store,
    string assignmentId,
    DateTimeOffset finalizingStartedAtUtc)
{
    var field = typeof(ControlPanelStore).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ControlPanelStore).FullName, "_state");
    var state = (ControlPanelState?)field.GetValue(store)
                ?? throw new InvalidOperationException("ControlPanelStore state was null.");
    var assignment = state.Benchmark.Passes
        .SelectMany(pass => pass.Assignments)
        .Single(item => string.Equals(item.AssignmentId, assignmentId, StringComparison.OrdinalIgnoreCase));
    assignment.FinalizingStartedAtUtc = finalizingStartedAtUtc;
}

static void SetLowFpsRecordingStartedAtUtc(ControlPanelStore store, string workerId, DateTimeOffset lowFpsRecordingStartedAtUtc)
{
    var field = typeof(ControlPanelStore).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ControlPanelStore).FullName, "_state");
    var state = (ControlPanelState?)field.GetValue(store)
                ?? throw new InvalidOperationException("ControlPanelStore state was null.");
    var instance = state.Instances.Single(item =>
        string.Equals(item.WorkerId, workerId, StringComparison.OrdinalIgnoreCase));
    instance.LowFpsRecordingStartedAtUtc = lowFpsRecordingStartedAtUtc;
}

static void SetGameProcessIds(ControlPanelStore store, params int[] processIds)
{
    var field = typeof(ControlPanelStore).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ControlPanelStore).FullName, "_state");
    var state = (ControlPanelState?)field.GetValue(store)
                ?? throw new InvalidOperationException("ControlPanelStore state was null.");
    for (var index = 0; index < processIds.Length; index++)
    {
        state.Instances[index].GameProcessId = processIds[index];
        state.Instances[index].GameLaunchStatus = "Failed";
    }
}

static void SetStartedGameProcessId(
    ControlPanelStore store,
    int instanceIndex,
    int processId,
    DateTimeOffset launchedAtUtc)
{
    var field = typeof(ControlPanelStore).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ControlPanelStore).FullName, "_state");
    var state = (ControlPanelState?)field.GetValue(store)
                ?? throw new InvalidOperationException("ControlPanelStore state was null.");
    state.Instances[instanceIndex].GameProcessId = processId;
    state.Instances[instanceIndex].GameLaunchedAtUtc = launchedAtUtc;
    state.Instances[instanceIndex].GameLaunchStatus = "Started";
}

static SettingsUpdateRequest CreateSettingsUpdateRequest(ControlPanelSettings settings)
{
    return new SettingsUpdateRequest
    {
        RecordingOutputDirectory = settings.RecordingOutputDirectory,
        FfmpegPath = settings.FfmpegPath,
        InstanceCount = settings.InstanceCount,
        MaxConcurrentRecordings = settings.MaxConcurrentRecordings,
        RequireAllWorkersReady = settings.RequireAllWorkersReady,
        RequireMatchingInstanceBaseline = settings.RequireMatchingInstanceBaseline,
        TargetFps = settings.TargetFps,
        CaptureWidth = settings.CaptureWidth,
        CaptureHeight = settings.CaptureHeight,
        Encoder = settings.Encoder,
        VideoBitrateKbps = settings.VideoBitrateKbps,
        OutputFormat = settings.OutputFormat,
        MonitorIndex = settings.MonitorIndex,
        QualityMode = settings.QualityMode,
        CaptureEngine = settings.CaptureEngine,
        AudioMode = settings.AudioMode,
        RequireAudioForRun = settings.RequireAudioForRun,
        AudioBitrateKbps = settings.AudioBitrateKbps,
        AudioSampleRate = settings.AudioSampleRate,
        AudioChannels = settings.AudioChannels,
        AudioLevelMode = settings.AudioLevelMode,
        AudioTargetLevelDb = settings.AudioTargetLevelDb,
        SharedCustomLevelsDirectory = settings.SharedCustomLevelsDirectory,
        SharedCustomWipLevelsDirectory = settings.SharedCustomWipLevelsDirectory,
        ShareCustomSabers = settings.ShareCustomSabers,
        SharedCustomSabersDirectory = settings.SharedCustomSabersDirectory,
        ShareCustomNotes = settings.ShareCustomNotes,
        SharedCustomNotesDirectory = settings.SharedCustomNotesDirectory,
        ShareCustomPlatforms = settings.ShareCustomPlatforms,
        SharedCustomPlatformsDirectory = settings.SharedCustomPlatformsDirectory,
        ShareCustomAvatars = settings.ShareCustomAvatars,
        SharedCustomAvatarsDirectory = settings.SharedCustomAvatarsDirectory,
        ShareCustomWalls = settings.ShareCustomWalls,
        SharedCustomWallsDirectory = settings.SharedCustomWallsDirectory,
        ShareCustomBombs = settings.ShareCustomBombs,
        SharedCustomBombsDirectory = settings.SharedCustomBombsDirectory,
        DisableScoreSubmissions = settings.DisableScoreSubmissions,
        SuppressScoreSaberReplayUi = settings.SuppressScoreSaberReplayUi,
        BeatSaberInstancesRoot = settings.BeatSaberInstancesRoot,
        SourceBeatSaberPath = settings.SourceBeatSaberPath,
        BeatSaberInstanceNamePrefix = settings.BeatSaberInstanceNamePrefix,
        BeatSaberLaunchPreset = settings.BeatSaberLaunchPreset,
        BeatSaberLaunchArguments = settings.BeatSaberLaunchArguments,
        ManageDisplayScale = settings.ManageDisplayScale,
        RecordingDisplayScalePercent = settings.RecordingDisplayScalePercent,
        RestoreDisplayScalePercent = settings.RestoreDisplayScalePercent,
        HideTaskbarDuringRun = settings.HideTaskbarDuringRun,
        DelayBetweenRecordingsSeconds = settings.DelayBetweenRecordingsSeconds,
        GamePresentation = new GamePresentationSettings
        {
            NoHud = settings.GamePresentation?.NoHud ?? true,
            LoadPlayerEnvironment = settings.GamePresentation?.LoadPlayerEnvironment ?? false,
            LoadPlayerJumpDistance = settings.GamePresentation?.LoadPlayerJumpDistance ?? false,
            OverrideReplayPlayerSettings = settings.GamePresentation?.OverrideReplayPlayerSettings ?? false,
            RestorePlayerSettingsOnExit = settings.GamePresentation?.RestorePlayerSettingsOnExit ?? false,
            IgnoreModifiers = settings.GamePresentation?.IgnoreModifiers ?? false,
            ShowHead = settings.GamePresentation?.ShowHead ?? false,
            ShowLeftSaber = settings.GamePresentation?.ShowLeftSaber ?? true,
            ShowRightSaber = settings.GamePresentation?.ShowRightSaber ?? true,
            ShowWatermark = settings.GamePresentation?.ShowWatermark ?? true,
            ShowTimelineMisses = settings.GamePresentation?.ShowTimelineMisses ?? true,
            ShowTimelineBombs = settings.GamePresentation?.ShowTimelineBombs ?? true,
            ShowTimelinePauses = settings.GamePresentation?.ShowTimelinePauses ?? true,
            SfxVolume = settings.GamePresentation?.SfxVolume ?? 0.3f,
            NoTextsAndHuds = settings.GamePresentation?.NoTextsAndHuds ?? true,
            AdvancedHud = settings.GamePresentation?.AdvancedHud ?? false,
            ReduceDebris = settings.GamePresentation?.ReduceDebris ?? true,
            NoFailEffects = settings.GamePresentation?.NoFailEffects ?? false,
            SaberTrailIntensity = settings.GamePresentation?.SaberTrailIntensity ?? 0f,
            NoteJumpDurationType = settings.GamePresentation?.NoteJumpDurationType ??
                                   GamePresentationSettings.NoteJumpDurationTypeDynamic,
            NoteJumpFixedDuration = settings.GamePresentation?.NoteJumpFixedDuration ?? 0.2f,
            NoteJumpStartBeatOffset = settings.GamePresentation?.NoteJumpStartBeatOffset ?? 0f,
            ApplyJdFixerSettings = settings.GamePresentation?.ApplyJdFixerSettings ?? false,
            JdFixerMode = settings.GamePresentation?.JdFixerMode ?? GamePresentationSettings.JdFixerModeReactionTime,
            JdFixerJumpDistance = settings.GamePresentation?.JdFixerJumpDistance ?? 18f,
            JdFixerReactionTime = settings.GamePresentation?.JdFixerReactionTime ?? 450f,
            HideNoteSpawnEffect = settings.GamePresentation?.HideNoteSpawnEffect ?? false,
            AdaptiveSfx = settings.GamePresentation?.AdaptiveSfx ?? true,
            ArcsHapticFeedback = settings.GamePresentation?.ArcsHapticFeedback ?? true,
            ArcVisibility = settings.GamePresentation?.ArcVisibility ?? GamePresentationSettings.ArcVisibilityLow,
            EnvironmentEffectsFilterDefaultPreset = settings.GamePresentation?.EnvironmentEffectsFilterDefaultPreset ??
                                                    GamePresentationSettings.EnvironmentEffectsAllEffects,
            EnvironmentEffectsFilterExpertPlusPreset = settings.GamePresentation?.EnvironmentEffectsFilterExpertPlusPreset ??
                                                       GamePresentationSettings.EnvironmentEffectsAllEffects,
            HeadsetHapticIntensity = settings.GamePresentation?.HeadsetHapticIntensity ?? 0.7f
        }
    };
}

static void CreateFakeBeatSaberInstance(string instancesRoot, string name, int index)
{
    var instanceDirectory = Path.Combine(instancesRoot, name);
    WriteFakeFile(instanceDirectory, "Beat Saber.exe", "game exe");
    WriteFakeFile(instanceDirectory, "IPA.exe", "ipa installer");
    WriteFakeFile(instanceDirectory, "winhttp.dll", "bsipa loader");
    WriteFakeFile(instanceDirectory, "Beat Saber_Data/Managed/IPA.Loader.dll", "ipa loader");
    WriteFakeFile(instanceDirectory, "Plugins/BeatLeader.dll", "beatleader");
    WriteFakeFile(instanceDirectory, "Plugins/BSAutoReplayRecorder.Plugin.dll", "recorder plugin");
    WriteFakeFile(instanceDirectory, "Libs/BSAutoReplayRecorder.Core.dll", "recorder core");
    WriteFakeFile(instanceDirectory, "UserData/BSAutoReplayRecorder/settings.json", CreateFakeRecorderSettings(index));
}

static void WriteFakeFile(string root, string relativePath, string contents)
{
    var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, contents);
}

static void AssertSettingsIniBackup(string instanceDirectory, string relativePath, string expectedContents, string label)
{
    var settingsPath = Path.Combine(instanceDirectory, relativePath);
    AssertEqual(expectedContents, File.ReadAllText(settingsPath), label + " original contents");
    var backupFiles = Directory
        .EnumerateFiles(
            Path.GetDirectoryName(settingsPath)!,
            Path.GetFileName(settingsPath) + ".*.bak")
        .ToList();
    AssertEqual(1, backupFiles.Count, label + " backup count");
    AssertEqual(expectedContents, File.ReadAllText(backupFiles[0]), label + " backup contents");
}

static bool IsReparsePoint(string path)
{
    return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

static void DeleteTempRoot(string path)
{
    if (!Directory.Exists(path))
    {
        return;
    }

    foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                 .OrderByDescending(item => item.Length))
    {
        if (IsReparsePoint(directory))
        {
            Directory.Delete(directory);
        }
    }

    Directory.Delete(path, recursive: true);
}

static string CreateFakeRecorderSettings(int index)
{
    return $$"""
    {
      "RecorderHost": {
        "BaseUrl": "http://127.0.0.1:{{5757 + index}}",
        "WindowTitle": "Beat Saber",
        "OutputDirectory": "Recordings/Instance {{index + 1}}",
        "AudioDeviceName": "Cable {{index + 1}}",
        "TargetProcessId": {{1000 + index}},
        "TimeoutSeconds": 10
      },
      "ControlPanelWorker": {
        "Enabled": true,
        "BaseUrl": "http://127.0.0.1:5770",
        "WorkerId": "worker-{{index}}",
        "WorkerName": "Worker {{index + 1}}",
        "PreferredInstanceIndex": {{index}},
        "PollIntervalSeconds": 1
      },
      "WindowPlacement": {
        "Enabled": true,
        "InstanceIndex": {{index}},
        "MonitorIndex": 1
      }
    }
    """;
}

static ReplayFormFiles CreateBeatLeaderAndScoreSaberReplayFiles()
{
    var collection = new FormFileCollection();
    var streams = new List<MemoryStream>();

    var beatLeaderStream = new MemoryStream();
    WriteSampleBsor(
        beatLeaderStream,
        "Song 1",
        "Mapper 1",
        "ExpertPlus",
        123456,
        60);
    beatLeaderStream.Position = 0;
    streams.Add(beatLeaderStream);
    collection.Add(new FormFile(beatLeaderStream, 0, beatLeaderStream.Length, "files", "01-mixed-beatleader.bsor"));

    var scoreSaberStream = new MemoryStream();
    scoreSaberStream.Write(Encoding.UTF8.GetBytes("ScoreSaber Replay "));
    scoreSaberStream.WriteByte(0x0D);
    scoreSaberStream.WriteByte(0x0A);
    scoreSaberStream.WriteByte(0x01);
    scoreSaberStream.WriteByte(0x02);
    scoreSaberStream.WriteByte(0x03);
    scoreSaberStream.Position = 0;
    streams.Add(scoreSaberStream);
    collection.Add(
        new FormFile(
            scoreSaberStream,
            0,
            scoreSaberStream.Length,
            "files",
            "03-scoresaber-player-song-SoloStandard-SoloStandard-CC0290E6A16C57889CEF9EF4AF4FC463483497BB.dat"));

    return new ReplayFormFiles(collection, streams);
}

static ReplayFormFiles CreateReplayFiles(int count, bool distinctLevelHashes = false)
{
    var collection = new FormFileCollection();
    var streams = new List<MemoryStream>();

    for (var index = 0; index < count; index++)
    {
        var stream = new MemoryStream();
        WriteSampleBsor(
            stream,
            "Song " + index,
            "Mapper " + index,
            "ExpertPlus",
            100000 + index,
            60 + index,
            distinctLevelHashes ? "ABCDEF123456" + index.ToString("X2") : "ABCDEF123456");
        stream.Position = 0;
        streams.Add(stream);
        collection.Add(new FormFile(stream, 0, stream.Length, "files", $"{index + 1:00}-sample.bsor"));
    }

    return new ReplayFormFiles(collection, streams);
}

static void WriteSampleBsor(
    Stream stream,
    string songName,
    string mapper,
    string difficulty,
    int score,
    float lastFrameTime,
    string levelHash = "ABCDEF123456")
{
    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

    writer.Write(0x442d3d69);
    writer.Write((byte)1);
    writer.Write((byte)0);

    WriteString(writer, "1.0.0");
    WriteString(writer, "1.40.6");
    WriteString(writer, "2026-06-02T00:00:00Z");
    WriteString(writer, "player-id");
    WriteString(writer, "Player");
    WriteString(writer, "Steam");
    WriteString(writer, "OpenVR");
    WriteString(writer, "Index");
    WriteString(writer, "Index Controllers");
    WriteString(writer, levelHash);
    WriteString(writer, songName);
    WriteString(writer, mapper);
    WriteString(writer, difficulty);
    writer.Write(score);
    WriteString(writer, "Standard");
    WriteString(writer, "DefaultEnvironment");
    WriteString(writer, "");
    writer.Write(18.5f);
    writer.Write(false);
    writer.Write(1.8f);
    writer.Write(0f);
    writer.Write(0f);
    writer.Write(1f);

    writer.Write((byte)1);
    writer.Write(3);
    WriteFrame(writer, 0f);
    WriteFrame(writer, lastFrameTime / 2f);
    WriteFrame(writer, lastFrameTime);
}

static void WriteFrame(BinaryWriter writer, float time)
{
    writer.Write(time);
    writer.Write(90);

    for (var index = 0; index < 21; index++)
    {
        writer.Write(0f);
    }
}

static void WriteMapCardTestMap(string levelDirectory)
{
    Directory.CreateDirectory(levelDirectory);
    File.WriteAllBytes(Path.Combine(levelDirectory, "cover.png"), new byte[] { 137, 80, 78, 71 });
    File.WriteAllText(
        Path.Combine(levelDirectory, "Info.dat"),
        """
        {
          "_songName": "Local Song",
          "_songAuthorName": "Local Artist",
          "_levelAuthorName": "Local Mapper",
          "_beatsPerMinute": 142,
          "_difficultyBeatmapSets": [
            {
              "_beatmapCharacteristicName": "Standard",
              "_difficultyBeatmaps": [
                {
                  "_difficulty": "ExpertPlus",
                  "_beatmapFilename": "ExpertPlusStandard.dat"
                }
              ]
            }
          ]
        }
        """);

    var notes = string.Join(
        ",",
        Enumerable.Range(0, 120).Select(index =>
            "{\"_time\":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"_lineIndex\":0,\"_lineLayer\":0,\"_type\":" + (index % 2) + ",\"_cutDirection\":0}"));
    File.WriteAllText(
        Path.Combine(levelDirectory, "ExpertPlusStandard.dat"),
        "{\"_version\":\"2.6.0\",\"_notes\":[" + notes + "]}");
}

static void WriteBookmarkChapterTestMap(string levelDirectory)
{
    Directory.CreateDirectory(levelDirectory);
    File.WriteAllText(
        Path.Combine(levelDirectory, "Info.dat"),
        """
        {
          "_songName": "Song 0",
          "_songAuthorName": "Artist",
          "_levelAuthorName": "Mapper 0",
          "_beatsPerMinute": 120,
          "_difficultyBeatmapSets": [
            {
              "_beatmapCharacteristicName": "Standard",
              "_difficultyBeatmaps": [
                {
                  "_difficulty": "ExpertPlus",
                  "_beatmapFilename": "ExpertPlusStandard.dat"
                }
              ]
            }
          ]
        }
        """);

    File.WriteAllText(
        Path.Combine(levelDirectory, "ExpertPlusStandard.dat"),
        """
        {
          "version": "3.3.0",
          "bpmEvents": [
            { "b": 0, "m": 120 },
            { "b": 60, "m": 60 }
          ],
          "customData": {
            "bookmarks": [
              { "b": 90, "n": "Slow Drop" }
            ],
            "bookmarksUseOfficialBpmEvents": true
          },
          "colorNotes": []
        }
        """);
}

static MemoryStream CreateMapZip()
{
    var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        var info = archive.CreateEntry("Info.dat");
        using (var writer = new StreamWriter(info.Open(), Encoding.UTF8))
        {
            writer.Write("{}");
        }

        var cover = archive.CreateEntry("cover.png");
        using (var writer = new BinaryWriter(cover.Open(), Encoding.UTF8))
        {
            writer.Write(new byte[] { 137, 80, 78, 71 });
        }
    }

    stream.Position = 0;
    return stream;
}

static void WriteString(BinaryWriter writer, string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    writer.Write(bytes.Length);
    writer.Write(bytes);
}

static void AssertQueuePlan(ControlPanelState state, IReadOnlyList<int> expectedPlan, string label)
{
    AssertEqual(expectedPlan.Count, state.Queue.Count, label + " queue count");
    for (var index = 0; index < expectedPlan.Count; index++)
    {
        AssertEqual(
            expectedPlan[index],
            state.Queue[index].AssignedInstance.GetValueOrDefault(-1),
            label + " item " + index);
    }
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(label + " failed. Expected " + expected + ", got " + actual + ".");
    }
}

static void AssertThrows<TException>(Action action, string label)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(label + " failed. Expected " + typeof(TException).Name + ".");
}

static void AssertContains(string expected, string? actual, string label)
{
    if (actual == null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(label + " failed. Expected to find '" + expected + "' in '" + actual + "'.");
    }
}

static void AssertDoesNotContain(string unexpected, string? actual, string label)
{
    if (actual != null && actual.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(label + " failed. Did not expect to find '" + unexpected + "' in '" + actual + "'.");
    }
}

static IReadOnlyList<string> WorkerPluginLegacyFieldNames()
{
    return new[]
    {
        "Settings" + "LockMode",
        "UseSession" + "Folders",
        "AutoStart" + "Batch",
        "RecordManualReplay" + "Starts"
    };
}

internal sealed class FakeRecordingAudioVerifier : IRecordingAudioVerifier
{
    private readonly bool _hasAudio;
    private readonly string _error;

    public FakeRecordingAudioVerifier(bool hasAudio, string error)
    {
        _hasAudio = hasAudio;
        _error = error;
    }

    public RecordingAudioVerificationResult Verify(string recordingPath)
    {
        return new RecordingAudioVerificationResult
        {
            HasAudio = _hasAudio,
            AudioStreams = _hasAudio ? 1 : 0,
            VideoStreams = 1,
            AudioCodecs = _hasAudio ? "aac" : "",
            Error = _error
        };
    }
}

internal sealed class FakeRecordingChapterEmbedder : IRecordingChapterEmbedder
{
    public List<FakeRecordingChapterEmbedRequest> Requests { get; } = new List<FakeRecordingChapterEmbedRequest>();

    public RecordingChapterEmbedResult Embed(string recordingPath, IReadOnlyList<RecordingChapter> chapters)
    {
        Requests.Add(new FakeRecordingChapterEmbedRequest
        {
            RecordingPath = recordingPath,
            Chapters = chapters.ToList()
        });
        return RecordingChapterEmbedResult.Success(chapters.Count);
    }
}

internal sealed class FakeRecordingChapterEmbedRequest
{
    public string RecordingPath { get; set; } = "";

    public List<RecordingChapter> Chapters { get; set; } = new List<RecordingChapter>();
}

internal sealed class FakeRecorderHostHealthChecker : IRecorderHostHealthChecker
{
    private readonly bool _healthy;
    private readonly bool _wgcSupported;
    private readonly bool _processLoopbackSupported;

    public FakeRecorderHostHealthChecker(
        bool healthy,
        bool wgcSupported = false,
        bool processLoopbackSupported = true)
    {
        _healthy = healthy;
        _wgcSupported = wgcSupported;
        _processLoopbackSupported = processLoopbackSupported;
    }

    public bool IsHealthy(string recorderHostUrl)
    {
        return _healthy;
    }

    public RecorderHostCapabilitiesSnapshot GetCapabilities(string recorderHostUrl)
    {
        var capabilities = RecorderHostCapabilitiesSnapshot.LegacyFallback("Fake recorder host capabilities.");
        capabilities.CaptureEngines["WindowsGraphicsCapture"] = new RecorderHostCapability
        {
            Supported = _wgcSupported,
            Status = _wgcSupported ? "WGC fake ready" : "WGC fake unavailable"
        };
        capabilities.AudioModes["ProcessLoopback"] = new RecorderHostCapability
        {
            Supported = _processLoopbackSupported,
            Status = _processLoopbackSupported ? "ProcessLoopback fake ready" : "ProcessLoopback fake unavailable"
        };
        return capabilities;
    }
}

internal sealed class FakeDisplayInfoProvider : IDisplayInfoProvider
{
    private readonly DisplayInfoSnapshot _snapshot;

    public FakeDisplayInfoProvider(DisplayInfoSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public DisplayInfoSnapshot GetDisplays()
    {
        return _snapshot;
    }
}

internal sealed class FakeCapturePreflightRunner : ICapturePreflightRunner
{
    private readonly CapturePreflightReport _report;

    public FakeCapturePreflightRunner(CapturePreflightReport report)
    {
        _report = report;
    }

    public int CheckCount { get; private set; }

    public IReadOnlyList<int> LastInstanceIndexes { get; private set; } = Array.Empty<int>();

    public CapturePreflightReport Check(
        ControlPanelSettings settings,
        DisplayInfoSnapshot displayInfo,
        IReadOnlyList<int> instanceIndexes)
    {
        CheckCount++;
        LastInstanceIndexes = instanceIndexes.ToArray();
        return _report;
    }
}

internal sealed class FakeFfmpegSetupService : IFfmpegSetupService
{
    private readonly FfmpegSetupReport _checkReport;

    public FakeFfmpegSetupService(FfmpegSetupReport checkReport, FfmpegSetupReport installedReport)
    {
        _checkReport = checkReport;
        InstalledReport = installedReport;
    }

    public FfmpegSetupReport InstalledReport { get; }

    public int CheckCount { get; private set; }

    public int InstallCount { get; private set; }

    public FfmpegSetupReport Check(string configuredPath)
    {
        CheckCount++;
        return _checkReport;
    }

    public FfmpegSetupReport Install(string configuredPath)
    {
        InstallCount++;
        return InstalledReport;
    }
}

internal sealed class FakeBeatSaverMapDownloader : IBeatSaverMapDownloader
{
    private readonly bool _downloadsMap;
    private readonly double? _songLengthSeconds;

    public FakeBeatSaverMapDownloader(bool downloadsMap, double? songLengthSeconds = null)
    {
        _downloadsMap = downloadsMap;
        _songLengthSeconds = songLengthSeconds;
    }

    public BeatSaverMapDownloadResult DownloadByHash(string levelHash, string targetRoot)
    {
        if (!_downloadsMap)
        {
            return new BeatSaverMapDownloadResult
            {
                NotFound = true,
                Detail = "BeatSaver does not have this map. It is probably WIP or unreleased."
            };
        }

        var target = Path.Combine(targetRoot, levelHash + " Sample Song");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Info.dat"), "{}");
        File.WriteAllBytes(Path.Combine(target, "cover.png"), new byte[] { 137, 80, 78, 71 });
        return new BeatSaverMapDownloadResult
        {
            Installed = true,
            InstallPath = target,
            Detail = "Downloaded from fake BeatSaver."
        };
    }

    public double? GetSongLengthSecondsByHash(string levelHash)
    {
        return _songLengthSeconds;
    }

    public BeatSaverMapCardMetadata? GetMapCardMetadataByHash(string levelHash, string difficulty, string mode)
    {
        if (!_downloadsMap)
        {
            return null;
        }

        return new BeatSaverMapCardMetadata
        {
            SongName = "BeatSaver Song",
            Artist = "BeatSaver Artist",
            MapAuthor = "BeatSaver Mapper",
            Difficulty = difficulty,
            Mode = mode,
            NotesPerSecond = 11.5,
            BeatsPerMinute = 180,
            NoteCount = 690,
            LengthSeconds = _songLengthSeconds,
            BeatSaverKey = "4fc4b",
            CoverArtUrl = "https://cdn.beatsaver.com/fake-cover.jpg"
        };
    }
}

internal sealed class FakeBeatLeaderScoreHttpMessageHandler : HttpMessageHandler
{
    public int ScoreLookupCount { get; private set; }

    public int ReplayDownloadCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.ToString() ?? "";
        if (string.Equals(uri, "https://api.beatleader.xyz/score/30643468", StringComparison.Ordinal))
        {
            ScoreLookupCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "id": 30643468,
                      "replay": "https://cdn.replays.beatleader.com/30643468-76561199081029968-ExpertPlus-Standard-D790917A21934DC957352377B204E9C57D97D386.bsor"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        }

        if (string.Equals(
                uri,
                "https://cdn.replays.beatleader.com/30643468-76561199081029968-ExpertPlus-Standard-D790917A21934DC957352377B204E9C57D97D386.bsor",
                StringComparison.Ordinal))
        {
            ReplayDownloadCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreateFakeBsor())
            });
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private static byte[] CreateFakeBsor()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x442d3d69);
            writer.Write((byte)1);
            writer.Write((byte)0);
            WriteString(writer, "1.0.0");
            WriteString(writer, "1.29.1");
            WriteString(writer, "2026-06-15T00:00:00Z");
            WriteString(writer, "76561199081029968");
            WriteString(writer, "thinking");
            WriteString(writer, "steam");
            WriteString(writer, "OpenVR");
            WriteString(writer, "Index");
            WriteString(writer, "Index Controllers");
            WriteString(writer, "D790917A21934DC957352377B204E9C57D97D386");
            WriteString(writer, "Train of Thought");
            WriteString(writer, "ZenithGD");
            WriteString(writer, "ExpertPlus");
            writer.Write(2373271);
            WriteString(writer, "Standard");
            WriteString(writer, "DefaultEnvironment");
            WriteString(writer, "");
            writer.Write(18.5f);
            writer.Write(false);
            writer.Write(1.8f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(1f);

            writer.Write((byte)1);
            writer.Write(3);
            WriteFrame(writer, 0f);
            WriteFrame(writer, 165f);
            WriteFrame(writer, 330f);
        }

        return stream.ToArray();
    }

    private static void WriteFrame(BinaryWriter writer, float time)
    {
        writer.Write(time);
        writer.Write(90);

        for (var index = 0; index < 21; index++)
        {
            writer.Write(0f);
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

internal sealed class FakeBeatLeaderReplayDownloader : IBeatLeaderReplayDownloader
{
    public Task<BeatLeaderReplayDownload> DownloadAsync(
        ReplayReference reference,
        string queueDirectory,
        Func<string, string> createImportPath,
        CancellationToken cancellationToken)
    {
        var targetPath = createImportPath(
            "9280912-76561198059961776-ExpertPlus-Standard-13400F5FB2FD19F52E8C7AC48815D12E72FA3B4A.bsor");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using (var stream = File.Create(targetPath))
        {
            WriteFakeBsor(stream);
        }

        return Task.FromResult(new BeatLeaderReplayDownload
        {
            LocalPath = targetPath,
            Metadata = new ReplayQueueSidecar
            {
                Provider = ReplayProvider.BeatLeader,
                ReferenceKind = ReplayReferenceKind.BeatLeaderCdnBsorUrl,
                ReplayFormat = "BSOR",
                SourceUrl = reference.OriginalValue,
                ScoreId = "9280912",
                PlayerName = "BeatLeader Player",
                PlayerId = "76561198059961776",
                SongName = "Song 1",
                Mapper = "Mapper 1",
                Difficulty = "ExpertPlus",
                Mode = "Standard",
                LevelHash = "13400F5FB2FD19F52E8C7AC48815D12E72FA3B4A",
                EstimatedSeconds = 60
            }
        });
    }

    private static void WriteFakeBsor(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x442d3d69);
        writer.Write((byte)1);
        writer.Write((byte)0);
        WriteString(writer, "1.0.0");
        WriteString(writer, "1.40.6");
        WriteString(writer, "2026-06-11");
        WriteString(writer, "76561198059961776");
        WriteString(writer, "BeatLeader Player");
        WriteString(writer, "steam");
        WriteString(writer, "OpenVR");
        WriteString(writer, "Index");
        WriteString(writer, "Index");
        WriteString(writer, "13400F5FB2FD19F52E8C7AC48815D12E72FA3B4A");
        WriteString(writer, "Song 1");
        WriteString(writer, "Mapper 1");
        WriteString(writer, "ExpertPlus");
        writer.Write(123456);
        WriteString(writer, "Standard");
        WriteString(writer, "DefaultEnvironment");
        WriteString(writer, "");
        writer.Write(18.0f);
        writer.Write(false);
        writer.Write(1.8f);
        writer.Write(0.0f);
        writer.Write(0.0f);
        writer.Write(1.0f);
        writer.Write((byte)1);
        writer.Write(0);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

internal sealed class FakeScoreSaberReplayDownloader : IScoreSaberReplayDownloader
{
    public Task<ScoreSaberReplayDownload> DownloadAsync(
        ReplayReference reference,
        string queueDirectory,
        Func<string, string> createImportPath,
        CancellationToken cancellationToken)
    {
        var targetPath = createImportPath(
            "scoresaber-88905556-Theatore Creatore-_Expert_SoloStandard-SoloStandard-CC0290E6A16C57889CEF9EF4AF4FC463483497BB.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(
            targetPath,
            Encoding.UTF8.GetBytes("ScoreSaber Replay ").Concat(new byte[] { 0x0D, 0x0A, 0x01, 0x02 }).ToArray());
        return Task.FromResult(new ScoreSaberReplayDownload
        {
            LocalPath = targetPath,
            Metadata = new ReplayQueueSidecar
            {
                Provider = ReplayProvider.ScoreSaber2,
                ReferenceKind = ReplayReferenceKind.ScoreSaber2ScoreUrl,
                ReplayFormat = "ScoreSaber",
                SourceUrl = reference.OriginalValue,
                ScoreId = "88905556",
                PlayerName = "Matty",
                SongName = "Theatore Creatore",
                Mapper = "Sachiko",
                Difficulty = "_Expert_SoloStandard",
                Mode = "SoloStandard",
                LevelHash = "CC0290E6A16C57889CEF9EF4AF4FC463483497BB",
                EstimatedSeconds = 154.84
            }
        });
    }

    public Task<ReplayQueueSidecar?> GetReplayMetadataByScoreIdAsync(
        string scoreId,
        CancellationToken cancellationToken)
    {
        if (scoreId == "88905556")
        {
            return Task.FromResult<ReplayQueueSidecar?>(new ReplayQueueSidecar
            {
                Provider = ReplayProvider.ScoreSaber2,
                ReferenceKind = ReplayReferenceKind.ScoreSaber2ScoreUrl,
                ReplayFormat = "ScoreSaber",
                SourceUrl = "https://scoresaber.com/api/v2/scores/88905556/replay",
                ScoreId = "88905556",
                PlayerName = "Matty",
                PlayerId = "76561198117409561",
                SongName = "Theatore Creatore",
                Mapper = "Sachiko",
                Difficulty = "_Expert_SoloStandard",
                Mode = "SoloStandard",
                LevelHash = "CC0290E6A16C57889CEF9EF4AF4FC463483497BB",
                EstimatedSeconds = 154.84
            });
        }

        return Task.FromResult<ReplayQueueSidecar?>(null);
    }

    public Task<string?> GetPlayerNameByPlayerIdAsync(string playerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Task.FromResult<string?>(null);
        }

        if (!string.Equals(playerId, "76561198117409561", StringComparison.Ordinal))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>("ResolvedSteamName");
    }
}

internal sealed class FakeWorkerPluginInstaller : IWorkerPluginInstaller
{
    public int InstallCount { get; private set; }
    public IReadOnlyList<int> LastContextIndexes { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<int> LastDeployTargetIndexes { get; private set; } = Array.Empty<int>();

    public void Install(
        IReadOnlyList<WorkerInstanceRecord> instances,
        ControlPanelSettings settings,
        IReadOnlyList<WorkerInstanceRecord>? deployTargets = null)
    {
        InstallCount++;
        var targets = deployTargets ?? instances;
        LastContextIndexes = instances.Select(instance => instance.Index).ToList();
        LastDeployTargetIndexes = targets.Select(instance => instance.Index).ToList();
        foreach (var instance in targets)
        {
            WriteFile(instance.LaunchDirectory, "Plugins/BSAutoReplayRecorder.Plugin.dll", "installed plugin");
            WriteFile(instance.LaunchDirectory, "Libs/BSAutoReplayRecorder.Core.dll", "installed core");
            WriteFile(
                instance.LaunchDirectory,
                "UserData/BSAutoReplayRecorder/settings.json",
                $$"""
                {
                  "RecorderHost": {
                    "BaseUrl": "http://127.0.0.1:{{5757 + instance.Index}}",
                    "OutputDirectory": "Recordings/Instance {{instance.Index + 1}}"
                  },
                  "ControlPanelWorker": {
                    "Enabled": true,
                    "BaseUrl": "{{settings.BindUrl}}",
                    "WorkerName": "Instance {{instance.Index + 1}}",
                    "PreferredInstanceIndex": {{instance.Index}}
                  },
                  "WindowPlacement": {
                    "Enabled": true,
                    "InstanceIndex": {{instance.Index}}
                  }
                }
                """);
        }
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}

internal sealed class ReplayFormFiles : IDisposable
{
    public ReplayFormFiles(FormFileCollection collection, List<MemoryStream> streams)
    {
        Collection = collection;
        Streams = streams;
    }

    public FormFileCollection Collection { get; }

    private List<MemoryStream> Streams { get; }

    public void Dispose()
    {
        foreach (var stream in Streams)
        {
            stream.Dispose();
        }
    }
}
