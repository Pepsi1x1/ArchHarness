const { dialog, ipcMain } = require("electron");

/**
 * IPC handler registry.
 * Open/Closed Principle: new handlers are added via the registry
 * without modifying existing handler code.
 */
const handlers = [
  {
    channel: "archharness:pick-folder",
    handler: (windowProvider) => async (_, options) => {
      const window = windowProvider();
      const owner = window && !window.isDestroyed() ? window : null;
      const title = typeof options?.title === "string" && options.title.trim()
        ? options.title.trim()
        : "Select Project Folder";
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
