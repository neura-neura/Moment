# Moment (WinUI 3)

<img src="Assets/MomentIcon.png" alt="Moment icon" width="180">

Moment is the native Windows application for frictionless Obsidian capture. It is built with WinUI 3 and Windows App SDK, so its settings window and both always-on-top picture-in-picture (PiP) capture panels use the same native UI stack. Moment runs independently; Quick Daily Capture and Quick Voice Notes are optional legacy integrations.

It is made to work alongside Obsidian, while remaining useful with Obsidian closed or without the capture plugins. The public source and releases are published at [github.com/neura-neura/Moment](https://github.com/neura-neura/Moment). The current Windows release is [Moment 1.2.0](https://github.com/neura-neura/Moment/releases/tag/v1.2.0).

The settings window uses a native WinUI NavigationView: Text Note, Voice, and Shortcuts are in the left menu, while startup and tray behavior live under the footer Settings section. The Save settings action remains available in the native footer across sections.

Moment remains useful while Obsidian is closed or another application, including a game, has focus. It registers ordinary combinations with the Win32 global `RegisterHotKey` API and uses a low-level keyboard hook for reliable F13-F24/Stream Deck delivery. Registration errors are shown in the settings page instead of being silently ignored.

## Features

- Capture any non-modifier key with any combination of Alt, Ctrl, Shift, and Windows.
- F13 through F24 are supported for Stream Deck and extended-keyboard workflows.
- Text shortcut: opens a small, titleless native writing canvas with normal Windows rounded corners in the lower-right work area above the taskbar. The editor is focused immediately, Enter saves, Shift+Enter inserts a new line, Ctrl+Enter also saves, Escape cancels, and the text area scrolls when it reaches the fixed canvas bounds. If it loses activation because focus moves to another app, it closes like Escape. There are no visible menus or secondary controls.
- Voice shortcut: opens a compact 260x60 native recording island centered near the top edge with a classic red recording ring, live level bars, and compact Stop/Cancel actions. Press the same shortcut again to stop, save a compact 16 kHz mono WebM/Opus file, queue the job, and close the panel immediately; no Recording, save-status, or completion text is shown in the island.
- Select any existing Obsidian vault. Text is inserted into the configured daily file immediately, and voice jobs are durably processed by Moment even when Obsidian is closed.
- Voice captures are finalized as 16 kHz mono WebM/Opus at the selected 32/64/96 kbps quality. The installer bundles the encoder, so no separate FFmpeg installation is required.
- Optional local Whisper transcription is installed and repaired from the Voice page. Output is written as Markdown in the vault, with a WebM embed when enabled; Daily Note insertion and separate-note fallback are handled natively.
- Text Note timestamps can be enabled or disabled for both text and voice destinations. Audio and transcript folders are selected relative to the vault with native folder pickers.
- The Settings page includes a GitHub update checker that verifies the release checksum before launching the normal Windows installer.
- Optional per-user "Start with Windows" registration through the installed executable, with a minimized launch mode.
- Closing the settings window can keep Moment alive in the Windows notification tray; use the tray menu to reopen it or exit completely.
- Native processing uses `.quick-capture/bridge-inbox`, `.quick-capture/bridge-processed`, and `.quick-capture/bridge-failed` so captures survive restarts. Legacy `.quick-capture/inbox` jobs are adopted after both legacy plugins are disabled or removed.

## Windows installer (EXE)

Download the current Windows installer from the public [Moment v1.2.0 release](https://github.com/neura-neura/Moment/releases/tag/v1.2.0). Choose `MomentSetup-x64.exe` from the release assets.

For local development, build the native Windows installer:

```powershell
npm run package:bridge
```

The output is created in `quick-capture-bridge-winui/dist/` as `MomentSetup-x64.exe`. It is a real NSIS installer with a setup wizard, selectable per-user installation folder, Start menu shortcut, Add/Remove Programs registration, uninstaller, and optional launch after setup. It installs to `%LOCALAPPDATA%\Programs\Moment` by default. It does not install an MSIX package and does not request a package certificate. Packaging removes its intermediate payload files, so `dist` contains only the installer after the command completes.

## Build from source

Requirements for building: Windows 10/11, .NET 8 SDK or newer, NSIS, and the NuGet packages restored by the project. End users only need to run the EXE installer.

```powershell
dotnet restore .\QuickCaptureBridgeWinUI.csproj
dotnet build .\QuickCaptureBridgeWinUI.csproj -c Release -p:Platform=x64 -p:PublishReadyToRun=false -p:PublishTrimmed=false
dotnet publish .\QuickCaptureBridgeWinUI.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64 -p:PublishReadyToRun=false -p:PublishTrimmed=false
```

The canonical bridge project is this directory. The older WPF bridge source remains in the repository only as historical reference and is not used by the root build or installer scripts.

## First run

1. Launch Moment.
2. Choose the existing vault that should receive external captures.
3. Configure the Text Note and Voice pages directly, then save.
4. Click each shortcut field and press the desired combination. The displayed registration state must say **Registered** after clicking **Register shortcuts** or **Save settings**.
5. If transcription is enabled, open the Voice page and choose **Install / repair Whisper** for the selected model.
6. Capture one text and one voice note, open the vault in Obsidian, and verify the text-file/voice Markdown output. The two legacy plugins can remain disabled or be uninstalled.
7. Enable **Start with Windows** only after the first-run setup is complete.

The selected settings are stored at `%LOCALAPPDATA%\Moment\settings.json`. Existing settings from `%LOCALAPPDATA%\QuickCaptureBridge\settings.json` are read once for compatibility.

