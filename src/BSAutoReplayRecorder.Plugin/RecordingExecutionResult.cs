namespace BSAutoReplayRecorder.Plugin;

public sealed class RecordingExecutionResult
{
    public RecordingExecutionResult(
        bool succeeded,
        string? outputPath,
        string? error,
        string? warning,
        string? syncStatus = null,
        double? syncCorrectionMilliseconds = null,
        double? trimStartSeconds = null,
        string? syncReportPath = null)
    {
        Succeeded = succeeded;
        OutputPath = outputPath;
        Error = error;
        Warning = warning;
        SyncStatus = syncStatus;
        SyncCorrectionMilliseconds = syncCorrectionMilliseconds;
        TrimStartSeconds = trimStartSeconds;
        SyncReportPath = syncReportPath;
    }

    public bool Succeeded { get; }

    public string? OutputPath { get; }

    public string? Error { get; }

    public string? Warning { get; }

    public string? SyncStatus { get; }

    public double? SyncCorrectionMilliseconds { get; }

    public double? TrimStartSeconds { get; }

    public string? SyncReportPath { get; }
}
