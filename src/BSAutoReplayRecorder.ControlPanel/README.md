# Replay Recorder Control Panel

The control panel is the browser dashboard for the recorder. Most users should start it with the repo launchers instead of running this project directly.

Start the full stack:

```bat
start.bat
```

Then open:

```text
http://127.0.0.1:5770
```

The control panel owns setup, queue management, worker launch, recording settings, readiness checks, and run progress.

## Main Views

`Run` is where you record replays.

- Import `.bsor` files.
- Search and filter the queue.
- Edit queue item names and metadata.
- Move, remove, retry, or open completed recordings.
- Launch all managed Beat Saber workers.
- Start and stop the run.
- Watch worker progress, map status, sync status, and recent events.

`Settings` is where you choose how the recordings should look and run.

- Select the recording monitor.
- Choose a feed preset.
- Enable or disable managed instances.
- Save capture settings such as FPS, resolution, bitrate, encoder, and output container.
- Adjust ProcessLoopback audio settings.
- Edit Beat Saber launch arguments.
- Choose run guards such as requiring workers, matching baselines, audio, and taskbar hiding.
- Change replay display options under `Game settings`.

`Diagnostics` is where you check whether the stack is ready.

- `Launch` starts the configured workers.
- `Launch + Verify` starts workers and watches for process, plugin, recorder-host, and audio readiness.
- `Check Baseline` compares managed instances against the baseline.
- `Replay Sync` shows marker correction status.
- `Recent Evidence` shows errors, output completions, map issues, sync events, and instance failures.

`Files` is where you verify local paths.

- Workspace directory.
- Recording output directory.
- Managed Beat Saber instance root.
- Shared song folders.
- Shared content folders.
- Shared-folder check and repair actions.

## First Run From The Panel

After `install.bat` finishes:

1. Open the dashboard.
2. Go to `Files` and confirm the workspace and recording paths.
3. Go to `Settings` and choose the recording monitor and feed preset.
4. Save settings if the page shows unsaved changes.
5. Go to `Diagnostics` and press `Launch + Verify`.
6. Return to `Run`.
7. Import `.bsor` files.
8. Wait for maps, workers, baseline, audio, disk, and sync to look ready.
9. Press `Start run`.

Finished recordings are written under the workspace's `Recordings` folder. The default path is:

```text
ControlPanelWorkspace\Recordings
```

## Managed Beat Saber Instances

The panel launches managed Beat Saber folders, not your normal daily-play folder. By default they live at:

```text
ControlPanelWorkspace\BeatSaberInstances
```

Each worker has its own folder, plugin settings, recorder host port, output folder, and launch slot. The panel can create missing instances from the configured source Beat Saber folder, then install this recorder's worker plugin into each managed copy.

Do not set the managed instance root to your everyday Beat Saber install. Use a separate recorder workspace.

## Maps And Shared Songs

When `.bsor` files are imported, the panel checks whether the matching map exists in the shared song folders. If a map is missing, the panel can:

- find an already installed matching map;
- download the map by hash from BeatSaver;
- accept a manually uploaded song zip for that queue item.

Shared songs and shared custom content are linked into the managed workers so every worker sees the same replay content.

## Readiness Checks

The status chips at the top of the dashboard summarize the things that can block a run:

- `Queue`: pending and total replay count.
- `Workers`: online managed Beat Saber workers.
- `Maps`: whether queued replays have maps.
- `Baseline`: whether configured instances match the expected files and recorder settings.
- `Audio`: whether ProcessLoopback can capture the launched Beat Saber processes.
- `Disk`: whether output folders are usable.
- `Sync`: whether completed recordings have automatic marker correction.

If the `Start run` button fails, the message usually points to one of these readiness checks.

## Stop And Quit

Use `Stop` to stop the current run, ask launched Beat Saber workers to exit the current replay, and leave the stack open.

Use `Quit` to stop the run, stop the recorder stack, and close tracked Beat Saber workers. This is the same cleanup path as:

```powershell
scripts\launcher\Stop-ReplayRecorder.ps1 -StopGames
```

## Local Settings

The control panel loads settings from the repo `settings.json` by default. The launcher and installer create that file from `settings.example.json` when needed.

For development, the control panel also accepts:

```powershell
$env:BSARR_CONTROL_PANEL_WORKSPACE = "D:\bsarr-workspace"
$env:BSARR_CONTROL_PANEL_URL = "http://127.0.0.1:5770"
$env:BSARR_SETTINGS_PATH = "D:\bsarr-workspace\settings.json"
```

The launcher and installer are still the recommended way to start normal user sessions.

## How It Works

The control panel is a local web app. It keeps queue and run state in the workspace, starts recorder hosts, launches managed Beat Saber workers, and hands out one replay assignment at a time. Workers report heartbeats and completion results back to the panel, and the panel verifies audio and sync before marking recordings complete.

The useful live truth endpoint is:

```text
http://127.0.0.1:5770/api/state
```

That endpoint is mainly for debugging; normal operation should happen through the dashboard.
