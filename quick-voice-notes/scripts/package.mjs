import { cp, mkdir, rm } from "node:fs/promises";
import { join, resolve } from "node:path";
import { execFileSync } from "node:child_process";

const root = resolve(import.meta.dirname, "..");
const stagingRoot = join(root, "dist", "quick-voice-notes");
const zipPath = join(root, "dist", "quick-voice-notes.zip");

await rm(join(root, "dist"), { recursive: true, force: true });
await mkdir(stagingRoot, { recursive: true });
for (const file of ["main.js", "manifest.json", "styles.css"]) {
  await cp(join(root, file), join(stagingRoot, file));
}

if (process.platform === "win32") {
  execFileSync("powershell", [
    "-NoProfile",
    "-Command",
    `Compress-Archive -Path '${stagingRoot.replaceAll("'", "''")}' -DestinationPath '${zipPath.replaceAll("'", "''")}' -Force`
  ], { stdio: "inherit" });
} else {
  execFileSync("zip", ["-r", zipPath, "quick-voice-notes"], { cwd: join(root, "dist"), stdio: "inherit" });
}

console.log(`Created ${zipPath}`);
