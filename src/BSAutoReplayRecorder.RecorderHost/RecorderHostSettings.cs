using System.Text.Json;
using System.Text.RegularExpressions;

namespace BSAutoReplayRecorder.RecorderHost;

public sealed class RecorderHostSettings
{
    private const string DefaultArgumentTemplate =
        "-hide_banner -y -f gdigrab -draw_mouse 0 -framerate {fps} -i title={windowTitle} {audioInput} -map 0:v:0 {audioMap} -c:v {encoder} -preset {encoderPreset} -b:v {videoBitrate} -pix_fmt yuv420p {audioOutputOptions} {containerFlags} {output}";

    public string BindUrl { get; set; } = "http://127.0.0.1:5757";

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string ProcessLoopbackCapturePath { get; set; } = "";

    public string WindowsGraphicsCapturePath { get; set; } = "";

    public string OutputDirectory { get; set; } = "Recordings";

    public string OutputExtension { get; set; } = ".mkv";

    public bool OverwriteExisting { get; set; }

    public bool PreserveProcessLoopbackSidecars { get; set; }

    public double StopTimeoutSeconds { get; set; } = 30;

    public int StartupProbeMilliseconds { get; set; } = 500;

    public string DefaultWindowTitle { get; set; } = "Beat Saber";

    public int DefaultTargetFps { get; set; } = 60;

    public int DefaultCaptureWidth { get; set; } = 1920;

    public int DefaultCaptureHeight { get; set; } = 1080;

    public string DefaultEncoder { get; set; } = "h264_nvenc";

    public int DefaultVideoBitrateKbps { get; set; } = 16000;

    public int DefaultMonitorIndex { get; set; }

    public string DefaultQualityMode { get; set; } = "Balanced";

    public string DefaultCaptureEngine { get; set; } = "FFmpegDdagrab";

    public string DefaultAudioMode { get; set; } = "None";

    public string DefaultAudioDeviceName { get; set; } = "";

    public int DefaultAudioBitrateKbps { get; set; } = 192;

    public int DefaultAudioSampleRate { get; set; } = 48000;

    public int DefaultAudioChannels { get; set; } = 2;

    public string DefaultAudioLevelMode { get; set; } = "Loudness";

    public double DefaultAudioTargetLevelDb { get; set; } = -12;

    public string ArgumentTemplate { get; set; } = DefaultArgumentTemplate;

    public static RecorderHostSettings Load(string path, bool requireExists = false)
    {
        if (!File.Exists(path))
        {
            if (requireExists)
            {
                throw new FileNotFoundException("Recorder host config was not found.", path);
            }

            var settings = new RecorderHostSettings();
            settings.Normalize();
            return settings;
        }

        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<RecorderHostSettings>(json, JsonOptions.Default)
                     ?? new RecorderHostSettings();
        loaded.Normalize();
        return loaded;
    }

    public void Save(string path, bool overwrite)
    {
        if (File.Exists(path) && !overwrite)
        {
            throw new InvalidOperationException("Config already exists: " + path);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Normalize();
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions.Default));
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(BindUrl))
        {
            BindUrl = "http://127.0.0.1:5757";
        }

        BindUrl = BindUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(FfmpegPath))
        {
            FfmpegPath = "ffmpeg";
        }

        ProcessLoopbackCapturePath = ProcessLoopbackCapturePath?.Trim() ?? "";
        WindowsGraphicsCapturePath = WindowsGraphicsCapturePath?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            OutputDirectory = "Recordings";
        }

        if (string.IsNullOrWhiteSpace(OutputExtension))
        {
            OutputExtension = ".mkv";
        }

        if (!OutputExtension.StartsWith(".", StringComparison.Ordinal))
        {
            OutputExtension = "." + OutputExtension;
        }

        if (StopTimeoutSeconds <= 0)
        {
            StopTimeoutSeconds = 30;
        }

        if (StartupProbeMilliseconds < 0)
        {
            StartupProbeMilliseconds = 0;
        }

        if (string.IsNullOrWhiteSpace(DefaultWindowTitle))
        {
            DefaultWindowTitle = "Beat Saber";
        }

        DefaultTargetFps = Math.Clamp(DefaultTargetFps, 1, 240);
        DefaultCaptureWidth = Math.Clamp(DefaultCaptureWidth, 320, 16384);
        DefaultCaptureHeight = Math.Clamp(DefaultCaptureHeight, 180, 8640);

        if (string.IsNullOrWhiteSpace(DefaultEncoder))
        {
            DefaultEncoder = "h264_nvenc";
        }

        DefaultVideoBitrateKbps = Math.Clamp(DefaultVideoBitrateKbps, 500, 200000);
        DefaultMonitorIndex = Math.Clamp(DefaultMonitorIndex, 0, 16);

        if (!string.Equals(DefaultQualityMode, "Performance", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(DefaultQualityMode, "Quality", StringComparison.OrdinalIgnoreCase))
        {
            DefaultQualityMode = "Balanced";
        }

        DefaultCaptureEngine = NormalizeCaptureEngine(DefaultCaptureEngine);
        DefaultAudioMode = NormalizeAudioMode(DefaultAudioMode);
        DefaultAudioDeviceName = DefaultAudioDeviceName?.Trim() ?? "";
        DefaultAudioBitrateKbps = Math.Clamp(DefaultAudioBitrateKbps, 64, 1024);
        DefaultAudioSampleRate = Math.Clamp(DefaultAudioSampleRate, 8000, 192000);
        DefaultAudioChannels = Math.Clamp(DefaultAudioChannels, 1, 8);
        DefaultAudioLevelMode = NormalizeAudioLevelMode(DefaultAudioLevelMode);
        DefaultAudioTargetLevelDb = NormalizeAudioTargetLevelDb(DefaultAudioTargetLevelDb, DefaultAudioLevelMode);

        if (string.IsNullOrWhiteSpace(ArgumentTemplate))
        {
            ArgumentTemplate = DefaultArgumentTemplate;
        }

        ArgumentTemplate = EnsureMouseCursorDisabled(ArgumentTemplate);
    }

    private static string EnsureMouseCursorDisabled(string template)
    {
        var normalized = template.Trim();
        normalized = EnsureDdagrabMouseCursorDisabled(normalized);
        normalized = EnsureGdigrabMouseCursorDisabled(normalized);
        return normalized;
    }

    private static string EnsureDdagrabMouseCursorDisabled(string template)
    {
        return Regex.Replace(
            template,
            @"ddagrab=([^""'\s]+)",
            match =>
            {
                var options = match.Groups[1].Value;
                if (Regex.IsMatch(options, @"(^|:)draw_mouse=", RegexOptions.IgnoreCase))
                {
                    options = Regex.Replace(
                        options,
                        @"(^|:)draw_mouse=[^:]*",
                        drawMouseMatch => drawMouseMatch.Groups[1].Value + "draw_mouse=0",
                        RegexOptions.IgnoreCase);
                }
                else
                {
                    options = "draw_mouse=0:" + options;
                }

                return "ddagrab=" + options;
            },
            RegexOptions.IgnoreCase);
    }

    private static string EnsureGdigrabMouseCursorDisabled(string template)
    {
        if (!Regex.IsMatch(template, @"(^|\s)-f\s+gdigrab(?=\s|$)", RegexOptions.IgnoreCase))
        {
            return template;
        }

        if (Regex.IsMatch(template, @"(^|\s)-draw_mouse\s+\S+", RegexOptions.IgnoreCase))
        {
            return Regex.Replace(
                template,
                @"(^|\s)-draw_mouse\s+\S+",
                match => match.Groups[1].Value + "-draw_mouse 0",
                RegexOptions.IgnoreCase);
        }

        return Regex.Replace(
            template,
            @"(^|\s)(-f\s+gdigrab)(?=\s|$)",
            match => match.Groups[1].Value + match.Groups[2].Value + " -draw_mouse 0",
            RegexOptions.IgnoreCase);
    }

    private static string NormalizeAudioMode(string? value)
    {
        var trimmed = value?.Trim();
        return string.Equals(trimmed, "ProcessLoopback", StringComparison.OrdinalIgnoreCase)
            ? "ProcessLoopback"
            : "None";
    }

    internal static string NormalizeCaptureEngine(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "WindowsGraphicsCapture", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "WGC", StringComparison.OrdinalIgnoreCase))
        {
            return "WindowsGraphicsCapture";
        }

        return "FFmpegDdagrab";
    }

    private static string NormalizeAudioLevelMode(string? value)
    {
        var trimmed = value?.Trim();
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

    private static double NormalizeAudioTargetLevelDb(double value, string mode)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = -12;
        }

        return string.Equals(mode, "Loudness", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(value, -70, -5)
            : Math.Clamp(value, -60, 0);
    }
}
