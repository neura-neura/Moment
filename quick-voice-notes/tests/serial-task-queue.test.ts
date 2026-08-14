import { describe, expect, it } from "vitest";
import { SerialTaskQueue } from "../src/serial-task-queue";

describe("SerialTaskQueue", () => {
  it("limits Whisper-style jobs to one at a time", async () => {
    const queue = new SerialTaskQueue();
    const events: string[] = [];
    let releaseFirst: (() => void) | undefined;
    const gate = new Promise<void>((resolve) => { releaseFirst = resolve; });
    const first = queue.enqueue(async () => { events.push("first:start"); await gate; events.push("first:end"); });
    const second = queue.enqueue(() => { events.push("second"); return Promise.resolve(); });
    await new Promise<void>((resolve) => { setTimeout(resolve, 0); });
    expect(events).toEqual(["first:start"]);
    releaseFirst?.();
    await Promise.all([first, second]);
    expect(events).toEqual(["first:start", "first:end", "second"]);
  });

  it("continues after a failed transcription", async () => {
    const queue = new SerialTaskQueue();
    await expect(queue.enqueue(() => Promise.reject(new Error("Whisper failed")))).rejects.toThrow("Whisper failed");
    await expect(queue.enqueue(() => Promise.resolve("next"))).resolves.toBe("next");
  });
});

