# Beat Saber Worker Plugin

This is the BSIPA plugin that turns a managed Beat Saber copy into a recorder worker.

Most users should not install or configure this plugin by hand. Run the root installer:

```bat
install.bat
```

The installer builds the plugin and deploys it into each managed Beat Saber worker under:

```text
ControlPanelWorkspace\BeatSaberInstances
```

## What The Plugin Does

When a managed Beat Saber worker starts, the plugin:

- registers with the control panel;
- sends regular heartbeats;
- waits for one replay assignment at a time;
- asks its recorder host to start capture;
- plays a short visual/audio sync marker;
- launches the assigned BeatLeader `.bsor` or ScoreSaber `.dat` replay;
- waits for replay playback to finish;
- stops capture with timing information;
- reports the output path, result, error details, and sync metadata.

The plugin does not own the queue. The browser control panel owns imports, ordering, retries, run start, and run stop.

## What You See In Game

When idle, the worker can show a small connection/status overlay so you know it is connected to the control panel.

During recording, the plugin avoids normal progress text in the game view so status UI is not burned into the captured video. Watch the browser control panel for run progress.

## Requirements

The managed worker needs:

- Beat Saber.
- BSIPA.
- BeatLeader.
- ScoreSaber when ScoreSaber replays are queued.
- `BSAutoReplayRecorder.Plugin.dll` in `Plugins`.
- `BSAutoReplayRecorder.Core.dll` in `Libs`.
- `UserData\BSAutoReplayRecorder\settings.json`.

The installer handles these recorder files. You are responsible for having a working Beat Saber, BSIPA, and BeatLeader setup in the source folder used to create workers. Install ScoreSaber in the source folder too if you want ScoreSaber replay playback.

## Worker Settings

Each managed worker has a settings file:

```text
UserData\BSAutoReplayRecorder\settings.json
```

The installer writes this file for each worker. A typical worker section looks like:

```json
{
  "RecorderHost": {
    "BaseUrl": "http://127.0.0.1:5757",
    "WindowTitle": "Beat Saber"
  },
  "ControlPanelWorker": {
    "Enabled": true,
    "BaseUrl": "http://127.0.0.1:5770",
    "WorkerName": "Instance 1",
    "PreferredInstanceIndex": 0,
    "PollIntervalSeconds": 1,
    "HeartbeatIntervalSeconds": 2,
    "IdleShutdownMinutes": 20
  }
}
```

The plugin stores a `WorkerId` after it registers for the first time. Leave that value alone unless you intentionally want the control panel to treat the worker as new.

## Replay Support

The current workflow supports BeatLeader `.bsor` files and ScoreSaber `.dat` replay files. BeatLeader playback uses the installed BeatLeader mod. ScoreSaber playback resolves ScoreSaber's replay loader at runtime and uses the same recorder start, sync marker, lag guard, stop, mux, and reporting path as BeatLeader.

BeatLeader score/replay URLs and ScoreSaber 2 score/replay URLs are imported by the control panel, downloaded to local replay files, and assigned to workers with provider metadata.

## Fail-Safe Behavior

A worker should report a replay as failed instead of silently producing a questionable recording when:

- the control panel or recorder host cannot be reached;
- the control panel stops responding for `IdleShutdownMinutes` (defaults to 20); then the worker exits game to avoid leaving Beat Saber open when the server is down;
- the replay cannot be launched;
- Beat Saber playback fails or times out;
- ProcessLoopback audio cannot be captured;
- automatic sync correction is not proven;
- replay playback has a lag spike severe enough to invalidate the capture.

The control panel shows these failures in queue details and recent evidence.

## Manual Build

Only use this when the installer cannot build the plugin for your Beat Saber folder. The installer is still preferred because it also deploys files and writes worker settings.

```powershell
dotnet build src/BSAutoReplayRecorder.Plugin/BSAutoReplayRecorder.Plugin.csproj -p:BeatSaberDir="C:\path\to\Beat Saber"
```

Build against a different Beat Saber version:

```powershell
dotnet build src/BSAutoReplayRecorder.Plugin/BSAutoReplayRecorder.Plugin.csproj -p:BeatSaberDir="C:\path\to\Beat Saber" -p:BeatSaberGameVersion="1.40.8"
```
