import { PluginSettingTab, type App, type SettingDefinitionItem } from "obsidian";
import type QuickDailyCapturePlugin from "./main";

export class QuickDailyCaptureSettingTab extends PluginSettingTab {
  public constructor(app: App, private readonly plugin: QuickDailyCapturePlugin) {
    super(app, plugin);
  }

  public override getSettingDefinitions(): SettingDefinitionItem[] {
    return [
      {
        type: "group",
        heading: "General",
        items: [
          {
            name: "Insertion location",
            desc: "Choose where captures are placed without rearranging existing content.",
            control: {
              type: "dropdown",
              key: "insertionLocation",
              options: {
                end: "End of note",
                beginning: "Beginning of note",
                "under-heading": "Under heading"
              }
            }
          },
          {
            name: "Target heading",
            desc: "Heading text such as Notes, or an ATX heading such as ## Notes.",
            visible: () => this.plugin.settings.insertionLocation === "under-heading",
            control: {
              type: "text",
              key: "targetHeading",
              placeholder: "Notes",
              validate: (value) => value.trim().length === 0 ? "Enter a target heading." : undefined
            }
          },
          {
            name: "If the heading is missing",
            desc: "Creating it adds a level-two heading unless the target includes # characters.",
            visible: () => this.plugin.settings.insertionLocation === "under-heading",
            control: {
              type: "dropdown",
              key: "missingHeadingBehavior",
              options: {
                create: "Create heading",
                end: "Append to end",
                error: "Show an error"
              }
            }
          },
          {
            name: "Timestamp format",
            desc: "Moment.js format used for each capture heading. The default is HH:mm.",
            control: {
              type: "text",
              key: "timestampFormat",
              placeholder: "HH:mm",
              validate: (value) => value.trim().length === 0 ? "Enter a timestamp format." : undefined
            }
          }
        ]
      },
      {
        type: "group",
        heading: "Floating capture",
        items: [
          {
            name: "Enter saves",
            desc: "Enter saves; Shift+Enter inserts a line break. When disabled, Ctrl/Cmd+Enter saves. Escape always cancels.",
            control: { type: "toggle", key: "enterToSave" }
          },
          {
            name: "Close after saving",
            desc: "When disabled, the popup stays open for another capture and records a new timestamp.",
            control: { type: "toggle", key: "closeAfterSave" }
          }
        ]
      }
    ];
  }

  public override async setControlValue(key: string, value: unknown): Promise<void> {
    const settings = this.plugin.settings as unknown as Record<string, unknown>;
    if (!(key in settings)) return;
    settings[key] = value;
    await this.plugin.saveSettings();
    this.refreshDomState();
  }
}
