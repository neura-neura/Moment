import { Modal, Notice, type App } from "obsidian";
import { messageFromError } from "./errors";
import type { CaptureRequest, QuickDailyCaptureSettings } from "./types";

export class CaptureModal extends Modal {
  private textarea: HTMLTextAreaElement | null = null;
  private saving = false;
  private captureTimestamp: Date;

  public constructor(
    app: App,
    timestamp: Date,
    private readonly getSettings: () => QuickDailyCaptureSettings,
    private readonly saveCapture: (request: CaptureRequest) => Promise<void>
  ) {
    super(app);
    this.captureTimestamp = timestamp;
  }

  public override onOpen(): void {
    this.modalEl.addClass("quick-daily-capture-modal");
    this.contentEl.empty();
    this.contentEl.addClass("quick-daily-capture-content");
    this.titleEl.setText("");
    this.titleEl.setAttribute("aria-hidden", "true");
    this.modalEl.setAttribute("aria-label", "Quick capture");

    const label = this.contentEl.createEl("label", {
      text: "Capture text",
      cls: "quick-capture-visually-hidden"
    });
    const textarea = this.contentEl.createEl("textarea", {
      cls: "quick-daily-capture-input",
      attr: {
        placeholder: "Write a note…",
        rows: "5",
        "aria-label": "Capture text"
      }
    });
    label.htmlFor = "quick-daily-capture-input";
    textarea.id = "quick-daily-capture-input";
    this.textarea = textarea;

    textarea.addEventListener("keydown", (event) => {
      const settings = this.getSettings();
      const enterSaves = settings.enterToSave && !event.shiftKey && !event.ctrlKey && !event.metaKey && !event.altKey;
      const modifiedEnterSaves = !settings.enterToSave && !event.shiftKey && (event.ctrlKey || event.metaKey);
      if (event.key === "Enter" && !event.isComposing && (enterSaves || modifiedEnterSaves)) {
        event.preventDefault();
        void this.submit();
      }
    });

    const focusEditor = (): void => {
      textarea.focus({ preventScroll: true });
      textarea.setSelectionRange(textarea.value.length, textarea.value.length);
    };
    window.requestAnimationFrame(focusEditor);
    window.setTimeout(focusEditor, 40);
    window.setTimeout(focusEditor, 160);
  }

  public override onClose(): void {
    this.contentEl.empty();
    this.textarea = null;
  }

  private async submit(): Promise<void> {
    if (this.saving || this.textarea === null) return;
    const textarea = this.textarea;
    const text = textarea.value.trim();
    if (text.length === 0) {
      new Notice("Type something before saving the capture.");
      return;
    }
    this.saving = true;
    textarea.disabled = true;
    try {
      await this.saveCapture({ text, timestamp: this.captureTimestamp, source: "quick-daily-capture" });
      if (this.getSettings().closeAfterSave) {
        this.close();
      } else {
        textarea.value = "";
        textarea.disabled = false;
        this.captureTimestamp = new Date();
        textarea.focus();
      }
    } catch (error) {
      new Notice(`Quick Daily Capture: ${messageFromError(error)}`, 8000);
      textarea.disabled = false;
      textarea.focus();
    } finally {
      this.saving = false;
    }
  }
}
