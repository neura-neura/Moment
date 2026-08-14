export class DailyNotesUnavailableError extends Error {
  public constructor() {
    super("Enable the Daily Notes core plugin or daily notes in Periodic Notes first.");
    this.name = "DailyNotesUnavailableError";
  }
}

export class TargetHeadingMissingError extends Error {
  public constructor(heading: string) {
    super(`The target heading "${heading}" was not found in today's Daily Note.`);
    this.name = "TargetHeadingMissingError";
  }
}

export function messageFromError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
