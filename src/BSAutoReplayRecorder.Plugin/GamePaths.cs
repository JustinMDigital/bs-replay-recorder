using System;
using System.IO;
using System.Reflection;

namespace BSAutoReplayRecorder.Plugin;

public static class GamePaths
{
    private const string RecorderUserDataDirectoryName = "BSAutoReplayRecorder";
    private const string LegacyRecorderUserDataDirectoryName = "BSWorldCupReplayRecorder";

    public static string GetGameRoot()
    {
        var pluginAssemblyPath = Assembly.GetExecutingAssembly().Location;
        var pluginDirectory = Path.GetDirectoryName(pluginAssemblyPath);

        if (pluginDirectory == null)
        {
            return Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(Path.Combine(pluginDirectory, ".."));
    }

    public static string GetBeatLeaderReplaysPath()
    {
        return Path.Combine(GetGameRoot(), "UserData", "BeatLeader", "Replays");
    }

    public static string GetRecorderUserDataDirectory()
    {
        var directory = Path.Combine(GetGameRoot(), "UserData", RecorderUserDataDirectoryName);
        MigrateLegacySettingsIfNeeded(directory);
        return directory;
    }

    public static string GetSettingsPath()
    {
        return Path.Combine(GetRecorderUserDataDirectory(), "settings.json");
    }

    public static string ResolveGamePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(GetGameRoot(), path);
    }

    private static void MigrateLegacySettingsIfNeeded(string recorderDirectory)
    {
        if (Directory.Exists(recorderDirectory))
        {
            return;
        }

        var legacyDirectory = Path.Combine(GetGameRoot(), "UserData", LegacyRecorderUserDataDirectoryName);
        if (!Directory.Exists(legacyDirectory))
        {
            return;
        }

        Directory.CreateDirectory(recorderDirectory);
        CopyIfExists(
            Path.Combine(legacyDirectory, "settings.json"),
            Path.Combine(recorderDirectory, "settings.json"));
        CopyIfExists(
            Path.Combine(legacyDirectory, "completed-replays.json"),
            Path.Combine(recorderDirectory, "completed-replays.json"));
    }

    private static void CopyIfExists(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(targetPath))
        {
            return;
        }

        File.Copy(sourcePath, targetPath, overwrite: false);
    }
}
