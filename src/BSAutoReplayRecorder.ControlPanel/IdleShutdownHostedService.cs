namespace BSAutoReplayRecorder.ControlPanel;

internal sealed class IdleShutdownHostedService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private readonly ControlPanelSettings _settings;
    private readonly ControlPanelStore _store;
    private readonly IStackShutdownLauncher _shutdownLauncher;
    private readonly ILogger<IdleShutdownHostedService> _logger;

    public IdleShutdownHostedService(
        ControlPanelSettings settings,
        ControlPanelStore store,
        IStackShutdownLauncher shutdownLauncher,
        ILogger<IdleShutdownHostedService> logger)
    {
        _settings = settings;
        _store = store;
        _shutdownLauncher = shutdownLauncher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                CheckIdleShutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Idle shutdown check failed.");
            }
        }
    }

    private void CheckIdleShutdown()
    {
        var timeout = TimeSpan.FromMinutes(_settings.IdleShutdownMinutes);
        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        if (!_store.TryRequestIdleShutdown(DateTimeOffset.UtcNow, timeout))
        {
            return;
        }

        _logger.LogWarning(
            "Idle timeout reached after {IdleShutdownMinutes} minute(s); stopping the recorder stack.",
            _settings.IdleShutdownMinutes);
        _shutdownLauncher.StartStopScript(stopGames: true);
    }
}
