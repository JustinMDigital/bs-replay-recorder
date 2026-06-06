using System.Reflection;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.RecorderHost;
using Microsoft.Extensions.Logging.Abstractions;

var tempRoot = Path.Combine(Path.GetTempPath(), "bsarr-recorder-host-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    RunCursorSuppressionNormalizationCheck();
    RunNoAudioArgumentsCheck(tempRoot);
    RunProcessLoopbackArgumentsCheck(tempRoot);
    RunProcessLoopbackRequiresTargetProcessCheck(tempRoot);
    RunProcessLoopbackMuxArgumentsCheck(tempRoot);
    RunSyncMarkerZeroOffsetCheck();
    RunSyncMarkerPositiveOffsetCheck();
    RunSyncMarkerNegativeOffsetCheck();
    RunSyncMarkerMissingAudioPulseCheck();
    RunSyncMarkerMissingVisualPulseCheck();
    RunSyncMarkerInconsistentPulseSpacingCheck();
    RunProcessLoopbackStartupTimestampCheck();
    RunProcessLoopbackAudioOffsetCheck();
    Console.WriteLine("All recorder host checks passed.");
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

static void RunNoAudioArgumentsCheck(string tempRoot)
{
    var recorder = CreateRecorder();
    var request = new StartRecordingRequest
    {
        WindowTitle = "Beat Saber",
        OutputFormat = "mkv",
        AudioMode = "None",
        AudioDeviceName = "Ignored legacy audio device"
    };

    var arguments = BuildArguments(recorder, request, Path.Combine(tempRoot, "video-only.mkv"));

    AssertContains("-map 0:v:0", arguments, "video map");
    AssertContains("-draw_mouse 0", arguments, "cursor suppression");
    AssertDoesNotContain("-f dshow", arguments, "disabled dshow input");
    AssertDoesNotContain("-map 1:a:0", arguments, "disabled audio map");
    AssertDoesNotContain("-c:a aac", arguments, "disabled audio encoder");
    AssertDoesNotContain("-movflags +faststart", arguments, "mkv container flags");
}

static void RunCursorSuppressionNormalizationCheck()
{
    var defaultSettings = new RecorderHostSettings();
    defaultSettings.Normalize();
    AssertContains(
        "-f gdigrab -draw_mouse 0 -framerate",
        defaultSettings.ArgumentTemplate,
        "default gdigrab cursor suppression");

    var legacyGdigrabSettings = new RecorderHostSettings
    {
        ArgumentTemplate = "-hide_banner -y -f gdigrab -framerate {fps} -i title={windowTitle} {output}"
    };
    legacyGdigrabSettings.Normalize();
    AssertContains(
        "-f gdigrab -draw_mouse 0 -framerate",
        legacyGdigrabSettings.ArgumentTemplate,
        "legacy gdigrab cursor suppression");

    var enabledGdigrabSettings = new RecorderHostSettings
    {
        ArgumentTemplate = "-hide_banner -y -f gdigrab -draw_mouse 1 -framerate {fps} -i desktop {output}"
    };
    enabledGdigrabSettings.Normalize();
    AssertContains(
        "-f gdigrab -draw_mouse 0 -framerate",
        enabledGdigrabSettings.ArgumentTemplate,
        "enabled gdigrab cursor suppression");

    var legacyDdagrabSettings = new RecorderHostSettings
    {
        ArgumentTemplate = "-hide_banner -y -f lavfi -i \"ddagrab=output_idx={monitorIndex}:framerate={fps}:video_size={videoSize}\" {output}"
    };
    legacyDdagrabSettings.Normalize();
    AssertContains(
        "ddagrab=draw_mouse=0:output_idx={monitorIndex}:framerate={fps}:video_size={videoSize}",
        legacyDdagrabSettings.ArgumentTemplate,
        "legacy ddagrab cursor suppression");

    var enabledDdagrabSettings = new RecorderHostSettings
    {
        ArgumentTemplate = "-hide_banner -y -f lavfi -i \"ddagrab=output_idx={monitorIndex}:draw_mouse=1:framerate={fps}\" {output}"
    };
    enabledDdagrabSettings.Normalize();
    AssertContains(
        "output_idx={monitorIndex}:draw_mouse=0:framerate={fps}",
        enabledDdagrabSettings.ArgumentTemplate,
        "enabled ddagrab cursor suppression");
}

static void RunProcessLoopbackArgumentsCheck(string tempRoot)
{
    var recorder = CreateRecorder();
    var request = new StartRecordingRequest
    {
        WindowTitle = "Beat Saber",
        OutputFormat = "mkv",
        AudioMode = "ProcessLoopback",
        AudioDeviceName = "Ignored legacy audio device",
        TargetProcessId = 1234
    };

    var arguments = BuildArguments(recorder, request, Path.Combine(tempRoot, "process-loopback.video.mkv"));

    AssertContains("-map 0:v:0", arguments, "video map");
    AssertDoesNotContain("-f dshow", arguments, "process loopback should not use dshow");
    AssertDoesNotContain("Ignored legacy audio device", arguments, "process loopback should ignore legacy audio device");
    AssertDoesNotContain("-map 1:a:0", arguments, "process loopback main pass is video-only");
    AssertDoesNotContain("-c:a aac", arguments, "process loopback main pass is video-only");
}

static void RunProcessLoopbackRequiresTargetProcessCheck(string tempRoot)
{
    var recorder = CreateRecorder();
    var request = new StartRecordingRequest
    {
        AudioMode = "ProcessLoopback"
    };

    AssertThrows<TargetInvocationException>(
        () => BuildArguments(recorder, request, Path.Combine(tempRoot, "missing-pid.mkv")),
        "process loopback missing target process guard",
        "Audio mode ProcessLoopback requires a targetProcessId.");
}

static void RunProcessLoopbackMuxArgumentsCheck(string tempRoot)
{
    var recorder = CreateRecorder();
    var request = new StartRecordingRequest
    {
        AudioMode = "ProcessLoopback",
        TargetProcessId = 1234,
        AudioBitrateKbps = 256,
        AudioSampleRate = 48000,
        AudioChannels = 2,
        AudioLevelMode = "Gain",
        AudioTargetLevelDb = -4
    };

    var options = ResolveRecordingOptions(recorder, request);
    var videoPath = Path.Combine(tempRoot, "capture.video.mkv");
    var audioPath = Path.Combine(tempRoot, "capture.process-loopback.wav");
    var outputPath = Path.Combine(tempRoot, "capture.mkv");
    var arguments = BuildExactMuxArguments(
        videoPath,
        audioPath,
        outputPath,
        options,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(1250));

    AssertContains("-i \"" + videoPath + "\" -itsoffset 0.05 -i \"" + audioPath + "\"", arguments, "mux inputs");
    AssertContains("-ss 1.25", arguments, "exact trim");
    AssertContains("-map 0:v:0 -map 1:a:0 -c:v", arguments, "mux maps");
    AssertDoesNotContain("-c:v copy", arguments, "exact mux re-encodes video");
    AssertContains("-pix_fmt yuv420p", arguments, "mux pixel format");
    AssertContains("-af volume=-4dB -c:a aac -b:a 256k -ar 48000 -ac 2", arguments, "mux audio encode");
    AssertContains("-shortest", arguments, "mux shortest");
    AssertContains("\"" + outputPath + "\"", arguments, "mux output");
}

static void RunSyncMarkerZeroOffsetCheck()
{
    var result = AnalyzeSyntheticMarker(offsetSeconds: 0);
    AssertEqual(0.0, Math.Round(result.AudioOffsetSeconds, 3), "zero sync offset");
    AssertEqual(RecordingSyncMarker.PulseCount, result.VideoPulseTimesSeconds.Count, "zero visual pulse count");
    AssertEqual(RecordingSyncMarker.PulseCount, result.AudioPulseTimesSeconds.Count, "zero audio pulse count");
}

static void RunSyncMarkerPositiveOffsetCheck()
{
    var result = AnalyzeSyntheticMarker(offsetSeconds: 0.05);
    AssertEqual(0.05, Math.Round(result.AudioOffsetSeconds, 3), "positive sync offset");
    AssertEqual(50.0, result.SyncCorrectionMilliseconds, "positive sync correction ms");
}

static void RunSyncMarkerNegativeOffsetCheck()
{
    var result = AnalyzeSyntheticMarker(offsetSeconds: -0.04);
    AssertEqual(-0.04, Math.Round(result.AudioOffsetSeconds, 3), "negative sync offset");
    AssertEqual(-40.0, result.SyncCorrectionMilliseconds, "negative sync correction ms");
}

static void RunSyncMarkerMissingAudioPulseCheck()
{
    AssertThrows<InvalidOperationException>(
        () =>
        {
            var samples = CreateSyntheticMarkerSamples(offsetSeconds: 0.02);
            Array.Clear(samples.Audio, 0, samples.Audio.Length);
            SyncMarkerAnalyzer.AnalyzeSamples(samples.Video, samples.SampleRate, samples.Audio, samples.SampleRate);
        },
        "missing audio marker",
        "peak was below");
}

static void RunSyncMarkerMissingVisualPulseCheck()
{
    AssertThrows<InvalidOperationException>(
        () =>
        {
            var samples = CreateSyntheticMarkerSamples(offsetSeconds: 0.02);
            Array.Clear(samples.Video, 0, samples.Video.Length);
            SyncMarkerAnalyzer.AnalyzeSamples(samples.Video, samples.SampleRate, samples.Audio, samples.SampleRate);
        },
        "missing visual marker",
        "peak was below");
}

static void RunSyncMarkerInconsistentPulseSpacingCheck()
{
    AssertThrows<InvalidOperationException>(
        () =>
        {
            var sampleRate = 1000;
            var video = new double[2000];
            var audio = new double[2000];
            AddVisualPulse(video, sampleRate, 0.20);
            AddVisualPulse(video, sampleRate, 0.55);
            AddVisualPulse(video, sampleRate, 1.20);
            AddAudioPulse(audio, sampleRate, 0.20);
            AddAudioPulse(audio, sampleRate, 0.55);
            AddAudioPulse(audio, sampleRate, 0.90);
            SyncMarkerAnalyzer.AnalyzeSamples(video, sampleRate, audio, sampleRate);
        },
        "inconsistent marker spacing",
        "expected spacing");
}

static void RunProcessLoopbackStartupTimestampCheck()
{
    var timestamp = DateTimeOffset.Parse("2026-06-04T16:35:41.1234567+00:00");
    var parsed = ParseProcessLoopbackStartedAt("CaptureStartedUtc=" + timestamp.ToString("O"), out var startedAtUtc);

    AssertEqual(true, parsed, "startup timestamp parsed");
    AssertEqual(timestamp.ToUniversalTime(), startedAtUtc, "startup timestamp value");

    parsed = ParseProcessLoopbackStartedAt("Capturing process-loopback audio for PID 1234.", out _);
    AssertEqual(false, parsed, "non-startup line ignored");
}

static void RunProcessLoopbackAudioOffsetCheck()
{
    var videoStarted = DateTimeOffset.Parse("2026-06-04T16:35:41.0000000+00:00");

    AssertEqual(
        TimeSpan.Zero,
        ResolveAudioOffset(videoStarted, videoStarted.AddMilliseconds(1)),
        "sub-frame startup offset ignored");
    AssertEqual(
        TimeSpan.FromMilliseconds(500),
        ResolveAudioOffset(videoStarted, videoStarted.AddMilliseconds(500)),
        "positive startup offset");
}

static FfmpegProcessRecorder CreateRecorder(RecorderHostSettings? settings = null)
{
    settings ??= new RecorderHostSettings();
    settings.Normalize();
    return new FfmpegProcessRecorder(settings, NullLogger<FfmpegProcessRecorder>.Instance);
}

static string BuildArguments(FfmpegProcessRecorder recorder, StartRecordingRequest request, string outputPath)
{
    var recorderType = typeof(FfmpegProcessRecorder);
    var buildMethod = recorderType.GetMethod(
        "BuildArguments",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(recorderType.FullName, "BuildArguments");

    var options = ResolveRecordingOptions(recorder, request);
    return (string?)buildMethod.Invoke(recorder, new[] { outputPath, request, options })
           ?? throw new InvalidOperationException("BuildArguments returned null.");
}

static object ResolveRecordingOptions(FfmpegProcessRecorder recorder, StartRecordingRequest request)
{
    var recorderType = typeof(FfmpegProcessRecorder);
    var resolveMethod = recorderType.GetMethod(
        "ResolveRecordingOptions",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(recorderType.FullName, "ResolveRecordingOptions");
    return resolveMethod.Invoke(recorder, new object[] { request })
           ?? throw new InvalidOperationException("ResolveRecordingOptions returned null.");
}

static string BuildExactMuxArguments(
    string videoPath,
    string audioPath,
    string outputPath,
    object options,
    TimeSpan audioOffset,
    TimeSpan trimStart)
{
    var recorderType = typeof(FfmpegProcessRecorder);
    var buildMethod = recorderType.GetMethod(
        "CreateProcessLoopbackExactMuxArguments",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(recorderType.FullName, "CreateProcessLoopbackExactMuxArguments");
    return (string?)buildMethod.Invoke(null, new[] { videoPath, audioPath, outputPath, options, audioOffset, trimStart })
           ?? throw new InvalidOperationException("CreateProcessLoopbackExactMuxArguments returned null.");
}

static SyncMarkerAnalysisResult AnalyzeSyntheticMarker(double offsetSeconds)
{
    var samples = CreateSyntheticMarkerSamples(offsetSeconds);
    return SyncMarkerAnalyzer.AnalyzeSamples(samples.Video, samples.SampleRate, samples.Audio, samples.SampleRate);
}

static (double[] Video, double[] Audio, int SampleRate) CreateSyntheticMarkerSamples(double offsetSeconds)
{
    var sampleRate = 1000;
    var video = new double[2000];
    var audio = new double[2000];
    for (var index = 0; index < RecordingSyncMarker.PulseCount; index++)
    {
        var visualTime = 0.20 + index * RecordingSyncMarker.PulseSpacingSeconds;
        AddVisualPulse(video, sampleRate, visualTime);
        AddAudioPulse(audio, sampleRate, visualTime - offsetSeconds);
    }

    return (video, audio, sampleRate);
}

static void AddVisualPulse(double[] samples, int sampleRate, double timeSeconds)
{
    var start = Math.Max(0, (int)Math.Round(timeSeconds * sampleRate));
    var length = Math.Max(1, (int)Math.Round(RecordingSyncMarker.PulseDurationSeconds * sampleRate));
    for (var index = start; index < Math.Min(samples.Length, start + length); index++)
    {
        samples[index] = 1.0;
    }
}

static void AddAudioPulse(double[] samples, int sampleRate, double timeSeconds)
{
    var index = Math.Max(0, Math.Min(samples.Length - 1, (int)Math.Round(timeSeconds * sampleRate)));
    samples[index] = 1.0;
}

static bool ParseProcessLoopbackStartedAt(string line, out DateTimeOffset startedAtUtc)
{
    var recorderType = typeof(FfmpegProcessRecorder);
    var parseMethod = recorderType.GetMethod(
        "TryParseProcessLoopbackStartedAt",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(recorderType.FullName, "TryParseProcessLoopbackStartedAt");
    var args = new object?[] { line, null };
    var parsed = (bool)(parseMethod.Invoke(null, args) ?? false);
    startedAtUtc = parsed ? (DateTimeOffset)args[1]! : default;
    return parsed;
}

static TimeSpan ResolveAudioOffset(DateTimeOffset videoStartedAtUtc, DateTimeOffset audioStartedAtUtc)
{
    var recorderType = typeof(FfmpegProcessRecorder);
    var offsetMethod = recorderType.GetMethod(
        "ResolveProcessLoopbackAudioOffset",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(recorderType.FullName, "ResolveProcessLoopbackAudioOffset");
    return (TimeSpan)(offsetMethod.Invoke(null, new object?[] { videoStartedAtUtc, audioStartedAtUtc })
                      ?? throw new InvalidOperationException("ResolveProcessLoopbackAudioOffset returned null."));
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            label + " failed. Expected '" + expected + "', got '" + actual + "'.");
    }
}

static void AssertContains(string expected, string actual, string label)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(label + " failed. Expected to find: " + expected + Environment.NewLine + actual);
    }
}

static void AssertDoesNotContain(string unexpected, string actual, string label)
{
    if (actual.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(label + " failed. Unexpected value: " + unexpected + Environment.NewLine + actual);
    }
}

static void AssertThrows<TException>(
    Action action,
    string label,
    string expectedMessagePart)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        if (!message.Contains(expectedMessagePart, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                label + " failed. Expected message to contain '" + expectedMessagePart + "', got '" + message + "'.");
        }

        return;
    }

    throw new InvalidOperationException(label + " failed. Expected " + typeof(TException).Name + ".");
}
