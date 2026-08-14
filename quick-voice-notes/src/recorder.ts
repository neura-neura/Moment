import { microphoneErrorMessage } from "./errors";
import type { CompletedRecording } from "./types";

export type RecorderState = "idle" | "starting" | "recording" | "stopping";

interface ActiveRecording {
  recorder: MediaRecorder;
  stream: MediaStream;
  chunks: Blob[];
  mimeType: string;
  startedAt: Date;
}

export class VoiceRecorder {
  private active: ActiveRecording | null = null;
  private stateValue: RecorderState = "idle";

  public constructor(
    private readonly mediaDevices: Pick<MediaDevices, "getUserMedia"> = navigator.mediaDevices
  ) {}

  public get state(): RecorderState {
    return this.stateValue;
  }

  public get startedAt(): Date | null {
    return this.active?.startedAt ?? null;
  }

  public async start(audioBitsPerSecond: number, invokedAt = new Date()): Promise<Date> {
    if (this.stateValue !== "idle") throw new Error("A recording is already active.");
    if (typeof MediaRecorder === "undefined") throw new Error("This Obsidian version does not provide MediaRecorder.");
    this.stateValue = "starting";
    let stream: MediaStream;
    try {
      stream = await this.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true
        }
      });
    } catch (error) {
      this.stateValue = "idle";
      throw new Error(microphoneErrorMessage(error));
    }

    const mimeType = selectMimeType();
    try {
      const recorder = new MediaRecorder(stream, {
        ...(mimeType.length > 0 ? { mimeType } : {}),
        audioBitsPerSecond
      });
      const chunks: Blob[] = [];
      const startedAt = new Date(invokedAt.getTime());
      recorder.addEventListener("dataavailable", (event) => {
        if (event.data.size > 0) chunks.push(event.data);
      });
      recorder.start(1_000);
      this.active = {
        recorder,
        stream,
        chunks,
        mimeType: recorder.mimeType || mimeType || "audio/webm",
        startedAt
      };
      this.stateValue = "recording";
      return startedAt;
    } catch (error) {
      stopTracks(stream);
      this.stateValue = "idle";
      throw new Error(`The microphone opened, but recording could not start: ${String(error)}`);
    }
  }

  public stop(): Promise<CompletedRecording> {
    const active = this.active;
    if (active === null || this.stateValue !== "recording") {
      return Promise.reject(new Error("No recording is active."));
    }
    this.stateValue = "stopping";
    return new Promise<CompletedRecording>((resolve, reject) => {
      const cleanup = (): void => {
        stopTracks(active.stream);
        this.active = null;
        this.stateValue = "idle";
      };
      active.recorder.addEventListener("stop", () => {
        const blob = new Blob(active.chunks, { type: active.mimeType });
        cleanup();
        if (blob.size === 0) {
          reject(new Error("The microphone returned an empty recording."));
        } else {
          resolve({ blob, mimeType: active.mimeType, startedAt: active.startedAt });
        }
      }, { once: true });
      active.recorder.addEventListener("error", () => {
        cleanup();
        reject(new Error("The recording failed before it could be saved."));
      }, { once: true });
      try {
        active.recorder.stop();
      } catch (error) {
        cleanup();
        reject(error instanceof Error ? error : new Error(String(error)));
      }
    });
  }

  public cancel(): void {
    const active = this.active;
    if (active === null) {
      this.stateValue = "idle";
      return;
    }
    active.recorder.ondataavailable = null;
    active.recorder.onstop = null;
    try {
      if (active.recorder.state !== "inactive") active.recorder.stop();
    } finally {
      stopTracks(active.stream);
      this.active = null;
      this.stateValue = "idle";
    }
  }
}

export function selectMimeType(): string {
  const candidates = [
    "audio/webm;codecs=opus",
    "audio/webm",
    "audio/ogg;codecs=opus",
    "audio/mp4"
  ];
  return candidates.find((candidate) => MediaRecorder.isTypeSupported(candidate)) ?? "";
}

function stopTracks(stream: MediaStream): void {
  for (const track of stream.getTracks()) track.stop();
}
