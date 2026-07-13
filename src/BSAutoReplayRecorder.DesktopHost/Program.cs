using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

var host = new DesktopHost(AppContext.BaseDirectory, Directory.GetCurrentDirectory(), args);
Environment.ExitCode = await host.RunAsync().ConfigureAwait(false);

internal sealed class DesktopHost
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string[] _args;
    private readonly string _repoRoot;
    private readonly LocalSettings _settings;
    private readonly string _workspace;
    private readonly string _controlPanelUrl;
    private readonly string _logDirectory;
    private readonly string _pidFile;
    private readonly string _launcherLogPath;
    private readonly AppLayout _layout;
    private readonly bool _createdLocalSettingsFile;

    public DesktopHost(string baseDirectory, string currentDirectory, string[] args)
    {
        _args = args;
        _repoRoot = ResolveRepoRoot(baseDirectory, currentDirectory, args);
        var command = ReadCommand(args);
        if (string.Equals(command, "start", StringComparison.OrdinalIgnoreCase) &&
            !args.Any(arg => string.Equals(arg, "--require-installed", StringComparison.OrdinalIgnoreCase)))
        {
            _createdLocalSettingsFile = EnsureLocalSettingsFile(_repoRoot);
        }

        _settings = LocalSettings.Load(Path.Combine(_repoRoot, "settings.json"));
        _workspace = ResolveWorkspace(_repoRoot, _settings);
        _controlPanelUrl = ResolveControlPanelUrl(_settings);
        _logDirectory = Path.Combine(_workspace, "Logs");
        _pidFile = Path.Combine(_workspace, "started-processes.json");
        _launcherLogPath = Path.Combine(_logDirectory, "desktop-host.log");
        _layout = AppLayout.Resolve(ResolveAppRoot(baseDirectory, currentDirectory, args, _repoRoot));
    }

    public async Task<int> RunAsync()
    {
        var command = ReadCommand(_args);
        try
        {
            Directory.CreateDirectory(_logDirectory);
            Log("Desktop host command: " + command + " (" + _layout.Description + ")");
            switch (command.ToLowerInvariant())
            {
                case "start":
                    await StartAsync().ConfigureAwait(false);
                    return 0;
                case "stop":
                    Stop(HasSwitch("--stop-games"));
                    return 0;
                case "status":
                    return await PrintStatusAsync().ConfigureAwait(false);
                default:
                    Console.Error.WriteLine("Unknown command: " + command);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private async Task StartAsync()
    {
        Log("Checking control panel status at " + _controlPanelUrl + "...");
        if (await IsHttpReadyAsync(_controlPanelUrl + "/api/state", TimeSpan.FromSeconds(2)).ConfigureAwait(false))
        {
            Log("Control panel already running: " + _controlPanelUrl);
            Console.WriteLine("READY " + _controlPanelUrl);
            return;
        }

        if (HasSwitch("--require-installed"))
        {
            Log("Checking installed recorder setup...");
            AssertInstalledStateReady();
        }
        else
        {
            Log("Checking local settings...");
            if (_createdLocalSettingsFile)
            {
                Log("Created local settings from settings.example.json.");
            }
        }

        Log("Preparing recorder workspace: " + _workspace);
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_logDirectory);

        if (_layout.UsesPublishedRuntime)
        {
            Log("Using published runtime: " + _layout.RuntimeRoot);
        }
        else
        {
            Log("Using source tree runtime; building required projects.");
            RunDotNetBuild("control panel", _layout.ControlPanelProject!, null);
            RunDotNetBuild("recorder host", _layout.RecorderHostProject!, null);
            RunDotNetBuild("process-loopback helper", _layout.ProcessLoopbackProject!, "Release");
            RunDotNetBuild("windows-graphics-capture helper", _layout.WindowsGraphicsCaptureProject!, "Release");
        }

        Log("Loading recorder host plan...");
        var state = ControlPanelStateFile.Load(Path.Combine(_workspace, "control-panel-state.json"));
        var captureDefaults = CaptureDefaults.From(_settings, state);
        Log("Resolving FFmpeg tools...");
        var ffmpegPath = ResolveFfmpegPath();
        var ffprobePath = ResolveFfprobePath(ffmpegPath);
        Log("Using FFmpeg: " + ffmpegPath);
        Log("Using ffprobe: " + ffprobePath);

        var started = new List<StartedProcessRecord>();
        foreach (var instance in ReadInstancePlan(state))
        {
            var port = GetPort(instance.RecorderHostUrl);
            Log("Preparing recorder host " + port + "...");
            Directory.CreateDirectory(instance.OutputDirectory);
            var configPath = EnsureRecorderHostConfig(
                instance,
                port,
                ffmpegPath,
                captureDefaults,
                _layout.ProcessLoopbackCapturePath,
                _layout.WindowsGraphicsCapturePath);
            if (await IsHttpReadyAsync(instance.RecorderHostUrl + "/health", TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            {
                Log("Recorder host " + port + " is already running.");
                continue;
            }

            started.Add(StartManagedService(
                "recorder host " + port,
                _layout.RecorderHostFileName,
                _layout.CreateRecorderHostArguments(configPath),
                Path.Combine(_logDirectory, "recorder-host-" + port + ".out.log"),
                Path.Combine(_logDirectory, "recorder-host-" + port + ".err.log"),
                _layout.RecorderHostWorkingDirectory,
                ffmpegPath,
                ffprobePath));
        }

        if (!await IsHttpReadyAsync(_controlPanelUrl + "/api/state", TimeSpan.FromSeconds(2)).ConfigureAwait(false))
        {
            Log("Preparing control panel service...");
            started.Add(StartManagedService(
                "control panel",
                _layout.ControlPanelFileName,
                _layout.CreateControlPanelArguments(),
                Path.Combine(_logDirectory, "control-panel.out.log"),
                Path.Combine(_logDirectory, "control-panel.err.log"),
                _layout.ControlPanelWorkingDirectory,
                ffmpegPath,
                ffprobePath));
        }

        AddStartedProcessRecords(started);

        foreach (var instance in ReadInstancePlan(state))
        {
            await WaitForEndpointAsync("Recorder host " + GetPort(instance.RecorderHostUrl), instance.RecorderHostUrl + "/health", TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }

        await WaitForEndpointAsync("Control panel", _controlPanelUrl + "/api/state", TimeSpan.FromSeconds(45)).ConfigureAwait(false);
        Console.WriteLine("READY " + _controlPanelUrl);
    }

    private async Task<int> PrintStatusAsync()
    {
        Log("Checking control panel status at " + _controlPanelUrl + "...");
        if (await IsHttpReadyAsync(_controlPanelUrl + "/api/state", TimeSpan.FromSeconds(2)).ConfigureAwait(false))
        {
            Console.WriteLine("READY " + _controlPanelUrl);
            return 0;
        }

        Console.WriteLine("STOPPED " + _controlPanelUrl);
        return 1;
    }

    private void Stop(bool stopGames)
    {
        RestoreTaskbarVisibility("before stopping recorder processes");
        RestoreDisplayScale("before stopping recorder processes");
        try
        {
            var workspacePaths = EnumerateWorkspacePaths().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (stopGames)
            {
                StopTrackedBeatSaberProcesses(workspacePaths);
            }

            var seen = new HashSet<int>();
            foreach (var workspace in workspacePaths)
            {
                var pidFile = Path.Combine(workspace, "started-processes.json");
                if (!File.Exists(pidFile))
                {
                    continue;
                }

                foreach (var record in ReadStartedProcessRecords(pidFile))
                {
                    seen.Add(record.Pid);
                    StopManagedProcess(record.Pid, record.Name);
                }

                TryDelete(pidFile);
            }

            foreach (var process in FindOrphanRecorderProcesses(seen))
            {
                using (process)
                {
                    StopProcess(process, "orphaned recorder process " + process.Id);
                }
            }
        }
        finally
        {
            RestoreTaskbarVisibility("after stopping recorder processes");
            RestoreDisplayScale("after stopping recorder processes");
        }

        Console.WriteLine("STOPPED");
    }

    private void RestoreTaskbarVisibility(string context)
    {
        try
        {
            TaskbarVisibilityController.Restore();
            Log("Restored Windows taskbar " + context + ".");
        }
        catch (Exception ex)
        {
            Log("Could not restore Windows taskbar " + context + ": " + ex.Message);
        }
    }

    private void RestoreDisplayScale(string context)
    {
        try
        {
            var statePath = Path.Combine(_workspace, "control-panel-state.json");
            var state = ControlPanelStateFile.Load(statePath);
            if (state.Run.GetBool("displayScaleRestorePending") != true)
            {
                return;
            }

            var toolPath = ResolveSetDpiToolPath();
            if (toolPath == null)
            {
                Log("Display scale restore skipped " + context + ": SetDpi.exe was not found.");
                return;
            }

            var scalePercent = Math.Clamp(
                state.Run.GetInt("displayScaleRestorePercent") ??
                state.Settings.GetInt("restoreDisplayScalePercent") ??
                _settings.GetInt("restoreDisplayScalePercent") ??
                150,
                100,
                500);
            var monitorIndex = Math.Clamp(
                state.Run.GetInt("displayScaleMonitorIndex") ??
                state.Settings.GetInt("monitorIndex") ??
                _settings.GetInt("monitorIndex") ??
                0,
                0,
                16);

            InvokeSetDpi(toolPath, scalePercent, monitorIndex, context);
            ClearDisplayScaleRestorePending(statePath);
        }
        catch (Exception ex)
        {
            Log("Could not restore display scale " + context + ": " + ex.Message);
        }
    }

    private void ClearDisplayScaleRestorePending(string statePath)
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return;
            }

            var root = JsonNode.Parse(ReadAllTextAllowBom(statePath)) as JsonObject;
            if (root == null)
            {
                return;
            }

            if (root["run"] is not JsonObject run)
            {
                run = new JsonObject();
                root["run"] = run;
            }

            run["displayScaleRestorePending"] = false;
            run["displayScaleRestorePercent"] = 0;
            run["displayScaleMonitorIndex"] = 0;
            File.WriteAllText(statePath, root.ToJsonString(JsonOptions));
        }
        catch (Exception ex)
        {
            Log("Could not clear display scale restore marker: " + ex.Message);
        }
    }

    private void InvokeSetDpi(string toolPath, int scalePercent, int monitorIndex, string context)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(scalePercent.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add((monitorIndex + 1).ToString(CultureInfo.InvariantCulture));

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Windows did not start SetDpi.exe.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("SetDpi.exe timed out.");
        }

        if (process.ExitCode != 0 ||
            output.Contains("Invalid Monitor", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Invalid Monitor", StringComparison.OrdinalIgnoreCase))
        {
            var detail = NormalizeNullable(error) ?? NormalizeNullable(output) ?? "exit code " + process.ExitCode;
            throw new InvalidOperationException("SetDpi.exe failed: " + detail);
        }

        Log(
            "Restored display scale " +
            context +
            " to " +
            scalePercent.ToString(CultureInfo.InvariantCulture) +
            "% on monitor " +
            monitorIndex.ToString(CultureInfo.InvariantCulture) +
            ".");
    }

    private static bool EnsureLocalSettingsFile(string repoRoot)
    {
        var settingsPath = Path.Combine(repoRoot, "settings.json");
        if (File.Exists(settingsPath))
        {
            return false;
        }

        var examplePath = Path.Combine(repoRoot, "settings.example.json");
        if (File.Exists(examplePath))
        {
            File.Copy(examplePath, settingsPath);
            return true;
        }

        return false;
    }

    private void AssertInstalledStateReady()
    {
        var settingsPath = Path.Combine(_repoRoot, "settings.json");
        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException("settings.json was not found. Run Support\\install.bat first, then open Replay Recorder.exe.");
        }

        var statePath = Path.Combine(_workspace, "control-panel-state.json");
        if (!File.Exists(statePath))
        {
            throw new FileNotFoundException("Installer state was not found at " + statePath + ". Run Support\\install.bat first.");
        }

        using var document = JsonDocument.Parse(ReadAllTextAllowBom(statePath));
        var root = document.RootElement;
        if (!TryGetProperty(root, "settings", out var settings) ||
            !TryGetProperty(root, "instances", out var instances) ||
            instances.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Installer state is incomplete. Re-run Support\\install.bat before opening Replay Recorder.exe.");
        }

        JsonElement statusElement = default;
        if (!TryGetProperty(root, "instanceProvision", out var provision) ||
            !TryGetProperty(provision, "status", out statusElement) ||
            !string.Equals(statusElement.GetString(), "Ready", StringComparison.OrdinalIgnoreCase))
        {
            var status = statusElement.ValueKind == JsonValueKind.String ? statusElement.GetString() : "missing";
            throw new InvalidOperationException("Managed instance provisioning is not ready (status: " + status + "). Re-run Support\\install.bat.");
        }

        var instanceCount = 0;
        if (TryGetProperty(settings, "instanceCount", out var instanceCountElement) &&
            instanceCountElement.TryGetInt32(out var configuredCount))
        {
            instanceCount = Math.Clamp(configuredCount, 1, 4);
        }

        if (instanceCount <= 0)
        {
            instanceCount = instances.GetArrayLength();
        }

        if (instanceCount <= 0)
        {
            throw new InvalidOperationException("Installer state has no managed Beat Saber instances. Re-run Support\\install.bat.");
        }

        var checkedCount = 0;
        foreach (var instance in instances.EnumerateArray())
        {
            if (checkedCount >= instanceCount)
            {
                break;
            }

            checkedCount++;
            var launchDirectory = ReadString(instance, "launchDirectory") ?? "";
            if (string.IsNullOrWhiteSpace(launchDirectory))
            {
                throw new InvalidOperationException("Installer state is missing a managed Beat Saber launch directory. Re-run Support\\install.bat.");
            }

            var beatSaberExe = Path.Combine(launchDirectory, "Beat Saber.exe");
            if (!File.Exists(beatSaberExe))
            {
                throw new FileNotFoundException("Managed Beat Saber instance is missing Beat Saber.exe: " + launchDirectory + ". Re-run Support\\install.bat.");
            }
        }
    }

    private void RunDotNetBuild(string name, string projectPath, string? configuration)
    {
        var args = new List<string> { "build", projectPath, "--nologo" };
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            args.Add("-c");
            args.Add(configuration);
        }

        Log("Building " + name + "...");
        var result = RunProcess("dotnet", args, _repoRoot, TimeSpan.FromMinutes(3));
        if (result != 0)
        {
            throw new InvalidOperationException("Build failed for " + name + ".");
        }

        Log("Built " + name + ".");
    }

    private int RunProcess(string fileName, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, eventArgs) => Log(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Log(eventArgs.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start " + fileName + ".");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException(fileName + " timed out.");
        }

        return process.ExitCode;
    }

    private StartedProcessRecord StartManagedService(
        string name,
        string fileName,
        string[] arguments,
        string outLog,
        string errLog,
        string workingDirectory,
        string ffmpegPath,
        string ffprobePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outLog) ?? _logDirectory);
        File.AppendAllText(outLog, "[" + DateTimeOffset.Now.ToString("O") + "] " + name + " starting." + Environment.NewLine);
        File.AppendAllText(errLog, "[" + DateTimeOffset.Now.ToString("O") + "] " + name + " starting." + Environment.NewLine);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["BSARR_CONTROL_PANEL_WORKSPACE"] = _workspace;
        startInfo.Environment["BSARR_CONTROL_PANEL_URL"] = _controlPanelUrl;
        startInfo.Environment["BSARR_FFMPEG_PATH"] = ffmpegPath;
        startInfo.Environment["BSARR_FFPROBE_PATH"] = ffprobePath;
        var settingsPath = Path.Combine(_repoRoot, "settings.json");
        if (File.Exists(settingsPath))
        {
            startInfo.Environment["BSARR_SETTINGS_PATH"] = settingsPath;
        }

        Log("Starting " + name + "...");
        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("Could not start " + name + ".");
        Log("Started " + name + ", pid " + process.Id + ".");
        return new StartedProcessRecord
        {
            Name = name,
            Pid = process.Id,
            StartedAt = DateTimeOffset.UtcNow,
            OutLog = outLog,
            ErrLog = errLog
        };
    }

    private string EnsureRecorderHostConfig(
        InstancePlan instance,
        int port,
        string ffmpegPath,
        CaptureDefaults defaults,
        string processLoopbackPath,
        string windowsGraphicsCapturePath)
    {
        var configPath = Path.Combine(_workspace, "recorder-host-" + port + ".settings.json");
        var offsetColumn = instance.Index % 2;
        var offsetRow = instance.Index / 2;
        var offsetX = offsetColumn * Math.Max(1, defaults.CaptureWidth);
        var offsetY = offsetRow * Math.Max(1, defaults.CaptureHeight);
        var outputFormat = NormalizeOutputFormat(defaults.OutputFormat);
        var argumentTemplate =
            "-hide_banner -y -f lavfi -i \"ddagrab=output_idx={monitorIndex}:draw_mouse=0:framerate={fps}:offset_x=" +
            offsetX.ToString() +
            ":offset_y=" +
            offsetY.ToString() +
            ":video_size={videoSize}\" {audioInput} -map 0:v:0 {audioMap} -c:v {encoder} -preset {encoderPreset} -b:v {videoBitrate} {audioOutputOptions} {containerFlags} {output}";

        var config = new Dictionary<string, object?>
        {
            ["bindUrl"] = "http://127.0.0.1:" + port,
            ["ffmpegPath"] = ffmpegPath,
            ["processLoopbackCapturePath"] = processLoopbackPath,
            ["windowsGraphicsCapturePath"] = windowsGraphicsCapturePath,
            ["outputDirectory"] = instance.OutputDirectory,
            ["outputExtension"] = "." + outputFormat,
            ["overwriteExisting"] = false,
            ["preserveProcessLoopbackSidecars"] = defaults.PreserveProcessLoopbackSidecars,
            ["stopTimeoutSeconds"] = 30,
            ["startupProbeMilliseconds"] = 500,
            ["defaultWindowTitle"] = "Beat Saber",
            ["defaultTargetFps"] = defaults.TargetFps,
            ["defaultCaptureWidth"] = defaults.CaptureWidth,
            ["defaultCaptureHeight"] = defaults.CaptureHeight,
            ["defaultEncoder"] = defaults.Encoder,
            ["defaultVideoBitrateKbps"] = defaults.VideoBitrateKbps,
            ["defaultMonitorIndex"] = defaults.MonitorIndex,
            ["defaultQualityMode"] = defaults.QualityMode,
            ["defaultCaptureEngine"] = defaults.CaptureEngine,
            ["defaultAudioMode"] = defaults.AudioMode,
            ["defaultAudioDeviceName"] = "",
            ["defaultAudioBitrateKbps"] = defaults.AudioBitrateKbps,
            ["defaultAudioSampleRate"] = defaults.AudioSampleRate,
            ["defaultAudioChannels"] = defaults.AudioChannels,
            ["argumentTemplate"] = argumentTemplate
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, JsonOptions));
        return configPath;
    }

    private IReadOnlyList<InstancePlan> ReadInstancePlan(ControlPanelStateFile state)
    {
        var instanceCount = state.Settings.GetInt("instanceCount")
                            ?? _settings.GetInt("instanceCount")
                            ?? 1;
        instanceCount = Math.Clamp(instanceCount, 1, 4);
        var plans = state.Instances.Take(instanceCount).ToList();
        if (plans.Count > 0)
        {
            return plans;
        }

        plans = new List<InstancePlan>();
        for (var index = 0; index < instanceCount; index++)
        {
            plans.Add(new InstancePlan(
                index,
                "http://127.0.0.1:" + (5757 + index),
                Path.Combine(_workspace, "Recordings", "Instance " + (index + 1))));
        }

        return plans;
    }

    private string ResolveFfmpegPath()
    {
        var configured = Environment.GetEnvironmentVariable("BSARR_FFMPEG_PATH");
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = _settings.GetString("ffmpegPath");
        }

        foreach (var candidate in EnumerateFfmpegCandidates(configured, "ffmpeg.exe"))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var fromPath = FindOnPath("ffmpeg.exe");
        if (fromPath != null)
        {
            return fromPath;
        }

        throw new FileNotFoundException("FFmpeg was not found. Install FFmpeg or set ffmpegPath in settings.json.");
    }

    private string ResolveFfprobePath(string ffmpegPath)
    {
        var configured = Environment.GetEnvironmentVariable("BSARR_FFPROBE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var ffmpegDirectory = Path.GetDirectoryName(ffmpegPath);
        if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
        {
            var sibling = Path.Combine(ffmpegDirectory, "ffprobe.exe");
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        foreach (var candidate in EnumerateFfmpegCandidates(null, "ffprobe.exe"))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var fromPath = FindOnPath("ffprobe.exe");
        if (fromPath != null)
        {
            return fromPath;
        }

        throw new FileNotFoundException("ffprobe was not found. Install FFmpeg/ffprobe or set BSARR_FFPROBE_PATH.");
    }

    private IEnumerable<string> EnumerateFfmpegCandidates(string? configured, string executableName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return ResolveRepoRelativePath(configured);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetPackages))
            {
                foreach (var candidate in Directory.EnumerateFiles(wingetPackages, executableName, SearchOption.AllDirectories)
                             .Where(path => path.Contains("Gyan.FFmpeg", StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    yield return candidate;
                }
            }

            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", executableName);
        }

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", executableName);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin", executableName);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", executableName);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ShareX", executableName);
    }

    private string? FindOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static int GetPort(string url)
    {
        return new Uri(url).Port;
    }

    private async Task WaitForEndpointAsync(string name, string url, TimeSpan timeout)
    {
        Log("Waiting for " + name + " at " + url + "...");
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            if (await IsHttpReadyAsync(url, TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            {
                Log(name + " is ready at " + url);
                return;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        throw new TimeoutException(name + " did not answer within " + timeout.TotalSeconds + " seconds.");
    }

    private static async Task<bool> IsHttpReadyAsync(string url, TimeSpan timeout)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout };
            using var response = await client.GetAsync(url).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void AddStartedProcessRecords(List<StartedProcessRecord> started)
    {
        if (started.Count == 0)
        {
            return;
        }

        var records = File.Exists(_pidFile)
            ? ReadStartedProcessRecords(_pidFile).ToList()
            : new List<StartedProcessRecord>();
        records.AddRange(started);
        File.WriteAllText(_pidFile, JsonSerializer.Serialize(records, JsonOptions));
    }

    private IEnumerable<StartedProcessRecord> ReadStartedProcessRecords(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<List<StartedProcessRecord>>(ReadAllTextAllowBom(path), JsonOptions)
                   ?? Enumerable.Empty<StartedProcessRecord>();
        }
        catch (Exception ex)
        {
            Log("Could not read process tracking file " + path + ": " + ex.Message);
            return Enumerable.Empty<StartedProcessRecord>();
        }
    }

    private void StopManagedProcess(int processId, string name)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!LooksLikeRecorderProcess(processId))
            {
                Log("Skipping pid " + processId + " because it no longer looks like a replay-recorder process.");
                return;
            }

            StopProcess(process, name + ", pid " + processId);
        }
        catch (ArgumentException)
        {
            Log(name + " is not running.");
        }
    }

    private void StopProcess(Process process, string name)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            Log("Stopped " + name + ".");
        }
        catch (Exception ex)
        {
            Log("Could not stop " + name + ": " + ex.Message);
        }
    }

    private bool LooksLikeRecorderProcess(int processId)
    {
        var commandLine = GetCommandLine(processId);
        return commandLine != null && commandLine.Contains("BSAutoReplayRecorder", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<Process> FindOrphanRecorderProcesses(HashSet<int> knownProcessIds)
    {
        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            if (knownProcessIds.Contains(process.Id) || process.Id == currentProcessId)
            {
                process.Dispose();
                continue;
            }

            string processName;
            try
            {
                processName = process.ProcessName;
            }
            catch
            {
                process.Dispose();
                continue;
            }

            if (IsPublishedRecorderProcessName(processName))
            {
                if (ProcessImageBelongsToRepo(process))
                {
                    yield return process;
                }
                else
                {
                    process.Dispose();
                }

                continue;
            }

            if (!string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                process.Dispose();
                continue;
            }

            string? commandLine;
            try
            {
                commandLine = GetCommandLine(process.Id);
            }
            catch
            {
                process.Dispose();
                continue;
            }

            if (LooksLikeRepoRecorderCommandLine(commandLine))
            {
                yield return process;
            }
            else
            {
                process.Dispose();
            }
        }
    }

    private bool ProcessImageBelongsToRepo(Process process)
    {
        try
        {
            var fileName = process.MainModule?.FileName;
            return IsPathUnderDirectory(fileName, _repoRoot) || IsPathUnderDirectory(fileName, _layout.RuntimeRoot);
        }
        catch
        {
            return LooksLikeRepoRecorderCommandLine(GetCommandLine(process.Id));
        }
    }

    private bool LooksLikeRepoRecorderCommandLine(string? commandLine)
    {
        return commandLine != null &&
               commandLine.Contains(_repoRoot, StringComparison.OrdinalIgnoreCase) &&
               commandLine.Contains("BSAutoReplayRecorder.", StringComparison.OrdinalIgnoreCase) &&
               (commandLine.Contains("ControlPanel", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains("RecorderHost", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPublishedRecorderProcessName(string processName)
    {
        return processName.Contains("BSAutoReplayRecorder.ControlPanel", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("BSAutoReplayRecorder.RecorderHost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathUnderDirectory(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void StopTrackedBeatSaberProcesses(List<string> workspacePaths)
    {
        var processIds = new HashSet<int>();
        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in workspacePaths)
        {
            var statePath = Path.Combine(workspace, "control-panel-state.json");
            var state = ControlPanelStateFile.Load(statePath);
            foreach (var instance in state.Instances)
            {
                if (instance.GameProcessId.HasValue)
                {
                    processIds.Add(instance.GameProcessId.Value);
                }

                if (!string.IsNullOrWhiteSpace(instance.LaunchDirectory))
                {
                    executablePaths.Add(Path.Combine(instance.LaunchDirectory, "Beat Saber.exe"));
                }
            }
        }

        foreach (var process in Process.GetProcessesByName("Beat Saber"))
        {
            try
            {
                var path = process.MainModule?.FileName ?? "";
                if (processIds.Contains(process.Id) || executablePaths.Contains(path))
                {
                    StopProcess(process, "Beat Saber, pid " + process.Id);
                }
            }
            catch
            {
                process.Dispose();
            }
        }
    }

    private IEnumerable<string> EnumerateWorkspacePaths()
    {
        yield return _workspace;
        yield return Path.Combine(_repoRoot, "src", "BSAutoReplayRecorder.ControlPanel", "ControlPanelWorkspace");
        yield return Path.Combine(_repoRoot, "ControlPanelWorkspace");
    }

    private string ResolveRepoRelativePath(string path)
    {
        var trimmed = path.Trim().Trim('"');
        return Path.IsPathRooted(trimmed) ? Path.GetFullPath(trimmed) : Path.GetFullPath(Path.Combine(_repoRoot, trimmed));
    }

    private static string ResolveRepoRoot(string baseDirectory, string currentDirectory, string[] args)
    {
        var explicitRoot = ReadOption(args, "--repo-root");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot.Trim().Trim('"'));
        }

        foreach (var root in new[] { currentDirectory, baseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(root));
            while (directory != null)
            {
                if (LooksLikeAppRoot(directory.FullName))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Path.GetFullPath(currentDirectory);
    }

    private static string ResolveAppRoot(string baseDirectory, string currentDirectory, string[] args, string repoRoot)
    {
        var explicitRoot = ReadOption(args, "--app-root");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot.Trim().Trim('"'));
        }

        foreach (var root in new[] { repoRoot, currentDirectory, baseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(root));
            while (directory != null)
            {
                if (LooksLikeAppRoot(directory.FullName))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return repoRoot;
    }

    private static bool LooksLikeAppRoot(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "settings.example.json")))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(directory, "src", "BSAutoReplayRecorder.ControlPanel")) ||
               File.Exists(Path.Combine(directory, "runtime", "control-panel", "BSAutoReplayRecorder.ControlPanel.exe"));
    }

    private static string ResolveWorkspace(string repoRoot, LocalSettings settings)
    {
        var configured = Environment.GetEnvironmentVariable("BSARR_CONTROL_PANEL_WORKSPACE")
                         ?? settings.GetString("workspace")
                         ?? settings.GetString("workspaceDirectory")
                         ?? "ControlPanelWorkspace";
        return Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(repoRoot, configured));
    }

    private static string ResolveControlPanelUrl(LocalSettings settings)
    {
        return (Environment.GetEnvironmentVariable("BSARR_CONTROL_PANEL_URL")
                ?? settings.GetString("controlPanelUrl")
                ?? settings.GetString("bindUrl")
                ?? "http://127.0.0.1:5770").TrimEnd('/');
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string ReadCommand(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (string.Equals(arg, "--repo-root", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--app-root", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                }

                continue;
            }

            if (string.Equals(arg, "start", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "stop", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "status", StringComparison.OrdinalIgnoreCase))
            {
                return arg;
            }
        }

        return "status";
    }

    private bool HasSwitch(string name)
    {
        return _args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    }

    private void Log(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Directory.CreateDirectory(_logDirectory);
        File.AppendAllText(_launcherLogPath, "[" + DateTimeOffset.UtcNow.ToString("O") + "] " + message + Environment.NewLine);
        Console.WriteLine(message);
    }

    private static string? GetCommandLine(int processId)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId.ToString());
            foreach (var item in searcher.Get())
            {
                return item["CommandLine"]?.ToString();
            }
        }
        catch
        {
        }

        return null;
    }

    private string? ResolveSetDpiToolPath()
    {
        var configuredPath = NormalizeNullable(Environment.GetEnvironmentVariable("BSARR_SETDPI_PATH"));
        if (configuredPath != null)
        {
            return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
        }

        foreach (var root in new[] { _layout.RepoRoot, _repoRoot, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var resolved = FindSetDpiUnder(root);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? FindSetDpiUnder(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(root));
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "SetDpi", "SetDpi.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static string NormalizeOutputFormat(string value)
    {
        var normalized = (value ?? "").Trim().TrimStart('.').ToLowerInvariant();
        return normalized == "mp4" ? "mp4" : "mkv";
    }

    private static string ReadAllTextAllowBom(string path)
    {
        return File.ReadAllText(path).TrimStart('\uFEFF');
    }

    private sealed class AppLayout
    {
        private AppLayout(
            string repoRoot,
            bool usesPublishedRuntime,
            string runtimeRoot,
            string description,
            string controlPanelFileName,
            string controlPanelWorkingDirectory,
            string recorderHostFileName,
            string recorderHostWorkingDirectory,
            string processLoopbackCapturePath,
            string windowsGraphicsCapturePath,
            string? controlPanelProject,
            string? recorderHostProject,
            string? processLoopbackProject,
            string? windowsGraphicsCaptureProject)
        {
            RepoRoot = repoRoot;
            UsesPublishedRuntime = usesPublishedRuntime;
            RuntimeRoot = runtimeRoot;
            Description = description;
            ControlPanelFileName = controlPanelFileName;
            ControlPanelWorkingDirectory = controlPanelWorkingDirectory;
            RecorderHostFileName = recorderHostFileName;
            RecorderHostWorkingDirectory = recorderHostWorkingDirectory;
            ProcessLoopbackCapturePath = processLoopbackCapturePath;
            WindowsGraphicsCapturePath = windowsGraphicsCapturePath;
            ControlPanelProject = controlPanelProject;
            RecorderHostProject = recorderHostProject;
            ProcessLoopbackProject = processLoopbackProject;
            WindowsGraphicsCaptureProject = windowsGraphicsCaptureProject;
        }

        public string RepoRoot { get; }

        public bool UsesPublishedRuntime { get; }

        public string RuntimeRoot { get; }

        public string Description { get; }

        public string ControlPanelFileName { get; }

        public string ControlPanelWorkingDirectory { get; }

        public string RecorderHostFileName { get; }

        public string RecorderHostWorkingDirectory { get; }

        public string ProcessLoopbackCapturePath { get; }

        public string WindowsGraphicsCapturePath { get; }

        public string? ControlPanelProject { get; }

        public string? RecorderHostProject { get; }

        public string? ProcessLoopbackProject { get; }

        public string? WindowsGraphicsCaptureProject { get; }

        public static AppLayout Resolve(string repoRoot)
        {
            var runtimeRoot = Path.Combine(repoRoot, "runtime");
            var controlPanelExe = Path.Combine(runtimeRoot, "control-panel", "BSAutoReplayRecorder.ControlPanel.exe");
            var recorderHostExe = Path.Combine(runtimeRoot, "recorder-host", "BSAutoReplayRecorder.RecorderHost.exe");
            var processLoopbackExe = Path.Combine(runtimeRoot, "tools", "ProcessLoopbackCapture.Managed", "ProcessLoopbackCapture.exe");
            var windowsGraphicsCaptureExe = Path.Combine(runtimeRoot, "tools", "WindowsGraphicsCapture.Managed", "WindowsGraphicsCapture.exe");

            if (File.Exists(controlPanelExe) && File.Exists(recorderHostExe))
            {
                return new AppLayout(
                    repoRoot,
                    usesPublishedRuntime: true,
                    runtimeRoot,
                    "published runtime",
                    controlPanelExe,
                    Path.GetDirectoryName(controlPanelExe) ?? repoRoot,
                    recorderHostExe,
                    Path.GetDirectoryName(recorderHostExe) ?? repoRoot,
                    processLoopbackExe,
                    windowsGraphicsCaptureExe,
                    null,
                    null,
                    null,
                    null);
            }

            var controlPanelProject = Path.Combine(repoRoot, "src", "BSAutoReplayRecorder.ControlPanel", "BSAutoReplayRecorder.ControlPanel.csproj");
            var recorderHostProject = Path.Combine(repoRoot, "src", "BSAutoReplayRecorder.RecorderHost", "BSAutoReplayRecorder.RecorderHost.csproj");
            var processLoopbackProject = Path.Combine(repoRoot, "tools", "ProcessLoopbackCapture.Managed", "ProcessLoopbackCapture.Managed.csproj");
            var windowsGraphicsCaptureProject = Path.Combine(repoRoot, "tools", "WindowsGraphicsCapture.Managed", "WindowsGraphicsCapture.Managed.csproj");

            return new AppLayout(
                repoRoot,
                usesPublishedRuntime: false,
                runtimeRoot,
                "source tree",
                "dotnet",
                Path.GetDirectoryName(controlPanelProject) ?? repoRoot,
                "dotnet",
                repoRoot,
                Path.Combine(repoRoot, "tools", "ProcessLoopbackCapture.Managed", "bin", "Release", "net10.0-windows10.0.20348.0", "win-x64", "ProcessLoopbackCapture.exe"),
                Path.Combine(repoRoot, "tools", "WindowsGraphicsCapture.Managed", "bin", "Release", "net10.0-windows10.0.20348.0", "win-x64", "WindowsGraphicsCapture.exe"),
                controlPanelProject,
                recorderHostProject,
                processLoopbackProject,
                windowsGraphicsCaptureProject);
        }

        public string[] CreateControlPanelArguments()
        {
            if (UsesPublishedRuntime)
            {
                return Array.Empty<string>();
            }

            return new[] { "run", "--no-build", "--project", ControlPanelProject! };
        }

        public string[] CreateRecorderHostArguments(string configPath)
        {
            if (UsesPublishedRuntime)
            {
                return new[] { "serve", "--config", configPath };
            }

            return new[]
            {
                "run",
                "--no-build",
                "--project",
                RecorderHostProject!,
                "--",
                "serve",
                "--config",
                configPath
            };
        }
    }

    private sealed class LocalSettings
    {
        private readonly JsonElement _root;

        private LocalSettings(JsonElement root)
        {
            _root = root;
        }

        public static LocalSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                return new LocalSettings(default);
            }

            try
            {
                using var document = JsonDocument.Parse(ReadAllTextAllowBom(path));
                return new LocalSettings(document.RootElement.Clone());
            }
            catch
            {
                return new LocalSettings(default);
            }
        }

        public string? GetString(string name)
        {
            if (_root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in _root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = property.Value.GetString()?.Trim();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
            }

            return null;
        }

        public int? GetInt(string name)
        {
            if (_root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in _root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.TryGetInt32(out var value))
                {
                    return value;
                }
            }

            return null;
        }

        public bool? GetBool(string name)
        {
            if (_root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in _root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return property.Value.GetBoolean();
                }
            }

            return null;
        }
    }

    private sealed class ControlPanelStateFile
    {
        public SettingBag Settings { get; private set; } = new SettingBag(default);

        public SettingBag Run { get; private set; } = new SettingBag(default);

        public List<InstancePlan> Instances { get; } = new List<InstancePlan>();

        public static ControlPanelStateFile Load(string path)
        {
            var result = new ControlPanelStateFile();
            if (!File.Exists(path))
            {
                return result;
            }

            try
            {
                using var document = JsonDocument.Parse(ReadAllTextAllowBom(path));
                var root = document.RootElement;
                if (root.TryGetProperty("settings", out var settings))
                {
                    result.Settings = new SettingBag(settings.Clone());
                }

                if (root.TryGetProperty("run", out var run))
                {
                    result.Run = new SettingBag(run.Clone());
                }

                if (root.TryGetProperty("instances", out var instances) &&
                    instances.ValueKind == JsonValueKind.Array)
                {
                    foreach (var instance in instances.EnumerateArray())
                    {
                        var index = ReadInt(instance, "index") ?? result.Instances.Count;
                        var url = ReadString(instance, "recorderHostUrl") ?? "http://127.0.0.1:" + (5757 + index);
                        var output = ReadString(instance, "outputDirectory") ?? "";
                        if (string.IsNullOrWhiteSpace(output))
                        {
                            output = Path.Combine(Path.GetDirectoryName(path) ?? ".", "Recordings", "Instance " + (index + 1));
                        }

                        result.Instances.Add(new InstancePlan(
                            index,
                            url.TrimEnd('/'),
                            output,
                            ReadString(instance, "launchDirectory"),
                            ReadInt(instance, "gameProcessId")));
                    }
                }
            }
            catch
            {
            }

            return result;
        }
    }

    private sealed class SettingBag
    {
        private readonly JsonElement _root;

        public SettingBag(JsonElement root)
        {
            _root = root;
        }

        public string? GetString(string name)
        {
            if (_root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in _root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                }
            }

            return null;
        }

        public int? GetInt(string name)
        {
            if (_root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in _root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.TryGetInt32(out var value))
                {
                    return value;
                }
            }

            return null;
        }

        public bool? GetBool(string name)
        {
            if (_root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in _root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return property.Value.GetBoolean();
                }
            }

            return null;
        }
    }

    private sealed class CaptureDefaults
    {
        public int TargetFps { get; private init; } = 60;

        public int CaptureWidth { get; private init; } = 1920;

        public int CaptureHeight { get; private init; } = 1080;

        public string Encoder { get; private init; } = "h264_nvenc";

        public int VideoBitrateKbps { get; private init; } = 12000;

        public string OutputFormat { get; private init; } = "mkv";

        public int MonitorIndex { get; private init; }

        public string QualityMode { get; private init; } = "Balanced";

        public string CaptureEngine { get; private init; } = "FFmpegDdagrab";

        public string AudioMode { get; private init; } = "ProcessLoopback";

        public int AudioBitrateKbps { get; private init; } = 192;

        public int AudioSampleRate { get; private init; } = 48000;

        public int AudioChannels { get; private init; } = 2;

        public bool PreserveProcessLoopbackSidecars { get; private init; }

        public static CaptureDefaults From(LocalSettings localSettings, ControlPanelStateFile state)
        {
            string ReadString(string name, string fallback) =>
                state.Settings.GetString(name) ?? localSettings.GetString(name) ?? fallback;
            int ReadInt(string name, int fallback) =>
                state.Settings.GetInt(name) ?? localSettings.GetInt(name) ?? fallback;
            bool ReadBool(string name, bool fallback) =>
                state.Settings.GetBool(name) ?? localSettings.GetBool(name) ?? fallback;

            return new CaptureDefaults
            {
                TargetFps = Math.Clamp(ReadInt("targetFps", 60), 1, 240),
                CaptureWidth = Math.Max(320, ReadInt("captureWidth", 1920)),
                CaptureHeight = Math.Max(180, ReadInt("captureHeight", 1080)),
                Encoder = ReadString("encoder", "h264_nvenc"),
                VideoBitrateKbps = Math.Clamp(ReadInt("videoBitrateKbps", 12000), 500, 200000),
                OutputFormat = NormalizeOutputFormat(ReadString("outputFormat", "mkv")),
                MonitorIndex = Math.Clamp(ReadInt("monitorIndex", 0), 0, 16),
                QualityMode = ReadString("qualityMode", "Balanced"),
                CaptureEngine = NormalizeCaptureEngine(ReadString("captureEngine", "FFmpegDdagrab")),
                AudioMode = string.Equals(ReadString("audioMode", "ProcessLoopback"), "ProcessLoopback", StringComparison.OrdinalIgnoreCase)
                    ? "ProcessLoopback"
                    : "None",
                AudioBitrateKbps = Math.Clamp(ReadInt("audioBitrateKbps", 192), 64, 1024),
                AudioSampleRate = Math.Clamp(ReadInt("audioSampleRate", 48000), 8000, 192000),
                AudioChannels = Math.Clamp(ReadInt("audioChannels", 2), 1, 8),
                PreserveProcessLoopbackSidecars = ReadBool("preserveProcessLoopbackSidecars", false)
            };
        }

        private static string NormalizeCaptureEngine(string value)
        {
            return string.Equals(value, "WindowsGraphicsCapture", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "WGC", StringComparison.OrdinalIgnoreCase)
                ? "WindowsGraphicsCapture"
                : "FFmpegDdagrab";
        }
    }

    private sealed record InstancePlan(
        int Index,
        string RecorderHostUrl,
        string OutputDirectory,
        string? LaunchDirectory = null,
        int? GameProcessId = null);

    private sealed class StartedProcessRecord
    {
        public string Name { get; set; } = "";

        public int Pid { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public string OutLog { get; set; } = "";

        public string ErrLog { get; set; } = "";
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.TryGetInt32(out var value))
            {
                return value;
            }
        }

        return null;
    }
}
