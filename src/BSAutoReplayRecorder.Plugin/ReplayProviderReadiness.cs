using System;
using System.Linq;

namespace BSAutoReplayRecorder.Plugin;

internal sealed class ReplayProviderReadiness
{
    public bool BeatLeaderReady { get; private set; }

    public string BeatLeaderStatus { get; private set; } = "Unchecked";

    public bool ScoreSaberReady { get; private set; }

    public string ScoreSaberStatus { get; private set; } = "Unchecked";

    public static ReplayProviderReadiness Check()
    {
        var readiness = new ReplayProviderReadiness();
        readiness.CheckBeatLeader();
        readiness.CheckScoreSaber();
        return readiness;
    }

    private void CheckBeatLeader()
    {
        try
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    "BeatLeader",
                    StringComparison.OrdinalIgnoreCase));
            var loaderType = assembly?.GetType("BeatLeader.Replayer.ReplayerMenuLoader", throwOnError: false);
            var instanceProperty = loaderType?.GetProperty(
                "Instance",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
            BeatLeaderReady = instanceProperty?.GetValue(null) != null;
            BeatLeaderStatus = BeatLeaderReady
                ? "BeatLeader replayer ready"
                : "BeatLeader ReplayerMenuLoader is not available yet";
        }
        catch (Exception ex)
        {
            BeatLeaderReady = false;
            BeatLeaderStatus = "BeatLeader readiness check failed: " + ex.Message;
        }
    }

    private void CheckScoreSaber()
    {
        var compatibility = ScoreSaberReplayPlaybackDriver.CheckRuntimeCompatibility();
        ScoreSaberReady = compatibility.Ready;
        ScoreSaberStatus = compatibility.Status;
    }
}
