using System.Diagnostics;
using System.Text.Json;

namespace BSAutoReplayRecorder.ControlPanel;

public interface IWorkerPluginInstaller
{
    void Install(
        IReadOnlyList<WorkerInstanceRecord> instances,
        ControlPanelSettings settings,
        IReadOnlyList<WorkerInstanceRecord>? deployTargets = null);
}

internal sealed class DotNetWorkerPluginInstaller : IWorkerPluginInstaller
{
    private const string PluginProjectRelativePath = "src/BSAutoReplayRecorder.Plugin/BSAutoReplayRecorder.Plugin.csproj";
    private const string BeatLeaderPluginRelativePath = "Plugins/BeatLeader.dll";

    private static readonly string[] StalePluginRelativePaths =
    {
        "Plugins/BSWorldCupReplayRecorder.Plugin.dll",
        "Plugins/BSWorldCupReplayRecorder.Plugin.pdb",
        "Libs/BSWorldCupReplayRecorder.Core.dll",
        "Libs/BSWorldCupReplayRecorder.Core.pdb",
        "Plugins/BSAutoReplayRecorder.Core.dll",
        "Plugins/BSAutoReplayRecorder.Core.pdb",
        "Plugins/.cache/BSAutoReplayRecorder.Core.dll",
        "Plugins/.cache/BSAutoReplayRecorder.Core.pdb",
        "Plugins/.cache/BSAutoReplayRecorder.Plugin.dll",
        "Plugins/.cache/BSAutoReplayRecorder.Plugin.pdb"
    };
    private static readonly string[] WorkerConflictingModRelativePaths =
    {
        "Plugins/BeatSaverDownloader.dll",
        "Plugins/DataPuller.dll",
        "UserData/BeatSaverDownloader.ini",
        "UserData/DataPuller.json"
    };

    public void Install(
        IReadOnlyList<WorkerInstanceRecord> instances,
        ControlPanelSettings settings,
        IReadOnlyList<WorkerInstanceRecord>? deployTargets = null)
    {
        if (instances.Count == 0)
        {
            return;
        }

        deployTargets ??= instances;
        if (deployTargets.Count == 0)
        {
            return;
        }

        var baselineDirectory = Path.GetFullPath(instances[0].LaunchDirectory);
        if (!File.Exists(Path.Combine(baselineDirectory, "Beat Saber.exe")))
        {
            throw new InvalidOperationException("Cannot build the worker plugin because instance 1 is missing Beat Saber.exe.");
        }

        var beatLeaderPath = Path.Combine(
            baselineDirectory,
            BeatLeaderPluginRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(beatLeaderPath))
        {
            throw new InvalidOperationException(
                "Cannot build the worker plugin because instance 1 is missing " +
                BeatLeaderPluginRelativePath +
                ". Install BeatLeader in the managed baseline instance or reprovision workers from a Beat Saber folder that already has BeatLeader installed.");
        }

        var outputDirectory = ResolveBundledPluginOutput(settings, baselineDirectory);
        if (outputDirectory == null)
        {
            var pluginProjectPath = ResolvePluginProjectPath()
                                    ?? throw new InvalidOperationException(
                                        "Bundled worker plugin was not found and the source project is missing under " +
                                        PluginProjectRelativePath + ".");
            var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pluginProjectPath)!, "..", ".."));
            var buildRoot = Path.Combine(Path.GetFullPath(settings.WorkspaceDirectory), "Build", "WorkerPlugin");
            BuildPlugin(pluginProjectPath, repoRoot, baselineDirectory, buildRoot);
            outputDirectory = Path.Combine(buildRoot, "Debug", "netstandard2.1");
        }

        var pluginDll = Path.Combine(outputDirectory, "BSAutoReplayRecorder.Plugin.dll");
        var coreDll = Path.Combine(outputDirectory, "BSAutoReplayRecorder.Core.dll");
        if (!File.Exists(pluginDll) || !File.Exists(coreDll))
        {
            throw new InvalidOperationException("Worker plugin build did not produce the expected DLLs in " + outputDirectory + ".");
        }

        var enabledInstanceCount = Math.Max(1, instances.Count(instance => instance.Enabled));
        var columns = enabledInstanceCount == 1 ? 1 : 2;
        var rows = enabledInstanceCount == 1 ? 1 : 2;

        foreach (var instance in deployTargets.OrderBy(instance => instance.Index))
        {
            DeployPluginToInstance(instance, settings, outputDirectory, columns, rows);
        }
    }

    private static void BuildPlugin(
        string pluginProjectPath,
        string repoRoot,
        string beatSaberDirectory,
        string buildRoot)
    {
        Directory.CreateDirectory(buildRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(pluginProjectPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-p:BeatSaberDir=" + beatSaberDirectory);
        startInfo.ArgumentList.Add("-p:BaseOutputPath=" + EnsureTrailingSeparator(buildRoot));

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start dotnet to build the worker plugin.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Worker plugin build failed: " + (NormalizeNullable(error) ?? NormalizeNullable(output) ?? "dotnet exited with code " + process.ExitCode) + ".");
        }
    }

    private static void DeployPluginToInstance(
        WorkerInstanceRecord instance,
        ControlPanelSettings settings,
        string outputDirectory,
        int columns,
        int rows)
    {
        try
        {
            var instanceDirectory = Path.GetFullPath(instance.LaunchDirectory);
            foreach (var relativePath in StalePluginRelativePaths)
            {
                DeleteFileIfExists(Path.Combine(instanceDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            foreach (var relativePath in WorkerConflictingModRelativePaths)
            {
                DeleteFileIfExists(Path.Combine(instanceDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            CopyIfExists(
                Path.Combine(outputDirectory, "BSAutoReplayRecorder.Plugin.dll"),
                Path.Combine(instanceDirectory, "Plugins"));
            CopyIfExists(
                Path.Combine(outputDirectory, "BSAutoReplayRecorder.Plugin.pdb"),
                Path.Combine(instanceDirectory, "Plugins"),
                optional: true);
            CopyIfExists(
                Path.Combine(outputDirectory, "BSAutoReplayRecorder.Core.dll"),
                Path.Combine(instanceDirectory, "Libs"));
            CopyIfExists(
                Path.Combine(outputDirectory, "BSAutoReplayRecorder.Core.pdb"),
                Path.Combine(instanceDirectory, "Libs"),
                optional: true);

            var settingsDirectory = Path.Combine(instanceDirectory, "UserData", "BSAutoReplayRecorder");
            var settingsPath = Path.Combine(settingsDirectory, "settings.json");
            Directory.CreateDirectory(settingsDirectory);
            BackupFile(settingsPath);
            var pluginSettings = CreatePluginSettings(
                instance,
                settings,
                CreateManagedWorkerId(instance),
                columns,
                rows);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(pluginSettings, JsonOptions.Default));
        }
        catch (IOException ex) when (IsPluginFileInUse(ex))
        {
            throw new InvalidOperationException(
                "Cannot update plugin files for " + instance.Name + " because a Beat Saber process is still using them. Stop that instance and retry launch.");
        }
    }

    internal static Dictionary<string, object?> CreatePluginSettings(
        WorkerInstanceRecord instance,
        ControlPanelSettings settings,
        string workerId,
        int columns,
        int rows)
    {
        var displayIndex = instance.Index + 1;
        return new Dictionary<string, object?>
        {
            ["DisableScoreSubmissions"] = true,
            ["SuppressScoreSaberReplayUi"] = true,
            ["RequirePreflightReplayValidation"] = true,
            ["RefreshSongCoreBeforeReplayValidation"] = true,
            ["SongCoreRefreshTimeoutSeconds"] = 45,
            ["RecordingOutputDirectory"] = "UserData/BSAutoReplayRecorder/Recordings",
            ["ReplayFinishTimeoutPaddingSeconds"] = 30,
            ["LagSpikeDetectionEnabled"] = true,
            ["LagSpikeThresholdMilliseconds"] = 250,
            ["LagSpikeConsecutiveFrameCount"] = 1,
            ["LagSpikeStartupGraceSeconds"] = settings.LagSpikeStartupGraceSeconds,
            ["DelayBetweenRecordingsSeconds"] = settings.DelayBetweenRecordingsSeconds,
            ["StartRecordingRetryCount"] = 5,
            ["StartRecordingRetryDelaySeconds"] = 2,
            ["RecorderHost"] = new Dictionary<string, object?>
            {
                ["BaseUrl"] = string.IsNullOrWhiteSpace(instance.RecorderHostUrl)
                    ? "http://127.0.0.1:" + (5757 + instance.Index)
                    : instance.RecorderHostUrl,
                ["WindowTitle"] = "Beat Saber",
                ["OutputDirectory"] = "",
                ["TargetFps"] = null,
                ["CaptureWidth"] = null,
                ["CaptureHeight"] = null,
                ["Encoder"] = "",
                ["VideoBitrateKbps"] = null,
                ["OutputFormat"] = "",
                ["MonitorIndex"] = null,
                ["QualityMode"] = "",
                ["CaptureEngine"] = "",
                ["AudioMode"] = "",
                ["AudioDeviceName"] = "",
                ["AudioBitrateKbps"] = null,
                ["AudioSampleRate"] = null,
                ["AudioChannels"] = null,
                ["AudioLevelMode"] = "",
                ["AudioTargetLevelDb"] = null,
                ["TargetProcessId"] = null,
                ["TimeoutSeconds"] = 300
            },
            ["ControlPanelWorker"] = new Dictionary<string, object?>
            {
                ["Enabled"] = true,
                ["BaseUrl"] = settings.BindUrl.TrimEnd('/'),
                ["WorkerId"] = workerId,
                ["WorkerName"] = "Instance " + displayIndex,
                ["PreferredInstanceIndex"] = instance.Index,
                ["PollIntervalSeconds"] = 1,
                ["HeartbeatIntervalSeconds"] = 2,
                ["IdleShutdownMinutes"] = settings.IdleShutdownMinutes,
                ["RequestTimeoutSeconds"] = 30
            },
            ["WindowPlacement"] = new Dictionary<string, object?>
            {
                ["Enabled"] = true,
                ["InstanceIndex"] = instance.Index,
                ["MonitorIndex"] = settings.MonitorIndex,
                ["Columns"] = columns,
                ["Rows"] = rows,
                ["Width"] = settings.CaptureWidth,
                ["Height"] = settings.CaptureHeight,
                ["ApplyDelaySeconds"] = 1,
                ["RetryCount"] = 60,
                ["RetryIntervalSeconds"] = 0.5,
                ["UseNativeWindowMove"] = true,
                ["UseBorderlessWindow"] = true
            }
        };
    }

    private static string? ResolvePluginProjectPath()
    {
        foreach (var root in EnumerateRepositorySearchRoots())
        {
            var candidate = Path.Combine(root, PluginProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string? ResolveBundledPluginOutput(ControlPanelSettings settings, string baselineDirectory)
    {
        var pluginBuild = SetupSourcePathDetector.ResolveWorkerPluginBuild(
            string.IsNullOrWhiteSpace(settings.SourceBeatSaberPath) ? baselineDirectory : settings.SourceBeatSaberPath,
            settings.SourceBeatSaberStore);
        foreach (var root in EnumerateRepositorySearchRoots())
        {
            var candidate = Path.Combine(root, "runtime", "worker-plugin", pluginBuild, "Release", "netstandard2.1");
            if (File.Exists(Path.Combine(candidate, "BSAutoReplayRecorder.Plugin.dll")) &&
                File.Exists(Path.Combine(candidate, "BSAutoReplayRecorder.Core.dll")))
            {
                return Path.GetFullPath(candidate);
            }

            candidate = Path.Combine(root, "runtime", "worker-plugin", "Release", "netstandard2.1");
            if (pluginBuild == "bs-1.40.6" &&
                File.Exists(Path.Combine(candidate, "BSAutoReplayRecorder.Plugin.dll")) &&
                File.Exists(Path.Combine(candidate, "BSAutoReplayRecorder.Core.dll")))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateRepositorySearchRoots()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            yield return current.FullName;
            current = current.Parent;
        }

        current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static void CopyIfExists(string source, string destinationDirectory, bool optional = false)
    {
        if (!File.Exists(source))
        {
            if (optional)
            {
                return;
            }

            throw new InvalidOperationException("Worker plugin build output is missing: " + source);
        }

        Directory.CreateDirectory(destinationDirectory);
        File.Copy(source, Path.Combine(destinationDirectory, Path.GetFileName(source)), overwrite: true);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void BackupFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backupPath = path + "." + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".bak";
        File.Copy(path, backupPath, overwrite: true);
    }

    internal static string CreateManagedWorkerId(WorkerInstanceRecord instance)
    {
        return "managed-worker-" + instance.Index.ToString("00");
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
               path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool IsPluginFileInUse(IOException ex)
    {
        return ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
    }
}
