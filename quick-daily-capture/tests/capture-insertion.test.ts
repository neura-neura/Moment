import { describe, expect, it } from "vitest";
import { insertCaptureContent, renderCapture } from "../src/capture-insertion";

const endOptions = {
  location: "end" as const,
  targetHeading: "Notes",
  missingHeadingBehavior: "create" as const
};

describe("renderCapture", () => {
  it("renders one-line and multiline captures", () => {
    expect(renderCapture({ heading: "20:37", text: "One line" }))
      .toBe("## 20:37\n\nOne line");
    expect(renderCapture({ heading: "20:37", text: "First\nSecond" }))
      .toBe("## 20:37\n\nFirst\nSecond");
  });

  it("rejects empty captures", () => {
    expect(() => renderCapture({ heading: "20:37", text: "   " })).toThrow("cannot be empty");
  });
});

describe("insertCaptureContent", () => {
  it("inserts into a new or existing note at the end", () => {
    expect(insertCaptureContent("", { heading: "08:42", text: "First" }, endOptions))
      .toBe("## 08:42\n\nFirst\n");
    expect(insertCaptureContent("# Daily Note\n", { heading: "13:17", text: "Second" }, endOptions))
      .toBe("# Daily Note\n\n## 13:17\n\nSecond\n");
  });

  it("inserts under a heading before its next peer section", () => {
    const input = "# Daily Note\n\n## Tasks\n\n- [ ] One\n\n## Notes\n\nExisting\n\n### Detail\n\nNested\n\n## Journal\n\nLater\n";
    const output = insertCaptureContent(input, { heading: "20:37", text: "Captured" }, {
      location: "under-heading",
      targetHeading: "Notes",
      missingHeadingBehavior: "error"
    });
    expect(output).toContain("### Detail\n\nNested\n\n## 20:37\n\nCaptured\n\n## Journal");
    expect(output.endsWith("Later\n")).toBe(true);
  });

  it("ignores headings inside fenced code blocks", () => {
    const input = "```markdown\n## Notes\n```\n\n## Journal\n";
    expect(() => insertCaptureContent(input, { heading: "20:37", text: "Captured" }, {
      location: "under-heading",
      targetHeading: "Notes",
      missingHeadingBehavior: "error"
    })).toThrow("was not found");
  });

  it("supports all missing-heading behaviors", () => {
    const input = "# Daily Note\n";
    const capture = { heading: "20:37", text: "Captured" };
    const created = insertCaptureContent(input, capture, {
      location: "under-heading",
      targetHeading: "### Notes",
      missingHeadingBehavior: "create"
    });
    expect(created).toBe("# Daily Note\n\n### Notes\n\n## 20:37\n\nCaptured\n");

    const appended = insertCaptureContent(input, capture, {
      location: "under-heading",
      targetHeading: "Notes",
      missingHeadingBehavior: "end"
    });
    expect(appended).toBe("# Daily Note\n\n## 20:37\n\nCaptured\n");
  });

  it("preserves frontmatter when inserting at the beginning", () => {
    const input = "---\ntags: [daily]\n---\n# Daily Note\n";
    const output = insertCaptureContent(input, { heading: "09:01", text: "Early" }, {
      location: "beginning",
      targetHeading: "Notes",
      missingHeadingBehavior: "create"
    });
    expect(output).toBe("---\ntags: [daily]\n---\n\n## 09:01\n\nEarly\n\n# Daily Note\n");
  });

  it("preserves Unicode and supports paths independently of content", () => {
    const text = "Español: bibliografía. 中文笔记。日本語のメモ。🎙️";
    const output = insertCaptureContent("", { heading: "14:05", text }, endOptions);
    expect(output).toContain(text);
  });

  it("retains multiple consecutive captures without overwriting", () => {
    const first = insertCaptureContent("", { heading: "08:42", text: "First" }, endOptions);
    const second = insertCaptureContent(first, { heading: "08:43", text: "Second" }, endOptions);
    const third = insertCaptureContent(second, { heading: "08:44", text: "Third" }, endOptions);
    expect(third.match(/^## /gm)).toHaveLength(3);
    expect(third).toContain("First");
    expect(third).toContain("Second");
    expect(third).toContain("Third");
  });

  it("preserves all existing whitespace outside the inserted block", () => {
    const input = "# Daily Note\n\n## Notes\nExisting text   \n\n\n## Journal\n  Indented journal\n";
    const output = insertCaptureContent(input, { heading: "20:37", text: "Captured" }, {
      location: "under-heading",
      targetHeading: "Notes",
      missingHeadingBehavior: "error"
    });
    expect(output.replace("## 20:37\n\nCaptured\n\n", "")).toBe(input);
  });

  it("uses the note's CRLF line endings for inserted content", () => {
    const input = "# Daily Note\r\n\r\n## Notes\r\n";
    const output = insertCaptureContent(input, { heading: "20:37", text: "First\nSecond" }, {
      location: "under-heading",
      targetHeading: "Notes",
      missingHeadingBehavior: "error"
    });
    expect(output).toBe("# Daily Note\r\n\r\n## Notes\r\n\r\n## 20:37\r\n\r\nFirst\r\nSecond\r\n");
  });
});
