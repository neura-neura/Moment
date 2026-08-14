import { afterEach, describe, expect, it, vi } from "vitest";
import { VoiceRecorder } from "../src/recorder";

class FakeTrack {
  public stopped = false;
  public stop(): void { this.stopped = true; }
}

class FakeMediaRecorder extends EventTarget {
  public static isTypeSupported = vi.fn(() => true);
  public readonly mimeType: string;
  public state: RecordingState = "inactive";

  public constructor(public readonly stream: MediaStream, options?: MediaRecorderOptions) {
    super();
    this.mimeType = options?.mimeType ?? "audio/webm";
  }

  public start(): void { this.state = "recording"; }

  public stop(): void {
    this.state = "inactive";
    const dataEvent = new Event("dataavailable") as BlobEvent;
    Object.defineProperty(dataEvent, "data", { value: new Blob(["voice"], { type: this.mimeType }) });
    this.dispatchEvent(dataEvent);
    this.dispatchEvent(new Event("stop"));
  }
}

const originalMediaRecorder = globalThis.MediaRecorder;

afterEach(() => {
  Object.defineProperty(globalThis, "MediaRecorder", { value: originalMediaRecorder, configurable: true });
  vi.restoreAllMocks();
});

describe("VoiceRecorder", () => {
  it("starts, stops, and returns the start timestamp", async () => {
    Object.defineProperty(globalThis, "MediaRecorder", { value: FakeMediaRecorder, configurable: true });
    const track = new FakeTrack();
    const mediaDevices = {
      getUserMedia: vi.fn(() => Promise.resolve(({ getTracks: () => [track] }) as unknown as MediaStream))
    };
    const recorder = new VoiceRecorder(mediaDevices);
    const invokedAt = new Date(2026, 7, 12, 20, 37);
    const startedAt = await recorder.start(64_000, invokedAt);
    const result = await recorder.stop();

    expect(startedAt).toEqual(invokedAt);
    expect(startedAt).not.toBe(invokedAt);
    expect(result.startedAt).toBe(startedAt);
    expect(await result.blob.text()).toBe("voice");
    expect(track.stopped).toBe(true);
    expect(recorder.state).toBe("idle");
  });

  it("cancels and stops all tracks", async () => {
    Object.defineProperty(globalThis, "MediaRecorder", { value: FakeMediaRecorder, configurable: true });
    const track = new FakeTrack();
    const recorder = new VoiceRecorder({
      getUserMedia: () => Promise.resolve(({ getTracks: () => [track] }) as unknown as MediaStream)
    });
    await recorder.start(64_000);
    recorder.cancel();
    expect(track.stopped).toBe(true);
    expect(recorder.state).toBe("idle");
  });

  it("maps microphone permission errors", async () => {
    Object.defineProperty(globalThis, "MediaRecorder", { value: FakeMediaRecorder, configurable: true });
    const recorder = new VoiceRecorder({
      getUserMedia: () => Promise.reject(new DOMException("Denied", "NotAllowedError"))
    });
    await expect(recorder.start(64_000)).rejects.toThrow("Microphone access was denied");
    expect(recorder.state).toBe("idle");
  });

  it("can stop and preserve audio during plugin unload", async () => {
    Object.defineProperty(globalThis, "MediaRecorder", { value: FakeMediaRecorder, configurable: true });
    const track = new FakeTrack();
    const recorder = new VoiceRecorder({
      getUserMedia: () => Promise.resolve(({ getTracks: () => [track] }) as unknown as MediaStream)
    });
    await recorder.start(64_000, new Date(2026, 7, 12, 20, 37));
    const completed = await recorder.stop();
    expect(completed.blob.size).toBeGreaterThan(0);
    expect(track.stopped).toBe(true);
  });
});
