# Beat Saber Auto Replay Recorder

<img src="https://i.imgur.com/Cp0L5BX.png" alt="Beat Saber Auto Replay Recorder logo" width="150">



## Record batches of Beat Saber `.bsor` replays from a local browser control panel.
### Tested with 1.40.6, Plugin built for 1.40.8, 1.39.1

This project runs one or more managed Beat Saber worker copies, plays each replay through BeatLeader, records the game window with FFmpeg, captures that Beat Saber process's audio, sync-corrects the result, and writes finished video files to a local recordings folder.

The normal user flow is:

1. Run `install.bat`.
2. Open the control panel at `http://127.0.0.1:5770`.
3. Launch the managed Beat Saber workers.
4. Import `.bsor` replay files.
5. Press `Start run`.
6. Pick up the finished recordings from `ControlPanelWorkspace\Recordings`.

The recorder does not use OBS, obs-websocket, VB-CABLE, or BSManager as runtime tools.

## What You Need

- BeatLeader
- Windows.
- PowerShell.
- .NET SDK 10.
- FFmpeg and ffprobe. The installer can find common WinGet, Chocolatey, and PATH installs, offer to install `Gyan.FFmpeg` with WinGet, or save a custom `ffmpeg.exe` path.
- A local PC Beat Saber install.
- BSIPA and BeatLeader installed in the Beat Saber folder used as the source template.
- Optional: `tools\SetDpi\SetDpi.exe`, or `BSARR_SETDPI_PATH`, if you use presets that temporarily change Windows display scaling.

The default plugin manifest targets Beat Saber `1.40.6`, BSIPA `^4.3.6`, and BeatLeader `^0.9.33`. You can build against a different Beat Saber version later, but the first-time install expects a working BeatLeader setup in the source folder.

## First-Time Install

From the repo root:

```bat
install.bat
```

The installer creates a local `settings.json` from `settings.example.json` if one does not already exist. That local file is ignored by git and is where machine-specific values live.

During install, you may be asked for:

- FFmpeg. Press Enter to retry detection, accept the WinGet install prompt, or paste a path to `ffmpeg.exe`. The selected FFmpeg install must also include `ffprobe.exe`, normally in the same folder.
- the Beat Saber source folder. Press Enter to use the detected Steam install, or paste another folder that contains `Beat Saber.exe`.
- the number of managed Beat Saber workers to create. The default is 3. Choose 1 for the simplest first test, or 2-4 for parallel recording.
- the display scaling helper. If `tools\SetDpi\SetDpi.exe` is missing, the installer can download `SetDpi.exe` from the latest `imniko/SetDPI` GitHub release; if that fails, you can paste a local path or keep display scaling disabled.
- whether to create missing managed copies. Say yes for a new install.
- whether to import existing `CustomLevels` and `CustomWIPLevels`. The recommended answer is no unless you intentionally want to copy those songs into the recorder's shared-song layout.

The installer will:

- create or update `settings.json`;
- resolve FFmpeg/ffprobe and save `ffmpegPath` when it discovers or installs a usable FFmpeg;
- create managed Beat Saber folders under `ControlPanelWorkspace\Instances`;
- build the control panel, recorder host, ProcessLoopback helper, and worker plugin;
- deploy the plugin into each managed Beat Saber worker;
- write `ControlPanelWorkspace\control-panel-state.json`;
- start recorder hosts and the browser control panel;
- repair shared song/content folder links automatically, then run baseline checks.

## Local Settings

Edit `settings.json` when you want defaults to stick between launches. Common values:

```json
{
  "preset": "4k-monitor-2x2",
  "bindUrl": "http://127.0.0.1:5770",
  "workspaceDirectory": "ControlPanelWorkspace",
  "beatSaberInstancesRoot": "ControlPanelWorkspace/Instances",
  "beatSaberInstanceNamePrefix": "I-",
  "instanceCount": 3,
  "maxConcurrentRecordings": 3,
  "sourceBeatSaberPath": "",
  "ffmpegPath": ""
}
```

Keep personal paths in `settings.json`. Keep portable defaults in `settings.example.json`.

The launcher also accepts environment overrides:

```powershell
$env:BSARR_CONTROL_PANEL_WORKSPACE = "D:\bsarr-workspace"
$env:BSARR_CONTROL_PANEL_URL = "http://127.0.0.1:5770"
```

## Daily Use

Start the recorder stack:

```bat
start.bat
```

Open:

```text
http://127.0.0.1:5770
```

In the control panel:

1. Go to `Files` and confirm the workspace, output, instance, shared-song, and shared-content paths.
2. Go to `Settings` and choose the recording monitor and feed preset.
3. Use `Diagnostics` -> `Launch + Verify` when you want the panel to launch workers and check readiness.
4. Use `Run` -> `Import .bsor` to add replays.
5. Confirm the status chips for workers, maps, baseline, audio, disk, and sync.
6. Press `Start run`.
7. Review completed queue items and open recordings from the queue details.

Stop the recorder stack:

```bat
stop.bat
```

Stop the stack and any Beat Saber workers launched by the control panel:

```powershell
scripts\launcher\Stop-ReplayRecorder.ps1 -StopGames
```

The dashboard `Quit` button uses the same stop-and-games path.

If the control panel sees no recorder activity for 20 minutes and no run is active, it automatically uses that same stop-and-games path. Set `idleShutdownMinutes` in local `settings.json` to change the timeout, or `0` to disable it.

## Presets

The `Settings` view recommends feed presets based on the selected recording monitor.

| Preset | Best for | Output per worker |
| --- | --- | --- |
| `single-1080p` | one 1080p feed | 1920x1080 |
| `single-1440p` | one 1440p feed | 2560x1440 |
| `single-4k` | one 4K feed | 3840x2160 |
| `1440p-monitor-2x2` | up to four feeds on a 1440p monitor | 1280x720 |
| `4k-monitor-2x2` | up to four feeds on a 4K monitor | 1920x1080 |

The multi-feed presets can run 2, 3, or 4 managed workers. If four concurrent captures are unstable on your machine, lower the enabled instance count in the control panel.

Warning: the current FFmpeg `ddagrab` video path records a desktop region, not an application-only frame. Keep desktop overlays and notifications disabled while recording, because Steam, Windows, Discord, or other popups drawn over the capture region can appear in the finished video. ProcessLoopback still captures only the assigned Beat Saber process tree for audio, so unrelated notification sounds should not be muxed into the recording.

Open `Advanced Settings` only when you want to edit capture size, FPS, encoder, bitrate, container, launch arguments, audio, display scaling, or run guards directly.

## Where Files Go

By default, runtime files live under:

```text
ControlPanelWorkspace/
  control-panel-state.json
  Queue/
  Logs/
    control-panel.out.log
    recorder-host-5757.out.log
  Recordings/
  Instances/
  SharedSongs/
  SharedContent/
  recorder-host-5757.settings.json
  started-processes.json
```

## How It Works

```text
Control panel
  -> owns settings, queue, worker launch, readiness checks, and run state
  -> assigns one replay at a time to each online worker

Beat Saber worker plugin
  -> registers with the control panel
  -> starts recording through its assigned recorder host
  -> plays a visual/audio sync marker
  -> launches the BeatLeader replay
  -> reports success, failure, output path, and sync metadata

Recorder host
  -> runs FFmpeg video capture
  -> captures audio from the assigned Beat Saber process with ProcessLoopback
  -> analyzes the sync marker
  -> trims pre-roll and muxes the final recording
```

The stack is intentionally control-panel-first. The plugin does not own the queue, and users should not need in-game recording menus.

## Important Notes

- Current replay playback is BeatLeader `.bsor` focused.
- ScoreSaber replay playback is not part of the current user workflow.
- The managed workers are local Beat Saber folders created for recording. Do not point the managed instance root at your everyday Beat Saber folder.
- Some `Game settings` options in the control panel are Beat Saber profile settings for the current Windows user. Change those intentionally, because Beat Saber stores some of them outside the managed game folders.
- The recorder expects maps to exist in the shared song folders. On import, the control panel checks maps and can download by hash from BeatSaver or accept a manual song zip upload for a queue item.

## Troubleshooting

If the control panel does not open, run:

```bat
start.bat
```

If FFmpeg is not found, re-run `install.bat` and accept the WinGet install prompt, or set the path in `settings.json`:

```json
{
  "ffmpegPath": "C:\\ffmpeg\\bin\\ffmpeg.exe"
}
```

The folder that contains `ffmpeg.exe` must also contain `ffprobe.exe`, because completed recordings are verified before they are accepted.

If workers do not connect, use `Diagnostics` -> `Launch + Verify`, then check that each managed Beat Saber instance has the worker plugin installed and BeatLeader available.

If audio fails, keep `Audio` set to `Process loopback` and make sure workers are launched from the control panel so the recorder knows the correct Beat Saber process id.

If maps are missing, use the queue item's map download/upload action or run shared-folder repair from `Files`.

If sync fails, the replay should fail instead of producing an unproven recording. Check the queue details and the adjacent `*.sync.json` file next to the recording.

## More Detail

- Control panel user guide: `src/BSAutoReplayRecorder.ControlPanel/README.md`
- Worker plugin user guide: `src/BSAutoReplayRecorder.Plugin/README.md`
- Recorder host user guide: `src/BSAutoReplayRecorder.RecorderHost/README.md`
