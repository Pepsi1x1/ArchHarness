const { BrowserWindow, shell } = require("electron");
const path = require("node:path");

/**
 * Manages Electron BrowserWindow creation and lifecycle.
 * Single Responsibility: only window management concerns.
 */
class WindowManager {
  #preloadPath;
  #mainWindow = null;

  constructor({ preloadPath }) {
    this.#preloadPath = preloadPath;
  }

  get mainWindow() {
    return this.#mainWindow;
  }

  createMainWindow(loadUrl) {
    this.#mainWindow = new BrowserWindow({
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
        preload: this.#preloadPath
      }
    });

    this.#mainWindow.webContents.setWindowOpenHandler(({ url }) => {
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

    void this.#mainWindow.loadURL(loadUrl);

    this.#mainWindow.on("closed", () => {
      this.#mainWindow = null;
    });

    return this.#mainWindow;
  }

  hasWindows() {
    return BrowserWindow.getAllWindows().length > 0;
  }
}

module.exports = { WindowManager };
