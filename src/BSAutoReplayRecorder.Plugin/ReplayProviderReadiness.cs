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
        try
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    "ScoreSaber",
                    StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                ScoreSaberReady = false;
                ScoreSaberStatus = "ScoreSaber.dll is not loaded";
                return;
            }

            var pluginType = assembly.GetType("ScoreSaber.Plugin", throwOnError: false);
            var replayLoaderType = assembly.GetType("ScoreSaber.Core.ReplaySystem.ReplayLoader", throwOnError: false);
            if (pluginType == null || replayLoaderType == null)
            {
                ScoreSaberReady = false;
                ScoreSaberStatus = "ScoreSaber replay loader types were not found";
                return;
            }

            var containerField = pluginType.GetField(
                "Container",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
            var container = containerField?.GetValue(null);
            if (container == null)
            {
                ScoreSaberReady = false;
                ScoreSaberStatus = "ScoreSaber Zenject container is not available yet";
                return;
            }

            ScoreSaberReady = true;
            ScoreSaberStatus = "ScoreSaber replay loader ready";
        }
        catch (Exception ex)
        {
            ScoreSaberReady = false;
            ScoreSaberStatus = "ScoreSaber readiness check failed: " + ex.Message;
        }
    }
}
