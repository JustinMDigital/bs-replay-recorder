using System;
using UnityEngine;

namespace BSAutoReplayRecorder.Plugin;

public sealed class RecordingStatusOverlay : MonoBehaviour
{
    private const int Width = 560;
    private const int MinHeight = 86;
    private const int Margin = 24;
    private static readonly object Gate = new object();

    private static RecordingStatusOverlay? _instance;
    private static OverlayState _state = OverlayState.Hidden();
    private static bool _statusPanelVisible;

    private GUIStyle? _boxStyle;
    private GUIStyle? _headerStyle;
    private GUIStyle? _detailStyle;
    private GUIStyle? _footerStyle;

    public static void EnsureCreated()
    {
        if (_instance != null)
        {
            return;
        }

        var gameObject = new GameObject("Auto Replay Recorder Overlay");
        DontDestroyOnLoad(gameObject);
        _instance = gameObject.AddComponent<RecordingStatusOverlay>();
    }

    public static void DestroyInstance()
    {
        var instance = _instance;
        _instance = null;
        Clear();

        if (instance != null)
        {
            Destroy(instance.gameObject);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _state = OverlayState.Hidden();
        }
    }

    public static void ShowIdle(string header, string detail)
    {
        SetState(new OverlayState(header, detail, "Idle", false, null));
    }

    public static void ShowConnected(string detail)
    {
        SetState(new OverlayState("Worker connected", detail, "Idle", false, null));
    }

    public static void ShowToast(string header, string detail, TimeSpan duration, bool isError = false)
    {
        SetState(new OverlayState(
            header,
            detail,
            isError ? "Check the log for details" : "",
            isError,
            DateTimeOffset.UtcNow + duration));
    }

    public static void SetStatusPanelVisible(bool visible)
    {
        lock (Gate)
        {
            _statusPanelVisible = visible;
        }
    }

    private static void SetState(OverlayState state)
    {
        EnsureCreated();
        lock (Gate)
        {
            _state = state;
        }
    }

    private void Update()
    {
        lock (Gate)
        {
            if (_state.ExpiresAtUtc.HasValue && DateTimeOffset.UtcNow >= _state.ExpiresAtUtc.Value)
            {
                _state = OverlayState.Hidden();
            }
        }
    }

    private void OnGUI()
    {
        var state = GetVisibleState(out var statusPanelVisible);
        if (!statusPanelVisible || !state.IsVisible)
        {
            return;
        }

        EnsureStyles();
        DrawStatusPanel(state);
    }

    private static OverlayState GetVisibleState(out bool statusPanelVisible)
    {
        lock (Gate)
        {
            statusPanelVisible = _statusPanelVisible;
            return _state;
        }
    }

    private void DrawStatusPanel(OverlayState state)
    {
        var screenWidth = Screen.width <= 0 ? 1920 : Screen.width;
        var width = Math.Min(Width, Math.Max(320, screenWidth - Margin * 2));
        var height = string.IsNullOrEmpty(state.Footer) ? MinHeight : MinHeight + 24;
        var rect = new Rect(Margin, Margin, width, height);

        var oldColor = GUI.color;
        GUI.depth = -10000;
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.Box(rect, GUIContent.none, _boxStyle);
        GUI.color = state.IsError
            ? new Color(1f, 0.22f, 0.18f, 1f)
            : new Color(0.18f, 0.62f, 1f, 1f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 6, rect.height), Texture2D.whiteTexture);
        GUI.color = oldColor;

        GUI.Label(new Rect(rect.x + 18, rect.y + 12, rect.width - 36, 26), state.Header, _headerStyle);
        GUI.Label(new Rect(rect.x + 18, rect.y + 40, rect.width - 36, 24), state.Detail, _detailStyle);

        if (!string.IsNullOrEmpty(state.Footer))
        {
            GUI.Label(new Rect(rect.x + 18, rect.y + 66, rect.width - 36, 22), state.Footer, _footerStyle);
        }
    }

    private void EnsureStyles()
    {
        if (_boxStyle != null)
        {
            return;
        }

        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            border = new RectOffset(10, 10, 10, 10),
            padding = new RectOffset(0, 0, 0, 0)
        };
        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        _detailStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = new Color(0.92f, 0.95f, 1f, 1f) }
        };
        _footerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.72f, 0.82f, 0.95f, 1f) }
        };
    }

    private sealed class OverlayState
    {
        public OverlayState(
            string header,
            string detail,
            string footer,
            bool isError,
            DateTimeOffset? expiresAtUtc)
        {
            Header = header;
            Detail = detail;
            Footer = footer;
            IsError = isError;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string Header { get; }

        public string Detail { get; }

        public string Footer { get; }

        public bool IsError { get; }

        public DateTimeOffset? ExpiresAtUtc { get; }

        public bool IsVisible => !string.IsNullOrWhiteSpace(Header) || !string.IsNullOrWhiteSpace(Detail);

        public static OverlayState Hidden()
        {
            return new OverlayState("", "", "", false, null);
        }
    }
}
