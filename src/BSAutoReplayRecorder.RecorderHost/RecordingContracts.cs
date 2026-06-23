namespace BSAutoReplayRecorder.RecorderHost;

public sealed class StartRecordingRequest
{
    public string? OutputBaseName { get; set; }

    public string? OutputDirectory { get; set; }

    public string? WindowTitle { get; set; }

    public int? TargetProcessId { get; set; }

    public int? TargetFps { get; set; }

    public int? CaptureWidth { get; set; }

    public int? CaptureHeight { get; set; }

    public string? Encoder { get; set; }

    public int? VideoBitrateKbps { get; set; }

    public string? OutputFormat { get; set; }

    public int? MonitorIndex { get; set; }

    public string? QualityMode { get; set; }

    public string? CaptureEngine { get; set; }

    public string? AudioMode { get; set; }

    public string? AudioDeviceName { get; set; }

    public int? AudioBitrateKbps { get; set; }

    public int? AudioSampleRate { get; set; }

    public int? AudioChannels { get; set; }

    public string? AudioLevelMode { get; set; }

    public double? AudioTargetLevelDb { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class StopRecordingRequest
{
    public string? RecordingId { get; set; }

    public DateTimeOffset? ContentStartUtc { get; set; }
}

public sealed class RecordingStatusResponse
{
    public string State { get; set; } = "idle";

    public bool IsRecording { get; set; }

    public string? RecordingId { get; set; }

    public string? OutputPath { get; set; }

    public string? OutputBaseName { get; set; }

    public string? WindowTitle { get; set; }

    public int? TargetProcessId { get; set; }

    public int? TargetFps { get; set; }

    public int? CaptureWidth { get; set; }

    public int? CaptureHeight { get; set; }

    public string? Encoder { get; set; }

    public int? VideoBitrateKbps { get; set; }

    public string? OutputFormat { get; set; }

    public string? OutputExtension { get; set; }

    public int? MonitorIndex { get; set; }

    public string? QualityMode { get; set; }

    public string? EncoderPreset { get; set; }

    public string? CaptureEngine { get; set; }

    public string? AudioMode { get; set; }

    public string? AudioDeviceName { get; set; }

    public int? AudioBitrateKbps { get; set; }

    public int? AudioSampleRate { get; set; }

    public int? AudioChannels { get; set; }

    public string? AudioLevelMode { get; set; }

    public double? AudioTargetLevelDb { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public int? ProcessId { get; set; }

    public int? ExitCode { get; set; }
}

public sealed class RecordingStoppedResponse
{
    public string RecordingId { get; set; } = "";

    public string OutputPath { get; set; } = "";

    public int? ExitCode { get; set; }

    public bool ForcedKill { get; set; }

    public string SyncStatus { get; set; } = "";

    public double? SyncCorrectionMilliseconds { get; set; }

    public double? TrimStartSeconds { get; set; }

    public string SyncReportPath { get; set; } = "";
}

public sealed class RecorderHostCapabilitiesResponse
{
    public string Status { get; set; } = "ok";

    public List<CaptureEngineCapability> CaptureEngines { get; set; } = new List<CaptureEngineCapability>();

    public List<AudioModeCapability> AudioModes { get; set; } = new List<AudioModeCapability>();

    public string FfmpegPath { get; set; } = "";

    public string ProcessLoopbackCapturePath { get; set; } = "";

    public string WindowsGraphicsCapturePath { get; set; } = "";
}

public sealed class CaptureEngineCapability
{
    public string Name { get; set; } = "";

    public bool Supported { get; set; }

    public string Status { get; set; } = "";
}

public sealed class AudioModeCapability
{
    public string Name { get; set; } = "";

    public bool Supported { get; set; }

    public string Status { get; set; } = "";
}

public sealed class ErrorResponse
{
    public ErrorResponse(string error)
    {
        Error = error;
    }

    public string Error { get; }
}
