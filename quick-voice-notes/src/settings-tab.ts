import { Notice, PluginSettingTab, type App, type SettingDefinitionItem } from "obsidian";
import { messageFromError } from "./errors";
import { WHISPER_MODELS } from "./whisper-models";
import type QuickVoiceNotesPlugin from "./main";

const LANGUAGES: Record<string, string> = {
  auto: "Auto detect",
  es: "Spanish",
  en: "English",
  zh: "Chinese",
  ja: "Japanese",
  de: "German",
  fr: "French",
  it: "Italian",
  pt: "Portuguese",
  ru: "Russian",
  ko: "Korean"
};

export class QuickVoiceNotesSettingTab extends PluginSettingTab {
  private installing = false;
  private statusText = "Checking…";
  private statusRequested = false;

  public constructor(app: App, private readonly plugin: QuickVoiceNotesPlugin) {
    super(app, plugin);
  }

  public override getSettingDefinitions(): SettingDefinitionItem[] {
    if (this.plugin.settings.enableTranscription && !this.statusRequested) {
      this.statusRequested = true;
      void this.refreshStatus(true);
    }
    const integrationAvailable = this.plugin.integrationAvailable;
    const dailyEnabled = integrationAvailable && this.plugin.settings.enableDailyCaptureIntegration;
    const modelOptions = Object.fromEntries(WHISPER_MODELS.map((model) => [model.id, model.label]));
    return [
      {
        type: "group",
        heading: "Recording",
        items: [
          {
            name: "Audio folder",
            desc: "Vault-relative folder. It is created automatically when needed.",
            control: {
              type: "folder",
              key: "audioFolder",
              placeholder: "Voice Notes",
              validate: validateVaultFolder
            }
          },
          {
            name: "Recording quality",
            desc: "WebM/Opus is compact and native to Obsidian Desktop. 64 kbps is clear for speech.",
            control: {
              type: "dropdown",
              key: "audioBitsPerSecond",
              options: {
                "32000": "Compact — 32 kbps",
                "64000": "Balanced — 64 kbps",
                "96000": "High — 96 kbps"
              }
            }
          }
        ]
      },
      {
        type: "group",
        heading: "Whisper",
        items: [
          {
            name: "Transcribe recordings automatically",
            desc: "Runs the official whisper.cpp engine locally after the original audio is safely stored.",
            control: { type: "toggle", key: "enableTranscription" }
          },
          {
            name: "Language",
            desc: "Use Auto detect unless you consistently record in one language.",
            visible: () => this.plugin.settings.enableTranscription,
            control: { type: "dropdown", key: "language", options: LANGUAGES }
          },
          {
            name: "Whisper model",
            desc: "Base multilingual is the recommended balance for local CPU transcription.",
            visible: () => this.plugin.settings.enableTranscription,
            control: { type: "dropdown", key: "model", options: modelOptions }
          },
          {
            name: "Transcription folder",
            desc: "Used for separate transcript notes and as a safe fallback if Daily Note integration disappears.",
            visible: () => this.plugin.settings.enableTranscription,
            control: {
              type: "folder",
              key: "transcriptionFolder",
              placeholder: "Voice Transcriptions",
              validate: validateVaultFolder
            }
          },
          {
            name: "Embed original audio",
            desc: "Add an Obsidian audio embed to each separate transcript note.",
            visible: () => this.plugin.settings.enableTranscription,
            control: { type: "toggle", key: "includeAudioEmbed" }
          },
          {
            name: "Whisper engine status",
            desc: this.statusText,
            visible: () => this.plugin.settings.enableTranscription,
            render: (setting) => {
              setting.addButton((button) => button
                .setButtonText(this.installing ? "Installing…" : "Install / repair Whisper")
                .setDisabled(this.installing)
                .onClick(async () => this.installWhisper()));
            }
          }
        ]
      },
      {
        type: "group",
        heading: "Integration",
        items: [
          {
            name: "Enable Quick Daily Capture integration",
            desc: integrationAvailable
              ? "Send transcripts through Quick Daily Capture's public API so it applies the same Daily Note rules."
              : "Install and enable Quick Daily Capture to send transcripts to today's Daily Note.",
            control: {
              type: "toggle",
              key: "enableDailyCaptureIntegration",
              disabled: () => !this.plugin.integrationAvailable
            }
          },
          {
            name: "After transcription",
            desc: dailyEnabled
              ? "Choose where completed transcripts go."
              : "Daily Note destinations become available after the integration is enabled.",
            control: {
              type: "dropdown",
              key: "destination",
              options: dailyEnabled
                ? {
                    "separate-note": "Save as separate note",
                    "daily-note": "Insert into Daily Note",
                    both: "Both"
                  }
                : { "separate-note": "Save as separate note" }
            }
          },
          {
            name: "Voice capture prefix",
            desc: "Optional marker such as 🎙 or [Voice]. Empty by default.",
            visible: () => this.plugin.integrationAvailable && this.plugin.settings.enableDailyCaptureIntegration,
            control: {
              type: "text",
              key: "voicePrefix",
              placeholder: "Leave empty for clean text"
            }
          }
        ]
      }
    ];
  }

  public override getControlValue(key: string): unknown {
    if (key === "audioBitsPerSecond") return String(this.plugin.settings.audioBitsPerSecond);
    if (key === "enableDailyCaptureIntegration" && !this.plugin.integrationAvailable) return false;
    if (key === "destination" && (!this.plugin.integrationAvailable || !this.plugin.settings.enableDailyCaptureIntegration)) {
      return "separate-note";
    }
    return (this.plugin.settings as unknown as Record<string, unknown>)[key];
  }

  public override async setControlValue(key: string, value: unknown): Promise<void> {
    const settings = this.plugin.settings as unknown as Record<string, unknown>;
    if (!(key in settings)) return;
    if (key === "audioBitsPerSecond") {
      settings[key] = Number(value);
    } else {
      settings[key] = value;
    }
    if (key === "enableDailyCaptureIntegration" && value !== true) {
      this.plugin.settings.destination = "separate-note";
    }
    await this.plugin.saveSettings();
    if (key === "enableTranscription" && value === true) this.plugin.scheduleExternalInbox();
    if (key === "model") {
      this.statusText = "Checking…";
      await this.refreshStatus(true);
      return;
    }
    this.refreshDomState();
    if (key === "enableTranscription" || key === "enableDailyCaptureIntegration") this.update();
  }

  private async refreshStatus(render: boolean): Promise<void> {
    try {
      const status = await this.plugin.whisper.status(this.plugin.settings);
      this.statusText = status.engineInstalled && status.modelInstalled
        ? `${status.engineVersion} and ${status.modelLabel} are ready.`
        : `${status.engineInstalled ? "Engine ready" : "Engine missing"}; ${status.modelInstalled ? "selected model ready" : "selected model missing"}.`;
    } catch (error) {
      this.statusText = `Status check failed: ${messageFromError(error)}`;
    }
    if (render) this.update();
  }

  private async installWhisper(): Promise<void> {
    if (this.installing) return;
    this.installing = true;
    this.statusText = "Starting download…";
    this.update();
    try {
      let lastProgressRender = 0;
      await this.plugin.whisper.install(this.plugin.settings, (progress) => {
        const received = formatBytes(progress.receivedBytes);
        const total = progress.totalBytes === null ? "" : ` / ${formatBytes(progress.totalBytes)}`;
        this.statusText = `${progress.phase === "engine" ? "Engine" : "Model"}: ${received}${total}`;
        const now = Date.now();
        if (now - lastProgressRender >= 250 || progress.receivedBytes === progress.totalBytes) {
          lastProgressRender = now;
          this.update();
        }
      }, this.plugin.operationSignal);
      new Notice("Quick Voice Notes: Whisper is ready.");
      this.plugin.scheduleExternalInbox();
    } catch (error) {
      new Notice(`Whisper installation failed: ${messageFromError(error)}`, 10_000);
      this.statusText = `Installation failed: ${messageFromError(error)}`;
    } finally {
      this.installing = false;
      await this.refreshStatus(true);
    }
  }
}

function validateVaultFolder(value: string): string | undefined {
  return value.split(/[\\/]/).some((part) => part === "..") ? "Vault folders cannot contain '..'." : undefined;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024 * 1024) return `${String(Math.round(bytes / 1024))} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
