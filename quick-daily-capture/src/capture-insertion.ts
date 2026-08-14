import { TargetHeadingMissingError } from "./errors";
import type { MissingHeadingBehavior } from "./types";

export interface RenderedCapture {
  heading: string;
  text: string;
}

export interface InsertionOptions {
  location: "end" | "beginning" | "under-heading";
  targetHeading: string;
  missingHeadingBehavior: MissingHeadingBehavior;
}

interface HeadingMatch {
  lineStart: number;
  lineEnd: number;
  level: number;
}

export function renderCapture(capture: RenderedCapture): string {
  const text = capture.text.trim();
  if (text.length === 0) throw new Error("A capture cannot be empty.");
  return `## ${capture.heading}\n\n${text}`;
}

export function insertCaptureContent(
  current: string,
  capture: RenderedCapture,
  options: InsertionOptions
): string {
  const entry = renderCapture(capture);
  switch (options.location) {
    case "end":
      return appendBlock(current, entry);
    case "beginning":
      return insertAtBeginning(current, entry);
    case "under-heading":
      return insertUnderHeading(current, entry, options.targetHeading, options.missingHeadingBehavior);
  }
}

function appendBlock(content: string, block: string): string {
  return insertBlockAt(content, content.length, block);
}

function insertAtBeginning(content: string, block: string): string {
  const frontmatterEnd = findFrontmatterEnd(content);
  return insertBlockAt(content, frontmatterEnd, block);
}

function findFrontmatterEnd(content: string): number {
  if (!content.startsWith("---\n") && !content.startsWith("---\r\n")) return 0;
  const match = /^---\r?\n[\s\S]*?\r?\n---[ \t]*\r?\n?/.exec(content);
  return match?.[0].length ?? 0;
}

function insertUnderHeading(
  content: string,
  block: string,
  configuredHeading: string,
  missingBehavior: MissingHeadingBehavior
): string {
  const target = normalizeTargetHeading(configuredHeading);
  if (target.text.length === 0) throw new Error("Set a target heading in Quick Daily Capture settings.");
  const headings = findHeadings(content);
  const matchIndex = headings.findIndex((heading) => {
    const line = content.slice(heading.lineStart, heading.lineEnd);
    const parsed = parseHeading(line);
    return parsed !== null && parsed.text.localeCompare(target.text, undefined, { sensitivity: "accent" }) === 0;
  });

  if (matchIndex < 0) {
    if (missingBehavior === "error") throw new TargetHeadingMissingError(target.text);
    if (missingBehavior === "end") return appendBlock(content, block);
    const heading = `${"#".repeat(target.level)} ${target.text}`;
    return appendBlock(appendBlock(content, heading), block);
  }

  const heading = headings[matchIndex];
  if (heading === undefined) return content;
  const following = headings.slice(matchIndex + 1).find((candidate) => candidate.level <= heading.level);
  const insertionPoint = following?.lineStart ?? content.length;
  return insertBlockAt(content, insertionPoint, block);
}

function insertBlockAt(content: string, index: number, block: string): string {
  const before = content.slice(0, index);
  const after = content.slice(index);
  const newline = content.includes("\r\n") ? "\r\n" : "\n";
  const normalizedBlock = newline === "\n" ? block : block.replaceAll("\n", newline);
  const prefix = blankLineSuffix(before, newline);
  const suffix = blankLinePrefix(after, newline);
  return `${before}${prefix}${normalizedBlock}${suffix}${after}`;
}

function blankLineSuffix(value: string, newline: string): string {
  if (value.length === 0) return "";
  if (value.endsWith(`${newline}${newline}`)) return "";
  if (value.endsWith(newline)) return newline;
  return `${newline}${newline}`;
}

function blankLinePrefix(value: string, newline: string): string {
  if (value.length === 0) return newline;
  if (value.startsWith(`${newline}${newline}`)) return "";
  if (value.startsWith(newline)) return newline;
  return `${newline}${newline}`;
}

function normalizeTargetHeading(value: string): { text: string; level: number } {
  const trimmed = value.trim();
  const parsed = parseHeading(trimmed);
  return parsed ?? { text: trimmed.replace(/[ \t]+#+[ \t]*$/, "").trim(), level: 2 };
}

function findHeadings(content: string): HeadingMatch[] {
  const matches: HeadingMatch[] = [];
  let inFence = false;
  let fenceMarker = "";
  let offset = 0;
  for (const lineWithEnding of content.match(/.*(?:\r?\n|$)/g) ?? []) {
    if (lineWithEnding.length === 0) continue;
    const line = lineWithEnding.replace(/\r?\n$/, "");
    const fence = /^\s*(`{3,}|~{3,})/.exec(line)?.[1];
    if (fence !== undefined) {
      if (!inFence) {
        inFence = true;
        fenceMarker = fence[0] ?? "";
      } else if ((fence[0] ?? "") === fenceMarker) {
        inFence = false;
      }
    } else if (!inFence) {
      const parsed = parseHeading(line);
      if (parsed !== null) {
        matches.push({ lineStart: offset, lineEnd: offset + line.length, level: parsed.level });
      }
    }
    offset += lineWithEnding.length;
  }
  return matches;
}

function parseHeading(line: string): { text: string; level: number } | null {
  const match = /^(#{1,6})[ \t]+(.+?)[ \t]*#*[ \t]*$/.exec(line);
  const hashes = match?.[1];
  const text = match?.[2]?.trim();
  if (hashes === undefined || text === undefined || text.length === 0) return null;
  return { level: hashes.length, text };
}
