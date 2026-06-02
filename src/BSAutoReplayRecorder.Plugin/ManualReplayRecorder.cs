using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Obs;
using BSAutoReplayRecorder.Core.Replay;
using BSAutoReplayRecorder.Core.Utility;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

public sealed class ManualReplayRecorder : IDisposable
{
    private readonly BatchRecorderSettings _settings;
    private readonly ObsWebSocketRecorder _obsRecorder;
    private readonly Logger _logger;
    private readonly CancellationToken _shutdownToken;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private ManualRecordingSession? _session;
    private bool _disposed;

    public ManualReplayRecorder(
        BatchRecorderSettings settings,
        ObsWebSocketRecorder obsRecorder,
        Logger logger,
        CancellationToken shutdownToken)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _obsRecorder = obsRecorder ?? throw new ArgumentNullException(nameof(obsRecorder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _shutdownToken = shutdownToken;
    }

    public void HandleReplayStarted(ReplayLaunchData launchData)
    {
        if (!_settings.RecordManualReplayStarts)
        {
            return;
        }

        _ = Task.Run(() => StartAsync(launchData, _shutdownToken), _shutdownToken);
    }

    public void HandleReplayFinished(ReplayLaunchData launchData)
    {
        if (!_settings.RecordManualReplayStarts)
        {
            return;
        }

        _ = Task.Run(() => StopAsync(launchData), CancellationToken.None);
    }

    public void Dispose()
    {
        _disposed = true;
        _gate.Dispose();
    }

    private async Task StartAsync(ReplayLaunchData launchData, CancellationToken cancellationToken)
    {
        if (_settings.DryRun)
        {
            _logger.Info("Manual replay recording is enabled, but DryRun=true, so OBS was not started.");
            RecordingStatusOverlay.ShowToast(
                "Manual replay recording",
                "DryRun=true, OBS was not started",
                TimeSpan.FromSeconds(5));
            return;
        }

        var plan = CreatePlan(launchData);
        var session = new ManualRecordingSession(launchData, plan);
        RecordingStatusOverlay.ShowManualStarting(plan.OutputBaseName);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            if (_session != null)
            {
                _logger.Warn("Manual replay recording skipped because another manual replay session is active.");
                return;
            }

            _session = session;
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            var status = await _obsRecorder.GetRecordingStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.OutputActive)
            {
                _logger.Info("Manual replay recording skipped because OBS is already recording.");
                RecordingStatusOverlay.ShowToast(
                    "Manual replay skipped",
                    "OBS is already recording",
                    TimeSpan.FromSeconds(5));
                await ClearSessionAsync(session, cancellationToken).ConfigureAwait(false);
                return;
            }

            _logger.Info("Manual replay recording starting OBS for: " + plan.OutputBaseName);
            await _obsRecorder.StartRecordingAsync(plan, cancellationToken).ConfigureAwait(false);

            if (!await ConfirmObsRecordingStartedAsync(plan, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("OBS accepted StartRecord for manual replay, but GetRecordStatus never reported an active recording.");
            }

            var shouldStop = false;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_session == session)
                {
                    session.RecordingStarted = true;
                    RecordingStatusOverlay.ShowManualRecording(plan.OutputBaseName);
                    shouldStop = session.FinishRequested;
                    if (shouldStop)
                    {
                        _session = null;
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            if (shouldStop)
            {
                await StopSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Manual replay recording failed to start: " + ex);
            RecordingStatusOverlay.ShowManualFailed(plan.OutputBaseName);
            await ClearSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task StopAsync(ReplayLaunchData launchData)
    {
        ManualRecordingSession? session = null;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed || _session == null)
            {
                return;
            }

            if (!ReferenceEquals(_session.LaunchData, launchData))
            {
                _logger.Warn("Manual replay finish event did not match the active manual recording session.");
                return;
            }

            if (!_session.RecordingStarted)
            {
                _session.FinishRequested = true;
                return;
            }

            session = _session;
            _session = null;
        }
        finally
        {
            _gate.Release();
        }

        if (session != null)
        {
            await StopSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task StopSessionAsync(ManualRecordingSession session, CancellationToken cancellationToken)
    {
        try
        {
            await DelayIfNeededAsync(TimeSpan.FromSeconds(Math.Max(0, _settings.PostRollSeconds)), cancellationToken)
                .ConfigureAwait(false);

            var stopResult = await _obsRecorder.StopRecordingAsync(session.Plan, cancellationToken).ConfigureAwait(false);
            var outputPath = await MoveRecordingOutputAsync(session.Plan, stopResult.OutputPath, cancellationToken)
                .ConfigureAwait(false);
            _logger.Info("Manual replay recording finished: " + session.Plan.OutputBaseName +
                         (string.IsNullOrEmpty(outputPath) ? "" : ". OBS output: " + outputPath));
            RecordingStatusOverlay.ShowManualFinished(session.Plan.OutputBaseName);
        }
        catch (Exception ex)
        {
            _logger.Error("Manual replay recording failed to stop: " + ex);
            RecordingStatusOverlay.ShowManualFailed(session.Plan.OutputBaseName);
        }
    }

    private async Task<bool> ConfirmObsRecordingStartedAsync(RecordingPlan plan, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);

            var status = await _obsRecorder.GetRecordingStatusAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("OBS recording status after manual StartRecord for " + plan.OutputBaseName +
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

    private async Task ClearSessionAsync(ManualRecordingSession session, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session == session)
            {
                _session = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private RecordingPlan CreatePlan(ReplayLaunchData launchData)
    {
        var songName = ReadStringProperty(launchData.BeatmapLevel, "songName") ??
                       ReadStringProperty(launchData.MainReplay?.ReplayData?.Player, "name") ??
                       "Manual Replay";
        var mapper = ReadStringProperty(launchData.BeatmapLevel, "levelAuthorName") ?? "";
        var difficulty = ReadValueProperty(launchData.BeatmapKey, "difficulty") ?? "Unknown";
        var hash = ReadStringProperty(launchData.BeatmapLevel, "levelID") ?? "";

        var info = new BsorInfo
        {
            SongName = songName,
            Mapper = mapper,
            Difficulty = difficulty,
            LevelHash = hash
        };

        var item = new ReplayQueueItem(1, "manual-replay", info);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        var outputBaseName = FileNameSanitizer.SanitizeBaseName(
            "manual - " + songName + " [" + difficulty + "] - " + timestamp);

        return new RecordingPlan(item, outputBaseName, TimeSpan.Zero, TimeSpan.Zero);
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

        var targetPath = PrepareTargetPath(
            Path.Combine(outputDirectory, plan.OutputBaseName + extension),
            _settings.OverwriteExistingRecordings);

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

    private static Task DelayIfNeededAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

    private static string? ReadStringProperty(object? source, string propertyName)
    {
        return ReadValueProperty(source, propertyName);
    }

    private static string? ReadValueProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return null;
        }

        var property = source.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        var value = property?.GetValue(source);
        return string.IsNullOrWhiteSpace(value?.ToString()) ? null : value.ToString();
    }

    private sealed class ManualRecordingSession
    {
        public ManualRecordingSession(ReplayLaunchData launchData, RecordingPlan plan)
        {
            LaunchData = launchData;
            Plan = plan;
        }

        public ReplayLaunchData LaunchData { get; }

        public RecordingPlan Plan { get; }

        public bool RecordingStarted { get; set; }

        public bool FinishRequested { get; set; }
    }
}
