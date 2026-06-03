using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BeatLeader.Replayer;
using BSAutoReplayRecorder.Core;
using IPA;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

[Plugin(RuntimeOptions.SingleStartInit)]
public sealed class Plugin
{
    private readonly object _runtimeSync = new object();
    private Logger? _logger;
    private BatchRecorderSettings? _configuredSettings;
    private BatchRecorderSettings? _settings;
    private RecorderSessionContext? _session;
    private CancellationTokenSource? _shutdownCancellation;
    private ObsWebSocketRecorder? _obsRecorder;
    private ManualReplayRecorder? _manualReplayRecorder;
    private CompletedReplayStore? _completedReplayStore;
    private IReadOnlyList<ReplayQueueItem> _queueItems = Array.Empty<ReplayQueueItem>();
    private ReplayImportResult _lastImportResult = new ReplayImportResult(0, 0, 0, 0);
    private BatchRunControl? _batchRunControl;
    private Task? _batchTask;
    private bool _batchRunning;
    private string _runtimeStatus = "Starting";
    private string _setupStatus = "Starting";
    private string _setupDetail = "Loading recorder";

    [Init]
    public void Init(Logger logger)
    {
        _logger = logger;
        _logger.Info("Beat Saber Auto Replay Recorder initialized.");
    }

    [OnStart]
    public void OnStart()
    {
        if (_logger == null)
        {
            return;
        }

        RecordingStatusOverlay.EnsureCreated();
        RecordingStatusOverlay.SetControlPanelActions(new ControlPanelActions
        {
            RescanRequested = RequestRescan,
            StartBatchRequested = RequestStartBatch,
            StopAfterCurrentRequested = RequestStopAfterCurrent,
            ClearCompletedRequested = RequestClearCompleted,
            CheckSetupRequested = RequestSetupCheck,
            TestObsRequested = RequestTestObs,
            SwitchSessionRequested = RequestSwitchSession,
            OpenImportFolderRequested = () => OpenFolder(_session?.ImportInboxDirectory),
            OpenQueueFolderRequested = () => OpenFolder(_session?.QueueDirectory),
            OpenSessionFolderRequested = () => OpenFolder(_session?.SessionDirectory),
            OpenSettingsRequested = () => OpenFile(GamePaths.GetSettingsPath()),
            OpenLogsRequested = () => OpenFolder(Path.Combine(GamePaths.GetGameRoot(), "Logs"))
        });
        ReplayerLauncher.ReplayWasStartedEvent += HandleReplayWasStarted;
        ReplayerLauncher.ReplayWasFinishedEvent += HandleReplayWasFinished;

        _shutdownCancellation = new CancellationTokenSource();
        _configuredSettings = PluginSettingsStore.LoadOrCreate(_logger);
        ActivateSession(importReplays: _configuredSettings.AutoImportReplays);
        if (_settings == null)
        {
            return;
        }

        _obsRecorder = new ObsWebSocketRecorder(_settings.Obs, _logger);
        RecreateManualRecorder();

        _logger.Info("Manual replay recording is " +
                     (_settings.RecordManualReplayStarts ? "enabled" : "disabled") + ".");

        if (_settings.ShowControlPanelOnStart)
        {
            RecordingStatusOverlay.SetControlPanelVisible(true);
        }

        if (_settings.AutoStartBatch)
        {
            if (_queueItems.Count == 0)
            {
                _runtimeStatus = "No pending replays";
                UpdateControlPanel();
                StartBatch(_settings, _queueItems, _shutdownCancellation.Token);
            }
            else
            {
                StartBatch(_settings, _queueItems, _shutdownCancellation.Token);
            }
        }
        else
        {
            _logger.Info("Batch auto-start is disabled. Set AutoStartBatch=true in settings.json to run the queue.");
        }

        if (!_settings.AutoStartBatch || _queueItems.Count == 0)
        {
            RequestSetupCheck();
        }
    }

    [OnExit]
    public void OnExit()
    {
        _shutdownCancellation?.Cancel();
        _manualReplayRecorder?.Dispose();
        _obsRecorder?.Dispose();
        RecordingStatusOverlay.DestroyInstance();
        ReplayerLauncher.ReplayWasStartedEvent -= HandleReplayWasStarted;
        ReplayerLauncher.ReplayWasFinishedEvent -= HandleReplayWasFinished;
        _logger?.Info("Beat Saber Auto Replay Recorder shut down.");
    }

    private void ActivateSession(bool importReplays)
    {
        var logger = _logger;
        var configuredSettings = _configuredSettings;
        if (logger == null || configuredSettings == null)
        {
            return;
        }

        _session = RecorderSessionContext.Create(configuredSettings);
        _settings = _session.EffectiveSettings;
        _completedReplayStore = CompletedReplayStore.Load(_settings, logger);
        _lastImportResult = new ReplayImportResult(0, 0, 0, 0);

        logger.Info("Active recorder session: " + _session.SessionName);
        logger.Info("Replay import folder: " + _session.ImportInboxDirectory);
        logger.Info("Replay queue folder: " + _session.QueueDirectory);
        RescanQueue(importReplays);
    }

    private void RecreateManualRecorder()
    {
        var settings = _settings;
        var obsRecorder = _obsRecorder;
        var logger = _logger;
        var shutdownCancellation = _shutdownCancellation;
        if (settings == null || obsRecorder == null || logger == null || shutdownCancellation == null)
        {
            return;
        }

        _manualReplayRecorder?.Dispose();
        _manualReplayRecorder = new ManualReplayRecorder(
            settings,
            obsRecorder,
            logger,
            shutdownCancellation.Token);
    }

    private void StartBatch(
        BatchRecorderSettings settings,
        IReadOnlyList<ReplayQueueItem> queueItems,
        CancellationToken cancellationToken)
    {
        var logger = _logger;
        var obsRecorder = _obsRecorder;
        var completedReplayStore = _completedReplayStore;
        if (logger == null || obsRecorder == null)
        {
            return;
        }

        if (queueItems.Count == 0)
        {
            logger.Warn("Batch recorder has no replay items to run.");
            lock (_runtimeSync)
            {
                _runtimeStatus = "No pending replays";
            }

            UpdateControlPanel();
            RecordingStatusOverlay.ShowIdle("No pending replays", "Drop .bsor files into the active session import folder");
            return;
        }

        lock (_runtimeSync)
        {
            if (_batchRunning)
            {
                RecordingStatusOverlay.ShowToast(
                    "Batch already running",
                    "Use Stop After Current before starting another batch",
                    TimeSpan.FromSeconds(5));
                return;
            }

            _batchRunning = true;
            _batchRunControl = new BatchRunControl();
            _runtimeStatus = "Batch running";
            UpdateControlPanel();
        }

        var runControl = _batchRunControl;
        _batchTask = Task.Run(async () =>
        {
            try
            {
                var delay = TimeSpan.FromSeconds(Math.Max(0, settings.AutoStartDelaySeconds));
                if (delay > TimeSpan.Zero)
                {
                    logger.Info("Batch auto-start delay: " + delay.TotalSeconds + " second(s).");
                    RecordingStatusOverlay.ShowBatchCountdown(queueItems.Count, delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                var runner = new BatchRecordingRunner(
                    settings,
                    new BeatLeaderReplayPlaybackDriver(logger),
                    obsRecorder,
                    completedReplayStore,
                    runControl,
                    logger);

                await runner.RunAsync(queueItems, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.Debug("Batch recorder canceled.");
            }
            catch (Exception ex)
            {
                logger.Error("Batch recorder failed: " + ex);
                RecordingStatusOverlay.ShowToast(
                    "Batch recorder failed",
                    "Check the Beat Saber log",
                    TimeSpan.FromSeconds(10),
                    isError: true);
            }
            finally
            {
                lock (_runtimeSync)
                {
                    _batchRunning = false;
                    _batchRunControl = null;
                    _runtimeStatus = "Idle";
                }

                RescanQueue(importReplays: false);
            }
        }, cancellationToken);
    }

    private void RescanQueue(bool importReplays)
    {
        var logger = _logger;
        var settings = _settings;
        var session = _session;
        var completedReplayStore = _completedReplayStore;
        if (logger == null || settings == null || session == null || completedReplayStore == null)
        {
            return;
        }

        try
        {
            if (importReplays)
            {
                _lastImportResult = new ReplayImportManager(logger).Import(session);
                if (_lastImportResult.HasWork)
                {
                    RecordingStatusOverlay.ShowToast(
                        "Replay import complete",
                        _lastImportResult.ToSummary(),
                        TimeSpan.FromSeconds(7),
                        isError: _lastImportResult.Failed > 0);
                }
            }

            var replayFolder = GamePaths.ResolveGamePath(settings.ReplayInputDirectory);
            logger.Info("Replay input folder: " + replayFolder);
            var loadResult = new ReplayQueue().Load(new ReplayQueueOptions
            {
                InputDirectory = replayFolder,
                IncludeSubdirectories = settings.IncludeSubdirectories,
                SkipInvalidReplays = true
            });

            logger.Info("Detected " + loadResult.Items.Count + " BeatLeader .bsor replay(s).");
            if (loadResult.Failures.Count > 0)
            {
                logger.Warn("Skipped " + loadResult.Failures.Count + " invalid replay file(s).");
            }

            if (settings.SkipCompletedReplays)
            {
                var beforeCount = loadResult.Items.Count;
                var filteredItems = completedReplayStore.FilterCompleted(loadResult.Items);
                var skippedCount = beforeCount - filteredItems.Count;
                if (skippedCount > 0)
                {
                    logger.Info("Skipping " + skippedCount + " completed replay(s) from completed-replays state.");
                }

                loadResult = new ReplayQueueLoadResult(filteredItems, loadResult.Failures);
            }

            lock (_runtimeSync)
            {
                _queueItems = loadResult.Items;
                if (!_batchRunning)
                {
                    _runtimeStatus = _queueItems.Count == 0 ? "No pending replays" : "Ready";
                }
            }

            UpdateControlPanel();
        }
        catch (Exception ex)
        {
            logger.Error("Failed to scan BeatLeader replay folder: " + ex);
            RecordingStatusOverlay.ShowToast(
                "Rescan failed",
                "Check the Beat Saber log",
                TimeSpan.FromSeconds(8),
                isError: true);
        }
    }

    private void RequestRescan()
    {
        Task.Run(() => RescanQueue(importReplays: true));
    }

    private void RequestStartBatch()
    {
        var settings = _settings;
        var shutdownCancellation = _shutdownCancellation;
        if (settings == null || shutdownCancellation == null)
        {
            return;
        }

        IReadOnlyList<ReplayQueueItem> queueItems;
        lock (_runtimeSync)
        {
            queueItems = _queueItems;
        }

        StartBatch(settings, queueItems, shutdownCancellation.Token);
    }

    private void RequestStopAfterCurrent()
    {
        var runControl = _batchRunControl;
        if (runControl == null)
        {
            return;
        }

        runControl.RequestStopAfterCurrent();
        _runtimeStatus = "Will stop after current";
        UpdateControlPanel();
        RecordingStatusOverlay.ShowToast(
            "Stop requested",
            "Batch will stop after the current recording",
            TimeSpan.FromSeconds(6));
    }

    private void RequestClearCompleted()
    {
        if (_batchRunning)
        {
            RecordingStatusOverlay.ShowToast(
                "Cannot clear completed",
                "Wait until the batch is idle",
                TimeSpan.FromSeconds(5),
                isError: true);
            return;
        }

        _completedReplayStore?.Clear();
        RescanQueue(importReplays: false);
        RecordingStatusOverlay.ShowToast(
            "Completed state cleared",
            "The active session can be rerun",
            TimeSpan.FromSeconds(6));
    }

    private void RequestSwitchSession(string sessionName)
    {
        var logger = _logger;
        var configuredSettings = _configuredSettings;
        if (logger == null || configuredSettings == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionName))
        {
            RecordingStatusOverlay.ShowToast(
                "Session name required",
                "Enter a session name before switching",
                TimeSpan.FromSeconds(5),
                isError: true);
            return;
        }

        if (_batchRunning)
        {
            RecordingStatusOverlay.ShowToast(
                "Cannot switch session",
                "Wait until the batch is idle",
                TimeSpan.FromSeconds(5),
                isError: true);
            return;
        }

        configuredSettings.ActiveSessionName = sessionName.Trim();
        PluginSettingsStore.Save(configuredSettings);
        ActivateSession(importReplays: configuredSettings.AutoImportReplays);
        RecreateManualRecorder();
        RequestSetupCheck();

        RecordingStatusOverlay.ShowToast(
            "Session switched",
            "Active session: " + (_session?.SessionName ?? configuredSettings.ActiveSessionName),
            TimeSpan.FromSeconds(5));
    }

    private void RequestSetupCheck()
    {
        var obsRecorder = _obsRecorder;
        var shutdownCancellation = _shutdownCancellation;
        if (obsRecorder == null || shutdownCancellation == null)
        {
            return;
        }

        Task.Run(async () =>
        {
            SetSetupStatus("Checking setup", "Testing OBS and recorder folders");
            try
            {
                EnsureSessionFolders();
                var status = await obsRecorder.GetRecordingStatusAsync(shutdownCancellation.Token)
                    .ConfigureAwait(false);

                int queueCount;
                lock (_runtimeSync)
                {
                    queueCount = _queueItems.Count;
                }

                if (status.OutputActive)
                {
                    SetSetupStatus("OBS is already recording", "Stop OBS recording before starting a batch");
                    return;
                }

                SetSetupStatus(
                    queueCount > 0 ? "Ready to record" : "Ready, no replays queued",
                    "OBS connected, BeatLeader loaded, folders ready");
            }
            catch (OperationCanceledException)
            {
                SetSetupStatus("Setup check canceled", "");
            }
            catch (Exception ex)
            {
                SetSetupStatus("OBS not connected", ex.Message);
                _logger?.Warn("Setup check failed: " + ex.Message);
            }
        }, shutdownCancellation.Token);
    }

    private void RequestTestObs()
    {
        var obsRecorder = _obsRecorder;
        var settings = _settings;
        var shutdownCancellation = _shutdownCancellation;
        if (obsRecorder == null || settings == null || shutdownCancellation == null)
        {
            return;
        }

        if (_batchRunning)
        {
            RecordingStatusOverlay.ShowToast(
                "Cannot test OBS",
                "Wait until the batch is idle",
                TimeSpan.FromSeconds(5),
                isError: true);
            return;
        }

        Task.Run(async () =>
        {
            var item = new ReplayQueueItem(
                1,
                "obs-test",
                new BSAutoReplayRecorder.Core.Replay.BsorInfo
                {
                    SongName = "OBS Test",
                    Difficulty = "Setup"
                });
            var plan = new RecordingPlan(
                item,
                "auto-replay-recorder-obs-test",
                TimeSpan.Zero,
                TimeSpan.Zero);
            var recordingStarted = false;

            try
            {
                SetSetupStatus("Testing OBS", "Recording a 3 second test clip");
                var status = await obsRecorder.GetRecordingStatusAsync(shutdownCancellation.Token)
                    .ConfigureAwait(false);
                if (status.OutputActive)
                {
                    SetSetupStatus("OBS is already recording", "Stop OBS recording before testing");
                    RecordingStatusOverlay.ShowToast(
                        "OBS test skipped",
                        "OBS is already recording",
                        TimeSpan.FromSeconds(5),
                        isError: true);
                    return;
                }

                await obsRecorder.StartRecordingAsync(plan, shutdownCancellation.Token)
                    .ConfigureAwait(false);
                recordingStarted = true;
                await Task.Delay(TimeSpan.FromSeconds(3), shutdownCancellation.Token)
                    .ConfigureAwait(false);
                var stopResult = await obsRecorder.StopRecordingAsync(plan, CancellationToken.None)
                    .ConfigureAwait(false);

                SetSetupStatus("OBS test passed", "Output: " + (stopResult.OutputPath ?? "OBS output folder"));
                RecordingStatusOverlay.ShowToast(
                    "OBS test passed",
                    string.IsNullOrEmpty(stopResult.OutputPath) ? "Check OBS output folder" : stopResult.OutputPath,
                    TimeSpan.FromSeconds(8));
            }
            catch (OperationCanceledException)
            {
                SetSetupStatus("OBS test canceled", "");
            }
            catch (Exception ex)
            {
                SetSetupStatus("OBS test failed", ex.Message);
                RecordingStatusOverlay.ShowToast(
                    "OBS test failed",
                    "Check OBS websocket settings",
                    TimeSpan.FromSeconds(8),
                    isError: true);

                if (recordingStarted)
                {
                    try
                    {
                        await obsRecorder.StopRecordingAsync(plan, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception stopEx)
                    {
                        _logger?.Error("Failed to stop OBS after failed test recording: " + stopEx);
                    }
                }
            }
        }, shutdownCancellation.Token);
    }

    private void EnsureSessionFolders()
    {
        var session = _session;
        if (session == null)
        {
            throw new InvalidOperationException("No active session.");
        }

        Directory.CreateDirectory(session.SessionDirectory);
        Directory.CreateDirectory(session.ImportInboxDirectory);
        Directory.CreateDirectory(session.QueueDirectory);
        Directory.CreateDirectory(Path.Combine(session.SessionDirectory, "Recordings"));
    }

    private void SetSetupStatus(string status, string detail)
    {
        lock (_runtimeSync)
        {
            _setupStatus = status;
            _setupDetail = detail;
        }

        UpdateControlPanel();
    }

    private void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            StartWindowsProcess("explorer.exe", QuoteArgument(path));
        }
        catch (Exception ex)
        {
            _logger?.Warn("Could not open folder " + path + ": " + ex.Message);
            RecordingStatusOverlay.ShowToast(
                "Could not open folder",
                path,
                TimeSpan.FromSeconds(6),
                isError: true);
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(path))
            {
                File.WriteAllText(path, "");
            }

            StartWindowsProcess("notepad.exe", QuoteArgument(path));
        }
        catch (Exception ex)
        {
            _logger?.Warn("Could not open file " + path + ": " + ex.Message);
            RecordingStatusOverlay.ShowToast(
                "Could not open file",
                path,
                TimeSpan.FromSeconds(6),
                isError: true);
        }
    }

    private static void StartWindowsProcess(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "") + "\"";
    }

    private void UpdateControlPanel()
    {
        var session = _session;
        var settings = _settings;
        var completedReplayStore = _completedReplayStore;
        if (session == null || settings == null)
        {
            return;
        }

        int queueCount;
        bool batchRunning;
        string runtimeStatus;
        string setupStatus;
        string setupDetail;
        lock (_runtimeSync)
        {
            queueCount = _queueItems.Count;
            batchRunning = _batchRunning;
            runtimeStatus = _runtimeStatus;
            setupStatus = _setupStatus;
            setupDetail = _setupDetail;
        }

        RecordingStatusOverlay.SetControlPanelState(new ControlPanelState
        {
            SessionName = session.SessionName,
            SessionInput = session.SessionName,
            QueueCount = queueCount,
            CompletedCount = completedReplayStore?.Count ?? 0,
            ImportSummary = _lastImportResult.ToSummary(),
            ObsSummary = settings.Obs.WebSocketUri,
            RuntimeStatus = runtimeStatus,
            SetupStatus = setupStatus,
            SetupDetail = setupDetail,
            SettingsLockMode = settings.SettingsLockMode,
            ImportFolder = session.ImportInboxDirectory,
            QueueFolder = session.QueueDirectory,
            SessionFolder = session.SessionDirectory,
            SettingsPath = GamePaths.GetSettingsPath(),
            LogsFolder = Path.Combine(GamePaths.GetGameRoot(), "Logs"),
            CanStartBatch = !batchRunning && queueCount > 0,
            CanStopAfterCurrent = batchRunning,
            CanSwitchSession = !batchRunning,
            CanTestObs = !batchRunning
        });
    }

    private void StartObsProbe(ObsWebSocketRecorder obsRecorder, CancellationToken cancellationToken)
    {
        var logger = _logger;
        if (logger == null)
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var status = await obsRecorder.GetRecordingStatusAsync(cancellationToken).ConfigureAwait(false);
                logger.Info("OBS WebSocket connection OK. Recording active: " +
                            status.OutputActive + ", paused: " + status.OutputPaused + ".");
            }
            catch (OperationCanceledException)
            {
                logger.Debug("OBS WebSocket probe canceled.");
            }
            catch (Exception ex)
            {
                logger.Error("OBS WebSocket probe failed: " + ex);
            }
        }, cancellationToken);
    }

    private void HandleReplayWasStarted(ReplayLaunchData launchData)
    {
        _logger?.Info("BeatLeader replay lifecycle event: started.");
        _manualReplayRecorder?.HandleReplayStarted(launchData);
    }

    private void HandleReplayWasFinished(ReplayLaunchData launchData)
    {
        _logger?.Info("BeatLeader replay lifecycle event: finished.");
        _manualReplayRecorder?.HandleReplayFinished(launchData);
    }
}
