# Test report

Date: 2026-08-14  
Environment: Windows, Node.js 24.13.1, npm 11.8.0, .NET SDK 10.0.400

## Automated verification

Run from the repository root:

```powershell
npm run verify
```

The final run passed **41 plugin tests**: 18 for Quick Daily Capture and 23 for Quick Voice Notes/integration. It also passed strict TypeScript checks, ESLint, production plugin builds, and the Release x64 WinUI bridge build with zero warnings or errors.

The native bridge build validates the configurable global registration path, native NavigationView settings surface, compact WinUI overlays, NAudio recorder wiring, WebM encoder integration, atomic vault writer, native voice processor compilation, field tooltips, tray notifications, the GitHub update checker, and release-version metadata.

## Native migration smoke test

The disposable-vault smoke project at `quick-capture-bridge-winui/Smoke/NativeSmoke.csproj` passed:

```powershell
dotnet run --project quick-capture-bridge-winui/Smoke/NativeSmoke.csproj -c Release
```

It creates a temporary vault, follows a Daily Notes provider/template, inserts native text, creates a WebM-linked native voice Markdown note without Whisper, adopts a legacy text job from `.quick-capture/inbox`, and verifies that a completed download moves from `.download` to its final file after the write stream closes. The temporary vault is removed after the test.

## Installer verification

The local x64 installer was regenerated at:

`quick-capture-bridge-winui/dist/QuickCaptureBridgeSetup-x64.exe`

SHA-256: `05E422B0A5457363EB40684D7578AEFC50BAF984941753FB845657C211EF1A30`
Size: 142,765,915 bytes

It is a real NSIS setup executable with a per-user install directory, Start menu shortcut, Add/Remove Programs registration, and uninstaller. It is not an MSIX package and does not require a development certificate. A silent disposable install created the app executable and uninstaller; the uninstaller then removed the target successfully. `dist` contains only the installer.

The Settings UI now exposes Text Note/Voice naming, conditional Target heading and Missing heading fields, a shared timestamp toggle, vault-relative folder pickers, native tooltip help icons, a GitHub update button, and the `Made by neura-neura` repository link.

## Whisper validation

The pinned whisper.cpp v1.9.2 Windows x64 CLI and model pipeline were validated in the previous release verification with a real WAV sample and a real transcript. The native bridge now uses the same pinned engine/model checks, converts saved WebM to temporary mono 16 kHz PCM WAV through its bundled FFmpeg, serializes transcription jobs, and retains failed jobs with an error sidecar. The download writer now closes its file stream before promoting the verified archive; a completed `.download` file is also promoted instead of being discarded and downloaded again.

The native keyboard hook now uses a short time-based debounce rather than relying only on a delayed key-up callback. This prevents the first stop press after the bridge launches from being lost while the first voice PiP window is activating.

## Manual checks still pending

Automated simulations cannot grant a real Windows microphone permission, deliver a physical Stream Deck F13-F24 key, assess microphone audio quality, inspect WinUI theme rendering, or exercise Obsidian's editor/autosave UI. Those checks are listed in [MANUAL-TEST-PLAN.md](MANUAL-TEST-PLAN.md), which was updated for native bridge ownership and intentionally **has not been executed**.
