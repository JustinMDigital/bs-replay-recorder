using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BSAutoReplayRecorder.Core;
using IPA.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BSAutoReplayRecorder.Plugin;

public sealed class RecorderHostHttpRecorder : IRecordingBackend
{
    private readonly RecorderHostConnectionSettings _settings;
    private readonly Logger _logger;
    private readonly HttpClient _httpClient;
    private string? _activeRecordingId;
    private DateTimeOffset? _activeStartedAtUtc;
    private bool _disposed;

    public RecorderHostHttpRecorder(RecorderHostConnectionSettings settings, Logger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.NormalizedBaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds))
        };
    }

    public string DisplayName => "Recorder Host";

    public string Summary => _settings.NormalizedBaseUrl;

    public async Task StartRecordingAsync(RecordingPlan plan, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var request = new JObject
        {
            ["outputBaseName"] = plan.OutputBaseName
        };

        if (!string.IsNullOrWhiteSpace(_settings.WindowTitle))
        {
            request["windowTitle"] = _settings.WindowTitle;
        }

        if (_settings.TargetProcessId.HasValue)
        {
            request["targetProcessId"] = _settings.TargetProcessId.Value;
        }

        if (!string.IsNullOrWhiteSpace(_settings.OutputDirectory))
        {
            request["outputDirectory"] = _settings.OutputDirectory;
        }

        AddPositiveNumber(request, "targetFps", _settings.TargetFps);
        AddPositiveNumber(request, "captureWidth", _settings.CaptureWidth);
        AddPositiveNumber(request, "captureHeight", _settings.CaptureHeight);
        AddPositiveNumber(request, "videoBitrateKbps", _settings.VideoBitrateKbps);
        AddNonNegativeNumber(request, "monitorIndex", _settings.MonitorIndex);
        AddPositiveNumber(request, "audioBitrateKbps", _settings.AudioBitrateKbps);
        AddPositiveNumber(request, "audioSampleRate", _settings.AudioSampleRate);
        AddPositiveNumber(request, "audioChannels", _settings.AudioChannels);
        AddNumber(request, "audioTargetLevelDb", _settings.AudioTargetLevelDb);

        if (!string.IsNullOrWhiteSpace(_settings.Encoder))
        {
            request["encoder"] = _settings.Encoder.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.OutputFormat))
        {
            request["outputFormat"] = _settings.OutputFormat.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.QualityMode))
        {
            request["qualityMode"] = _settings.QualityMode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.CaptureEngine))
        {
            request["captureEngine"] = _settings.CaptureEngine.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.AudioMode))
        {
            request["audioMode"] = _settings.AudioMode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.AudioDeviceName))
        {
            request["audioDeviceName"] = _settings.AudioDeviceName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.AudioLevelMode))
        {
            request["audioLevelMode"] = _settings.AudioLevelMode.Trim();
        }

        JObject response;
        try
        {
            response = await SendJsonAsync("recordings/start", request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.IndexOf("failed (409)", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (await TryAdoptActiveRecordingAsync(plan, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            throw;
        }

        _activeRecordingId = response["recordingId"]?.Value<string>();
        _activeStartedAtUtc = ReadDateTimeOffset(response["startedAtUtc"]);
        _logger.Info("Recorder host accepted start for plan: " + plan.OutputBaseName +
                     (string.IsNullOrEmpty(_activeRecordingId) ? "" : ". RecordingId=" + _activeRecordingId));
    }

    public async Task<RecordingStatus> GetRecordingStatusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        using (var response = await _httpClient.GetAsync("status", cancellationToken).ConfigureAwait(false))
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    "Recorder host status failed (" + (int)response.StatusCode + "): " + body);
            }

            var json = JObject.Parse(body);
            var state = json["state"]?.Value<string>() ?? "";
            var isRecording = json["isRecording"]?.Value<bool>() ?? false;
            return new RecordingStatus(
                isRecording ||
                string.Equals(state, "recording", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "stopping", StringComparison.OrdinalIgnoreCase),
                outputPaused: false);
        }
    }

    public async Task<RecordingStopResult> StopRecordingAsync(
        RecordingPlan plan,
        DateTimeOffset? contentStartUtc,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var request = new JObject();
        if (!string.IsNullOrWhiteSpace(_activeRecordingId))
        {
            request["recordingId"] = _activeRecordingId;
        }

        if (contentStartUtc.HasValue)
        {
            request["contentStartUtc"] = contentStartUtc.Value.ToUniversalTime().ToString("O");
        }

        var response = await SendJsonAsync("recordings/stop", request, cancellationToken)
            .ConfigureAwait(false);
        _activeRecordingId = null;
        _activeStartedAtUtc = null;

        var outputPath = response["outputPath"]?.Value<string>();
        var syncStatus = response["syncStatus"]?.Value<string>() ?? "";
        var syncCorrectionMilliseconds = response["syncCorrectionMilliseconds"]?.Value<double?>();
        var trimStartSeconds = response["trimStartSeconds"]?.Value<double?>();
        var syncReportPath = response["syncReportPath"]?.Value<string>();
        _logger.Info("Recorder host stopped recording for plan: " + plan.OutputBaseName +
                     (string.IsNullOrEmpty(outputPath) ? "" : ". Output=" + outputPath) +
                     (string.IsNullOrWhiteSpace(syncStatus) ? "" : ". Sync=" + syncStatus));
        return new RecordingStopResult(
            outputPath,
            syncStatus,
            syncCorrectionMilliseconds,
            trimStartSeconds,
            syncReportPath);
    }

    private async Task<bool> TryAdoptActiveRecordingAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        using (var response = await _httpClient.GetAsync("status", cancellationToken).ConfigureAwait(false))
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            var json = JObject.Parse(body);
            var state = json["state"]?.Value<string>() ?? "";
            var isRecording = json["isRecording"]?.Value<bool>() ?? false;
            if (!isRecording && !string.Equals(state, "recording", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var activeOutputBaseName = json["outputBaseName"]?.Value<string>() ?? "";
            if (!string.Equals(activeOutputBaseName, plan.OutputBaseName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _activeRecordingId = json["recordingId"]?.Value<string>();
            _activeStartedAtUtc = ReadDateTimeOffset(json["startedAtUtc"]);
            _logger.Warn("Recorder host already had active recording for plan " + plan.OutputBaseName +
                         "; adopting active session" +
                         (string.IsNullOrWhiteSpace(_activeRecordingId) ? "." : " " + _activeRecordingId + "."));
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private async Task<JObject> SendJsonAsync(
        string relativeUrl,
        JObject request,
        CancellationToken cancellationToken)
    {
        var content = new StringContent(
            request.ToString(Formatting.None),
            Encoding.UTF8,
            "application/json");

        using (var response = await _httpClient.PostAsync(relativeUrl, content, cancellationToken)
                   .ConfigureAwait(false))
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    "Recorder host request " + relativeUrl + " failed (" +
                    (int)response.StatusCode + "): " + body);
            }

            return string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RecorderHostHttpRecorder));
        }
    }

    private static void AddPositiveNumber(JObject request, string name, int? value)
    {
        if (value.HasValue && value.Value > 0)
        {
            request[name] = value.Value;
        }
    }

    private static void AddNonNegativeNumber(JObject request, string name, int? value)
    {
        if (value.HasValue && value.Value >= 0)
        {
            request[name] = value.Value;
        }
    }

    private static void AddNumber(JObject request, string name, double? value)
    {
        if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
        {
            request[name] = value.Value;
        }
    }

    private static DateTimeOffset? ReadDateTimeOffset(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }

        if (token.Type == JTokenType.Date)
        {
            var value = token.Value<object>();
            if (value is DateTimeOffset offset)
            {
                return offset;
            }

            if (value is DateTime dateTime)
            {
                return dateTime.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
                    : new DateTimeOffset(dateTime);
            }
        }

        var text = token.Value<string>();
        return DateTimeOffset.TryParse(text, out var parsed) ? parsed : null;
    }
}
