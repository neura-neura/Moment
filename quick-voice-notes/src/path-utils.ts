import { normalizePath, TFolder, type Vault } from "obsidian";

export function sanitizeVaultFolder(value: string, fallback: string): string {
  const normalized = normalizePath(value.trim().replace(/^[/\\]+|[/\\]+$/g, ""));
  if (normalized.length === 0 || normalized === ".") return fallback;
  if (normalized.split("/").some((part) => part === "..")) throw new Error("Vault folders cannot contain '..'.");
  return normalized;
}

export async function ensureVaultFolder(vault: Vault, folder: string): Promise<void> {
  const normalized = normalizePath(folder);
  if (normalized.length === 0 || normalized === ".") return;
  let current = "";
  for (const segment of normalized.split("/")) {
    current = normalizePath(current.length === 0 ? segment : `${current}/${segment}`);
    const existing = vault.getAbstractFileByPath(current);
    if (existing === null) {
      try {
        await vault.createFolder(current);
      } catch (error) {
        if (!(vault.getAbstractFileByPath(current) instanceof TFolder)) throw error;
      }
    } else if (!(existing instanceof TFolder)) {
      throw new Error(`Cannot create folder "${current}" because a file already uses that path.`);
    }
  }
}

export function formatFileStem(date: Date): string {
  const pad = (number: number, length = 2): string => String(number).padStart(length, "0");
  return [
    pad(date.getFullYear(), 4), "-", pad(date.getMonth() + 1), "-", pad(date.getDate()),
    " ", pad(date.getHours()), "-", pad(date.getMinutes()), "-", pad(date.getSeconds()),
    "-", pad(date.getMilliseconds(), 3)
  ].join("");
}

export function uniqueVaultPath(
  exists: (path: string) => boolean,
  folder: string,
  stem: string,
  extension: string
): string {
  for (let index = 0; index < 10_000; index += 1) {
    const suffix = index === 0 ? "" : `-${String(index)}`;
    const path = normalizePath(`${folder}/${stem}${suffix}.${extension}`);
    if (!exists(path)) return path;
  }
  throw new Error("Could not allocate a unique recording filename.");
}

export function extensionForMimeType(mimeType: string): string {
  if (mimeType.includes("ogg")) return "ogg";
  if (mimeType.includes("mp4")) return "m4a";
  return "webm";
}
