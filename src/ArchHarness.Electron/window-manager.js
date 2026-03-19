const { BrowserWindow, shell } = require("electron");
const path = require("node:path");

/**
 * Manages Electron BrowserWindow creation and lifecycle.
 * Single Responsibility: only window management concerns.
 */
class WindowManager {
  #preloadPath;
  #windowIconPath;
  #mainWindow = null;

  constructor({ preloadPath, windowIconPath }) {
    this.#preloadPath = preloadPath;
    this.#windowIconPath = windowIconPath;
  }

  get mainWindow() {
    return this.#mainWindow;
  }

  createMainWindow(loadUrl) {
    const isMac = process.platform === "darwin";
    const isWindows = process.platform === "win32";

    this.#mainWindow = new BrowserWindow({
      width: 1600,
      height: 1040,
      minWidth: 1200,
      minHeight: 800,
      autoHideMenuBar: true,
      backgroundColor: "#08121c",
      ...(isMac
        ? {
            titleBarStyle: "hiddenInset",
            trafficLightPosition: { x: 14, y: 13 }
          }
        : {}),
      ...(isWindows
        ? {
            titleBarStyle: "hidden",
            titleBarOverlay: {
              color: "#08121c",
              symbolColor: "#f4f5f7",
              height: 46
            }
          }
        : {}),
      title: "ArchHarness",
      icon: this.#windowIconPath,
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

    this.#mainWindow.loadURL(loadUrl);

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
