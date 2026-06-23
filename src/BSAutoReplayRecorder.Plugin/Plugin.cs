using System;
using System.Threading;
using BSAutoReplayRecorder.Core;
using IPA;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

[Plugin(RuntimeOptions.SingleStartInit)]
public sealed class Plugin
{
    private Logger? _logger;
    private BatchRecorderSettings? _settings;
    private CancellationTokenSource? _shutdownCancellation;
    private ControlPanelWorkerRunner? _controlPanelWorker;

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

        _shutdownCancellation = new CancellationTokenSource();
        _settings = PluginSettingsStore.LoadOrCreate(_logger);

        RecordingStatusOverlay.EnsureCreated();
        GameFpsSampler.EnsureCreated();
        RecordingStatusOverlay.SetStatusPanelVisible(true);
        RecordingStatusOverlay.ShowIdle(
            "Worker starting",
            "Connecting to " + _settings.ControlPanelWorker.NormalizedBaseUrl);

        InstanceWindowPlacementController.EnsureCreated(_settings, _logger);
        StartControlPanelWorker(_settings, _shutdownCancellation.Token);
    }

    [OnExit]
    public void OnExit()
    {
        _shutdownCancellation?.Cancel();
        _controlPanelWorker?.Dispose();
        InstanceWindowPlacementController.DestroyInstance();
        GameFpsSampler.DestroyInstance();
        RecordingStatusOverlay.DestroyInstance();
        _logger?.Info("Beat Saber Auto Replay Recorder shut down.");
    }

    private void StartControlPanelWorker(BatchRecorderSettings settings, CancellationToken cancellationToken)
    {
        var logger = _logger;
        if (logger == null)
        {
            return;
        }

        _controlPanelWorker?.Dispose();
        _controlPanelWorker = new ControlPanelWorkerRunner(
            settings,
            logger,
            PersistControlPanelWorkerId,
            status => logger.Debug("Control panel worker status: " + status));
        _controlPanelWorker.Start(cancellationToken);

        logger.Info("Control panel worker mode enabled. Polling " +
                    settings.ControlPanelWorker.NormalizedBaseUrl + ".");
    }

    private void PersistControlPanelWorkerId(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || _settings == null)
        {
            return;
        }

        _settings.ControlPanelWorker.WorkerId = workerId;
        PluginSettingsStore.Save(_settings);
    }
}
