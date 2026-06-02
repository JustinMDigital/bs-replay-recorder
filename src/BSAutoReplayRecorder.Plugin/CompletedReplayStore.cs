using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BSAutoReplayRecorder.Core;
using IPA.Logging;
using Newtonsoft.Json;

namespace BSAutoReplayRecorder.Plugin;

public sealed class CompletedReplayStore
{
    private readonly string _path;
    private readonly Logger _logger;
    private CompletedReplayState _state;
    private HashSet<string> _keys;

    private CompletedReplayStore(string path, Logger logger, CompletedReplayState state)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _state = state ?? new CompletedReplayState();
        _keys = new HashSet<string>(
            _state.CompletedReplays.Select(record => record.Key),
            StringComparer.OrdinalIgnoreCase);
    }

    public static CompletedReplayStore Load(BatchRecorderSettings settings, Logger logger)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var path = GamePaths.ResolveGamePath(settings.CompletedReplayStatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GamePaths.GetRecorderUserDataDirectory());

        if (!File.Exists(path))
        {
            return new CompletedReplayStore(path, logger, new CompletedReplayState());
        }

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonConvert.DeserializeObject<CompletedReplayState>(json) ?? new CompletedReplayState();
            return new CompletedReplayStore(path, logger, state);
        }
        catch (Exception ex) when (ex is IOException || ex is JsonException)
        {
            logger.Warn("Could not load completed replay state from " + path + ": " + ex);
            return new CompletedReplayStore(path, logger, new CompletedReplayState());
        }
    }

    public bool IsCompleted(ReplayQueueItem item)
    {
        return _keys.Contains(CreateKey(item));
    }

    public int Count => _state.CompletedReplays.Count;

    public IReadOnlyList<ReplayQueueItem> FilterCompleted(IReadOnlyList<ReplayQueueItem> items)
    {
        return items.Where(item => !IsCompleted(item)).ToList();
    }

    public void MarkCompleted(ReplayQueueItem item, string? outputPath)
    {
        var key = CreateKey(item);
        if (_keys.Contains(key))
        {
            return;
        }

        var info = item.ReplayInfo;
        _state.CompletedReplays.Add(new CompletedReplayRecord
        {
            Key = key,
            ReplayPath = item.ReplayPath,
            OutputPath = outputPath ?? "",
            SongName = info.SongName,
            Difficulty = info.Difficulty,
            LevelHash = info.LevelHash,
            PlayerId = info.PlayerId,
            Timestamp = info.Timestamp,
            CompletedAtUtc = DateTime.UtcNow.ToString("O")
        });
        _keys.Add(key);
        Save();
    }

    public void Clear()
    {
        _state = new CompletedReplayState();
        _keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Save();
    }

    public string CreateKey(ReplayQueueItem item)
    {
        var info = item.ReplayInfo;
        return string.Join(
            "|",
            info.PlayerId,
            info.LevelHash,
            info.Difficulty,
            info.Mode,
            info.Timestamp,
            info.Score.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonConvert.SerializeObject(_state, Formatting.Indented));
        }
        catch (IOException ex)
        {
            _logger.Error("Could not save completed replay state to " + _path + ": " + ex);
        }
    }

    private sealed class CompletedReplayState
    {
        public List<CompletedReplayRecord> CompletedReplays { get; set; } = new List<CompletedReplayRecord>();
    }

    private sealed class CompletedReplayRecord
    {
        public string Key { get; set; } = "";

        public string ReplayPath { get; set; } = "";

        public string OutputPath { get; set; } = "";

        public string SongName { get; set; } = "";

        public string Difficulty { get; set; } = "";

        public string LevelHash { get; set; } = "";

        public string PlayerId { get; set; } = "";

        public string Timestamp { get; set; } = "";

        public string CompletedAtUtc { get; set; } = "";
    }
}
