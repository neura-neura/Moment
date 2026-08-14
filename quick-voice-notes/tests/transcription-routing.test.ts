import { describe, expect, it } from "vitest";
import { DEFAULT_SETTINGS, type QuickVoiceNotesSettings } from "../src/types";
import { resolveRouting, withVoicePrefix } from "../src/transcription-routing";

function settings(overrides: Partial<QuickVoiceNotesSettings>): QuickVoiceNotesSettings {
  return { ...DEFAULT_SETTINGS, ...overrides };
}

describe("transcription routing", () => {
  it("works independently with no integration", () => {
    expect(resolveRouting(settings({ destination: "separate-note" }), false))
      .toEqual({ separateNote: true, dailyNote: false });
  });

  it("routes to the API when installed and enabled", () => {
    expect(resolveRouting(settings({
      destination: "daily-note",
      enableDailyCaptureIntegration: true
    }), true)).toEqual({ separateNote: false, dailyNote: true });
  });

  it("falls back to a separate note when integration is missing", () => {
    expect(resolveRouting(settings({
      destination: "daily-note",
      enableDailyCaptureIntegration: true
    }), false)).toEqual({ separateNote: true, dailyNote: false });
  });

  it("supports both destinations and an optional clean prefix", () => {
    expect(resolveRouting(settings({
      destination: "both",
      enableDailyCaptureIntegration: true
    }), true)).toEqual({ separateNote: true, dailyNote: true });
    expect(withVoicePrefix(" Transcript ", "")).toBe("Transcript");
    expect(withVoicePrefix("Transcript", "🎙")).toBe("🎙 Transcript");
  });
});

