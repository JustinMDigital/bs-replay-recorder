using System;

namespace BSAutoReplayRecorder.Core;

public sealed class ControlPanelWorkerSettings
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://127.0.0.1:5770";

    public string WorkerId { get; set; } = "";

    public string WorkerName { get; set; } = "";

    public int? PreferredInstanceIndex { get; set; }

    public double PollIntervalSeconds { get; set; } = 2;

    public double HeartbeatIntervalSeconds { get; set; } = 5;

    public double RequestTimeoutSeconds { get; set; } = 10;

    public string NormalizedBaseUrl
    {
        get
        {
            var value = string.IsNullOrWhiteSpace(BaseUrl)
                ? "http://127.0.0.1:5770"
                : BaseUrl.Trim();
            return value.TrimEnd('/');
        }
    }

    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(0.25, PollIntervalSeconds));

    public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(Math.Max(1, HeartbeatIntervalSeconds));

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Max(1, RequestTimeoutSeconds));

    public bool ShouldSerializeNormalizedBaseUrl()
    {
        return false;
    }

    public bool ShouldSerializePollInterval()
    {
        return false;
    }

    public bool ShouldSerializeHeartbeatInterval()
    {
        return false;
    }

    public bool ShouldSerializeRequestTimeout()
    {
        return false;
    }
}
