const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('replayRecorder', {
  minimizeWindow: () => ipcRenderer.invoke('replay-recorder:minimize-window')
});
