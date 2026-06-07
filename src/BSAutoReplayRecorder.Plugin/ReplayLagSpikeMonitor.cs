using System;
using System.Threading;
using System.Threading.Tasks;
using BSAutoReplayRecorder.Core;
using UnityEngine;
using IpaLogger = IPA.Logging.Logger;

namespace BSAutoReplayRecorder.Plugin;

internal sealed class ReplayLagSpikeMonitor : IDisposable
{
    private readonly LagSpikeMonitorBehaviour? _behaviour;

    private ReplayLagSpikeMonitor(LagSpikeMonitorBehaviour? behaviour)
    {
        _behaviour = behaviour;
    }

    public static ReplayLagSpikeMonitor Create(BatchRecorderSettings settings, IpaLogger logger)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!settings.LagSpikeDetectionEnabled)
        {
            return new ReplayLagSpikeMonitor(null);
        }

        var threshold = TimeSpan.FromMilliseconds(Math.Max(1, settings.LagSpikeThresholdMilliseconds));
        var consecutiveFrames = Math.Max(1, settings.LagSpikeConsecutiveFrameCount);
        var startupGrace = TimeSpan.FromSeconds(Math.Max(0, settings.LagSpikeStartupGraceSeconds));
        var gameObject = new GameObject("Auto Replay Recorder Lag Spike Monitor");
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        var behaviour = gameObject.AddComponent<LagSpikeMonitorBehaviour>();
        behaviour.Configure(threshold, consecutiveFrames, startupGrace, logger);
        return new ReplayLagSpikeMonitor(behaviour);
    }

    public void StartMonitoring()
    {
        _behaviour?.StartMonitoring();
    }

    public async Task<LagSpikeDetectedException?> WaitForLagSpikeAsync(CancellationToken cancellationToken)
    {
        if (_behaviour == null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var spikeTask = _behaviour.SpikeDetectedTask;
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var completed = await Task.WhenAny(spikeTask, cancellationTask).ConfigureAwait(false);
        if (completed == spikeTask)
        {
            try
            {
                return await spikeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    public void ThrowIfLagSpikeDetected()
    {
        if (_behaviour == null || !_behaviour.SpikeDetectedTask.IsCompleted)
        {
            return;
        }

        throw _behaviour.SpikeDetectedTask.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_behaviour != null)
        {
            _behaviour.StopMonitoring();
            UnityEngine.Object.Destroy(_behaviour.gameObject);
        }
    }
}

internal sealed class LagSpikeDetectedException : Exception
{
    public LagSpikeDetectedException(TimeSpan frameTime, TimeSpan threshold, int consecutiveFrameCount)
        : base(
            "Lag spike detected during replay recording: frame time " +
            Math.Round(frameTime.TotalMilliseconds, 1) +
            "ms exceeded " +
            Math.Round(threshold.TotalMilliseconds, 1) +
            "ms" +
            (consecutiveFrameCount > 1
                ? " for " + consecutiveFrameCount + " consecutive frames"
                : "") +
            ". Recording is invalid.")
    {
        FrameTime = frameTime;
        Threshold = threshold;
        ConsecutiveFrameCount = consecutiveFrameCount;
    }

    public TimeSpan FrameTime { get; }

    public TimeSpan Threshold { get; }

    public int ConsecutiveFrameCount { get; }
}

internal sealed class LagSpikeMonitorBehaviour : MonoBehaviour
{
    private readonly TaskCompletionSource<LagSpikeDetectedException> _spikeDetected =
        new TaskCompletionSource<LagSpikeDetectedException>();
    private TimeSpan _threshold;
    private TimeSpan _startupGrace;
    private int _requiredConsecutiveFrames;
    private int _consecutiveSpikeFrames;
    private bool _monitoringStarted;
    private float? _monitoringStartedAt;
    private bool _hasSkippedFirstFrame;
    private IpaLogger? _logger;

    public Task<LagSpikeDetectedException> SpikeDetectedTask => _spikeDetected.Task;

    public void Configure(
        TimeSpan threshold,
        int requiredConsecutiveFrames,
        TimeSpan startupGrace,
        IpaLogger logger)
    {
        _threshold = threshold;
        _requiredConsecutiveFrames = Math.Max(1, requiredConsecutiveFrames);
        _startupGrace = startupGrace;
        _logger = logger;
    }

    public void StartMonitoring()
    {
        _consecutiveSpikeFrames = 0;
        _hasSkippedFirstFrame = false;
        _monitoringStartedAt = null;
        _monitoringStarted = true;
    }

    public void StopMonitoring()
    {
        _spikeDetected.TrySetCanceled();
    }

    private void Update()
    {
        if (!_monitoringStarted)
        {
            return;
        }

        if (!_monitoringStartedAt.HasValue)
        {
            _monitoringStartedAt = Time.realtimeSinceStartup;
        }

        if (_startupGrace > TimeSpan.Zero &&
            Time.realtimeSinceStartup - _monitoringStartedAt.Value < _startupGrace.TotalSeconds)
        {
            _consecutiveSpikeFrames = 0;
            return;
        }

        if (!_hasSkippedFirstFrame)
        {
            _hasSkippedFirstFrame = true;
            return;
        }

        if (_spikeDetected.Task.IsCompleted)
        {
            return;
        }

        var frameTime = TimeSpan.FromSeconds(Time.unscaledDeltaTime);
        if (frameTime <= _threshold)
        {
            _consecutiveSpikeFrames = 0;
            return;
        }

        _consecutiveSpikeFrames++;
        if (_consecutiveSpikeFrames < _requiredConsecutiveFrames)
        {
            return;
        }

        var exception = new LagSpikeDetectedException(frameTime, _threshold, _consecutiveSpikeFrames);
        _logger?.Warn(exception.Message);
        _spikeDetected.TrySetResult(exception);
    }
}
