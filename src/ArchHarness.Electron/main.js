const { app, BrowserWindow, dialog, ipcMain, shell } = require("electron");
const path = require("node:path");
const { WebHostManager } = require("./web-host-manager");

const webHost = new WebHostManager();

let mainWindow = null;
let shutdownComplete = false;

ipcMain.handle("archharness:pick-folder", async () => {
  const owner = mainWindow && !mainWindow.isDestroyed() ? mainWindow : null;
  const result = await dialog.showOpenDialog(owner, {
    properties: ["openDirectory", "createDirectory"],
    title: "Select Project Folder"
  });

  if (result.canceled || result.filePaths.length === 0) {
    return null;
  }

  return result.filePaths[0];
});

function createMainWindow() {
  mainWindow = new BrowserWindow({
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
      preload: path.join(__dirname, "preload.js")
    }
  });

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    try {
      const parsed = new URL(url);
      if (parsed.protocol === "https:" || parsed.protocol === "http:") {
        void shell.openExternal(url);
      }
    } catch {
      // Ignore malformed URLs
    }
    return { action: "deny" };
  });

  void mainWindow.loadURL(webHost.hostUrl);

  mainWindow.on("closed", () => {
    mainWindow = null;
  });
}

app.on("window-all-closed", () => {
  app.quit();
});

app.on("before-quit", event => {
  if (shutdownComplete) {
    return;
  }

  event.preventDefault();

  void webHost.stop().finally(() => {
    shutdownComplete = true;
    app.exit(0);
  });
});

app.on("activate", () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createMainWindow();
  }
});

app.whenReady().then(async () => {
  try {
    await webHost.ensure();
    createMainWindow();
  } catch (error) {
    dialog.showErrorBox("ArchHarness failed to start", error instanceof Error ? error.message : String(error));
    app.quit();
  }
});
