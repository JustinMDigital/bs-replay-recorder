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
    private Material? _overlayMaterial;
    private bool _markerActive;
    private float _pulseUntilRealtime;

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
        _markerActive = true;

        for (var index = 0; index < RecordingSyncMarker.PulseCount; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                CompleteCanceled(completion);
                yield break;
            }

            var pulseStart = Time.realtimeSinceStartup;
            _pulseUntilRealtime = pulseStart + (float)RecordingSyncMarker.PulseDurationSeconds;
            _audioSource!.PlayOneShot(_clickClip!, 1f);

            var waitSeconds = index == RecordingSyncMarker.PulseCount - 1
                ? RecordingSyncMarker.PulseDurationSeconds
                : RecordingSyncMarker.PulseSpacingSeconds;
            var nextPulseAt = pulseStart + (float)waitSeconds;
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

        _pulseUntilRealtime = 0f;
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

        _markerActive = false;
        completion.TrySetResult(true);
    }

    private void CompleteCanceled(TaskCompletionSource<bool> completion)
    {
        _markerActive = false;
        _pulseUntilRealtime = 0f;
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

    private void OnEnable()
    {
        Camera.onPostRender += DrawCameraOverlay;
    }

    private void OnDisable()
    {
        Camera.onPostRender -= DrawCameraOverlay;
    }

    private void OnDestroy()
    {
        Camera.onPostRender -= DrawCameraOverlay;
        if (_overlayMaterial != null)
        {
            Destroy(_overlayMaterial);
            _overlayMaterial = null;
        }
    }

    private void DrawCameraOverlay(Camera camera)
    {
        if (!_markerActive)
        {
            return;
        }

        DrawGlOverlay(Time.realtimeSinceStartup <= _pulseUntilRealtime ? Color.white : Color.black);
    }

    private void DrawGlOverlay(Color color)
    {
        EnsureOverlayMaterial();
        if (_overlayMaterial == null)
        {
            return;
        }

        _overlayMaterial.SetPass(0);
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        GL.Color(color);
        GL.Vertex3(0f, 0f, 0f);
        GL.Vertex3(1f, 0f, 0f);
        GL.Vertex3(1f, 1f, 0f);
        GL.Vertex3(0f, 1f, 0f);
        GL.End();
        GL.PopMatrix();
    }

    private void EnsureOverlayMaterial()
    {
        if (_overlayMaterial != null)
        {
            return;
        }

        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            return;
        }

        _overlayMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _overlayMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _overlayMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _overlayMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _overlayMaterial.SetInt("_ZWrite", 0);
    }

    private void OnGUI()
    {
        if (!_markerActive)
        {
            return;
        }

        var previousDepth = GUI.depth;
        var previousColor = GUI.color;
        GUI.depth = -20000;
        GUI.color = Time.realtimeSinceStartup <= _pulseUntilRealtime ? Color.white : Color.black;
        GUI.DrawTexture(
            new Rect(0, 0, Screen.width <= 0 ? 1920 : Screen.width, Screen.height <= 0 ? 1080 : Screen.height),
            Texture2D.whiteTexture);
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }
}
