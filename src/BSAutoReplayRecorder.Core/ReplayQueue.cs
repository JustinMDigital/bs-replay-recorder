using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BSAutoReplayRecorder.Core.Replay;

namespace BSAutoReplayRecorder.Core;

public sealed class ReplayQueue
{
    private readonly BsorInfoReader _bsorReader;
    private readonly ScoreSaberReplayInfoReader _scoreSaberReader;

    public ReplayQueue()
        : this(new BsorInfoReader(), new ScoreSaberReplayInfoReader())
    {
    }

    public ReplayQueue(BsorInfoReader reader)
        : this(reader, new ScoreSaberReplayInfoReader())
    {
    }

    public ReplayQueue(BsorInfoReader reader, ScoreSaberReplayInfoReader scoreSaberReader)
    {
        _bsorReader = reader ?? throw new ArgumentNullException(nameof(reader));
        _scoreSaberReader = scoreSaberReader ?? throw new ArgumentNullException(nameof(scoreSaberReader));
    }

    public ReplayQueueLoadResult Load(ReplayQueueOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!Directory.Exists(options.InputDirectory))
        {
            throw new DirectoryNotFoundException("Replay input directory was not found: " + options.InputDirectory);
        }

        var searchOption = options.IncludeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var replayPaths = Directory
            .EnumerateFiles(options.InputDirectory, "*.*", searchOption)
            .Where(IsSupportedReplayFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<ReplayQueueItem>();
        var failures = new List<ReplayQueueLoadFailure>();

        foreach (var replayPath in replayPaths)
        {
            try
            {
                var item = CreateItem(items.Count + 1, replayPath);
                items.Add(item);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is EndOfStreamException)
            {
                failures.Add(new ReplayQueueLoadFailure(replayPath, ex.Message));
                if (!options.SkipInvalidReplays)
                {
                    throw;
                }
            }
        }

        return new ReplayQueueLoadResult(items, failures);
    }

    private ReplayQueueItem CreateItem(int sequenceNumber, string replayPath)
    {
        var extension = Path.GetExtension(replayPath);
        if (string.Equals(extension, ".bsor", StringComparison.OrdinalIgnoreCase))
        {
            return new ReplayQueueItem(
                sequenceNumber,
                replayPath,
                _bsorReader.Read(replayPath),
                ReplayProvider.BeatLeader,
                ReplayReferenceKind.LocalBsorFile,
                null,
                null);
        }

        if (string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase))
        {
            var info = _scoreSaberReader.Read(replayPath);
            return new ReplayQueueItem(
                sequenceNumber,
                replayPath,
                info,
                ReplayProvider.ScoreSaber2,
                ReplayReferenceKind.LocalScoreSaberDatFile,
                null,
                info.ScoreId);
        }

        throw new InvalidDataException("Unsupported replay file extension: " + extension);
    }

    private static bool IsSupportedReplayFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".bsor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase);
    }
}
