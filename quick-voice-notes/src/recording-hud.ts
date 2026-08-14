export interface RecordingHudHandlers {
  stop: () => void;
  cancel: () => void;
}

export class RecordingHud {
  private root: HTMLDivElement | null = null;
  private timer: number | null = null;
  private timeEl: HTMLSpanElement | null = null;
  private keyHandler: ((event: KeyboardEvent) => void) | null = null;

  public show(startedAt: Date, handlers: RecordingHudHandlers): void {
    this.hide();
    const root = document.body.createDiv({ cls: "quick-voice-hud" });
    root.setAttribute("role", "status");
    root.setAttribute("aria-live", "polite");

    root.createSpan({ cls: "quick-voice-hud-dot", attr: { "aria-hidden": "true" } });
    root.createSpan({ text: "Recording", cls: "quick-voice-hud-label" });
    this.timeEl = root.createSpan({ text: "00:00", cls: "quick-voice-hud-time" });
    const stopButton = root.createEl("button", {
      text: "Stop",
      cls: "quick-voice-hud-button mod-cta",
      attr: { type: "button", "aria-label": "Stop and save recording" }
    });
    const cancelButton = root.createEl("button", {
      text: "Cancel",
      cls: "quick-voice-hud-button",
      attr: { type: "button", "aria-label": "Cancel recording" }
    });
    stopButton.addEventListener("click", handlers.stop);
    cancelButton.addEventListener("click", handlers.cancel);
    this.keyHandler = (event: KeyboardEvent): void => {
      if (event.key === "Escape") {
        event.preventDefault();
        handlers.cancel();
      }
    };
    document.addEventListener("keydown", this.keyHandler, true);
    this.root = root;
    this.updateTime(startedAt);
    this.timer = window.setInterval(() => this.updateTime(startedAt), 1_000);
  }

  public setStopping(): void {
    this.root?.addClass("is-stopping");
    const label = this.root?.querySelector<HTMLElement>(".quick-voice-hud-label");
    if (label !== null && label !== undefined) label.setText("Saving");
    for (const button of this.root?.querySelectorAll<HTMLButtonElement>("button") ?? []) button.disabled = true;
  }

  public hide(): void {
    if (this.timer !== null) window.clearInterval(this.timer);
    if (this.keyHandler !== null) document.removeEventListener("keydown", this.keyHandler, true);
    this.root?.remove();
    this.root = null;
    this.timeEl = null;
    this.timer = null;
    this.keyHandler = null;
  }

  private updateTime(startedAt: Date): void {
    if (this.timeEl === null) return;
    const totalSeconds = Math.max(0, Math.floor((Date.now() - startedAt.getTime()) / 1_000));
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    this.timeEl.setText(`${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`);
  }
}

