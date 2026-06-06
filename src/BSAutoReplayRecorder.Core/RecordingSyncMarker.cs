namespace BSAutoReplayRecorder.Core;

public static class RecordingSyncMarker
{
    public const int PulseCount = 3;
    public const double PulseDurationSeconds = 0.10;
    public const double PulseSpacingSeconds = 0.35;
    public const double TailSeconds = 0.15;
    public const double SearchWindowSeconds = 2.50;
    public const double VideoSampleRate = 60.0;
    public const int AudioSampleRate = 48000;
    public const double MinimumVisualBrightness = 0.70;
    public const double MinimumAudioPeak = 0.20;
    public const double PulseSpacingToleranceSeconds = 0.12;
    public const double MaximumPairOffsetSeconds = 1.50;

    public static double MarkerDurationSeconds =>
        PulseSpacingSeconds * (PulseCount - 1) + PulseDurationSeconds + TailSeconds;
}
