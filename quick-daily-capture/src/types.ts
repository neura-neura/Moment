export type InsertionLocation = "end" | "beginning" | "under-heading";
export type MissingHeadingBehavior = "create" | "end" | "error";

export interface QuickDailyCaptureSettings {
  insertionLocation: InsertionLocation;
  targetHeading: string;
  missingHeadingBehavior: MissingHeadingBehavior;
  timestampFormat: string;
  closeAfterSave: boolean;
  enterToSave: boolean;
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

export const DEFAULT_SETTINGS: QuickDailyCaptureSettings = {
  insertionLocation: "end",
  targetHeading: "Notes",
  missingHeadingBehavior: "create",
  timestampFormat: "HH:mm",
  closeAfterSave: true,
  enterToSave: true
};

