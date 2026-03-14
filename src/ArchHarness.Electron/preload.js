const { contextBridge } = require("electron");

contextBridge.exposeInMainWorld("archHarnessDesktop", {
  hostMode: "electron-local-web"
});