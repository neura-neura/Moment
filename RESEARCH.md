# Product and architecture research

Research was performed against the current APIs and releases on 2026-08-12 before implementation.

## Product requirements

### Quick Daily Capture

- One assignable Obsidian command with F13–F24-compatible defaults.
- Capture invocation time before rendering UI.
- Focused compact modal with keyboard-only happy path.
- Respect Daily Notes provider folder, format, and template.
- Insert at end, beginning after frontmatter, or under an ATX heading.
- Preserve all existing content and prevent concurrent-write loss.
- Expose a minimal, versioned API to other enabled plugins.

### Quick Voice Notes

- One toggle command with F13–F24-compatible defaults.
- Start microphone capture immediately and show a compact HUD.
- Stop/save from the same command or HUD; cancel with Escape.
- Save collision-safe original audio inside the vault before post-processing.
- Optional real Whisper transcription, local by default, with no resident server.
- Work independently; optionally route through Quick Daily Capture.
- Preserve the recording start time through slow transcription.
- Fall back safely if transcription or integration fails.

## Obsidian API findings

- `Plugin.addCommand()` is the supported way to register assignable commands. Users assign shortcuts in **Settings → Hotkeys**; default hotkeys were intentionally omitted to avoid conflicts.
- `Modal`, `Notice`, and the declarative `PluginSettingTab.getSettingDefinitions()` API provide native UI and settings behavior. The implementation targets Obsidian 1.13.0 because declarative plugin settings were introduced there.
- `Vault.create()`, `Vault.createBinary()`, `Vault.createFolder()`, and `normalizePath()` are used for vault-safe file operations.
- `Vault.process()` atomically transforms the latest file content and is explicitly recommended over read/modify/write for avoiding accidental data loss. Quick Daily Capture adds a per-path serial queue on top for deterministic ordering.
- Obsidian includes Moment and uses local system time. The implementation converts the invocation/recording `Date` using Obsidian's Moment instance only when formatting the `HH:mm` heading.
- Obsidian's core Daily Notes plugin already exposes **Open today's daily note**, so Quick Daily Capture does not duplicate it.
- There is still no public plugin API for reading core Daily Notes settings, creating a note with its template semantics, or enumerating enabled community plugins.

Primary references:

- [Obsidian plugin API types](https://github.com/obsidianmd/obsidian-api)
- [Obsidian manifest reference](https://docs.obsidian.md/Reference/Manifest)
- [Obsidian vault guide and atomic `Vault.process()`](https://docs.obsidian.md/Plugins/Vault)
- [Obsidian plugin UI elements and theme variables](https://docs.obsidian.md/Plugins/User%20interface/HTML%20elements)
- [Obsidian event lifecycle](https://docs.obsidian.md/Plugins/Events)
- [Obsidian Daily Notes interface compatibility package](https://github.com/liamcain/obsidian-daily-notes-interface)

## Daily Notes compatibility decision

`obsidian-daily-notes-interface` 0.9.5 is bundled into Quick Daily Capture. It is a focused, maintained compatibility package used by Obsidian plugins to mirror core Daily Notes and Periodic Notes daily settings and template creation.

This is the only Daily Notes compatibility boundary. It necessarily reads non-public provider/plugin state because Obsidian exposes no public equivalent. All note content mutation remains on the public `Vault.process()` API.

Periodic Notes daily support is included through that adapter because it adds little additional product complexity. Weekly, monthly, quarterly, and yearly capture are not part of this product.

## Microphone and audio decision

Obsidian Desktop runs on Electron/Chromium, where `navigator.mediaDevices.getUserMedia()` and `MediaRecorder` are available. The code feature-detects MIME support with `MediaRecorder.isTypeSupported()` rather than assuming a container.

WebM/Opus is preferred on Windows because it offers:

- native Chromium recording without native modules;
- compact speech files at 64 kbps by default;
- broad playback support inside Obsidian Desktop;
- reliable browser decoding for conversion to Whisper input.

The original is retained. A temporary local mono 16 kHz PCM16 WAV is produced through `AudioContext` for Whisper, avoiding an FFmpeg dependency.

Reference: [MDN `MediaRecorder.isTypeSupported()`](https://developer.mozilla.org/en-US/docs/Web/API/MediaRecorder/isTypeSupported_static).

## Whisper decision

The selected implementation is the official open-source `whisper.cpp` CLI running actual OpenAI Whisper model weights in GGML format.

Why it was selected over Python Whisper or faster-whisper:

- no Python runtime, virtual environment, CUDA stack, or background service;
- official Windows x64 release binaries are small enough for on-demand installation;
- models are downloaded separately and only by explicit user action;
- CPU operation is straightforward and private;
- the native process exits after each task, releasing model memory;
- no API key, recurring payment, or audio upload.

The installer pins `whisper.cpp` v1.9.2 and validates its official Windows x64 archive with SHA-256. Model URLs, expected sizes, and SHA-256 digests are pinned from the official `ggerganov/whisper.cpp` model repository. Downloads stream to `.download` files and are renamed only after verification.

Transcription jobs are serialized to prevent several large models from loading simultaneously. There is no permanent model cache in application memory and no startup process.

References:

- [`whisper.cpp` repository and CLI](https://github.com/ggml-org/whisper.cpp)
- [`whisper.cpp` model documentation](https://github.com/ggml-org/whisper.cpp/blob/master/models/README.md)
- [Official converted GGML models](https://huggingface.co/ggerganov/whisper.cpp/tree/main)

## Privacy and external communication

Neither plugin has telemetry. Quick Daily Capture makes no network calls. Quick Voice Notes performs network requests only after the user clicks **Install / repair Whisper**, and only to download pinned engine/model artifacts from GitHub and Hugging Face. Audio, transcripts, and vault data are never sent.

## Global capture companion decision

Obsidian's command hotkeys are scoped to Obsidian. A native Windows application is therefore required for shortcuts while a game or another application has focus. `quick-capture-bridge-winui` uses Win32 `RegisterHotKey` plus a low-level keyboard hook for extended F13-F24/Stream Deck delivery, WinUI 3 for the settings and two compact topmost PiP surfaces, NAudio for 16 kHz mono capture, and a bundled FFmpeg encoder for compact WebM/Opus output.

The bridge now owns the independent path end to end. `NativeDailyNoteService` mirrors the provider folder/format/template and the capture insertion rules. `NativeWhisperEngine` runs the pinned whisper.cpp CLI and model locally. `NativeVoiceProcessor` serializes durable jobs, writes Markdown destinations, and moves failures to a retryable sidecar folder. Jobs are stored under `.quick-capture/bridge-inbox`; the old plugin inbox is adopted only after migration and plugin removal/disablement, avoiding duplicate processing while legacy plugins are still active. The older WPF bridge remains only as historical source and is not used by the root scripts.
