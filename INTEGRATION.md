# Capture integration and migration

Moment is the independent Windows path for capture outside Obsidian. Quick Daily Capture and Quick Voice Notes remain compatible Obsidian plugins for users who want in-app commands, but Moment no longer depends on either plugin.

## Native bridge path

1. Choose a vault in Moment.
2. Configure the Text Note and Voice pages directly and save.
3. If transcription is enabled, install the selected Whisper engine/model from the Voice page.
4. Capture while Obsidian is closed. Text is written to the configured daily file. Voice is saved as WebM and routed to a Text Note, a separate Markdown note, or both.
5. Open Obsidian and verify the files. The two capture plugins can remain disabled or be uninstalled.

The bridge reads the Daily Notes core settings, or the enabled Periodic Notes daily settings, directly from the vault. It preserves the provider folder, filename format, template, insertion location, target heading, missing-heading behavior, and timestamp format. Native voice processing also preserves audio folder, voice-note filename format/prefix, bitrate, language, model, transcription folder, separate-transcript filename format/prefix, audio embed, and destination.

Native jobs are stored under `.quick-capture/bridge-inbox`, completed jobs under `.quick-capture/bridge-processed`, and failures under `.quick-capture/bridge-failed`. Existing legacy `.quick-capture/inbox` jobs are adopted when the corresponding legacy plugin is disabled or removed and the bridge has previously been configured for that vault.

## Legacy plugin API

Quick Daily Capture still exposes this instance property for compatible Obsidian callers:

```ts
interface QuickDailyCaptureAPI {
  readonly version: 1;
  insertCapture(request: {
    text: string;
    timestamp?: Date | number | string;
    source?: string;
  }): Promise<{
    path: string;
    timestamp: Date;
  }>;
}
```

`timestamp` defaults to the call time when omitted. It may be a `Date`, epoch milliseconds, or a parseable date string. Invalid timestamps and empty text reject with a human-readable error. `source` is optional metadata reserved for compatible callers.

When both legacy plugins remain enabled, Quick Voice Notes can still call this API after its own in-Obsidian recording and transcription flow. That path is independent from the native bridge path and is retained for backward compatibility.
