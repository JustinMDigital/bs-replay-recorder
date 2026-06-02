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

    public async Task<ReplayPlaybackSession> StartReplayAsync(
        ReplayQueueItem queueItem,
        ReplayReference replayReference,
        CancellationToken cancellationToken)
    {
        var replay = await LoadLaunchableReplayAsync(replayReference, cancellationToken).ConfigureAwait(false);

        _logger.Info("Launching BeatLeader replay: " + replay.info.songName + " [" + replay.info.difficulty + "]");

        Action? finishedCallback = () =>
        {
            _logger.Info("BeatLeader replay finished callback fired for: " + replay.info.songName);
        };

        var loader = GetLoader();
        await loader
            .StartReplayAsync(replay, null!, ReplayerSettings.UserSettings, finishedCallback, cancellationToken)
            .ConfigureAwait(false);

        return new ReplayPlaybackSession(queueItem, replayReference, DateTimeOffset.Now);
    }

    public async Task ValidateReplayAsync(ReplayReference replayReference, CancellationToken cancellationToken)
    {
        await LoadLaunchableReplayAsync(replayReference, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Replay> LoadLaunchableReplayAsync(
        ReplayReference replayReference,
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

        var loader = GetLoader();

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

        var loadResult = await loader
            .LoadBeatmapAsync(hash, replay.info.mode, replay.info.difficulty, cancellationToken)
            .ConfigureAwait(false);

        _logger.Warn(
            "BeatLeader beatmap probe for hash '" + hash +
            "': levelFound=" + (loadResult.Item1 != null) +
            ", keyFound=" + loadResult.Item2.HasValue + ".");
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

    private static ReplayerMenuLoader GetLoader()
    {
        var loader = ReplayerMenuLoader.Instance;
        if (loader == null)
        {
            throw new InvalidOperationException("BeatLeader ReplayerMenuLoader.Instance is not available yet.");
        }

        return loader;
    }
}
