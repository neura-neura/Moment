import { beforeEach, describe, expect, it, vi } from "vitest";
import { TFile } from "obsidian";
import { DEFAULT_SETTINGS } from "../src/types";

const dailyMocks = vi.hoisted(() => ({
  enabled: true,
  folder: "Daily Notes With Spaces",
  format: "YYYY-MM-DD",
  create: vi.fn()
}));

vi.mock("obsidian-daily-notes-interface", () => ({
  appHasDailyNotesPluginLoaded: () => dailyMocks.enabled,
  getDailyNoteSettings: () => ({ folder: dailyMocks.folder, format: dailyMocks.format, template: "" }),
  createDailyNote: dailyMocks.create
}));

import { DailyNoteService } from "../src/daily-note-service";

interface MockVault {
  files: Map<string, TFile>;
  contents: Map<string, string>;
  getAbstractFileByPath(path: string): TFile | null;
  process(file: TFile, transform: (content: string) => string): Promise<string>;
}

function createVault(initial: Record<string, string> = {}): MockVault {
  const files = new Map<string, TFile>();
  const contents = new Map<string, string>();
  for (const [path, content] of Object.entries(initial)) {
    files.set(path, fileAt(path));
    contents.set(path, content);
  }
  return {
    files,
    contents,
    getAbstractFileByPath(path) { return files.get(path) ?? null; },
    async process(file, transform) {
      const next = transform(contents.get(file.path) ?? "");
      await new Promise<void>((resolve) => { setTimeout(resolve, 1); });
      contents.set(file.path, next);
      return next;
    }
  };
}

describe("DailyNoteService", () => {
  beforeEach(() => {
    dailyMocks.enabled = true;
    dailyMocks.folder = "Daily Notes With Spaces";
    dailyMocks.format = "YYYY-MM-DD";
    dailyMocks.create.mockReset();
  });

  it("updates an existing Daily Note with the invocation timestamp", async () => {
    const path = "Daily Notes With Spaces/2026-08-12.md";
    const vault = createVault({ [path]: "# Daily Note\n" });
    const service = new DailyNoteService({ vault } as never, () => ({ ...DEFAULT_SETTINGS }));
    const invokedAt = new Date(2026, 7, 12, 20, 37, 0);

    const result = await service.insertCapture({ text: "Buscar bibliografía.", timestamp: invokedAt });

    expect(result).toEqual({ path, timestamp: invokedAt });
    expect(vault.contents.get(path)).toBe("# Daily Note\n\n## 20:37\n\nBuscar bibliografía.\n");
    expect(dailyMocks.create).not.toHaveBeenCalled();
  });

  it("creates a missing Daily Note and preserves its template", async () => {
    const path = "Daily Notes With Spaces/2026-08-12.md";
    const vault = createVault();
    dailyMocks.create.mockImplementation(() => {
      const file = fileAt(path);
      vault.files.set(path, file);
      vault.contents.set(path, "# Daily Note\n\n## Notes\n\n## Journal\n");
      return Promise.resolve(file);
    });
    const service = new DailyNoteService({ vault } as never, () => ({
      ...DEFAULT_SETTINGS,
      insertionLocation: "under-heading",
      targetHeading: "Notes"
    }));

    await service.insertCapture({ text: "Created note", timestamp: new Date(2026, 7, 12, 9, 5) });

    expect(dailyMocks.create).toHaveBeenCalledOnce();
    expect(vault.contents.get(path)).toContain("## Notes\n\n## 09:05\n\nCreated note\n\n## Journal");
  });

  it("serializes multiple concurrent captures without loss", async () => {
    const path = "Daily Notes With Spaces/2026-08-12.md";
    const vault = createVault({ [path]: "" });
    const service = new DailyNoteService({ vault } as never, () => ({ ...DEFAULT_SETTINGS }));

    await Promise.all([
      service.insertCapture({ text: "First", timestamp: new Date(2026, 7, 12, 10, 1) }),
      service.insertCapture({ text: "Second", timestamp: new Date(2026, 7, 12, 10, 2) }),
      service.insertCapture({ text: "第三", timestamp: new Date(2026, 7, 12, 10, 3) })
    ]);

    const content = vault.contents.get(path) ?? "";
    expect(content.match(/^## /gm)).toHaveLength(3);
    expect(content).toContain("First");
    expect(content).toContain("Second");
    expect(content).toContain("第三");
  });

  it("reports when Daily Notes is disabled", async () => {
    dailyMocks.enabled = false;
    const vault = createVault();
    const service = new DailyNoteService({ vault } as never, () => ({ ...DEFAULT_SETTINGS }));
    await expect(service.insertCapture({ text: "Cannot save" })).rejects.toThrow("Enable the Daily Notes core plugin");
  });
});

function fileAt(path: string): TFile {
  const file = new TFile();
  Object.defineProperty(file, "path", { value: path });
  return file;
}
