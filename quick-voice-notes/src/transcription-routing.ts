import type { QuickVoiceNotesSettings } from "./types";

export interface RoutingPlan {
  separateNote: boolean;
  dailyNote: boolean;
}

export function resolveRouting(settings: QuickVoiceNotesSettings, integrationAvailable: boolean): RoutingPlan {
  const wantsSeparate = settings.destination === "separate-note" || settings.destination === "both";
  const wantsDaily = settings.destination === "daily-note" || settings.destination === "both";
  return {
    separateNote: wantsSeparate || (wantsDaily && (!settings.enableDailyCaptureIntegration || !integrationAvailable)),
    dailyNote: wantsDaily && settings.enableDailyCaptureIntegration && integrationAvailable
  };
}

export function withVoicePrefix(transcript: string, prefix: string): string {
  const clean = transcript.trim();
  const marker = prefix.trim();
  return marker.length === 0 ? clean : `${marker} ${clean}`;
}

