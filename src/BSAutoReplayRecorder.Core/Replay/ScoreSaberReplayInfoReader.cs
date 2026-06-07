using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BSAutoReplayRecorder.Core.Replay;

public sealed class ScoreSaberReplayInfoReader
{
    private static readonly byte[] HeaderBytes = Encoding.UTF8.GetBytes("ScoreSaber Replay");
    private static readonly TimeSpan DefaultEstimatedPlaybackLength = TimeSpan.FromMinutes(3);
    private const string DownloadedReplayPrefix = "scoresaber-";

    public BsorInfo Read(string replayPath)
    {
        if (string.IsNullOrWhiteSpace(replayPath))
        {
            throw new ArgumentException("Replay path is required.", nameof(replayPath));
        }

        using (var stream = File.OpenRead(replayPath))
        {
            ValidateHeader(stream);
        }

        var info = TryReadInfoFromFileName(replayPath) ?? new BsorInfo();
        if (info.LastFrameTime <= 0 && info.FailTime <= 0)
        {
            info.LastFrameTime = (float)DefaultEstimatedPlaybackLength.TotalSeconds;
        }

        return info;
    }

    public void Validate(string replayPath)
    {
        if (string.IsNullOrWhiteSpace(replayPath))
        {
            throw new ArgumentException("Replay path is required.", nameof(replayPath));
        }

        using (var stream = File.OpenRead(replayPath))
        {
            ValidateHeader(stream);
        }
    }

    private static void ValidateHeader(Stream stream)
    {
        var buffer = new byte[HeaderBytes.Length];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read != buffer.Length)
        {
            throw new InvalidDataException("The file is not a ScoreSaber replay. Header was incomplete.");
        }

        for (var index = 0; index < HeaderBytes.Length; index++)
        {
            if (buffer[index] != HeaderBytes[index])
            {
                throw new InvalidDataException("The file is not a ScoreSaber replay. Header did not match.");
            }
        }
    }

    private static BsorInfo? TryReadInfoFromFileName(string replayPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(replayPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var fileNameWithoutDuplicateSuffix = StripDuplicateSuffix(fileName);
        var lastDash = fileNameWithoutDuplicateSuffix.LastIndexOf('-');
        if (lastDash <= 0 || lastDash == fileNameWithoutDuplicateSuffix.Length - 1)
        {
            return null;
        }

        var hash = fileNameWithoutDuplicateSuffix.Substring(lastDash + 1);
        if (!LooksLikeSha1(hash))
        {
            return null;
        }

        var beforeHash = fileNameWithoutDuplicateSuffix.Substring(0, lastDash);
        var modeDash = beforeHash.LastIndexOf('-');
        if (modeDash <= 0 || modeDash == beforeHash.Length - 1)
        {
            return null;
        }

        var mode = beforeHash.Substring(modeDash + 1);
        var beforeMode = beforeHash.Substring(0, modeDash);
        var difficultyDash = beforeMode.LastIndexOf('-');
        if (difficultyDash <= 0 || difficultyDash == beforeMode.Length - 1)
        {
            return null;
        }

        var difficulty = beforeMode.Substring(difficultyDash + 1);
        var beforeDifficulty = beforeMode.Substring(0, difficultyDash);
        var playerDash = beforeDifficulty.IndexOf('-');
        if (playerDash <= 0 || playerDash == beforeDifficulty.Length - 1)
        {
            return null;
        }

        var firstToken = beforeDifficulty.Substring(0, playerDash);
        if (string.Equals(firstToken, DownloadedReplayPrefix.TrimEnd('-'), StringComparison.OrdinalIgnoreCase))
        {
            var downloadedParts = beforeHash.Substring(DownloadedReplayPrefix.Length)
                .Split('-')
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
            if (downloadedParts.Length < 3)
            {
                return null;
            }

            var scoreId = downloadedParts[0];
            var playerName = downloadedParts[1];
            if (string.IsNullOrWhiteSpace(playerName))
            {
                return null;
            }

            var songNameWithMetadata = string.Join("-", downloadedParts, 2, downloadedParts.Length - 2);
            if (string.IsNullOrWhiteSpace(songNameWithMetadata))
            {
                return null;
            }

            var estimatedSecondsFromPrefixName = ParseEstimatedSecondsFromSongName(ref songNameWithMetadata);
            var songNameTokens = songNameWithMetadata.Split('-');
            var parsedDifficulty = songNameTokens.Length >= 2 ? songNameTokens[songNameTokens.Length - 2] : "";
            var parsedMode = songNameTokens.Length >= 2 ? songNameTokens[songNameTokens.Length - 1] : "";
            var parsedSongName = songNameTokens.Length > 2
                ? string.Join("-", songNameTokens, 0, songNameTokens.Length - 2)
                : "";

            return new BsorInfo
            {
                FileVersion = "ScoreSaber",
                ScoreId = scoreId,
                PlayerName = playerName,
                SongName = parsedSongName,
                Difficulty = parsedDifficulty,
                Mode = parsedMode,
                LevelHash = hash,
                LastFrameTime = estimatedSecondsFromPrefixName > 0
                    ? (float)estimatedSecondsFromPrefixName
                    : (float)DefaultEstimatedPlaybackLength.TotalSeconds,
                Speed = 1
            };
        }

        var rawSongName = beforeDifficulty.Substring(playerDash + 1);
        var estimatedSecondsFromFallbackName = ParseEstimatedSecondsFromSongName(ref rawSongName);
        var tokens = rawSongName.Split('-');
        if (estimatedSecondsFromFallbackName > 0 && IsLikelySteamId(firstToken) && tokens.Length >= 1 && !string.IsNullOrWhiteSpace(tokens[0]))
        {
            var playerName = tokens[0];
            return new BsorInfo
            {
                FileVersion = "ScoreSaber",
                PlayerId = firstToken,
                PlayerName = playerName,
                SongName = tokens.Length > 1 ? string.Join("-", tokens, 1, tokens.Length - 1) : "",
                Difficulty = difficulty,
                Mode = mode,
                LevelHash = hash,
                LastFrameTime = estimatedSecondsFromFallbackName > 0
                    ? (float)estimatedSecondsFromFallbackName
                    : (float)DefaultEstimatedPlaybackLength.TotalSeconds,
                Speed = 1
            };
        }

        return new BsorInfo
        {
            FileVersion = "ScoreSaber",
            PlayerId = firstToken,
            SongName = rawSongName,
            Difficulty = difficulty,
            Mode = mode,
            LevelHash = hash,
            LastFrameTime = (float)DefaultEstimatedPlaybackLength.TotalSeconds,
            Speed = 1
        };
    }

    private static bool LooksLikeSha1(string value)
    {
        if (value.Length != 40)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (!((ch >= '0' && ch <= '9') ||
                  (ch >= 'a' && ch <= 'f') ||
                  (ch >= 'A' && ch <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private static string StripDuplicateSuffix(string value)
    {
        var suffixStart = value.LastIndexOf(" (", StringComparison.Ordinal);
        if (suffixStart <= 0 || !value.EndsWith(")", StringComparison.Ordinal))
        {
            return value;
        }

        for (var index = suffixStart + 2; index < value.Length - 1; index++)
        {
            var ch = value[index];
            if (ch < '0' || ch > '9')
            {
                return value;
            }
        }

        return value.Substring(0, suffixStart);
    }

    private static double ParseEstimatedSecondsFromSongName(ref string songName)
    {
        if (string.IsNullOrWhiteSpace(songName))
        {
            return 0;
        }

        var parts = songName.Split('-');
        if (parts.Length < 2)
        {
            return 0;
        }

        var secondsText = parts[parts.Length - 1];
        if (!int.TryParse(secondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0 || seconds > 59)
        {
            return 0;
        }

        if (!int.TryParse(parts[parts.Length - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            minutes < 0)
        {
            return 0;
        }

        if (parts.Length == 2)
        {
            songName = "";
        }
        else
        {
            songName = string.Join("-", parts, 0, parts.Length - 2);
        }

        return 60 * minutes + seconds;
    }

    private static bool IsLikelySteamId(string? value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value) || value.Length < 15 || value.Length > 20)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character < '0' || character > '9')
            {
                return false;
            }
        }

        return true;
    }
}
