using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BSAutoReplayRecorder.Core;
using BSAutoReplayRecorder.Core.Obs;
using IPA.Logging;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using IpaLogger = IPA.Logging.Logger;

namespace BSAutoReplayRecorder.Plugin;

public sealed class ObsWebSocketRecorder : IObsRecorder, IDisposable
{
    private const int RpcVersion = 1;
    private readonly ObsConnectionSettings _settings;
    private readonly IpaLogger _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pendingRequests =
        new ConcurrentDictionary<string, TaskCompletionSource<JObject>>();

    private WebSocket? _webSocket;
    private TaskCompletionSource<bool>? _identifiedCompletion;
    private readonly object _sync = new object();

    public ObsWebSocketRecorder(ObsConnectionSettings settings, IpaLogger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartRecordingAsync(RecordingPlan plan, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await SendRequestAsync("StartRecord", cancellationToken).ConfigureAwait(false);
        _logger.Info("OBS accepted StartRecord for plan: " + plan.OutputBaseName);
    }

    public async Task<ObsRecordingStatus> GetRecordingStatusAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var response = await SendRequestAsync("GetRecordStatus", cancellationToken).ConfigureAwait(false);
        var responseData = response["responseData"];
        var outputActive = responseData?["outputActive"]?.Value<bool>() ?? false;
        var outputPaused = responseData?["outputPaused"]?.Value<bool>() ?? false;
        return new ObsRecordingStatus(outputActive, outputPaused);
    }

    public async Task<RecordingStopResult> StopRecordingAsync(RecordingPlan plan, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var response = await SendRequestAsync("StopRecord", cancellationToken).ConfigureAwait(false);
        var outputPath = response["responseData"]?["outputPath"]?.Value<string>();
        _logger.Info("OBS recording stopped for plan: " + plan.OutputBaseName);
        return new RecordingStopResult(outputPath);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_webSocket != null)
            {
                _webSocket.OnMessage -= HandleMessage;
                _webSocket.OnError -= HandleError;
                _webSocket.OnClose -= HandleClose;
                _webSocket.Close();
                _webSocket = null;
            }
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> identifiedCompletion;

        lock (_sync)
        {
            if (_webSocket != null && _webSocket.ReadyState == WebSocketState.Open)
            {
                return;
            }

            _identifiedCompletion = new TaskCompletionSource<bool>();
            identifiedCompletion = _identifiedCompletion;

            _webSocket = new WebSocket(_settings.WebSocketUri);
            _webSocket.OnMessage += HandleMessage;
            _webSocket.OnError += HandleError;
            _webSocket.OnClose += HandleClose;

            _logger.Info("Connecting to OBS WebSocket at " + _settings.WebSocketUri);
            _webSocket.Connect();
        }

        await AwaitWithCancellationAsync(identifiedCompletion.Task, TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<JObject> SendRequestAsync(string requestType, CancellationToken cancellationToken)
    {
        var webSocket = _webSocket;
        if (webSocket == null || webSocket.ReadyState != WebSocketState.Open)
        {
            throw new InvalidOperationException("OBS WebSocket is not connected.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JObject>();
        _pendingRequests[requestId] = completion;

        webSocket.Send(ObsRequestFactory.CreateRequest(requestType, requestId));

        return AwaitRequestAsync(requestType, requestId, completion, cancellationToken);
    }

    private async Task<JObject> AwaitRequestAsync(
        string requestType,
        string requestId,
        TaskCompletionSource<JObject> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await AwaitWithCancellationAsync(completion.Task, TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);

            var status = response["requestStatus"];
            var result = status?["result"]?.Value<bool>() ?? false;
            if (!result)
            {
                var code = status?["code"]?.Value<int>() ?? 0;
                var comment = status?["comment"]?.Value<string>() ?? "OBS request failed.";
                var responseJson = response.ToString(Newtonsoft.Json.Formatting.None);
                throw new InvalidOperationException(
                    "OBS request " + requestType + " failed (" + code + "): " + comment +
                    ". Response: " + responseJson);
            }

            _logger.Info("OBS request " + requestType + " succeeded.");
            return response;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private void HandleMessage(object sender, MessageEventArgs args)
    {
        try
        {
            var envelope = JObject.Parse(args.Data);
            var op = envelope["op"]?.Value<int>();
            var data = envelope["d"] as JObject;

            if (op == 0 && data != null)
            {
                HandleHello(data);
                return;
            }

            if (op == 2)
            {
                _logger.Info("OBS WebSocket identified.");
                _identifiedCompletion?.TrySetResult(true);
                return;
            }

            if (op == 7 && data != null)
            {
                var requestId = data["requestId"]?.Value<string>();
                if (requestId != null && _pendingRequests.TryGetValue(requestId, out var completion))
                {
                    completion.TrySetResult(data);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to process OBS WebSocket message: " + ex);
        }
    }

    private void HandleHello(JObject data)
    {
        var authentication = data["authentication"] as JObject;
        string? authPayload = null;

        if (authentication != null)
        {
            var challenge = authentication["challenge"]?.Value<string>();
            var salt = authentication["salt"]?.Value<string>();
            if (challenge != null && salt != null)
            {
                authPayload = ObsAuthentication.CreateAuthentication(_settings.Password, salt, challenge);
            }
        }

        _webSocket?.Send(ObsRequestFactory.CreateIdentifyRequest(RpcVersion, authPayload));
    }

    private void HandleError(object sender, ErrorEventArgs args)
    {
        _logger.Error("OBS WebSocket error: " + args.Message);
        _identifiedCompletion?.TrySetException(args.Exception ?? new InvalidOperationException(args.Message));
    }

    private void HandleClose(object sender, CloseEventArgs args)
    {
        _logger.Warn("OBS WebSocket closed: " + args.Code + " " + args.Reason);
        _identifiedCompletion?.TrySetException(new InvalidOperationException("OBS WebSocket closed: " + args.Reason));
    }

    private static async Task<T> AwaitWithCancellationAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);
        if (completed == task)
        {
            return await task.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException("Timed out waiting for OBS WebSocket response.");
    }
}
