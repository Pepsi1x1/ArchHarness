const { dialog, ipcMain } = require("electron");

/**
 * IPC handler registry.
 * Open/Closed Principle: new handlers are added via the registry
 * without modifying existing handler code.
 */
const handlers = [
  {
    channel: "archharness:pick-folder",
    handler: (windowProvider) => async () => {
      const window = windowProvider();
      const owner = window && !window.isDestroyed() ? window : null;
      const result = await dialog.showOpenDialog(owner, {
        properties: ["openDirectory", "createDirectory"],
        title: "Select Project Folder"
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
