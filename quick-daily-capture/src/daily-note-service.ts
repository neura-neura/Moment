import { moment, normalizePath, TFile, type App } from "obsidian";
import type { Moment, MomentInput } from "moment";
import {
  appHasDailyNotesPluginLoaded,
  createDailyNote,
  getDailyNoteSettings
} from "obsidian-daily-notes-interface";
import { insertCaptureContent } from "./capture-insertion";
import { DailyNotesUnavailableError } from "./errors";
import { KeyedSerialQueue } from "./serial-queue";
import type {
  CaptureRequest,
  CaptureResult,
  QuickDailyCaptureSettings
} from "./types";

export class DailyNoteService {
  private readonly queue = new KeyedSerialQueue();
  private creationTail: Promise<unknown> = Promise.resolve();

  public constructor(
    private readonly app: App,
    private readonly getSettings: () => QuickDailyCaptureSettings
  ) {}

  public async insertCapture(request: CaptureRequest): Promise<CaptureResult> {
    const text = request.text.trim();
    if (text.length === 0) throw new Error("Type something before saving the capture.");
    const timestamp = parseTimestamp(request.timestamp);
    const file = await this.getOrCreateDailyNote(timestamp);
    const settings = this.getSettings();
    const timestampFormat = settings.timestampFormat.trim().length === 0 ? "HH:mm" : settings.timestampFormat;
    const heading = localMoment(timestamp).format(timestampFormat);

    await this.queue.run(file.path, async () => {
      await this.app.vault.process(file, (current) => insertCaptureContent(
        current,
        { heading, text },
        {
          location: settings.insertionLocation,
          targetHeading: settings.targetHeading,
          missingHeadingBehavior: settings.missingHeadingBehavior
        }
      ));
    });

    return { path: file.path, timestamp };
  }

  private getOrCreateDailyNote(timestamp: Date): Promise<TFile> {
    const task = this.creationTail.catch(() => undefined).then(async () => {
      if (!appHasDailyNotesPluginLoaded()) throw new DailyNotesUnavailableError();
      const settings = getDailyNoteSettings();
      const date = localMoment(timestamp);
      const dateFormat = settings.format?.trim();
      const filename = `${date.format(dateFormat === undefined || dateFormat.length === 0 ? "YYYY-MM-DD" : dateFormat)}.md`;
      const path = normalizePath([settings.folder?.trim(), filename].filter(Boolean).join("/"));
      const existing = this.app.vault.getAbstractFileByPath(path);
      if (existing instanceof TFile) return existing;
      if (existing !== null) throw new Error(`Cannot create today's Daily Note because "${path}" is not a file.`);

      const created = await createDailyNote(date);
      if (created instanceof TFile) return created;
      const raced = this.app.vault.getAbstractFileByPath(path);
      if (raced instanceof TFile) return raced;
      throw new Error(`Obsidian could not create today's Daily Note at "${path}".`);
    });
    this.creationTail = task;
    return task;
  }
}

const localMoment = moment as unknown as (input?: MomentInput) => Moment;

function parseTimestamp(value: CaptureRequest["timestamp"]): Date {
  if (value === undefined) return new Date();
  const date = value instanceof Date ? new Date(value.getTime()) : new Date(value);
  if (Number.isNaN(date.getTime())) throw new Error("The capture timestamp is invalid.");
  return date;
}
