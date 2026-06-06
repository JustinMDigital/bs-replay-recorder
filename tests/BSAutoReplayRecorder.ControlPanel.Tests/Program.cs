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
    RunParallelAssignmentCheck(Path.Combine(tempRoot, "parallel"));
    RunFourInstanceAssignmentCheck(Path.Combine(tempRoot, "parallel-four"));
    RunImportedQueuePlanDistributionCheck(Path.Combine(tempRoot, "queue-plan-distribution"));
    RunEnabledInstanceQueuePlanDistributionCheck(Path.Combine(tempRoot, "enabled-queue-plan-distribution"));
    RunConfiguredInstanceAssignmentCheck(Path.Combine(tempRoot, "configured-instances"));
    RunActiveRunInstanceSettingsGuardCheck(Path.Combine(tempRoot, "active-run-settings-guard"));
    RunSingleReplayFailureDoesNotCancelOtherAssignmentsCheck(Path.Combine(tempRoot, "single-failure"));
    RunAllConcurrentReplayFailuresCancelQueuedRunCheck(Path.Combine(tempRoot, "all-concurrent-failed"));
    RunWorkerProgressContractCheck(Path.Combine(tempRoot, "worker-progress"));
    RunLaunchPlanCheck(Path.Combine(tempRoot, "launch-plan"));
    RunDisplayLabelFormattingCheck();
    RunDefaultLaunchArgumentsCheck(Path.Combine(tempRoot, "default-launch-args"));
    RunForceStopCheck(Path.Combine(tempRoot, "force-stop"));
    RunRecordingOutputDirectoryCheck(Path.Combine(tempRoot, "recording-output"));
    RunLocalSettingsFileCheck(Path.Combine(tempRoot, "local-settings-file"));
    RunLaunchPresetNormalizationCheck();
    RunAudioLevelNormalizationCheck();
    RunAudioLevelSettingsUpdateCheck(Path.Combine(tempRoot, "audio-level-update"));
    RunGamePresentationSettingsSyncCheck(Path.Combine(tempRoot, "game-presentation-sync"));
    RunRequireAudioGuardCheck(Path.Combine(tempRoot, "require-audio-guard"));
    RunProcessLoopbackAudioGuardCheck(Path.Combine(tempRoot, "process-loopback-audio-guard"));
    RunLaunchValidationCheck(Path.Combine(tempRoot, "launch-validation"));
    RunWorkerPluginSettingsIdentityCheck();
    RunManagedInstanceProvisioningCheck(Path.Combine(tempRoot, "managed-instance-provisioning"));
    RunInstanceBaselineCheck(Path.Combine(tempRoot, "instance-baseline"));
    RunSongFolderLinksCheck(Path.Combine(tempRoot, "song-folder-links"));
    RunQueueCoverArtCheck(Path.Combine(tempRoot, "queue-cover-art"));
    RunQueueMapImportCheck(Path.Combine(tempRoot, "queue-map-import"));
    RunQueueEditingCheck(Path.Combine(tempRoot, "queue-editing"));
    RunReplayCalibrationCheck(Path.Combine(tempRoot, "replay-calibration"));
    RunDiskSpaceAndEventLogCheck(Path.Combine(tempRoot, "disk-events"));
    RunCompletedRecordingUriCheck(Path.Combine(tempRoot, "recording-uri"));
    RunCompletedRecordingAudioVerificationCheck(Path.Combine(tempRoot, "recording-audio-verification"));
    RunCompletedRecordingSyncVerificationCheck(Path.Combine(tempRoot, "recording-sync-verification"));
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
    var store = CreateStore(workspace, instancesRoot, "Missing I-");

    var state = store.LaunchInstance(0);
    AssertEqual("Failed", state.Instances[0].GameLaunchStatus, "missing folder launch status");
    AssertContains("Instance folder was not found", state.Instances[0].GameLaunchError, "missing folder launch error");

    Directory.CreateDirectory(Path.Combine(instancesRoot, "Missing I-1"));
    state = store.LaunchInstance(0);
    AssertEqual("Failed", state.Instances[0].GameLaunchStatus, "missing exe launch status");
    AssertContains("Beat Saber.exe was not found", state.Instances[0].GameLaunchError, "missing exe launch error");
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
        AssertEqual(2, settings.InstanceCount, "local settings instance count");
        AssertEqual(2, settings.MaxConcurrentRecordings, "local settings max concurrent follows instance count");
    }
    finally
    {
        Environment.SetEnvironmentVariable("BSARR_SETTINGS_PATH", previousSettingsPath);
        Directory.SetCurrentDirectory(previousDirectory);
    }
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

static void RunForceStopCheck(string workspace)
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
    AssertEqual(true, assignment.HasAssignment, "force stop active assignment exists");

    var stopped = store.ForceStopAllGames();
    AssertEqual("Stopping", stopped.Run.Status, "force stop run status");
    AssertEqual(1, stopped.Run.ForceStopCommandId, "force stop command id");

    var activeHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = first.WorkerId,
        Status = "Recording",
        CurrentReplayId = assignment.ReplayId
    });
    AssertEqual(true, activeHeartbeat.ShouldCancelAssignment, "force stop cancels active assignment");
    AssertEqual(true, activeHeartbeat.ShouldOpenPauseMenu, "force stop opens active worker pause menu");

    var idleHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = second.WorkerId,
        Status = "Online"
    });
    AssertEqual(false, idleHeartbeat.ShouldCancelAssignment, "force stop does not cancel idle worker");
    AssertEqual(true, idleHeartbeat.ShouldOpenPauseMenu, "force stop opens idle worker pause menu");

    var repeatedHeartbeat = store.Heartbeat(new WorkerHeartbeatRequest
    {
        WorkerId = second.WorkerId,
        Status = "Online"
    });
    AssertEqual(false, repeatedHeartbeat.ShouldOpenPauseMenu, "force stop command is delivered once");
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
    AssertEqual(true, snapshot.Settings.ManageDisplayScale, "default display scale management");
    AssertEqual(true, snapshot.Settings.RequireMatchingInstanceBaseline, "default baseline guard");
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
        MonitorIndex = 1
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
        var pluginSettings = DotNetWorkerPluginInstaller.CreatePluginSettings(instance, settings, workerId);
        var json = JsonSerializer.Serialize(pluginSettings);
        var parsed = JsonSerializer.Deserialize<BatchRecorderSettings>(json)
                     ?? throw new InvalidOperationException("worker plugin settings identity deserialize failed");

        foreach (var legacyFieldName in WorkerPluginLegacyFieldNames())
        {
            AssertDoesNotContain(legacyFieldName, json, "worker settings omit legacy field " + legacyFieldName + " " + index);
        }

        AssertEqual(true, workerIds.Add(parsed.ControlPanelWorker.WorkerId), "worker id is unique " + index);
        AssertEqual("managed-worker-" + index.ToString("00"), parsed.ControlPanelWorker.WorkerId, "worker id " + index);
        AssertEqual("BSARR I-" + (index + 1), parsed.ControlPanelWorker.WorkerName, "worker name " + index);
        AssertEqual(index, parsed.ControlPanelWorker.PreferredInstanceIndex, "preferred instance index " + index);
        AssertEqual("http://127.0.0.1:" + (5757 + index), parsed.RecorderHost.BaseUrl, "recorder host port " + index);
        AssertEqual(300d, parsed.RecorderHost.TimeoutSeconds, "recorder host timeout " + index);
        AssertEqual(5.0, parsed.DelayBetweenRecordingsSeconds, "delay between recordings " + index);
        AssertEqual(index, parsed.WindowPlacement.InstanceIndex, "window placement index " + index);
    }
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
        BeatSaberLaunchArguments = ControlPanelSettings.DefaultBeatSaberLaunchArguments
    };
    migratedPresetSettings.Normalize();
    AssertEqual("4k-monitor-2x2", migratedPresetSettings.BeatSaberLaunchPreset, "saved 4k values restore 4k preset");

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
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed720pBeatSaberLaunchArguments
    };
    monitor1440pPresetSettings.Normalize();
    AssertEqual("1440p-monitor-2x2", monitor1440pPresetSettings.BeatSaberLaunchPreset, "saved 1440p values restore 1440p preset");

    var smallWindowSettings = new ControlPanelSettings
    {
        BeatSaberLaunchPreset = "windowed-720p",
        CaptureWidth = 1280,
        CaptureHeight = 720,
        BeatSaberLaunchArguments = ControlPanelSettings.Windowed720pBeatSaberLaunchArguments
    };
    smallWindowSettings.Normalize();
    AssertEqual("windowed-720p", smallWindowSettings.BeatSaberLaunchPreset, "720p launch preset");

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

    var instanceCountClampSettings = new ControlPanelSettings
    {
        InstanceCount = 12,
        MaxConcurrentRecordings = 12
    };
    instanceCountClampSettings.Normalize();
    AssertEqual(4, instanceCountClampSettings.InstanceCount, "instance count clamps to managed maximum");
    AssertEqual(4, instanceCountClampSettings.MaxConcurrentRecordings, "max concurrent follows managed instance count");
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
    AssertEqual(0.3f, worker.Settings.GamePresentation.SfxVolume, "registration SFX volume setting");

    var request = CreateSettingsUpdateRequest(initial.Settings);
    request.GamePresentation = new GamePresentationSettings
    {
        NoHud = false,
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
        HeadsetHapticIntensity = 0.65f
    };

    var updated = store.UpdateSettings(request);
    AssertEqual(false, updated.Settings.GamePresentation.NoHud, "updated no HUD setting");
    AssertEqual(false, updated.Settings.GamePresentation.ShowWatermark, "updated watermark setting");
    AssertEqual(0.45f, updated.Settings.GamePresentation.SfxVolume, "updated SFX volume setting");
    AssertEqual(false, updated.Settings.GamePresentation.NoTextsAndHuds, "updated no texts and HUDs setting");
    AssertEqual(GamePresentationSettings.ArcVisibilityStandard, updated.Settings.GamePresentation.ArcVisibility, "updated arc visibility setting");
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
    AssertEqual(false, heartbeat.GamePresentation.ShowWatermark, "heartbeat watermark setting");
    AssertEqual(0.45f, heartbeat.GamePresentation.SfxVolume, "heartbeat SFX volume setting");
    AssertEqual(false, heartbeat.GamePresentation.NoTextsAndHuds, "heartbeat no texts and HUDs setting");

    var snapshot = store.Snapshot();
    AssertEqual(1, snapshot.Instances[0].AppliedGamePresentationSettingsVersion, "worker reported applied game presentation version");
    AssertEqual("Applied", snapshot.Instances[0].GamePresentationSyncStatus, "worker game presentation sync status");

    var assignment = store.GetAssignment(worker.WorkerId);
    AssertEqual(2, assignment.GamePresentationSettingsVersion, "assignment game presentation version");
    AssertEqual(false, assignment.GamePresentation.NoHud, "assignment no HUD setting");
    AssertEqual(false, assignment.GamePresentation.ShowWatermark, "assignment watermark setting");
    AssertEqual(0.45f, assignment.GamePresentation.SfxVolume, "assignment SFX volume setting");
    AssertEqual(GamePresentationSettings.EnvironmentEffectsNoEffects, assignment.GamePresentation.EnvironmentEffectsFilterExpertPlusPreset, "assignment expert plus effects setting");

    var preserveRequest = CreateSettingsUpdateRequest(updated.Settings);
    preserveRequest.GamePresentation = null;
    preserveRequest.TargetFps = 72;
    var preserved = store.UpdateSettings(preserveRequest);
    AssertEqual(false, preserved.Settings.GamePresentation.NoHud, "omitted game presentation keeps no HUD setting");
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
    AssertEqual(3, assignments.Select(assignment => assignment.OutputDirectory).Distinct().Count(), "distinct output directory count");

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
    bool requireAudioForRun = false,
    bool requireMatchingInstanceBaseline = false,
    IRecordingAudioVerifier? recordingAudioVerifier = null,
    IRecorderHostHealthChecker? recorderHostHealthChecker = null,
    IBeatSaverMapDownloader? mapDownloader = null,
    IWorkerPluginInstaller? workerPluginInstaller = null)
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
        AudioMode = audioMode,
        RequireAudioForRun = requireAudioForRun,
        AudioBitrateKbps = 192,
        AudioSampleRate = 48000,
        AudioChannels = 2,
        BeatSaberInstancesRoot = beatSaberInstancesRoot ?? Path.Combine(workspace, "BSInstances"),
        BeatSaberInstanceNamePrefix = beatSaberInstanceNamePrefix,
        BeatSaberLaunchPreset = "custom",
        BeatSaberLaunchArguments = "--no-yeet fpfc"
    }, recordingAudioVerifier,
        recorderHostHealthChecker ?? new FakeRecorderHostHealthChecker(true),
        mapDownloader ?? new FakeBeatSaverMapDownloader(downloadsMap: true),
        workerPluginInstaller);
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

static SettingsUpdateRequest CreateSettingsUpdateRequest(ControlPanelSettings settings)
{
    return new SettingsUpdateRequest
    {
        RecordingOutputDirectory = settings.RecordingOutputDirectory,
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
        BeatSaberInstancesRoot = settings.BeatSaberInstancesRoot,
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

static ReplayFormFiles CreateReplayFiles(int count)
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
            60 + index);
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
    float lastFrameTime)
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
    WriteString(writer, "ABCDEF123456");
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

internal sealed class FakeRecorderHostHealthChecker : IRecorderHostHealthChecker
{
    private readonly bool _healthy;

    public FakeRecorderHostHealthChecker(bool healthy)
    {
        _healthy = healthy;
    }

    public bool IsHealthy(string recorderHostUrl)
    {
        return _healthy;
    }
}

internal sealed class FakeBeatSaverMapDownloader : IBeatSaverMapDownloader
{
    private readonly bool _downloadsMap;

    public FakeBeatSaverMapDownloader(bool downloadsMap)
    {
        _downloadsMap = downloadsMap;
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
}

internal sealed class FakeWorkerPluginInstaller : IWorkerPluginInstaller
{
    public int InstallCount { get; private set; }

    public void Install(IReadOnlyList<WorkerInstanceRecord> instances, ControlPanelSettings settings)
    {
        InstallCount++;
        foreach (var instance in instances)
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
                    "WorkerName": "BSARR I-{{instance.Index + 1}}",
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
