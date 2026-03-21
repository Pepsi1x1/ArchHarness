import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const thisFilePath = fileURLToPath(import.meta.url);
const scriptsDirectory = path.dirname(thisFilePath);
const electronDirectory = path.resolve(scriptsDirectory, "..");

function runCommand(command, args) {
  const result = spawnSync(command, args, {
    cwd: electronDirectory,
    stdio: "inherit",
    shell: process.platform === "win32"
  });

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

runCommand("npm", ["run", "prepare:win-icon"]);
runCommand("npm", ["run", "bundle:web-host"]);
runCommand("npx", [
  "electron-builder",
  "--win",
  "dir",
  "-c.win.signAndEditExecutable=false"
]);
runCommand("npm", ["run", "stamp:win-exe-icon"]);
runCommand("npx", [
  "electron-builder",
  "--prepackaged",
  "dist/win-unpacked",
  "--win",
  "nsis",
  "zip",
  "-c.win.signAndEditExecutable=false"
]);