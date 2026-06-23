namespace BSAutoReplayRecorder.ControlPanel;

public sealed class BenchmarkState
{
    public bool IsRunning { get; set; }

    public bool CancellationRequested { get; set; }

    public string Status { get; set; } = "Idle";

    public string RunId { get; set; } = "";

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public int ActiveConcurrency { get; set; }

    public int MaxConcurrency { get; set; }

    public List<int> SelectedConcurrencies { get; set; } = new List<int>();

    public int? RecommendedWorkerCount { get; set; }

    public string FailureReason { get; set; } = "";

    public string OutputDirectory { get; set; } = "";

    public string ReportPath { get; set; } = "";

    public List<string> SourceQueueItemIds { get; set; } = new List<string>();

    public BenchmarkSettingsSnapshot SettingsSnapshot { get; set; } = new BenchmarkSettingsSnapshot();

    public List<BenchmarkPassResult> Passes { get; set; } = new List<BenchmarkPassResult>();
}

public sealed class BenchmarkStartRequest
{
    public List<int>? ConcurrencyLevels { get; set; }
}

public sealed class BenchmarkSettingsSnapshot
{
    public int EnabledWorkerCount { get; set; }

    public int TargetFps { get; set; }

    public int CaptureWidth { get; set; }

    public int CaptureHeight { get; set; }

    public string Encoder { get; set; } = "";

    public int VideoBitrateKbps { get; set; }

    public string OutputFormat { get; set; } = "";

    public int MonitorIndex { get; set; }

    public string QualityMode { get; set; } = "";

    public string CaptureEngine { get; set; } = "";

    public string AudioMode { get; set; } = "";

    public bool RequireAudioForRun { get; set; }

    public int AudioBitrateKbps { get; set; }

    public int AudioSampleRate { get; set; }

    public int AudioChannels { get; set; }

    public string AudioLevelMode { get; set; } = "";

    public double AudioTargetLevelDb { get; set; }

    public double LagSpikeStartupGraceSeconds { get; set; }

    public double DelayBetweenRecordingsSeconds { get; set; }
}

public sealed class BenchmarkPassResult
{
    public int Concurrency { get; set; }

    public string Status { get; set; } = "Queued";

    public bool Passed { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public double? MinimumFramesPerSecond { get; set; }

    public double? AverageFramesPerSecond { get; set; }

    public string OutputSummary { get; set; } = "";

    public string FailureReason { get; set; } = "";

    public List<BenchmarkAssignmentResult> Assignments { get; set; } = new List<BenchmarkAssignmentResult>();
}

public sealed class BenchmarkAssignmentResult
{
    public string AssignmentId { get; set; } = "";

    public string SourceReplayId { get; set; } = "";

    public string ReplayLabel { get; set; } = "";

    public int InstanceIndex { get; set; }

    public string InstanceName { get; set; } = "";

    public string WorkerId { get; set; } = "";

    public string Status { get; set; } = "Queued";

    public bool Passed { get; set; }

    public DateTimeOffset? AssignedAtUtc { get; set; }

    public DateTimeOffset? RecordingStartedAtUtc { get; set; }

    public DateTimeOffset? FinalizingStartedAtUtc { get; set; }

    public DateTimeOffset? FinalizingCompletedAtUtc { get; set; }

    public double? FinalizationSeconds { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }


    public string OutputPath { get; set; } = "";

    public string Error { get; set; } = "";

    public string Warning { get; set; } = "";

    public string SyncStatus { get; set; } = "";

    public double? SyncCorrectionMilliseconds { get; set; }

    public double? TrimStartSeconds { get; set; }

    public string SyncReportPath { get; set; } = "";

    public double? MinimumFramesPerSecond { get; set; }

    public double? AverageFramesPerSecond { get; set; }

    public int HeartbeatCount { get; set; }

    public int SampledFrameCount { get; set; }
}
