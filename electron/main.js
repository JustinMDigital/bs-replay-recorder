const { app, BrowserWindow, Menu, shell, dialog, ipcMain, screen } = require('electron');
const { resolveRecordingDisplay, shouldMinimizeForRecording } = require('./display-matching');
const childProcess = require('child_process');
const fs = require('fs');
const http = require('http');
const path = require('path');

const appRoot = path.resolve(__dirname, '..');
const repoRoot = readRepoRoot();
const logDirectory = path.join(repoRoot, 'ControlPanelWorkspace', 'Logs');
const launcherLogPath = path.join(logDirectory, 'electron-launcher.log');
const desktopHostExe = path.join(appRoot, 'runtime', 'desktop-host', 'BSAutoReplayRecorder.DesktopHost.exe');
const desktopHostProject = path.join(repoRoot, 'src', 'BSAutoReplayRecorder.DesktopHost', 'BSAutoReplayRecorder.DesktopHost.csproj');
const privateDotnetRoot = path.join(appRoot, 'runtime', 'dotnet');

let mainWindow = null;
let controlPanelUrl = 'http://127.0.0.1:5770';
let isShuttingDown = false;
let isCloseRequested = false;
let isQuitConfirmationOpen = false;
let startupStatusHistory = [];
let lastStatusPageKey = '';
let startupSetupMode = '';

function readRepoRoot() {
  const explicitRoot = readOption('--repo-root');
  if (explicitRoot) {
    return path.resolve(explicitRoot);
  }

  return appRoot;
}

function readOption(name) {
  for (let index = 0; index < process.argv.length - 1; index += 1) {
    if (String(process.argv[index]).toLowerCase() === name.toLowerCase()) {
      return process.argv[index + 1];
    }
  }

  return null;
}

function appendLauncherLog(message) {
  try {
    fs.mkdirSync(logDirectory, { recursive: true });
    fs.appendFileSync(launcherLogPath, `[${new Date().toISOString()}] ${message}\n`);
  } catch {
    // Logging must not keep the app from opening.
  }
}

function resolveDesktopHostCommand() {
  if (fs.existsSync(desktopHostExe)) {
    return {
      fileName: desktopHostExe,
      args: ['--repo-root', repoRoot, '--app-root', appRoot]
    };
  }

  if (fs.existsSync(desktopHostProject)) {
    return {
      fileName: 'dotnet',
      args: ['run', '--project', desktopHostProject, '--', '--repo-root', repoRoot]
    };
  }

  throw new Error(`Desktop host was not found. Expected ${desktopHostExe} or ${desktopHostProject}.`);
}

function parseHostUrl(output) {
  const lines = String(output || '').split(/\r?\n/).map(line => line.trim());
  for (let index = lines.length - 1; index >= 0; index -= 1) {
    const match = /^(READY|STOPPED)\s+(.+)$/i.exec(lines[index]);
    if (match) {
      return match[2].replace(/\/+$/, '');
    }
  }

  return null;
}

function outputHasReady(output) {
  return String(output || '').split(/\r?\n/).some(line => /^READY\s+/i.test(line.trim()));
}

function runDesktopHost(command, args = [], timeoutMs = 120000, options = {}) {
  return new Promise((resolve, reject) => {
    let host;
    try {
      host = resolveDesktopHostCommand();
    } catch (error) {
      reject(error);
      return;
    }

    const hostArgs = [...host.args, command, ...args];
    appendLauncherLog(`Desktop host: ${host.fileName} ${hostArgs.join(' ')}`);

    const hostEnvironment = { ...process.env };
    if (fs.existsSync(path.join(privateDotnetRoot, 'dotnet.exe'))) {
      hostEnvironment.DOTNET_ROOT = privateDotnetRoot;
      hostEnvironment.DOTNET_ROOT_X64 = privateDotnetRoot;
      hostEnvironment.DOTNET_MULTILEVEL_LOOKUP = '0';
    }

    const child = childProcess.spawn(host.fileName, hostArgs, {
      cwd: repoRoot,
      env: hostEnvironment,
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe']
    });

    let output = '';
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      try {
        child.kill();
      } catch {
        // Best effort; the reject below carries the actual problem.
      }
      reject(new Error(`Desktop host ${command} timed out.`));
    }, timeoutMs);

    const handleOutput = chunk => {
      const text = chunk.toString();
      output += text;
      appendLauncherLog(text.trimEnd());
      if (typeof options.onOutput === 'function') {
        options.onOutput(text);
      }
    };

    child.stdout.on('data', handleOutput);
    child.stderr.on('data', handleOutput);
    child.on('error', error => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      reject(error);
    });
    child.on('exit', code => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      const result = {
        code,
        output,
        url: parseHostUrl(output),
        ready: outputHasReady(output)
      };
      if (code === 0 || options.allowFailure) {
        resolve(result);
      } else {
        reject(new Error(`Desktop host ${command} exited with code ${code}.`));
      }
    });
  });
}

function requestUrl(url, options = {}) {
  const method = options.method || 'GET';
  const timeoutMs = options.timeoutMs || 2000;
  return new Promise((resolve, reject) => {
    const request = http.request(url, { method, timeout: timeoutMs }, response => {
      response.resume();
      response.on('end', () => {
        if (response.statusCode >= 200 && response.statusCode < 300) {
          resolve();
        } else {
          reject(new Error(`${method} ${url} returned ${response.statusCode}`));
        }
      });
    });

    request.on('timeout', () => {
      request.destroy(new Error(`${method} ${url} timed out`));
    });
    request.on('error', reject);
    request.end();
  });
}

async function waitForControlPanel(timeoutMs) {
  const startedAt = Date.now();
  while (Date.now() - startedAt < timeoutMs) {
    try {
      await requestUrl(`${controlPanelUrl}/api/state`, { timeoutMs: 1200 });
      return true;
    } catch {
      await new Promise(resolve => setTimeout(resolve, 500));
    }
  }

  return false;
}

function createDashboardUrl() {
  const url = new URL(controlPanelUrl);
  url.searchParams.set('v', String(Date.now()));
  if (startupSetupMode) {
    url.searchParams.set('setup', startupSetupMode);
  }
  return url.toString();
}

function readStartupWorkspacePath() {
  const settingsPath = path.join(repoRoot, 'settings.json');
  if (fs.existsSync(settingsPath)) {
    try {
      const settings = JSON.parse(fs.readFileSync(settingsPath, 'utf8').replace(/^\uFEFF/, ''));
      const configuredWorkspace = settings.workspaceDirectory || settings.workspace || settings.workspaceDir;
      if (typeof configuredWorkspace === 'string' && configuredWorkspace.trim()) {
        const workspace = configuredWorkspace.trim();
        return path.isAbsolute(workspace) ? workspace : path.join(repoRoot, workspace);
      }
    } catch (error) {
      appendLauncherLog(`Could not read settings workspace for setup detection: ${error.message}`);
    }
  }

  return path.join(repoRoot, 'ControlPanelWorkspace');
}

function detectStartupSetupMode() {
  const settingsPath = path.join(repoRoot, 'settings.json');
  if (!fs.existsSync(settingsPath)) {
    return 'first-run';
  }

  const statePath = path.join(readStartupWorkspacePath(), 'control-panel-state.json');
  if (!fs.existsSync(statePath)) {
    return 'repair';
  }

  return '';
}

async function startRecorderStack() {
  resetStartupStatus();
  startupSetupMode = detectStartupSetupMode();
  if (startupSetupMode) {
    appendLauncherLog(`Setup mode requested: ${startupSetupMode}`);
  }
  setStartupStatus('Checking whether the recorder stack is already running...');

  const startupProgress = createStartupOutputHandler();
  const status = await runDesktopHost('status', [], 30000, {
    allowFailure: true,
    onOutput: startupProgress
  });
  if (status.url) {
    controlPanelUrl = status.url;
  }

  if (status.ready) {
    appendLauncherLog(`Control panel already running at ${controlPanelUrl}`);
    setStartupStatus(startupSetupMode
      ? 'Control panel is already running. Opening setup...'
      : 'Control panel is already running. Opening dashboard...');
    return;
  }

  setStartupStatus('Starting recorder services...');
  appendLauncherLog('Starting recorder stack through desktop host.');
  const started = await runDesktopHost('start', [], 180000, {
    onOutput: startupProgress
  });
  if (started.url) {
    controlPanelUrl = started.url;
  }

  setStartupStatus(`Verifying control panel at ${controlPanelUrl}...`);
  if (!started.ready || !await waitForControlPanel(30000)) {
    throw new Error(`Control panel did not become ready at ${controlPanelUrl}. Check ${launcherLogPath}.`);
  }

  setStartupStatus(startupSetupMode ? 'Opening setup...' : 'Opening dashboard...');
}

function resetStartupStatus() {
  startupStatusHistory = [];
  lastStatusPageKey = '';
}

function setStartupStatus(message) {
  const text = String(message || '').trim();
  if (!text) {
    return;
  }

  if (startupStatusHistory[startupStatusHistory.length - 1] !== text) {
    startupStatusHistory.push(text);
    startupStatusHistory = startupStatusHistory.slice(-7);
  }

  loadStatusPage(text, startupStatusHistory);
}

function createStartupOutputHandler() {
  let buffered = '';
  return text => {
    buffered += String(text || '');
    const lines = buffered.split(/\r?\n/);
    buffered = lines.pop() || '';
    for (const line of lines) {
      updateStartupStatusFromHostLine(line);
    }
  };
}

function updateStartupStatusFromHostLine(line) {
  const text = String(line || '').trim();
  if (!text) {
    return;
  }

  let match = /^Desktop host command:\s+status\b/i.exec(text);
  if (match) {
    setStartupStatus('Checking recorder service status...');
    return;
  }

  match = /^Desktop host command:\s+start\s+\((.+)\)/i.exec(text);
  if (match) {
    setStartupStatus(`Starting recorder services with ${match[1]}...`);
    return;
  }

  if (/^Checking control panel status at /i.test(text)) {
    setStartupStatus('Checking control panel status...');
    return;
  }

  if (/^Checking installed recorder setup/i.test(text)) {
    setStartupStatus('Checking installed recorder setup...');
    return;
  }

  if (/^Checking local settings/i.test(text)) {
    setStartupStatus('Checking local settings...');
    return;
  }

  if (/^Created local settings/i.test(text)) {
    setStartupStatus('Created settings from defaults...');
    return;
  }

  if (/^Preparing recorder workspace/i.test(text)) {
    setStartupStatus('Preparing recorder workspace...');
    return;
  }

  if (/^Using published runtime:/i.test(text)) {
    setStartupStatus('Using packaged recorder runtime...');
    return;
  }

  if (/^Using source tree runtime/i.test(text)) {
    setStartupStatus('Using source tree recorder runtime...');
    return;
  }

  match = /^Building (.+)\.\.\.$/i.exec(text);
  if (match) {
    setStartupStatus(`Building ${match[1]}...`);
    return;
  }

  match = /^Built (.+)\.$/i.exec(text);
  if (match) {
    setStartupStatus(`Built ${match[1]}.`);
    return;
  }

  if (/^Loading recorder host plan/i.test(text)) {
    setStartupStatus('Loading recorder host plan...');
    return;
  }

  if (/^Resolving FFmpeg tools/i.test(text)) {
    setStartupStatus('Resolving FFmpeg tools...');
    return;
  }

  if (/^Using FFmpeg:/i.test(text)) {
    setStartupStatus('Found FFmpeg.');
    return;
  }

  if (/^Using ffprobe:/i.test(text)) {
    setStartupStatus('Found ffprobe.');
    return;
  }

  match = /^Preparing recorder host (\d+)/i.exec(text);
  if (match) {
    setStartupStatus(`Preparing recorder host ${match[1]}...`);
    return;
  }

  match = /^Recorder host (\d+) is already running\./i.exec(text);
  if (match) {
    setStartupStatus(`Recorder host ${match[1]} is already running.`);
    return;
  }

  if (/^Preparing control panel service/i.test(text)) {
    setStartupStatus('Preparing control panel service...');
    return;
  }

  match = /^Starting (.+)\.\.\.$/i.exec(text);
  if (match) {
    setStartupStatus(`Starting ${match[1]}...`);
    return;
  }

  match = /^Started (.+), pid \d+\.$/i.exec(text);
  if (match) {
    setStartupStatus(`Started ${match[1]}.`);
    return;
  }

  match = /^Waiting for (.+) at /i.exec(text);
  if (match) {
    setStartupStatus(`Waiting for ${match[1]}...`);
    return;
  }

  match = /^(.+) is ready at /i.exec(text);
  if (match) {
    setStartupStatus(`${match[1]} is ready.`);
    return;
  }

  if (/^Control panel already running:/i.test(text)) {
    setStartupStatus('Control panel is already running.');
    return;
  }

  if (/^READY\s+/i.test(text)) {
    setStartupStatus('Recorder stack is ready.');
    return;
  }

  if (/^STOPPED\s+/i.test(text)) {
    setStartupStatus('Recorder stack is stopped.');
  }
}

async function stopRecorderStack() {
  if (isShuttingDown) {
    return;
  }

  isShuttingDown = true;
  appendLauncherLog('Stopping recorder stack from Electron shell.');
  await runDesktopHost('stop', ['--stop-games'], 60000);
}

async function confirmQuit() {
  if (isQuitConfirmationOpen) {
    return false;
  }

  isQuitConfirmationOpen = true;
  try {
    const options = {
      type: 'warning',
      buttons: ['Quit', 'Cancel'],
      defaultId: 1,
      cancelId: 1,
      noLink: true,
      title: 'Quit Replay Recorder?',
      message: 'Are you sure you want to quit?',
      detail: 'This will stop recorder hosts and close managed Beat Saber games.'
    };
    const result = mainWindow && !mainWindow.isDestroyed()
      ? await dialog.showMessageBox(mainWindow, options)
      : await dialog.showMessageBox(options);

    return result.response === 0;
  } finally {
    isQuitConfirmationOpen = false;
  }
}

function loadStatusPage(message, steps = [], options = {}) {
  if (!mainWindow || mainWindow.isDestroyed()) {
    return;
  }

  const phaseLabel = options.phaseLabel || 'Starting up';
  const statusPageKey = JSON.stringify({ message, steps, phaseLabel });
  if (statusPageKey === lastStatusPageKey) {
    return;
  }

  lastStatusPageKey = statusPageKey;
  mainWindow.loadURL(createLoadingHtml(message, steps, { phaseLabel })).catch(error => {
    if (!String(error.message || '').includes('ERR_ABORTED')) {
      appendLauncherLog(error.stack || error.message);
    }
  });
}

async function requestQuit(exitApp = false) {
  if (isCloseRequested) {
    return;
  }

  const confirmed = await confirmQuit();
  if (!confirmed) {
    return;
  }

  isCloseRequested = true;
  loadStatusPage(
    'Stopping recorder hosts and managed workers...',
    ['Stopping recorder hosts and managed workers...'],
    { phaseLabel: 'Shutting down' });
  try {
    await stopRecorderStack();
  } catch (error) {
    appendLauncherLog(error.stack || error.message);
  } finally {
    if (exitApp || !mainWindow || mainWindow.isDestroyed()) {
      app.exit(0);
    } else {
      mainWindow.destroy();
    }
  }
}

function createLoadingHtml(message, steps = [], options = {}) {
  const phaseLabel = options.phaseLabel || 'Starting up';
  const stepItems = steps.map((step, index) => {
    const activeClass = index === steps.length - 1 ? ' class="active"' : '';
    return `<li${activeClass}><span class="stepDot"></span><span>${escapeHtml(step)}</span></li>`;
  }).join('');

  return `data:text/html;charset=utf-8,${encodeURIComponent(`
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <title>Replay Recorder</title>
  <style>
    body {
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      background: #05090d;
      color: #edf3fb;
      font-family: "Segoe UI", system-ui, sans-serif;
    }
    main {
      width: min(620px, calc(100vw - 48px));
      border: 1px solid #263646;
      background: #0d1720;
      border-radius: 8px;
      padding: 28px;
      box-shadow: 0 18px 70px rgba(0, 0, 0, 0.34);
    }
    h1 {
      margin: 0 0 10px;
      font-size: 24px;
    }
    p {
      margin: 0;
      color: #91a0af;
      line-height: 1.5;
    }
    .kicker {
      margin: 0 0 8px;
      color: #55c7a4;
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 0;
      text-transform: uppercase;
    }
    .statusMessage {
      font-size: 16px;
      color: #d8e3ee;
    }
    .statusSteps {
      list-style: none;
      margin: 24px 0 0;
      padding: 0;
      display: grid;
      gap: 10px;
    }
    .statusSteps li {
      display: grid;
      grid-template-columns: 12px 1fr;
      align-items: start;
      gap: 10px;
      color: #91a0af;
      font-size: 13px;
      line-height: 1.35;
    }
    .statusSteps li.active {
      color: #edf3fb;
      font-weight: 600;
    }
    .stepDot {
      width: 8px;
      height: 8px;
      margin-top: 5px;
      border-radius: 999px;
      background: #314456;
      box-shadow: 0 0 0 3px rgba(49, 68, 86, 0.18);
    }
    .statusSteps li.active .stepDot {
      background: #55c7a4;
      box-shadow: 0 0 0 4px rgba(85, 199, 164, 0.16);
    }
  </style>
</head>
<body>
  <main>
    <p class="kicker">${escapeHtml(phaseLabel)}</p>
    <h1>Replay Recorder</h1>
    <p class="statusMessage">${escapeHtml(message)}</p>
    ${stepItems ? `<ol class="statusSteps">${stepItems}</ol>` : ''}
  </main>
</body>
</html>`)}`
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

async function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1440,
    height: 920,
    minWidth: 1180,
    minHeight: 720,
    title: 'Replay Recorder',
    icon: path.join(repoRoot, 'assets', 'logo.png'),
    backgroundColor: '#05090d',
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      preload: path.join(__dirname, 'preload.js')
    }
  });

  resetStartupStatus();
  setStartupStatus('Starting the local recorder stack...');
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  mainWindow.on('close', event => {
    event.preventDefault();
    if (isCloseRequested) {
      return;
    }

    requestQuit().catch(error => {
      appendLauncherLog(error.stack || error.message);
    });
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });

  try {
    await startRecorderStack();
    if (isCloseRequested || !mainWindow || mainWindow.isDestroyed()) {
      return;
    }

    await mainWindow.loadURL(createDashboardUrl());
  } catch (error) {
    appendLauncherLog(error.stack || error.message);
    if (mainWindow && !mainWindow.isDestroyed()) {
      await mainWindow.loadURL(createLoadingHtml(error.message));
    }
  }
}

Menu.setApplicationMenu(null);

ipcMain.handle('replay-recorder:minimize-window', (_event, recordingDisplayTarget) => {
  if (!mainWindow || mainWindow.isDestroyed()) {
    return { minimized: false, reason: 'window-unavailable' };
  }

  const currentDisplay = screen.getDisplayMatching(mainWindow.getBounds());
  const recordingDisplay = resolveRecordingDisplay(
    screen.getAllDisplays(),
    screen.getPrimaryDisplay(),
    recordingDisplayTarget);
  if (!shouldMinimizeForRecording(currentDisplay, recordingDisplay)) {
    return { minimized: false, reason: recordingDisplay ? 'different-display' : 'display-unresolved' };
  }

  mainWindow.minimize();
  return { minimized: true, reason: 'same-display' };
});

app.whenReady().then(createWindow);

app.on('before-quit', event => {
  if (!isShuttingDown && !isCloseRequested) {
    event.preventDefault();
    requestQuit(true).catch(error => {
      appendLauncherLog(error.stack || error.message);
    });
  }
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});
