export type WhisperLanguage = "auto" | "es" | "en" | "zh" | "ja" | "de" | "fr" | "it" | "pt" | "ru" | "ko";
export type WhisperModel = "tiny" | "base" | "small" | "medium" | "large-v3-turbo-q5_0";
export type TranscriptionDestination = "separate-note" | "daily-note" | "both";

export interface QuickVoiceNotesSettings {
  audioFolder: string;
  audioBitsPerSecond: number;
  enableTranscription: boolean;
  language: WhisperLanguage;
  model: WhisperModel;
  transcriptionFolder: string;
  includeAudioEmbed: boolean;
  destination: TranscriptionDestination;
  enableDailyCaptureIntegration: boolean;
  voicePrefix: string;
}

export interface SavedRecording {
  vaultPath: string;
  absolutePath: string;
  startedAt: Date;
  mimeType: string;
}

export interface CompletedRecording {
  blob: Blob;
  mimeType: string;
  startedAt: Date;
}

export interface CaptureRequest {
  text: string;
  timestamp?: Date | number | string;
  source?: string;
}

export interface CaptureResult {
  path: string;
  timestamp: Date;
}

export interface QuickDailyCaptureAPI {
  readonly version: 1;
  insertCapture(request: CaptureRequest): Promise<CaptureResult>;
}

export interface QuickDailyCapturePluginLike {
  api?: QuickDailyCaptureAPI;
}

export const DEFAULT_SETTINGS: QuickVoiceNotesSettings = {
  audioFolder: "Voice Notes",
  audioBitsPerSecond: 64_000,
  enableTranscription: false,
  language: "auto",
  model: "base",
  transcriptionFolder: "Voice Transcriptions",
  includeAudioEmbed: true,
  destination: "separate-note",
  enableDailyCaptureIntegration: false,
  voicePrefix: ""
};

