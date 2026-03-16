import { mkdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const thisFilePath = fileURLToPath(import.meta.url);
const scriptsDirectory = path.dirname(thisFilePath);
const electronDirectory = path.resolve(scriptsDirectory, "..");
const repoRoot = path.resolve(electronDirectory, "..", "..");
const projectPath = path.join(repoRoot, "src", "ArchHarness.Web", "ArchHarness.Web.csproj");
const outputDirectory = path.join(electronDirectory, "build", "web-host");

mkdirSync(outputDirectory, { recursive: true });

const result = spawnSync("dotnet", [
  "publish",
  projectPath,
  "-c",
  "Release",
  "-o",
  outputDirectory,
  "--nologo"
], {
  cwd: repoRoot,
  stdio: "inherit"
});

if (result.status !== 0) {
  process.exit(result.status ?? 1);
}