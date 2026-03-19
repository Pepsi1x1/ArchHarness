import { access, copyFile, rm, rename } from "node:fs/promises";
import { constants as fsConstants } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { rcedit } from "rcedit";

const thisFilePath = fileURLToPath(import.meta.url);
const scriptsDirectory = path.dirname(thisFilePath);
const electronDirectory = path.resolve(scriptsDirectory, "..");
const executablePath = path.join(electronDirectory, "dist", "win-unpacked", "ArchHarness.exe");
const iconPath = path.join(electronDirectory, "assets", "icons", "icon.ico");
const stagedExecutablePath = path.join(electronDirectory, "dist", "win-unpacked", "ArchHarness.staged.exe");

await access(executablePath, fsConstants.F_OK);
await access(iconPath, fsConstants.F_OK);

await copyFile(executablePath, stagedExecutablePath);

await rcedit(stagedExecutablePath, {
  icon: iconPath
});

await rm(executablePath, { force: true });
await rename(stagedExecutablePath, executablePath);

console.log(`Stamped Windows executable icon: ${executablePath}`);