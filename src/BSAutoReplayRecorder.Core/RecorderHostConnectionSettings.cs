namespace BSAutoReplayRecorder.Core;

public sealed class RecorderHostConnectionSettings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5757";

    public string WindowTitle { get; set; } = "Beat Saber";

    public string OutputDirectory { get; set; } = "";

    public int? TargetFps { get; set; }

    public int? CaptureWidth { get; set; }

    public int? CaptureHeight { get; set; }

    public string Encoder { get; set; } = "";

    public int? VideoBitrateKbps { get; set; }

    public string OutputFormat { get; set; } = "";

    public int? MonitorIndex { get; set; }

    public string QualityMode { get; set; } = "";

    public string AudioMode { get; set; } = "";

    public string AudioDeviceName { get; set; } = "";

    public int? AudioBitrateKbps { get; set; }

    public int? AudioSampleRate { get; set; }

    public int? AudioChannels { get; set; }

    public string AudioLevelMode { get; set; } = "";

    public double? AudioTargetLevelDb { get; set; }

    public int? TargetProcessId { get; set; }

    public double TimeoutSeconds { get; set; } = 300;

    public string NormalizedBaseUrl => string.IsNullOrWhiteSpace(BaseUrl)
        ? "http://127.0.0.1:5757"
        : BaseUrl.TrimEnd('/');

    public bool ShouldSerializeNormalizedBaseUrl()
    {
        return false;
    }
}
