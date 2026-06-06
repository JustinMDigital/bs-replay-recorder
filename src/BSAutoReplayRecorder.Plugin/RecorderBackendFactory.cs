using System;
using BSAutoReplayRecorder.Core;
using IPA.Logging;

namespace BSAutoReplayRecorder.Plugin;

public static class RecorderBackendFactory
{
    public static IRecordingBackend Create(BatchRecorderSettings settings, Logger logger)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        return new RecorderHostHttpRecorder(settings.RecorderHost, logger);
    }

    public static string Describe(BatchRecorderSettings settings)
    {
        if (settings == null)
        {
            return "Not configured";
        }

        return "RecorderHost " + settings.RecorderHost.NormalizedBaseUrl;
    }
}
