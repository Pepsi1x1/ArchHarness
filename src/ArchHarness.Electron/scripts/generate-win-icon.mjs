import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import pngToIco from "png-to-ico";

const thisFilePath = fileURLToPath(import.meta.url);
const scriptsDirectory = path.dirname(thisFilePath);
const electronDirectory = path.resolve(scriptsDirectory, "..");
const iconsDirectory = path.join(electronDirectory, "assets", "icons");
const sourceIconPath = path.join(iconsDirectory, "icon-1024.png");
const targetIconPath = path.join(iconsDirectory, "icon.ico");

await mkdir(iconsDirectory, { recursive: true });

const sourceBytes = await readFile(sourceIconPath);
const icoBytes = await pngToIco(sourceBytes);
await writeFile(targetIconPath, icoBytes);
