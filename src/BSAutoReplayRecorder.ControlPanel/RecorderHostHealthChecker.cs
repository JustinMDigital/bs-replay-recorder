namespace BSAutoReplayRecorder.ControlPanel;

public interface IRecorderHostHealthChecker
{
    bool IsHealthy(string recorderHostUrl);
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
}
