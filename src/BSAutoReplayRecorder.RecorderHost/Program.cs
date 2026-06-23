using System.Text.Json;
using BSAutoReplayRecorder.RecorderHost;

var options = CommandLineOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(CommandLineOptions.GetHelpText());
    return;
}

switch (options.Command)
{
    case "serve":
        await RunServerAsync(options).ConfigureAwait(false);
        break;
    case "init-config":
        var settings = new RecorderHostSettings();
        settings.Save(options.ConfigPath, options.Force);
        Console.WriteLine("Wrote recorder host config: " + Path.GetFullPath(options.ConfigPath));
        break;
    case "print-default-config":
        Console.WriteLine(JsonSerializer.Serialize(new RecorderHostSettings(), JsonOptions.Default));
        break;
    case "probe":
        RunProbe(options);
        break;
    case "record-once":
        await RecordOnceAsync(options).ConfigureAwait(false);
        break;
    case "benchmark":
        await BenchmarkAsync(options).ConfigureAwait(false);
        break;
    default:
        throw new InvalidOperationException("Unknown command: " + options.Command);
}

static async Task RunServerAsync(CommandLineOptions options)
{
    var settings = RecorderHostSettings.Load(options.ConfigPath, options.ConfigPathWasProvided);

    var builder = WebApplication.CreateBuilder(Array.Empty<string>());
    builder.WebHost.UseUrls(settings.BindUrl);
    builder.Services.AddSingleton(settings);
    builder.Services.AddSingleton<FfmpegProcessRecorder>();

    var app = builder.Build();
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    app.MapGet("/capabilities", (RecorderHostSettings recorderHostSettings) =>
        Results.Ok(RecorderHostCapabilities.Create(recorderHostSettings)));
    app.MapGet("/status", (FfmpegProcessRecorder recorder) => Results.Ok(recorder.GetStatus()));
    app.MapPost(
        "/recordings/start",
        async (StartRecordingRequest request, FfmpegProcessRecorder recorder, CancellationToken cancellationToken) =>
            await StartRecordingAsync(request, recorder, cancellationToken).ConfigureAwait(false));
    app.MapPost(
        "/recordings/stop",
        async (StopRecordingRequest request, FfmpegProcessRecorder recorder, CancellationToken cancellationToken) =>
            await StopRecordingAsync(request, recorder, cancellationToken).ConfigureAwait(false));

    app.Logger.LogInformation("Recorder host listening on {BindUrl}", settings.BindUrl);
    await app.RunAsync().ConfigureAwait(false);
}

static void RunProbe(CommandLineOptions options)
{
    var settings = RecorderHostSettings.Load(options.ConfigPath, options.ConfigPathWasProvided);
    var ffmpegResolver = new FfmpegExecutableResolver();
    var ffmpegPath = ffmpegResolver.Resolve(settings.FfmpegPath);
    var candidates = ffmpegResolver.FindCandidates(settings.FfmpegPath);

    Console.WriteLine("BindUrl: " + settings.BindUrl);
    Console.WriteLine("OutputDirectory: " + Path.GetFullPath(settings.OutputDirectory));
    Console.WriteLine("ConfiguredFFmpeg: " + settings.FfmpegPath);
    Console.WriteLine("ResolvedFFmpeg: " + (ffmpegPath ?? "not found"));
    foreach (var candidate in candidates)
    {
        Console.WriteLine("FFmpegCandidate: " + candidate);
    }

    Console.WriteLine("ArgumentTemplate: " + settings.ArgumentTemplate);
    Console.WriteLine("DefaultCaptureEngine: " + settings.DefaultCaptureEngine);
    Console.WriteLine("DefaultWindowTitle: " + settings.DefaultWindowTitle);
    Console.WriteLine("StartupProbeMilliseconds: " + settings.StartupProbeMilliseconds);

    if (ffmpegPath == null)
    {
        Environment.ExitCode = 1;
    }
}

static async Task<IResult> StartRecordingAsync(
    StartRecordingRequest request,
    FfmpegProcessRecorder recorder,
    CancellationToken cancellationToken)
{
    try
    {
        var status = await recorder.StartAsync(request, cancellationToken).ConfigureAwait(false);
        return Results.Ok(status);
    }
    catch (RecordingAlreadyActiveException ex)
    {
        return Results.Conflict(new ErrorResponse(ex.Message));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
}

static async Task<IResult> StopRecordingAsync(
    StopRecordingRequest request,
    FfmpegProcessRecorder recorder,
    CancellationToken cancellationToken)
{
    try
    {
        var stopped = await recorder.StopAsync(request, cancellationToken).ConfigureAwait(false);
        return Results.Ok(stopped);
    }
    catch (RecordingNotActiveException ex)
    {
        return Results.Conflict(new ErrorResponse(ex.Message));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
}

static async Task RecordOnceAsync(CommandLineOptions options)
{
    var settings = RecorderHostSettings.Load(options.ConfigPath, options.ConfigPathWasProvided);
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });

    var recorder = new FfmpegProcessRecorder(
        settings,
        loggerFactory.CreateLogger<FfmpegProcessRecorder>());

    var start = await recorder.StartAsync(
        CreateStartRequest(options, options.OutputBaseName, options.WindowTitle),
        CancellationToken.None).ConfigureAwait(false);

    Console.WriteLine("Started recording " + start.RecordingId + ": " + start.OutputPath);
    await Task.Delay(options.Duration).ConfigureAwait(false);

    try
    {
        var stopped = await recorder.StopAsync(
            new StopRecordingRequest { RecordingId = start.RecordingId },
            CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine("Stopped recording " + stopped.RecordingId + ": " + stopped.OutputPath);
    }
    catch (RecordingNotActiveException ex)
    {
        var status = recorder.GetStatus();
        Console.WriteLine("Recording exited before stop: " + ex.Message);
        if (!string.IsNullOrWhiteSpace(status.OutputPath))
        {
            Console.WriteLine("Output path: " + status.OutputPath);
        }

        Environment.ExitCode = 1;
    }
}

static async Task BenchmarkAsync(CommandLineOptions options)
{
    if (options.ConfigPaths.Count == 0)
    {
        throw new InvalidOperationException("Benchmark requires at least one --config path.");
    }

    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole();
        builder.SetMinimumLevel(LogLevel.Warning);
    });

    var starts = new List<(int Index, FfmpegProcessRecorder Recorder, RecordingStatusResponse Start)>();
    var startedRecorders = new List<(int Index, FfmpegProcessRecorder Recorder, string? RecordingId)>();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        var startTasks = new List<Task<(int Index, FfmpegProcessRecorder Recorder, RecordingStatusResponse Start)>>();
        for (var index = 0; index < options.ConfigPaths.Count; index++)
        {
            var settings = RecorderHostSettings.Load(options.ConfigPaths[index], requireExists: true);
            var recorder = new FfmpegProcessRecorder(
                settings,
                loggerFactory.CreateLogger<FfmpegProcessRecorder>());
            var outputBaseName = options.OutputBaseName + "-" + index;
            var capturedIndex = index;
            startTasks.Add(Task.Run(async () =>
            {
                var start = await recorder.StartAsync(
                    CreateStartRequest(options, outputBaseName, settings.DefaultWindowTitle),
                    CancellationToken.None).ConfigureAwait(false);
                return (capturedIndex, recorder, start);
            }));
        }

        try
        {
            starts.AddRange(await Task.WhenAll(startTasks).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            foreach (var task in startTasks)
            {
                if (task.IsCompletedSuccessfully)
                {
                    var start = task.Result;
                    startedRecorders.Add((start.Index, start.Recorder, start.Start.RecordingId));
                }
            }

            var startFailureReport = new BenchmarkReport
            {
                ConfigCount = options.ConfigPaths.Count,
                DurationSeconds = options.Duration.TotalSeconds,
                WallSeconds = stopwatch.Elapsed.TotalSeconds,
                MinFps = options.MinFps,
                Passed = false,
                Results = Enumerable.Range(0, options.ConfigPaths.Count)
                    .Select(index => BenchmarkRecordingResult.Failed(index, ex.Message))
                    .ToList()
            };
            Console.WriteLine(JsonSerializer.Serialize(startFailureReport, JsonOptions.Default));
            Environment.ExitCode = 1;
            return;
        }

        foreach (var start in starts)
        {
            startedRecorders.Add((start.Index, start.Recorder, start.Start.RecordingId));
        }

        await Task.Delay(options.Duration).ConfigureAwait(false);

        var stopTasks = starts
            .Select(start => Task.Run(async () =>
            {
                try
                {
                    var stopped = await start.Recorder.StopAsync(
                        new StopRecordingRequest { RecordingId = start.Start.RecordingId },
                        CancellationToken.None).ConfigureAwait(false);
                    return (start.Index, start.Start, Stopped: stopped, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    var status = start.Recorder.GetStatus();
                    return (
                        start.Index,
                        start.Start,
                        Stopped: new RecordingStoppedResponse
                        {
                            RecordingId = start.Start.RecordingId ?? "",
                            OutputPath = status.OutputPath ?? start.Start.OutputPath ?? "",
                            ExitCode = status.ExitCode,
                            ForcedKill = false
                        },
                        Error: ex.Message);
                }
            }))
            .ToArray();

        var stops = await Task.WhenAll(stopTasks).ConfigureAwait(false);
        stopwatch.Stop();

        var ffmpegPath = new FfmpegExecutableResolver().Resolve("ffmpeg")
                         ?? throw new InvalidOperationException("FFmpeg was not found for benchmark analysis.");
        var results = new List<BenchmarkRecordingResult>();

        foreach (var stop in stops.OrderBy(result => result.Index))
        {
            if (string.IsNullOrWhiteSpace(stop.Stopped.OutputPath))
            {
                results.Add(BenchmarkRecordingResult.Failed(
                    stop.Index,
                    string.IsNullOrWhiteSpace(stop.Error) ? "Missing output path." : stop.Error));
                continue;
            }

            var analysis = AnalyzeRecording(ffmpegPath, stop.Stopped.OutputPath, options.Duration);
            analysis.Index = stop.Index;
            analysis.OutputPath = stop.Stopped.OutputPath;
            analysis.ExitCode = stop.Stopped.ExitCode;
            analysis.ForcedKill = stop.Stopped.ForcedKill;
            analysis.Error = stop.Error;
            analysis.Passed = analysis.DecodeOk &&
                              string.IsNullOrWhiteSpace(stop.Error) &&
                              !stop.Stopped.ForcedKill &&
                              (stop.Stopped.ExitCode == null || stop.Stopped.ExitCode == 0) &&
                              analysis.CapturedFps >= options.MinFps;
            results.Add(analysis);
        }

        var report = new BenchmarkReport
        {
            ConfigCount = options.ConfigPaths.Count,
            DurationSeconds = options.Duration.TotalSeconds,
            WallSeconds = stopwatch.Elapsed.TotalSeconds,
            MinFps = options.MinFps,
            Passed = results.All(result => result.Passed),
            Results = results
        };

        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions.Default));
        if (!report.Passed)
        {
            Environment.ExitCode = 1;
        }
    }
    finally
    {
        foreach (var started in startedRecorders)
        {
            try
            {
                var status = started.Recorder.GetStatus();
                if (status.IsRecording)
                {
                    await started.Recorder.StopAsync(
                        new StopRecordingRequest { RecordingId = started.RecordingId },
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
                // Best-effort cleanup after benchmark failure.
            }
        }
    }
}

static StartRecordingRequest CreateStartRequest(
    CommandLineOptions options,
    string outputBaseName,
    string? windowTitle)
{
    return new StartRecordingRequest
    {
        OutputBaseName = outputBaseName,
        WindowTitle = windowTitle,
        TargetFps = options.TargetFps,
        CaptureWidth = options.CaptureWidth,
        CaptureHeight = options.CaptureHeight,
        Encoder = options.Encoder,
        VideoBitrateKbps = options.VideoBitrateKbps,
        OutputFormat = options.OutputFormat,
        MonitorIndex = options.MonitorIndex,
        QualityMode = options.QualityMode
    };
}

static BenchmarkRecordingResult AnalyzeRecording(
    string ffmpegPath,
    string outputPath,
    TimeSpan requestedDuration)
{
    if (!File.Exists(outputPath))
    {
        return BenchmarkRecordingResult.Failed(0, "Output file does not exist.");
    }

    var nullOutput = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
    var startInfo = new System.Diagnostics.ProcessStartInfo
    {
        FileName = ffmpegPath,
        Arguments = "-hide_banner -i " + QuoteArgument(outputPath) + " -map 0:v:0 -f null " + nullOutput,
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };

    using var process = System.Diagnostics.Process.Start(startInfo)
                        ?? throw new InvalidOperationException("Failed to start FFmpeg analysis.");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    var combined = output + Environment.NewLine + error;
    var frames = ParseLastInt(combined, "frame=\\s*(\\d+)");
    var mediaSeconds = ParseLastTimestampSeconds(combined);
    var durationSeconds = requestedDuration.TotalSeconds;
    var capturedFps = frames.HasValue && durationSeconds > 0
        ? frames.Value / durationSeconds
        : 0;

    return new BenchmarkRecordingResult
    {
        OutputPath = outputPath,
        OutputBytes = new FileInfo(outputPath).Length,
        DecodeOk = process.ExitCode == 0,
        DecodeExitCode = process.ExitCode,
        Frames = frames,
        MediaSeconds = mediaSeconds,
        CapturedFps = capturedFps,
        Passed = false
    };
}

static int? ParseLastInt(string text, string pattern)
{
    var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern);
    if (matches.Count == 0)
    {
        return null;
    }

    return int.Parse(matches[matches.Count - 1].Groups[1].Value);
}

static double? ParseLastTimestampSeconds(string text)
{
    var matches = System.Text.RegularExpressions.Regex.Matches(
        text,
        "time=(\\d+):(\\d+):(\\d+(?:\\.\\d+)?)");
    if (matches.Count == 0)
    {
        return null;
    }

    var match = matches[matches.Count - 1];
    var hours = double.Parse(match.Groups[1].Value);
    var minutes = double.Parse(match.Groups[2].Value);
    var seconds = double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
    return hours * 3600 + minutes * 60 + seconds;
}

static string QuoteArgument(string value)
{
    return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

public sealed class BenchmarkReport
{
    public int ConfigCount { get; set; }

    public double DurationSeconds { get; set; }

    public double WallSeconds { get; set; }

    public double MinFps { get; set; }

    public bool Passed { get; set; }

    public List<BenchmarkRecordingResult> Results { get; set; } = new List<BenchmarkRecordingResult>();
}

public sealed class BenchmarkRecordingResult
{
    public int Index { get; set; }

    public string OutputPath { get; set; } = "";

    public long OutputBytes { get; set; }

    public int? Frames { get; set; }

    public double? MediaSeconds { get; set; }

    public double CapturedFps { get; set; }

    public int? ExitCode { get; set; }

    public bool ForcedKill { get; set; }

    public int DecodeExitCode { get; set; }

    public bool DecodeOk { get; set; }

    public bool Passed { get; set; }

    public string? Error { get; set; }

    public static BenchmarkRecordingResult Failed(int index, string error)
    {
        return new BenchmarkRecordingResult
        {
            Index = index,
            Error = error,
            Passed = false
        };
    }
}
