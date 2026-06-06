using System;

namespace BSAutoReplayRecorder.Core;

public sealed class WindowPlacementSettings
{
    public bool Enabled { get; set; }

    public int? InstanceIndex { get; set; }

    public int MonitorIndex { get; set; } = 1;

    public int Columns { get; set; } = 2;

    public int Rows { get; set; } = 2;

    public int Width { get; set; }

    public int Height { get; set; }

    public double ApplyDelaySeconds { get; set; } = 1;

    public int RetryCount { get; set; } = 60;

    public double RetryIntervalSeconds { get; set; } = 0.5;

    public bool UseNativeWindowMove { get; set; } = true;

    public bool UseBorderlessWindow { get; set; }

    public TimeSpan ApplyDelay => TimeSpan.FromSeconds(Math.Max(0, ApplyDelaySeconds));

    public TimeSpan RetryInterval => TimeSpan.FromSeconds(Math.Max(0.1, RetryIntervalSeconds));

    public bool ShouldSerializeApplyDelay()
    {
        return false;
    }

    public bool ShouldSerializeRetryInterval()
    {
        return false;
    }
}
