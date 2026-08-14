import { describe, expect, it } from "vitest";
import { extensionForMimeType, formatFileStem, sanitizeVaultFolder, uniqueVaultPath } from "../src/path-utils";

describe("recording paths", () => {
  it("uses a local millisecond timestamp", () => {
    const date = new Date(2026, 7, 12, 20, 37, 42, 123);
    expect(formatFileStem(date)).toBe("2026-08-12 20-37-42-123");
  });

  it("prevents collisions deterministically", () => {
    const existing = new Set([
      "Voice Notes/2026-08-12 20-37-42-123.webm",
      "Voice Notes/2026-08-12 20-37-42-123-1.webm"
    ]);
    expect(uniqueVaultPath((path) => existing.has(path), "Voice Notes", "2026-08-12 20-37-42-123", "webm"))
      .toBe("Voice Notes/2026-08-12 20-37-42-123-2.webm");
  });

  it("supports spaces and rejects parent traversal", () => {
    expect(sanitizeVaultFolder("  My Voice Notes / Inbox  ", "Voice Notes")).toBe("My Voice Notes / Inbox");
    expect(() => sanitizeVaultFolder("../outside", "Voice Notes")).toThrow("cannot contain '..'");
  });

  it("selects compatible file extensions", () => {
    expect(extensionForMimeType("audio/webm;codecs=opus")).toBe("webm");
    expect(extensionForMimeType("audio/ogg;codecs=opus")).toBe("ogg");
    expect(extensionForMimeType("audio/mp4")).toBe("m4a");
  });
});

