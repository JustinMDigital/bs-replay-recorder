using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Utility;

namespace BSAutoReplayRecorder.RecorderHost;

public sealed class FfmpegProcessRecorder
{
    private readonly RecorderHostSettings _settings;
    private readonly ILogger<FfmpegProcessRecorder> _logger;
    private readonly FfmpegExecutableResolver _ffmpegResolver = new FfmpegExecutableResolver();
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private ActiveRecording? _activeRecording;
    private RecordingStatusResponse _lastStatus = new RecordingStatusResponse();

    public FfmpegProcessRecorder(
        RecorderHostSettings settings,
        ILogger<FfmpegProcessRecorder> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RecordingStatusResponse> StartAsync(
        StartRecordingRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClearExitedRecordingNoLock();
            if (_activeRecording != null)
            {
                throw new RecordingAlreadyActiveException(
                    "Recording is already active: " + _activeRecording.RecordingId);
            }

            var outputBaseName = FileNameSanitizer.SanitizeBaseName(
                string.IsNullOrWhiteSpace(request.OutputBaseName)
                    ? "recording-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")
                    : request.OutputBaseName);
            var resolvedOptions = ResolveRecordingOptions(request);
            var outputPath = CreateOutputPath(
                outputBaseName,
                request.OutputDirectory,
                resolvedOptions.OutputExtension);
            var ffmpegOutputPath = resolvedOptions.UsesProcessLoopback
                ? CreateProcessLoopbackVideoPath(outputPath)
                : outputPath;
            var arguments = BuildArguments(ffmpegOutputPath, request, resolvedOptions);
            var ffmpegPath = _ffmpegResolver.Resolve(_settings.FfmpegPath)
                             ?? throw new InvalidOperationException(
                                 "FFmpeg was not found. Set ffmpegPath in the config or BSARR_FFMPEG_PATH.");
            var processLoopbackCapturePath = resolvedOptions.UsesProcessLoopback
                ? ResolveProcessLoopbackCapturePath()
                  ?? throw new InvalidOperationException(
                      "ProcessLoopback audio requires ProcessLoopbackCapture.exe. Build tools\\ProcessLoopbackCapture or set processLoopbackCapturePath in the recorder host config.")
                : null;

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, args) => LogFfmpegLine(args.Data, isError: false);
            process.ErrorDataReceived += (_, args) => LogFfmpegLine(args.Data, isError: true);

            var videoStartedAtUtc = DateTimeOffset.UtcNow;
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start FFmpeg.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (_settings.StartupProbeMilliseconds > 0)
            {
                await Task.Delay(_settings.StartupProbeMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (process.HasExited)
            {
                var exitCode = TryGetExitCode(process);
                process.Dispose();
                throw new InvalidOperationException(
                    "FFmpeg exited during startup. ExitCode=" +
                    (exitCode.HasValue ? exitCode.Value.ToString() : "unknown") + ".");
            }

            ProcessLoopbackAudioProcess? audioProcess;
            try
            {
                if (resolvedOptions.UsesProcessLoopback)
                {
                    audioProcess = await StartProcessLoopbackCaptureAsync(
                        processLoopbackCapturePath!,
                        request.TargetProcessId!.Value,
                        CreateProcessLoopbackAudioPath(outputPath),
                        resolvedOptions.AudioSampleRate,
                        resolvedOptions.AudioChannels,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    audioProcess = null;
                }
            }
            catch
            {
                await StopProcessAsync(process, CancellationToken.None).ConfigureAwait(false);
                process.Dispose();
                throw;
            }

            var activeRecording = new ActiveRecording(
                Guid.NewGuid().ToString("N"),
                outputBaseName,
                outputPath,
                ffmpegOutputPath,
                audioProcess?.AudioPath,
                ResolveWindowTitle(request),
                request.TargetProcessId,
                resolvedOptions,
                process,
                audioProcess?.Process,
                ffmpegPath,
                videoStartedAtUtc,
                ResolveProcessLoopbackAudioOffset(videoStartedAtUtc, audioProcess?.StartedAtUtc));

            _activeRecording = activeRecording;
            _lastStatus = activeRecording.ToStatus("recording");
            _logger.LogInformation(
                "Started FFmpeg recording {RecordingId} with process {ProcessId}: {OutputPath}",
                activeRecording.RecordingId,
                process.Id,
                outputPath);
            _logger.LogInformation(
                "Recorder settings for {RecordingId}: fps={TargetFps}, size={CaptureWidth}x{CaptureHeight}, encoder={Encoder}, bitrate={VideoBitrateKbps}k, format={OutputFormat}, monitor={MonitorIndex}, quality={QualityMode}, preset={EncoderPreset}, audio={AudioMode}:{AudioDeviceName}, audioLevel={AudioLevelMode}:{AudioTargetLevelDb}",
                activeRecording.RecordingId,
                resolvedOptions.TargetFps,
                resolvedOptions.CaptureWidth,
                resolvedOptions.CaptureHeight,
                resolvedOptions.Encoder,
                resolvedOptions.VideoBitrateKbps,
                resolvedOptions.OutputFormat,
                resolvedOptions.MonitorIndex,
                resolvedOptions.QualityMode,
                resolvedOptions.EncoderPreset,
                resolvedOptions.AudioMode,
                resolvedOptions.AudioDeviceName,
                resolvedOptions.AudioLevelMode,
                resolvedOptions.AudioTargetLevelDb);

            _ = MonitorExitAsync(activeRecording);
            return _lastStatus;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecordingStoppedResponse> StopAsync(
        StopRecordingRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ActiveRecording activeRecording;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClearExitedRecordingNoLock();
            if (_activeRecording == null)
            {
                throw new RecordingNotActiveException("No recording is active.");
            }

            if (!string.IsNullOrWhiteSpace(request.RecordingId) &&
                !string.Equals(request.RecordingId, _activeRecording.RecordingId, StringComparison.OrdinalIgnoreCase))
            {
                throw new RecordingNotActiveException(
                    "Active recording id does not match requested id: " + request.RecordingId);
            }

            activeRecording = _activeRecording;
            _lastStatus = activeRecording.ToStatus("stopping");
        }
        finally
        {
            _gate.Release();
        }

        var forcedKill = await StopProcessAsync(activeRecording.Process, cancellationToken).ConfigureAwait(false);
        var audioForcedKill = false;
        if (activeRecording.AudioProcess != null)
        {
            audioForcedKill = await StopProcessAsync(activeRecording.AudioProcess, cancellationToken).ConfigureAwait(false);
        }

        ProcessLoopbackMuxResult? muxResult = null;
        if (activeRecording.Options.UsesProcessLoopback)
        {
            muxResult = await MuxProcessLoopbackRecordingAsync(
                activeRecording,
                request.ContentStartUtc,
                cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_activeRecording, activeRecording))
            {
                _activeRecording = null;
            }

            var exitCode = TryGetExitCode(activeRecording.Process);
            _lastStatus = activeRecording.ToStatus("idle", isRecording: false);
            _lastStatus.ExitCode = exitCode;

            _logger.LogInformation(
                "Stopped FFmpeg recording {RecordingId}. ExitCode={ExitCode}, ForcedKill={ForcedKill}, AudioForcedKill={AudioForcedKill}",
                activeRecording.RecordingId,
                exitCode,
                forcedKill,
                audioForcedKill);

            return new RecordingStoppedResponse
            {
                RecordingId = activeRecording.RecordingId,
                OutputPath = activeRecording.OutputPath,
                ExitCode = exitCode,
                ForcedKill = forcedKill,
                SyncStatus = muxResult?.SyncStatus ?? "",
                SyncCorrectionMilliseconds = muxResult?.SyncCorrectionMilliseconds,
                TrimStartSeconds = muxResult?.TrimStartSeconds,
                SyncReportPath = muxResult?.SyncReportPath ?? ""
            };
        }
        finally
        {
            _gate.Release();
            activeRecording.AudioProcess?.Dispose();
            activeRecording.Process.Dispose();
        }
    }

    public RecordingStatusResponse GetStatus()
    {
        _gate.Wait();
        try
        {
            ClearExitedRecordingNoLock();
            return _activeRecording?.ToStatus("recording") ?? _lastStatus;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MonitorExitAsync(ActiveRecording activeRecording)
    {
        try
        {
            await activeRecording.Process.WaitForExitAsync().ConfigureAwait(false);

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_activeRecording, activeRecording))
                {
                    _activeRecording = null;
                    _lastStatus = activeRecording.ToStatus("idle", isRecording: false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed while monitoring FFmpeg recording process.");
        }
    }

    private async Task<bool> StopProcessAsync(Process process, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return false;
        }

        try
        {
            await process.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send graceful FFmpeg stop command.");
        }

        var timeout = TimeSpan.FromSeconds(_settings.StopTimeoutSeconds);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested &&
                                                !cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return true;
        }
    }

    private void ClearExitedRecordingNoLock()
    {
        if (_activeRecording == null || !_activeRecording.Process.HasExited)
        {
            return;
        }

        _lastStatus = _activeRecording.ToStatus("idle", isRecording: false);
        _activeRecording.AudioProcess?.Dispose();
        _activeRecording.Process.Dispose();
        _activeRecording = null;
    }

    private async Task<ProcessLoopbackAudioProcess> StartProcessLoopbackCaptureAsync(
        string executablePath,
        int targetProcessId,
        string audioPath,
        int audioSampleRate,
        int audioChannels,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(audioPath) ?? ".");
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = targetProcessId.ToString(CultureInfo.InvariantCulture) +
                        " includetree " +
                        QuoteArgument(audioPath) +
                        " " +
                        audioSampleRate.ToString(CultureInfo.InvariantCulture) +
                        " " +
                        audioChannels.ToString(CultureInfo.InvariantCulture),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var startupCapture = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) => LogProcessLoopbackLine(args.Data, isError: false, startupCapture);
        process.ErrorDataReceived += (_, args) => LogProcessLoopbackLine(args.Data, isError: true);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start ProcessLoopbackCapture.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var startedAtUtc = await ReadProcessLoopbackStartupAsync(process, startupCapture.Task, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Started process-loopback audio capture for target process {TargetProcessId}: {AudioPath}",
                targetProcessId,
                audioPath);
            return new ProcessLoopbackAudioProcess(process, audioPath, startedAtUtc);
        }
        catch
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // The process may have exited between the HasExited check and Kill.
                }
            }

            process.Dispose();
            throw;
        }
    }

    private async Task<DateTimeOffset> ReadProcessLoopbackStartupAsync(
        Process process,
        Task<DateTimeOffset> startupTask,
        CancellationToken cancellationToken)
    {
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var completedTask = await Task.WhenAny(startupTask, timeoutTask, exitTask).ConfigureAwait(false);
        if (completedTask == startupTask)
        {
            return await startupTask.ConfigureAwait(false);
        }

        if (completedTask == exitTask || process.HasExited)
        {
            await exitTask.ConfigureAwait(false);
            throw new InvalidOperationException(
                "ProcessLoopbackCapture exited during startup. ExitCode=" +
                (TryGetExitCode(process)?.ToString(CultureInfo.InvariantCulture) ?? "unknown") + ".");
        }

        _logger.LogWarning("ProcessLoopbackCapture did not report startup within 2 seconds; using current time for audio sync.");
        return DateTimeOffset.UtcNow;
    }

    private static bool TryParseProcessLoopbackStartedAt(string line, out DateTimeOffset startedAtUtc)
    {
        const string prefix = "CaptureStartedUtc=";
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            DateTimeOffset.TryParse(line.Substring(prefix.Length), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedStartedAtUtc))
        {
            startedAtUtc = parsedStartedAtUtc.ToUniversalTime();
            return true;
        }

        startedAtUtc = default;
        return false;
    }

    private string CreateOutputPath(
        string outputBaseName,
        string? outputDirectoryOverride,
        string outputExtension)
    {
        var outputDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(outputDirectoryOverride)
                ? _settings.OutputDirectory
                : outputDirectoryOverride);
        Directory.CreateDirectory(outputDirectory);

        var candidate = Path.Combine(outputDirectory, outputBaseName + outputExtension);
        if (_settings.OverwriteExisting || !File.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 2; index < 10_000; index++)
        {
            candidate = Path.Combine(
                outputDirectory,
                outputBaseName + " (" + index + ")" + outputExtension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find an available output filename for " + outputBaseName + ".");
    }

    private static string CreateProcessLoopbackVideoPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory, fileName + ".video" + Path.GetExtension(outputPath));
    }

    private static string CreateProcessLoopbackAudioPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory, fileName + ".process-loopback.wav");
    }

    private string? ResolveProcessLoopbackCapturePath()
    {
        foreach (var candidate in EnumerateProcessLoopbackCapturePathCandidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateProcessLoopbackCapturePathCandidates()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ProcessLoopbackCapturePath))
        {
            yield return _settings.ProcessLoopbackCapturePath;
        }

        yield return Path.Combine(Environment.CurrentDirectory, "tools", "ProcessLoopbackCapture", "x64", "Release", "ProcessLoopbackCapture.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "tools", "ProcessLoopbackCapture", "x64", "Debug", "ProcessLoopbackCapture.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "tools", "ProcessLoopbackCapture.Managed", "bin", "Release", "net10.0-windows10.0.20348.0", "win-x64", "ProcessLoopbackCapture.exe");
        yield return Path.Combine(Environment.CurrentDirectory, "tools", "ProcessLoopbackCapture.Managed", "bin", "Debug", "net10.0-windows10.0.20348.0", "win-x64", "ProcessLoopbackCapture.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "ProcessLoopbackCapture.exe");
        yield return "ProcessLoopbackCapture.exe";
    }

    private async Task<ProcessLoopbackMuxResult> MuxProcessLoopbackRecordingAsync(
        ActiveRecording activeRecording,
        DateTimeOffset? contentStartUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(activeRecording.AudioPath) || !File.Exists(activeRecording.AudioPath))
        {
            throw new InvalidOperationException("ProcessLoopback audio sidecar was not created: " + activeRecording.AudioPath);
        }

        if (!File.Exists(activeRecording.VideoOutputPath))
        {
            throw new InvalidOperationException("ProcessLoopback video sidecar was not created: " + activeRecording.VideoOutputPath);
        }

        if (!contentStartUtc.HasValue)
        {
            throw new InvalidOperationException("Automatic sync requires contentStartUtc when stopping a ProcessLoopback recording.");
        }

        var trimStart = contentStartUtc.Value.ToUniversalTime() - activeRecording.StartedAtUtc.ToUniversalTime();
        if (trimStart < TimeSpan.Zero)
        {
            trimStart = TimeSpan.Zero;
        }

        var analysis = await SyncMarkerAnalyzer
            .AnalyzeFilesAsync(
                activeRecording.FfmpegPath,
                activeRecording.VideoOutputPath,
                activeRecording.AudioPath,
                cancellationToken)
            .ConfigureAwait(false);
        var reportPath = CreateProcessLoopbackSyncReportPath(activeRecording.OutputPath);
        WriteProcessLoopbackSyncReport(reportPath, activeRecording, trimStart, analysis);

        var arguments = CreateProcessLoopbackExactMuxArguments(
            activeRecording.VideoOutputPath,
            activeRecording.AudioPath,
            activeRecording.OutputPath,
            activeRecording.Options,
            TimeSpan.FromSeconds(analysis.AudioOffsetSeconds),
            trimStart);
        var startInfo = new ProcessStartInfo
        {
            FileName = activeRecording.FfmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) => LogFfmpegLine(args.Data, isError: false);
        process.ErrorDataReceived += (_, args) => LogFfmpegLine(args.Data, isError: true);

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start FFmpeg mux for ProcessLoopback recording.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("FFmpeg mux for ProcessLoopback recording failed. ExitCode=" + process.ExitCode + ".");
        }

        TryDeleteSidecar(activeRecording.VideoOutputPath);
        TryDeleteSidecar(activeRecording.AudioPath);
        return new ProcessLoopbackMuxResult(
            analysis.Status,
            analysis.SyncCorrectionMilliseconds,
            Math.Round(trimStart.TotalSeconds, 3),
            reportPath);
    }

    private static string CreateProcessLoopbackMuxArguments(
        string videoPath,
        string audioPath,
        string outputPath,
        ResolvedRecordingOptions options,
        TimeSpan audioOffset)
    {
        var audioEncoderArguments =
            "-c:a aac -b:a " + options.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) +
            "k -ar " + options.AudioSampleRate.ToString(CultureInfo.InvariantCulture) +
            " -ac " + options.AudioChannels.ToString(CultureInfo.InvariantCulture);
        var audioOutputArguments = string.IsNullOrWhiteSpace(options.AudioFilterArguments)
            ? audioEncoderArguments
            : options.AudioFilterArguments + " " + audioEncoderArguments;
        var audioInputArguments = audioOffset == TimeSpan.Zero
            ? "-i " + QuoteArgument(audioPath)
            : "-itsoffset " + FormatSeconds(audioOffset.TotalSeconds) + " -i " + QuoteArgument(audioPath);
        return "-hide_banner -y -i " + QuoteArgument(videoPath) +
               " " + audioInputArguments +
               " -map 0:v:0 -map 1:a:0 -c:v copy " +
               audioOutputArguments + " " +
               options.ContainerFlags + " " +
               QuoteArgument(outputPath);
    }

    private static string CreateProcessLoopbackExactMuxArguments(
        string videoPath,
        string audioPath,
        string outputPath,
        ResolvedRecordingOptions options,
        TimeSpan audioOffset,
        TimeSpan trimStart)
    {
        var audioEncoderArguments =
            "-c:a aac -b:a " + options.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) +
            "k -ar " + options.AudioSampleRate.ToString(CultureInfo.InvariantCulture) +
            " -ac " + options.AudioChannels.ToString(CultureInfo.InvariantCulture);
        var audioOutputArguments = string.IsNullOrWhiteSpace(options.AudioFilterArguments)
            ? audioEncoderArguments
            : options.AudioFilterArguments + " " + audioEncoderArguments;
        var audioInputArguments = audioOffset == TimeSpan.Zero
            ? "-i " + QuoteArgument(audioPath)
            : "-itsoffset " + FormatSeconds(audioOffset.TotalSeconds) + " -i " + QuoteArgument(audioPath);
        return "-hide_banner -y -i " + QuoteArgument(videoPath) +
               " " + audioInputArguments +
               " -ss " + FormatSeconds(Math.Max(0, trimStart.TotalSeconds)) +
               " -map 0:v:0 -map 1:a:0 -c:v " + options.Encoder +
               " -preset " + options.EncoderPreset +
               " -b:v " + options.VideoBitrateKbps.ToString(CultureInfo.InvariantCulture) +
               "k -pix_fmt yuv420p " +
               audioOutputArguments + " -shortest " +
               options.ContainerFlags + " " +
               QuoteArgument(outputPath);
    }

    private static string CreateProcessLoopbackSyncReportPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory, fileName + ".sync.json");
    }

    private static void WriteProcessLoopbackSyncReport(
        string reportPath,
        ActiveRecording activeRecording,
        TimeSpan trimStart,
        SyncMarkerAnalysisResult analysis)
    {
        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            recordingId = activeRecording.RecordingId,
            outputPath = activeRecording.OutputPath,
            videoSidecarPath = activeRecording.VideoOutputPath,
            audioSidecarPath = activeRecording.AudioPath,
            status = analysis.Status,
            audioOffsetSeconds = analysis.AudioOffsetSeconds,
            syncCorrectionMilliseconds = analysis.SyncCorrectionMilliseconds,
            trimStartSeconds = Math.Round(trimStart.TotalSeconds, 3),
            videoPulseTimesSeconds = analysis.VideoPulseTimesSeconds,
            audioPulseTimesSeconds = analysis.AudioPulseTimesSeconds
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions.Default));
    }

    private static TimeSpan ResolveProcessLoopbackAudioOffset(
        DateTimeOffset videoStartedAtUtc,
        DateTimeOffset? audioStartedAtUtc)
    {
        if (!audioStartedAtUtc.HasValue)
        {
            return TimeSpan.Zero;
        }

        var offset = audioStartedAtUtc.Value - videoStartedAtUtc;
        return offset.TotalMilliseconds <= 1
            ? TimeSpan.Zero
            : offset;
    }

    private static void TryDeleteSidecar(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Sidecars are disposable; keeping them is better than failing a completed recording.
        }
    }

    private string BuildArguments(
        string outputPath,
        StartRecordingRequest request,
        ResolvedRecordingOptions options)
    {
        var windowTitle = ResolveWindowTitle(request);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["output"] = QuoteArgument(outputPath),
            ["outputRaw"] = outputPath,
            ["windowTitle"] = QuoteArgument(windowTitle),
            ["windowTitleRaw"] = windowTitle,
            ["fps"] = options.TargetFps.ToString(CultureInfo.InvariantCulture),
            ["targetFps"] = options.TargetFps.ToString(CultureInfo.InvariantCulture),
            ["captureWidth"] = options.CaptureWidth.ToString(CultureInfo.InvariantCulture),
            ["captureHeight"] = options.CaptureHeight.ToString(CultureInfo.InvariantCulture),
            ["videoSize"] = options.CaptureWidth.ToString(CultureInfo.InvariantCulture) + "x" +
                            options.CaptureHeight.ToString(CultureInfo.InvariantCulture),
            ["encoder"] = options.Encoder,
            ["videoBitrateKbps"] = options.VideoBitrateKbps.ToString(CultureInfo.InvariantCulture),
            ["videoBitrate"] = options.VideoBitrateKbps.ToString(CultureInfo.InvariantCulture) + "k",
            ["outputFormat"] = options.OutputFormat,
            ["outputExtension"] = options.OutputExtension,
            ["monitorIndex"] = options.MonitorIndex.ToString(CultureInfo.InvariantCulture),
            ["qualityMode"] = options.QualityMode,
            ["encoderPreset"] = options.EncoderPreset,
            ["containerFlags"] = options.ContainerFlags,
            ["audioMode"] = options.AudioMode,
            ["audioDeviceName"] = QuoteArgument(options.AudioDeviceName),
            ["audioDeviceNameRaw"] = options.AudioDeviceName,
            ["audioInput"] = options.AudioInputArguments,
            ["audioMap"] = options.AudioMapArguments,
            ["audioOutputOptions"] = options.AudioOutputArguments,
            ["audioBitrateKbps"] = options.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture),
            ["audioBitrate"] = options.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) + "k",
            ["audioSampleRate"] = options.AudioSampleRate.ToString(CultureInfo.InvariantCulture),
            ["audioChannels"] = options.AudioChannels.ToString(CultureInfo.InvariantCulture),
            ["audioLevelMode"] = options.AudioLevelMode,
            ["audioTargetLevelDb"] = FormatAudioLevel(options.AudioTargetLevelDb),
            ["audioFilter"] = options.AudioFilterArguments
        };

        if (request.TargetProcessId.HasValue)
        {
            tokens["targetProcessId"] = request.TargetProcessId.Value.ToString();
        }

        if (request.Metadata != null)
        {
            foreach (var pair in request.Metadata)
            {
                tokens["metadata." + pair.Key] = QuoteArgument(pair.Value);
                tokens["metadataRaw." + pair.Key] = pair.Value;
            }
        }

        return Regex.Replace(
            _settings.ArgumentTemplate,
            "\\{([A-Za-z0-9_.]+)\\}",
            match =>
            {
                var token = match.Groups[1].Value;
                if (tokens.TryGetValue(token, out var value))
                {
                    return value;
                }

                throw new InvalidOperationException("Unknown FFmpeg argument template token: " + token);
            });
    }

    private ResolvedRecordingOptions ResolveRecordingOptions(StartRecordingRequest request)
    {
        var targetFps = ClampValue(request.TargetFps, fallback: _settings.DefaultTargetFps, min: 1, max: 240);
        var captureWidth = ClampValue(request.CaptureWidth, fallback: _settings.DefaultCaptureWidth, min: 320, max: 16384);
        var captureHeight = ClampValue(request.CaptureHeight, fallback: _settings.DefaultCaptureHeight, min: 180, max: 8640);
        var defaultEncoder = NormalizeArgumentToken(_settings.DefaultEncoder, "h264_nvenc");
        var encoder = NormalizeArgumentToken(request.Encoder, defaultEncoder);
        var videoBitrateKbps = ClampValue(
            request.VideoBitrateKbps,
            fallback: _settings.DefaultVideoBitrateKbps,
            min: 500,
            max: 200000);
        var outputFormat = NormalizeOutputFormat(request.OutputFormat, _settings.OutputExtension);
        var outputExtension = "." + outputFormat;
        var monitorIndex = ClampValue(request.MonitorIndex, fallback: _settings.DefaultMonitorIndex, min: 0, max: 16);
        var qualityMode = NormalizeQualityMode(request.QualityMode, _settings.DefaultQualityMode);
        var audioMode = NormalizeAudioMode(request.AudioMode, _settings.DefaultAudioMode);
        var audioDeviceName = ResolveAudioDeviceName(request.AudioDeviceName);
        var audioBitrateKbps = ClampValue(
            request.AudioBitrateKbps,
            fallback: _settings.DefaultAudioBitrateKbps,
            min: 64,
            max: 1024);
        var audioSampleRate = ClampValue(
            request.AudioSampleRate,
            fallback: _settings.DefaultAudioSampleRate,
            min: 8000,
            max: 192000);
        var audioChannels = ClampValue(
            request.AudioChannels,
            fallback: _settings.DefaultAudioChannels,
            min: 1,
            max: 8);
        var audioLevelMode = NormalizeAudioLevelMode(request.AudioLevelMode, _settings.DefaultAudioLevelMode);
        var audioTargetLevelDb = NormalizeAudioTargetLevelDb(
            request.AudioTargetLevelDb,
            _settings.DefaultAudioTargetLevelDb,
            audioLevelMode);
        if (string.Equals(audioMode, "ProcessLoopback", StringComparison.OrdinalIgnoreCase) &&
            !request.TargetProcessId.HasValue)
        {
            throw new InvalidOperationException("Audio mode ProcessLoopback requires a targetProcessId.");
        }

        var audioInputArguments = CreateAudioInputArguments(audioMode, audioDeviceName);
        var audioFilterArguments = string.Equals(audioMode, "None", StringComparison.OrdinalIgnoreCase)
            ? ""
            : CreateAudioFilterArguments(audioLevelMode, audioTargetLevelDb);
        var audioEncoderArguments =
            "-c:a aac -b:a " + audioBitrateKbps.ToString(CultureInfo.InvariantCulture) +
            "k -ar " + audioSampleRate.ToString(CultureInfo.InvariantCulture) +
            " -ac " + audioChannels.ToString(CultureInfo.InvariantCulture);
        var audioOutputArguments = string.IsNullOrWhiteSpace(audioInputArguments)
            ? ""
            : string.IsNullOrWhiteSpace(audioFilterArguments)
                ? audioEncoderArguments
                : audioFilterArguments + " " + audioEncoderArguments;
        return new ResolvedRecordingOptions(
            targetFps,
            captureWidth,
            captureHeight,
            encoder,
            videoBitrateKbps,
            outputFormat,
            outputExtension,
            monitorIndex,
            qualityMode,
            ResolveEncoderPreset(encoder, qualityMode),
            ResolveContainerFlags(outputFormat),
            audioMode,
            audioDeviceName,
            audioBitrateKbps,
            audioSampleRate,
            audioChannels,
            audioLevelMode,
            audioTargetLevelDb,
            audioInputArguments,
            audioFilterArguments,
            string.IsNullOrWhiteSpace(audioInputArguments) ? "" : "-map 1:a:0",
            audioOutputArguments);
    }

    private string ResolveWindowTitle(StartRecordingRequest request)
    {
        return string.IsNullOrWhiteSpace(request.WindowTitle)
            ? _settings.DefaultWindowTitle
            : request.WindowTitle;
    }

    private void LogFfmpegLine(string? line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (isError)
        {
            _logger.LogInformation("ffmpeg: {Line}", line);
        }
        else
        {
            _logger.LogDebug("ffmpeg: {Line}", line);
        }
    }

    private void LogProcessLoopbackLine(
        string? line,
        bool isError,
        TaskCompletionSource<DateTimeOffset>? startupCapture = null)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (!isError && startupCapture != null && TryParseProcessLoopbackStartedAt(line, out var startedAtUtc))
        {
            startupCapture.TrySetResult(startedAtUtc);
        }

        if (isError)
        {
            _logger.LogInformation("process-loopback: {Line}", line);
        }
        else
        {
            _logger.LogDebug("process-loopback: {Line}", line);
        }
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static int ClampValue(int? value, int fallback, int min, int max)
    {
        return Math.Clamp(value.GetValueOrDefault(fallback), min, max);
    }

    private static string NormalizeArgumentToken(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        return Regex.IsMatch(trimmed, "^[A-Za-z0-9_.+-]+$")
            ? trimmed
            : fallback;
    }

    private static string NormalizeQualityMode(string? value, string? fallback = "Balanced")
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "Performance", StringComparison.OrdinalIgnoreCase))
        {
            return "Performance";
        }

        if (string.Equals(trimmed, "Quality", StringComparison.OrdinalIgnoreCase))
        {
            return "Quality";
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            return "Balanced";
        }

        var fallbackTrimmed = fallback?.Trim();
        if (string.Equals(fallbackTrimmed, "Performance", StringComparison.OrdinalIgnoreCase))
        {
            return "Performance";
        }

        if (string.Equals(fallbackTrimmed, "Quality", StringComparison.OrdinalIgnoreCase))
        {
            return "Quality";
        }

        return "Balanced";
    }

    private string ResolveAudioDeviceName(string? requestDeviceName)
    {
        var trimmed = requestDeviceName?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return _settings.DefaultAudioDeviceName?.Trim() ?? "";
    }

    private static string NormalizeAudioMode(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var trimmed = candidate?.Trim();
        return string.Equals(trimmed, "ProcessLoopback", StringComparison.OrdinalIgnoreCase)
            ? "ProcessLoopback"
            : "None";
    }

    private static string NormalizeAudioLevelMode(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var trimmed = candidate?.Trim();
        if (string.Equals(trimmed, "Gain", StringComparison.OrdinalIgnoreCase))
        {
            return "Gain";
        }

        if (string.Equals(trimmed, "Off", StringComparison.OrdinalIgnoreCase))
        {
            return "Off";
        }

        return "Loudness";
    }

    private static double NormalizeAudioTargetLevelDb(double? value, double fallback, string mode)
    {
        var candidate = value.GetValueOrDefault(fallback);
        if (double.IsNaN(candidate) || double.IsInfinity(candidate))
        {
            candidate = -12;
        }

        return string.Equals(mode, "Loudness", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(candidate, -70, -5)
            : Math.Clamp(candidate, -60, 0);
    }

    private static string CreateAudioInputArguments(string audioMode, string audioDeviceName)
    {
        return "";
    }

    private static string CreateAudioFilterArguments(string audioLevelMode, double audioTargetLevelDb)
    {
        if (string.Equals(audioLevelMode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var target = FormatAudioLevel(audioTargetLevelDb);
        var filter = string.Equals(audioLevelMode, "Gain", StringComparison.OrdinalIgnoreCase)
            ? "volume=" + target + "dB"
            : "loudnorm=I=" + target + ":TP=-1.5:LRA=11";
        return "-af " + filter;
    }

    private static string FormatAudioLevel(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatSeconds(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string NormalizeOutputFormat(string? value, string fallbackExtension)
    {
        var fallback = fallbackExtension.Trim().TrimStart('.').ToLowerInvariant();
        var trimmed = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().TrimStart('.').ToLowerInvariant();
        if (string.Equals(trimmed, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            return "mp4";
        }

        if (string.Equals(trimmed, "mkv", StringComparison.OrdinalIgnoreCase))
        {
            return "mkv";
        }

        return "mkv";
    }

    private static string ResolveEncoderPreset(string encoder, string qualityMode)
    {
        if (encoder.IndexOf("nvenc", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (string.Equals(qualityMode, "Performance", StringComparison.OrdinalIgnoreCase))
            {
                return "p1";
            }

            return string.Equals(qualityMode, "Quality", StringComparison.OrdinalIgnoreCase) ? "p6" : "p4";
        }

        if (string.Equals(qualityMode, "Performance", StringComparison.OrdinalIgnoreCase))
        {
            return "ultrafast";
        }

        return string.Equals(qualityMode, "Quality", StringComparison.OrdinalIgnoreCase) ? "medium" : "veryfast";
    }

    private static string ResolveContainerFlags(string outputFormat)
    {
        return string.Equals(outputFormat, "mp4", StringComparison.OrdinalIgnoreCase)
            ? "-movflags +faststart"
            : "";
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed class ActiveRecording
    {
        public ActiveRecording(
            string recordingId,
            string outputBaseName,
            string outputPath,
            string videoOutputPath,
            string? audioPath,
            string windowTitle,
            int? targetProcessId,
            ResolvedRecordingOptions options,
            Process process,
            Process? audioProcess,
            string ffmpegPath,
            DateTimeOffset startedAtUtc,
            TimeSpan processLoopbackAudioOffset)
        {
            RecordingId = recordingId;
            OutputBaseName = outputBaseName;
            OutputPath = outputPath;
            VideoOutputPath = videoOutputPath;
            AudioPath = audioPath;
            WindowTitle = windowTitle;
            TargetProcessId = targetProcessId;
            Options = options;
            Process = process;
            AudioProcess = audioProcess;
            FfmpegPath = ffmpegPath;
            StartedAtUtc = startedAtUtc;
            ProcessLoopbackAudioOffset = processLoopbackAudioOffset;
        }

        public string RecordingId { get; }

        public string OutputBaseName { get; }

        public string OutputPath { get; }

        public string VideoOutputPath { get; }

        public string? AudioPath { get; }

        public string WindowTitle { get; }

        public int? TargetProcessId { get; }

        public ResolvedRecordingOptions Options { get; }

        public Process Process { get; }

        public Process? AudioProcess { get; }

        public string FfmpegPath { get; }

        public DateTimeOffset StartedAtUtc { get; }

        public TimeSpan ProcessLoopbackAudioOffset { get; }

        public RecordingStatusResponse ToStatus(string state, bool? isRecording = null)
        {
            return new RecordingStatusResponse
            {
                State = state,
                IsRecording = isRecording ?? !Process.HasExited,
                RecordingId = RecordingId,
                OutputPath = OutputPath,
                OutputBaseName = OutputBaseName,
                WindowTitle = WindowTitle,
                TargetProcessId = TargetProcessId,
                TargetFps = Options.TargetFps,
                CaptureWidth = Options.CaptureWidth,
                CaptureHeight = Options.CaptureHeight,
                Encoder = Options.Encoder,
                VideoBitrateKbps = Options.VideoBitrateKbps,
                OutputFormat = Options.OutputFormat,
                OutputExtension = Options.OutputExtension,
                MonitorIndex = Options.MonitorIndex,
                QualityMode = Options.QualityMode,
                EncoderPreset = Options.EncoderPreset,
                AudioMode = Options.AudioMode,
                AudioDeviceName = Options.AudioDeviceName,
                AudioBitrateKbps = Options.AudioBitrateKbps,
                AudioSampleRate = Options.AudioSampleRate,
                AudioChannels = Options.AudioChannels,
                AudioLevelMode = Options.AudioLevelMode,
                AudioTargetLevelDb = Options.AudioTargetLevelDb,
                StartedAtUtc = StartedAtUtc,
                ProcessId = Process.Id,
                ExitCode = TryGetExitCode(Process)
            };
        }
    }

    private sealed class ResolvedRecordingOptions
    {
        public ResolvedRecordingOptions(
            int targetFps,
            int captureWidth,
            int captureHeight,
            string encoder,
            int videoBitrateKbps,
            string outputFormat,
            string outputExtension,
            int monitorIndex,
            string qualityMode,
            string encoderPreset,
            string containerFlags,
            string audioMode,
            string audioDeviceName,
            int audioBitrateKbps,
            int audioSampleRate,
            int audioChannels,
            string audioLevelMode,
            double audioTargetLevelDb,
            string audioInputArguments,
            string audioFilterArguments,
            string audioMapArguments,
            string audioOutputArguments)
        {
            TargetFps = targetFps;
            CaptureWidth = captureWidth;
            CaptureHeight = captureHeight;
            Encoder = encoder;
            VideoBitrateKbps = videoBitrateKbps;
            OutputFormat = outputFormat;
            OutputExtension = outputExtension;
            MonitorIndex = monitorIndex;
            QualityMode = qualityMode;
            EncoderPreset = encoderPreset;
            ContainerFlags = containerFlags;
            AudioMode = audioMode;
            AudioDeviceName = audioDeviceName;
            AudioBitrateKbps = audioBitrateKbps;
            AudioSampleRate = audioSampleRate;
            AudioChannels = audioChannels;
            AudioLevelMode = audioLevelMode;
            AudioTargetLevelDb = audioTargetLevelDb;
            AudioInputArguments = audioInputArguments;
            AudioFilterArguments = audioFilterArguments;
            AudioMapArguments = audioMapArguments;
            AudioOutputArguments = audioOutputArguments;
        }

        public int TargetFps { get; }

        public int CaptureWidth { get; }

        public int CaptureHeight { get; }

        public string Encoder { get; }

        public int VideoBitrateKbps { get; }

        public string OutputFormat { get; }

        public string OutputExtension { get; }

        public int MonitorIndex { get; }

        public string QualityMode { get; }

        public string EncoderPreset { get; }

        public string ContainerFlags { get; }

        public string AudioMode { get; }

        public string AudioDeviceName { get; }

        public int AudioBitrateKbps { get; }

        public int AudioSampleRate { get; }

        public int AudioChannels { get; }

        public string AudioLevelMode { get; }

        public double AudioTargetLevelDb { get; }

        public string AudioInputArguments { get; }

        public string AudioFilterArguments { get; }

        public string AudioMapArguments { get; }

        public string AudioOutputArguments { get; }

        public bool UsesProcessLoopback => string.Equals(AudioMode, "ProcessLoopback", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProcessLoopbackAudioProcess
    {
        public ProcessLoopbackAudioProcess(Process process, string audioPath, DateTimeOffset startedAtUtc)
        {
            Process = process;
            AudioPath = audioPath;
            StartedAtUtc = startedAtUtc;
        }

        public Process Process { get; }

        public string AudioPath { get; }

        public DateTimeOffset StartedAtUtc { get; }
    }

    private sealed class ProcessLoopbackMuxResult
    {
        public ProcessLoopbackMuxResult(
            string syncStatus,
            double syncCorrectionMilliseconds,
            double trimStartSeconds,
            string syncReportPath)
        {
            SyncStatus = syncStatus;
            SyncCorrectionMilliseconds = syncCorrectionMilliseconds;
            TrimStartSeconds = trimStartSeconds;
            SyncReportPath = syncReportPath;
        }

        public string SyncStatus { get; }

        public double SyncCorrectionMilliseconds { get; }

        public double TrimStartSeconds { get; }

        public string SyncReportPath { get; }
    }
}
