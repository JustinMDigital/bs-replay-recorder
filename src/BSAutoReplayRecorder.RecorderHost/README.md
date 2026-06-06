# Replay Recorder Host

The recorder host is the local capture service used by each Beat Saber worker.

Most users should not start this project directly. The launcher starts one recorder host per configured worker:

```bat
start.bat
```

Default ports start at:

```text
http://127.0.0.1:5757
```

Worker 1 uses port `5757`, worker 2 uses `5758`, and so on.

## What It Does

For each assigned replay, the recorder host:

- receives a start request from the Beat Saber worker plugin;
- runs FFmpeg video capture for the assigned game window or monitor region;
- starts ProcessLoopback audio capture for that exact Beat Saber process;
- writes temporary video and audio sidecars;
- receives a stop request after replay playback ends, including content-start timing;
- analyzes the sync marker;
- trims pre-roll exactly;
- muxes the final MKV or MP4;
- writes sync metadata next to the recording.

This keeps capture and encoding outside Unity while still letting the plugin control replay timing.

## Output Files

The control panel writes each worker's recordings under:

```text
ControlPanelWorkspace\Recordings\Instance 1
ControlPanelWorkspace\Recordings\Instance 2
ControlPanelWorkspace\Recordings\Instance 3
```

Completed recordings are visible from the queue details in the control panel.

When sync correction runs, a `*.sync.json` report is written next to the output file. If sync cannot be proven, the replay should fail instead of being marked complete.

## FFmpeg And Audio

The launcher resolves FFmpeg in this order:

- `-FfmpegPath` passed to the launcher;
- `ffmpegPath` in `settings.json`;
- common WinGet and Chocolatey install locations;
- `ffmpeg` on `PATH`.

It also resolves `ffprobe`, preferring `ffprobe.exe` next to the selected `ffmpeg.exe`, then common WinGet, Chocolatey, ShareX, and `PATH` locations. `ffprobe` is used by the control panel to verify completed recording audio.

The normal audio mode is `ProcessLoopback`. It records only the assigned Beat Saber process, so parallel workers do not get mixed together.

Do not configure OBS, desktop loopback, or virtual audio cables for the normal workflow.

## Health Checks

The control panel checks recorder hosts automatically. For manual debugging:

```text
GET http://127.0.0.1:5757/health
GET http://127.0.0.1:5757/status
```

If a worker cannot start recording, check `Diagnostics` in the control panel first. It shows whether the worker is online, whether the game process id is known, and whether the recorder host is reachable.

## Manual Commands

Only use these commands for development or troubleshooting.

Create a default config:

```powershell
dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- init-config
```

Run one host with a config:

```powershell
dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- serve --config "ControlPanelWorkspace\recorder-host-5757.settings.json"
```

Probe the configured capture backend:

```powershell
dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- probe --config "ControlPanelWorkspace\recorder-host-5757.settings.json"
```

Run a short manual capture:

```powershell
dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- record-once --duration 10 --output "manual-test"
```

Run a benchmark across multiple configs:

```powershell
dotnet run --project src/BSAutoReplayRecorder.RecorderHost -- benchmark --duration 10 --min-fps 60 --output "benchmark" --config "ControlPanelWorkspace\recorder-host-5757.settings.json" --config "ControlPanelWorkspace\recorder-host-5758.settings.json"
```

## API Surface

The worker plugin uses this local HTTP API:

```http
GET /health
GET /status
POST /recordings/start
POST /recordings/stop
```

Normal users do not need to call these endpoints. Use the control panel instead.

## Capture Template Notes

Recorder host configs include an FFmpeg argument template. The launcher writes the template needed for the selected control-panel preset. Templates are normalized to suppress the mouse cursor with `-draw_mouse 0` for `gdigrab` and `draw_mouse=0` for `ddagrab`.

Useful tokens include:

- `{output}`: quoted output path.
- `{windowTitle}`: requested window title.
- `{targetProcessId}`: Beat Saber process id for ProcessLoopback audio.
- `{fps}`: target capture FPS.
- `{videoSize}`: capture size as `WIDTHxHEIGHT`.
- `{encoder}`: video encoder, such as `h264_nvenc` or `libx264`.
- `{videoBitrate}`: FFmpeg bitrate value.
- `{monitorIndex}`: display index for desktop duplication capture.
- `{audioMode}`: `ProcessLoopback` or `None`.
- `{containerFlags}`: output container flags.

Manual template editing is an advanced troubleshooting path. The control panel presets should be enough for normal use.
