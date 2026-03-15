const { spawn } = require("node:child_process");
const { dialog } = require("electron");
const fs = require("node:fs");
const path = require("node:path");

const HOST_URL = process.env.ARCHHARNESS_WEB_URL || "http://127.0.0.1:5057";
const HEALTH_URL = `${HOST_URL}/api/health`;
const REPO_ROOT = path.resolve(__dirname, "..", "..");
const WEB_PROJECT_PATH = path.join(REPO_ROOT, "src", "ArchHarness.Web", "ArchHarness.Web.csproj");
const DEV_PUBLISHED_WEB_HOST_DIRECTORY = path.join(__dirname, "build", "web-host");
const STARTUP_TIMEOUT_MS = 45000;
const HEALTH_POLL_MS = 500;

class WebHostManager {
  #process = null;
  #ownsProcess = false;
  #shuttingDown = false;

  get hostUrl() {
    return HOST_URL;
  }

  get isShuttingDown() {
    return this.#shuttingDown;
  }

  #canLaunchLocalWebHost() {
    return fs.existsSync(WEB_PROJECT_PATH);
  }

  #getPublishedWebHostDirectory() {
    const { app } = require("electron");
    return app.isPackaged
      ? path.join(process.resourcesPath, "web-host")
      : DEV_PUBLISHED_WEB_HOST_DIRECTORY;
  }

  #getPublishedWebHostExecutablePath() {
    const fileName = process.platform === "win32" ? "ArchHarness.Web.exe" : "ArchHarness.Web";
    const candidate = path.join(this.#getPublishedWebHostDirectory(), fileName);
    return fs.existsSync(candidate) ? candidate : null;
  }

  async #isHealthy() {
    try {
      const response = await fetch(HEALTH_URL, { cache: "no-store" });
      if (!response.ok) {
        return false;
      }

      const payload = await response.json();
      return payload?.healthy === true;
    } catch {
      return false;
    }
  }

  async #waitForReady() {
    const startedAt = Date.now();

    while ((Date.now() - startedAt) < STARTUP_TIMEOUT_MS) {
      if (await this.#isHealthy()) {
        return;
      }

      if (this.#process && this.#process.exitCode !== null) {
        throw new Error(`ArchHarness web host exited early with code ${this.#process.exitCode}.`);
      }

      await new Promise(resolve => setTimeout(resolve, HEALTH_POLL_MS));
    }

    throw new Error(`Timed out waiting for ArchHarness web host at ${HOST_URL}.`);
  }

  #start() {
    if (this.#process) {
      return;
    }

    const environment = {
      ...process.env,
      webHost__url: HOST_URL
    };

    const publishedExecutablePath = this.#getPublishedWebHostExecutablePath();

    if (publishedExecutablePath) {
      this.#process = spawn(publishedExecutablePath, [], {
        cwd: path.dirname(publishedExecutablePath),
        env: environment,
        stdio: ["ignore", "pipe", "pipe"]
      });
    } else {
      if (!this.#canLaunchLocalWebHost()) {
        return;
      }

      this.#process = spawn("dotnet", ["run", "--project", WEB_PROJECT_PATH, "--no-launch-profile"], {
        cwd: REPO_ROOT,
        env: environment,
        stdio: ["ignore", "pipe", "pipe"]
      });
    }

    this.#ownsProcess = true;

    this.#process.stdout.on("data", chunk => {
      process.stdout.write(`[archharness-web] ${chunk}`);
    });

    this.#process.stderr.on("data", chunk => {
      process.stderr.write(`[archharness-web] ${chunk}`);
    });

    this.#process.once("exit", code => {
      if (!this.#shuttingDown && code !== 0) {
        dialog.showErrorBox("ArchHarness Web Host Stopped", `The local web host exited with code ${code}.`);
      }

      this.#process = null;
      this.#ownsProcess = false;
    });
  }

  async ensure() {
    if (await this.#isHealthy()) {
      this.#ownsProcess = false;
      return;
    }

    if (!this.#getPublishedWebHostExecutablePath() && !this.#canLaunchLocalWebHost()) {
      throw new Error(`Unable to find a published web host or ${WEB_PROJECT_PATH}.`);
    }

    this.#start();
    await this.#waitForReady();
  }

  async stop() {
    if (!this.#process || !this.#ownsProcess) {
      return;
    }

    const processToStop = this.#process;
    this.#process = null;
    this.#ownsProcess = false;
    this.#shuttingDown = true;

    if (processToStop.exitCode === null) {
      processToStop.kill("SIGTERM");
      await new Promise(resolve => setTimeout(resolve, 750));
    }

    if (processToStop.exitCode === null) {
      processToStop.kill("SIGKILL");
    }
  }
}

module.exports = { WebHostManager };
