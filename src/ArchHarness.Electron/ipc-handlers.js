const { BrowserWindow, dialog, ipcMain, powerSaveBlocker } = require("electron");

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
    handler: () => async (event, options) => {
      const sender = BrowserWindow.fromWebContents(event.sender);
      const owner = sender && !sender.isDestroyed() ? sender : null;
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
  },
  {
    channel: "archharness:open-wikidoc-screen",
    handler: ({ hostUrlProvider, windowManager }) => () => {
      const hostUrl = hostUrlProvider();
      if (!hostUrl) {
        return;
      }
      windowManager.createWikiDocWindow(`${hostUrl}/wikidoc.html`);
    }
  }
];

/**
 * Registers all IPC handlers.
 * @param {{ windowProvider: () => BrowserWindow | null, hostUrlProvider: () => string | null, windowManager: import('./window-manager').WindowManager }} deps
 */
function registerAll({ windowProvider, hostUrlProvider, windowManager }) {
  for (const entry of handlers) {
    ipcMain.handle(entry.channel, entry.handler({ windowProvider, hostUrlProvider, windowManager }));
  }
}

module.exports = { registerAll };
