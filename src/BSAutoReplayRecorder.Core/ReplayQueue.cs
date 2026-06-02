using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BSAutoReplayRecorder.Core.Replay;

namespace BSAutoReplayRecorder.Core;

public sealed class ReplayQueue
{
    private readonly BsorInfoReader _reader;

    public ReplayQueue()
        : this(new BsorInfoReader())
    {
    }

    public ReplayQueue(BsorInfoReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
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
            .EnumerateFiles(options.InputDirectory, "*.bsor", searchOption)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<ReplayQueueItem>();
        var failures = new List<ReplayQueueLoadFailure>();

        foreach (var replayPath in replayPaths)
        {
            try
            {
                var info = _reader.Read(replayPath);
                items.Add(new ReplayQueueItem(items.Count + 1, replayPath, info));
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
}

