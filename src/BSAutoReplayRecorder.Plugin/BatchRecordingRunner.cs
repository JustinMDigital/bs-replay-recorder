using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Playback;
using IPA.Logging;
using IPA.Utilities.Async;

namespace BSAutoReplayRecorder.Plugin;

public sealed class BatchRecordingRunner
{
    private readonly BatchRecorderSettings _settings;
    private readonly IReadOnlyList<IReplayPlaybackDriver> _playbackDrivers;
    private readonly IRecordingBackend _recordingBackend;
    private readonly Func<string, CancellationToken, Task>? _phaseChangedAsync;
    private readonly ReplayReferenceParser _referenceParser = new ReplayReferenceParser();
    private readonly Logger _logger;

    public BatchRecordingRunner(
        BatchRecorderSettings settings,
        IReplayPlaybackDriver playbackDriver,
        IRecordingBackend recordingBackend,
        Logger logger,
        Func<string, CancellationToken, Task>? phaseChangedAsync = null)
        : this(settings, new[] { playbackDriver }, recordingBackend, logger, phaseChangedAsync)
    {
    }

    public BatchRecordingRunner(
        BatchRecorderSettings settings,
        IReadOnlyList<IReplayPlaybackDriver> playbackDrivers,
        IRecordingBackend recordingBackend,
        Logger logger,
        Func<string, CancellationToken, Task>? phaseChangedAsync = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _playbackDrivers = playbackDrivers ?? throw new ArgumentNullException(nameof(playbackDrivers));
        if (_playbackDrivers.Count == 0)
        {
            throw new ArgumentException("At least one replay playback driver is required.", nameof(playbackDrivers));
        }

        _recordingBackend = recordingBackend ?? throw new ArgumentNullException(nameof(recordingBackend));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _phaseChangedAsync = phaseChangedAsync;
    }

    public async Task<RecordingExecutionResult> RunSingleAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        ValidateSettings();

        if (_settings.RequirePreflightReplayValidation)
        {
            await RefreshSongsBeforePreflightAsync(cancellationToken).ConfigureAwait(false);
            await RunPreflightAsync(new[] { plan }, cancellationToken).ConfigureAwait(false);
        }

        var initialRecordingStatus = await _recordingBackend.GetRecordingStatusAsync(cancellationToken).ConfigureAwait(false);
        if (initialRecordingStatus.OutputActive)
        {
            throw new InvalidOperationException("Recorder backend is already recording. Stop recording before starting an assignment.");
        }

        return await RunPlanAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunPreflightAsync(
        IReadOnlyList<RecordingPlan> plans,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var replayReference = _referenceParser.Parse(plan.QueueItem.ReplayPath);
                var playbackDriver = SelectPlaybackDriver(replayReference);
                await ValidateReplayOnMainThreadAsync(playbackDriver, replayReference, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(plan.OutputBaseName + ": " + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        if (failures.Count == 0)
        {
            _logger.Info("Preflight replay validation passed for " + plans.Count + " plan(s).");
            return;
        }

        foreach (var failure in failures)
        {
            _logger.Error("Preflight replay validation failed: " + failure);
        }

        throw new InvalidOperationException(
            "Preflight replay validation failed for " + failures.Count + " replay(s). See the Beat Saber log for details.");
    }

    private Task RefreshSongsBeforePreflightAsync(CancellationToken cancellationToken)
    {
        if (!_settings.RefreshSongCoreBeforeReplayValidation)
        {
            return Task.CompletedTask;
        }

        var timeoutSeconds = _settings.SongCoreRefreshTimeoutSeconds;
        if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds < 1)
        {
            timeoutSeconds = 45;
        }

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return SongCoreRefreshCoordinator.RefreshAllSongsAsync(timeout, _logger, cancellationToken);
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.RecordingOutputDirectory))
        {
            throw new InvalidOperationException("RecordingOutputDirectory is required.");
        }
    }

    private async Task<RecordingExecutionResult> RunPlanAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        string? outputPath = null;
        var recordingStarted = false;
        var replayLaunchStarted = false;
        DateTimeOffset? contentStartUtc = null;
        RecordingStopResult? stopResult = null;
        string? warning = null;
        Exception? failure = null;

        try
        {
            _logger.Info("Starting assignment plan: " + plan.OutputBaseName);
            var replayReference = _referenceParser.Parse(plan.QueueItem.ReplayPath);
            var playbackDriver = SelectPlaybackDriver(replayReference);
            await ValidateReplayOnMainThreadAsync(playbackDriver, replayReference, cancellationToken).ConfigureAwait(false);

            using (var startWait = playbackDriver.CreateStartWait())
            using (var finishWait = playbackDriver.CreateFinishWait())
            using (var lagSpikeMonitor = await CreateLagSpikeMonitorOnMainThreadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                await StartRecordingWithRetriesAsync(plan, cancellationToken).ConfigureAwait(false);
                recordingStarted = await ConfirmRecordingStartedAsync(plan, cancellationToken).ConfigureAwait(false);
                if (!recordingStarted)
                {
                    throw new InvalidOperationException("Recorder accepted start, but status never reported an active recording.");
                }

                await PlaySyncMarkerOnMainThreadAsync(cancellationToken).ConfigureAwait(false);
                await DelayIfNeededAsync(plan.PreRoll, cancellationToken).ConfigureAwait(false);

                var launchRequestedUtc = DateTimeOffset.UtcNow;
                contentStartUtc = launchRequestedUtc;
                replayLaunchStarted = true;
                var launchTask = StartReplayOnMainThreadAsync(playbackDriver, plan.QueueItem, replayReference, cancellationToken);
                var startSignalTask = startWait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                var completedStartTask = await Task.WhenAny(launchTask, startSignalTask).ConfigureAwait(false);
                if (completedStartTask == launchTask)
                {
                    await launchTask.ConfigureAwait(false);
                    if (!startSignalTask.IsCompleted)
                    {
                        _logger.Warn(playbackDriver.DriverName + " replay start event was not observed before launch returned for " +
                                     plan.OutputBaseName + "; using replay launch time for content trim.");
                    }
                    else
                    {
                        contentStartUtc = await startSignalTask.ConfigureAwait(false) ?? launchRequestedUtc;
                    }
                }
                else
                {
                    contentStartUtc = await startSignalTask.ConfigureAwait(false) ?? launchRequestedUtc;
                    if (contentStartUtc == launchRequestedUtc)
                    {
                        _logger.Warn(playbackDriver.DriverName + " replay start event was not observed within the timeout for " +
                                     plan.OutputBaseName + "; using replay launch time for content trim.");
                    }
                }

                lagSpikeMonitor.StartMonitoring();
                var replayFinishTask = finishWait.WaitAsync(CreateReplayFinishTimeout(plan), cancellationToken);
                var lagSpikeTask = lagSpikeMonitor.WaitForLagSpikeAsync(cancellationToken);
                var completedTask = await Task.WhenAny(replayFinishTask, lagSpikeTask).ConfigureAwait(false);
                if (completedTask == lagSpikeTask)
                {
                    var lagSpike = await lagSpikeTask.ConfigureAwait(false);
                    throw new InvalidOperationException(
                        "Replay ended with lag spike during recording for " +
                        plan.OutputBaseName + ": " + (lagSpike?.Message ?? "spike detected"));
                }

                await replayFinishTask.ConfigureAwait(false);
                if (lagSpikeTask.Status == TaskStatus.RanToCompletion)
                {
                    var lagSpike = lagSpikeTask.Result;
                    warning = lagSpike?.Message;
                    if (!string.IsNullOrWhiteSpace(warning))
                    {
                        _logger.Warn("Replay ended with end-of-playback lag spike warning for " +
                                     plan.OutputBaseName + ": " + warning);
                    }
                }
            }

            await DelayIfNeededAsync(plan.PostRoll, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failure = ex;
            _logger.Error("Assignment plan failed: " + plan.OutputBaseName + ": " + ex);
        }
        finally
        {
            if (recordingStarted)
            {
                try
                {
                    await NotifyPhaseChangedAsync("Finalizing", CancellationToken.None).ConfigureAwait(false);
                    stopResult = await _recordingBackend.StopRecordingAsync(plan, contentStartUtc, CancellationToken.None)
                        .ConfigureAwait(false);
                    outputPath = stopResult.OutputPath;
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to stop recorder after plan " + plan.OutputBaseName + ": " + ex);
                    if (failure == null)
                    {
                        failure = ex;
                    }
                }
            }

            if ((failure != null || cancellationToken.IsCancellationRequested) && replayLaunchStarted)
            {
                await RequestLeaveActiveReplayAfterInterruptedPlanAsync(plan, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        if (failure == null)
        {
            _logger.Info("Finished assignment plan: " + plan.OutputBaseName +
                         (string.IsNullOrEmpty(outputPath) ? "" : ". Output: " + outputPath) +
                         (string.IsNullOrWhiteSpace(stopResult?.SyncStatus) ? "" : ". Sync=" + stopResult.SyncStatus));
            return new RecordingExecutionResult(
                true,
                outputPath,
                null,
                warning,
                stopResult?.SyncStatus,
                stopResult?.SyncCorrectionMilliseconds,
                stopResult?.TrimStartSeconds,
                stopResult?.SyncReportPath);
        }

        return new RecordingExecutionResult(false, outputPath, failure.Message, null);
    }

    private async Task NotifyPhaseChangedAsync(string phase, CancellationToken cancellationToken)
    {
        if (_phaseChangedAsync == null)
        {
            return;
        }

        try
        {
            await _phaseChangedAsync(phase, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn("Could not report assignment phase " + phase + ": " + ex.Message);
        }
    }

    private async Task RequestLeaveActiveReplayAfterInterruptedPlanAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.Warn("Requesting replay exit after interrupted plan: " + plan.OutputBaseName);
            await UnityMainThreadTaskScheduler.Factory
                .StartNew(
                    () => ActiveReplayTerminator.RequestLeaveActiveReplay(_logger),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn("Could not request replay exit after interrupted plan " + plan.OutputBaseName + ": " + ex.Message);
        }
    }

    private Task<ReplayPlaybackSession> StartReplayOnMainThreadAsync(
        IReplayPlaybackDriver playbackDriver,
        ReplayQueueItem queueItem,
        ReplayReference replayReference,
        CancellationToken cancellationToken)
    {
        return UnityMainThreadTaskScheduler.Factory
            .StartNew(
                () => playbackDriver.StartReplayAsync(queueItem, replayReference, cancellationToken),
                cancellationToken)
            .Unwrap();
    }

    private Task PlaySyncMarkerOnMainThreadAsync(CancellationToken cancellationToken)
    {
        return UnityMainThreadTaskScheduler.Factory
            .StartNew(
                () => RecordingSyncMarkerPlayer.PlayAsync(cancellationToken),
                cancellationToken)
            .Unwrap();
    }

    private async Task StartRecordingWithRetriesAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _settings.StartRecordingRetryCount + 1);
        var retryDelay = TimeSpan.FromSeconds(Math.Max(0, _settings.StartRecordingRetryDelaySeconds));
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _recordingBackend.StartRecordingAsync(plan, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                _logger.Warn("Recorder start failed for " + plan.OutputBaseName +
                             " on attempt " + attempt + " of " + maxAttempts +
                             ". Retrying after " + retryDelay.TotalSeconds + " second(s): " + ex.Message);

                await DelayIfNeededAsync(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw lastException ?? new InvalidOperationException("Recorder start failed.");
    }

    private Task ValidateReplayOnMainThreadAsync(
        IReplayPlaybackDriver playbackDriver,
        ReplayReference replayReference,
        CancellationToken cancellationToken)
    {
        return UnityMainThreadTaskScheduler.Factory
            .StartNew(
                () => playbackDriver.ValidateReplayAsync(replayReference, cancellationToken),
                cancellationToken)
            .Unwrap();
    }

    private IReplayPlaybackDriver SelectPlaybackDriver(ReplayReference replayReference)
    {
        foreach (var playbackDriver in _playbackDrivers)
        {
            if (playbackDriver.CanPlay(replayReference))
            {
                return playbackDriver;
            }
        }

        throw new InvalidOperationException(
            "No replay playback driver is available for " + replayReference.Provider + " (" + replayReference.Kind + ").");
    }

    private Task<ReplayLagSpikeMonitor> CreateLagSpikeMonitorOnMainThreadAsync(CancellationToken cancellationToken)
    {
        return UnityMainThreadTaskScheduler.Factory
            .StartNew(
                () => ReplayLagSpikeMonitor.Create(_settings, _logger),
                cancellationToken);
    }

    private async Task<bool> ConfirmRecordingStartedAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);

            var status = await _recordingBackend.GetRecordingStatusAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("Recorder status after start for " + plan.OutputBaseName +
                         ": active=" + status.OutputActive +
                         ", paused=" + status.OutputPaused +
                         ", check=" + attempt + ".");
            if (status.OutputActive)
            {
                return true;
            }
        }

        return false;
    }

    private TimeSpan CreateReplayFinishTimeout(RecordingPlan plan)
    {
        return plan.QueueItem.EstimatedPlaybackLength +
               TimeSpan.FromSeconds(Math.Max(0, _settings.ReplayFinishTimeoutPaddingSeconds));
    }

    private static Task DelayIfNeededAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

}
