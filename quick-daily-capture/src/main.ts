import { Notice, Plugin } from "obsidian";
import { CaptureModal } from "./capture-modal";
import { DailyNoteService } from "./daily-note-service";
import { messageFromError } from "./errors";
import { ExternalTextInbox } from "./external-inbox";
import { QuickDailyCaptureSettingTab } from "./settings-tab";
import {
  DEFAULT_SETTINGS,
  type CaptureRequest,
  type QuickDailyCaptureAPI,
  type QuickDailyCaptureSettings
} from "./types";

export default class QuickDailyCapturePlugin extends Plugin {
  public override settings: QuickDailyCaptureSettings = { ...DEFAULT_SETTINGS };
  public readonly api: QuickDailyCaptureAPI = {
    version: 1,
    insertCapture: async (request: CaptureRequest) => this.dailyNotes.insertCapture(request)
  };
  private dailyNotes!: DailyNoteService;
  private externalInbox!: ExternalTextInbox;

  public override async onload(): Promise<void> {
    await this.loadSettings();
    this.dailyNotes = new DailyNoteService(this.app, () => this.settings);
    this.externalInbox = new ExternalTextInbox(this.app, () => this.api);
    this.registerEvent(this.app.vault.on("create", () => this.externalInbox.schedule()));
    this.registerEvent(this.app.vault.on("rename", () => this.externalInbox.schedule()));
    // The bridge writes jobs directly to disk, so Obsidian's vault events are
    // not guaranteed to fire. Poll briefly while the plugin is loaded so a
    // note captured before or during an Obsidian session is imported reliably.
    this.registerInterval(window.setInterval(() => this.externalInbox.schedule(), 2_000));
    this.addSettingTab(new QuickDailyCaptureSettingTab(this.app, this));
    this.addCommand({
      id: "new-entry",
      name: "New entry",
      hotkeys: [{ modifiers: [], key: "F14" }],
      callback: () => {
        const invokedAt = new Date();
        new CaptureModal(
          this.app,
          invokedAt,
          () => this.settings,
          async (request) => { await this.api.insertCapture(request); }
        ).open();
      }
    });
    window.setTimeout(() => this.externalInbox.schedule(), 0);
  }

  public async saveSettings(): Promise<void> {
    await this.saveData(this.settings);
  }

  private async loadSettings(): Promise<void> {
    try {
      const stored = await this.loadData() as Partial<QuickDailyCaptureSettings> | null;
      this.settings = { ...DEFAULT_SETTINGS, ...(stored ?? {}) };
    } catch (error) {
      console.error("Quick Daily Capture could not load its settings", error);
      new Notice(`Quick Daily Capture settings could not be loaded: ${messageFromError(error)}`);
      this.settings = { ...DEFAULT_SETTINGS };
    }
  }
}
