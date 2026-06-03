# Beat Saber Plugin

This folder is the BSIPA plugin layer for the auto replay recorder.

The core project handles:

- reading `.bsor` replay metadata
- building an ordered queue
- estimating recording length
- generating safe output names
- creating OBS WebSocket request payloads

The plugin layer adds:

- BSIPA entry point
- BeatLeader replay playback adapter
- OBS WebSocket transport
- batch recording state machine
- session import folders
- in-game status overlay and F9 control panel

## Local Instance

Set `BeatSaberDir` to the Beat Saber instance you want to build against:

```powershell
dotnet build src/BSAutoReplayRecorder.Plugin /p:BeatSaberDir="C:\path\to\Beat Saber"
```

## Current Plugin Scope

The current plugin is BeatLeader-only:

- initializes under BSIPA
- subscribes to BeatLeader replay started/finished events
- imports `.bsor` files from the active session import folder
- scans the active session queue
- includes a `BeatLeaderReplayPlaybackDriver` that decodes local `.bsor` files with BeatLeader's own decoder and calls `ReplayerMenuLoader.StartReplayAsync`
- starts/stops OBS through obs-websocket v5
- validates replay launchability before starting OBS
- shows batch status and controls in game
- opens import, queue, session, settings, and logs locations from the F9 panel
- switches/creates sessions without hand-editing settings

It does not yet include ScoreSaber playback adapters.

## Proposed Runtime State Machine

```text
Idle
  -> BuildQueue
  -> ConnectObs
  -> StartRecording
  -> LaunchReplay
  -> WaitForReplayEnd
  -> StopRecording
  -> ArchiveReplay
  -> StartRecording next item
  -> Complete
```

## BeatLeader Adapter

The installed BeatLeader plugin has a usable public replay surface:

- `BeatLeader.Models.Replay.ReplayDecoder.DecodeReplay(byte[])`
- `BeatLeader.Replayer.ReplayerMenuLoader.Instance`
- `ReplayerMenuLoader.StartReplayAsync(Replay, Player, ReplayerSettings, Action, CancellationToken)`
- `BeatLeader.Replayer.ReplayerLauncher.ReplayWasStartedEvent`
- `BeatLeader.Replayer.ReplayerLauncher.ReplayWasFinishedEvent`

Recommended approach:

- use direct references for the target Beat Saber instance first
- keep the adapter behind `IReplayPlaybackDriver`
- add a reflection fallback only if BeatLeader changes this API in a future Beat Saber/mod instance

The adapter should expose:

```csharp
Task StartReplayAsync(ReplayQueueItem item, CancellationToken cancellationToken);
bool IsReplayPlaying { get; }
event Action<ReplayQueueItem> ReplayEnded;
```

## ScoreSaber Adapters

ScoreSaber needs its own driver path.

Legacy ScoreSaber local replays are commonly found in `UserData/ScoreSaber/Replays` as `.dat` files. ScoreSaber 2 adds a new backend/API and browser replay support powered by ArcViewer while keeping legacy compatibility layers. Those details make it risky to hardcode a single ScoreSaber replay launch path.

Recommended approach:

- add one `IReplayPlaybackDriver` for local legacy ScoreSaber `.dat` playback
- add another driver for ScoreSaber 2 score/API references
- prefer in-game mod APIs once the ScoreSaber 2 PC mod refresh is available
- keep downloaded or resolved replay artifacts in the batch recorder input folder so retries are deterministic

## OBS Adapter

OBS Studio 28+ uses obs-websocket v5. The plugin can use the request builders in core, but still needs a WebSocket transport suitable for Unity/BSIPA.

OBS requests needed for the MVP:

- `StartRecord`
- `StopRecord`

Nice-to-have later:

- `SetProfileParameter` to adjust filename formatting
- `SetCurrentProgramScene` for map-pool reveal scenes
- `GetRecordStatus` for recovery if OBS and Beat Saber get out of sync
