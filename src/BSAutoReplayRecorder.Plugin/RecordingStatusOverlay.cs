using System;
using BSAutoReplayRecorder.Core;
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
    private static ControlPanelState _controlPanelState = ControlPanelState.Empty();
    private static ControlPanelActions _controlPanelActions = new ControlPanelActions();
    private static bool _controlPanelVisible;

    private GUIStyle? _boxStyle;
    private GUIStyle? _headerStyle;
    private GUIStyle? _detailStyle;
    private GUIStyle? _footerStyle;
    private GUIStyle? _panelLabelStyle;
    private GUIStyle? _panelHeaderStyle;

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
        SetState(new OverlayState(header, detail, "Waiting", false, false, null, null));
    }

    public static void ShowToast(string header, string detail, TimeSpan duration, bool isError = false)
    {
        SetState(new OverlayState(
            header,
            detail,
            isError ? "Check the log for details" : "",
            false,
            isError,
            null,
            DateTimeOffset.UtcNow + duration));
    }

    public static void ShowCountdown(RecordingPlan plan, int index, int total, TimeSpan delay)
    {
        var now = DateTimeOffset.UtcNow;
        SetState(new OverlayState(
            "Next recording " + index + "/" + total,
            DescribePlan(plan),
            "",
            false,
            false,
            now + delay,
            now + delay));
    }

    public static void ShowBatchCountdown(int total, TimeSpan delay)
    {
        var now = DateTimeOffset.UtcNow;
        SetState(new OverlayState(
            "Batch auto-start",
            total + " queued recording(s)",
            "",
            false,
            false,
            now + delay,
            now + delay));
    }

    public static void ShowPreflight(RecordingPlan plan, int index, int total)
    {
        SetState(new OverlayState(
            "Preflight " + index + "/" + total,
            DescribePlan(plan),
            "Checking replay launchability",
            false,
            false,
            null,
            null));
    }

    public static void ShowPreparing(RecordingPlan plan, int index, int total)
    {
        SetState(new OverlayState(
            "Preparing recording " + index + "/" + total,
            DescribePlan(plan),
            "Checking replay and OBS",
            false,
            false,
            null,
            null));
    }

    public static void ShowStartingObs(RecordingPlan plan, int index, int total)
    {
        SetState(new OverlayState(
            "Starting OBS " + index + "/" + total,
            DescribePlan(plan),
            "Recording will begin before replay launch",
            false,
            false,
            null,
            null));
    }

    public static void ShowRecording(RecordingPlan plan, int index, int total)
    {
        SetState(new OverlayState(
            "Recording " + index + "/" + total,
            DescribePlan(plan),
            "OBS is recording",
            true,
            false,
            null,
            null));
    }

    public static void ShowPlanFinished(RecordingPlan plan, int succeeded, int failed)
    {
        SetState(new OverlayState(
            "Recording saved",
            DescribePlan(plan),
            "Succeeded: " + succeeded + "  Failed: " + failed,
            false,
            false,
            null,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5)));
    }

    public static void ShowPlanFailed(RecordingPlan plan, int succeeded, int failed)
    {
        SetState(new OverlayState(
            "Recording failed",
            DescribePlan(plan),
            "Succeeded: " + succeeded + "  Failed: " + failed,
            false,
            true,
            null,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8)));
    }

    public static void ShowBatchCompleted(int succeeded, int failed)
    {
        SetState(new OverlayState(
            "Batch complete",
            "All queued recordings have finished",
            "Succeeded: " + succeeded + "  Failed: " + failed,
            false,
            failed > 0,
            null,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15)));
    }

    public static void ShowBatchStopped(int succeeded, int failed)
    {
        SetState(new OverlayState(
            "Batch stopped",
            "Stopped after current recording",
            "Succeeded: " + succeeded + "  Failed: " + failed,
            false,
            failed > 0,
            null,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(12)));
    }

    public static void SetControlPanelState(ControlPanelState state)
    {
        lock (Gate)
        {
            _controlPanelState = state ?? ControlPanelState.Empty();
        }
    }

    public static void SetControlPanelActions(ControlPanelActions actions)
    {
        lock (Gate)
        {
            _controlPanelActions = actions ?? new ControlPanelActions();
        }
    }

    public static void SetControlPanelVisible(bool visible)
    {
        lock (Gate)
        {
            _controlPanelVisible = visible;
        }
    }

    public static void ShowManualStarting(string outputBaseName)
    {
        SetState(new OverlayState(
            "Manual replay recording",
            outputBaseName,
            "Starting OBS",
            false,
            false,
            null,
            null));
    }

    public static void ShowManualRecording(string outputBaseName)
    {
        SetState(new OverlayState(
            "Manual replay recording",
            outputBaseName,
            "OBS is recording",
            true,
            false,
            null,
            null));
    }

    public static void ShowManualFinished(string outputBaseName)
    {
        SetState(new OverlayState(
            "Manual recording saved",
            outputBaseName,
            "",
            false,
            false,
            null,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5)));
    }

    public static void ShowManualFailed(string outputBaseName)
    {
        SetState(new OverlayState(
            "Manual recording failed",
            outputBaseName,
            "Check the log for details",
            false,
            true,
            null,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8)));
    }

    private static void SetState(OverlayState state)
    {
        lock (Gate)
        {
            _state = state;
        }
    }

    private static string DescribePlan(RecordingPlan plan)
    {
        var info = plan.QueueItem.ReplayInfo;
        var song = string.IsNullOrWhiteSpace(info.SongName) ? plan.OutputBaseName : info.SongName;
        var difficulty = string.IsNullOrWhiteSpace(info.Difficulty) ? "Unknown" : info.Difficulty;
        return song + " [" + difficulty + "]";
    }

    private void OnGUI()
    {
        HandleKeyboard();
        var state = GetVisibleState();
        var controlState = GetControlPanelState(out var controlActions, out var controlPanelVisible);

        if (!state.IsVisible && !controlPanelVisible)
        {
            return;
        }

        EnsureStyles();

        GUI.depth = -10000;
        if (state.IsVisible)
        {
            DrawStatusPanel(state);
        }

        if (controlPanelVisible)
        {
            DrawControlPanel(controlState, controlActions);
        }
    }

    private void DrawStatusPanel(OverlayState state)
    {
        var screenWidth = Screen.width <= 0 ? 1920 : Screen.width;
        var width = Math.Min(Width, Math.Max(320, screenWidth - Margin * 2));
        var height = string.IsNullOrEmpty(state.Footer) ? MinHeight : MinHeight + 24;
        var rect = new Rect(Margin, Margin, width, height);

        var oldColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.Box(rect, GUIContent.none, _boxStyle);
        GUI.color = state.IsError
            ? new Color(1f, 0.22f, 0.18f, 1f)
            : state.IsRecording
                ? new Color(1f, 0.08f, 0.08f, 1f)
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

    private void DrawControlPanel(ControlPanelState state, ControlPanelActions actions)
    {
        var screenWidth = Screen.width <= 0 ? 1920 : Screen.width;
        var width = Math.Min(520, Math.Max(320, screenWidth - Margin * 2));
        var rect = new Rect(screenWidth - width - Margin, Margin, width, 286);

        var oldColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.Box(rect, GUIContent.none, _boxStyle);
        GUI.color = new Color(0.18f, 0.62f, 1f, 1f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 6, rect.height), Texture2D.whiteTexture);
        GUI.color = oldColor;

        var y = rect.y + 14;
        GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 24), "Auto Replay Recorder", _panelHeaderStyle);
        y += 32;
        GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 20), "Session: " + state.SessionName, _panelLabelStyle);
        y += 22;
        GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 20), "Queue: " + state.QueueCount + " pending / " + state.CompletedCount + " completed", _panelLabelStyle);
        y += 22;
        GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 20), "Import: " + state.ImportSummary, _panelLabelStyle);
        y += 22;
        GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 20), "OBS: " + state.ObsSummary, _panelLabelStyle);
        y += 22;
        GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 20), "Status: " + state.RuntimeStatus, _panelLabelStyle);
        y += 28;
        GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 20), "Import folder:", _footerStyle);
        y += 20;
        GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 20), state.ImportFolder, _panelLabelStyle);
        y += 32;

        var buttonWidth = (rect.width - 48) / 3;
        if (GUI.Button(new Rect(rect.x + 18, y, buttonWidth, 28), "Rescan / Import"))
        {
            actions.RescanRequested?.Invoke();
        }

        GUI.enabled = state.CanStartBatch;
        if (GUI.Button(new Rect(rect.x + 24 + buttonWidth, y, buttonWidth, 28), "Start Batch"))
        {
            actions.StartBatchRequested?.Invoke();
        }

        GUI.enabled = state.CanStopAfterCurrent;
        if (GUI.Button(new Rect(rect.x + 30 + buttonWidth * 2, y, buttonWidth, 28), "Stop After Current"))
        {
            actions.StopAfterCurrentRequested?.Invoke();
        }

        GUI.enabled = true;
        y += 36;
        if (GUI.Button(new Rect(rect.x + 18, y, buttonWidth, 28), "Clear Completed"))
        {
            actions.ClearCompletedRequested?.Invoke();
        }

        if (GUI.Button(new Rect(rect.x + 24 + buttonWidth, y, buttonWidth, 28), "Hide Panel"))
        {
            SetControlPanelVisible(false);
        }
    }

    private static void HandleKeyboard()
    {
        var current = Event.current;
        if (current == null || current.type != EventType.KeyDown || current.keyCode != KeyCode.F9)
        {
            return;
        }

        lock (Gate)
        {
            _controlPanelVisible = !_controlPanelVisible;
        }

        current.Use();
    }

    private OverlayState GetVisibleState()
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_state.ExpiresAtUtc.HasValue && _state.ExpiresAtUtc.Value <= now)
            {
                return OverlayState.Hidden();
            }

            if (_state.CountdownEndsAtUtc.HasValue)
            {
                var seconds = Math.Max(0, (int)Math.Ceiling((_state.CountdownEndsAtUtc.Value - now).TotalSeconds));
                return _state.WithFooter("Starts in " + seconds + "s");
            }

            return _state;
        }
    }

    private static ControlPanelState GetControlPanelState(
        out ControlPanelActions actions,
        out bool controlPanelVisible)
    {
        lock (Gate)
        {
            actions = _controlPanelActions;
            controlPanelVisible = _controlPanelVisible;
            return _controlPanelState;
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
            normal =
            {
                background = Texture2D.blackTexture,
                textColor = Color.white
            }
        };

        _headerStyle = CreateLabelStyle(22, FontStyle.Bold, Color.white);
        _detailStyle = CreateLabelStyle(18, FontStyle.Normal, new Color(0.92f, 0.96f, 1f, 1f));
        _footerStyle = CreateLabelStyle(15, FontStyle.Normal, new Color(0.76f, 0.86f, 0.95f, 1f));
        _panelHeaderStyle = CreateLabelStyle(20, FontStyle.Bold, Color.white);
        _panelLabelStyle = CreateLabelStyle(14, FontStyle.Normal, new Color(0.92f, 0.96f, 1f, 1f));
    }

    private static GUIStyle CreateLabelStyle(int size, FontStyle fontStyle, Color color)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = fontStyle,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal =
            {
                textColor = color
            }
        };
    }

    private sealed class OverlayState
    {
        public OverlayState(
            string header,
            string detail,
            string footer,
            bool isRecording,
            bool isError,
            DateTimeOffset? countdownEndsAtUtc,
            DateTimeOffset? expiresAtUtc)
        {
            Header = header;
            Detail = detail;
            Footer = footer;
            IsRecording = isRecording;
            IsError = isError;
            CountdownEndsAtUtc = countdownEndsAtUtc;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string Header { get; }

        public string Detail { get; }

        public string Footer { get; }

        public bool IsRecording { get; }

        public bool IsError { get; }

        public DateTimeOffset? CountdownEndsAtUtc { get; }

        public DateTimeOffset? ExpiresAtUtc { get; }

        public bool IsVisible => !string.IsNullOrWhiteSpace(Header) || !string.IsNullOrWhiteSpace(Detail);

        public static OverlayState Hidden()
        {
            return new OverlayState("", "", "", false, false, null, DateTimeOffset.UtcNow);
        }

        public OverlayState WithFooter(string footer)
        {
            return new OverlayState(Header, Detail, footer, IsRecording, IsError, CountdownEndsAtUtc, ExpiresAtUtc);
        }
    }
}

public sealed class ControlPanelState
{
    public string SessionName { get; set; } = "";

    public int QueueCount { get; set; }

    public int CompletedCount { get; set; }

    public string ImportSummary { get; set; } = "";

    public string ObsSummary { get; set; } = "";

    public string RuntimeStatus { get; set; } = "";

    public string ImportFolder { get; set; } = "";

    public bool CanStartBatch { get; set; }

    public bool CanStopAfterCurrent { get; set; }

    public static ControlPanelState Empty()
    {
        return new ControlPanelState
        {
            SessionName = "Unknown",
            ImportSummary = "Not scanned",
            ObsSummary = "Not checked",
            RuntimeStatus = "Starting",
            ImportFolder = ""
        };
    }
}

public sealed class ControlPanelActions
{
    public Action? RescanRequested { get; set; }

    public Action? StartBatchRequested { get; set; }

    public Action? StopAfterCurrentRequested { get; set; }

    public Action? ClearCompletedRequested { get; set; }
}
