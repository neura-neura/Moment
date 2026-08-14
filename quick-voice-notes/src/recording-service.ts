import { normalizePath, TFile, type App, type FileSystemAdapter } from "obsidian";
import { ensureVaultFolder, extensionForMimeType, formatFileStem, sanitizeVaultFolder, uniqueVaultPath } from "./path-utils";
import type { CompletedRecording, QuickVoiceNotesSettings, SavedRecording } from "./types";

export class RecordingService {
  public constructor(
    private readonly app: App,
    private readonly getSettings: () => QuickVoiceNotesSettings
  ) {}

  public async save(recording: CompletedRecording): Promise<SavedRecording> {
    const settings = this.getSettings();
    const folder = sanitizeVaultFolder(settings.audioFolder, "Voice Notes");
    await ensureVaultFolder(this.app.vault, folder);
    const stem = formatFileStem(recording.startedAt);
    const extension = extensionForMimeType(recording.mimeType);
    const vaultPath = uniqueVaultPath(
      (path) => this.app.vault.getAbstractFileByPath(path) !== null,
      folder,
      stem,
      extension
    );
    const arrayBuffer = await recording.blob.arrayBuffer();
    try {
      await this.app.vault.createBinary(vaultPath, arrayBuffer);
    } catch (error) {
      throw new Error(`The recording could not be saved to "${vaultPath}": ${String(error)}`);
    }
    const file = this.app.vault.getAbstractFileByPath(vaultPath);
    if (!(file instanceof TFile)) throw new Error(`The recording was written but Obsidian could not resolve "${vaultPath}".`);
    const adapter = this.app.vault.adapter as FileSystemAdapter;
    const absolutePath = adapter.getFullPath(normalizePath(vaultPath));
    return { vaultPath, absolutePath, startedAt: recording.startedAt, mimeType: recording.mimeType };
  }
}
