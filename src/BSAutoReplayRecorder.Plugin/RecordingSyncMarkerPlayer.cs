using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using BSAutoReplayRecorder.Core;
using UnityEngine;

namespace BSAutoReplayRecorder.Plugin;

public sealed class RecordingSyncMarkerPlayer : MonoBehaviour
{
    private static readonly object Gate = new object();
    private static RecordingSyncMarkerPlayer? _instance;
    private TaskCompletionSource<bool>? _completion;
    private AudioSource? _audioSource;
    private AudioClip? _clickClip;
    private float _flashUntilRealtime;

    public static void EnsureCreated()
    {
        if (_instance != null)
        {
            return;
        }

        var gameObject = new GameObject("Auto Replay Recorder Sync Marker");
        DontDestroyOnLoad(gameObject);
        _instance = gameObject.AddComponent<RecordingSyncMarkerPlayer>();
    }

    public static Task PlayAsync(CancellationToken cancellationToken)
    {
        EnsureCreated();
        return _instance!.PlayInternalAsync(cancellationToken);
    }

    private Task PlayInternalAsync(CancellationToken cancellationToken)
    {
        lock (Gate)
        {
            if (_completion != null)
            {
                throw new InvalidOperationException("A recording sync marker is already playing.");
            }

            _completion = new TaskCompletionSource<bool>();
            StartCoroutine(PlayCoroutine(_completion, cancellationToken));
            return _completion.Task;
        }
    }

    private IEnumerator PlayCoroutine(TaskCompletionSource<bool> completion, CancellationToken cancellationToken)
    {
        EnsureAudio();

        for (var index = 0; index < RecordingSyncMarker.PulseCount; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                CompleteCanceled(completion);
                yield break;
            }

            var pulseStart = Time.realtimeSinceStartup;
            _flashUntilRealtime = pulseStart + (float)RecordingSyncMarker.PulseDurationSeconds;
            _audioSource!.PlayOneShot(_clickClip!, 1f);

            var nextPulseAt = pulseStart + (float)RecordingSyncMarker.PulseSpacingSeconds;
            while (Time.realtimeSinceStartup < nextPulseAt)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    CompleteCanceled(completion);
                    yield break;
                }

                yield return null;
            }
        }

        _flashUntilRealtime = 0f;
        var endAt = Time.realtimeSinceStartup + (float)RecordingSyncMarker.TailSeconds;
        while (Time.realtimeSinceStartup < endAt)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                CompleteCanceled(completion);
                yield break;
            }

            yield return null;
        }

        lock (Gate)
        {
            if (ReferenceEquals(_completion, completion))
            {
                _completion = null;
            }
        }

        completion.TrySetResult(true);
    }

    private void CompleteCanceled(TaskCompletionSource<bool> completion)
    {
        _flashUntilRealtime = 0f;
        lock (Gate)
        {
            if (ReferenceEquals(_completion, completion))
            {
                _completion = null;
            }
        }

        completion.TrySetCanceled();
    }

    private void EnsureAudio()
    {
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f;
            _audioSource.volume = 1f;
        }

        if (_clickClip != null)
        {
            return;
        }

        var sampleRate = RecordingSyncMarker.AudioSampleRate;
        var sampleCount = Math.Max(1, (int)(RecordingSyncMarker.PulseDurationSeconds * sampleRate));
        var data = new float[sampleCount];
        const double frequency = 2200.0;
        for (var index = 0; index < sampleCount; index++)
        {
            var t = index / (double)sampleRate;
            var envelope = Math.Max(0.0, 1.0 - t / RecordingSyncMarker.PulseDurationSeconds);
            data[index] = (float)(Math.Sin(2.0 * Math.PI * frequency * t) * envelope * 0.95);
        }

        _clickClip = AudioClip.Create(
            "BSARR Sync Marker Click",
            sampleCount,
            1,
            sampleRate,
            false);
        _clickClip.SetData(data, 0);
    }

    private void OnGUI()
    {
        if (_flashUntilRealtime <= 0f || Time.realtimeSinceStartup > _flashUntilRealtime)
        {
            return;
        }

        var previousDepth = GUI.depth;
        var previousColor = GUI.color;
        GUI.depth = -20000;
        GUI.color = Color.white;
        GUI.DrawTexture(
            new Rect(0, 0, Screen.width <= 0 ? 1920 : Screen.width, Screen.height <= 0 ? 1080 : Screen.height),
            Texture2D.whiteTexture);
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }
}
