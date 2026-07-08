using System;

namespace BSAutoReplayRecorder.Plugin;

internal sealed class GamePresentationSettingsNotReadyException : InvalidOperationException
{
    public GamePresentationSettingsNotReadyException(string message)
        : base(message)
    {
    }
}
