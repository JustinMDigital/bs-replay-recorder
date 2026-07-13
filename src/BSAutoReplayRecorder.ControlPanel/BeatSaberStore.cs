namespace BSAutoReplayRecorder.ControlPanel;

public static class BeatSaberStore
{
    public const string Unknown = "Unknown";
    public const string Steam = "Steam";
    public const string MetaPc = "MetaPc";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "steam" => Steam,
            "metapc" or "meta" or "oculus" or "oculuspc" => MetaPc,
            _ => Unknown
        };
    }

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Steam => "Steam",
        MetaPc => "Meta/Oculus PC",
        _ => "Unknown store"
    };
}
