# Moment verification report

## Scope

This report covers the standalone Moment native application. The manual Windows plan remains pending and has not been executed.

## Automated coverage

- Native WinUI 3 Release build.
- Recurring-note creation, insertion positions, heading behavior, timestamps, localized filename formats, prefixes, and collision reuse.
- Configurable output folders inside or outside the selected workspace.
- WebM voice storage and configurable transcript destinations.
- Durable pending, completed, and failed voice queues.
- Silent-capture rejection before queueing.
- Text PiP pin behavior and the optional remembered pin preference remain manual-only because they require native pointer, focus, and window-activation events.
- Verified Whisper download finalization.

## Manual-only coverage

The following require a real Windows session and are listed in [MANUAL-TEST-PLAN.md](MANUAL-TEST-PLAN.md): microphone permission, live waveform/no-audio behavior, physical F13-F24 or Stream Deck input, tray activation, installer finish-page launch, shortcut focus, and WinUI visual inspection.
