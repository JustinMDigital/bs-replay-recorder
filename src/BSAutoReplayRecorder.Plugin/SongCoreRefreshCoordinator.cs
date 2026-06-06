using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IPA.Logging;
using IPA.Utilities.Async;

namespace BSAutoReplayRecorder.Plugin;

internal static class SongCoreRefreshCoordinator
{
    private const string LoaderTypeName = "SongCore.Loader";
    private const int PollMilliseconds = 250;

    public static async Task RefreshAllSongsAsync(
        TimeSpan timeout,
        Logger logger,
        CancellationToken cancellationToken)
    {
        var bridge = SongCoreBridge.TryCreate(logger);
        if (bridge == null)
        {
            return;
        }

        var wasAlreadyLoading = bridge.AreSongsLoading;
        await UnityMainThreadTaskScheduler.Factory
            .StartNew(
                () =>
                {
                    if (bridge.AreSongsLoading)
                    {
                        logger.Info("SongCore is already refreshing songs; waiting for the active refresh.");
                        return;
                    }

                    logger.Info("Requesting SongCore full song refresh before replay validation.");
                    bridge.RefreshSongs(fullRefresh: true);
                },
                cancellationToken)
            .ConfigureAwait(false);

        await WaitForRefreshAsync(bridge, wasAlreadyLoading, timeout, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WaitForRefreshAsync(
        SongCoreBridge bridge,
        bool wasAlreadyLoading,
        TimeSpan timeout,
        Logger logger,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observedLoading = wasAlreadyLoading || bridge.AreSongsLoading;

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (bridge.AreSongsLoading)
            {
                observedLoading = true;
            }
            else if (bridge.AreSongsLoaded)
            {
                logger.Info(
                    "SongCore song refresh is ready" +
                    (observedLoading ? "" : " without reporting a loading phase") +
                    ".");
                return;
            }

            await Task.Delay(PollMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Timed out waiting for SongCore to refresh songs before replay validation.");
    }

    private sealed class SongCoreBridge
    {
        private readonly object _instance;
        private readonly MethodInfo _refreshSongs;
        private readonly PropertyInfo _areSongsLoading;
        private readonly PropertyInfo _areSongsLoaded;

        private SongCoreBridge(
            object instance,
            MethodInfo refreshSongs,
            PropertyInfo areSongsLoading,
            PropertyInfo areSongsLoaded)
        {
            _instance = instance;
            _refreshSongs = refreshSongs;
            _areSongsLoading = areSongsLoading;
            _areSongsLoaded = areSongsLoaded;
        }

        public bool AreSongsLoading => GetBool(_areSongsLoading);

        public bool AreSongsLoaded => GetBool(_areSongsLoaded);

        public void RefreshSongs(bool fullRefresh)
        {
            _refreshSongs.Invoke(_instance, new object[] { fullRefresh });
        }

        public static SongCoreBridge? TryCreate(Logger logger)
        {
            var loaderType = FindLoadedType(LoaderTypeName);
            if (loaderType == null)
            {
                logger.Warn("SongCore.Loader is not loaded; skipping song refresh before replay validation.");
                return null;
            }

            var instance = loaderType
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            if (instance == null)
            {
                logger.Warn("SongCore.Loader.Instance is not available; skipping song refresh before replay validation.");
                return null;
            }

            var refreshSongs = loaderType.GetMethod(
                "RefreshSongs",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(bool) },
                null);
            var areSongsLoading = loaderType.GetProperty(
                "AreSongsLoading",
                BindingFlags.Public | BindingFlags.Static);
            var areSongsLoaded = loaderType.GetProperty(
                "AreSongsLoaded",
                BindingFlags.Public | BindingFlags.Static);

            if (refreshSongs == null || areSongsLoading == null || areSongsLoaded == null)
            {
                logger.Warn("SongCore refresh API was not found; skipping song refresh before replay validation.");
                return null;
            }

            return new SongCoreBridge(instance, refreshSongs, areSongsLoading, areSongsLoaded);
        }

        private bool GetBool(PropertyInfo property)
        {
            return property.GetValue(null) is bool value && value;
        }

        private static Type? FindLoadedType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type;
                try
                {
                    type = assembly.GetType(typeName, throwOnError: false);
                }
                catch
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
