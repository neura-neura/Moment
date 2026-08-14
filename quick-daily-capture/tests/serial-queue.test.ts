import { describe, expect, it } from "vitest";
import { KeyedSerialQueue } from "../src/serial-queue";

describe("KeyedSerialQueue", () => {
  it("serializes same-file modifications in submission order", async () => {
    const queue = new KeyedSerialQueue();
    const events: string[] = [];
    let releaseFirst: (() => void) | undefined;
    const gate = new Promise<void>((resolve) => { releaseFirst = resolve; });

    const first = queue.run("Daily Notes With Spaces/2026-08-12.md", async () => {
      events.push("first:start");
      await gate;
      events.push("first:end");
    });
    const second = queue.run("Daily Notes With Spaces/2026-08-12.md", () => {
      events.push("second:start");
      events.push("second:end");
      return Promise.resolve();
    });

    await new Promise<void>((resolve) => { setTimeout(resolve, 0); });
    expect(events).toEqual(["first:start"]);
    releaseFirst?.();
    await Promise.all([first, second]);
    expect(events).toEqual(["first:start", "first:end", "second:start", "second:end"]);
    expect(queue.pendingKeys).toBe(0);
  });

  it("continues after a failed write", async () => {
    const queue = new KeyedSerialQueue();
    await expect(queue.run("note", () => Promise.reject(new Error("read-only")))).rejects.toThrow("read-only");
    await expect(queue.run("note", () => Promise.resolve("saved"))).resolves.toBe("saved");
  });

  it("allows different files to progress concurrently", async () => {
    const queue = new KeyedSerialQueue();
    const events: string[] = [];
    let releaseA: (() => void) | undefined;
    const gateA = new Promise<void>((resolve) => { releaseA = resolve; });
    const a = queue.run("a", async () => { events.push("a"); await gateA; });
    const b = queue.run("b", () => { events.push("b"); return Promise.resolve(); });
    await b;
    expect(events).toEqual(["a", "b"]);
    releaseA?.();
    await a;
  });
});
