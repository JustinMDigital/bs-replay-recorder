let state = null;
let toastTimeout = null;
let selectedReplayFiles = [];
let editingQueueId = null;
let selectedQueueId = null;
let queueSearchText = '';
let isRendering = false;
let settingsDirty = false;
let activeView = 'run';
let setupAssistantHidden = readSetupAssistantHidden();
let displayInfo = { displays: [] };
let draggedQueueId = null;
let pendingSetupEnabledInstanceCount = null;
let lastRunPlanPlayheadLeftPx = null;
let runPlanPlayheadInstantTimeout = null;

const runPlanTimingDefaults = Object.freeze({
  startLeadInSeconds: 3,
  recorderStartupSeconds: 1.25,
  syncMarkerSeconds: 0.95,
  recorderFinalizationSeconds: 4,
  interReplayGapSeconds: 5
});

const defaultLaunchArguments = '-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 --no-yeet fpfc --verbose';
const windowed720pLaunchArguments = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 --no-yeet fpfc --verbose';
const windowed1440pLaunchArguments = '-screen-fullscreen 0 -screen-width 2560 -screen-height 1440 --no-yeet fpfc --verbose';
const windowed4kLaunchArguments = '-screen-fullscreen 0 -screen-width 3840 -screen-height 2160 --no-yeet fpfc --verbose';
const minManagedInstanceCount = 1;
const maxManagedInstanceCount = 4;
const visibleManagedInstanceSlots = maxManagedInstanceCount;
const managedInstanceNamePrefix = 'BSARR I-';
const launchPresets = {
  '4k-monitor-2x2': {
    instanceCount: 4,
    maxConcurrentRecordings: 4,
    targetFps: 60,
    captureWidth: 1920,
    captureHeight: 1080,
    videoBitrateKbps: 12000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Performance',
    beatSaberLaunchArguments: defaultLaunchArguments,
    manageDisplayScale: true,
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150,
    hideTaskbarDuringRun: true
  },
  '1440p-monitor-2x2': {
    instanceCount: 4,
    maxConcurrentRecordings: 4,
    targetFps: 60,
    captureWidth: 1280,
    captureHeight: 720,
    videoBitrateKbps: 8000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Performance',
    beatSaberLaunchArguments: windowed720pLaunchArguments,
    manageDisplayScale: true,
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150,
    hideTaskbarDuringRun: true
  },
  'single-1080p': {
    instanceCount: 1,
    maxConcurrentRecordings: 1,
    captureWidth: 1920,
    captureHeight: 1080,
    beatSaberLaunchArguments: defaultLaunchArguments,
    manageDisplayScale: false,
    hideTaskbarDuringRun: false
  },
  'single-1440p': {
    instanceCount: 1,
    maxConcurrentRecordings: 1,
    captureWidth: 2560,
    captureHeight: 1440,
    beatSaberLaunchArguments: windowed1440pLaunchArguments,
    manageDisplayScale: false,
    hideTaskbarDuringRun: false
  },
  'single-4k': {
    instanceCount: 1,
    maxConcurrentRecordings: 1,
    captureWidth: 3840,
    captureHeight: 2160,
    beatSaberLaunchArguments: windowed4kLaunchArguments,
    manageDisplayScale: false,
    hideTaskbarDuringRun: false
  },
  'windowed-1080p': {
    captureWidth: 1920,
    captureHeight: 1080,
    beatSaberLaunchArguments: defaultLaunchArguments
  },
  'windowed-720p': {
    captureWidth: 1280,
    captureHeight: 720,
    beatSaberLaunchArguments: windowed720pLaunchArguments
  }
};
const launchPresetApplyDefaults = {
  'single-1080p': {
    targetFps: 60,
    videoBitrateKbps: 12000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Balanced',
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150
  },
  'single-1440p': {
    targetFps: 60,
    videoBitrateKbps: 18000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Balanced',
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150
  },
  'single-4k': {
    targetFps: 60,
    videoBitrateKbps: 32000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Quality',
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150
  }
};
const setupProfiles = {
  'grid-1080p': {
    launchPreset: '4k-monitor-2x2',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: true,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'grid-720p': {
    launchPreset: '1440p-monitor-2x2',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: true,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'single-1080p': {
    launchPreset: 'single-1080p',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: false,
      manageDisplayScale: false,
      recordingDisplayScalePercent: 100,
      restoreDisplayScalePercent: 150,
      hideTaskbarDuringRun: false,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'single-1440p': {
    launchPreset: 'single-1440p',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: false,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'single-4k': {
    launchPreset: 'single-4k',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: false,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  }
};
setupProfiles['quad-4k'] = setupProfiles['grid-1080p'];
setupProfiles['quad-1440p'] = setupProfiles['grid-720p'];
const feedPresetDefinitions = [
  {
    profileId: 'single-1080p',
    launchPreset: 'single-1080p',
    title: '1 x 1080p',
    detail: 'One full-resolution feed for a 1920 x 1080 monitor.',
    minWidth: 1920,
    minHeight: 1080,
    tier: '1080p'
  },
  {
    profileId: 'single-1440p',
    launchPreset: 'single-1440p',
    title: '1 x 1440p',
    detail: 'One full-resolution feed for a 2560 x 1440 monitor.',
    minWidth: 2560,
    minHeight: 1440,
    tier: '1440p'
  },
  {
    profileId: 'grid-720p',
    launchPreset: '1440p-monitor-2x2',
    title: 'Up to 4 x 720p',
    detail: 'A 2 x 2 grid of 1280 x 720 feeds on a 1440p monitor.',
    minWidth: 2560,
    minHeight: 1440,
    tier: '1440p'
  },
  {
    profileId: 'single-4k',
    launchPreset: 'single-4k',
    title: '1 x 4K',
    detail: 'One full-resolution feed for a 3840 x 2160 monitor.',
    minWidth: 3840,
    minHeight: 2160,
    tier: '4k'
  },
  {
    profileId: 'grid-1080p',
    launchPreset: '4k-monitor-2x2',
    title: 'Up to 4 x 1080p',
    detail: 'A 2 x 2 grid of 1920 x 1080 feeds on a 4K monitor.',
    minWidth: 3840,
    minHeight: 2160,
    tier: '4k'
  }
];
const launchPresetFieldIds = [
  'targetFps',
  'captureWidth',
  'captureHeight',
  'videoBitrateKbps',
  'outputFormat',
  'encoder',
  'qualityMode',
  'beatSaberLaunchArguments',
  'manageDisplayScale',
  'recordingDisplayScalePercent',
  'hideTaskbarDuringRun'
];

function readSetupAssistantHidden() {
  try {
    return localStorage.getItem('bsarr.setupAssistantHidden') === 'true';
  } catch {
    return false;
  }
}

function writeSetupAssistantHidden(hidden) {
  setupAssistantHidden = hidden;
  try {
    if (hidden) {
      localStorage.setItem('bsarr.setupAssistantHidden', 'true');
    } else {
      localStorage.removeItem('bsarr.setupAssistantHidden');
    }
  } catch {
    // Local storage is only a convenience; the assistant still works without it.
  }
}

async function loadState() {
  const response = await fetch('/api/state');
  if (!response.ok) throw new Error(await response.text());
  state = await response.json();
  render();
}

async function loadDisplayInfo() {
  const response = await fetch('/api/displays');
  if (!response.ok) throw new Error(await response.text());
  displayInfo = await response.json();
  renderMonitorOptions();
  render();
}

function render() {
  if (!state) return;

  isRendering = true;
  try {
  const settings = state.settings;
  const enabledInstances = getEnabledManagedInstances();
  const activeInstanceCount = enabledInstances.length || getVisibleManagedInstanceSlotCount();
  const activeWorkers = enabledInstances.filter(instance => instance.workerId).length;
  const pendingReplays = state.queue.filter(item => isPendingStatus(item.status)).length;
  const recordingReplays = state.queue.filter(item => isActiveStatus(item.status)).length;
  const completedReplays = state.queue.filter(item => sameStatus(item.status, 'Completed')).length;
  const failedReplays = state.queue.filter(item => sameStatus(item.status, 'Failed')).length;
  const runActive = Boolean(state.run?.isRunning || state.run?.cancellationRequested);
  document.body.classList.toggle('queueLoaded', state.queue.length > 0);
  updateActiveView();
  setHidden('startRun', runActive);
  setHidden('stopRun', !runActive);
  setHidden('forceStopGames', !runActive);

  document.getElementById('runPill').textContent = state.run.status;
  document.getElementById('runPill').className = `statusPill ${statusClass(state.run.status)}`;
  const runStatus = document.getElementById('runStatus');
  const runIssue = buildRunIssue();
  const runMeta = `${state.queue.length} replays | ${settings.captureWidth}x${settings.captureHeight}@${settings.targetFps} | ${formatBitrate(settings.videoBitrateKbps)} ${formatContainer(settings.outputFormat)} | ${formatMonitorSummary(settings.monitorIndex)} | ${settings.encoder} | ${formatAudioMode(settings)}`;
  runStatus.textContent = runIssue ? `${runIssue} | ${runMeta}` : runMeta;
  runStatus.classList.toggle('hasIssue', Boolean(runIssue) && hasRunIssue());
  renderCommandStatus(settings, {
    activeWorkers,
    pendingReplays,
    recordingReplays,
    completedReplays,
    failedReplays
  });
  document.getElementById('pendingCount').textContent = pendingReplays;
  document.getElementById('recordingCount').textContent = recordingReplays;
  document.getElementById('completedCount').textContent = completedReplays;
  document.getElementById('failedCount').textContent = failedReplays;
  const visibleInstanceCount = getVisibleManagedInstanceSlotCount();
  document.getElementById('workerCount').textContent = `${activeWorkers}/${activeInstanceCount}`;
  setText('pendingSub', `${state.queue.length} total`);
  setText('activeSub', `${activeWorkers} worker${activeWorkers === 1 ? '' : 's'}`);
  setText('completeSub', `${Math.round(completedReplays / Math.max(1, state.queue.length) * 100)}% done`);
  setText('failedSub', failedReplays ? 'needs review' : 'clear');
  setText('workerSub', `${activeInstanceCount} enabled, ${state.instances.length || visibleInstanceCount} configured`);
  setText('dockOutput', formatContainer(settings.outputFormat));
  setText('dockFps', settings.targetFps);
  setText('dockEncoder', settings.encoder);
  setText('dockAudio', formatAudioMode(settings));
  setText('dockBitrate', formatBitrate(settings.videoBitrateKbps));
  document.getElementById('lastUpdated').textContent = `Updated ${formatClock(new Date())}`;

  setValue('instanceCount', settings.instanceCount);
  setValue('recordingOutputDirectory', settings.recordingOutputDirectory);
  setValue('sharedCustomLevelsDirectory', settings.sharedCustomLevelsDirectory);
  setValue('sharedCustomWipLevelsDirectory', settings.sharedCustomWipLevelsDirectory);
  setValue('sharedCustomSabersDirectory', settings.sharedCustomSabersDirectory);
  setValue('sharedCustomNotesDirectory', settings.sharedCustomNotesDirectory);
  setValue('sharedCustomPlatformsDirectory', settings.sharedCustomPlatformsDirectory);
  setValue('sharedCustomAvatarsDirectory', settings.sharedCustomAvatarsDirectory);
  setValue('sharedCustomWallsDirectory', settings.sharedCustomWallsDirectory);
  setValue('sharedCustomBombsDirectory', settings.sharedCustomBombsDirectory);
  setValue('targetFps', settings.targetFps);
  setValue('captureWidth', settings.captureWidth);
  setValue('captureHeight', settings.captureHeight);
  setResolutionPreset(settings.captureWidth, settings.captureHeight);
  setValue('videoBitrateKbps', settings.videoBitrateKbps);
  setValue('outputFormat', settings.outputFormat);
  setValue('monitorIndex', settings.monitorIndex);
  renderMonitorOptions();
  setValue('encoder', settings.encoder);
  setValue('qualityMode', settings.qualityMode);
  setValue('audioMode', settings.audioMode);
  setValue('audioBitrateKbps', settings.audioBitrateKbps);
  setValue('audioSampleRate', settings.audioSampleRate);
  setValue('audioLevelMode', settings.audioLevelMode || 'Loudness');
  setValue('audioTargetLevelDb', settings.audioTargetLevelDb ?? -12);
  updateAudioLevelTargetConstraints();
  setValue('beatSaberInstancesRoot', settings.beatSaberInstancesRoot);
  setValue('beatSaberInstanceNamePrefix', settings.beatSaberInstanceNamePrefix);
  setValue('beatSaberLaunchPreset', resolveLaunchPreset(settings));
  setValue('beatSaberLaunchArguments', settings.beatSaberLaunchArguments);
  setValue('recordingDisplayScalePercent', settings.recordingDisplayScalePercent);
  setValue('restoreDisplayScalePercent', settings.restoreDisplayScalePercent);
  setText(
    'restoreDisplayScaleSummary',
    Number.isFinite(Number(settings.restoreDisplayScalePercent))
      ? `Auto-detect before recording (last ${settings.restoreDisplayScalePercent}%)`
      : 'Auto-detect before recording');
  document.getElementById('requireAllWorkersReady').checked = Boolean(settings.requireAllWorkersReady);
  document.getElementById('requireMatchingInstanceBaseline').checked = Boolean(settings.requireMatchingInstanceBaseline);
  document.getElementById('requireAudioForRun').checked = settings.requireAudioForRun !== false;
  document.getElementById('manageDisplayScale').checked = Boolean(settings.manageDisplayScale);
  document.getElementById('hideTaskbarDuringRun').checked = settings.hideTaskbarDuringRun !== false;
  updateDisplayScaleAvailability();
  renderGamePresentationSettings(settings);

  renderSetupAssistant();
  updateAdvancedSettingsToggle();
  renderWorkers();
  renderAttentionLog();
  renderBaseline();
  renderSyncSummary();
  renderDiagnostics();
  renderQueue();
  updateSettingsDirtyBadge();
  } finally {
    isRendering = false;
  }
}

function renderCommandStatus(settings, counts) {
  setText('captureProfile', buildCaptureProfile(settings));

  const configuredInstances = getEnabledManagedInstanceCount();
  const maps = summarizeMapAvailability(state.queue || []);
  const sync = summarizeSyncCorrection(state.queue || []);
  const baselineStatus = state.instanceBaseline?.status || 'Unchecked';
  const audioRequired = settings.requireAudioForRun !== false;
  const audioActive = audioRequired && String(settings.audioMode || '').toLowerCase() === 'processloopback';

  setText('mapsChip', `${maps.ready}/${maps.total}`);
  setText('mapsChipSub', maps.total ? (maps.missing ? `${maps.missing} missing` : 'available') : 'no queue');
  setStatusDot('mapsChipDot', maps.total && maps.missing ? 'warn' : 'good');

  setText('syncChip', sync.label);
  setText('syncChipSub', sync.detail);
  setStatusDot('syncChipDot', sync.kind);

  setText('baselineChip', baselineStatus);
  setText('baselineChipSub', state.instanceBaseline?.summary || 'instance check');
  setStatusDot('baselineChipDot', sameStatus(baselineStatus, 'Matched') ? 'good' : (sameStatus(baselineStatus, 'Unchecked') ? 'warn' : 'bad'));

  setText('audioChip', audioActive ? 'Process Loopback' : 'Audio off');
  setText('audioChipSub', audioRequired ? 'required for run' : 'not required');
  setStatusDot('audioChipDot', audioActive ? 'good' : 'warn');

  const disk = state.diskSpace || {};
  const diskStatus = disk.status || 'Unchecked';
  setText('diskChip', diskStatus);
  setText('diskChipSub', disk.summary || 'recording folder');
  setStatusDot('diskChipDot', sameStatus(diskStatus, 'Ready') ? 'good' : (sameStatus(diskStatus, 'Low') || sameStatus(diskStatus, 'Unavailable') ? 'bad' : 'warn'));

  setStatusDot('workerChipDot', counts.activeWorkers >= configuredInstances ? 'good' : 'warn');
  setStatusDot('failedChipDot', counts.failedReplays ? 'bad' : 'good');
  setStatusDot('queueChipDot', counts.pendingReplays ? 'warn' : 'good');
  setStatusDot('activeChipDot', counts.recordingReplays ? 'good' : (counts.activeWorkers ? 'warn' : 'good'));
  setStatusDot('completeChipDot', counts.completedReplays ? 'good' : 'warn');
}

function buildCaptureProfile(settings) {
  const preset = formatLaunchPresetLabel(resolveLaunchPreset(settings));
  return [
    preset,
    formatContainer(settings.outputFormat),
    formatEncoderLabel(settings.encoder),
    `${settings.targetFps || 0} FPS`,
    formatAudioMode(settings)
  ].filter(Boolean).join(' | ');
}

function formatLaunchPresetLabel(id) {
  switch (id) {
    case '4k-monitor-2x2':
      return 'Up to 4 x 1080p';
    case '1440p-monitor-2x2':
      return 'Up to 4 x 720p';
    case 'single-4k':
      return 'Single 4K';
    case 'single-1440p':
      return 'Single 1440p';
    case 'single-1080p':
      return 'Single 1080p';
    case 'windowed-1080p':
      return 'Windowed 1080p';
    case 'windowed-720p':
      return 'Windowed 720p';
    default:
      return 'Custom profile';
  }
}

function formatEncoderLabel(value) {
  const text = String(value || '').toLowerCase();
  if (text.includes('nvenc')) return text.includes('hevc') ? 'HEVC NVENC' : 'NVENC';
  if (text.includes('x264')) return 'CPU H.264';
  return value || 'Encoder';
}

function summarizeMapAvailability(queue) {
  const total = queue.length;
  let ready = 0;
  let missing = 0;
  for (const item of queue) {
    if (sameStatus(item.mapStatus, 'Found') || sameStatus(item.mapStatus, 'Downloaded')) {
      ready++;
    } else if (sameStatus(item.mapStatus, 'Missing')) {
      missing++;
    }
  }

  return { total, ready, missing };
}

function summarizeSyncCorrection(queue) {
  const completed = queue.filter(item => sameStatus(item.status, 'Completed'));
  const corrected = completed.filter(item => sameStatus(item.syncStatus, 'Corrected'));
  const offsets = queue
    .map(item => Number(item.syncCorrectionMilliseconds))
    .filter(value => Number.isFinite(value));
  const maxOffset = offsets.length ? Math.max(...offsets.map(value => Math.abs(value))) : 0;

  if (maxOffset >= 100) {
    return { kind: 'bad', label: 'Out of range', detail: `${formatSignedNumber(maxOffset.toFixed(1))} ms max` };
  }

  if (corrected.length) {
    return {
      kind: maxOffset >= 50 ? 'warn' : 'good',
      label: 'Active',
      detail: `${corrected.length}/${completed.length} corrected`
    };
  }

  return { kind: 'warn', label: 'Waiting', detail: 'marker correction' };
}

function setStatusDot(id, kind) {
  const element = document.getElementById(id);
  if (!element) return;
  element.className = `statusDot ${kind === 'bad' ? 'bad' : (kind === 'warn' ? 'warn' : 'good')}`;
}

function renderSongFolders() {
  const report = state.songFolders || {};
  const summary = document.getElementById('songFoldersSummary');
  const list = document.getElementById('songFoldersList');
  const status = report.status || 'Unchecked';
  const checked = report.checkedAtUtc ? `Checked ${formatClock(new Date(report.checkedAtUtc))}` : 'Not checked';
  summary.innerHTML = `
    <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
    <span class="baselineSummaryText">${escapeHtml(report.summary || 'Song folders have not been checked.')}</span>
    <span class="muted">${escapeHtml(checked)}</span>
  `;

  list.innerHTML = '';
  const links = report.links || [];
  if (!links.length) {
    const empty = document.createElement('div');
    empty.className = 'emptyState baselineEmpty';
    empty.textContent = 'No shared folder results';
    list.appendChild(empty);
    return;
  }

  for (const item of links) {
    const row = document.createElement('div');
    row.className = 'songFolderRow';
    row.innerHTML = `
      <div class="cellBlock">
        <span class="cellTitle">${escapeHtml(item.instanceName || `Instance ${Number(item.instanceIndex) + 1}`)} · ${escapeHtml(item.folderKind || 'Songs')}</span>
        <span class="cellMeta">${escapeHtml(item.instanceFolderPath || '')}</span>
      </div>
      <span class="badge ${statusClass(item.status)}">${escapeHtml(item.status)}</span>
      <div class="cellBlock">
        <span class="cellTitle">${escapeHtml(item.detail || '')}</span>
        <span class="cellMeta">${escapeHtml(item.sharedFolderPath || '')}</span>
      </div>
    `;
    list.appendChild(row);
  }
}

function renderGamePresentationSettings(settings) {
  const gamePresentation = settings.gamePresentation || {};
  const defaults = {
    noHud: true,
    loadPlayerEnvironment: false,
    loadPlayerJumpDistance: false,
    ignoreModifiers: false,
    showHead: false,
    showLeftSaber: true,
    showRightSaber: true,
    showWatermark: true,
    showTimelineMisses: true,
    showTimelineBombs: true,
    showTimelinePauses: true,
    sfxVolume: 0.3,
    noTextsAndHuds: true,
    advancedHud: false,
    reduceDebris: true,
    noFailEffects: false,
    saberTrailIntensity: 0,
    noteJumpDurationType: 'Dynamic',
    noteJumpFixedDuration: 0.2,
    noteJumpStartBeatOffset: 0,
    hideNoteSpawnEffect: false,
    adaptiveSfx: true,
    arcsHapticFeedback: true,
    arcVisibility: 'Low',
    environmentEffectsFilterDefaultPreset: 'AllEffects',
    environmentEffectsFilterExpertPlusPreset: 'AllEffects',
    headsetHapticIntensity: 0.7
  };

  const booleanFields = [
    'noHud',
    'loadPlayerEnvironment',
    'loadPlayerJumpDistance',
    'ignoreModifiers',
    'showHead',
    'showLeftSaber',
    'showRightSaber',
    'showWatermark',
    'showTimelineMisses',
    'showTimelineBombs',
    'showTimelinePauses',
    'noTextsAndHuds',
    'advancedHud',
    'reduceDebris',
    'noFailEffects',
    'hideNoteSpawnEffect',
    'adaptiveSfx',
    'arcsHapticFeedback'
  ];
  for (const fieldId of booleanFields) {
    const element = document.getElementById(fieldId);
    if (element) element.checked = gamePresentation[fieldId] ?? defaults[fieldId];
  }
  updateAdvancedHudAvailability();

  for (const fieldId of [
    'sfxVolume',
    'saberTrailIntensity',
    'noteJumpFixedDuration',
    'noteJumpStartBeatOffset',
    'headsetHapticIntensity'
  ]) {
    const value = Number(gamePresentation[fieldId] ?? defaults[fieldId]);
    setValue(fieldId, Number.isFinite(value) ? value : defaults[fieldId]);
    updateGameValueLabel(fieldId);
  }

  for (const fieldId of [
    'noteJumpDurationType',
    'arcVisibility',
    'environmentEffectsFilterDefaultPreset',
    'environmentEffectsFilterExpertPlusPreset'
  ]) {
    setValue(fieldId, gamePresentation[fieldId] || defaults[fieldId]);
  }
  updateNoteJumpDurationAvailability();

  const version = document.getElementById('gamePresentationVersion');
  if (version) {
    version.textContent = `v${Number(settings.gamePresentationSettingsVersion) || 1}`;
  }
}

function updateGameValueLabel(fieldId) {
  const input = document.getElementById(fieldId);
  const output = document.getElementById(`${fieldId}Value`);
  if (!input || !output) return;

  const value = Number(input.value);
  if (!Number.isFinite(value)) {
    output.textContent = '';
    return;
  }

  const format = input.dataset.gameValue || '';
  if (format === 'percent') {
    output.textContent = `${Math.round(value * 100)}%`;
  } else if (format === 'seconds') {
    output.textContent = `${value.toFixed(2)}s`;
  } else if (format === 'offset') {
    output.textContent = value.toFixed(1);
  } else {
    output.textContent = String(value);
  }
}

function updateAdvancedHudAvailability() {
  const noTextsAndHuds = document.getElementById('noTextsAndHuds');
  const advancedHud = document.getElementById('advancedHud');
  if (!noTextsAndHuds || !advancedHud) return;

  const disabled = noTextsAndHuds.checked;
  if (disabled) {
    advancedHud.checked = false;
  }

  advancedHud.disabled = disabled;
  advancedHud.closest('.gameToggle')?.classList.toggle('isDisabled', disabled);
}

function updateDisplayScaleAvailability() {
  const enabled = Boolean(document.getElementById('manageDisplayScale')?.checked);
  const fields = document.getElementById('displayScaleFields');
  fields?.classList.toggle('isDisabled', !enabled);
  document.getElementById('recordingDisplayScalePercent')?.toggleAttribute('disabled', !enabled);
}

function updateNoteJumpDurationAvailability() {
  const mode = document.getElementById('noteJumpDurationType');
  const fixedDuration = document.getElementById('noteJumpFixedDuration');
  const row = document.getElementById('noteJumpFixedDurationRow');
  if (!mode || !fixedDuration || !row) return;

  const disabled = !sameStatus(mode.value, 'Static');
  fixedDuration.disabled = disabled;
  row.classList.toggle('isDisabled', disabled);
}

function updateAudioLevelTargetConstraints() {
  const target = document.getElementById('audioTargetLevelDb');
  if (!target) return;

  const isLoudness = sameStatus(getText('audioLevelMode') || 'Loudness', 'Loudness');
  target.min = isLoudness ? '-70' : '-60';
  target.max = isLoudness ? '-5' : '0';
  target.step = '0.5';

  const value = Number(target.value);
  if (!Number.isFinite(value)) return;

  const min = Number(target.min);
  const max = Number(target.max);
  if (value < min) target.value = String(min);
  if (value > max) target.value = String(max);
}

function renderWorkers() {
  const instances = document.getElementById('instances');
  if (!instances) return;
  instances.innerHTML = '';

  const slots = buildManagedInstanceSlots();
  renderActiveInstanceControls(slots);
  if (!slots.length) {
    const empty = document.createElement('div');
    empty.className = 'emptyState';
    empty.textContent = 'No workers configured';
    instances.appendChild(empty);
    return;
  }

  for (const instance of slots) {
    const reservedSlot = Boolean(instance.reservedSlot);
    const enabled = isInstanceEnabled(instance);
    const gameRunning = Boolean(instance.workerId || instance.gameProcessId);
    const assignment = enabled ? findInstanceAssignment(instance) : null;
    const heartbeat = reservedSlot ? 'Not configured' : formatHeartbeat(instance.lastHeartbeatUtc);
    const gameState = formatInstanceGameState(instance, assignment);
    const workerState = reservedSlot ? 'Reserved' : (!enabled ? 'Disabled' : (instance.workerId ? (instance.status || 'Ready') : 'Waiting'));
    const audioState = formatInstanceAudioMode(enabled && !reservedSlot);
    const assignmentTitle = assignment
      ? (assignment.songName || assignment.fileName || `Replay #${assignment.sequenceNumber}`)
      : !enabled
      ? 'Scheduling disabled'
      : 'No assignment';
    const assignmentMeta = assignment
      ? `${escapeHtml(assignment.status || 'Queued')} | ${formatSeconds(assignment.estimatedSeconds)}`
      : !enabled
      ? 'Use + to include this lane'
      : (reservedSlot ? 'Slot available when enabled' : 'Idle');
    const rowActionLabel = gameRunning ? 'Quit' : 'Launch';
    const rowActionClass = gameRunning ? 'dangerText' : '';
    const rowActionAttribute = gameRunning ? 'data-quit-index' : 'data-launch-index';
    const rowActionDisabled = reservedSlot || (!enabled && !gameRunning);
    const row = document.createElement('div');
    row.className = `workerRow instanceLane ${reservedSlot ? 'reservedSlot' : ''} ${!enabled ? 'disabledInstance' : ''} ${instance.gameLaunchError || instance.audioRoutingError ? 'hasError' : ''}`;
    row.innerHTML = `
      <div class="instanceIdentity">
        <span class="instanceDot ${enabled && (instance.workerId || instance.gameProcessId) ? 'online' : 'idle'}" aria-hidden="true"></span>
        <span class="monitorIcon" aria-hidden="true"></span>
        <div>
          <strong>${escapeHtml(instance.name || `BSARR I-${Number(instance.index) + 1}`)}</strong>
          <span>${escapeHtml(!enabled ? 'Disabled for scheduling' : (instance.recorderHostUrl || (reservedSlot ? 'Managed slot reserved' : 'Recorder host pending')))}</span>
        </div>
      </div>
      <div class="instanceMetric"><span>Heartbeat</span><strong>${escapeHtml(heartbeat)}</strong></div>
      <div class="instanceMetric"><span>Game state</span><strong>${escapeHtml(gameState)}</strong></div>
      <div class="instanceMetric"><span>Worker</span><strong>${escapeHtml(workerState)}</strong></div>
      <div class="instanceMetric"><span>Audio</span><strong>${escapeHtml(audioState)}</strong></div>
      <div class="assignmentPreview ${assignment ? '' : 'isIdle'}">
        ${assignment ? renderPlanCover(assignment) : ''}
        <div>
          <strong>${escapeHtml(assignmentTitle)}</strong>
          <span>${assignmentMeta}</span>
        </div>
      </div>
      <button class="textButton rowAction ${rowActionClass}" type="button" ${rowActionAttribute}="${instance.index}"${disabledAttr(rowActionDisabled)}>${rowActionLabel}</button>
    `;
    const launchButton = row.querySelector('[data-launch-index]');
    if (launchButton) {
      launchButton.addEventListener('click', () => launchInstance(instance.index));
    }

    const quitButton = row.querySelector('[data-quit-index]');
    if (quitButton) {
      quitButton.addEventListener('click', () => quitInstance(instance.index));
    }
    instances.appendChild(row);
  }
}

function renderActiveInstanceControls(slots = buildManagedInstanceSlots()) {
  const enabled = getEnabledManagedInstances();
  const configuredCount = Math.max(
    minManagedInstanceCount,
    state?.instances?.length || slots.filter(slot => !slot.reservedSlot).length || slots.length || 0);
  const text = document.getElementById('activeInstanceCount');
  if (text) {
    text.textContent = `${enabled.length}/${configuredCount} enabled`;
  }

  const decrease = document.getElementById('decreaseActiveInstances');
  const increase = document.getElementById('increaseActiveInstances');
  if (decrease) {
    decrease.disabled = enabled.length <= 1;
  }

  if (increase) {
    increase.disabled = !getNextDisabledInstance();
  }
}

function buildManagedInstanceSlots() {
  const count = getVisibleManagedInstanceSlotCount();
  const byIndex = new Map((state.instances || []).map(instance => [Number(instance.index), instance]));
  const slots = [];
  for (let index = 0; index < count; index++) {
    slots.push(byIndex.get(index) || {
      index,
      name: `BSARR I-${index + 1}`,
      reservedSlot: true
    });
  }

  return slots;
}

function getVisibleManagedInstanceSlotCount(queue = state?.queue || []) {
  return Math.min(
    visibleManagedInstanceSlots,
    Math.max(
      minManagedInstanceCount,
      visibleManagedInstanceSlots,
      state?.instances?.length || 0,
      getMaxAssignedInstanceSlot(queue)));
}

function getMaxAssignedInstanceSlot(queue) {
  return (queue || []).reduce((max, item) => {
    const assigned = Number(item.assignedInstance);
    return Number.isFinite(assigned) ? Math.max(max, assigned + 1) : max;
  }, 0);
}

function isInstanceEnabled(instance) {
  return Boolean(instance) && !instance.reservedSlot && instance.enabled !== false;
}

function getEnabledManagedInstances() {
  return (state?.instances || [])
    .filter(isInstanceEnabled)
    .sort((a, b) => Number(a.index) - Number(b.index));
}

function getEnabledManagedInstanceCount() {
  return Math.max(1, getEnabledManagedInstances().length || getVisibleManagedInstanceSlotCount());
}

function getHighestEnabledInstance() {
  const enabled = getEnabledManagedInstances();
  return enabled.length ? enabled[enabled.length - 1] : null;
}

function getNextDisabledInstance() {
  return (state?.instances || [])
    .filter(instance => !instance.reservedSlot && instance.enabled === false)
    .sort((a, b) => Number(a.index) - Number(b.index))[0] || null;
}

function getEnabledPlanLaneIndexes() {
  const indexes = getEnabledManagedInstances()
    .map(instance => Number(instance.index))
    .filter(index => Number.isFinite(index));
  return indexes.length ? indexes : [0];
}

function findInstanceAssignment(instance) {
  const index = Number(instance.index);
  const queue = state.queue || [];
  if (instance.currentReplayId) {
    const current = queue.find(item => item.id === instance.currentReplayId);
    if (current) return current;
  }

  return queue.find(item => Number(item.assignedInstance) === index && isActiveStatus(item.status)) ||
    queue.find((item, itemIndex) =>
      sameStatus(item.status, 'Queued') &&
      resolvePlanLaneIndex(item, itemIndex, getVisibleManagedInstanceSlotCount(queue)) === index) ||
    queue.find(item => Number(item.assignedInstance) === index) ||
    null;
}

function formatInstanceGameState(instance, assignment) {
  if (instance.reservedSlot) return assignment ? 'Awaiting instance' : 'Available';
  if (instance.gameLaunchError) return 'Launch failed';
  if (assignment && isActiveStatus(assignment.status)) return 'Recording';
  if (instance.gameProcessId) return 'In Menu';
  return sameStatus(instance.gameLaunchStatus, 'Idle') || !instance.gameLaunchStatus
    ? 'Off'
    : instance.gameLaunchStatus;
}

function renderBaseline() {
  const report = state.instanceBaseline || {};
  const summary = document.getElementById('baselineSummary');
  const instances = document.getElementById('baselineInstances');
  const status = report.status || 'Unchecked';
  const checked = report.checkedAtUtc ? `Checked ${formatClock(new Date(report.checkedAtUtc))}` : 'Not checked';
  summary.innerHTML = `
    <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
    <span class="baselineSummaryText">${escapeHtml(report.summary || 'Baseline has not been checked.')}</span>
    <span class="muted">${escapeHtml(checked)}</span>
  `;
  instances.innerHTML = '';

  const rows = report.instances || [];
  if (!rows.length) {
    const empty = document.createElement('div');
    empty.className = 'emptyState baselineEmpty';
    empty.textContent = 'No baseline results';
    instances.appendChild(empty);
    return;
  }

  for (const item of rows) {
    const issueCount = (item.issues || []).length;
    const issueText = issueCount
      ? item.issues.slice(0, 3).join(' ')
      : `${item.checkedFileCount || 0} files checked`;
    const suffix = issueCount > 3 ? ` +${issueCount - 3} more` : '';
    const row = document.createElement('div');
    row.className = 'baselineRow';
    row.innerHTML = `
      <div class="cellBlock">
        <span class="cellTitle">${escapeHtml(item.name || `Instance ${Number(item.index) + 1}`)}</span>
        <span class="cellMeta">${escapeHtml(item.directory || 'No instance folder')}</span>
      </div>
      <span class="badge ${statusClass(item.status)}">${escapeHtml(item.status)}</span>
      <span class="baselineIssue">${escapeHtml(issueText + suffix)}</span>
    `;
    instances.appendChild(row);
  }
}

function renderSyncSummary() {
  const summary = document.getElementById('syncSummary');
  if (!summary) return;

  const completed = (state.queue || []).filter(item => sameStatus(item.status, 'Completed'));
  const corrected = completed.filter(item => sameStatus(item.syncStatus, 'Corrected'));
  const failed = (state.queue || []).filter(item => item.error && /sync|marker/i.test(item.error));
  const latest = corrected
    .slice()
    .sort((left, right) => String(right.completedAtUtc || '').localeCompare(String(left.completedAtUtc || '')))[0];
  const status = failed.length ? 'Failed' : (completed.length ? 'Ready' : 'Waiting');
  const detail = latest
    ? `Last correction ${formatSignedNumber(Number(latest.syncCorrectionMilliseconds || 0).toFixed(1))} ms, trim ${formatNumber(latest.trimStartSeconds)}s`
    : 'Completed replays will show marker correction metadata here.';

  summary.innerHTML = `
    <div class="syncHeader">
      <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
      <span class="cellTitle">${escapeHtml(detail)}</span>
      <span class="muted">${corrected.length}/${completed.length} corrected</span>
    </div>
    ${failed.length ? `<div class="errorText">${escapeHtml(`${failed.length} replay${failed.length === 1 ? '' : 's'} failed sync detection`)}</div>` : ''}
  `;
}

function renderDiagnostics() {
  if (!state) return;

  const settings = buildCurrentSettingsPreview();
  renderDiagnosticReadiness(settings);
  renderDiagnosticRuntime(settings);
  renderDiagnosticWorkers();
  renderDiagnosticEvents();
}

function renderDiagnosticReadiness(settings) {
  const summary = document.getElementById('diagnosticsReadinessSummary');
  const list = document.getElementById('diagnosticsReadinessList');
  if (!summary || !list) return;

  const items = buildDiagnosticsChecklist(settings);
  const failed = items.filter(item => diagnosticSeverity(item) === 'bad');
  const waiting = items.filter(item => diagnosticSeverity(item) === 'warn');
  const readyCount = items.length - failed.length - waiting.length;
  const overallStatus = failed.length ? 'Blocked' : (waiting.length ? 'Needs check' : 'Ready');
  const overallDetail = failed.length
    ? `${failed.length} blocker${failed.length === 1 ? '' : 's'} before a clean run`
    : (waiting.length ? `${waiting.length} check${waiting.length === 1 ? '' : 's'} still waiting` : 'All live checks are ready');

  summary.innerHTML = `
    <div class="diagnosticHeroState ${diagnosticSeverity({ status: overallStatus })}">
      <span class="badge ${statusClass(overallStatus)}">${escapeHtml(overallStatus)}</span>
      <div>
        <strong>${escapeHtml(overallDetail)}</strong>
        <span>${readyCount}/${items.length} checks ready</span>
      </div>
    </div>
    <div class="diagnosticHeroMeta">
      <span>${escapeHtml(formatLaunchPresetLabel(resolveLaunchPreset(settings)))}</span>
      <span>${escapeHtml(`${settings.captureWidth || 0}x${settings.captureHeight || 0}@${settings.targetFps || 0}`)}</span>
      <span>${escapeHtml(formatAudioMode(settings))}</span>
    </div>
  `;

  list.innerHTML = items.map(item => `
    <div class="diagnosticCheckItem ${diagnosticSeverity(item)}">
      <span class="diagnosticStateDot" aria-hidden="true"></span>
      <span class="badge ${statusClass(item.status)}">${escapeHtml(item.status)}</span>
      <div class="cellBlock">
        <span class="cellTitle">${escapeHtml(item.label)}</span>
        <span class="cellMeta">${escapeHtml(item.detail)}</span>
      </div>
    </div>
  `).join('');
}

function buildDiagnosticsChecklist(settings) {
  const queue = state.queue || [];
  const failedReplays = queue.filter(item => sameStatus(item.status, 'Failed') || item.error);
  const maps = summarizeMapAvailability(queue);
  const sync = summarizeSyncCorrection(queue);
  const disk = state.diskSpace || {};
  const run = state.run || {};
  const runStatus = run.status || 'Idle';
  const runDetail = run.cancellationReason
    || `${queue.length} replay${queue.length === 1 ? '' : 's'}, ${failedReplays.length} failed`;

  return [
    {
      label: 'Run state',
      status: runStatus,
      detail: runDetail
    },
    ...buildSetupWizardChecklist(settings),
    {
      label: 'Queue maps',
      status: !maps.total ? 'Waiting' : (maps.missing ? 'Missing' : (maps.ready === maps.total ? 'Ready' : 'Check')),
      detail: maps.total
        ? `${maps.ready}/${maps.total} available${maps.missing ? `, ${maps.missing} missing` : ''}`
        : 'Import replays to check map availability.'
    },
    {
      label: 'Recording disk',
      status: disk.status || 'Unchecked',
      detail: disk.summary || 'Disk space has not been checked.'
    },
    {
      label: 'Sync correction',
      status: sync.kind === 'bad' ? 'Failed' : (sync.kind === 'good' ? 'Ready' : 'Waiting'),
      detail: `${sync.label}: ${sync.detail}`
    }
  ];
}

function renderDiagnosticRuntime(settings) {
  const grid = document.getElementById('diagnosticsRuntimeGrid');
  if (!grid) return;

  const queue = state.queue || [];
  const instances = state.instances || [];
  const enabled = getEnabledManagedInstances();
  const enabledCount = enabled.length || getEnabledManagedInstanceCount();
  const activeWorkers = enabled.filter(instance => instance.workerId).length;
  const processCount = enabled.filter(instance => instance.gameProcessId).length;
  const hostCount = enabled.filter(instance => instance.recorderHostUrl).length;
  const maps = summarizeMapAvailability(queue);
  const disk = state.diskSpace || {};
  const run = state.run || {};
  const completed = queue.filter(item => sameStatus(item.status, 'Completed')).length;
  const failed = queue.filter(item => sameStatus(item.status, 'Failed') || item.error).length;
  const pending = queue.filter(item => isPendingStatus(item.status)).length;

  const rows = [
    {
      label: 'Control API',
      value: 'Online',
      status: 'Ready',
      detail: 'Polling /api/state'
    },
    {
      label: 'Run',
      value: run.status || 'Idle',
      status: run.status || 'Idle',
      detail: run.startedAtUtc ? `Started ${formatEventTime(run.startedAtUtc)}` : 'No active run'
    },
    {
      label: 'Queue',
      value: `${queue.length} replay${queue.length === 1 ? '' : 's'}`,
      status: failed ? 'Failed' : (queue.length ? 'Ready' : 'Waiting'),
      detail: `${pending} pending, ${completed} complete, ${failed} failed`
    },
    {
      label: 'Workers',
      value: `${activeWorkers}/${enabledCount} online`,
      status: activeWorkers >= enabledCount ? 'Ready' : 'Waiting',
      detail: `${processCount}/${enabledCount} Beat Saber process ids known`
    },
    {
      label: 'Recorder hosts',
      value: `${hostCount}/${enabledCount} linked`,
      status: hostCount >= enabledCount ? 'Ready' : 'Waiting',
      detail: instances.length ? `${instances.length} configured instance${instances.length === 1 ? '' : 's'}` : 'No instances reported'
    },
    {
      label: 'Output disk',
      value: disk.status || 'Unchecked',
      status: disk.status || 'Unchecked',
      detail: disk.summary || shortPath(disk.path || settings.recordingOutputDirectory || '')
    },
    {
      label: 'Capture',
      value: `${settings.captureWidth || 0}x${settings.captureHeight || 0}`,
      status: 'Ready',
      detail: `${settings.targetFps || 0} FPS, ${formatBitrate(settings.videoBitrateKbps)}, ${formatEncoderLabel(settings.encoder)}`
    },
    {
      label: 'Maps',
      value: maps.total ? `${maps.ready}/${maps.total}` : 'No queue',
      status: !maps.total ? 'Waiting' : (maps.missing ? 'Missing' : 'Ready'),
      detail: maps.missing ? `${maps.missing} missing maps` : 'Map availability clear'
    }
  ];

  grid.innerHTML = rows.map(row => `
    <div class="diagnosticMetricTile ${diagnosticSeverity({ status: row.status })}">
      <span>${escapeHtml(row.label)}</span>
      <strong>${escapeHtml(row.value)}</strong>
      <small>${escapeHtml(row.detail)}</small>
    </div>
  `).join('');
}

function renderDiagnosticWorkers() {
  const list = document.getElementById('diagnosticsWorkerList');
  if (!list) return;

  const slots = buildManagedInstanceSlots();
  if (!slots.length) {
    list.innerHTML = '<div class="emptyState">No workers configured</div>';
    return;
  }

  list.innerHTML = slots.map(instance => {
    const reservedSlot = Boolean(instance.reservedSlot);
    const enabled = isInstanceEnabled(instance);
    const assignment = enabled ? findInstanceAssignment(instance) : null;
    const status = reservedSlot ? 'Reserved' : (!enabled ? 'Disabled' : (instance.workerId ? (instance.status || 'Ready') : 'Waiting'));
    const audioState = reservedSlot
      ? 'Pending'
      : !enabled
      ? 'Off'
      : instance.audioRoutingError
      ? 'Failed'
      : (instance.audioRoutingStatus || (state.settings?.requireAudioForRun === false ? 'Off' : 'Waiting'));
    const assignmentTitle = assignment
      ? (assignment.songName || assignment.fileName || `Replay #${assignment.sequenceNumber}`)
      : (!enabled ? 'Scheduling disabled' : 'No assignment');
    const host = instance.recorderHostUrl || (reservedSlot ? 'Managed slot reserved' : 'Recorder host pending');
    const severity = diagnosticSeverity({ status: instance.gameLaunchError || instance.audioRoutingError ? 'Failed' : status });

    return `
      <div class="diagnosticWorkerRow ${severity} ${!enabled ? 'isDisabled' : ''}">
        <div class="diagnosticWorkerIdentity">
          <span class="diagnosticStateDot" aria-hidden="true"></span>
          <div>
            <strong>${escapeHtml(instance.name || `BSARR I-${Number(instance.index) + 1}`)}</strong>
            <span>${escapeHtml(host)}</span>
          </div>
        </div>
        <div class="diagnosticWorkerMetric"><span>Status</span><strong>${escapeHtml(status)}</strong></div>
        <div class="diagnosticWorkerMetric"><span>Heartbeat</span><strong>${escapeHtml(reservedSlot ? 'Not configured' : formatHeartbeat(instance.lastHeartbeatUtc))}</strong></div>
        <div class="diagnosticWorkerMetric"><span>Game</span><strong>${escapeHtml(formatInstanceGameState(instance, assignment))}</strong></div>
        <div class="diagnosticWorkerMetric"><span>Audio</span><strong>${escapeHtml(audioState)}</strong></div>
        <div class="diagnosticWorkerAssignment">
          <span>${escapeHtml(assignment ? (assignment.status || 'Queued') : 'Idle')}</span>
          <strong>${escapeHtml(assignmentTitle)}</strong>
        </div>
      </div>
    `;
  }).join('');
}

function renderDiagnosticEvents() {
  const log = document.getElementById('diagnosticsEventLog');
  if (!log) return;

  const events = collectAttentionEvents();
  if (!events.length) {
    log.innerHTML = '<div class="emptyState">No diagnostic events yet</div>';
    return;
  }

  log.innerHTML = events.slice(0, 12).map(event => `
    <div class="attentionItem ${event.kind}">
      <span class="attentionDot" aria-hidden="true"></span>
      <time>${escapeHtml(formatEventTime(event.time))}</time>
      <strong>${escapeHtml(event.text)}</strong>
      <span>${escapeHtml(event.tag)}</span>
    </div>
  `).join('');
}

function diagnosticSeverity(item) {
  const status = String(item?.status ?? '').toLowerCase();
  if (/blocked|fail|error|missing|mismatch|out of range|low|unavailable|wrong/.test(status)) return 'bad';
  if (/ready|matched|clean|import|off|idle|running|recording|started|online|ok|completed|linked|disabled|reserved/.test(status)) return 'good';
  return 'warn';
}

function renderAttentionLog() {
  const log = document.getElementById('attentionLog');
  if (!log) return;

  const events = collectAttentionEvents();

  if (!events.length) {
    log.innerHTML = '<div class="emptyState">No attention items yet</div>';
    return;
  }

  log.innerHTML = events.slice(0, 8).map(event => `
    <div class="attentionItem ${event.kind}">
      <span class="attentionDot" aria-hidden="true"></span>
      <time>${escapeHtml(formatEventTime(event.time))}</time>
      <strong>${escapeHtml(event.text)}</strong>
      <span>${escapeHtml(event.tag)}</span>
    </div>
  `).join('');
}

function collectAttentionEvents() {
  let events = (state.events || []).map(event => ({
    kind: eventKindClass(event.kind),
    tag: event.tag || 'Event',
    time: event.createdAtUtc,
    text: event.text || 'Event recorded'
  }));

  if (!events.length) {
    events = buildDerivedAttentionEvents();
  }

  events.sort((left, right) => new Date(right.time || 0) - new Date(left.time || 0));
  return events;
}

function buildDerivedAttentionEvents() {
  const events = [];
  for (const item of state.queue || []) {
    const label = item.songName || item.fileName || `Replay #${item.sequenceNumber}`;
    if (item.error) {
      events.push({
        kind: 'bad',
        tag: 'Error',
        time: item.completedAtUtc || item.assignedAtUtc,
        text: `${label}: ${item.error}`
      });
    }
    if (sameStatus(item.mapStatus, 'Missing')) {
      events.push({
        kind: 'bad',
        tag: 'Map',
        time: item.assignedAtUtc,
        text: `Missing map: ${label}`
      });
    }
    if (isSyncOutOfRange(item)) {
      events.push({
        kind: 'bad',
        tag: 'Sync',
        time: item.completedAtUtc,
        text: `Sync out of range: ${label} ${formatPlanSync(item)}`
      });
    }
    if (isActiveStatus(item.status)) {
      events.push({
        kind: 'info',
        tag: 'Run',
        time: item.assignedAtUtc,
        text: `Recording started: ${label} on I-${assignedInstanceText(item)}`
      });
    }
    if (sameStatus(item.status, 'Completed')) {
      events.push({
        kind: 'good',
        tag: 'Output',
        time: item.completedAtUtc,
        text: `Recording complete: ${label}`
      });
    }
  }

  for (const instance of state.instances || []) {
    if (instance.gameLaunchError || instance.audioRoutingError) {
      events.push({
        kind: 'bad',
        tag: 'Instance',
        time: instance.gameLaunchedAtUtc,
        text: `${instance.name}: ${instance.gameLaunchError || instance.audioRoutingError}`
      });
    }
  }

  return events;
}

function eventKindClass(kind) {
  const normalized = String(kind || '').toLowerCase();
  if (normalized === 'good' || normalized === 'success') return 'good';
  if (normalized === 'bad' || normalized === 'error') return 'bad';
  if (normalized === 'warn' || normalized === 'warning') return 'bad';
  return 'info';
}

function formatEventTime(value) {
  if (!value) return '--:--';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '--:--';
  return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

function renderQueue() {
  const queue = state.queue || [];
  const visibleQueue = queue.filter(matchesQueueSearch);
  const hasSearch = Boolean(normalizedQueueSearch());
  const schedule = visibleQueue.length ? buildRunPlanSchedule(visibleQueue) : null;
  setHidden('clearQueue', queue.length === 0);
  document.getElementById('queueSummary').textContent = queue.length
    ? `${visibleQueue.length} shown / ${queue.length} total | est ${formatRulerTime(schedule?.totalSeconds || 0)} | ${state.run.completedCount} done / ${state.run.failedCount} failed`
    : 'No replays imported';
  const rows = document.getElementById('queueRows');
  rows.innerHTML = '';
  syncQueueSelection(queue, visibleQueue);

  if (!visibleQueue.length) {
    if (queue.length) {
      const empty = document.createElement('div');
      empty.className = 'emptyState queueEmpty';
      empty.textContent = hasSearch ? 'No replays match this search' : 'No replays available';
      rows.appendChild(empty);
    } else {
      rows.appendChild(createEmptyQueueImportTarget());
    }
    renderQueueDetails();
    return;
  }

  const board = document.createElement('div');
  board.className = 'runPlanBoard';
  board.innerHTML = `
    <aside class="runPlanTimeline">
      <div class="timelineHeader">Order</div>
      <div class="timelineList">
        ${visibleQueue.map(item => renderTimelineItem(item)).join('')}
      </div>
    </aside>
    <section class="runPlanSchedule">
      ${renderTimeRuler(schedule.totalSeconds)}
      <div class="laneStack">
        ${schedule.lanes.map(lane => renderRunPlanLane(lane, visibleQueue, schedule.totalSeconds)).join('')}
      </div>
      ${renderRunPlanPlayhead(schedule.totalSeconds)}
      <div class="runPlanLegend">
        <span><i class="legendBox queued"></i>Queued</span>
        <span><i class="legendBox recording"></i>Recording</span>
        <span><i class="legendBox complete"></i>Complete</span>
        <span><i class="legendDot good"></i>Map OK</span>
        <span><i class="legendDot warn"></i>Map missing</span>
      </div>
    </section>
  `;

  bindRunPlanActions(board);
  bindRunPlanDrag(board);
  rows.appendChild(board);
  updateRunPlanPlayhead({ animate: false });

  renderQueueDetails();
}

function createEmptyQueueImportTarget() {
  const target = document.createElement('button');
  target.className = 'emptyState queueEmpty queueImportDrop';
  target.type = 'button';
  target.setAttribute('aria-label', 'Import .bsor replay files');
  target.innerHTML = `
    <span class="queueImportIcon" aria-hidden="true">&#8595;</span>
    <strong>Drop .bsor replays here</strong>
    <span>or click to choose replay files</span>
    <small>Imported replays will appear in the Replay Queue.</small>
  `;
  target.addEventListener('click', openQueueReplayPicker);
  bindQueueImportDropTarget(target);
  return target;
}

function buildRunPlanSchedule(visibleQueue) {
  const configuredCount = getVisibleManagedInstanceSlotCount(visibleQueue);
  const lanes = [];
  for (let index = 0; index < configuredCount; index++) {
    lanes.push({
      index,
      instance: state.instances.find(item => Number(item.index) === index),
      items: [],
      totalSeconds: 0
    });
  }

  for (const [itemIndex, item] of visibleQueue.entries()) {
    const laneIndex = resolvePlanLaneIndex(item, itemIndex, configuredCount);
    const lane = lanes[laneIndex] || lanes[0];
    const durationSeconds = getPlanDurationSeconds(item);
    const startLeadInSeconds = getPlanStartLeadInSeconds();
    const scheduled = {
      item,
      startSeconds: lane.totalSeconds,
      durationSeconds,
      startLeadInSeconds,
      endSeconds: lane.totalSeconds + durationSeconds,
      gapAfterSeconds: getPlanInterReplayGapSeconds()
    };
    lane.items.push(scheduled);
    lane.totalSeconds = scheduled.endSeconds + scheduled.gapAfterSeconds;
  }

  for (const lane of lanes) {
    const lastItem = lane.items[lane.items.length - 1];
    if (!lastItem) continue;
    lastItem.gapAfterSeconds = 0;
    lane.totalSeconds = lastItem.endSeconds;
  }

  return {
    lanes,
    totalSeconds: Math.max(1, ...lanes.map(lane => lane.totalSeconds))
  };
}

function resolvePlanLaneIndex(item, itemIndex, laneCount) {
  const assigned = Number(item.assignedInstance);
  if (Number.isFinite(assigned) && assigned >= 0 && assigned < laneCount) return assigned;

  if (sameStatus(item.status, 'Queued')) {
    const enabledIndexes = getEnabledPlanLaneIndexes().filter(index => index >= 0 && index < laneCount);
    return enabledIndexes[itemIndex % Math.max(1, enabledIndexes.length)] ?? 0;
  }

  return itemIndex % Math.max(1, laneCount);
}

function getPlanDurationSeconds(item) {
  return getPlanStartLeadInSeconds() + getPlanPlaybackSeconds(item) + getPlanFixedOverheadSeconds();
}

function getPlanPlaybackSeconds(item) {
  const seconds = Number(item?.estimatedSeconds);
  return Number.isFinite(seconds) && seconds > 0 ? seconds : 60;
}

function getPlanFixedOverheadSeconds() {
  return runPlanTimingDefaults.recorderStartupSeconds +
    runPlanTimingDefaults.syncMarkerSeconds +
    runPlanTimingDefaults.recorderFinalizationSeconds;
}

function getPlanStartLeadInSeconds() {
  return runPlanTimingDefaults.startLeadInSeconds;
}

function getPlanInterReplayGapSeconds() {
  return getConfiguredInterReplayGapSeconds();
}

function getConfiguredInterReplayGapSeconds() {
  const configured = Number(state?.settings?.delayBetweenRecordingsSeconds);
  const seconds = Number.isFinite(configured)
    ? configured
    : runPlanTimingDefaults.interReplayGapSeconds;
  return Math.max(0, Math.min(30, seconds));
}

function renderTimeRuler(totalSeconds) {
  return `
    <div class="timeRuler" aria-label="Estimated queue wall-clock time" data-run-plan-total-seconds="${Number(totalSeconds).toFixed(3)}">
      <div class="timeRulerTrack" data-run-plan-ruler-track>
        ${buildTimeRulerTicks(totalSeconds).map(tick => `
          <span style="left: ${formatPercent(tick.position)}">${escapeHtml(tick.label)}</span>
        `).join('')}
      </div>
    </div>
  `;
}

function renderRunPlanPlayhead(totalSeconds) {
  const initialLeft = Number.isFinite(lastRunPlanPlayheadLeftPx)
    ? Math.max(0, Math.round(lastRunPlanPlayheadLeftPx))
    : 0;
  return `
    <div class="runPlanPlayhead instant" data-run-plan-playhead data-total-seconds="${Number(totalSeconds).toFixed(3)}" aria-hidden="true" style="left: ${initialLeft}px">
      <span class="playheadTime" data-playhead-time>0:00</span>
      <span class="playheadLine"></span>
    </div>
  `;
}

function updateRunPlanPlayhead(options = {}) {
  const playhead = document.querySelector('[data-run-plan-playhead]');
  const track = document.querySelector('[data-run-plan-card-track]') || document.querySelector('[data-run-plan-ruler-track]');
  const schedule = document.querySelector('.runPlanSchedule');
  if (!playhead || !track || !schedule || !state) return;

  const animate = options.animate !== false;
  const totalSeconds = Math.max(1, Number(playhead.dataset.totalSeconds) || Number(document.querySelector('.timeRuler')?.dataset.runPlanTotalSeconds) || 1);
  const runActive = Boolean(state.run?.isRunning || state.run?.cancellationRequested);
  const timelineSeconds = getRunPlayheadTimelineSeconds(totalSeconds);
  const position = Math.max(0, Math.min(1, timelineSeconds / totalSeconds));
  const scheduleRect = schedule.getBoundingClientRect();
  const trackRect = track.getBoundingClientRect();
  const left = (trackRect.left - scheduleRect.left) + trackRect.width * position;
  const timeLabel = playhead.querySelector('[data-playhead-time]');

  playhead.classList.toggle('instant', !animate);
  playhead.classList.toggle('running', runActive);
  playhead.classList.toggle('finished', !runActive && state.run?.finishedAtUtc);
  if (timeLabel) {
    timeLabel.textContent = formatTimelineElapsed(timelineSeconds);
    positionPlayheadTimeLabel(timeLabel, left, trackRect, scheduleRect);
  }
  const roundedLeft = Math.round(left);
  playhead.style.left = `${roundedLeft}px`;
  lastRunPlanPlayheadLeftPx = roundedLeft;
  if (!animate) {
    if (runPlanPlayheadInstantTimeout) {
      clearTimeout(runPlanPlayheadInstantTimeout);
    }
    runPlanPlayheadInstantTimeout = setTimeout(() => {
      if (playhead.isConnected) {
        playhead.classList.remove('instant');
      }
      runPlanPlayheadInstantTimeout = null;
    }, 420);
  }
}

function positionPlayheadTimeLabel(timeLabel, lineLeft, trackRect, scheduleRect) {
  const labelWidth = Math.max(44, timeLabel.getBoundingClientRect().width || timeLabel.offsetWidth || 44);
  const trackStart = Math.max(0, trackRect.left - scheduleRect.left);
  const trackEnd = Math.min(scheduleRect.width, trackRect.right - scheduleRect.left);
  const minLabelLeft = trackStart;
  const maxLabelLeft = Math.max(minLabelLeft, trackEnd - labelWidth);
  const centeredLabelLeft = lineLeft - (labelWidth / 2);
  const labelLeft = Math.max(minLabelLeft, Math.min(maxLabelLeft, centeredLabelLeft));
  const pointerLeft = Math.max(7, Math.min(labelWidth - 7, lineLeft - labelLeft));

  timeLabel.style.setProperty('--playhead-time-offset', `${Math.round(labelLeft - lineLeft)}px`);
  timeLabel.style.setProperty('--playhead-time-pointer', `${Math.round(pointerLeft)}px`);
}

function getRunPlayheadTimelineSeconds(totalSeconds) {
  const cards = Array.from(document.querySelectorAll('.runPlanCard'));
  const activePositions = cards
    .map(card => getActiveCardTimelineSeconds(card))
    .filter(value => Number.isFinite(value));

  if (activePositions.length) {
    return Math.max(0, Math.min(totalSeconds, Math.max(...activePositions)));
  }

  const completedPositions = cards
    .map(card => getCompletedCardEndSeconds(card))
    .filter(value => Number.isFinite(value));
  if ((state.run?.isRunning || state.run?.cancellationRequested || state.run?.finishedAtUtc) && completedPositions.length) {
    return Math.max(0, Math.min(totalSeconds, Math.max(...completedPositions)));
  }

  return 0;
}

function getActiveCardTimelineSeconds(card) {
  const item = findQueueItemByCard(card);
  if (!item || !isActiveStatus(item.status)) return NaN;

  const startSeconds = Number(card.dataset.startSeconds);
  const durationSeconds = Number(card.dataset.durationSeconds);
  if (!Number.isFinite(startSeconds) || !Number.isFinite(durationSeconds)) return NaN;
  const startLeadInSeconds = Math.max(
    0,
    Math.min(durationSeconds, Number(card.dataset.startLeadInSeconds) || 0));
  const movableDurationSeconds = Math.max(0.001, durationSeconds - startLeadInSeconds);

  const startedAt = item.assignedAtUtc ? new Date(item.assignedAtUtc) : null;
  if (!startedAt || Number.isNaN(startedAt.getTime())) return startSeconds;

  const activeSeconds = Math.max(0, (Date.now() - startedAt.getTime()) / 1000);
  if (activeSeconds <= startLeadInSeconds) return startSeconds;

  const movementSeconds = activeSeconds - startLeadInSeconds;
  const progressSeconds = Math.min(
    durationSeconds,
    movementSeconds * (durationSeconds / movableDurationSeconds));
  return startSeconds + progressSeconds;
}

function getCompletedCardEndSeconds(card) {
  const item = findQueueItemByCard(card);
  if (!item || (!sameStatus(item.status, 'Completed') && !sameStatus(item.status, 'Failed'))) return NaN;

  const startSeconds = Number(card.dataset.startSeconds);
  const durationSeconds = Number(card.dataset.durationSeconds);
  if (!Number.isFinite(startSeconds) || !Number.isFinite(durationSeconds)) return NaN;
  const endSeconds = startSeconds + durationSeconds;
  const gapAfterSeconds = Math.max(0, Number(card.dataset.gapAfterSeconds) || 0);
  if (!gapAfterSeconds || !state.run?.isRunning) return endSeconds;

  const completedAt = item.completedAtUtc ? new Date(item.completedAtUtc) : null;
  if (!completedAt || Number.isNaN(completedAt.getTime())) return endSeconds;

  const gapSeconds = Math.max(0, (Date.now() - completedAt.getTime()) / 1000);
  return endSeconds + Math.min(gapAfterSeconds, gapSeconds);
}

function findQueueItemByCard(card) {
  const id = card?.dataset?.selectId;
  if (!id) return null;
  return (state.queue || []).find(item => item.id === id) || null;
}

function buildTimeRulerTicks(totalSeconds) {
  const safeTotal = Math.max(1, Number(totalSeconds) || 1);
  const tickCount = 5;
  const ticks = [{ position: 0, label: '0:00' }];
  for (let index = 1; index < tickCount; index++) {
    const seconds = safeTotal * (index / (tickCount - 1));
    ticks.push({
      position: index / (tickCount - 1),
      label: formatRulerTime(seconds)
    });
  }

  return ticks;
}

function formatRulerTime(seconds) {
  const rounded = Math.max(1, Math.round(Number(seconds) || 0));
  const hours = Math.floor(rounded / 3600);
  const minutes = Math.floor((rounded % 3600) / 60);
  const remainingSeconds = rounded % 60;
  if (hours > 0) return `${hours}:${String(minutes).padStart(2, '0')}:${String(remainingSeconds).padStart(2, '0')}`;
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

function formatTimelineElapsed(seconds) {
  const rounded = Math.max(0, Math.round(Number(seconds) || 0));
  const hours = Math.floor(rounded / 3600);
  const minutes = Math.floor((rounded % 3600) / 60);
  const remainingSeconds = rounded % 60;
  if (hours > 0) return `${hours}:${String(minutes).padStart(2, '0')}:${String(remainingSeconds).padStart(2, '0')}`;
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

function formatPercent(value) {
  const percent = Math.max(0, Math.min(1, Number(value) || 0)) * 100;
  return `${percent.toFixed(3)}%`;
}

function renderRunPlanLane(lane, visibleQueue, totalSeconds) {
  const index = lane.index;
  const instance = lane.instance;
  const label = instance?.name || `BSARR I-${index + 1}`;
  const enabled = !instance || isInstanceEnabled(instance);
  const subtitle = !enabled
    ? 'Disabled'
    : instance?.workerId
    ? `${instance.status || 'Ready'} worker`
    : (instance?.gameLaunchStatus || 'Worker waiting');
  const cards = lane.items
    .map(scheduled => renderRunPlanCard(scheduled, visibleQueue, index, totalSeconds))
    .join('');

  return `
    <div class="runPlanLane ${enabled ? '' : 'disabledLane'}">
      <div class="laneLabel">
        <span class="statusDot ${enabled && (instance?.workerId || instance?.gameProcessId) ? 'good' : 'warn'}" aria-hidden="true"></span>
        <strong>${escapeHtml(label)}</strong>
        <span>${escapeHtml(subtitle)}</span>
      </div>
      <div class="laneTrack">
        <div class="laneLine" aria-hidden="true"></div>
        <div class="laneCards" data-run-plan-card-track style="--timeline-seconds: ${Number(totalSeconds).toFixed(3)}">${cards}</div>
      </div>
    </div>
  `;
}

function renderRunPlanCard(scheduled, visibleQueue, laneIndex, totalSeconds) {
  const item = scheduled.item;
  const queue = state.queue || [];
  const index = queue.findIndex(candidate => candidate.id === item.id);
  const selected = selectedQueueId === item.id;
  const active = isActiveStatus(item.status);
  const completed = sameStatus(item.status, 'Completed');
  const canDrag = !active;
  const canOpen = canOpenRecording(item);
  const warning = !completed && (Boolean(item.error) || sameStatus(item.mapStatus, 'Missing') || isSyncOutOfRange(item));
  const className = [
    'runPlanCard',
    selected ? 'selected' : '',
    active ? 'active' : '',
    completed ? 'complete' : '',
    warning ? 'warning' : ''
  ].filter(Boolean).join(' ');
  const startRatio = scheduled.startSeconds / Math.max(1, totalSeconds);
  const widthRatio = scheduled.durationSeconds / Math.max(1, totalSeconds);

  return `
    <article class="${className}" data-select-id="${escapeHtml(item.id)}" data-drag-id="${escapeHtml(item.id)}" data-start-seconds="${Number(scheduled.startSeconds).toFixed(3)}" data-duration-seconds="${Number(scheduled.durationSeconds).toFixed(3)}" data-start-lead-in-seconds="${Number(scheduled.startLeadInSeconds || 0).toFixed(3)}" data-gap-after-seconds="${Number(scheduled.gapAfterSeconds || 0).toFixed(3)}" draggable="${canDrag ? 'true' : 'false'}" style="--card-left: ${formatPercent(startRatio)}; --card-width: ${formatPercent(widthRatio)}">
      ${renderPlanCover(item)}
      <div class="runPlanCardMain">
        <div class="cardTitleRow">
          <strong>${escapeHtml(item.songName || item.fileName)}</strong>
          <span>${formatSeconds(scheduled.durationSeconds)}</span>
        </div>
        <span class="queueMeta">${escapeHtml(renderRunPlanCardSubtitle(item))}</span>
        <div class="planPillRow">
          <span class="planPill ${mapStatusClass(item.mapStatus)}">${escapeHtml(formatPlanMapStatus(item))}</span>
          <span class="planPill sync">${escapeHtml(formatPlanSync(item))}</span>
          ${canOpen ? '<span class="planPill output">Output</span>' : ''}
        </div>
      </div>
    </article>
  `;
}

function renderTimelineItem(item) {
  return `
    <button class="timelineItem ${selectedQueueId === item.id ? 'selected' : ''}" type="button" data-select-id="${escapeHtml(item.id)}">
      <span>${escapeHtml(item.sequenceNumber)}</span>
      ${renderPlanCover(item)}
    </button>
  `;
}

function bindRunPlanActions(board) {
  board.querySelectorAll('[data-select-id]').forEach(element => {
    element.addEventListener('click', event => {
      selectedQueueId = element.dataset.selectId;
      editingQueueId = null;
      renderQueue();
    });
  });
}

function bindRunPlanDrag(board) {
  board.querySelectorAll('.runPlanCard[draggable="true"]').forEach(card => {
    card.addEventListener('dragstart', event => {
      draggedQueueId = card.dataset.dragId || null;
      card.classList.add('dragging');
      if (event.dataTransfer) {
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', draggedQueueId || '');
      }
    });

    card.addEventListener('dragend', () => {
      draggedQueueId = null;
      clearRunPlanDropMarkers(board);
    });

    card.addEventListener('dragover', event => {
      if (!draggedQueueId || draggedQueueId === card.dataset.dragId) return;
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
      updateRunPlanDropMarker(card, event);
    });

    card.addEventListener('dragleave', event => {
      if (!event.relatedTarget || !card.contains(event.relatedTarget)) {
        card.classList.remove('dropBefore', 'dropAfter');
      }
    });

    card.addEventListener('drop', event => {
      event.preventDefault();
      event.stopPropagation();
      const sourceId = draggedQueueId || event.dataTransfer?.getData('text/plain') || '';
      const targetId = card.dataset.dragId || '';
      const placeAfter = isDropAfter(card, event);
      draggedQueueId = null;
      clearRunPlanDropMarkers(board);
      moveQueueItemByDrag(sourceId, targetId, placeAfter);
    });
  });
}

function updateRunPlanDropMarker(card, event) {
  const after = isDropAfter(card, event);
  card.classList.toggle('dropBefore', !after);
  card.classList.toggle('dropAfter', after);
}

function isDropAfter(card, event) {
  const rect = card.getBoundingClientRect();
  return event.clientX > rect.left + rect.width / 2;
}

function clearRunPlanDropMarkers(root = document) {
  root.querySelectorAll('.runPlanCard.dropBefore, .runPlanCard.dropAfter, .runPlanCard.dragging')
    .forEach(card => card.classList.remove('dropBefore', 'dropAfter', 'dragging'));
}

async function moveQueueItemByDrag(sourceId, targetId, placeAfter) {
  if (!sourceId || !targetId || sourceId === targetId) return;
  const queue = state.queue || [];
  const sourceIndex = queue.findIndex(item => item.id === sourceId);
  const targetIndex = queue.findIndex(item => item.id === targetId);
  if (sourceIndex < 0 || targetIndex < 0) return;

  let destinationIndex = targetIndex + (placeAfter ? 1 : 0);
  if (sourceIndex < destinationIndex) destinationIndex--;
  destinationIndex = Math.max(0, Math.min(queue.length - 1, destinationIndex));
  if (destinationIndex === sourceIndex) return;

  const action = destinationIndex < sourceIndex ? 'move-up' : 'move-down';
  const steps = Math.abs(destinationIndex - sourceIndex);
  await runAction(async () => {
    for (let index = 0; index < steps; index++) {
      await postJson(queueUrl(sourceId, action));
    }
    selectedQueueId = sourceId;
    renderQueue();
    showToast('Replay moved');
  });
}

function renderPlanCover(item) {
  return `
    <span class="planCover" aria-hidden="true">
      ${item.coverArtUrl ? `<img src="${escapeHtml(item.coverArtUrl)}" alt="" onerror="this.remove()">` : ''}
      <span>${escapeHtml(initials(item.songName || item.fileName))}</span>
    </span>
  `;
}

function formatPlanMapStatus(item) {
  if (sameStatus(item.mapStatus, 'Found') || sameStatus(item.mapStatus, 'Downloaded')) return 'Map OK';
  return formatMapStatus(item);
}

function formatPlanSync(item) {
  if (Number.isFinite(Number(item.syncCorrectionMilliseconds))) {
    return `${formatSignedNumber(Number(item.syncCorrectionMilliseconds).toFixed(1))} ms`;
  }

  if (item.syncStatus) return item.syncStatus;
  return 'Sync pending';
}

function isSyncOutOfRange(item) {
  const offset = Number(item.syncCorrectionMilliseconds);
  return Number.isFinite(offset) && Math.abs(offset) >= 100;
}

function renderQueueDetails() {
  const details = document.getElementById('queueDetails');
  const item = (state.queue || []).find(candidate => candidate.id === selectedQueueId);
  details.innerHTML = '';

  if (!item) {
    details.innerHTML = `
      <div class="detailsEmpty">
        <span class="detailIcon">${icon('list')}</span>
        <strong>No replay selected</strong>
      </div>
    `;
    return;
  }

  const index = state.queue.findIndex(candidate => candidate.id === item.id);
  const active = isActiveStatus(item.status);
  const queued = sameStatus(item.status, 'Queued');
  const canOpen = canOpenRecording(item);
  const pathText = item.error || item.outputPath || '';
  const pathClass = item.error ? 'detailPath errorText' : 'detailPath';
  const mapMissing = sameStatus(item.mapStatus, 'Missing');

  details.innerHTML = `
    <div class="inspectorHero">
      <div class="inspectorThumb" aria-hidden="true">
        ${item.coverArtUrl ? `<img src="${escapeHtml(item.coverArtUrl)}" alt="" onerror="this.remove()">` : ''}
        <span>${escapeHtml(initials(item.songName || item.fileName))}</span>
      </div>
      <div class="inspectorMain">
        <div class="detailsHeader">
          <div class="detailsTitleBlock">
            <span class="detailIndex">#${item.sequenceNumber}</span>
            <h3>${escapeHtml(item.songName || item.fileName)}</h3>
            <p>${escapeHtml(renderRunPlanCardSubtitle(item))}</p>
          </div>
          <span class="badge ${statusClass(item.status)}">${escapeHtml(item.status)}</span>
        </div>
        <dl class="detailGrid">
          <div class="detailMetric"><dt>Player</dt><dd>${escapeHtml(item.playerName || '-')}</dd></div>
          <div class="detailMetric"><dt>Difficulty</dt><dd>${escapeHtml(item.difficulty || '-')}</dd></div>
          <div class="detailMetric"><dt>Length</dt><dd>${formatSeconds(item.estimatedSeconds)}</dd></div>
        </dl>
        ${renderMapStatusDetail(item)}
        ${pathText ? `<div class="${pathClass}">${item.error ? '<strong>Failure reason</strong>' : ''}<span>${escapeHtml(pathText)}</span></div>` : ''}
        ${renderSyncMarkerResult(item)}
        ${renderCalibrationResult(item)}
      </div>
      <div class="detailsActions">
        <button class="actionButton" type="button" data-detail-move-up${disabledAttr(active || index === 0)}>${icon('up')}<span>Move earlier</span></button>
        <button class="actionButton" type="button" data-detail-move-down${disabledAttr(active || index === state.queue.length - 1)}>${icon('down')}<span>Move later</span></button>
        <button class="actionButton" type="button" data-detail-open${disabledAttr(!canOpen)}>${icon('open')}<span>Open recording</span></button>
        ${mapMissing ? `
          <button class="actionButton" type="button" data-detail-download-map${disabledAttr(active)}>${icon('download')}<span>Repair / download map</span></button>
          <button class="actionButton primaryAction" type="button" data-detail-upload-map${disabledAttr(active)}>${icon('upload')}<span>Upload map</span></button>
        ` : ''}
        <button class="actionButton" type="button" data-detail-requeue${disabledAttr(active || queued)}>${icon('refresh')}<span>Requeue</span></button>
        <button class="actionButton dangerAction" type="button" data-detail-remove${disabledAttr(active)}>${icon('trash')}<span>Remove</span></button>
      </div>
      <input class="hiddenFileInput" type="file" accept=".zip" data-map-file>
    </div>
  `;

  bindQueueDetailsActions(details, item);
}

function syncQueueSelection(queue, visibleQueue) {
  const selectedExists = queue.some(item => item.id === selectedQueueId);
  const selectedVisible = visibleQueue.some(item => item.id === selectedQueueId);
  if (!selectedExists || !selectedVisible) {
    selectedQueueId = visibleQueue[0]?.id || null;
  }

  if (editingQueueId && editingQueueId !== selectedQueueId) {
    editingQueueId = null;
  }
}

function renderQueueSubtitle(item) {
  const parts = [];
  if (item.mapper) parts.push(item.mapper);
  if (item.fileName && item.mapper !== item.fileName) parts.push(item.fileName);
  return parts.join(' | ') || item.fileName || 'Replay';
}

function renderRunPlanCardSubtitle(item) {
  return item.mapper || item.playerName || item.difficulty || 'Replay';
}

function formatMapStatus(item) {
  const status = item.mapStatus || 'Unchecked';
  if (sameStatus(status, 'Downloading')) return 'Checking map';
  if (sameStatus(status, 'Missing')) return 'Map missing';
  return 'Map unchecked';
}

function shouldShowMapStatus(item) {
  const status = item.mapStatus || 'Unchecked';
  return sameStatus(status, 'Missing') ||
    sameStatus(status, 'Downloading') ||
    sameStatus(status, 'Unchecked');
}

function renderMapMiniStatus(item) {
  if (!shouldShowMapStatus(item)) return '';
  return `<span class="mapMiniStatus ${mapStatusClass(item.mapStatus)}">${escapeHtml(formatMapStatus(item))}</span>`;
}

function mapStatusClass(status) {
  if (sameStatus(status, 'Found') || sameStatus(status, 'Downloaded')) return 'mapOk';
  if (sameStatus(status, 'Downloading')) return 'mapChecking';
  if (sameStatus(status, 'Missing')) return 'mapMissing';
  return 'mapUnchecked';
}

function renderMapStatusDetail(item) {
  if (!shouldShowMapStatus(item)) return '';
  const detail = item.mapStatusDetail || '';
  const installPath = item.mapInstallPath || '';
  if (!detail && !installPath) return '';
  return `
    <div class="mapStatusDetail ${mapStatusClass(item.mapStatus)}">
      <strong>${escapeHtml(formatMapStatus(item))}</strong>
      ${detail ? `<span>${escapeHtml(detail)}</span>` : ''}
      ${installPath ? `<small>${escapeHtml(installPath)}</small>` : ''}
    </div>
  `;
}

function renderQueuePathLine(item) {
  const text = item.outputPath || item.error || '';
  if (!text) return '';
  const className = item.error ? 'queuePath errorText' : 'queuePath outputPath';
  return `<span class="${className}">${escapeHtml(text)}</span>`;
}

function assignedInstanceText(item) {
  return item.assignedInstance === null || item.assignedInstance === undefined
    ? '-'
    : item.assignedInstance + 1;
}

function canOpenRecording(item) {
  return sameStatus(item.status, 'Completed') && Boolean(item.outputPath);
}

function disabledAttr(disabled) {
  return disabled ? ' disabled' : '';
}

function bindQueueRowActions(row) {
  const moveUp = row.querySelector('[data-move-up]');
  const moveDown = row.querySelector('[data-move-down]');
  const open = row.querySelector('[data-open]');
  const select = row.querySelector('[data-select-id]');
  const selectId = select?.dataset.selectId;

  if (moveUp) moveUp.addEventListener('click', () => queueAction(moveUp.dataset.moveUp, 'move-up', 'Replay moved up'));
  if (moveDown) moveDown.addEventListener('click', () => queueAction(moveDown.dataset.moveDown, 'move-down', 'Replay moved down'));
  if (open) open.addEventListener('click', () => openRecordedFile(open.dataset.open));
  row.addEventListener('click', event => {
    if (!selectId || event.target.closest('.queueItemControls')) return;
    selectedQueueId = selectId;
    editingQueueId = null;
    renderQueue();
  });
}

function bindQueueDetailsActions(details, item) {
  const moveUp = details.querySelector('[data-detail-move-up]');
  const moveDown = details.querySelector('[data-detail-move-down]');
  const open = details.querySelector('[data-detail-open]');
  const downloadMap = details.querySelector('[data-detail-download-map]');
  const uploadMap = details.querySelector('[data-detail-upload-map]');
  const mapFile = details.querySelector('[data-map-file]');
  const requeue = details.querySelector('[data-detail-requeue]');
  const remove = details.querySelector('[data-detail-remove]');

  if (moveUp) moveUp.addEventListener('click', () => queueAction(item.id, 'move-up', 'Replay moved up'));
  if (moveDown) moveDown.addEventListener('click', () => queueAction(item.id, 'move-down', 'Replay moved down'));
  if (open) open.addEventListener('click', () => openRecordedFile(item.id));
  if (downloadMap) downloadMap.addEventListener('click', () => queueAction(item.id, 'map/download', 'Map check finished'));
  if (uploadMap && mapFile) {
    uploadMap.addEventListener('click', () => mapFile.click());
    mapFile.addEventListener('change', () => uploadQueueMap(item.id, mapFile.files?.[0] || null));
  }
  if (requeue) requeue.addEventListener('click', () => queueAction(item.id, 'requeue', 'Replay requeued'));
  if (remove) remove.addEventListener('click', () => queueAction(item.id, 'remove', 'Replay removed'));
}

function renderSyncMarkerResult(item) {
  if (!item.syncStatus) return '';

  const status = item.syncStatus || 'Unknown';
  const correction = Number.isFinite(Number(item.syncCorrectionMilliseconds))
    ? `Correction ${formatSignedNumber(Number(item.syncCorrectionMilliseconds).toFixed(1))} ms`
    : 'Correction unavailable';
  const trim = Number.isFinite(Number(item.trimStartSeconds))
    ? `Trim ${formatNumber(item.trimStartSeconds)}s`
    : 'Trim unavailable';

  return `
    <div class="syncSummary">
      <div class="syncHeader">
        <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
        <span class="cellTitle">${escapeHtml(correction)}</span>
        <span class="muted">${escapeHtml(trim)}</span>
      </div>
      ${item.syncReportPath ? `<div class="detailPath">${escapeHtml(item.syncReportPath)}</div>` : ''}
    </div>
  `;
}

function renderCalibrationResult(item) {
  const calibration = item.calibration || {};
  if (!calibration.updatedAtUtc && (!calibration.status || calibration.status === 'Unset')) return '';

  const offset = Number.isFinite(Number(calibration.syncOffsetMilliseconds))
    ? `${formatSignedNumber(Number(calibration.syncOffsetMilliseconds).toFixed(1))} ms`
    : 'Offset unset';
  const trim = Number.isFinite(Number(calibration.trimStartSeconds))
    ? `Trim ${formatNumber(calibration.trimStartSeconds)}s`
    : 'Trim unset';
  const updated = calibration.updatedAtUtc ? `Updated ${formatEventTime(calibration.updatedAtUtc)}` : 'Not saved';

  return `
    <div class="syncSummary calibrationSummary">
      <div class="syncHeader">
        <span class="badge ${statusClass(calibration.status || 'Manual')}">${escapeHtml(calibration.status || 'Manual')}</span>
        <span class="cellTitle">${escapeHtml(offset)}</span>
        <span class="muted">${escapeHtml(`${trim} | ${updated}`)}</span>
      </div>
      ${calibration.notes ? `<div class="detailPath">${escapeHtml(calibration.notes)}</div>` : ''}
    </div>
  `;
}

async function queueAction(id, action, message) {
  if (!id) return;
  await runAction(async () => {
    await postJson(queueUrl(id, action));
    showToast(message);
  });
}

async function uploadQueueMap(id, file) {
  if (!id || !file) return;
  if (!file.name.toLowerCase().endsWith('.zip')) {
    showToast('Choose a song .zip');
    return;
  }

  await runAction(async () => {
    const form = new FormData();
    form.append('file', file);
    const response = await fetch(queueUrl(id, 'map/upload'), { method: 'POST', body: form });
    if (!response.ok) throw new Error(await response.text());
    state = await response.json();
    render();
    showToast('Map uploaded');
  });
}

async function openRecordedFile(id) {
  if (!id) return;
  await runAction(async () => {
    const response = await fetch(queueUrl(id, 'recording/open'), { method: 'POST' });
    if (!response.ok) throw new Error(await response.text());

    const result = await response.json();
    showToast(result.status || 'Recording opened in File Explorer');
  });
}

function queueUrl(id, action) {
  return `/api/queue/${encodeURIComponent(id)}/${action}`;
}

function matchesQueueSearch(item) {
  const query = normalizedQueueSearch();
  if (!query) return true;

  const fields = [
    item.sequenceNumber,
    item.songName,
    item.mapper,
    item.playerName,
    item.difficulty,
    item.fileName,
    item.path,
    item.outputPath,
    item.error,
    item.levelHash,
    item.mapStatus,
    item.mapStatusDetail,
    item.mapInstallPath,
    item.syncStatus
  ];

  return fields.some(value => String(value ?? '').toLowerCase().includes(query));
}

function normalizedQueueSearch() {
  return queueSearchText.trim().toLowerCase();
}

function icon(name) {
  const symbols = {
    up: '&#9650;',
    down: '&#9660;',
    edit: '&#9998;',
    open: '&#9654;',
    download: '&#8595;',
    upload: '&#8593;',
    refresh: '&#8635;',
    trash: '&#215;',
    save: '&#10003;',
    close: '&#215;',
    list: '&#9776;',
    wave: '&#8767;'
  };

  return `<span class="nativeIcon" aria-hidden="true">${symbols[name] || ''}</span>`;
}

function setValue(id, value) {
  const element = document.getElementById(id);
  if (!element) return;
  element.value = value ?? '';
}

function renderMonitorOptions() {
  const select = document.getElementById('monitorIndex');
  if (!select) return;

  const savedValue = String(state?.settings?.monitorIndex ?? 1);
  const selectedValue = select.value || savedValue;
  const displays = Array.isArray(displayInfo?.displays)
    ? displayInfo.displays.filter(display => Number.isFinite(Number(display.index)))
    : [];
  const options = displays.length
    ? displays
      .slice()
      .sort((first, second) => Number(first.index) - Number(second.index))
      .map(display => ({
        value: String(display.index),
        label: buildDetectedMonitorLabel(display)
      }))
    : [{
      value: selectedValue,
      label: `Saved monitor ${selectedValue} (display detection unavailable)`
    }];

  select.innerHTML = '';
  for (const option of options) {
    const element = document.createElement('option');
    element.value = option.value;
    element.textContent = option.label;
    select.appendChild(element);
  }

  select.value = options.some(option => option.value === selectedValue)
    ? selectedValue
    : options[0]?.value ?? '';
}

function setText(id, value) {
  const element = document.getElementById(id);
  if (!element) return;
  element.textContent = value ?? '';
}

function getNumber(id) {
  return Number(document.getElementById(id)?.value);
}

function getNumberOrSetting(id, settingName, fallback = 0) {
  const element = document.getElementById(id);
  if (element) return Number(element.value);
  const value = state?.settings?.gamePresentation?.[settingName] ?? state?.settings?.[settingName] ?? fallback;
  return Number(value);
}

function buildDetectedMonitorLabel(display) {
  if (display?.label) return trimMonitorIndexSuffix(display.label);

  const index = Number(display?.index);
  const monitorNumber = Number(display?.monitorNumber);
  const labelParts = [
    Number.isFinite(monitorNumber) && monitorNumber > 0
      ? `Monitor ${monitorNumber}`
      : 'Monitor'
  ];

  if (display?.friendlyName) labelParts.push(display.friendlyName);
  if (display?.width && display?.height) labelParts.push(`${display.width} x ${display.height}`);
  if (display?.isPrimary) labelParts.push('Primary');

  return labelParts.join(' - ');
}

function trimMonitorIndexSuffix(label) {
  return String(label ?? '').replace(/\s+-\s+index\s+\d+\s*$/i, '').trim();
}

function clampManagedInstanceCount(value) {
  const count = Math.trunc(Number(value));
  if (!Number.isFinite(count)) return minManagedInstanceCount;
  return Math.min(maxManagedInstanceCount, Math.max(minManagedInstanceCount, count));
}

function clampPositiveInteger(value, minimum, maximum) {
  const count = Math.trunc(Number(value));
  if (!Number.isFinite(count)) return minimum;
  return Math.min(maximum, Math.max(minimum, count));
}

function getCreatedInstanceCount(settings = {}) {
  const desiredCount = getManagedInstanceInventoryCount(settings);
  const provision = state?.instanceProvision || {};
  const reportCount = Number(provision.createdInstanceCount);
  if (Number.isFinite(reportCount) && reportCount > 0) {
    return Math.min(desiredCount, reportCount);
  }

  return Math.min(
    desiredCount,
    (state?.instances || []).filter(instance =>
      Number(instance.index) < desiredCount &&
      instance.launchDirectoryReady).length);
}

function getManagedInstanceInventoryCount(settings = {}) {
  const settingCount = Number(state?.settings?.instanceCount);
  const previewCount = Number(settings.instanceCount);
  const fieldCount = Number(document.getElementById('instanceCount')?.value);
  const provisionCount = Number(state?.instanceProvision?.desiredInstanceCount);
  const recordCount = Math.max(
    0,
    ...(state?.instances || []).map(instance => Number(instance.index) + 1).filter(Number.isFinite));
  return clampManagedInstanceCount(Math.max(
    minManagedInstanceCount,
    Number.isFinite(settingCount) ? settingCount : 0,
    Number.isFinite(previewCount) ? previewCount : 0,
    Number.isFinite(fieldCount) ? fieldCount : 0,
    Number.isFinite(provisionCount) ? provisionCount : 0,
    recordCount));
}

function getText(id) {
  return document.getElementById(id).value;
}

function getTextOrSetting(id, settingName) {
  const element = document.getElementById(id);
  return element ? element.value : state.settings[settingName];
}

function getCheckedOrSetting(id, settingName) {
  const element = document.getElementById(id);
  return element ? element.checked : state.settings[settingName] !== false;
}

function getGameCheckedOrSetting(id, settingName, fallback = false) {
  const element = document.getElementById(id);
  if (element) return element.checked;
  return state?.settings?.gamePresentation?.[settingName] ?? fallback;
}

function getLines(id) {
  return getText(id)
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean);
}

function setResolutionPreset(width, height) {
  const select = document.getElementById('resolutionPreset');
  const value = `${Number(width) || 0}x${Number(height) || 0}`;
  const hasPreset = Array.from(select.options).some(option => option.value === value);
  select.value = hasPreset ? value : 'custom';
}

function resolveLaunchPreset(settings) {
  const presetOrder = [
    '4k-monitor-2x2',
    '1440p-monitor-2x2',
    'single-4k',
    'single-1440p',
    'single-1080p',
    'windowed-720p',
    'windowed-1080p'
  ];
  return presetOrder.find(presetId => launchPresetMatches(settings, launchPresets[presetId], presetId)) || 'custom';
}

function launchPresetMatches(settings, preset, presetId = '') {
  if (!settings || !preset) return false;

  return Object.entries(preset).every(([fieldId, expected]) => {
    if (fieldId === 'instanceCount' || fieldId === 'maxConcurrentRecordings') return true;

    const actual = settings[fieldId];
    if (typeof expected === 'number') return Number(actual) === expected;
    if (typeof expected === 'boolean') return Boolean(actual) === expected;
    return String(actual ?? '') === String(expected);
  });
}

function applyLaunchPreset(id) {
  const preset = launchPresets[id];
  if (!preset) return;

  const values = {
    ...(launchPresetApplyDefaults[id] || {}),
    ...preset
  };

  for (const [fieldId, value] of Object.entries(values)) {
    if (fieldId === 'instanceCount' || fieldId === 'maxConcurrentRecordings') continue;

    const element = document.getElementById(fieldId);
    if (!element) continue;

    if (element.type === 'checkbox') {
      element.checked = Boolean(value);
    } else {
      setValue(fieldId, value);
    }
  }

  setResolutionPreset(getNumber('captureWidth'), getNumber('captureHeight'));
  updateDisplayScaleAvailability();
  setValue('beatSaberLaunchPreset', id);
}

function markLaunchPresetCustom() {
  if (isRendering) return;
  setValue('beatSaberLaunchPreset', 'custom');
}

function applySetupProfile(id) {
  const profile = setupProfiles[id];
  if (!profile) return;

  if (profile.launchPreset) {
    applyLaunchPreset(profile.launchPreset);
  }

  for (const [fieldId, value] of Object.entries(profile.settings || {})) {
    setFieldValue(fieldId, value);
  }

  pendingSetupEnabledInstanceCount = getSetupProfileEnabledInstanceCount(id);
  updateAudioLevelTargetConstraints();
  settingsDirty = true;
  updateSettingsDirtyBadge();
  renderSetupAssistant();
  showToast('Setup profile applied');
}

function setFieldValue(fieldId, value) {
  const element = document.getElementById(fieldId);
  if (!element) return;

  if (element.type === 'checkbox') {
    element.checked = Boolean(value);
    return;
  }

  setValue(fieldId, value);
}

function renderSetupAssistant() {
  ensureGameSettingsPlacement();

  const panel = document.getElementById('setupAssistant');
  if (!panel || !state) return;

  setupAssistantHidden = false;
  panel.hidden = false;
  const showButton = document.getElementById('showSetupAssistant');
  if (showButton) showButton.hidden = true;

  const settings = buildCurrentSettingsPreview();
  const profileId = resolveSetupProfile(settings);
  const configuredCount = getManagedInstanceInventoryCount(settings);
  const createdCount = getCreatedInstanceCount(settings);
  const missingCount = Math.max(0, configuredCount - createdCount);
  renderFeedPresetList(settings, profileId);
  renderSelectedMonitorSummary(settings);
  renderSetupInstanceList(settings, configuredCount, createdCount, missingCount);

  const summary = document.getElementById('setupAssistantSummary');
  if (summary) {
    summary.textContent = `${createdCount}/${configuredCount} created | ${formatSetupProfile(profileId)} | ${formatMonitorSummary(settings.monitorIndex)} | ${formatAudioMode(settings)}`;
  }
}

function renderSetupInstanceList(settings, configuredCount, createdCount, missingCount) {
  const rows = document.getElementById('setupInstanceRows');
  const summary = document.getElementById('setupInstanceSummary');
  const addButton = document.getElementById('setupWizardAddInstance');
  if (!rows) return;

  const instances = state?.instances || [];
  const enabledCount = instances
    .filter(instance => Number(instance.index) < configuredCount && getEffectiveSetupInstanceEnabled(instance))
    .length;

  if (summary) {
    summary.textContent = `${createdCount}/${configuredCount} created | ${Math.max(1, enabledCount)} enabled`;
  }

  rows.innerHTML = '';
  for (let index = 0; index < configuredCount; index++) {
    const instance = instances.find(item => Number(item.index) === index) || {
      index,
      name: `BSARR I-${index + 1}`,
      enabled: true,
      launchDirectoryReady: false
    };
    const created = Boolean(instance.launchDirectoryReady);
    const storedEnabled = isInstanceEnabled(instance);
    const enabled = getEffectiveSetupInstanceEnabled(instance);
    const pendingEnabledChange = pendingSetupEnabledInstanceCount != null && enabled !== storedEnabled;
    const isLast = index === configuredCount - 1;
    const canRemove = configuredCount > minManagedInstanceCount && isLast;
    const busy = Boolean(
      state?.run?.isRunning ||
      state?.run?.cancellationRequested ||
      instance.gameProcessId ||
      instance.workerId ||
      isActiveStatus(instance.status) ||
      sameStatus(instance.status, 'Recording'));
    const statusLabel = created ? 'Created' : 'Missing';
    const enabledLabel = created
      ? (pendingEnabledChange
        ? (enabled ? 'Will enable' : 'Will disable')
        : (enabled ? 'Enabled' : 'Disabled'))
      : 'Not created';
    const locationLabel = created
      ? shortPath(instance.launchDirectory || settings.beatSaberInstancesRoot || '')
      : 'Create this managed copy from Instance 1.';
    const row = document.createElement('div');
    row.className = `setupInstanceRow ${created ? '' : 'isMissing'} ${enabled ? '' : 'isDisabled'}`;
    row.innerHTML = `
      <div class="setupInstanceIdentity">
        <span class="instanceDot ${created && enabled ? 'online' : 'idle'}" aria-hidden="true"></span>
        <div>
          <strong>${escapeHtml(instance.name || `BSARR I-${index + 1}`)}</strong>
          <span>${escapeHtml(locationLabel)}</span>
        </div>
      </div>
      <div class="setupInstanceBadges">
        <span class="setupInstanceBadge ${created ? 'isCreated' : 'isMissing'}">${escapeHtml(statusLabel)}</span>
        <span class="setupInstanceBadge ${enabled && created ? 'isEnabled' : ''}">${escapeHtml(enabledLabel)}</span>
      </div>
      <div class="setupInstanceActions">
        ${created ? `<button class="textButton" type="button" data-setup-instance-enabled="${index}" data-enabled="${enabled ? 'false' : 'true'}"${disabledAttr(enabled && enabledCount <= 1)}>${enabled ? 'Disable' : 'Enable'}</button>` : `<button class="textButton" type="button" data-setup-instance-create="${index}">Create</button>`}
        ${canRemove ? `<button class="textButton dangerText" type="button" data-setup-instance-remove="${index}"${disabledAttr(busy)}>Remove</button>` : ''}
      </div>
    `;
    rows.appendChild(row);
  }

  if (addButton) {
    const canAdd = configuredCount < maxManagedInstanceCount || missingCount > 0;
    addButton.hidden = !canAdd;
    addButton.textContent = missingCount > 0
      ? `Create ${missingCount} Missing Instance${missingCount === 1 ? '' : 's'}`
      : '+ Add Instance';
  }
}

function ensureGameSettingsPlacement() {
  const assistant = document.getElementById('setupAssistant');
  const advanced = document.getElementById('advancedSettingsDetails');
  const gameSettings = document.getElementById('gameSettings');
  if (!assistant || !advanced || !gameSettings) return;

  gameSettings.classList.add('setupGamePanel');
  gameSettings.classList.remove('span4');
  if (gameSettings.parentElement === assistant && advanced.nextElementSibling === gameSettings) return;

  assistant.insertBefore(gameSettings, advanced.nextElementSibling);
}

function renderFeedPresetList(settings, activeProfileId) {
  const list = document.getElementById('feedPresetList');
  if (!list) return;

  const selectedDisplay = getSelectedDisplay(settings);
  const supported = feedPresetDefinitions.filter(definition => feedPresetFitsDisplay(definition, selectedDisplay));
  const visibleDefinitions = supported.length
    ? feedPresetDefinitions.filter(definition => definition.tier === supported[supported.length - 1].tier)
    : feedPresetDefinitions;

  list.innerHTML = visibleDefinitions.map(definition => {
    const fits = feedPresetFitsDisplay(definition, selectedDisplay);
    const active = definition.profileId === activeProfileId;
    const status = fits ? 'Recommended' : formatPresetRequirement(definition);
    return `
      <button class="setupProfileButton feedPresetButton ${active ? 'active' : ''}" type="button" data-setup-profile="${escapeHtml(definition.profileId)}" ${fits ? '' : 'disabled'}>
        <strong>${escapeHtml(definition.title)}</strong>
        <span>${escapeHtml(definition.detail)}</span>
        <small>${escapeHtml(status)}</small>
      </button>
    `;
  }).join('');
}

function renderSelectedMonitorSummary(settings) {
  const summary = document.getElementById('selectedMonitorSummary');
  if (!summary) return;

  const selectedDisplay = getSelectedDisplay(settings);
  if (!selectedDisplay) {
    summary.textContent = displayInfo?.summary || 'Display detection unavailable; choose a feed manually in Advanced.';
    return;
  }

  const capability = classifyDisplayCapability(selectedDisplay);
  summary.textContent = `${buildDetectedMonitorLabel(selectedDisplay)} supports ${capability}.`;
}

function getSelectedDisplay(settings = {}) {
  const monitorIndex = Number(settings.monitorIndex);
  if (!Number.isFinite(monitorIndex)) return null;

  return (displayInfo?.displays || []).find(display => Number(display.index) === monitorIndex) || null;
}

function classifyDisplayCapability(display) {
  if (displaySupportsResolution(display, 3840, 2160)) return '1 x 4K or up to 4 x 1080p feeds';
  if (displaySupportsResolution(display, 2560, 1440)) return '1 x 1440p or up to 4 x 720p feeds';
  if (displaySupportsResolution(display, 1920, 1080)) return '1 x 1080p feed';
  return 'custom capture sizing';
}

function feedPresetFitsDisplay(definition, display) {
  if (!display) return true;
  return displaySupportsResolution(display, definition.minWidth, definition.minHeight);
}

function displaySupportsResolution(display, minWidth, minHeight) {
  const width = Number(display?.width);
  const height = Number(display?.height);
  if (!Number.isFinite(width) || !Number.isFinite(height)) return false;
  return width >= minWidth && height >= minHeight;
}

function formatPresetRequirement(definition) {
  return `Needs ${definition.minWidth} x ${definition.minHeight} or larger`;
}

function setHidden(id, hidden) {
  const element = document.getElementById(id);
  if (!element) return;
  element.hidden = hidden;
}

function resolveSetupProfile(settings) {
  const launchPreset = resolveLaunchPreset(settings);
  if (launchPreset === '4k-monitor-2x2') return 'grid-1080p';
  if (launchPreset === '1440p-monitor-2x2') return 'grid-720p';
  if (launchPreset === 'single-4k') return 'single-4k';
  if (launchPreset === 'single-1440p') return 'single-1440p';
  if (launchPreset === 'single-1080p' || launchPreset === 'windowed-1080p') return 'single-1080p';
  return 'custom';
}

function getSetupProfileEnabledInstanceCount(id) {
  if (id === 'grid-1080p' || id === 'grid-720p' || id === 'quad-4k' || id === 'quad-1440p') {
    return maxManagedInstanceCount;
  }

  if (id === 'single-1080p' || id === 'single-1440p' || id === 'single-4k') {
    return minManagedInstanceCount;
  }

  return null;
}

function getEffectiveSetupInstanceEnabled(instance) {
  if (pendingSetupEnabledInstanceCount == null) return isInstanceEnabled(instance);
  return Number(instance.index) < pendingSetupEnabledInstanceCount;
}

function formatSetupProfile(id) {
  if (id === 'grid-1080p' || id === 'quad-4k') return 'Up to 4 x 1080p';
  if (id === 'grid-720p' || id === 'quad-1440p') return 'Up to 4 x 720p';
  if (id === 'single-4k') return 'Single 4K';
  if (id === 'single-1440p') return 'Single 1440p';
  if (id === 'single-1080p') return 'Single 1080p';
  return 'Custom';
}

function buildSetupWizardChecklist(settings) {
  const instances = state.instances || [];
  const configuredCount = clampManagedInstanceCount(settings.instanceCount || instances.length || 1);
  const createdCount = getCreatedInstanceCount(settings);
  const runtimeCount = createdCount > 0 ? createdCount : configuredCount;
  const activeWorkers = instances.filter(instance => instance.index < configuredCount && instance.workerId && (createdCount === 0 || instance.launchDirectoryReady)).length;
  const processCount = instances.filter(instance => instance.index < configuredCount && instance.gameProcessId && (createdCount === 0 || instance.launchDirectoryReady)).length;
  const failedInstance = instances.find(instance => instance.gameLaunchError || instance.audioRoutingError);
  const provision = state.instanceProvision || {};
  const baseline = state.instanceBaseline || {};
  const audioMode = settings.audioMode || 'ProcessLoopback';
  const root = settings.beatSaberInstancesRoot || 'No instance root';
  const instancePlanStatus = createdCount >= configuredCount ? 'Ready' : (createdCount > 0 ? 'Missing' : 'Check');
  const provisionStatus = createdCount >= configuredCount && sameStatus(provision.status, 'Ready') ? 'Ready' : (createdCount > 0 ? 'Missing' : 'Check');
  const workerStatus = activeWorkers >= runtimeCount ? 'Ready' : 'Waiting';
  const launchStatus = failedInstance
    ? 'Failed'
    : (processCount >= runtimeCount ? 'Ready' : 'Waiting');
  const audioStatus = sameStatus(audioMode, 'ProcessLoopback')
    ? (processCount >= runtimeCount ? 'Ready' : 'Waiting')
    : (settings.requireAudioForRun === false ? 'Off' : 'Check');
  const baselineStatus = baseline.status || 'Unchecked';

  return [
    {
      label: 'Instance plan',
      status: instancePlanStatus,
      detail: `${createdCount}/${configuredCount} created under ${shortPath(root)}`
    },
    {
      label: 'Managed copy',
      status: provisionStatus,
      detail: provision.summary || 'Managed instances have not been created.'
    },
    {
      label: 'Workers',
      status: workerStatus,
      detail: `${activeWorkers}/${runtimeCount} online`
    },
    {
      label: 'Launch',
      status: launchStatus,
      detail: failedInstance
        ? `${failedInstance.name || 'Instance'}: ${failedInstance.gameLaunchError || failedInstance.audioRoutingError}`
        : `${processCount}/${runtimeCount} Beat Saber process ids known`
    },
    {
      label: 'Audio',
      status: audioStatus,
      detail: sameStatus(audioMode, 'ProcessLoopback')
        ? 'ProcessLoopback per game process'
        : 'Audio disabled'
    },
    {
      label: 'Baseline',
      status: baselineStatus,
      detail: baseline.summary || 'Baseline has not been checked.'
    }
  ];
}

function buildCurrentSettingsPreview() {
  if (!state) return {};

  try {
    return { ...state.settings, ...buildSettingsRequest() };
  } catch {
    return state.settings || {};
  }
}

function shortPath(value) {
  const text = String(value ?? '');
  if (!text) return '';
  const parts = text.split(/[\\/]/).filter(Boolean);
  if (parts.length <= 2) return text;
  return `${parts[parts.length - 2]}\\${parts[parts.length - 1]}`;
}

function showSetupAssistant() {
  writeSetupAssistantHidden(false);
  activateView('settings');
  renderSetupAssistant();
}

function updateAdvancedSettingsToggle() {
  const panel = document.getElementById('advancedSettingsDetails');
  const button = document.getElementById('setupWizardAdvanced');
  if (!panel || !button) return;

  const isOpen = !panel.hidden;
  button.textContent = isOpen ? 'Hide Advanced' : 'Advanced Settings';
  button.setAttribute('aria-expanded', String(isOpen));
}

function setAdvancedSettingsOpen(open, options = {}) {
  const panel = document.getElementById('advancedSettingsDetails');
  if (!panel) return;

  panel.hidden = !open;
  updateAdvancedSettingsToggle();
  if (open && options.scroll !== false) {
    panel.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }
}

function toggleAdvancedSettings() {
  const panel = document.getElementById('advancedSettingsDetails');
  if (!panel) return;

  setAdvancedSettingsOpen(panel.hidden);
}

function showAdvancedSettings() {
  setAdvancedSettingsOpen(true);
}

function revealSettingsTarget(targetId) {
  const panel = document.getElementById('advancedSettingsDetails');
  const target = document.getElementById(targetId);
  if (!panel || !target) return;

  if (panel === target || panel.contains(target)) {
    setAdvancedSettingsOpen(true, { scroll: false });
  }
}

async function persistSettings() {
  const enabledInstanceCount = pendingSetupEnabledInstanceCount;
  await postJson('/api/settings', buildSettingsRequest());
  if (enabledInstanceCount != null) {
    await applySetupEnabledInstanceCount(enabledInstanceCount);
    pendingSetupEnabledInstanceCount = null;
    render();
  }
  settingsDirty = false;
  updateSettingsDirtyBadge();
}

async function applySetupEnabledInstanceCount(enabledInstanceCount) {
  const targetEnabledCount = clampManagedInstanceCount(enabledInstanceCount);
  const configuredCount = getManagedInstanceInventoryCount();
  const instances = state?.instances || [];
  for (let index = 0; index < configuredCount; index++) {
    if (index >= targetEnabledCount) continue;
    const instance = instances.find(item => Number(item.index) === index);
    if (instance && !isInstanceEnabled(instance)) {
      await postJson(`/api/instances/${index}/enabled`, { enabled: true });
    }
  }

  for (let index = configuredCount - 1; index >= 0; index--) {
    if (index < targetEnabledCount) continue;
    const instance = (state?.instances || []).find(item => Number(item.index) === index);
    if (instance && isInstanceEnabled(instance)) {
      await postJson(`/api/instances/${index}/enabled`, { enabled: false });
    }
  }
}

async function runSetupWizardBaselineCheck() {
  await persistSettings();
  await postJson('/api/instances/baseline/check');
  showToast(`Baseline ${state.instanceBaseline?.status || 'checked'}`);
}

async function runSetupWizardAddInstance() {
  const settings = buildCurrentSettingsPreview();
  const configuredCount = clampManagedInstanceCount(settings.instanceCount);
  const createdCount = getCreatedInstanceCount(settings);
  const missingCount = Math.max(0, configuredCount - createdCount);
  const nextCount = missingCount > 0 ? configuredCount : configuredCount + 1;
  if (nextCount > maxManagedInstanceCount) {
    throw new Error(`Managed instances are limited to ${maxManagedInstanceCount}.`);
  }

  setValue('instanceCount', nextCount);
  await persistSettings();
  await postJson('/api/instances/provision', {
    instanceCount: nextCount,
    createMissingOnly: true,
    overwriteExisting: false,
    copyExistingSongs: false
  });
  showToast(state.instanceProvision?.summary || 'Managed instance created');
}

async function setSetupInstanceEnabled(index, enabled) {
  pendingSetupEnabledInstanceCount = null;
  const instance = (state.instances || []).find(item => Number(item.index) === Number(index));
  await postJson(`/api/instances/${Number(index)}/enabled`, { enabled });
  showToast(`${instance?.name || `BSARR I-${Number(index) + 1}`} ${enabled ? 'enabled' : 'disabled'}`);
}

async function removeSetupInstance(index) {
  const instance = (state.instances || []).find(item => Number(item.index) === Number(index));
  const name = instance?.name || `BSARR I-${Number(index) + 1}`;
  if (!window.confirm(`Remove ${name} from the managed recorder workspace? This deletes only that managed copy, not your real Beat Saber install.`)) {
    return;
  }

  await persistSettings();
  await postJson(`/api/instances/${Number(index)}/remove`);
  showToast(`${name} removed`);
}

async function runSetupWizardLaunchOnly() {
  await persistSettings();
  await postJson('/api/instances/launch');
  showToast('Game launch requested');
}

async function runSetupWizardVerify() {
  await persistSettings();
  await postJson('/api/instances/baseline/check');
  await postJson('/api/instances/launch');
  showToast('Launch requested; verification is updating');
}

async function postJson(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await response.text());
  state = await response.json();
  render();
}

function buildSettingsRequest() {
  const instanceCount = getManagedInstanceInventoryCount();

  return {
    recordingOutputDirectory: getTextOrSetting('recordingOutputDirectory', 'recordingOutputDirectory'),
    instanceCount,
    maxConcurrentRecordings: instanceCount,
    requireAllWorkersReady: document.getElementById('requireAllWorkersReady').checked,
    requireMatchingInstanceBaseline: document.getElementById('requireMatchingInstanceBaseline').checked,
    sharedCustomLevelsDirectory: getTextOrSetting('sharedCustomLevelsDirectory', 'sharedCustomLevelsDirectory'),
    sharedCustomWipLevelsDirectory: getTextOrSetting('sharedCustomWipLevelsDirectory', 'sharedCustomWipLevelsDirectory'),
    shareCustomSabers: getCheckedOrSetting('shareCustomSabers', 'shareCustomSabers'),
    sharedCustomSabersDirectory: getTextOrSetting('sharedCustomSabersDirectory', 'sharedCustomSabersDirectory'),
    shareCustomNotes: getCheckedOrSetting('shareCustomNotes', 'shareCustomNotes'),
    sharedCustomNotesDirectory: getTextOrSetting('sharedCustomNotesDirectory', 'sharedCustomNotesDirectory'),
    shareCustomPlatforms: getCheckedOrSetting('shareCustomPlatforms', 'shareCustomPlatforms'),
    sharedCustomPlatformsDirectory: getTextOrSetting('sharedCustomPlatformsDirectory', 'sharedCustomPlatformsDirectory'),
    shareCustomAvatars: getCheckedOrSetting('shareCustomAvatars', 'shareCustomAvatars'),
    sharedCustomAvatarsDirectory: getTextOrSetting('sharedCustomAvatarsDirectory', 'sharedCustomAvatarsDirectory'),
    shareCustomWalls: getCheckedOrSetting('shareCustomWalls', 'shareCustomWalls'),
    sharedCustomWallsDirectory: getTextOrSetting('sharedCustomWallsDirectory', 'sharedCustomWallsDirectory'),
    shareCustomBombs: getCheckedOrSetting('shareCustomBombs', 'shareCustomBombs'),
    sharedCustomBombsDirectory: getTextOrSetting('sharedCustomBombsDirectory', 'sharedCustomBombsDirectory'),
    targetFps: getNumber('targetFps'),
    captureWidth: getNumber('captureWidth'),
    captureHeight: getNumber('captureHeight'),
    videoBitrateKbps: getNumber('videoBitrateKbps'),
    outputFormat: getText('outputFormat'),
    monitorIndex: getNumber('monitorIndex'),
    encoder: getText('encoder'),
    qualityMode: getText('qualityMode'),
    audioMode: getText('audioMode'),
    requireAudioForRun: document.getElementById('requireAudioForRun').checked,
    audioBitrateKbps: getNumber('audioBitrateKbps'),
    audioSampleRate: getNumber('audioSampleRate'),
    audioChannels: 2,
    audioLevelMode: getText('audioLevelMode'),
    audioTargetLevelDb: getNumber('audioTargetLevelDb'),
    beatSaberInstancesRoot: getTextOrSetting('beatSaberInstancesRoot', 'beatSaberInstancesRoot'),
    beatSaberInstanceNamePrefix: managedInstanceNamePrefix,
    beatSaberLaunchPreset: getText('beatSaberLaunchPreset'),
    beatSaberLaunchArguments: getText('beatSaberLaunchArguments'),
    manageDisplayScale: document.getElementById('manageDisplayScale').checked,
    recordingDisplayScalePercent: getNumber('recordingDisplayScalePercent'),
    restoreDisplayScalePercent: getNumberOrSetting(
      'restoreDisplayScalePercent',
      'restoreDisplayScalePercent',
      150),
    hideTaskbarDuringRun: document.getElementById('hideTaskbarDuringRun').checked,
    delayBetweenRecordingsSeconds: getConfiguredInterReplayGapSeconds(),
    gamePresentation: {
      noHud: document.getElementById('noHud').checked,
      loadPlayerEnvironment: document.getElementById('loadPlayerEnvironment').checked,
      loadPlayerJumpDistance: document.getElementById('loadPlayerJumpDistance').checked,
      ignoreModifiers: document.getElementById('ignoreModifiers').checked,
      showHead: document.getElementById('showHead').checked,
      showLeftSaber: document.getElementById('showLeftSaber').checked,
      showRightSaber: document.getElementById('showRightSaber').checked,
      showWatermark: document.getElementById('showWatermark').checked,
      showTimelineMisses: getGameCheckedOrSetting('showTimelineMisses', 'showTimelineMisses', true),
      showTimelineBombs: getGameCheckedOrSetting('showTimelineBombs', 'showTimelineBombs', true),
      showTimelinePauses: getGameCheckedOrSetting('showTimelinePauses', 'showTimelinePauses', true),
      sfxVolume: getNumber('sfxVolume'),
      noTextsAndHuds: document.getElementById('noTextsAndHuds').checked,
      advancedHud: document.getElementById('noTextsAndHuds').checked ? false : document.getElementById('advancedHud').checked,
      reduceDebris: document.getElementById('reduceDebris').checked,
      noFailEffects: getGameCheckedOrSetting('noFailEffects', 'noFailEffects', false),
      saberTrailIntensity: getNumber('saberTrailIntensity'),
      noteJumpDurationType: getText('noteJumpDurationType'),
      noteJumpFixedDuration: getNumber('noteJumpFixedDuration'),
      noteJumpStartBeatOffset: getNumber('noteJumpStartBeatOffset'),
      hideNoteSpawnEffect: document.getElementById('hideNoteSpawnEffect').checked,
      adaptiveSfx: document.getElementById('adaptiveSfx').checked,
      arcsHapticFeedback: getGameCheckedOrSetting('arcsHapticFeedback', 'arcsHapticFeedback', true),
      arcVisibility: getText('arcVisibility'),
      environmentEffectsFilterDefaultPreset: getText('environmentEffectsFilterDefaultPreset'),
      environmentEffectsFilterExpertPlusPreset: getText('environmentEffectsFilterExpertPlusPreset'),
      headsetHapticIntensity: getNumberOrSetting('headsetHapticIntensity', 'headsetHapticIntensity', 0.7)
    }
  };
}

async function saveSettings() {
  await persistSettings();
  showToast('Settings saved');
}

ensureGameSettingsPlacement();

document.getElementById('saveSettings').addEventListener('click', () => runAction(saveSettings));
document.getElementById('setupWizardAdvanced')?.addEventListener('click', toggleAdvancedSettings);
document.getElementById('unsavedSettingsPrompt')?.addEventListener('click', () => runAction(saveSettings));
document.getElementById('setupWizardAddInstance')?.addEventListener('click', () => runAction(runSetupWizardAddInstance));
document.getElementById('setupWizardVerify')?.addEventListener('click', () => runAction(runSetupWizardVerify));
document.getElementById('setupWizardCheckBaseline')?.addEventListener('click', () => runAction(runSetupWizardBaselineCheck));
document.getElementById('setupWizardLaunchOnly')?.addEventListener('click', () => runAction(runSetupWizardLaunchOnly));
document.getElementById('showSetupAssistant')?.addEventListener('click', showSetupAssistant);

document.addEventListener('click', event => {
  const profileButton = event.target.closest('[data-setup-profile]');
  if (profileButton && !profileButton.disabled) {
    applySetupProfile(profileButton.dataset.setupProfile || '');
    return;
  }

  const createButton = event.target.closest('[data-setup-instance-create]');
  if (createButton && !createButton.disabled) {
    runAction(runSetupWizardAddInstance);
    return;
  }

  const enabledButton = event.target.closest('[data-setup-instance-enabled]');
  if (enabledButton && !enabledButton.disabled) {
    runAction(() => setSetupInstanceEnabled(
      enabledButton.dataset.setupInstanceEnabled,
      enabledButton.dataset.enabled === 'true'));
    return;
  }

  const removeButton = event.target.closest('[data-setup-instance-remove]');
  if (removeButton && !removeButton.disabled) {
    runAction(() => removeSetupInstance(removeButton.dataset.setupInstanceRemove));
  }
});

document.querySelectorAll('.setupWizardGrid input').forEach(element => {
  if (element.type === 'hidden') return;

  element.addEventListener('input', () => {
    markSettingsDirty();
    renderSetupAssistant();
  });
  element.addEventListener('change', () => {
    markSettingsDirty();
    renderSetupAssistant();
  });
});

document.querySelector('[data-settings-jump]')?.addEventListener('click', () => {
  activateView('settings');
});

document.querySelectorAll('[data-view-target]').forEach(button => {
  button.addEventListener('click', () => activateView(button.dataset.viewTarget || 'run'));
});

document.querySelectorAll('[data-settings-target]').forEach(button => {
  button.addEventListener('click', () => {
    const targetId = button.dataset.settingsTarget || 'setupAssistant';
    activateView('settings');
    requestAnimationFrame(() => {
      revealSettingsTarget(targetId);
      requestAnimationFrame(() => {
        document.getElementById(targetId)?.scrollIntoView({ block: 'start', behavior: 'smooth' });
      });
    });
  });
});

function activateView(view, updateHash = true) {
  const requestedView = view === 'setup' ? 'settings' : view;
  const nextView = ['run', 'settings', 'diagnostics'].includes(requestedView) ? requestedView : 'run';
  activeView = nextView;
  updateActiveView();
  if (updateHash) {
    history.replaceState(null, '', nextView === 'run' ? location.pathname : `#${nextView}`);
  }
}

function updateActiveView() {
  document.querySelectorAll('[data-view-panel]').forEach(panel => {
    panel.classList.toggle('active', panel.dataset.viewPanel === activeView);
  });
  document.querySelectorAll('[data-view-target]').forEach(button => {
    button.classList.toggle('active', button.dataset.viewTarget === activeView);
  });
}

function markSettingsDirty() {
  if (isRendering) return;
  settingsDirty = true;
  updateSettingsDirtyBadge();
}

function updateSettingsDirtyBadge() {
  const badge = document.getElementById('settingsDirtyBadge');
  const prompt = document.getElementById('unsavedSettingsPrompt');

  if (badge) {
    badge.textContent = settingsDirty ? 'Unsaved' : 'Saved';
    badge.classList.toggle('dirty', settingsDirty);
  }

  if (prompt) {
    prompt.hidden = !settingsDirty;
  }
}

document.getElementById('beatSaberLaunchPreset').addEventListener('change', event => {
  const id = event.target.value;
  if (id !== 'custom') applyLaunchPreset(id);
});

for (const fieldId of launchPresetFieldIds) {
  const element = document.getElementById(fieldId);
  if (!element) continue;

  element.addEventListener('input', markLaunchPresetCustom);
  element.addEventListener('change', markLaunchPresetCustom);
}

document.querySelectorAll('.settingsPanel input, .settingsPanel select, .settingsPanel textarea, .setupAdvancedPanel input, .setupAdvancedPanel select, .setupAdvancedPanel textarea, .setupGamePanel input, .setupGamePanel select, .setupGamePanel textarea').forEach(element => {
  element.addEventListener('input', markSettingsDirty);
  element.addEventListener('change', markSettingsDirty);
});
document.querySelectorAll('[data-game-value]').forEach(element => {
  element.addEventListener('input', () => updateGameValueLabel(element.id));
  element.addEventListener('change', () => updateGameValueLabel(element.id));
});
document.getElementById('noTextsAndHuds')?.addEventListener('change', updateAdvancedHudAvailability);
document.getElementById('manageDisplayScale')?.addEventListener('change', updateDisplayScaleAvailability);
document.getElementById('noteJumpDurationType')?.addEventListener('change', updateNoteJumpDurationAvailability);
document.getElementById('audioLevelMode').addEventListener('change', updateAudioLevelTargetConstraints);
document.getElementById('monitorIndex')?.addEventListener('change', () => {
  markSettingsDirty();
  renderSetupAssistant();
});

document.getElementById('resolutionPreset').addEventListener('change', event => {
  const value = event.target.value;
  if (value === 'custom') return;
  const [width, height] = value.split('x').map(Number);
  setValue('captureWidth', width);
  setValue('captureHeight', height);
  markLaunchPresetCustom();
});

document.getElementById('queueSearchInput')?.addEventListener('input', event => {
  queueSearchText = event.target.value;
  editingQueueId = null;
  renderQueue();
});

const replayFileInput = document.getElementById('replayFiles');
const queueReplayFileInput = document.getElementById('queueReplayFiles');
const replayDrop = document.querySelector('.fileDrop');
let replayDragDepth = 0;

replayFileInput.addEventListener('change', event => {
  selectReplayFiles(event.target.files);
});

queueReplayFileInput.addEventListener('change', event => {
  if (!selectReplayFiles(event.target.files)) {
    showToast('Choose .bsor files');
    return;
  }

  runAction(importSelectedReplays);
});

replayDrop.addEventListener('dragenter', event => {
  event.preventDefault();
  event.stopPropagation();
  replayDragDepth++;
  replayDrop.classList.add('dragOver');
  if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
});

replayDrop.addEventListener('dragover', event => {
  event.preventDefault();
  event.stopPropagation();
  replayDrop.classList.add('dragOver');
  if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
});

replayDrop.addEventListener('dragleave', event => {
  event.preventDefault();
  event.stopPropagation();
  replayDragDepth = Math.max(0, replayDragDepth - 1);
  if (replayDragDepth === 0) replayDrop.classList.remove('dragOver');
});

replayDrop.addEventListener('drop', event => {
  event.preventDefault();
  event.stopPropagation();
  replayDragDepth = 0;
  replayDrop.classList.remove('dragOver');
  if (!selectReplayFiles(event.dataTransfer?.files)) {
    showToast('Drop .bsor files');
    return;
  }

  runAction(importSelectedReplays);
});

function openQueueReplayPicker() {
  queueReplayFileInput.value = '';
  queueReplayFileInput.click();
}

function bindQueueImportDropTarget(target) {
  let dragDepth = 0;

  target.addEventListener('dragenter', event => {
    event.preventDefault();
    event.stopPropagation();
    dragDepth++;
    target.classList.add('dragOver');
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  });

  target.addEventListener('dragover', event => {
    event.preventDefault();
    event.stopPropagation();
    target.classList.add('dragOver');
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  });

  target.addEventListener('dragleave', event => {
    event.preventDefault();
    event.stopPropagation();
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) target.classList.remove('dragOver');
  });

  target.addEventListener('drop', event => {
    event.preventDefault();
    event.stopPropagation();
    dragDepth = 0;
    target.classList.remove('dragOver');
    if (!selectReplayFiles(event.dataTransfer?.files)) {
      showToast('Drop .bsor files');
      return;
    }

    runAction(importSelectedReplays);
  });
}

document.getElementById('addReplays').addEventListener('click', openQueueReplayPicker);

document.getElementById('uploadReplays').addEventListener('click', () => runAction(importSelectedReplays));

async function importSelectedReplays() {
  if (!selectedReplayFiles.length) {
    showToast('Choose replay files first');
    return;
  }

  const form = new FormData();
  for (const file of selectedReplayFiles) form.append('files', file);
  const response = await fetch('/api/replays/import', { method: 'POST', body: form });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  clearReplaySelection();
  render();
  showToast(`Imported ${result.count} replay${result.count === 1 ? '' : 's'}`);
}

function filterReplayFiles(files) {
  return files.filter(file => file.name.toLowerCase().endsWith('.bsor'));
}

function selectReplayFiles(files) {
  selectedReplayFiles = filterReplayFiles(Array.from(files || []));
  updateReplayFileSelection();
  return selectedReplayFiles.length;
}

function clearReplaySelection() {
  selectedReplayFiles = [];
  replayFileInput.value = '';
  queueReplayFileInput.value = '';
  updateReplayFileSelection();
}

function updateReplayFileSelection() {
  const selection = document.getElementById('fileSelection');
  if (!selectedReplayFiles.length) {
    selection.textContent = 'No files selected';
    return;
  }

  selection.textContent = selectedReplayFiles.length === 1
    ? selectedReplayFiles[0].name
    : `${selectedReplayFiles.length} files selected`;
}

document.getElementById('clearQueue').addEventListener('click', () => runAction(async () => {
  await postJson('/api/queue/clear');
  showToast('Queue cleared');
}));

document.getElementById('launchGames').addEventListener('click', () => runAction(async () => {
  await postJson('/api/instances/launch');
  showToast('Game launch requested');
}));

document.getElementById('decreaseActiveInstances')?.addEventListener('click', () => changeActiveInstanceCount(-1));
document.getElementById('increaseActiveInstances')?.addEventListener('click', () => changeActiveInstanceCount(1));

document.getElementById('forceStopGames').addEventListener('click', () => runAction(async () => {
  await postJson('/api/instances/force-stop');
  showToast('Force stop requested');
}));

document.getElementById('checkBaseline').addEventListener('click', () => runAction(async () => {
  await postJson('/api/instances/baseline/check');
  showToast(`Baseline ${state.instanceBaseline?.status || 'checked'}`);
}));

document.getElementById('startRun').addEventListener('click', () => runAction(async () => {
  await postJson('/api/run/start');
  showToast('Queue started');
}));

document.getElementById('stopRun').addEventListener('click', () => runAction(async () => {
  await postJson('/api/run/stop');
  showToast('Run stopped');
}));

document.getElementById('quitApp').addEventListener('click', () => runAction(async () => {
  if (!window.confirm('Quit the recorder and close all launched Beat Saber instances?')) {
    return;
  }

  await postJson('/api/quit');
  showToast('Quit requested');
}));

async function runAction(action) {
  try {
    await action();
  } catch (error) {
    showToast(readableError(error));
  }
}

async function launchInstance(index) {
  await runAction(async () => {
    await postJson(`/api/instances/${index}/launch`);
    const instance = state.instances.find(item => item.index === index);
    const status = instance?.gameLaunchStatus || 'requested';
    showToast(`${instance?.name || 'Instance'} launch ${status.toLowerCase()}`);
  });
}

async function quitInstance(index) {
  await runAction(async () => {
    const instance = state.instances.find(item => item.index === index);
    const name = instance?.name || `BSARR I-${Number(index) + 1}`;
    await postJson(`/api/instances/${index}/quit`);
    showToast(`${name} quit requested`);
  });
}

async function changeActiveInstanceCount(delta) {
  await runAction(async () => {
    const target = delta < 0 ? getHighestEnabledInstance() : getNextDisabledInstance();
    if (!target) return;

    const enabled = delta > 0;
    await postJson(`/api/instances/${Number(target.index)}/enabled`, { enabled });
    showToast(`${target.name || `BSARR I-${Number(target.index) + 1}`} ${enabled ? 'enabled' : 'disabled'}`);
  });
}

function formatSeconds(value) {
  const total = Math.max(0, Math.round(value || 0));
  const minutes = Math.floor(total / 60);
  const seconds = String(total % 60).padStart(2, '0');
  return `${minutes}:${seconds}`;
}

function formatHeartbeat(value) {
  if (!value) return 'No heartbeat';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'No heartbeat';
  const elapsedSeconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
  if (elapsedSeconds < 2) return 'Just now';
  if (elapsedSeconds < 60) return `${elapsedSeconds}s ago`;
  return `${Math.round(elapsedSeconds / 60)}m ago`;
}

function formatClock(date) {
  return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });
}

function formatBitrate(value) {
  const kbps = Math.max(0, Number(value) || 0);
  if (!kbps) return 'auto bitrate';
  if (kbps < 1000) return `${kbps} kbps`;
  const mbps = kbps / 1000;
  return `${Number.isInteger(mbps) ? mbps.toFixed(0) : mbps.toFixed(1)} Mbps`;
}

function formatContainer(value) {
  const text = String(value ?? 'mkv').trim().toUpperCase();
  return text === 'MP4' ? 'MP4' : 'MKV';
}

function formatMonitorSummary(index) {
  const normalized = Number(index);
  const display = (displayInfo?.displays || []).find(item => Number(item.index) === normalized);
  if (!display) return `M${normalized}`;

  const parts = [`M${normalized}`];
  if (display.friendlyName) parts.push(display.friendlyName);
  if (display.width && display.height) parts.push(`${display.width}x${display.height}`);
  return parts.join(' ');
}

function formatAudioMode(settings) {
  const mode = settings?.audioMode || 'ProcessLoopback';
  if (sameStatus(mode, 'ProcessLoopback')) {
    const levelMode = settings.audioLevelMode || 'Off';
    const levelUnit = sameStatus(levelMode, 'Loudness') ? ' LUFS' : ' dB';
    const level = sameStatus(levelMode, 'Off')
      ? ''
      : ` ${formatSignedNumber(settings.audioTargetLevelDb ?? -12)}${levelUnit}`;
    return 'Loopback' + level;
  }

  return settings?.requireAudioForRun === false ? 'Audio off' : 'Audio off required';
}

function formatInstanceAudioMode(audioEnabled) {
  const settings = state?.settings || {};
  const mode = settings.audioMode || 'ProcessLoopback';
  return audioEnabled && settings.requireAudioForRun !== false && sameStatus(mode, 'ProcessLoopback')
    ? 'Process Loopback'
    : 'Disabled';
}

function formatGamePresentationSync(instance) {
  if (!instance?.workerId) return 'Game settings pending';

  const targetVersion = Number(state?.settings?.gamePresentationSettingsVersion) || 0;
  const appliedVersion = Number(instance.appliedGamePresentationSettingsVersion) || 0;
  const status = instance.gamePresentationSyncStatus || (appliedVersion > 0 ? 'Applied' : 'Pending');
  if (sameStatus(status, 'Failed')) {
    return `Game settings failed: ${instance.gamePresentationSyncError || 'unknown error'}`;
  }

  if (targetVersion > 0 && appliedVersion === targetVersion) {
    return `Game settings v${appliedVersion}`;
  }

  return appliedVersion > 0 && targetVersion > 0
    ? `Game settings ${status} v${appliedVersion}/${targetVersion}`
    : `Game settings ${status}`;
}

function formatSignedNumber(value) {
  const number = Number(value) || 0;
  return number > 0 ? `+${number}` : String(number);
}

function formatNumber(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) return '0';
  return number.toFixed(3).replace(/\.?0+$/, '');
}

function shortId(value) {
  const text = String(value ?? '');
  return text.length <= 16 ? text : `${text.slice(0, 16)}...`;
}

function shortFileText(value) {
  const text = String(value ?? '');
  if (!text) return '-';
  const fileName = text.split(/[\\/]/).filter(Boolean).pop() || text;
  return fileName.length <= 24 ? fileName : `${fileName.slice(0, 21)}...`;
}

function initials(value) {
  const words = String(value ?? 'Replay')
    .split(/[^a-z0-9]+/i)
    .filter(Boolean)
    .slice(0, 2);
  return (words.map(word => word[0]).join('') || 'R').toUpperCase();
}

function isPendingStatus(status) {
  const text = String(status ?? '').toLowerCase();
  return text === 'queued' || text === 'assigned' || text === 'recording';
}

function isActiveStatus(status) {
  const text = String(status ?? '').toLowerCase();
  return text === 'assigned' || text === 'recording';
}

function sameStatus(left, right) {
  return String(left ?? '').toLowerCase() === String(right ?? '').toLowerCase();
}

function buildRunIssue() {
  const failedReplay = (state.queue || []).find(item => item.error);
  if (failedReplay) {
    const label = failedReplay.songName || failedReplay.fileName || `Replay #${failedReplay.sequenceNumber}`;
    return `#${failedReplay.sequenceNumber} ${label}: ${failedReplay.error}`;
  }

  const failedInstance = (state.instances || []).find(instance => instance.gameLaunchError || instance.audioRoutingError);
  if (failedInstance) {
    const reason = failedInstance.gameLaunchError || failedInstance.audioRoutingError;
    return `${failedInstance.name || `Instance ${Number(failedInstance.index) + 1}`}: ${reason}`;
  }

  const runStatus = String(state.run?.status || '');
  const reason = state.run?.cancellationReason;
  if (reason && /error|fail|stop/i.test(runStatus)) {
    return reason;
  }

  if (/error|fail/i.test(runStatus)) {
    return 'Run stopped before the queue reported a replay-level reason.';
  }

  return '';
}

function hasRunIssue() {
  if ((state.queue || []).some(item => item.error)) return true;
  if ((state.instances || []).some(instance => instance.gameLaunchError || instance.audioRoutingError)) return true;
  return /error|fail/i.test(String(state.run?.status || ''));
}

function statusClass(status) {
  const normalized = String(status ?? 'idle')
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
  return `status-${normalized || 'idle'}`;
}

function readableError(error) {
  const text = error instanceof Error ? error.message : String(error);
  try {
    const parsed = JSON.parse(text);
    return parsed.error || text;
  } catch {
    return text;
  }
}

function showToast(message) {
  const toast = document.getElementById('toast');
  toast.textContent = message;
  toast.classList.add('visible');
  clearTimeout(toastTimeout);
  toastTimeout = setTimeout(() => toast.classList.remove('visible'), 3500);
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

activateView(location.hash.replace('#', ''), false);

loadState().catch(error => {
  document.getElementById('runStatus').textContent = readableError(error);
});
loadDisplayInfo().catch(() => {
  displayInfo = { displays: [] };
  renderMonitorOptions();
});

setInterval(() => {
  if (settingsDirty) return;
  if (editingQueueId) return;
  const activeElement = document.activeElement;
  if (activeElement && (activeElement.tagName === 'INPUT' || activeElement.tagName === 'SELECT' || activeElement.tagName === 'TEXTAREA')) return;
  loadState().catch(() => {});
}, 2500);

setInterval(() => {
  if (shouldTickRunPlanPlayhead()) updateRunPlanPlayhead();
}, 500);

function shouldTickRunPlanPlayhead() {
  if (!state?.run?.isRunning && !state?.run?.cancellationRequested) return false;
  return (state.queue || []).some(item => isActiveStatus(item.status) && item.assignedAtUtc);
}
