using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Playback;
using BSAutoReplayRecorder.Core.Replay;
using BSAutoReplayRecorder.Core.Utility;
using IPA.Utilities.Async;
using Newtonsoft.Json;
using Logger = IPA.Logging.Logger;

namespace BSAutoReplayRecorder.Plugin;

public sealed class ControlPanelWorkerRunner : IDisposable
{
    private const double MaximumIdleShutdownMinutes = 7 * 24 * 60;
    private readonly BatchRecorderSettings _settings;
    private readonly Logger _logger;
    private readonly Action<string>? _persistWorkerId;
    private readonly Action<string>? _statusChanged;
    private CancellationTokenSource? _cancellation;
    private Task? _task;
    private Task? _idleShutdownTask;
    private bool _disposed;
    private int _appliedGamePresentationSettingsVersion;
    private string _gamePresentationSyncStatus = "Pending";
    private string? _gamePresentationSyncError;
    private long _lastControlPanelContactUtcTicks;
    private int _idleShutdownRequested;

    public ControlPanelWorkerRunner(
        BatchRecorderSettings settings,
        Logger logger,
        Action<string>? persistWorkerId,
        Action<string>? statusChanged)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _persistWorkerId = persistWorkerId;
        _statusChanged = statusChanged;
        _lastControlPanelContactUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
    }

    public void Start(CancellationToken shutdownToken)
    {
        if (_task != null)
        {
            return;
        }

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        _task = Task.Run(() => RunUntilCanceledAsync(_cancellation.Token), _cancellation.Token);
        _idleShutdownTask = Task.Run(() => RunIdleShutdownAsync(_cancellation.Token), _cancellation.Token);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _idleShutdownTask = null;
    }

    private async Task RunUntilCanceledAsync(CancellationToken cancellationToken)
    {
        var idleShutdownTask = _idleShutdownTask;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunConnectedLoopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Warn("Control panel worker disconnected: " + ex.Message);
                    SetStatus("Control panel disconnected");
                    RecordingStatusOverlay.SetStatusPanelVisible(true);
                    RecordingStatusOverlay.ShowToast(
                        "Control panel disconnected",
                        ex.Message,
                        TimeSpan.FromSeconds(8),
                        isError: true);
                    await DelayIgnoringCancellationAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (idleShutdownTask != null)
            {
                await ObserveTaskAsync(idleShutdownTask).ConfigureAwait(false);
            }
        }
    }

    private async Task RunConnectedLoopAsync(CancellationToken cancellationToken)
    {
        using (var client = new ControlPanelWorkerClient(_settings.ControlPanelWorker))
        {
            var registration = await RegisterAsync(client, cancellationToken).ConfigureAwait(false);
            var workerId = registration.WorkerId;
            var nextHeartbeat = DateTimeOffset.MinValue;

            await ApplyGamePresentationSettingsAsync(
                registration.Settings.GamePresentationSettingsVersion,
                registration.Settings.GamePresentation,
                cancellationToken).ConfigureAwait(false);

            SetStatus("Control-panel worker online");
            ShowConnectedStatus(registration.InstanceIndex, null);

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextHeartbeat)
                {
                    var heartbeat = await client.HeartbeatAsync(
                        CreateHeartbeatRequest(workerId, "Online", null),
                        cancellationToken).ConfigureAwait(false);
                    await HandleHeartbeatResponseAsync(heartbeat, cancellationToken).ConfigureAwait(false);
                    ShowConnectedStatus(registration.InstanceIndex, heartbeat.Progress);
                    nextHeartbeat = now + _settings.ControlPanelWorker.HeartbeatInterval;
                    RecordControlPanelContact();
                }

                var assignment = await client.GetAssignmentAsync(workerId, cancellationToken).ConfigureAwait(false);
                await ApplyGamePresentationSettingsAsync(
                    assignment.GamePresentationSettingsVersion,
                    assignment.GamePresentation,
                    cancellationToken).ConfigureAwait(false);
                if (assignment.HasAssignment)
                {
                    await RunAssignmentAsync(client, workerId, assignment, cancellationToken).ConfigureAwait(false);
                    nextHeartbeat = DateTimeOffset.MinValue;
                    continue;
                }

                ShowConnectedStatus(assignment.InstanceIndex ?? registration.InstanceIndex, assignment.Progress);
                await Task.Delay(_settings.ControlPanelWorker.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ControlPanelWorkerRegisterResponse> RegisterAsync(
        ControlPanelWorkerClient client,
        CancellationToken cancellationToken)
    {
        var workerSettings = _settings.ControlPanelWorker;
        var response = await client.RegisterAsync(
            new ControlPanelWorkerRegisterRequest
            {
                WorkerId = NormalizeNullable(workerSettings.WorkerId),
                WorkerName = NormalizeNullable(workerSettings.WorkerName) ?? CreateDefaultWorkerName(),
                PreferredInstanceIndex = workerSettings.PreferredInstanceIndex,
                GameDirectory = GamePaths.GetGameRoot(),
                PluginVersion = GetPluginVersion()
            },
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(response.WorkerId) &&
            !string.Equals(NormalizeNullable(workerSettings.WorkerId), response.WorkerId, StringComparison.OrdinalIgnoreCase))
        {
            workerSettings.WorkerId = response.WorkerId;
            _persistWorkerId?.Invoke(response.WorkerId);
        }

        _logger.Info("Control panel worker registered as " + response.WorkerId +
                     " on instance " + (response.InstanceIndex + 1) + ".");
        RecordControlPanelContact();
        return response;
    }

    private async Task RunAssignmentAsync(
        ControlPanelWorkerClient client,
        string workerId,
        ControlPanelWorkerAssignmentResponse assignment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignment.AssignmentId))
        {
            throw new InvalidOperationException("Control panel assignment did not include an assignment id.");
        }

        SetStatus("Recording assignment");
        RecordingStatusOverlay.Clear();
        RecordingStatusOverlay.SetStatusPanelVisible(false);
        RecordControlPanelContact();

        await client.ReportAsync(
            new ControlPanelWorkerReportRequest
            {
                WorkerId = workerId,
                AssignmentId = assignment.AssignmentId,
                Status = "Recording"
            },
            cancellationToken).ConfigureAwait(false);

        using (var assignmentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        using (var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var heartbeatTask = Task.Run(
                () => RunAssignmentHeartbeatAsync(
                    client,
                    workerId,
                    assignment,
                    assignmentCancellation,
                    heartbeatCancellation.Token),
                heartbeatCancellation.Token);

            try
            {
                var result = await ExecuteAssignmentAsync(assignment, assignmentCancellation.Token).ConfigureAwait(false);
                heartbeatCancellation.Cancel();
                await ObserveTaskAsync(heartbeatTask).ConfigureAwait(false);
                RecordControlPanelContact();

                await client.ReportAsync(
                    new ControlPanelWorkerReportRequest
                    {
                        WorkerId = workerId,
                        AssignmentId = assignment.AssignmentId,
                        Status = result.Succeeded ? "Completed" : "Failed",
                        OutputPath = result.OutputPath,
                        Error = result.Error,
                        Warning = result.Warning,
                        SyncStatus = result.SyncStatus,
                        SyncCorrectionMilliseconds = result.SyncCorrectionMilliseconds,
                        TrimStartSeconds = result.TrimStartSeconds,
                        SyncReportPath = result.SyncReportPath
                    },
                    CancellationToken.None).ConfigureAwait(false);

                SetStatus(result.Succeeded ? "Control-panel worker online" : "Assignment failed");
                if (result.Succeeded)
                {
                    await ShowConnectedStatusThenDelayBeforeNextAssignmentAsync(
                        client,
                        workerId,
                        assignment,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await ShowConnectedStatusFromHeartbeatAsync(
                        client,
                        workerId,
                        assignment.InstanceIndex,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                heartbeatCancellation.Cancel();
                await ObserveTaskAsync(heartbeatTask).ConfigureAwait(false);
                RecordControlPanelContact();
                throw;
            }
            catch (OperationCanceledException) when (assignmentCancellation.IsCancellationRequested)
            {
                heartbeatCancellation.Cancel();
                await ObserveTaskAsync(heartbeatTask).ConfigureAwait(false);
                RecordControlPanelContact();

                var reason = "Assignment canceled by control panel.";
                _logger.Warn(reason);
                await client.ReportAsync(
                    new ControlPanelWorkerReportRequest
                    {
                        WorkerId = workerId,
                        AssignmentId = assignment.AssignmentId,
                        Status = "Stopped",
                        Error = reason
                    },
                    CancellationToken.None).ConfigureAwait(false);

                SetStatus("Assignment stopped");
                await ShowConnectedStatusFromHeartbeatAsync(
                    client,
                    workerId,
                    assignment.InstanceIndex,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                heartbeatCancellation.Cancel();
                await ObserveTaskAsync(heartbeatTask).ConfigureAwait(false);
                RecordControlPanelContact();

                _logger.Error("Control panel assignment failed: " + ex);
                await client.ReportAsync(
                    new ControlPanelWorkerReportRequest
                    {
                        WorkerId = workerId,
                        AssignmentId = assignment.AssignmentId,
                        Status = "Failed",
                        Error = ex.Message
                    },
                    CancellationToken.None).ConfigureAwait(false);

                SetStatus("Assignment failed");
                await ShowConnectedStatusFromHeartbeatAsync(
                    client,
                    workerId,
                    assignment.InstanceIndex,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task RunAssignmentHeartbeatAsync(
        ControlPanelWorkerClient client,
        string workerId,
        ControlPanelWorkerAssignmentResponse assignment,
        CancellationTokenSource assignmentCancellation,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await client.HeartbeatAsync(
                CreateHeartbeatRequest(workerId, "Recording", assignment.ReplayId),
                cancellationToken).ConfigureAwait(false);
            if (response.ShouldCancelAssignment)
            {
                _logger.Warn(
                    "Control panel requested assignment cancellation: " +
                    (string.IsNullOrWhiteSpace(response.CancellationReason)
                        ? "no reason provided"
                        : response.CancellationReason));
                await HandleHeartbeatResponseAsync(response, cancellationToken).ConfigureAwait(false);
                assignmentCancellation.Cancel();
                return;
            }

            await HandleHeartbeatResponseAsync(response, cancellationToken).ConfigureAwait(false);
            await Task.Delay(_settings.ControlPanelWorker.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleHeartbeatResponseAsync(
        ControlPanelWorkerHeartbeatResponse response,
        CancellationToken cancellationToken)
    {
        RecordControlPanelContact();
        await ApplyGamePresentationSettingsAsync(
            response.GamePresentationSettingsVersion,
            response.GamePresentation,
            cancellationToken).ConfigureAwait(false);

        if (!response.ShouldOpenPauseMenu && !response.ShouldCancelAssignment)
        {
            return;
        }

        await RequestLeaveActiveReplayAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task RequestLeaveActiveReplayAsync(CancellationToken cancellationToken)
    {
        return UnityMainThreadTaskScheduler.Factory.StartNew(
            () => ActiveReplayTerminator.RequestLeaveActiveReplay(_logger),
            cancellationToken);
    }

    private async Task<RecordingExecutionResult> ExecuteAssignmentAsync(
        ControlPanelWorkerAssignmentResponse assignment,
        CancellationToken cancellationToken)
    {
        var replayPath = NormalizeNullable(assignment.ReplayPath)
                         ?? throw new InvalidOperationException("Control panel assignment did not include a replay path.");
        if (!File.Exists(replayPath))
        {
            throw new FileNotFoundException("Assigned replay file was not found.", replayPath);
        }

        var replayInfo = CreateReplayInfo(assignment, replayPath);
        var item = new ReplayQueueItem(
            1,
            replayPath,
            replayInfo,
            assignment.Provider == ReplayProvider.Unknown ? ResolveProviderFromPath(replayPath) : assignment.Provider,
            assignment.ReferenceKind == ReplayReferenceKind.Unknown ? ResolveReferenceKindFromPath(replayPath) : assignment.ReferenceKind,
            NormalizeNullable(assignment.SourceUrl),
            NormalizeNullable(assignment.ScoreId));
        var outputBaseName = NormalizeNullable(assignment.OutputBaseName) ??
                             FileNameSanitizer.SanitizeBaseName(
                                 "control-panel - " + replayInfo.SongName + " [" + replayInfo.Difficulty + "]");
        var plan = new RecordingPlan(
            item,
            outputBaseName,
            TimeSpan.Zero,
            TimeSpan.Zero,
            "");
        var assignmentSettings = CreateAssignmentSettings(assignment);

        using (var recordingBackend = RecorderBackendFactory.Create(assignmentSettings, _logger))
        using (var scoreSubmissionDisabler = ResolveAssignmentBoolValue(
                   assignment.DisableScoreSubmissions,
                   assignmentSettings.DisableScoreSubmissions)
                   ? ScoreSubmissionDisabler.Install(_logger)
                   : null)
        {
            var runner = new BatchRecordingRunner(
                assignmentSettings,
                CreatePlaybackDrivers(assignmentSettings.SuppressScoreSaberReplayUi),
                recordingBackend,
                _logger);

            return await runner.RunSingleAsync(plan, cancellationToken).ConfigureAwait(false);
        }
    }

    private static BsorInfo CreateReplayInfo(ControlPanelWorkerAssignmentResponse assignment, string replayPath)
    {
        var provider = assignment.Provider == ReplayProvider.Unknown
            ? ResolveProviderFromPath(replayPath)
            : assignment.Provider;

        BsorInfo info;
        if (provider == ReplayProvider.ScoreSaber2)
        {
            info = new ScoreSaberReplayInfoReader().Read(replayPath);
        }
        else
        {
            info = new BsorInfoReader().Read(replayPath);
        }

        if (!string.IsNullOrWhiteSpace(assignment.SongName))
        {
            info.SongName = assignment.SongName!;
        }

        if (!string.IsNullOrWhiteSpace(assignment.Mapper))
        {
            info.Mapper = assignment.Mapper!;
        }

        if (!string.IsNullOrWhiteSpace(assignment.PlayerName))
        {
            info.PlayerName = assignment.PlayerName!;
        }

        if (!string.IsNullOrWhiteSpace(assignment.Difficulty))
        {
            info.Difficulty = assignment.Difficulty!;
        }

        if (!string.IsNullOrWhiteSpace(assignment.Mode))
        {
            info.Mode = assignment.Mode!;
        }

        if (!string.IsNullOrWhiteSpace(assignment.LevelHash))
        {
            info.LevelHash = assignment.LevelHash!;
        }

        if (assignment.EstimatedSeconds > 0)
        {
            info.LastFrameTime = (float)assignment.EstimatedSeconds;
            info.StartTime = 0;
            info.Speed = 1;
        }

        return info;
    }

    private static bool ResolveAssignmentBoolValue(bool? assignmentValue, bool defaultValue)
    {
        return assignmentValue ?? defaultValue;
    }

    private IReadOnlyList<IReplayPlaybackDriver> CreatePlaybackDrivers(bool suppressScoreSaberReplayUi)
    {
        return new IReplayPlaybackDriver[]
        {
            new BeatLeaderReplayPlaybackDriver(_logger),
            new ScoreSaberReplayPlaybackDriver(_logger, suppressScoreSaberReplayUi)
        };
    }

    private static ReplayProvider ResolveProviderFromPath(string replayPath)
    {
        return string.Equals(Path.GetExtension(replayPath), ".dat", StringComparison.OrdinalIgnoreCase)
            ? ReplayProvider.ScoreSaber2
            : ReplayProvider.BeatLeader;
    }

    private static ReplayReferenceKind ResolveReferenceKindFromPath(string replayPath)
    {
        return string.Equals(Path.GetExtension(replayPath), ".dat", StringComparison.OrdinalIgnoreCase)
            ? ReplayReferenceKind.LocalScoreSaberDatFile
            : ReplayReferenceKind.LocalBsorFile;
    }

    private BatchRecorderSettings CreateAssignmentSettings(ControlPanelWorkerAssignmentResponse assignment)
    {
        var clone = JsonConvert.DeserializeObject<BatchRecorderSettings>(
                        JsonConvert.SerializeObject(_settings))
                    ?? new BatchRecorderSettings();

        if (!string.IsNullOrWhiteSpace(assignment.OutputDirectory))
        {
            clone.RecordingOutputDirectory = assignment.OutputDirectory!;
        }

        if (!string.IsNullOrWhiteSpace(assignment.RecorderHostUrl))
        {
            clone.RecorderHost.BaseUrl = assignment.RecorderHostUrl!;
            clone.RecorderHost.OutputDirectory = assignment.OutputDirectory ?? "";
            clone.RecorderHost.TargetProcessId = assignment.TargetProcessId;
            clone.RecorderHost.TargetFps = assignment.TargetFps > 0 ? assignment.TargetFps : null;
            clone.RecorderHost.CaptureWidth = assignment.CaptureWidth > 0 ? assignment.CaptureWidth : null;
            clone.RecorderHost.CaptureHeight = assignment.CaptureHeight > 0 ? assignment.CaptureHeight : null;
            clone.RecorderHost.Encoder = assignment.Encoder ?? "";
            clone.RecorderHost.VideoBitrateKbps = assignment.VideoBitrateKbps > 0 ? assignment.VideoBitrateKbps : null;
            clone.RecorderHost.OutputFormat = assignment.OutputFormat ?? "";
            clone.RecorderHost.MonitorIndex = assignment.MonitorIndex >= 0 ? assignment.MonitorIndex : null;
            clone.RecorderHost.QualityMode = assignment.QualityMode ?? "";
            clone.RecorderHost.AudioMode = assignment.AudioMode ?? "";
            clone.RecorderHost.AudioDeviceName = assignment.AudioDeviceName ?? "";
            clone.RecorderHost.AudioBitrateKbps = assignment.AudioBitrateKbps > 0 ? assignment.AudioBitrateKbps : null;
            clone.RecorderHost.AudioSampleRate = assignment.AudioSampleRate > 0 ? assignment.AudioSampleRate : null;
            clone.RecorderHost.AudioChannels = assignment.AudioChannels > 0 ? assignment.AudioChannels : null;
            clone.RecorderHost.AudioLevelMode = assignment.AudioLevelMode ?? "";
            clone.RecorderHost.AudioTargetLevelDb = string.IsNullOrWhiteSpace(assignment.AudioLevelMode)
                ? null
                : assignment.AudioTargetLevelDb;
        }

        if (assignment.DelayBetweenRecordingsSeconds > 0)
        {
            clone.DelayBetweenRecordingsSeconds = Math.Min(30, assignment.DelayBetweenRecordingsSeconds);
        }

        clone.DisableScoreSubmissions = true;
        clone.SuppressScoreSaberReplayUi = true;

        if (assignment.LagSpikeStartupGraceSeconds.HasValue)
        {
            clone.LagSpikeStartupGraceSeconds = Math.Min(30, Math.Max(0, assignment.LagSpikeStartupGraceSeconds.Value));
        }

        return clone;
    }

    private async Task RunIdleShutdownAsync(CancellationToken cancellationToken)
    {
        var timeout = GetIdleShutdownTimeout();
        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = DateTimeOffset.UtcNow - GetLastControlPanelContact();
            if (elapsed >= timeout)
            {
                await RequestGameShutdownAfterIdleAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var delay = timeout - elapsed;
            await DelayIgnoringCancellationAsync(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RequestGameShutdownAfterIdleAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _idleShutdownRequested, 1, 0) != 0)
        {
            return;
        }

        var timeout = GetIdleShutdownTimeout();
        _logger.Warn(
            "No control panel heartbeat for " + FormatTimeout(timeout) + "; shutting down game worker.");
        SetStatus("Control panel offline; shutting down");
        RecordingStatusOverlay.ShowToast(
            "Control panel offline",
            "No control panel heartbeat for " + FormatTimeout(timeout) + ". Closing game.",
            TimeSpan.FromSeconds(12),
            isError: true);

        await UnityMainThreadTaskScheduler.Factory.StartNew(
            () =>
            {
                if (cancellationToken.IsCancellationRequested || _disposed)
                {
                    return;
                }

                RequestLeaveActiveReplay();
                Application.Quit();
            },
            cancellationToken).ConfigureAwait(false);
    }

    private void RequestLeaveActiveReplay()
    {
        try
        {
            ActiveReplayTerminator.RequestLeaveActiveReplay(_logger);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to request active replay exit during idle shutdown: " + ex);
        }
    }

    private DateTimeOffset GetLastControlPanelContact()
    {
        return new DateTimeOffset(Interlocked.Read(ref _lastControlPanelContactUtcTicks), TimeSpan.Zero);
    }

    private void RecordControlPanelContact()
    {
        Interlocked.Exchange(ref _lastControlPanelContactUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    private TimeSpan GetIdleShutdownTimeout()
    {
        var value = _settings.ControlPanelWorker.IdleShutdownMinutes;
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMinutes(Math.Min(value, MaximumIdleShutdownMinutes));
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        var minutes = (int)timeout.TotalMinutes;
        return minutes + " minute" + (minutes == 1 ? "" : "s");
    }

    private void SetStatus(string status)
    {
        _statusChanged?.Invoke(status);
    }

    private async Task<ControlPanelWorkerRunProgress> ShowConnectedStatusFromHeartbeatAsync(
        ControlPanelWorkerClient client,
        string workerId,
        int? instanceIndex,
        CancellationToken cancellationToken)
    {
        var heartbeat = await client.HeartbeatAsync(
            CreateHeartbeatRequest(workerId, "Online", null),
            cancellationToken).ConfigureAwait(false);
        await HandleHeartbeatResponseAsync(heartbeat, cancellationToken).ConfigureAwait(false);
        ShowConnectedStatus(instanceIndex, heartbeat.Progress);
        return heartbeat.Progress;
    }

    private async Task ShowConnectedStatusThenDelayBeforeNextAssignmentAsync(
        ControlPanelWorkerClient client,
        string workerId,
        ControlPanelWorkerAssignmentResponse assignment,
        CancellationToken cancellationToken)
    {
        var progress = await ShowConnectedStatusFromHeartbeatAsync(
            client,
            workerId,
            assignment.InstanceIndex,
            cancellationToken).ConfigureAwait(false);
        var delay = GetDelayBetweenRecordings(assignment.DelayBetweenRecordingsSeconds);
        if (delay <= TimeSpan.Zero || !HasMoreRunWork(progress))
        {
            return;
        }

        SetStatus("Waiting before next replay");
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasMoreRunWork(ControlPanelWorkerRunProgress progress)
    {
        if (!progress.IsRunning || progress.TotalCount <= 0)
        {
            return false;
        }

        return Math.Max(0, progress.CompletedCount) + Math.Max(0, progress.FailedCount) < progress.TotalCount;
    }

    private static TimeSpan GetDelayBetweenRecordings(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(Math.Min(30, seconds));
    }

    private ControlPanelWorkerHeartbeatRequest CreateHeartbeatRequest(
        string workerId,
        string status,
        string? currentReplayId)
    {
        return new ControlPanelWorkerHeartbeatRequest
        {
            WorkerId = workerId,
            Status = status,
            CurrentReplayId = currentReplayId,
            AppliedGamePresentationSettingsVersion = _appliedGamePresentationSettingsVersion,
            GamePresentationSyncStatus = _gamePresentationSyncStatus,
            GamePresentationSyncError = _gamePresentationSyncError
        };
    }

    private async Task ApplyGamePresentationSettingsAsync(
        int settingsVersion,
        GamePresentationSettings? settings,
        CancellationToken cancellationToken)
    {
        if (settingsVersion <= 0 || settings == null ||
            settingsVersion == _appliedGamePresentationSettingsVersion)
        {
            return;
        }

        try
        {
            await UnityMainThreadTaskScheduler.Factory.StartNew(
                () => GamePresentationSettingsApplier.Apply(settings, _logger),
                cancellationToken).ConfigureAwait(false);
            _appliedGamePresentationSettingsVersion = settingsVersion;
            _gamePresentationSyncStatus = "Applied";
            _gamePresentationSyncError = null;
        }
        catch (Exception ex)
        {
            _gamePresentationSyncStatus = "Failed";
            _gamePresentationSyncError = ex.Message;
            _logger.Error("Failed to apply game presentation settings v" + settingsVersion + ": " + ex);
        }
    }

    private static void ShowConnectedStatus(
        int? instanceIndex,
        ControlPanelWorkerRunProgress? progress)
    {
        RecordingStatusOverlay.SetStatusPanelVisible(true);
        var instanceDetail = instanceIndex.HasValue
            ? "Instance " + (instanceIndex.Value + 1) + " online"
            : "Control panel online";
        var progressDetail = FormatProgressDetail(progress);
        RecordingStatusOverlay.ShowConnected(
            string.IsNullOrWhiteSpace(progressDetail)
                ? instanceDetail
                : instanceDetail + " | " + progressDetail);
    }

    private static string FormatProgressDetail(ControlPanelWorkerRunProgress? progress)
    {
        if (progress == null || progress.TotalCount <= 0)
        {
            return "";
        }

        var completed = Math.Max(0, progress.CompletedCount);
        var failed = Math.Max(0, progress.FailedCount);
        var total = Math.Max(1, progress.TotalCount);
        var finished = Math.Min(total, completed + failed);
        if (!progress.IsRunning && finished == 0)
        {
            return total + " queued";
        }

        var detail = failed > 0
            ? finished + "/" + total + " finished, " + completed + " completed, " + failed + " failed"
            : completed + "/" + total + " completed";
        if (!progress.IsRunning && finished >= total)
        {
            detail = failed > 0
                ? "Run finished: " + detail
                : "Run complete: " + detail;
        }

        if (!string.IsNullOrWhiteSpace(progress.Status) &&
            !string.Equals(progress.Status, "Running", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(progress.Status, "Idle", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(progress.Status, "Complete", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(progress.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            detail += " (" + progress.Status + ")";
        }

        return detail;
    }

    private static async Task ObserveTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task DelayIgnoringCancellationAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string CreateDefaultWorkerName()
    {
        var gameRootName = Path.GetFileName(GamePaths.GetGameRoot().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(gameRootName)
            ? Environment.MachineName
            : gameRootName;
    }

    private static string GetPluginVersion()
    {
        return typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
               typeof(Plugin).Assembly.GetName().Version?.ToString() ??
               "unknown";
    }

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
