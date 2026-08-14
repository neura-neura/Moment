import { normalizePath, TFolder, type Vault } from "obsidian";

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
