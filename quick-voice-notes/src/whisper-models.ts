import type { WhisperModel } from "./types";

export interface WhisperModelInfo {
  id: WhisperModel;
  label: string;
  filename: string;
  bytes: number;
  sha256: string;
}

export const WHISPER_MODELS: readonly WhisperModelInfo[] = [
  {
    id: "tiny",
    label: "Tiny multilingual (74 MB, fastest)",
    filename: "ggml-tiny.bin",
    bytes: 77_691_713,
    sha256: "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21"
  },
  {
    id: "base",
    label: "Base multilingual (141 MB, recommended)",
    filename: "ggml-base.bin",
    bytes: 147_951_465,
    sha256: "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"
  },
  {
    id: "small",
    label: "Small multilingual (465 MB, more accurate)",
    filename: "ggml-small.bin",
    bytes: 487_601_967,
    sha256: "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"
  },
  {
    id: "medium",
    label: "Medium multilingual (1.43 GB, demanding)",
    filename: "ggml-medium.bin",
    bytes: 1_533_763_059,
    sha256: "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208"
  },
  {
    id: "large-v3-turbo-q5_0",
    label: "Large v3 Turbo Q5 (547 MB, high quality)",
    filename: "ggml-large-v3-turbo-q5_0.bin",
    bytes: 574_041_195,
    sha256: "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2"
  }
] as const;

export function getModelInfo(model: WhisperModel): WhisperModelInfo {
  const info = WHISPER_MODELS.find((candidate) => candidate.id === model);
  if (info === undefined) throw new Error(`Unknown Whisper model: ${model}`);
  return info;
}

export function modelDownloadUrl(model: WhisperModel): string {
  return `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/${getModelInfo(model).filename}`;
}

