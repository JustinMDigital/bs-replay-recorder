using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models;
using BeatLeader.Models.Replay;
using BeatLeader.Replayer;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Playback;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

public sealed class BeatLeaderReplayPlaybackDriver : IReplayPlaybackDriver
{
    private static readonly TimeSpan FinishEventStartupGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LoaderReadyTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LoaderReadyPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly Logger _logger;

    public BeatLeaderReplayPlaybackDriver(Logger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string DriverName => "BeatLeader";

    public bool CanPlay(ReplayReference replayReference)
    {
        if (replayReference == null)
        {
            return false;
        }

        return replayReference.Provider == ReplayProvider.BeatLeader &&
               replayReference.Kind == ReplayReferenceKind.LocalBsorFile &&
               replayReference.LocalPath != null;
    }

    public IReplayPlaybackWait CreateStartWait()
    {
        return new ReplayStartWait();
    }

    public IReplayPlaybackWait CreateFinishWait()
    {
        return new ReplayFinishWait(_logger);
    }

    public async Task<ReplayPlaybackSession> StartReplayAsync(
        ReplayQueueItem queueItem,
        ReplayReference replayReference,
        CancellationToken cancellationToken)
    {
        var loader = await WaitForLoaderAsync(cancellationToken).ConfigureAwait(false);
        var replay = await LoadLaunchableReplayAsync(replayReference, loader, cancellationToken).ConfigureAwait(false);

        _logger.Info("Launching BeatLeader replay: " + replay.info.songName + " [" + replay.info.difficulty + "]");

        Action? finishedCallback = () =>
        {
            _logger.Info("BeatLeader replay finished callback fired for: " + replay.info.songName);
        };

        await BeatLeaderCompatibility
            .StartReplayAsync(loader, replay, ReplayerSettings.UserSettings, finishedCallback, cancellationToken)
            .ConfigureAwait(false);

        return new ReplayPlaybackSession(queueItem, replayReference, DateTimeOffset.Now);
    }

    public async Task ValidateReplayAsync(ReplayReference replayReference, CancellationToken cancellationToken)
    {
        var loader = await WaitForLoaderAsync(cancellationToken).ConfigureAwait(false);
        await LoadLaunchableReplayAsync(replayReference, loader, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Replay> LoadLaunchableReplayAsync(
        ReplayReference replayReference,
        ReplayerMenuLoader loader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanPlay(replayReference))
        {
            throw new InvalidOperationException("Replay reference is not a local BeatLeader .bsor file.");
        }

        var replayPath = replayReference.LocalPath!;
        if (!File.Exists(replayPath))
        {
            throw new FileNotFoundException("Replay file was not found.", replayPath);
        }

        var replayBytes = File.ReadAllBytes(replayPath);
        var replay = ReplayDecoder.DecodeReplay(replayBytes);

        if (await CanLaunchWithHashAsync(loader, replay, replay.info.hash, cancellationToken, logProbe: false)
                .ConfigureAwait(false))
        {
            return replay;
        }

        var originalHash = replay.info.hash;
        _logger.Warn(
            "BeatLeader rejected replay hash '" + originalHash +
            "' for " + replay.info.songName + " [" + replay.info.difficulty +
            ", " + replay.info.mode + "]. Probing fallback hashes.");

        foreach (var fallbackHash in CreateFallbackHashCandidates(originalHash))
        {
            if (await CanLaunchWithHashAsync(loader, replay, fallbackHash, cancellationToken, logProbe: true)
                    .ConfigureAwait(false))
            {
                _logger.Warn(
                    "BeatLeader accepted fallback replay hash '" + fallbackHash +
                    "' for original hash '" + originalHash + "'.");
                return replay;
            }
        }

        replay.info.hash = originalHash;
        await LogBeatmapProbeAsync(loader, replay, originalHash, cancellationToken).ConfigureAwait(false);

        throw new InvalidOperationException(
            "BeatLeader cannot launch replay. The map may be missing or the replay metadata may not match an installed map.");
    }

    private async Task<bool> CanLaunchWithHashAsync(
        ReplayerMenuLoader loader,
        Replay replay,
        string hash,
        CancellationToken cancellationToken,
        bool logProbe)
    {
        cancellationToken.ThrowIfCancellationRequested();

        replay.info.hash = hash;
        var canLaunch = await loader.CanLaunchReplay(replay.info).ConfigureAwait(false);
        if (logProbe)
        {
            _logger.Info("BeatLeader launch probe hash '" + hash + "': canLaunch=" + canLaunch + ".");
            if (!canLaunch)
            {
                await LogBeatmapProbeAsync(loader, replay, hash, cancellationToken).ConfigureAwait(false);
            }
        }

        return canLaunch;
    }

    private async Task LogBeatmapProbeAsync(
        ReplayerMenuLoader loader,
        Replay replay,
        string hash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        object? loadResult = await loader
            .LoadBeatmapAsync(hash, replay.info.mode, replay.info.difficulty, cancellationToken)
            .ConfigureAwait(false);
        var result = BeatLeaderCompatibility.InspectBeatmapLoadResult(loadResult);

        _logger.Warn(
            "BeatLeader beatmap probe for hash '" + hash +
            "': levelFound=" + result.LevelFound +
            ", keyFound=" + result.KeyFound + ".");
    }

    private static IReadOnlyList<string> CreateFallbackHashCandidates(string hash)
    {
        var candidates = new List<string>();
        AddCandidate(candidates, "custom_level_" + hash);

        var leadingSha1 = TryGetLeadingSha1(hash);
        if (!string.IsNullOrEmpty(leadingSha1))
        {
            AddCandidate(candidates, leadingSha1!);
            AddCandidate(candidates, "custom_level_" + leadingSha1);
        }

        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        foreach (var existing in candidates)
        {
            if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        candidates.Add(candidate);
    }

    private static string? TryGetLeadingSha1(string hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length < 40)
        {
            return null;
        }

        for (var index = 0; index < 40; index++)
        {
            if (!IsHex(hash[index]))
            {
                return null;
            }
        }

        return hash.Substring(0, 40);
    }

    private static bool IsHex(char value)
    {
        return value >= '0' && value <= '9' ||
               value >= 'a' && value <= 'f' ||
               value >= 'A' && value <= 'F';
    }

    private async Task<ReplayerMenuLoader> WaitForLoaderAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + LoaderReadyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loader = ReplayerMenuLoader.Instance;
            if (loader != null)
            {
                return loader;
            }

            await Task.Delay(LoaderReadyPollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "BeatLeader's replay menu did not finish loading within 45 seconds. " +
            "Leave the recorder worker at the Beat Saber main menu, then start the run again.");
    }

    private sealed class ReplayStartWait : IReplayPlaybackWait
    {
        private readonly TaskCompletionSource<DateTimeOffset> _completion = new TaskCompletionSource<DateTimeOffset>();

        public ReplayStartWait()
        {
            ReplayerLauncher.ReplayWasStartedEvent += HandleReplayStarted;
        }

        public async Task<DateTimeOffset?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(_completion.Task, timeoutTask).ConfigureAwait(false);
            if (completed == _completion.Task)
            {
                return await _completion.Task.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        public void Dispose()
        {
            ReplayerLauncher.ReplayWasStartedEvent -= HandleReplayStarted;
        }

        private void HandleReplayStarted(ReplayLaunchData launchData)
        {
            _completion.TrySetResult(DateTimeOffset.UtcNow);
        }
    }

    private sealed class ReplayFinishWait : IReplayPlaybackWait
    {
        private readonly TaskCompletionSource<DateTimeOffset> _completion = new TaskCompletionSource<DateTimeOffset>();
        private readonly Logger _logger;
        private readonly object _sync = new object();
        private DateTimeOffset? _acceptFinishAfterUtc;
        private bool _loggedUnarmedFinishEvent;
        private int _ignoredStartupFinishEvents;

        public ReplayFinishWait(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ReplayerLauncher.ReplayWasFinishedEvent += HandleReplayFinished;
        }

        public async Task<DateTimeOffset?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_acceptFinishAfterUtc.HasValue)
                {
                    _acceptFinishAfterUtc = DateTimeOffset.UtcNow + FinishEventStartupGrace;
                }
            }

            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(_completion.Task, timeoutTask).ConfigureAwait(false);
            if (completed == _completion.Task)
            {
                return await _completion.Task.ConfigureAwait(false);
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
            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? acceptFinishAfterUtc;
            int ignoredStartupFinishEvents = 0;

            lock (_sync)
            {
                acceptFinishAfterUtc = _acceptFinishAfterUtc;
                if (!acceptFinishAfterUtc.HasValue)
                {
                    if (!_loggedUnarmedFinishEvent)
                    {
                        _loggedUnarmedFinishEvent = true;
                        _logger.Warn("Ignoring BeatLeader replay finish event before finish wait was armed.");
                    }

                    return;
                }

                if (now < acceptFinishAfterUtc.Value)
                {
                    _ignoredStartupFinishEvents++;
                    ignoredStartupFinishEvents = _ignoredStartupFinishEvents;
                }
            }

            if (acceptFinishAfterUtc.HasValue && now < acceptFinishAfterUtc.Value)
            {
                var remainingSeconds = Math.Ceiling((acceptFinishAfterUtc.Value - now).TotalSeconds);
                if (ignoredStartupFinishEvents <= 3 || ignoredStartupFinishEvents % 10 == 0)
                {
                    _logger.Warn(
                        "Ignoring early BeatLeader replay finish event during startup grace; " +
                        remainingSeconds + " second(s) remain before finish events are trusted.");
                }

                return;
            }

            _completion.TrySetResult(DateTimeOffset.UtcNow);
        }
    }
}
