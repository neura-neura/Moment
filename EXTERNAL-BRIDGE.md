# Global capture architecture

The repository now includes `quick-capture-bridge-winui`, a native WinUI 3 Windows companion for captures made while Obsidian is closed or another application has focus.

```text
Configured shortcut anywhere in Windows
        |
        v
Moment (WinUI 3 settings + compact topmost PiP panels)
        |
        +--> Voice Notes/<timestamp>.webm (Opus)
        +--> Daily/<date>.md (timestamped text or voice entry)
        +--> Voice Transcriptions/<timestamp>.md (optional)
        +--> .quick-capture/bridge-{inbox,processed,failed}
```

The bridge is now the independent native owner of the external-capture path:

1. Registering a key combination (including `F13`-`F24`) uses native Win32 global registration plus a low-level keyboard hook for extended Stream Deck keys, so the shortcuts work outside Obsidian and are not limited by the active application.
2. The bridge writes an atomic JSON job only after the audio file exists. A crash cannot leave an apparently-complete job pointing at a partial file.
3. Native text capture follows the vault's Daily Notes or Periodic Notes provider settings, including folder, filename format, template, insertion location, headings, and timestamp format.
4. Native voice processing serializes Whisper jobs, routes to a separate Markdown note, the Daily Note, or both, and writes failed jobs to `.quick-capture/bridge-failed` with an error sidecar.
5. After **Import plugin settings**, legacy jobs in `.quick-capture/inbox` are adopted once the corresponding legacy plugin is disabled or removed. This makes it safe to uninstall both plugins without losing queued captures.

## Shortcut behavior inside and outside Obsidian

Both commands also expose safe defaults (`F14` for **Quick Daily Capture: New entry** and `F13` for **Quick Voice Notes: Toggle recording**). Users can change them under **Settings â†’ Hotkeys**. Obsidian's command hotkey type accepts the `F13`â€“`F24` key names. The WinUI bridge accepts any captured key plus Alt/Ctrl/Shift/Windows modifiers. If the bridge is running, its global registration intentionally wins for the configured keys; choose different keys if you want an Obsidian-only command binding.

## Limitations and safety

- The WinUI bridge records microphone PCM through NAudio and finalizes a compact WebM/Opus file with its bundled encoder. Native processing then handles transcription and vault routing; Obsidian only needs to be opened later to display the resulting Markdown and audio.
- If the selected vault is moved, reopen bridge settings and select it again.
- If a hotkey is already registered by another application, the bridge reports the conflict and that action is unavailable until a different key is selected.
- `.quick-capture/bridge-inbox` is the native durable queue. `.quick-capture/bridge-processed` contains completed jobs and `.quick-capture/bridge-failed` contains jobs that need attention. Deleting a queued job discards only that pending capture.

The production installer is published in the [Moment v1.2.0 release](https://github.com/neura-neura/Moment/releases/tag/v1.2.0). Local development builds produce a real NSIS Windows installer EXE through `npm run package:bridge`; it has a setup wizard and uninstaller, and no MSIX certificate is required. Moment can stay hidden in the Windows notification tray when its settings window is closed, and its two capture panels are compact topmost overlays rather than normal application windows.


