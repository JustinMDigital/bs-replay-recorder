using System;
using UnityEngine;

namespace BSAutoReplayRecorder.Plugin
{
    internal sealed class GameFpsSampler : MonoBehaviour
    {
        private const double MaximumReportedFramesPerSecond = 1000;
        private static readonly object Gate = new object();

        private static GameFpsSampler? _instance;
        private static double? _minimumFramesPerSecondSinceRead;
        private static double _totalFramesPerSecondSinceRead;
        private static int _sampledFrameCountSinceRead;

        public static void EnsureCreated()
        {
            if (_instance != null)
            {
                return;
            }

            var gameObject = new GameObject("Auto Replay Recorder FPS Sampler");
            DontDestroyOnLoad(gameObject);
            _instance = gameObject.AddComponent<GameFpsSampler>();
        }

        public static void DestroyInstance()
        {
            var instance = _instance;
            _instance = null;
            lock (Gate)
            {
                _minimumFramesPerSecondSinceRead = null;
            }

            if (instance != null)
            {
                Destroy(instance.gameObject);
            }
        }

        public static double? ReadHeartbeatFramesPerSecond()
        {
            return ReadHeartbeatSample().MinimumFramesPerSecond;
        }

        public static GameFpsSample ReadHeartbeatSample()
        {
            lock (Gate)
            {
                var framesPerSecond = _minimumFramesPerSecondSinceRead;
                var averageFramesPerSecond = _sampledFrameCountSinceRead > 0
                    ? _totalFramesPerSecondSinceRead / _sampledFrameCountSinceRead
                    : (double?)null;
                var sampledFrameCount = _sampledFrameCountSinceRead;
                _minimumFramesPerSecondSinceRead = null;
                _totalFramesPerSecondSinceRead = 0;
                _sampledFrameCountSinceRead = 0;
                return new GameFpsSample(framesPerSecond, averageFramesPerSecond, sampledFrameCount);
            }
        }

        public static void ResetHeartbeatWindow()
        {
            lock (Gate)
            {
                _minimumFramesPerSecondSinceRead = null;
                _totalFramesPerSecondSinceRead = 0;
                _sampledFrameCountSinceRead = 0;
            }
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0 ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                return;
            }

            var framesPerSecond = Math.Min(MaximumReportedFramesPerSecond, 1.0 / deltaTime);
            lock (Gate)
            {
                if (!_minimumFramesPerSecondSinceRead.HasValue ||
                    framesPerSecond < _minimumFramesPerSecondSinceRead.Value)
                {
                    _minimumFramesPerSecondSinceRead = framesPerSecond;
                }

                _totalFramesPerSecondSinceRead += framesPerSecond;
                _sampledFrameCountSinceRead++;
            }
        }
    }

    internal readonly struct GameFpsSample
    {
        public GameFpsSample(
            double? minimumFramesPerSecond,
            double? averageFramesPerSecond,
            int sampledFrameCount)
        {
            MinimumFramesPerSecond = minimumFramesPerSecond;
            AverageFramesPerSecond = averageFramesPerSecond;
            SampledFrameCount = sampledFrameCount;
        }

        public double? MinimumFramesPerSecond { get; }

        public double? AverageFramesPerSecond { get; }

        public int SampledFrameCount { get; }
    }
}
