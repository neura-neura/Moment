import { Notice, normalizePath, type App } from "obsidian";
import { messageFromError } from "./errors";
import type { CaptureRequest, QuickDailyCaptureAPI } from "./types";

export const EXTERNAL_INBOX_PATH = ".quick-capture/inbox";
const PROCESSED_PATH = ".quick-capture/processed";

interface ExternalTextJob {
  version: 1;
  type: "text";
  id: string;
  createdAt: string;
  timestamp: string;
  text: string;
  source?: string;
}

export class ExternalTextInbox {
  private drainTail: Promise<void> = Promise.resolve();
  private readonly retryAfter = new Map<string, number>();
  private drainScheduled = false;

  public constructor(
    private readonly app: App,
    private readonly getApi: () => QuickDailyCaptureAPI
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
    const adapter = this.app.vault.adapter;
    let listed: { files: string[] };
    try {
      listed = await adapter.list(EXTERNAL_INBOX_PATH);
    } catch {
      return;
    }
    const jobs = listed.files
      .filter((path) => path.split("/").pop()?.startsWith("text-") === true && path.endsWith(".json"))
      .sort((left, right) => left.localeCompare(right));
    for (const filePath of jobs) {
      if ((this.retryAfter.get(filePath) ?? 0) > Date.now()) continue;
      try {
        const job = parseTextJob(JSON.parse(await adapter.read(filePath)) as unknown);
        const request: CaptureRequest = {
          text: job.text,
          timestamp: job.timestamp,
          source: job.source ?? "quick-capture-bridge"
        };
        await this.getApi().insertCapture(request);
        await moveToProcessed(this.app, filePath);
        this.retryAfter.delete(filePath);
        new Notice(`Quick Daily Capture: imported external note from ${job.createdAt}.`);
      } catch (error) {
        this.retryAfter.set(filePath, Date.now() + 15_000);
        console.error("Quick Daily Capture could not import an external note", error);
        new Notice(`Quick Daily Capture: external note is still queued. ${messageFromError(error)}`, 10_000);
      }
    }
  }
}

function parseTextJob(value: unknown): ExternalTextJob {
  if (!isRecord(value) || value.version !== 1 || value.type !== "text") {
    throw new Error("The external note format is not supported.");
  }
  const text = typeof value.text === "string" ? value.text.trim() : "";
  const timestamp = typeof value.timestamp === "string" ? value.timestamp : "";
  const createdAt = typeof value.createdAt === "string" ? value.createdAt : "unknown time";
  if (text.length === 0) throw new Error("The external note is empty.");
  if (Number.isNaN(new Date(timestamp).getTime())) throw new Error("The external note timestamp is invalid.");
  return {
    version: 1,
    type: "text",
    id: typeof value.id === "string" ? value.id : "unknown",
    createdAt,
    timestamp,
    text,
    ...(typeof value.source === "string" ? { source: value.source } : {})
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
