namespace BSAutoReplayRecorder.Core;

public sealed class ObsConnectionSettings
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 4455;

    public string Password { get; set; } = "";

    public bool UseAuthentication => !string.IsNullOrEmpty(Password);

    public string WebSocketUri => "ws://" + Host + ":" + Port;

    public bool ShouldSerializeUseAuthentication()
    {
        return false;
    }

    public bool ShouldSerializeWebSocketUri()
    {
        return false;
    }
}
