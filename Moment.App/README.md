# Moment

Moment is a native Windows capture app built with WinUI 3 and the Windows App SDK. It creates compact text and voice notes without requiring an editor to be open. It can also be used alongside any compatible Markdown editor: the generated Markdown and WebM files are written directly to the selected workspace folder.

## Features

- Global text and voice shortcuts, including F13-F24 and Stream Deck keys.
- A compact PiP text surface with immediate keyboard focus.
- A compact PiP voice surface with WebM/Opus recording and live no-audio feedback.
- One new **recurring note** per calendar day, with configurable filename format, prefix, template insertion, headings, and timestamps.
- Folder pickers for Text Notes, Voice Notes, and Transcriptions. Each output can be placed anywhere on the computer, independently of the selected workspace.
- Optional local Whisper transcription. Audio stays local and remains in the configured audio folder when transcription is unavailable.
- Separate transcript notes, recurring-note output, or both destinations.
- A durable Moment queue under `.moment/capture/pending`, `.moment/capture/completed`, and `.moment/capture/failed` so voice processing survives restarts.
- Native Windows tray behavior, startup registration, updates, and a real per-user NSIS installer.

## Build

```powershell
dotnet restore .\Moment.App.csproj
dotnet build .\Moment.App.csproj -c Release -p:Platform=x64 -p:PublishReadyToRun=false -p:PublishTrimmed=false
dotnet publish .\Moment.App.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64 -p:PublishReadyToRun=false -p:PublishTrimmed=false
```

The native smoke test uses a disposable workspace and does not open an editor:

```powershell
dotnet run --project .\Smoke\NativeSmoke.csproj -c Release
```

## Installer

Install NSIS, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

The output is `Moment.App/dist/MomentSetup-x64.exe`, a real NSIS installer with a setup wizard, per-user installation, Start menu shortcut, Desktop shortcut selected by default, Add/Remove Programs registration, uninstaller, and optional launch after setup. It installs to `%LOCALAPPDATA%\Programs\Moment` and does not require an MSIX certificate.

## Data locations

- Settings: `%LOCALAPPDATA%\Moment\settings.json`
- Whisper engine and models: `%LOCALAPPDATA%\Moment\whisper`
- Workspace queue: `.moment\capture\pending`, `.moment\capture\completed`, `.moment\capture\failed`

Moment is made by [neura-neura](https://github.com/neura-neura/Moment).
