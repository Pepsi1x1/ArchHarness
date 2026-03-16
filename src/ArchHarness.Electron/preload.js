const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("archHarnessDesktop", {
  hostMode: "electron-local-web",
  selectFolder: () => ipcRenderer.invoke("archharness:pick-folder")
});