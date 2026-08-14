import { beforeEach, describe, expect, it, vi } from "vitest";
import { TFile } from "obsidian";
import { DEFAULT_SETTINGS, type QuickDailyCaptureAPI, type QuickVoiceNotesSettings, type SavedRecording } from "../src/types";

const registry = vi.hoisted(() => ({ api: null as QuickDailyCaptureAPI | null }));
vi.mock("../src/plugin-registry", () => ({ getQuickDailyCaptureAPI: () => registry.api }));

import { TranscriptionService } from "../src/transcription-service";

function settings(overrides: Partial<QuickVoiceNotesSettings>): QuickVoiceNotesSettings {
  return { ...DEFAULT_SETTINGS, ...overrides };
}

function recording(): SavedRecording {
  return {
    vaultPath: "Voice Notes/2026-08-12 20-37-00-000.webm",
    absolutePath: "C:\\Vault With Spaces\\Voice Notes\\2026-08-12 20-37-00-000.webm",
    startedAt: new Date(2026, 7, 12, 20, 37),
    mimeType: "audio/webm;codecs=opus"
  };
}

function createApp(): { app: never; created: Map<string, string> } {
  const created = new Map<string, string>();
  const folders = new Set<string>();
  const vault = {
    getAbstractFileByPath(path: string) {
      if (created.has(path)) return fileAt(path);
      if (folders.has(path)) return { path };
      return null;
    },
    createFolder(path: string) { folders.add(path); return Promise.resolve(); },
    create(path: string, content: string) {
      created.set(path, content);
      return Promise.resolve(fileAt(path));
    }
  };
  return { app: { vault } as never, created };
}

function fileAt(path: string): TFile {
  const file = new TFile();
  Object.defineProperty(file, "path", { value: path });
  return file;
}

describe("TranscriptionService", () => {
  beforeEach(() => { registry.api = null; });

  it("saves a standalone transcript with the audio embed", async () => {
    const { app, created } = createApp();
    const service = new TranscriptionService(app, () => settings({ destination: "separate-note" }));
    const result = await service.route("Hola, 世界。", recording());
    expect(result.separateNotePath).toContain("Voice Transcriptions/2026-08-12 20-37-00-000.md");
    expect(created.get(result.separateNotePath ?? "")).toBe("Hola, 世界。\n\n![[Voice Notes/2026-08-12 20-37-00-000.webm]]\n");
  });

  it("passes the recording start timestamp to Quick Daily Capture", async () => {
    const insertCapture = vi.fn(() => Promise.resolve({ path: "Daily/2026-08-12.md", timestamp: recording().startedAt }));
    registry.api = { version: 1, insertCapture };
    const { app } = createApp();
    const service = new TranscriptionService(app, () => settings({
      destination: "daily-note",
      enableDailyCaptureIntegration: true,
      voicePrefix: "🎙"
    }));

    const result = await service.route("Transcribed", recording());

    expect(insertCapture).toHaveBeenCalledWith({
      text: "🎙 Transcribed",
      timestamp: recording().startedAt,
      source: "quick-voice-notes"
    });
    expect(result.dailyNotePath).toBe("Daily/2026-08-12.md");
    expect(result.separateNotePath).toBeNull();
  });

  it("falls back safely if the integration disables during the call", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const insertCapture = vi.fn(() => Promise.reject(new Error("Plugin disabled")));
    registry.api = { version: 1, insertCapture };
    const { app, created } = createApp();
    const service = new TranscriptionService(app, () => settings({
      destination: "daily-note",
      enableDailyCaptureIntegration: true
    }));
    const result = await service.route("Still safe", recording());
    expect(result.integrationFellBack).toBe(true);
    expect(result.separateNotePath).not.toBeNull();
    expect(created.get(result.separateNotePath ?? "")).toContain("Still safe");
    expect(consoleError).toHaveBeenCalledOnce();
  });
});
