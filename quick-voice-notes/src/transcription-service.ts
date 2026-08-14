import { normalizePath, TFile, type App } from "obsidian";
import { getQuickDailyCaptureAPI } from "./plugin-registry";
import { ensureVaultFolder, formatFileStem, sanitizeVaultFolder, uniqueVaultPath } from "./path-utils";
import { resolveRouting, withVoicePrefix } from "./transcription-routing";
import type { QuickVoiceNotesSettings, SavedRecording } from "./types";

export interface TranscriptionRoutingResult {
  separateNotePath: string | null;
  dailyNotePath: string | null;
  integrationFellBack: boolean;
}

export class TranscriptionService {
  public constructor(
    private readonly app: App,
    private readonly getSettings: () => QuickVoiceNotesSettings
  ) {}

  public async route(transcript: string, recording: SavedRecording): Promise<TranscriptionRoutingResult> {
    const settings = this.getSettings();
    const apiAtStart = getQuickDailyCaptureAPI(this.app);
    const plan = resolveRouting(settings, apiAtStart !== null);
    let separateNotePath: string | null = null;
    let dailyNotePath: string | null = null;
    let integrationFellBack = false;
    const text = withVoicePrefix(transcript, settings.voicePrefix);

    if (plan.dailyNote) {
      const currentApi = getQuickDailyCaptureAPI(this.app);
      if (currentApi === null) {
        integrationFellBack = true;
      } else {
        try {
          const result = await currentApi.insertCapture({
            text,
            timestamp: recording.startedAt,
            source: "quick-voice-notes"
          });
          dailyNotePath = result.path;
        } catch (error) {
          integrationFellBack = true;
          console.error("Quick Voice Notes daily capture integration failed", error);
        }
      }
    }

    if (plan.separateNote || integrationFellBack) {
      separateNotePath = await this.saveSeparateNote(transcript, recording);
    }

    return { separateNotePath, dailyNotePath, integrationFellBack };
  }

  private async saveSeparateNote(transcript: string, recording: SavedRecording): Promise<string> {
    const settings = this.getSettings();
    const folder = sanitizeVaultFolder(settings.transcriptionFolder, "Voice Transcriptions");
    await ensureVaultFolder(this.app.vault, folder);
    const path = uniqueVaultPath(
      (candidate) => this.app.vault.getAbstractFileByPath(candidate) !== null,
      folder,
      formatFileStem(recording.startedAt),
      "md"
    );
    const body = settings.includeAudioEmbed
      ? `${transcript.trim()}\n\n![[${recording.vaultPath}]]\n`
      : `${transcript.trim()}\n`;
    const created = await this.app.vault.create(normalizePath(path), body);
    if (!(created instanceof TFile)) throw new Error(`The transcription could not be saved to "${path}".`);
    return path;
  }
}
