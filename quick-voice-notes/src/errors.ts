export function messageFromError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export function microphoneErrorMessage(error: unknown): string {
  const name = error instanceof DOMException ? error.name : "";
  if (name === "NotAllowedError" || name === "SecurityError") {
    return "Microphone access was denied. Allow microphone access for Obsidian in Windows Settings and try again.";
  }
  if (name === "NotFoundError" || name === "DevicesNotFoundError") {
    return "No microphone was found. Connect or enable a microphone and try again.";
  }
  if (name === "NotReadableError" || name === "TrackStartError") {
    return "The microphone is already in use or unavailable to Obsidian.";
  }
  return `Recording could not start: ${messageFromError(error)}`;
}

