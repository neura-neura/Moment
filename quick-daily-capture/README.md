# Quick Daily Capture

Quick Daily Capture is a keyboard-first Obsidian plugin for adding a timestamped entry to today's Daily Note without navigating away from the current task.

The normal flow is:

```text
Hotkey → type → Enter
```

The timestamp is captured before the popup opens, so it reflects when the thought was initiated rather than when typing finished.

## Requirements

- Obsidian 1.13.0 or newer.
- Either the Daily Notes core plugin, or daily notes enabled in Periodic Notes.
- Desktop and mobile are supported by this plugin, although the companion voice plugin is desktop-only.

## Installation

1. Close Obsidian or disable the plugin before replacing its files.
2. Extract `quick-daily-capture.zip` into `<Vault>/.obsidian/plugins/`.
3. Confirm the resulting path is `<Vault>/.obsidian/plugins/quick-daily-capture/manifest.json`.
4. In Obsidian, open **Settings → Community plugins** and enable **Quick Daily Capture**.
5. Enable and configure **Daily Notes** under **Settings → Core plugins → Daily notes**, if it is not already enabled.

The ZIP already contains the built `main.js`, `manifest.json`, and `styles.css`; Node.js is not required for normal use.

## Commands and hotkeys

The plugin registers:

- **Quick Daily Capture: New entry**

Assign any shortcut under **Settings → Hotkeys**. `F14` is the default suggestion and any `F13`–`F24` key is accepted.

Obsidian's Daily Notes core plugin already provides **Daily notes: Open today's daily note**. Assign that built-in command its own hotkey if desired; this plugin deliberately does not duplicate it.

## Popup controls

- **Enter** saves when **Enter saves** is enabled.
- **Shift+Enter** inserts a line break.
- **Escape** cancels and closes the popup.
- If **Enter saves** is disabled, **Ctrl+Enter** on Windows/Linux or **Cmd+Enter** on macOS saves.

The text field receives focus automatically.

## Settings

### General

- **Insertion location**
  - **End of note** appends captures.
  - **Beginning of note** inserts after YAML frontmatter, if present.
  - **Under heading** inserts at the end of the selected heading's section, before the next heading of the same or higher level.
- **Target heading** accepts `Notes` or an explicit ATX heading such as `## Notes`.
- **If the heading is missing** can create the heading, append to the note, or show an error.
- **Timestamp format** is a Moment.js format. The stable default is `HH:mm`.

### Floating capture

- **Enter saves** controls the primary save shortcut.
- **Close after saving** closes after success. If disabled, the field clears and a new invocation timestamp is captured for the next entry.

## Daily Note behavior

Daily Note folder, filename format, and template come from the configured Daily Notes provider. New notes preserve template content and supported template date/time tokens through `obsidian-daily-notes-interface`.

Writes use Obsidian's atomic `Vault.process()` API and a per-file serial queue. Each update transforms the latest current content, so rapid captures do not overwrite one another and an open editor remains synchronized through Obsidian's vault layer.

Periodic Notes is supported when its **daily** period is enabled. Other periods are intentionally out of scope.

## Integration API

The plugin exposes `plugin.api.version === 1` with:

```ts
interface QuickDailyCaptureAPI {
  readonly version: 1;
  insertCapture(request: {
    text: string;
    timestamp?: Date | number | string;
    source?: string;
  }): Promise<{ path: string; timestamp: Date }>;
}
```

Callers supply the capture time explicitly when needed. Quick Voice Notes uses the recording start time. See the repository's `INTEGRATION.md`.

## Privacy

The plugin has no telemetry and makes no network requests. Captured text remains in the vault.

## Troubleshooting

- **“Enable the Daily Notes core plugin…”** — enable Daily Notes, or enable the daily period in Periodic Notes.
- **Target heading not found** — check spelling and heading text, or choose **Create heading** / **Append to end**.
- **Cannot create/write the note** — verify the vault is writable and the Daily Notes folder path is valid.
- **A template section appears in the wrong place** — set **Insertion location** to **Under heading** and use the exact visible heading text.

## Build from source

From the repository root:

```powershell
npm install
npm run typecheck -w quick-daily-capture
npm run lint -w quick-daily-capture
npm run test -w quick-daily-capture
npm run build -w quick-daily-capture
npm run package -w quick-daily-capture
```

## Limitations

For global capture while Obsidian is closed, use [Moment (WinUI 3)](../quick-capture-bridge-winui/README.md). It accepts any key combination (including F13-F24), shows a compact PiP panel, and writes directly to the selected vault. Moment is independent, so this plugin can be disabled or uninstalled; it remains available for users who prefer the in-Obsidian command. Download the current Windows installer from the public [Moment v1.2.6 release](https://github.com/neura-neura/Moment/releases/tag/v1.2.6).

- Obsidian does not expose Daily Notes settings through a public plugin API. The maintained `obsidian-daily-notes-interface` compatibility package is isolated behind the Daily Note service for provider detection, settings, template handling, and note creation. Actual content mutation uses the public atomic vault API.
- Heading matching supports Markdown ATX headings (`#` through `######`), not Setext headings.
- If the same heading text appears multiple times, the first matching heading is used.
