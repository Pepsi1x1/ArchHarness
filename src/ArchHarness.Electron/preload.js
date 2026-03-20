const { contextBridge, ipcRenderer } = require("electron");

const MAX_DIALOG_TITLE_LENGTH = 120;

function sanitizePickFolderOptions(options) {
  if (!options || typeof options !== "object" || Array.isArray(options)) {
    return null;
  }

  const title = typeof options.title === "string" && options.title.trim()
    ? options.title.trim().slice(0, MAX_DIALOG_TITLE_LENGTH)
    : undefined;

  return title ? { title } : null;
}

contextBridge.exposeInMainWorld("archHarnessDesktop", {
  hostMode: "electron-local-web",
  chrome: {
    platform: process.platform,
    titleBarOverlay: process.platform === "win32"
  },
  selectFolder: (options) => ipcRenderer.invoke("archharness:pick-folder", sanitizePickFolderOptions(options))
});
