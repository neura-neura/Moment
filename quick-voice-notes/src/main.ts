import { Notice, Plugin, type FileSystemAdapter } from "obsidian";
import { join } from "node:path";
import { messageFromError } from "./errors";
import { getQuickDailyCaptureAPI } from "./plugin-registry";
import { ExternalVoiceInbox } from "./external-inbox";
import { RecordingHud } from "./recording-hud";
import { RecordingService } from "./recording-service";
import { VoiceRecorder } from "./recorder";
import { SerialTaskQueue } from "./serial-task-queue";
import { QuickVoiceNotesSettingTab } from "./settings-tab";
import { TranscriptionService } from "./transcription-service";
import { DEFAULT_SETTINGS, type QuickVoiceNotesSettings } from "./types";
import { WhisperEngine } from "./whisper-engine";

export default class QuickVoiceNotesPlugin extends Plugin {
  public override settings: QuickVoiceNotesSettings = { ...DEFAULT_SETTINGS };
  public whisper!: WhisperEngine;
  private readonly recorder = new VoiceRecorder();
  private readonly hud = new RecordingHud();
  private recordings!: RecordingService;
  private transcriptions!: TranscriptionService;
  private externalInbox!: ExternalVoiceInbox;
  private readonly transcriptionQueue = new SerialTaskQueue();
  private readonly operationController = new AbortController();
  private unloaded = false;

  public get integrationAvailable(): boolean {
    return getQuickDailyCaptureAPI(this.app) !== null;
  }

  public get operationSignal(): AbortSignal {
    return this.operationController.signal;
  }

  public scheduleExternalInbox(): void {
    this.externalInbox.schedule();
  }

  public override async onload(): Promise<void> {
    await this.loadSettings();
    const adapter = this.app.vault.adapter as FileSystemAdapter;
    const dataDirectory = join(adapter.getBasePath(), this.app.vault.configDir, "plugins", this.manifest.id, "whisper");
    this.whisper = new WhisperEngine(dataDirectory);
    this.recordings = new RecordingService(this.app, () => this.settings);
    this.transcriptions = new TranscriptionService(this.app, () => this.settings);
    this.externalInbox = new ExternalVoiceInbox(this.app, this);
    this.registerEvent(this.app.vault.on("create", () => this.externalInbox.schedule()));
    this.registerEvent(this.app.vault.on("rename", () => this.externalInbox.schedule()));
    // External bridge files are created outside Obsidian's adapter. Polling
    // complements vault events and drains jobs even when Obsidian was already
    // open when the capture completed.
    this.registerInterval(window.setInterval(() => this.externalInbox.schedule(), 3_000));
    this.addSettingTab(new QuickVoiceNotesSettingTab(this.app, this));
    this.addCommand({
      id: "toggle-recording",
      name: "Toggle recording",
      hotkeys: [{ modifiers: [], key: "F13" }],
      callback: () => { void this.toggleRecording(); }
    });
    window.setTimeout(() => this.externalInbox.schedule(), 1_000);
  }

  public override onunload(): void {
    this.unloaded = true;
    this.operationController.abort();
    if (this.recorder.state === "recording") {
      void this.stopRecording();
    } else {
      this.recorder.cancel();
    }
    this.hud.hide();
  }

  public async saveSettings(): Promise<void> {
    await this.saveData(this.settings);
  }

  private async toggleRecording(): Promise<void> {
    if (this.recorder.state === "idle") {
      await this.startRecording();
    } else if (this.recorder.state === "recording") {
      await this.stopRecording();
    }
  }

  private async startRecording(): Promise<void> {
    const invokedAt = new Date();
    try {
      const startedAt = await this.recorder.start(this.settings.audioBitsPerSecond, invokedAt);
      if (this.unloaded) {
        this.recorder.cancel();
        return;
      }
      this.hud.show(startedAt, {
        stop: () => { void this.stopRecording(); },
        cancel: () => this.cancelRecording()
      });
    } catch (error) {
      new Notice(`Quick Voice Notes: ${messageFromError(error)}`, 10_000);
    }
  }

  private async stopRecording(): Promise<void> {
    if (this.recorder.state !== "recording") return;
    this.hud.setStopping();
    try {
      const completed = await this.recorder.stop();
      this.hud.hide();
      const saved = await this.recordings.save(completed);
      new Notice(`Voice note saved: ${saved.vaultPath}`);
      if (this.settings.enableTranscription && !this.unloaded) {
        void this.transcriptionQueue.enqueue(async () => this.processTranscription(completed.blob, saved));
      }
    } catch (error) {
      this.hud.hide();
      new Notice(`Quick Voice Notes: ${messageFromError(error)}`, 10_000);
    }
  }

  private cancelRecording(): void {
    this.recorder.cancel();
    this.hud.hide();
    new Notice("Voice recording cancelled.");
  }

  public async processExternalRecording(blob: Blob, saved: Awaited<ReturnType<RecordingService["save"]>>): Promise<boolean> {
    return this.transcriptionQueue.enqueue(async () => this.processTranscription(blob, saved));
  }

  private async processTranscription(blob: Blob, saved: Awaited<ReturnType<RecordingService["save"]>>): Promise<boolean> {
    if (this.unloaded) return false;
    const notice = new Notice("Quick Voice Notes: transcribing locally…", 0);
    try {
      const transcript = await this.whisper.transcribe(blob, this.settings, this.operationSignal);
      if (this.isUnloaded()) return false;
      const result = await this.transcriptions.route(transcript, saved);
      const destinations = [result.separateNotePath, result.dailyNotePath].filter((path): path is string => path !== null);
      if (result.integrationFellBack) {
        new Notice(`Daily Note integration became unavailable. The transcript was saved safely to ${result.separateNotePath ?? "the transcript folder"}.`, 10_000);
      } else {
        new Notice(`Transcription saved${destinations.length > 0 ? `: ${destinations.join(", ")}` : "."}`);
      }
      return true;
    } catch (error) {
      new Notice(`Transcription failed, but the audio is safe at ${saved.vaultPath}. ${messageFromError(error)}`, 12_000);
      return false;
    } finally {
      notice.hide();
    }
  }

  private async loadSettings(): Promise<void> {
    try {
      const stored = await this.loadData() as Partial<QuickVoiceNotesSettings> | null;
      this.settings = { ...DEFAULT_SETTINGS, ...(stored ?? {}) };
    } catch (error) {
      console.error("Quick Voice Notes could not load its settings", error);
      new Notice(`Quick Voice Notes settings could not be loaded: ${messageFromError(error)}`);
      this.settings = { ...DEFAULT_SETTINGS };
    }
  }

  private isUnloaded(): boolean {
    return this.unloaded;
  }
}
