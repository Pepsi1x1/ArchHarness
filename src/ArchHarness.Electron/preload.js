const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("archHarnessDesktop", {
  hostMode: "electron-local-web",
  chrome: {
    platform: process.platform,
    titleBarOverlay: process.platform === "win32"
  },
  selectFolder: () => ipcRenderer.invoke("archharness:pick-folder")
});