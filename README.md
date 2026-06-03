# Beat Saber Auto Replay Recorder

Batch recorder prototype for Beat Saber replay capture workflows.

The target workflow is:

1. Drop `.bsor` replay files into an input folder.
2. A Beat Saber mod builds a replay queue from those files.
3. The mod starts OBS recording, launches each replay, waits for completion, stops OBS, and advances to the next replay.
4. Finished recordings are named consistently for editing.

## Current State

This repo contains the first working foundation for that Option C plan:

- `.bsor` metadata parser
- replay queue loader
- recording plan and output filename generator
- OBS WebSocket protocol request scaffolding
- replay provider/reference classifier for BeatLeader, legacy ScoreSaber, and ScoreSaber 2
- Beat Saber / BSIPA plugin that can batch-launch BeatLeader `.bsor` replays
- OBS WebSocket recorder integration
- in-game status overlay and F9 control panel
- session-based import folders
- local test harness for the parser and queue logic

The remaining integration work is provider-specific:

- bind ScoreSaber replay launching separately; ScoreSaber 2 should be treated as a different provider

## Session Workflow

The plugin now organizes replay batches by session. By default, the active session is `Default`.

1. Open `UserData/BSAutoReplayRecorder/settings.json`.
2. Set `ActiveSessionName` to the week or pool name, for example `Week 1 Pool Reveal`.
3. Drop `.bsor` files into:

```text
UserData/BSAutoReplayRecorder/Sessions/<ActiveSessionName>/Import
```

On game start, or when pressing `Rescan / Import` in the F9 panel, valid imports are copied into the session `Queue` folder. Imported originals move to `Imported`, duplicate files move to `Duplicate Imports`, and unreadable files move to `Failed Imports`.

Each session also has its own `completed-replays.json`, so rerunning one week does not affect another week.

## In-Game Controls

Press `F9` in game to toggle the recorder panel.

- `Rescan / Import` imports new `.bsor` files from the active session import folder.
- `Check Setup` verifies OBS websocket access and recorder folders.
- `Test OBS` records a short 3 second OBS clip and reports the output path.
- `Start Batch` starts the pending queue when auto-start is disabled or after adding files.
- `Stop After Current` lets the current recording finish, then stops the batch.
- `Clear Completed` clears the active session's completed state so the same queue can be rerun.
- `Switch` changes or creates the active session from the session text field.
- Folder buttons open the import, queue, session, settings, and logs locations.

The status overlay shows preflight, OBS start, active recording, delays between recordings, failures, and completion.

## Locked Standard Settings

Fresh settings files default to `SettingsLockMode: Standard`. Existing settings files that do not already opt into `Standard` are migrated without forcing the lock. The old `Tournament` value is still accepted as a legacy alias. That lock pins the settings that were painful during recording tests:

- pre-roll and post-roll are `0`
- delay between recordings is `5` seconds
- dry-run is off
- completed replays are skipped
- failed recordings do not stop the whole batch
- session folders and auto-import are enabled
- preflight replay validation runs before OBS recording starts

Set `SettingsLockMode` to `None` if you need to override those locked values. Set `MoveRecordingsToOutputDirectory` to `true` if you want OBS outputs moved into the active session's `Recordings` folder; leave it `false` to keep recordings in OBS's configured output folder.

The standard lock never changes OBS host, port, or password. When the settings file is rewritten during migration, the previous file is copied next to it as a timestamped `.bak`.

## Quick Check

Run the core test harness:

```powershell
dotnet run --project tests/BSAutoReplayRecorder.Core.Tests
```

## Recommended Architecture

Keep the boring batch logic in `BSAutoReplayRecorder.Core` and keep game-specific logic in the plugin layer.

```text
Beat Saber mod
  |
  |-- Replay queue and metadata parser      src/BSAutoReplayRecorder.Core
  |-- BeatLeader replay launcher adapter    src/BSAutoReplayRecorder.Plugin
  |-- OBS WebSocket recorder adapter        src/BSAutoReplayRecorder.Plugin
  |-- In-game menu / progress UI            src/BSAutoReplayRecorder.Plugin
```

This split matters because the queue/parser can be tested quickly outside the game, while the plugin adapter can evolve around whatever API the installed replay mod exposes.

## Replay Providers

Do not assume every replay is BeatLeader `.bsor`.

| Provider | Input shape | Current recorder handling |
| --- | --- | --- |
| BeatLeader | local `.bsor`, BeatLeader replay URL, BeatLeader score URL | direct metadata parsing for local `.bsor`; URL/download adapter still needed |
| ScoreSaber legacy | local `UserData/ScoreSaber/Replays/*.dat`, old ScoreSaber score links | classified, but playback must go through a ScoreSaber-specific adapter |
| ScoreSaber 2 | new website/API score links and browser replay surfaces | classified separately so the adapter can follow the new API/mod refresh |

ScoreSaber 2 is not just an old API skin. Its announcement says the backend has a new API with OpenAPI support, the old API remains via compatibility layers, browser replays are powered by ArcViewer, and the mod refresh is planned separately. That is why the project keeps replay providers behind an adapter boundary instead of hardcoding BeatLeader or legacy ScoreSaber behavior.

Sources:

- BeatLeader BSOR format: https://github.com/BeatLeader/BS-Open-Replay
- BeatLeader server / replay-first model: https://github.com/BeatLeader/beatleader-server
- ScoreSaber API docs: https://docs.scoresaber.com/
- ScoreSaber 2 announcement: https://www.patreon.com/posts/announcing-2-157688806

## OBS Setup

OBS Studio 28+ includes obs-websocket. Enable it in:

`Tools -> WebSocket Server Settings`

Default values:

- Host: `127.0.0.1`
- Port: `4455`
- Password: configurable

## Beat Saber Mod Setup

This project is intended for PC Beat Saber modding with BSIPA. The plugin scaffold is documented in:

`src/BSAutoReplayRecorder.Plugin/README.md`
