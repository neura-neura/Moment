import type { App } from "obsidian";
import type { QuickDailyCaptureAPI, QuickDailyCapturePluginLike } from "./types";

interface CommunityPluginManager {
  getPlugin(id: string): QuickDailyCapturePluginLike | null;
}

interface AppWithPlugins extends App {
  plugins?: CommunityPluginManager;
}

export function getQuickDailyCaptureAPI(app: App): QuickDailyCaptureAPI | null {
  const plugin = (app as AppWithPlugins).plugins?.getPlugin("quick-daily-capture") ?? null;
  const api = plugin?.api;
  if (api?.version !== 1 || typeof api.insertCapture !== "function") return null;
  return api;
}

