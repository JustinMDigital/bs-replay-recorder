namespace BSAutoReplayRecorder.Core;

public sealed class RecordingStopResult
{
    public RecordingStopResult(
        string? outputPath,
        string syncStatus = "",
        double? syncCorrectionMilliseconds = null,
        double? trimStartSeconds = null,
        string? syncReportPath = null)
    {
        OutputPath = outputPath;
        SyncStatus = syncStatus;
        SyncCorrectionMilliseconds = syncCorrectionMilliseconds;
        TrimStartSeconds = trimStartSeconds;
        SyncReportPath = syncReportPath;
    }

    public string? OutputPath { get; }

    public string SyncStatus { get; }

    public double? SyncCorrectionMilliseconds { get; }

    public double? TrimStartSeconds { get; }

    public string? SyncReportPath { get; }
}
