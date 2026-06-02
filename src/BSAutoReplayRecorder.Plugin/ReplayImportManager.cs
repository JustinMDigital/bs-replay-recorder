using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BSAutoReplayRecorder.Core.Replay;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

public sealed class ReplayImportManager
{
    private readonly BsorInfoReader _reader = new BsorInfoReader();
    private readonly Logger _logger;

    public ReplayImportManager(Logger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ReplayImportResult Import(RecorderSessionContext session)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        Directory.CreateDirectory(session.ImportInboxDirectory);
        Directory.CreateDirectory(session.QueueDirectory);
        Directory.CreateDirectory(session.ImportedDirectory);
        Directory.CreateDirectory(session.DuplicateImportDirectory);
        Directory.CreateDirectory(session.FailedImportDirectory);

        var importFiles = Directory
            .EnumerateFiles(session.ImportInboxDirectory, "*.bsor", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (importFiles.Count == 0)
        {
            return new ReplayImportResult(0, 0, 0, 0);
        }

        var existingKeys = LoadExistingQueueKeys(session.QueueDirectory);
        var imported = 0;
        var duplicates = 0;
        var failed = 0;

        foreach (var importFile in importFiles)
        {
            try
            {
                var info = _reader.Read(importFile);
                var key = CreateKey(info);
                if (!existingKeys.Add(key))
                {
                    duplicates++;
                    MoveImportFile(session, importFile, session.DuplicateImportDirectory);
                    continue;
                }

                var queueTarget = PrepareTargetPath(Path.Combine(session.QueueDirectory, Path.GetFileName(importFile)));
                File.Copy(importFile, queueTarget, overwrite: false);
                if (session.EffectiveSettings.MoveImportedReplayFiles)
                {
                    MoveImportFile(session, importFile, session.ImportedDirectory);
                }

                imported++;
            }
            catch (Exception ex) when (IsImportFileException(ex))
            {
                failed++;
                _logger.Warn("Could not import replay " + importFile + ": " + ex.Message);
                MoveImportFile(session, importFile, session.FailedImportDirectory);
            }
        }

        _logger.Info(
            "Replay import complete for session '" + session.SessionName +
            "'. Imported=" + imported +
            ", duplicate=" + duplicates +
            ", failed=" + failed + ".");

        return new ReplayImportResult(importFiles.Count, imported, duplicates, failed);
    }

    private HashSet<string> LoadExistingQueueKeys(string queueDirectory)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(queueDirectory))
        {
            return keys;
        }

        foreach (var replayPath in Directory.EnumerateFiles(queueDirectory, "*.bsor", SearchOption.TopDirectoryOnly))
        {
            try
            {
                keys.Add(CreateKey(_reader.Read(replayPath)));
            }
            catch (Exception ex) when (IsImportFileException(ex))
            {
                _logger.Warn("Could not read existing queued replay " + replayPath + ": " + ex.Message);
            }
        }

        return keys;
    }

    private static bool IsImportFileException(Exception ex)
    {
        return ex is IOException ||
               ex is InvalidDataException ||
               ex is EndOfStreamException ||
               ex is UnauthorizedAccessException ||
               ex is ArgumentException ||
               ex is NotSupportedException;
    }

    private static string CreateKey(BsorInfo info)
    {
        return string.Join(
            "|",
            info.PlayerId,
            info.LevelHash,
            info.Difficulty,
            info.Mode,
            info.Timestamp,
            info.Score.ToString(CultureInfo.InvariantCulture));
    }

    private static void MoveImportFile(
        RecorderSessionContext session,
        string sourcePath,
        string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var targetPath = PrepareTargetPath(Path.Combine(targetDirectory, Path.GetFileName(sourcePath)));
        MoveOrCopyImportFile(session, sourcePath, targetPath);
    }

    private static void MoveOrCopyImportFile(
        RecorderSessionContext session,
        string sourcePath,
        string targetPath)
    {
        if (session.EffectiveSettings.MoveImportedReplayFiles)
        {
            File.Move(sourcePath, targetPath);
            return;
        }

        File.Copy(sourcePath, targetPath, overwrite: false);
    }

    private static string PrepareTargetPath(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
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
}

public sealed class ReplayImportResult
{
    public ReplayImportResult(int scanned, int imported, int duplicates, int failed)
    {
        Scanned = scanned;
        Imported = imported;
        Duplicates = duplicates;
        Failed = failed;
    }

    public int Scanned { get; }

    public int Imported { get; }

    public int Duplicates { get; }

    public int Failed { get; }

    public bool HasWork => Scanned > 0;

    public string ToSummary()
    {
        if (!HasWork)
        {
            return "No import files found";
        }

        return "Imported: " + Imported + "  Duplicates: " + Duplicates + "  Failed: " + Failed;
    }
}
