let state = null;
let toastTimeout = null;
let settingsDirty = false;
let isRendering = false;
const managedInstanceNamePrefix = 'I-';

async function loadState() {
  const response = await fetch('/api/state');
  if (!response.ok) throw new Error(await response.text());
  state = await response.json();
  render();
}

function render() {
  if (!state) return;

  isRendering = true;
  try {
    const settings = state.settings;
    const report = state.songFolders || {};
    document.getElementById('fileSettingsPill').textContent = report.status || 'Unchecked';
    document.getElementById('fileSettingsPill').className = `statusPill ${statusClass(report.status || 'Unchecked')}`;
    document.getElementById('fileSettingsStatus').textContent =
      `${settings.instanceCount} instances | ${report.summary || 'Shared folders have not been checked.'}`;

    setValue('recordingOutputDirectory', settings.recordingOutputDirectory);
    setValue('beatSaberInstancesRoot', settings.beatSaberInstancesRoot);
    setValue('beatSaberInstanceNamePrefix', settings.beatSaberInstanceNamePrefix);
    setValue('sharedCustomLevelsDirectory', settings.sharedCustomLevelsDirectory);
    setValue('sharedCustomWipLevelsDirectory', settings.sharedCustomWipLevelsDirectory);
    setValue('sharedCustomSabersDirectory', settings.sharedCustomSabersDirectory);
    setValue('sharedCustomNotesDirectory', settings.sharedCustomNotesDirectory);
    setValue('sharedCustomPlatformsDirectory', settings.sharedCustomPlatformsDirectory);
    setValue('sharedCustomAvatarsDirectory', settings.sharedCustomAvatarsDirectory);
    setValue('sharedCustomWallsDirectory', settings.sharedCustomWallsDirectory);
    setValue('sharedCustomBombsDirectory', settings.sharedCustomBombsDirectory);
    setChecked('shareCustomSabers', settings.shareCustomSabers !== false);
    setChecked('shareCustomNotes', settings.shareCustomNotes !== false);
    setChecked('shareCustomPlatforms', settings.shareCustomPlatforms !== false);
    setChecked('shareCustomAvatars', settings.shareCustomAvatars !== false);
    setChecked('shareCustomWalls', settings.shareCustomWalls !== false);
    setChecked('shareCustomBombs', settings.shareCustomBombs !== false);
    updateShareVisibility();
    updateDirtyBadge();

    renderSharedFolders();
  } finally {
    isRendering = false;
  }
}

function renderSharedFolders() {
  const report = state.songFolders || {};
  const summary = document.getElementById('songFoldersSummary');
  const list = document.getElementById('songFoldersList');
  const status = report.status || 'Unchecked';
  const checked = report.checkedAtUtc ? `Checked ${formatClock(new Date(report.checkedAtUtc))}` : 'Not checked';
  summary.innerHTML = `
    <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
    <span class="baselineSummaryText">${escapeHtml(report.summary || 'Shared folders have not been checked.')}</span>
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
        <span class="cellTitle">${escapeHtml(item.instanceName || `Instance ${Number(item.instanceIndex) + 1}`)} - ${escapeHtml(item.folderKind || 'Folder')}</span>
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

function buildSettingsRequest() {
  const settings = state.settings;
  return {
    recordingOutputDirectory: getText('recordingOutputDirectory'),
    instanceCount: settings.instanceCount,
    maxConcurrentRecordings: settings.instanceCount,
    requireAllWorkersReady: settings.requireAllWorkersReady,
    requireMatchingInstanceBaseline: settings.requireMatchingInstanceBaseline,
    sharedCustomLevelsDirectory: getText('sharedCustomLevelsDirectory'),
    sharedCustomWipLevelsDirectory: getText('sharedCustomWipLevelsDirectory'),
    shareCustomSabers: document.getElementById('shareCustomSabers').checked,
    sharedCustomSabersDirectory: getText('sharedCustomSabersDirectory'),
    shareCustomNotes: document.getElementById('shareCustomNotes').checked,
    sharedCustomNotesDirectory: getText('sharedCustomNotesDirectory'),
    shareCustomPlatforms: document.getElementById('shareCustomPlatforms').checked,
    sharedCustomPlatformsDirectory: getText('sharedCustomPlatformsDirectory'),
    shareCustomAvatars: document.getElementById('shareCustomAvatars').checked,
    sharedCustomAvatarsDirectory: getText('sharedCustomAvatarsDirectory'),
    shareCustomWalls: document.getElementById('shareCustomWalls').checked,
    sharedCustomWallsDirectory: getText('sharedCustomWallsDirectory'),
    shareCustomBombs: document.getElementById('shareCustomBombs').checked,
    sharedCustomBombsDirectory: getText('sharedCustomBombsDirectory'),
    targetFps: settings.targetFps,
    captureWidth: settings.captureWidth,
    captureHeight: settings.captureHeight,
    videoBitrateKbps: settings.videoBitrateKbps,
    outputFormat: settings.outputFormat,
    monitorIndex: settings.monitorIndex,
    encoder: settings.encoder,
    qualityMode: settings.qualityMode,
    audioMode: settings.audioMode,
    requireAudioForRun: settings.requireAudioForRun,
    audioBitrateKbps: settings.audioBitrateKbps,
    audioSampleRate: settings.audioSampleRate,
    audioChannels: 2,
    audioLevelMode: settings.audioLevelMode,
    audioTargetLevelDb: settings.audioTargetLevelDb,
    beatSaberInstancesRoot: getText('beatSaberInstancesRoot'),
    beatSaberInstanceNamePrefix: managedInstanceNamePrefix,
    beatSaberLaunchPreset: settings.beatSaberLaunchPreset,
    beatSaberLaunchArguments: settings.beatSaberLaunchArguments,
    manageDisplayScale: settings.manageDisplayScale,
    recordingDisplayScalePercent: settings.recordingDisplayScalePercent,
    restoreDisplayScalePercent: settings.restoreDisplayScalePercent,
    hideTaskbarDuringRun: settings.hideTaskbarDuringRun
  };
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

async function saveSettings() {
  await postJson('/api/settings', buildSettingsRequest());
  settingsDirty = false;
  updateDirtyBadge();
  showToast('File settings saved');
}

async function runAction(action) {
  try {
    await action();
  } catch (error) {
    showToast(readableError(error));
  }
}

function setValue(id, value) {
  const element = document.getElementById(id);
  if (!element) return;
  element.value = value ?? '';
}

function setChecked(id, value) {
  const element = document.getElementById(id);
  if (!element) return;
  element.checked = Boolean(value);
}

function getText(id) {
  return document.getElementById(id)?.value ?? '';
}

function markSettingsDirty() {
  if (isRendering) return;
  settingsDirty = true;
  updateDirtyBadge();
}

function updateDirtyBadge() {
  const badge = document.getElementById('fileSettingsDirtyBadge');
  const prompt = document.getElementById('unsavedFileSettingsPrompt');

  if (badge) {
    badge.textContent = settingsDirty ? 'Unsaved' : 'Saved';
    badge.classList.toggle('dirty', settingsDirty);
  }

  if (prompt) {
    prompt.hidden = !settingsDirty;
  }
}

function updateShareVisibility() {
  document.querySelectorAll('[data-share-toggle]').forEach(toggle => {
    const targetId = toggle.dataset.shareToggle;
    const wrapper = document.querySelector(`[data-share-path="${targetId}"]`);
    if (!wrapper) return;
    wrapper.classList.toggle('isHidden', !toggle.checked);
  });
}

function formatClock(date) {
  return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });
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

document.getElementById('saveFileSettings').addEventListener('click', () => runAction(saveSettings));
document.getElementById('unsavedFileSettingsPrompt')?.addEventListener('click', () => runAction(saveSettings));
document.getElementById('checkSongFolders').addEventListener('click', () => runAction(async () => {
  await postJson('/api/song-folders/check');
  showToast(`Shared folders ${state.songFolders?.status || 'checked'}`);
}));
document.getElementById('repairSongFolders').addEventListener('click', () => runAction(async () => {
  await postJson('/api/song-folders/repair');
  showToast(`Shared folders ${state.songFolders?.status || 'repaired'}`);
}));

document.querySelectorAll('.fileSettingsGrid input').forEach(element => {
  element.addEventListener('input', markSettingsDirty);
  element.addEventListener('change', markSettingsDirty);
});

document.querySelectorAll('[data-share-toggle]').forEach(element => {
  element.addEventListener('change', updateShareVisibility);
});

loadState().catch(error => {
  document.getElementById('fileSettingsStatus').textContent = readableError(error);
});

setInterval(() => {
  if (settingsDirty) return;
  const activeElement = document.activeElement;
  if (activeElement && activeElement.tagName === 'INPUT') return;
  loadState().catch(() => {});
}, 2500);
