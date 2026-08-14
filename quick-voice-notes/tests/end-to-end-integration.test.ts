import { describe, expect, it, vi } from "vitest";
import { insertCaptureContent } from "../../quick-daily-capture/src/capture-insertion";
import { DEFAULT_SETTINGS, type QuickDailyCaptureAPI } from "../src/types";

const registry = vi.hoisted(() => ({ api: null as QuickDailyCaptureAPI | null }));
vi.mock("../src/plugin-registry", () => ({ getQuickDailyCaptureAPI: () => registry.api }));

import { TranscriptionService } from "../src/transcription-service";

describe("voice-to-Daily-Note integration", () => {
  it("uses 20:37 from recording start even when transcription finishes later", async () => {
    let dailyNote = "# Daily Note\n\n## Notes\n\n## Journal\n";
    registry.api = {
      version: 1,
      insertCapture(request) {
        const timestamp = request.timestamp instanceof Date ? request.timestamp : new Date(request.timestamp ?? Date.now());
        const heading = `${String(timestamp.getHours()).padStart(2, "0")}:${String(timestamp.getMinutes()).padStart(2, "0")}`;
        dailyNote = insertCaptureContent(dailyNote, { heading, text: request.text }, {
          location: "under-heading",
          targetHeading: "Notes",
          missingHeadingBehavior: "error"
        });
        return Promise.resolve({ path: "Daily Notes/2026-08-12.md", timestamp });
      }
    };
    const startedAt = new Date(2026, 7, 12, 20, 37);
    const app = { vault: {} } as never;
    const service = new TranscriptionService(app, () => ({
      ...DEFAULT_SETTINGS,
      enableDailyCaptureIntegration: true,
      destination: "daily-note"
    }));

    vi.setSystemTime(new Date(2026, 7, 12, 20, 40));
    try {
      await service.route("Content transcribed by Whisper.", {
        vaultPath: "Voice Notes/2026-08-12 20-37-00-000.webm",
        absolutePath: "C:\\Vault\\Voice Notes\\2026-08-12 20-37-00-000.webm",
        startedAt,
        mimeType: "audio/webm;codecs=opus"
      });
    } finally {
      vi.useRealTimers();
    }

    expect(dailyNote).toContain("## Notes\n\n## 20:37\n\nContent transcribed by Whisper.\n\n## Journal");
    expect(dailyNote).not.toContain("## 20:40");
  });
});

