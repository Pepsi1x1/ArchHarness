const { dialog, ipcMain, powerSaveBlocker } = require("electron");

let activePowerSaveBlockerId = null;

const MAX_DIALOG_TITLE_LENGTH = 120;

function sanitizePickFolderOptions(options) {
  if (!options || typeof options !== "object" || Array.isArray(options)) {
    return { title: "Select Project Folder" };
  }

  const title = typeof options.title === "string" && options.title.trim()
    ? options.title.trim().slice(0, MAX_DIALOG_TITLE_LENGTH)
    : "Select Project Folder";

  return { title };
}

/**
 * IPC handler registry.
 * Open/Closed Principle: new handlers are added via the registry
 * without modifying existing handler code.
 */
const handlers = [
  {
    channel: "archharness:set-keep-awake",
    handler: () => (_, enabled) => {
      if (enabled && activePowerSaveBlockerId === null) {
        activePowerSaveBlockerId = powerSaveBlocker.start("prevent-display-sleep");
      } else if (!enabled && activePowerSaveBlockerId !== null) {
        powerSaveBlocker.stop(activePowerSaveBlockerId);
        activePowerSaveBlockerId = null;
      }
    }
  },
  {
    channel: "archharness:pick-folder",
    handler: (windowProvider) => async (_, options) => {
      const window = windowProvider();
      const owner = window && !window.isDestroyed() ? window : null;
      const { title } = sanitizePickFolderOptions(options);
      const result = await dialog.showOpenDialog(owner, {
        properties: ["openDirectory", "createDirectory"],
        title
      });

      if (result.canceled || result.filePaths.length === 0) {
        return null;
      }

      return result.filePaths[0];
    }
  }
];

/**
 * Registers all IPC handlers.
 * @param {{ windowProvider: () => BrowserWindow | null }} deps - injected dependencies
 */
function registerAll({ windowProvider }) {
  for (const entry of handlers) {
    ipcMain.handle(entry.channel, entry.handler(windowProvider));
  }
}

module.exports = { registerAll };
