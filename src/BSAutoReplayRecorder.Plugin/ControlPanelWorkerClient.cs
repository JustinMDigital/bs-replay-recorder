using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BSAutoReplayRecorder.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BSAutoReplayRecorder.Plugin;

internal sealed class ControlPanelWorkerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public ControlPanelWorkerClient(ControlPanelWorkerSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.NormalizedBaseUrl + "/api/"),
            Timeout = settings.RequestTimeout
        };
    }

    public Task<ControlPanelWorkerRegisterResponse> RegisterAsync(
        ControlPanelWorkerRegisterRequest request,
        CancellationToken cancellationToken)
    {
        return PostJsonAsync<ControlPanelWorkerRegisterResponse>(
            "workers/register",
            request,
            cancellationToken);
    }

    public Task<ControlPanelWorkerHeartbeatResponse> HeartbeatAsync(
        ControlPanelWorkerHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        return PostJsonAsync<ControlPanelWorkerHeartbeatResponse>("workers/heartbeat", request, cancellationToken);
    }

    public Task<ControlPanelWorkerAssignmentResponse> GetAssignmentAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        return GetJsonAsync<ControlPanelWorkerAssignmentResponse>(
            "workers/" + Uri.EscapeDataString(workerId) + "/assignment",
            cancellationToken);
    }

    public Task ReportAsync(ControlPanelWorkerReportRequest request, CancellationToken cancellationToken)
    {
        return PostJsonAsync<JObject>("workers/report", request, cancellationToken);
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

    private async Task<T> GetJsonAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using (var response = await _httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false))
        {
            return await ReadJsonResponseAsync<T>(relativeUrl, response).ConfigureAwait(false);
        }
    }

    private async Task<T> PostJsonAsync<T>(
        string relativeUrl,
        object request,
        CancellationToken cancellationToken)
    {
        var json = JsonConvert.SerializeObject(request);
        using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
        using (var response = await _httpClient.PostAsync(relativeUrl, content, cancellationToken).ConfigureAwait(false))
        {
            return await ReadJsonResponseAsync<T>(relativeUrl, response).ConfigureAwait(false);
        }
    }

    private static async Task<T> ReadJsonResponseAsync<T>(string relativeUrl, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Control panel request " + relativeUrl + " failed (" +
                (int)response.StatusCode + "): " + ExtractError(body));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Activator.CreateInstance<T>();
        }

        return JsonConvert.DeserializeObject<T>(body)
               ?? throw new InvalidOperationException("Control panel returned an empty response for " + relativeUrl + ".");
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty response";
        }

        try
        {
            var json = JObject.Parse(body);
            return json["error"]?.Value<string>() ?? body;
        }
        catch
        {
            return body;
        }
    }
}
