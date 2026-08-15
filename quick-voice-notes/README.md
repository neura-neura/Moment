# Quick Voice Notes

Quick Voice Notes is an Obsidian Desktop plugin for immediate microphone capture with optional private, local Whisper transcription.

The normal flow is:

```text
Hotkey → speak → same hotkey
```

The recording start timestamp is captured immediately. The original audio is always written to the vault before transcription or Daily Note integration begins.

## Requirements

- Obsidian Desktop 1.13.0 or newer.
- Windows x64 for the managed one-click `whisper.cpp` installation in version 1.1.0.
- Microphone permission for Obsidian.
- Quick Daily Capture is optional and is never required for recording or separate transcript notes.

The plugin is marked `isDesktopOnly: true` because local Whisper execution uses Node.js and native executables.

## Installation

1. Close Obsidian or disable the plugin before replacing its files.
2. Extract `quick-voice-notes.zip` into `<Vault>/.obsidian/plugins/`.
3. Confirm the resulting path is `<Vault>/.obsidian/plugins/quick-voice-notes/manifest.json`.
4. In Obsidian, open **Settings → Community plugins** and enable **Quick Voice Notes**.
5. In **Settings → Hotkeys**, assign a shortcut to **Quick Voice Notes: Toggle recording**. `F13` is the default suggestion and any `F13`–`F24` key is accepted.
6. The first recording may trigger a Windows microphone permission prompt. Allow access if you want Obsidian to record.

The ZIP already contains the built plugin. Node.js, Python, FFmpeg, a terminal, and a background server are not required for normal use.

## Recording controls

- Run **Quick Voice Notes: Toggle recording** while idle to start.
- Run the same command again to stop and save.
- Choose **Stop** in the compact HUD to stop and save.
- Choose **Cancel** or press **Escape** to discard the active, unsaved recording.

The HUD uses Obsidian theme variables, keyboard-accessible buttons, a live duration, tabular numerals, and reduced-motion preferences.

## Audio format

The plugin asks Chromium's `MediaRecorder.isTypeSupported()` at runtime and prefers:

1. WebM with Opus;
2. generic WebM;
3. Ogg with Opus;
4. MP4 as a final browser-supported fallback.

On the Windows Obsidian target, WebM/Opus is expected. It is compact, speech-friendly, and directly recordable without a native encoder. The original stays in this format. For Whisper, the plugin uses the browser's local audio decoder and creates a temporary mono 16 kHz PCM WAV, then deletes that temporary workspace after processing. FFmpeg is not required.

Filenames include local date, time, and milliseconds, for example:

```text
Voice Notes/2026-08-12 20-37-42-123.webm
```

An additional numeric suffix is allocated if a collision still exists.

## Local Whisper setup

1. Open **Settings → Quick Voice Notes**.
2. Enable **Transcribe recordings automatically**.
3. Choose a language and model. **Base multilingual** is recommended initially.
4. Choose **Install / repair Whisper**.
5. Wait until the status says the engine and selected model are ready.

The explicit installation action downloads:

- the pinned official `whisper.cpp` Windows x64 release from GitHub;
- the selected official GGML Whisper model from the `ggerganov/whisper.cpp` Hugging Face repository.

Downloads are SHA-256 verified. **Install / repair Whisper** also re-hashes an existing selected model and executes the CLI's local version check, redownloading when validation fails. The engine and models are stored under:

```text
<Vault>/.obsidian/plugins/quick-voice-notes/whisper/
```

The native process starts only for queued transcription work and exits after each recording. No server or Windows startup task is installed, and the model is not kept permanently resident in RAM. Transcriptions are serialized to avoid loading multiple large models concurrently.

Available multilingual models:

- Tiny — about 74 MB, fastest.
- Base — about 141 MB, recommended balance.
- Small — about 465 MB, more accurate and slower.
- Medium — about 1.43 GB, demanding.
- Large v3 Turbo Q5 — about 547 MB, high quality with quantization.

Available language choices include Auto detect, Spanish, English, Chinese, Japanese, German, French, Italian, Portuguese, Russian, and Korean. Whisper supports many more languages; Auto detect handles those without adding a large settings list.

## Transcript destinations

- **Save as separate note** creates a Markdown note in the configured transcription folder.
- **Insert into Daily Note** is shown only when Quick Daily Capture is installed, enabled, and integration is enabled.
- **Both** writes both destinations.

Separate notes can include a full-path Obsidian embed of the original audio.

If Quick Daily Capture becomes unavailable after a transcription starts, the transcript automatically falls back to a separate note rather than being lost.

For Daily Note insertion, the plugin passes the recording's start `Date` to Quick Daily Capture. A recording begun at 20:37 is therefore inserted under `## 20:37` even if it stops at 20:39 and transcription finishes at 20:40.

## Privacy and network access

- No telemetry.
- No audio, transcript, or vault content is uploaded.
- Recording and transcription are local.
- The only network access is the user-initiated download of the official engine/model files during **Install / repair Whisper**.
- There are no API keys, paid services, or background servers.

## Failure safety

- Microphone permission, missing-device, busy-device, recorder, vault, decode, engine, model, and transcription failures produce human-readable notices.
- Audio is stored before any Whisper work.
- A Whisper failure reports the saved audio path and never deletes the original.
- Missing folders are recreated automatically.
- If Daily Note integration fails, the transcript falls back to the separate transcription folder.
- Plugin unload cancels an active unsaved recording and stops microphone tracks.

## Troubleshooting

- **Microphone access denied** — open Windows **Settings → Privacy & security → Microphone**, enable microphone access and desktop app access, then restart Obsidian if necessary.
- **No microphone found** — connect/enable the input device and select it as a Windows input device.
- **Whisper not ready** — open plugin settings and choose **Install / repair Whisper** for the currently selected model.
- **Model download fails** — check network/proxy/firewall access to GitHub and Hugging Face, then retry. Partial downloads are removed.
- **Install / repair says “Failed to fetch”** — update to this build. Windows desktop downloads use a native HTTPS stream with redirect support instead of Chromium CORS fetch; retry after checking that the network can reach GitHub release assets and Hugging Face.
- **Transcription is slow** — choose Tiny or Base; Medium and Large models require substantially more CPU/RAM.
- **No Daily Note option** — install and enable Quick Daily Capture, reopen this settings tab, then enable the integration.
- **Audio saved but transcription failed** — the notice includes the safe vault path. Repair Whisper or change model/language and record again.

## Build from source

From the repository root:

```powershell
npm install
npm run typecheck -w quick-voice-notes
npm run lint -w quick-voice-notes
npm run test -w quick-voice-notes
npm run build -w quick-voice-notes
npm run package -w quick-voice-notes
```

## Limitations

- Version 1.1.0 automates native Whisper installation for Windows x64. The architecture is portable, but managed macOS/Linux release selection is not implemented yet.
- A real microphone and Obsidian's permission UI require an in-app manual test; automated tests use a deterministic MediaRecorder simulation.
- Transcription begins after recording stops; live transcription is intentionally out of scope.
- Changing the selected model requires installing that model once. Previous models remain on disk until manually removed from the plugin’s `whisper/models` directory.

## Capturing while Obsidian is closed

For global capture from a game or another full-screen app, use [Moment (WinUI 3)](../quick-capture-bridge-winui/README.md). Moment records WebM, runs local Whisper, and routes Markdown/audio output directly to the vault without requiring this plugin. This plugin remains available for the in-Obsidian command path. Download the current Windows installer from the public [Moment v1.2.7 release](https://github.com/neura-neura/Moment/releases/tag/v1.2.7).
