using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BeatLeader.Replayer;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Playback;
using BSAutoReplayRecorder.Core.Obs;
using IPA.Logging;
using IPA.Utilities.Async;

namespace BSAutoReplayRecorder.Plugin;

public sealed class BatchRecordingRunner
{
    private readonly BatchRecorderSettings _settings;
    private readonly BeatLeaderReplayPlaybackDriver _playbackDriver;
    private readonly ObsWebSocketRecorder _obsRecorder;
    private readonly CompletedReplayStore? _completedReplayStore;
    private readonly BatchRunControl? _runControl;
    private readonly ReplayReferenceParser _referenceParser = new ReplayReferenceParser();
    private readonly Logger _logger;

    public BatchRecordingRunner(
        BatchRecorderSettings settings,
        BeatLeaderReplayPlaybackDriver playbackDriver,
        ObsWebSocketRecorder obsRecorder,
        CompletedReplayStore? completedReplayStore,
        BatchRunControl? runControl,
        Logger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _playbackDriver = playbackDriver ?? throw new ArgumentNullException(nameof(playbackDriver));
        _obsRecorder = obsRecorder ?? throw new ArgumentNullException(nameof(obsRecorder));
        _completedReplayStore = completedReplayStore;
        _runControl = runControl;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(IReadOnlyList<ReplayQueueItem> queueItems, CancellationToken cancellationToken)
    {
        if (queueItems == null)
        {
            throw new ArgumentNullException(nameof(queueItems));
        }

        var selectedItems = SelectItems(queueItems);
        if (selectedItems.Count == 0)
        {
            _logger.Warn("Batch recorder has no replay items to run.");
            RecordingStatusOverlay.ShowIdle("No pending replays", "Batch recorder has no replay items to run");
            return;
        }

        ValidateSettings();

        var plans = new RecordingPlanner().CreatePlans(selectedItems, _settings.ToRecordingPlanOptions());
        _logger.Info("Batch recorder prepared " + plans.Count + " plan(s). DryRun=" + _settings.DryRun + ".");
        _logger.Info("Batch recorder output directory: " +
                     GamePaths.ResolveGamePath(_settings.RecordingOutputDirectory));
        _logger.Info("Batch recorder will " +
                     (_settings.MoveProcessedReplays ? "" : "not ") +
                     "move processed replay files.");

        if (_settings.DryRun)
        {
            foreach (var plan in plans)
            {
                _logger.Info("Dry run plan: " + plan.OutputBaseName + " from " + plan.QueueItem.ReplayPath);
            }

            return;
        }

        if (_settings.RequirePreflightReplayValidation)
        {
            await RunPreflightAsync(plans, cancellationToken).ConfigureAwait(false);
        }

        var initialObsStatus = await _obsRecorder.GetRecordingStatusAsync(cancellationToken).ConfigureAwait(false);
        if (initialObsStatus.OutputActive)
        {
            throw new InvalidOperationException("OBS is already recording. Stop OBS recording before starting a batch.");
        }

        var succeeded = 0;
        var failed = 0;
        var hasRunPlan = false;
        var stoppedAfterCurrent = false;

        for (var planIndex = 0; planIndex < plans.Count; planIndex++)
        {
            var plan = plans[planIndex];
            var displayIndex = planIndex + 1;
            cancellationToken.ThrowIfCancellationRequested();
            if (hasRunPlan)
            {
                var delay = TimeSpan.FromSeconds(Math.Max(0, _settings.DelayBetweenRecordingsSeconds));
                RecordingStatusOverlay.ShowCountdown(plan, displayIndex, plans.Count, delay);
                await DelayIfNeededAsync(delay, cancellationToken).ConfigureAwait(false);
            }

            var success = await RunPlanAsync(plan, displayIndex, plans.Count, cancellationToken).ConfigureAwait(false);
            hasRunPlan = true;
            if (success)
            {
                succeeded++;
                RecordingStatusOverlay.ShowPlanFinished(plan, succeeded, failed);
            }
            else
            {
                failed++;
                RecordingStatusOverlay.ShowPlanFailed(plan, succeeded, failed);
                if (!_settings.ContinueAfterFailure)
                {
                    break;
                }
            }

            if (_runControl != null && _runControl.StopAfterCurrentRequested)
            {
                _logger.Info("Batch recorder stopping after current plan because it was requested from the control panel.");
                stoppedAfterCurrent = true;
                break;
            }
        }

        if (stoppedAfterCurrent)
        {
            _logger.Info("Batch recorder stopped after current plan. Succeeded: " + succeeded + ", failed: " + failed + ".");
            RecordingStatusOverlay.ShowBatchStopped(succeeded, failed);
        }
        else
        {
            _logger.Info("Batch recorder completed. Succeeded: " + succeeded + ", failed: " + failed + ".");
            RecordingStatusOverlay.ShowBatchCompleted(succeeded, failed);
        }
    }

    private async Task RunPreflightAsync(
        IReadOnlyList<RecordingPlan> plans,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        for (var index = 0; index < plans.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = plans[index];
            RecordingStatusOverlay.ShowPreflight(plan, index + 1, plans.Count);

            try
            {
                var replayReference = _referenceParser.Parse(plan.QueueItem.ReplayPath);
                await ValidateReplayOnMainThreadAsync(replayReference, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(plan.OutputBaseName + ": " + ex.Message);
            }
        }

        if (failures.Count == 0)
        {
            _logger.Info("Preflight replay validation passed for " + plans.Count + " plan(s).");
            RecordingStatusOverlay.ShowToast(
                "Preflight passed",
                plans.Count + " replay(s) ready",
                TimeSpan.FromSeconds(4));
            return;
        }

        foreach (var failure in failures)
        {
            _logger.Error("Preflight replay validation failed: " + failure);
        }

        RecordingStatusOverlay.ShowToast(
            "Preflight failed",
            failures.Count + " replay(s) need attention",
            TimeSpan.FromSeconds(12),
            isError: true);
        throw new InvalidOperationException(
            "Preflight replay validation failed for " + failures.Count + " replay(s). See the Beat Saber log for details.");
    }

    private IReadOnlyList<ReplayQueueItem> SelectItems(IReadOnlyList<ReplayQueueItem> queueItems)
    {
        if (_settings.MaxReplayCount <= 0)
        {
            return queueItems;
        }

        return queueItems.Take(_settings.MaxReplayCount).ToList();
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.RecordingOutputDirectory))
        {
            throw new InvalidOperationException("RecordingOutputDirectory is required.");
        }

        if (_settings.MoveProcessedReplays)
        {
            if (string.IsNullOrWhiteSpace(_settings.CompletedReplayDirectory))
            {
                throw new InvalidOperationException("CompletedReplayDirectory is required when MoveProcessedReplays is enabled.");
            }

            if (string.IsNullOrWhiteSpace(_settings.FailedReplayDirectory))
            {
                throw new InvalidOperationException("FailedReplayDirectory is required when MoveProcessedReplays is enabled.");
            }
        }
    }

    private async Task<bool> RunPlanAsync(
        RecordingPlan plan,
        int displayIndex,
        int totalPlans,
        CancellationToken cancellationToken)
    {
        string? outputPath = null;
        var recordingStarted = false;
        Exception? failure = null;

        try
        {
            RecordingStatusOverlay.ShowPreparing(plan, displayIndex, totalPlans);
            _logger.Info("Starting batch plan: " + plan.OutputBaseName);
            var replayReference = _referenceParser.Parse(plan.QueueItem.ReplayPath);
            await ValidateReplayOnMainThreadAsync(replayReference, cancellationToken).ConfigureAwait(false);

            RecordingStatusOverlay.ShowStartingObs(plan, displayIndex, totalPlans);
            await StartRecordingWithRetriesAsync(plan, cancellationToken).ConfigureAwait(false);
            recordingStarted = await ConfirmObsRecordingStartedAsync(plan, cancellationToken).ConfigureAwait(false);
            if (!recordingStarted)
            {
                throw new InvalidOperationException("OBS accepted StartRecord, but GetRecordStatus never reported an active recording.");
            }

            RecordingStatusOverlay.ShowRecording(plan, displayIndex, totalPlans);
            await DelayIfNeededAsync(plan.PreRoll, cancellationToken).ConfigureAwait(false);

            using (var finishWait = new ReplayFinishWait())
            {
                await StartReplayOnMainThreadAsync(plan.QueueItem, replayReference, cancellationToken)
                    .ConfigureAwait(false);

                await finishWait.WaitAsync(CreateReplayFinishTimeout(plan), cancellationToken).ConfigureAwait(false);
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
            _logger.Error("Batch plan failed: " + plan.OutputBaseName + ": " + ex);
        }
        finally
        {
            if (recordingStarted)
            {
                try
                {
                    var stopResult = await _obsRecorder.StopRecordingAsync(plan, CancellationToken.None)
                        .ConfigureAwait(false);
                    outputPath = stopResult.OutputPath;
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to stop OBS recording after plan " + plan.OutputBaseName + ": " + ex);
                    if (failure == null)
                    {
                        failure = ex;
                    }
                }
            }
        }

        if (failure == null)
        {
            outputPath = await MoveRecordingOutputAsync(plan, outputPath, CancellationToken.None)
                .ConfigureAwait(false);
            await ArchiveReplayAsync(plan, succeeded: true, CancellationToken.None).ConfigureAwait(false);
            _completedReplayStore?.MarkCompleted(plan.QueueItem, outputPath);
            _logger.Info("Finished batch plan: " + plan.OutputBaseName +
                         (string.IsNullOrEmpty(outputPath) ? "" : ". OBS output: " + outputPath));
            return true;
        }

        await ArchiveReplayAsync(plan, succeeded: false, CancellationToken.None).ConfigureAwait(false);
        return false;
    }

    private Task<ReplayPlaybackSession> StartReplayOnMainThreadAsync(
        ReplayQueueItem queueItem,
        ReplayReference replayReference,
        CancellationToken cancellationToken)
    {
        return UnityMainThreadTaskScheduler.Factory
            .StartNew(
                () => _playbackDriver.StartReplayAsync(queueItem, replayReference, cancellationToken),
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
                await _obsRecorder.StartRecordingAsync(plan, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                _logger.Warn("OBS StartRecord failed for " + plan.OutputBaseName +
                             " on attempt " + attempt + " of " + maxAttempts +
                             ". Retrying after " + retryDelay.TotalSeconds + " second(s): " + ex.Message);

                await DelayIfNeededAsync(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw lastException ?? new InvalidOperationException("OBS StartRecord failed.");
    }

    private Task ValidateReplayOnMainThreadAsync(
        ReplayReference replayReference,
        CancellationToken cancellationToken)
    {
        return UnityMainThreadTaskScheduler.Factory
            .StartNew(
                () => _playbackDriver.ValidateReplayAsync(replayReference, cancellationToken),
                cancellationToken)
            .Unwrap();
    }

    private async Task<bool> ConfirmObsRecordingStartedAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);

            var status = await _obsRecorder.GetRecordingStatusAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("OBS recording status after StartRecord for " + plan.OutputBaseName +
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

    private async Task<string?> MoveRecordingOutputAsync(
        RecordingPlan plan,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        if (!_settings.MoveRecordingsToOutputDirectory || string.IsNullOrEmpty(outputPath))
        {
            return outputPath;
        }

        var outputDirectory = GamePaths.ResolveGamePath(_settings.RecordingOutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var extension = Path.GetExtension(outputPath);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".mkv";
        }

        var targetPath = Path.Combine(outputDirectory, plan.OutputBaseName + extension);
        targetPath = PrepareTargetPath(targetPath, _settings.OverwriteExistingRecordings);

        return await FileMoveHelper
            .MoveWithRetriesAsync(
                outputPath,
                targetPath,
                _settings.OverwriteExistingRecordings,
                "OBS output",
                _logger,
                cancellationToken)
            .ConfigureAwait(false)
            ? targetPath
            : outputPath;
    }

    private async Task ArchiveReplayAsync(
        RecordingPlan plan,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        if (!_settings.MoveProcessedReplays)
        {
            return;
        }

        var sourcePath = plan.QueueItem.ReplayPath;
        var archiveDirectory = GamePaths.ResolveGamePath(
            succeeded ? _settings.CompletedReplayDirectory : _settings.FailedReplayDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var targetPath = Path.Combine(archiveDirectory, Path.GetFileName(sourcePath));
        targetPath = PrepareTargetPath(targetPath, overwrite: false);

        if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(targetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await FileMoveHelper
            .MoveWithRetriesAsync(
                sourcePath,
                targetPath,
                overwrite: false,
                "replay",
                _logger,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string PrepareTargetPath(string targetPath, bool overwrite)
    {
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        if (overwrite)
        {
            File.Delete(targetPath);
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, baseName + " (" + index + ")" + extension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private sealed class ReplayFinishWait : IDisposable
    {
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();

        public ReplayFinishWait()
        {
            ReplayerLauncher.ReplayWasFinishedEvent += HandleReplayFinished;
        }

        public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(_completion.Task, timeoutTask).ConfigureAwait(false);
            if (completed == _completion.Task)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Timed out waiting for BeatLeader replay to finish.");
        }

        public void Dispose()
        {
            ReplayerLauncher.ReplayWasFinishedEvent -= HandleReplayFinished;
        }

        private void HandleReplayFinished(ReplayLaunchData launchData)
        {
            _completion.TrySetResult(true);
        }
    }
}
