import { execFile } from "node:child_process";
import { createReadStream } from "node:fs";
import { createHash } from "node:crypto";
import { mkdtemp, mkdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { promisify } from "node:util";
import { unzipSync } from "fflate";
import { decodeRecordingToWav } from "./wav";
import { downloadVerifiedFile, type DownloadProgress } from "./download";
import { getModelInfo, modelDownloadUrl } from "./whisper-models";
import type { QuickVoiceNotesSettings } from "./types";

const execFileAsync = promisify(execFile);
const ENGINE_VERSION = "v1.9.2";
const ENGINE_URL = `https://github.com/ggml-org/whisper.cpp/releases/download/${ENGINE_VERSION}/whisper-bin-x64.zip`;
const ENGINE_SHA256 = "49dcc16de826f20bd53d44f947a1ae49dfa81f86cad67a64d80820cb192d674a";
const REQUIRED_ENGINE_FILES = [
  "whisper-cli.exe",
  "whisper.dll",
  "ggml.dll",
  "ggml-base.dll",
  "ggml-cpu-x64.dll"
] as const;

export interface EngineStatus {
  engineInstalled: boolean;
  modelInstalled: boolean;
  engineVersion: string;
  modelLabel: string;
}

export interface InstallProgress {
  phase: "engine" | "model";
  receivedBytes: number;
  totalBytes: number | null;
}

export class WhisperEngine {
  public constructor(private readonly dataDirectory: string) {}

  public async status(settings: QuickVoiceNotesSettings): Promise<EngineStatus> {
    const info = getModelInfo(settings.model);
    return {
      engineInstalled: await this.engineReady(),
      modelInstalled: await fileMatches(this.modelPath(settings.model), info.bytes, info.sha256),
      engineVersion: ENGINE_VERSION,
      modelLabel: info.label
    };
  }

  public async install(
    settings: QuickVoiceNotesSettings,
    onProgress?: (progress: InstallProgress) => void,
    signal?: AbortSignal
  ): Promise<void> {
    signal?.throwIfAborted();
    await mkdir(this.dataDirectory, { recursive: true });
    if (!(await this.engineHealthy(signal))) {
      const zipPath = join(this.dataDirectory, `whisper-${ENGINE_VERSION}.zip`);
      await downloadVerifiedFile(ENGINE_URL, zipPath, ENGINE_SHA256, (progress) => {
        onProgress?.({ phase: "engine", ...progress });
      }, signal);
      try {
        const archive = unzipSync(new Uint8Array(await readFile(zipPath)));
        for (const [archivePath, contents] of Object.entries(archive)) {
          if (!archivePath.startsWith("Release/") || archivePath.endsWith("/")) continue;
          const target = join(this.engineDirectory, basename(archivePath));
          await mkdir(dirname(target), { recursive: true });
          await writeFile(target, contents);
        }
      } finally {
        await rm(zipPath, { force: true });
      }
      if (!(await this.engineHealthy(signal))) throw new Error("The installed whisper.cpp command-line engine failed validation.");
    }

    const model = getModelInfo(settings.model);
    const modelPath = this.modelPath(settings.model);
    if (!(await verifyFile(modelPath, model.bytes, model.sha256, signal))) {
      await downloadVerifiedFile(modelDownloadUrl(settings.model), modelPath, model.sha256, (progress) => {
        onProgress?.({ phase: "model", ...progress });
      }, signal);
    }
    await writeFile(`${modelPath}.sha256`, `${model.sha256}\n`, "utf8");
  }

  public async transcribe(blob: Blob, settings: QuickVoiceNotesSettings, signal?: AbortSignal): Promise<string> {
    signal?.throwIfAborted();
    const status = await this.status(settings);
    if (!status.engineInstalled || !status.modelInstalled) {
      throw new Error("Whisper is not ready. Open Quick Voice Notes settings and choose Install / repair Whisper.");
    }

    const workDirectory = await mkdtemp(join(tmpdir(), "quick-voice-notes-"));
    const wavPath = join(workDirectory, "recording.wav");
    const outputBase = join(workDirectory, "transcript");
    try {
      await writeFile(wavPath, Buffer.from(await decodeRecordingToWav(blob)));
      const args = [
        "-m", this.modelPath(settings.model),
        "-f", wavPath,
        "-l", settings.language,
        "-otxt",
        "-of", outputBase,
        "-nt",
        "-np"
      ];
      try {
        await execFileAsync(this.executablePath, args, {
          cwd: this.engineDirectory,
          windowsHide: true,
          timeout: 30 * 60 * 1_000,
          maxBuffer: 8 * 1024 * 1024,
          signal
        });
      } catch (error) {
        const details = isExecError(error) ? (error.stderr ?? error.message) : String(error);
        throw new Error(`Whisper transcription failed: ${details.trim()}`);
      }
      const transcript = (await readFile(`${outputBase}.txt`, "utf8")).trim();
      if (transcript.length === 0) throw new Error("Whisper completed but did not detect any speech.");
      return transcript;
    } finally {
      await rm(workDirectory, { recursive: true, force: true });
    }
  }

  private get engineDirectory(): string {
    return join(this.dataDirectory, "engine", ENGINE_VERSION);
  }

  private get executablePath(): string {
    return join(this.engineDirectory, "whisper-cli.exe");
  }

  private async engineReady(): Promise<boolean> {
    const checks = await Promise.all(REQUIRED_ENGINE_FILES.map(async (file) => fileExists(join(this.engineDirectory, file))));
    return checks.every(Boolean);
  }

  private async engineHealthy(signal?: AbortSignal): Promise<boolean> {
    if (!(await this.engineReady())) return false;
    try {
      const result = await execFileAsync(this.executablePath, ["--version"], {
        cwd: this.engineDirectory,
        windowsHide: true,
        timeout: 30_000,
        maxBuffer: 1024 * 1024,
        signal
      });
      return result.stdout.includes("1.9.2");
    } catch {
      return false;
    }
  }

  private modelPath(model: QuickVoiceNotesSettings["model"]): string {
    return join(this.dataDirectory, "models", getModelInfo(model).filename);
  }
}

async function fileExists(path: string): Promise<boolean> {
  try {
    return (await stat(path)).isFile();
  } catch {
    return false;
  }
}

async function fileMatches(path: string, expectedBytes: number, expectedSha256: string): Promise<boolean> {
  try {
    const details = await stat(path);
    if (!details.isFile() || details.size !== expectedBytes) return false;
    const recordedDigest = (await readFile(`${path}.sha256`, "utf8")).trim();
    return recordedDigest.toLowerCase() === expectedSha256.toLowerCase();
  } catch {
    return false;
  }
}

async function verifyFile(path: string, expectedBytes: number, expectedSha256: string, signal?: AbortSignal): Promise<boolean> {
  try {
    const details = await stat(path);
    if (!details.isFile() || details.size !== expectedBytes) return false;
    const hash = createHash("sha256");
    for await (const chunk of createReadStream(path, { signal })) hash.update(chunk as Buffer);
    return hash.digest("hex").toLowerCase() === expectedSha256.toLowerCase();
  } catch {
    return false;
  }
}

function isExecError(error: unknown): error is Error & { stderr?: string } {
  return error instanceof Error;
}

export type { DownloadProgress };
