using System;
using System.Collections.Generic;
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
            ClearCompletedRequested = RequestClearCompleted
        });
        ReplayerLauncher.ReplayWasStartedEvent += HandleReplayWasStarted;
        ReplayerLauncher.ReplayWasFinishedEvent += HandleReplayWasFinished;

        _shutdownCancellation = new CancellationTokenSource();
        var configuredSettings = PluginSettingsStore.LoadOrCreate(_logger);
        _session = RecorderSessionContext.Create(configuredSettings);
        _settings = _session.EffectiveSettings;
        _logger.Info("Active recorder session: " + _session.SessionName);
        _logger.Info("Replay import folder: " + _session.ImportInboxDirectory);
        _logger.Info("Replay queue folder: " + _session.QueueDirectory);

        _obsRecorder = new ObsWebSocketRecorder(_settings.Obs, _logger);
        _completedReplayStore = CompletedReplayStore.Load(_settings, _logger);
        RescanQueue(importReplays: _settings.AutoImportReplays);

        _manualReplayRecorder = new ManualReplayRecorder(
            _settings,
            _obsRecorder,
            _logger,
            _shutdownCancellation.Token);

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
            StartObsProbe(_obsRecorder, _shutdownCancellation.Token);
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
        lock (_runtimeSync)
        {
            queueCount = _queueItems.Count;
            batchRunning = _batchRunning;
            runtimeStatus = _runtimeStatus;
        }

        RecordingStatusOverlay.SetControlPanelState(new ControlPanelState
        {
            SessionName = session.SessionName,
            QueueCount = queueCount,
            CompletedCount = completedReplayStore?.Count ?? 0,
            ImportSummary = _lastImportResult.ToSummary(),
            ObsSummary = settings.Obs.WebSocketUri,
            RuntimeStatus = runtimeStatus,
            ImportFolder = session.ImportInboxDirectory,
            CanStartBatch = !batchRunning && queueCount > 0,
            CanStopAfterCurrent = batchRunning
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
