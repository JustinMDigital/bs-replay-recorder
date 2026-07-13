const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('replayRecorder', {
  minimizeWindow: recordingDisplay => ipcRenderer.invoke('replay-recorder:minimize-window', recordingDisplay)
});
