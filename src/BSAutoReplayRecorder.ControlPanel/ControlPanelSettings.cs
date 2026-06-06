using BSAutoReplayRecorder.Core;

namespace BSAutoReplayRecorder.ControlPanel;

public sealed class ControlPanelSettings
{
    public const string DefaultBeatSaberLaunchPreset = "windowed-1080p";
    public const int MinimumManagedInstanceCount = 1;
    public const int MaximumManagedInstanceCount = 4;
    public const string DefaultBeatSaberLaunchArguments =
        "-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 --no-yeet fpfc --verbose";
    public const string Windowed720pBeatSaberLaunchArguments =
        "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 --no-yeet fpfc --verbose";
    public const string Windowed1440pBeatSaberLaunchArguments =
        "-screen-fullscreen 0 -screen-width 2560 -screen-height 1440 --no-yeet fpfc --verbose";
    public const string Windowed4kBeatSaberLaunchArguments =
        "-screen-fullscreen 0 -screen-width 3840 -screen-height 2160 --no-yeet fpfc --verbose";

    public string BindUrl { get; set; } = "http://127.0.0.1:5770";

    public string WorkspaceDirectory { get; set; } = "ControlPanelWorkspace";

    public string RecordingOutputDirectory { get; set; } = "";

    public string SharedCustomLevelsDirectory { get; set; } = "";

    public string SharedCustomWipLevelsDirectory { get; set; } = "";

    public bool ShareCustomSabers { get; set; } = true;

    public string SharedCustomSabersDirectory { get; set; } = "";

    public bool ShareCustomNotes { get; set; } = true;

    public string SharedCustomNotesDirectory { get; set; } = "";

    public bool ShareCustomPlatforms { get; set; } = true;

    public string SharedCustomPlatformsDirectory { get; set; } = "";

    public bool ShareCustomAvatars { get; set; } = true;

    public string SharedCustomAvatarsDirectory { get; set; } = "";

    public bool ShareCustomWalls { get; set; } = true;

    public string SharedCustomWallsDirectory { get; set; } = "";

    public bool ShareCustomBombs { get; set; } = true;

    public string SharedCustomBombsDirectory { get; set; } = "";

    public int InstanceCount { get; set; } = 4;

    public int MaxConcurrentRecordings { get; set; } = 4;

    public bool RequireAllWorkersReady { get; set; } = true;

    public bool RequireMatchingInstanceBaseline { get; set; } = true;

    public int TargetFps { get; set; } = 60;

    public int CaptureWidth { get; set; } = 1920;

    public int CaptureHeight { get; set; } = 1080;

    public string Encoder { get; set; } = "h264_nvenc";

    public int VideoBitrateKbps { get; set; } = 16000;

    public string OutputFormat { get; set; } = "mkv";

    public int MonitorIndex { get; set; } = 1;

    public string QualityMode { get; set; } = "Balanced";

    public string AudioMode { get; set; } = "ProcessLoopback";

    public bool RequireAudioForRun { get; set; } = true;

    public int AudioBitrateKbps { get; set; } = 192;

    public int AudioSampleRate { get; set; } = 48000;

    public int AudioChannels { get; set; } = 2;

    public string AudioLevelMode { get; set; } = "Loudness";

    public double AudioTargetLevelDb { get; set; } = -12;

    public string BeatSaberInstancesRoot { get; set; } = "";

    public string BeatSaberInstanceNamePrefix { get; set; } = "I-";

    public string BeatSaberLaunchPreset { get; set; } = DefaultBeatSaberLaunchPreset;

    public string BeatSaberLaunchArguments { get; set; } = DefaultBeatSaberLaunchArguments;

    public bool ManageDisplayScale { get; set; } = true;

    public int RecordingDisplayScalePercent { get; set; } = 100;

    public int RestoreDisplayScalePercent { get; set; } = 150;

    public bool HideTaskbarDuringRun { get; set; } = true;

    public double DelayBetweenRecordingsSeconds { get; set; } = 5;

    public int GamePresentationSettingsVersion { get; set; } = 1;

    public GamePresentationSettings GamePresentation { get; set; } = new GamePresentationSettings();

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(BindUrl))
        {
            BindUrl = "http://127.0.0.1:5770";
        }

        BindUrl = BindUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(WorkspaceDirectory))
        {
            WorkspaceDirectory = "ControlPanelWorkspace";
        }

        WorkspaceDirectory = NormalizePathOrDefault(WorkspaceDirectory, "ControlPanelWorkspace");
        RecordingOutputDirectory = NormalizeRecordingOutputDirectory(RecordingOutputDirectory, WorkspaceDirectory);
        SharedCustomLevelsDirectory = NormalizePathOrDefault(
            SharedCustomLevelsDirectory,
            Path.Combine(Path.GetFullPath(WorkspaceDirectory), "SharedSongs", "CustomLevels"));
        SharedCustomWipLevelsDirectory = NormalizePathOrDefault(
            SharedCustomWipLevelsDirectory,
            Path.Combine(Path.GetFullPath(WorkspaceDirectory), "SharedSongs", "CustomWIPLevels"));
        SharedCustomSabersDirectory = NormalizeSharedContentDirectory(SharedCustomSabersDirectory, "CustomSabers");
        SharedCustomNotesDirectory = NormalizeSharedContentDirectory(SharedCustomNotesDirectory, "CustomNotes");
        SharedCustomPlatformsDirectory = NormalizeSharedContentDirectory(SharedCustomPlatformsDirectory, "CustomPlatforms");
        SharedCustomAvatarsDirectory = NormalizeSharedContentDirectory(SharedCustomAvatarsDirectory, "CustomAvatars");
        SharedCustomWallsDirectory = NormalizeSharedContentDirectory(SharedCustomWallsDirectory, "CustomWalls");
        SharedCustomBombsDirectory = NormalizeSharedContentDirectory(SharedCustomBombsDirectory, "CustomBombs");

        InstanceCount = Math.Clamp(InstanceCount, MinimumManagedInstanceCount, MaximumManagedInstanceCount);
        MaxConcurrentRecordings = InstanceCount;
        TargetFps = Math.Clamp(TargetFps, 1, 240);
        CaptureWidth = Math.Max(320, CaptureWidth);
        CaptureHeight = Math.Max(180, CaptureHeight);
        if (VideoBitrateKbps <= 0)
        {
            VideoBitrateKbps = 16000;
        }

        VideoBitrateKbps = Math.Clamp(VideoBitrateKbps, 500, 200000);
        MonitorIndex = Math.Clamp(MonitorIndex, 0, 16);

        if (string.IsNullOrWhiteSpace(Encoder))
        {
            Encoder = "h264_nvenc";
        }

        OutputFormat = NormalizeOutputFormat(OutputFormat);

        if (string.IsNullOrWhiteSpace(QualityMode))
        {
            QualityMode = "Balanced";
        }

        AudioMode = NormalizeAudioMode(AudioMode);
        AudioBitrateKbps = Math.Clamp(AudioBitrateKbps <= 0 ? 192 : AudioBitrateKbps, 64, 1024);
        AudioSampleRate = Math.Clamp(AudioSampleRate <= 0 ? 48000 : AudioSampleRate, 8000, 192000);
        AudioChannels = 2;
        AudioLevelMode = NormalizeAudioLevelMode(AudioLevelMode);
        AudioTargetLevelDb = NormalizeAudioTargetLevelDb(AudioTargetLevelDb, AudioLevelMode);

        BeatSaberInstancesRoot = NormalizePathOrDefault(
            BeatSaberInstancesRoot,
            Path.Combine(Path.GetFullPath(WorkspaceDirectory), "Instances"));
        BeatSaberInstanceNamePrefix = BeatSaberInstanceNamePrefix?.Trim() ?? "";
        BeatSaberLaunchArguments = BeatSaberLaunchArguments?.Trim() ?? "";
        RecordingDisplayScalePercent = NormalizeScalePercent(RecordingDisplayScalePercent, 100);
        RestoreDisplayScalePercent = NormalizeScalePercent(RestoreDisplayScalePercent, 150);
        DelayBetweenRecordingsSeconds = NormalizeDelayBetweenRecordingsSeconds(DelayBetweenRecordingsSeconds);
        BeatSaberLaunchPreset = NormalizeLaunchPreset();
        if (GamePresentation == null)
        {
            GamePresentation = new GamePresentationSettings();
        }

        GamePresentation.Normalize();

        if (GamePresentationSettingsVersion <= 0)
        {
            GamePresentationSettingsVersion = 1;
        }
    }

    private static string NormalizeOutputFormat(string? value)
    {
        var trimmed = value?.Trim().TrimStart('.').ToLowerInvariant();
        return trimmed == "mp4" ? "mp4" : "mkv";
    }

    private static string NormalizeAudioMode(string? value)
    {
        var trimmed = value?.Trim();
        return string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase)
            ? "None"
            : "ProcessLoopback";
    }

    private static string NormalizePathOrDefault(string? value, string fallback)
    {
        var trimmed = value?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return fallback;
        }
    }

    private static string NormalizeRecordingOutputDirectory(string? value, string workspaceDirectory)
    {
        var fallback = Path.Combine(Path.GetFullPath(workspaceDirectory), "Recordings");
        var trimmed = value?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        try
        {
            return Path.GetFullPath(Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(workspaceDirectory, trimmed));
        }
        catch
        {
            return fallback;
        }
    }

    private string NormalizeSharedContentDirectory(string? value, string folderName)
    {
        return NormalizePathOrDefault(
            value,
            Path.Combine(Path.GetFullPath(WorkspaceDirectory), "SharedContent", folderName));
    }

    private string NormalizeLaunchPreset()
    {
        var trimmed = BeatSaberLaunchPreset?.Trim().ToLowerInvariant();
        if (trimmed == "custom")
        {
            return trimmed;
        }

        if (Matches4kMonitor2x2Preset())
        {
            return "4k-monitor-2x2";
        }

        if (Matches1440pMonitor2x2Preset())
        {
            return "1440p-monitor-2x2";
        }

        if (MatchesWindowed720pPreset())
        {
            return "windowed-720p";
        }

        if (MatchesSingle4kPreset())
        {
            return "single-4k";
        }

        if (MatchesSingle1440pPreset())
        {
            return "single-1440p";
        }

        if (MatchesSingle1080pPreset())
        {
            return "single-1080p";
        }

        if (MatchesWindowed1080pPreset())
        {
            return "windowed-1080p";
        }

        return "custom";
    }

    private bool Matches4kMonitor2x2Preset()
    {
        return IsGridInstanceCount() &&
               MaxConcurrentRecordings == InstanceCount &&
               TargetFps == 60 &&
               CaptureWidth == 1920 &&
               CaptureHeight == 1080 &&
               VideoBitrateKbps == 12000 &&
               string.Equals(OutputFormat, "mkv", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Encoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(QualityMode, "Performance", StringComparison.OrdinalIgnoreCase) &&
               ManageDisplayScale &&
               RecordingDisplayScalePercent == 100 &&
               RestoreDisplayScalePercent == 150 &&
               HideTaskbarDuringRun &&
               string.Equals(BeatSaberLaunchArguments, DefaultBeatSaberLaunchArguments, StringComparison.Ordinal);
    }

    private bool Matches1440pMonitor2x2Preset()
    {
        return IsGridInstanceCount() &&
               MaxConcurrentRecordings == InstanceCount &&
               TargetFps == 60 &&
               CaptureWidth == 1280 &&
               CaptureHeight == 720 &&
               VideoBitrateKbps == 8000 &&
               string.Equals(OutputFormat, "mkv", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Encoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(QualityMode, "Performance", StringComparison.OrdinalIgnoreCase) &&
               ManageDisplayScale &&
               RecordingDisplayScalePercent == 100 &&
               RestoreDisplayScalePercent == 150 &&
               HideTaskbarDuringRun &&
               string.Equals(BeatSaberLaunchArguments, Windowed720pBeatSaberLaunchArguments, StringComparison.Ordinal);
    }

    private bool MatchesWindowed1080pPreset()
    {
        return CaptureWidth == 1920 &&
               CaptureHeight == 1080 &&
               string.Equals(BeatSaberLaunchArguments, DefaultBeatSaberLaunchArguments, StringComparison.Ordinal);
    }

    private bool IsGridInstanceCount()
    {
        return InstanceCount >= 2 && InstanceCount <= MaximumManagedInstanceCount;
    }

    private bool MatchesSingle1080pPreset()
    {
        return !ManageDisplayScale &&
               !HideTaskbarDuringRun &&
               MatchesWindowed1080pPreset();
    }

    private bool MatchesSingle1440pPreset()
    {
        return CaptureWidth == 2560 &&
               CaptureHeight == 1440 &&
               !ManageDisplayScale &&
               !HideTaskbarDuringRun &&
               string.Equals(BeatSaberLaunchArguments, Windowed1440pBeatSaberLaunchArguments, StringComparison.Ordinal);
    }

    private bool MatchesSingle4kPreset()
    {
        return CaptureWidth == 3840 &&
               CaptureHeight == 2160 &&
               !ManageDisplayScale &&
               !HideTaskbarDuringRun &&
               string.Equals(BeatSaberLaunchArguments, Windowed4kBeatSaberLaunchArguments, StringComparison.Ordinal);
    }

    private bool MatchesWindowed720pPreset()
    {
        return CaptureWidth == 1280 &&
               CaptureHeight == 720 &&
               string.Equals(BeatSaberLaunchArguments, Windowed720pBeatSaberLaunchArguments, StringComparison.Ordinal);
    }

    private static int NormalizeScalePercent(int value, int fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, 100, 500);
    }

    private static double NormalizeDelayBetweenRecordingsSeconds(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 5;
        }

        if (value < 0)
        {
            return 0;
        }

        if (value > 30)
        {
            return 30;
        }

        return Math.Round(value, 2);
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
