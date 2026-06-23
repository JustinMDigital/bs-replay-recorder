namespace BSAutoReplayRecorder.ControlPanel;

public interface IRecorderHostHealthChecker
{
    bool IsHealthy(string recorderHostUrl);

    RecorderHostCapabilitiesSnapshot GetCapabilities(string recorderHostUrl);
}

internal sealed class HttpRecorderHostHealthChecker : IRecorderHostHealthChecker
{
    private static readonly HttpClient Client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public bool IsHealthy(string recorderHostUrl)
    {
        if (string.IsNullOrWhiteSpace(recorderHostUrl))
        {
            return false;
        }

        try
        {
            var healthUrl = recorderHostUrl.TrimEnd('/') + "/health";
            using var response = Client.GetAsync(healthUrl).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public RecorderHostCapabilitiesSnapshot GetCapabilities(string recorderHostUrl)
    {
        if (string.IsNullOrWhiteSpace(recorderHostUrl))
        {
            return RecorderHostCapabilitiesSnapshot.Unavailable("Recorder host URL is missing.");
        }

        try
        {
            var capabilitiesUrl = recorderHostUrl.TrimEnd('/') + "/capabilities";
            using var response = Client.GetAsync(capabilitiesUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return RecorderHostCapabilitiesSnapshot.LegacyFallback(
                    "Recorder host capabilities endpoint returned " + (int)response.StatusCode + ".");
            }

            using var stream = response.Content.ReadAsStream();
            using var document = System.Text.Json.JsonDocument.Parse(stream);
            var root = document.RootElement;
            var snapshot = new RecorderHostCapabilitiesSnapshot
            {
                Status = ReadString(root, "status") ?? "ok",
                Detail = "Capabilities reported by recorder host."
            };

            if (root.TryGetProperty("captureEngines", out var captureEngines) &&
                captureEngines.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var engine in captureEngines.EnumerateArray())
                {
                    var name = ReadString(engine, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    snapshot.CaptureEngines[name] = new RecorderHostCapability
                    {
                        Supported = ReadBool(engine, "supported"),
                        Status = ReadString(engine, "status") ?? ""
                    };
                }
            }

            if (root.TryGetProperty("audioModes", out var audioModes) &&
                audioModes.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var mode in audioModes.EnumerateArray())
                {
                    var name = ReadString(mode, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    snapshot.AudioModes[name] = new RecorderHostCapability
                    {
                        Supported = ReadBool(mode, "supported"),
                        Status = ReadString(mode, "status") ?? ""
                    };
                }
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            return RecorderHostCapabilitiesSnapshot.LegacyFallback(
                "Recorder host capabilities endpoint was unavailable: " + ex.Message);
        }
    }

    private static string? ReadString(System.Text.Json.JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool ReadBool(System.Text.Json.JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == System.Text.Json.JsonValueKind.True;
    }
}

public sealed class RecorderHostCapabilitiesSnapshot
{
    public string Status { get; set; } = "unknown";

    public string Detail { get; set; } = "";

    public Dictionary<string, RecorderHostCapability> CaptureEngines { get; } =
        new Dictionary<string, RecorderHostCapability>(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, RecorderHostCapability> AudioModes { get; } =
        new Dictionary<string, RecorderHostCapability>(StringComparer.OrdinalIgnoreCase);

    public bool SupportsCaptureEngine(string name)
    {
        return CaptureEngines.TryGetValue(name, out var capability) && capability.Supported;
    }

    public bool SupportsAudioMode(string name)
    {
        return AudioModes.TryGetValue(name, out var capability) && capability.Supported;
    }

    public string DescribeCaptureEngine(string name)
    {
        return CaptureEngines.TryGetValue(name, out var capability)
            ? capability.Status
            : "Capture engine was not reported.";
    }

    public string DescribeAudioMode(string name)
    {
        return AudioModes.TryGetValue(name, out var capability)
            ? capability.Status
            : "Audio mode was not reported.";
    }

    public static RecorderHostCapabilitiesSnapshot Unavailable(string detail)
    {
        return new RecorderHostCapabilitiesSnapshot
        {
            Status = "unavailable",
            Detail = detail
        };
    }

    public static RecorderHostCapabilitiesSnapshot LegacyFallback(string detail)
    {
        var snapshot = new RecorderHostCapabilitiesSnapshot
        {
            Status = "legacy",
            Detail = detail
        };
        snapshot.CaptureEngines["FFmpegDdagrab"] = new RecorderHostCapability
        {
            Supported = true,
            Status = "Assumed available from legacy recorder host health."
        };
        snapshot.CaptureEngines["WindowsGraphicsCapture"] = new RecorderHostCapability
        {
            Supported = false,
            Status = "Recorder host does not report WGC support."
        };
        snapshot.AudioModes["None"] = new RecorderHostCapability
        {
            Supported = true,
            Status = "Audio disabled."
        };
        snapshot.AudioModes["ProcessLoopback"] = new RecorderHostCapability
        {
            Supported = true,
            Status = "Assumed available from legacy recorder host health."
        };
        return snapshot;
    }
}

public sealed class RecorderHostCapability
{
    public bool Supported { get; set; }

    public string Status { get; set; } = "";
}
