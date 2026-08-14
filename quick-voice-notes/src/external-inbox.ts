import { Notice, normalizePath, type App, type FileSystemAdapter } from "obsidian";
import { messageFromError } from "./errors";
import type QuickVoiceNotesPlugin from "./main";
import type { SavedRecording } from "./types";

export const EXTERNAL_INBOX_PATH = ".quick-capture/inbox";
const PROCESSED_PATH = ".quick-capture/processed";

interface ExternalVoiceJob {
  version: 1;
  type: "voice";
  id: string;
  createdAt: string;
  startedAt: string;
  audioPath: string;
  mimeType: string;
}

export class ExternalVoiceInbox {
  private drainTail: Promise<void> = Promise.resolve();
  private readonly retryAfter = new Map<string, number>();
  private drainScheduled = false;

  public constructor(
    private readonly app: App,
    private readonly plugin: QuickVoiceNotesPlugin
  ) {}

  public schedule(): void {
    if (this.drainScheduled) return;
    this.drainScheduled = true;
    this.drainTail = this.drainTail
      .catch(() => undefined)
      .then(async () => this.drain())
      .finally(() => { this.drainScheduled = false; });
  }

  private async drain(): Promise<void> {
    if (!this.plugin.settings.enableTranscription) return;
  const adapter = this.app.vault.adapter as FileSystemAdapter;
    let listed: { files: string[] };
    try {
      listed = await adapter.list(EXTERNAL_INBOX_PATH);
    } catch {
      return;
    }
    const jobs = listed.files
      .filter((path) => path.split("/").pop()?.startsWith("voice-") === true && path.endsWith(".json"))
      .sort((left, right) => left.localeCompare(right));
    for (const filePath of jobs) {
      if ((this.retryAfter.get(filePath) ?? 0) > Date.now()) continue;
      try {
        const job = parseVoiceJob(JSON.parse(await adapter.read(filePath)) as unknown);
        const audioPath = normalizePath(job.audioPath);
        if (!(await adapter.exists(audioPath))) throw new Error(`The queued audio file "${job.audioPath}" is missing.`);
        const saved: SavedRecording = {
          vaultPath: audioPath,
          absolutePath: adapter.getFullPath(audioPath),
          startedAt: new Date(job.startedAt),
          mimeType: job.mimeType
        };
        const blob = new Blob([await adapter.readBinary(audioPath)], { type: job.mimeType });
        const processed = await this.plugin.processExternalRecording(blob, saved);
        if (!processed) {
          this.retryAfter.set(filePath, Date.now() + 15_000);
          continue;
        }
        await moveToProcessed(this.app, filePath);
        this.retryAfter.delete(filePath);
        new Notice(`Quick Voice Notes: imported external recording from ${job.createdAt}.`);
      } catch (error) {
        this.retryAfter.set(filePath, Date.now() + 15_000);
        console.error("Quick Voice Notes could not import an external recording", error);
        new Notice(`Quick Voice Notes: external recording is still queued. ${messageFromError(error)}`, 10_000);
      }
    }
  }
}

function parseVoiceJob(value: unknown): ExternalVoiceJob {
  if (!isRecord(value) || value.version !== 1 || value.type !== "voice") {
    throw new Error("The external recording format is not supported.");
  }
  const audioPath = typeof value.audioPath === "string" ? normalizePath(value.audioPath) : "";
  const startedAt = typeof value.startedAt === "string" ? value.startedAt : "";
  const createdAt = typeof value.createdAt === "string" ? value.createdAt : "unknown time";
  const mimeType = typeof value.mimeType === "string" ? value.mimeType : "audio/wav";
  if (audioPath.length === 0 || audioPath.startsWith("../") || audioPath.includes("/../")) throw new Error("The queued audio path is invalid.");
  if (Number.isNaN(new Date(startedAt).getTime())) throw new Error("The queued recording timestamp is invalid.");
  return {
    version: 1,
    type: "voice",
    id: typeof value.id === "string" ? value.id : "unknown",
    createdAt,
    startedAt,
    audioPath,
    mimeType
  };
}

async function moveToProcessed(app: App, filePath: string): Promise<void> {
  await ensureQueueFolder(app, PROCESSED_PATH);
  const fileName = filePath.split("/").pop() ?? "job.json";
  const base = normalizePath(`${PROCESSED_PATH}/${fileName}`);
  let destination = base;
  let suffix = 1;
  while (await app.vault.adapter.exists(destination)) {
    destination = normalizePath(`${PROCESSED_PATH}/${fileName.replace(/\.json$/i, "")}-${String(suffix)}.json`);
    suffix += 1;
  }
  await app.vault.adapter.rename(filePath, destination);
}

async function ensureQueueFolder(app: App, folder: string): Promise<void> {
  const adapter = app.vault.adapter;
  let current = "";
  for (const segment of normalizePath(folder).split("/")) {
    current = normalizePath(current.length === 0 ? segment : `${current}/${segment}`);
    const existing = await adapter.stat(current);
    if (existing === null) await adapter.mkdir(current);
    else if (existing.type !== "folder") throw new Error(`Cannot create queue folder "${current}" because a file uses that path.`);
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
