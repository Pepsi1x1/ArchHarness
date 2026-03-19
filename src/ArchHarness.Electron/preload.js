const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("archHarnessDesktop", {
  hostMode: "electron-local-web",
  chrome: {
    platform: process.platform,
    titleBarOverlay: process.platform === "win32"
  },
  selectFolder: (options) => ipcRenderer.invoke("archharness:pick-folder", options ?? null)
});