const { BrowserWindow, shell } = require("electron");
const path = require("node:path");

/**
 * Manages Electron BrowserWindow creation and lifecycle.
 * Single Responsibility: only window management concerns.
 */
class WindowManager {
  constructor({ preloadPath }) {
    this._preloadPath = preloadPath;
    this._mainWindow = null;
  }

  get mainWindow() {
    return this._mainWindow;
  }

  createMainWindow(loadUrl) {
    this._mainWindow = new BrowserWindow({
      width: 1600,
      height: 1040,
      minWidth: 1200,
      minHeight: 800,
      autoHideMenuBar: true,
      backgroundColor: "#08121c",
      title: "ArchHarness",
      webPreferences: {
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: true,
        preload: this._preloadPath
      }
    });

    this._mainWindow.webContents.setWindowOpenHandler(({ url }) => {
      try {
        const parsed = new URL(url);
        if (parsed.protocol === "https:") {
          void shell.openExternal(url);
        }
      } catch {
        // Ignore malformed URLs
      }
      return { action: "deny" };
    });

    void this._mainWindow.loadURL(loadUrl);

    this._mainWindow.on("closed", () => {
      this._mainWindow = null;
    });

    return this._mainWindow;
  }

  hasWindows() {
    return BrowserWindow.getAllWindows().length > 0;
  }
}

module.exports = { WindowManager };
