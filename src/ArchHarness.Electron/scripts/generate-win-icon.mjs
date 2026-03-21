import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import pngToIco from "png-to-ico";
import sharp from "sharp";

const thisFilePath = fileURLToPath(import.meta.url);
const scriptsDirectory = path.dirname(thisFilePath);
const electronDirectory = path.resolve(scriptsDirectory, "..");
const iconsDirectory = path.join(electronDirectory, "assets", "icons");
const sourceIconPath = path.join(iconsDirectory, "icon-1024.png");
const targetIconPath = path.join(iconsDirectory, "icon.ico");
const iconSizes = [16, 24, 32, 48, 64, 128, 256];

await mkdir(iconsDirectory, { recursive: true });

const sourceBytes = await readFile(sourceIconPath);
const resizedIconBytes = await Promise.all(
	iconSizes.map(size =>
		sharp(sourceBytes)
			.resize(size, size, {
				fit: "contain"
			})
			.png()
			.toBuffer()
	)
);
const icoBytes = await pngToIco(resizedIconBytes);
await writeFile(targetIconPath, icoBytes);
