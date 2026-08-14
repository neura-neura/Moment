# Manual Obsidian and WinUI bridge test plan

Automated checks cover Daily Note transforms, provider settings, templates, insertion modes, headings, frontmatter, timestamps, paths with spaces, WebM routing, native migration, durable voice jobs, and the native Release build. The checks below require a real Obsidian Desktop instance, Windows microphone permission, WinUI rendering, physical F13-F24/Stream Deck input, or human visual inspection. **This plan has been updated for native bridge ownership but has not been executed.**

## Preparation

1. Create or back up a disposable vault whose path contains spaces.
2. Enable Daily Notes or the Periodic Notes daily provider and configure a folder, filename format, and template.
3. If the vault previously used the two capture plugins, their folders may remain for comparison. Do not depend on them for the native test.
4. Build Moment with `npm run package:bridge`, or run the published installer. The local installer is `quick-capture-bridge-winui/dist/MomentSetup-x64.exe`.
5. Verify that the installer opens a real setup wizard (not a 7-Zip extraction dialog), does not request an MSIX certificate, offers selectable Start menu and Desktop shortcut components, appears in Apps & features, and offers an uninstaller.
6. Select both shortcut components, finish with **Launch Moment** enabled, and verify the app opens visibly in the foreground from the Finish page. Verify both shortcuts launch the same `Moment.exe`.
7. Launch Moment, choose this disposable vault, configure the Text Note and Voice pages, and save. Leave Moment running. Do not run this manual plan during automated verification.

## WinUI bridge setup and registration

1. Verify the native WinUI 3 surface uses a left NavigationView with **Text Note**, **Voice**, **Shortcuts**, and footer **Settings** sections.
2. Click the voice shortcut field and press physical `F13`; verify the field displays `F13`. Repeat with `F24` and with `Ctrl + Shift + F13`.
3. Click the text shortcut field and press physical `F14`; verify the field displays `F14`. Repeat with a letter, arrow, and `Alt + Shift + letter`.
4. Click **Register shortcuts**. Verify each row reports **Registered** and the status identifies both bindings.
5. Intentionally choose the same binding for voice and text. Verify text reports that the bindings must be different while voice remains independently registered.
6. Register a key already owned by another application. Verify the row reports the conflict and the other row remains usable.
7. Save settings, close and relaunch Moment, and verify the vault and bindings persist in `%LOCALAPPDATA%\Moment\settings.json`.
8. Enable **Start with Windows**, sign out/in or restart the app, and verify the bridge starts minimized and still registers both shortcuts.
9. With **Keep running in the tray when the window is closed** enabled, close the settings window. Verify the process remains active, the tray icon is visible, and the tray Exit command stops the process.
10. On a fresh bridge launch, press the voice shortcut once, speak, and press it once to stop. Verify the first stop press closes the recording; a third press must not be required.

## Native Text Note capture

1. On the Text Note page, verify the text-file insertion mode, target heading, missing-heading behavior, timestamp format, Enter-to-save, and close-after-save values.
2. Delete today's test daily file. Press the text shortcut, type `Research this concept.`, and press Enter.
3. Verify the small titleless writing canvas appears above the taskbar, owns focus immediately, saves once, and closes without a disabled-frame flash.
4. Open today's note in Obsidian and verify the provider template remains intact and the entry uses the invocation minute.
5. Set insertion to **Under a heading -> Notes** and repeat with multiline Spanish, Chinese, Japanese, and emoji text using Shift+Enter.
6. Rename/remove the target heading and test create, append, and error behavior.
7. Test **Beginning of note** with YAML frontmatter and verify frontmatter remains first.
8. Trigger several captures rapidly and verify every entry survives without stale overwrites.
9. Keep today's note open and edited while capturing; verify the bridge's atomic update preserves the latest file content.
10. Move focus to another application while the editor is open. Verify the editor closes like Escape and does not save.

## Native voice capture and routing

1. On the Voice page, choose an input device, audio folder, bitrate, destination, audio-embed option, and voice prefix. Verify the controls persist after saving. If Windows default is a virtual cable, choose the physical microphone explicitly.
2. Press the voice shortcut. Verify only the compact native top-center island appears, recording starts automatically, and it shows the red recording ring, moving level bars, and native Stop/Cancel controls without status text.
3. Leave the selected microphone silent for about two seconds. Verify the recording island keeps its original compact size and replaces the wave bars with a small red **No audio** warning while recording is still active. Speak into the selected microphone and verify the warning clears without interrupting recording.
4. Speak for several seconds, then press the same shortcut again. Verify the island disappears immediately without a disabled-button flash and a WebM/Opus file exists under the selected audio folder.
5. Verify a durable JSON job appears under `.quick-capture\bridge-inbox` and points to the WebM file with the correct relative path and MIME type.
6. With destination **Separate transcription note** and transcription disabled, verify a Markdown note is created with the optional WebM embed.
7. With destination **Text Note**, verify the audio entry is inserted using the recording start timestamp. With **Both**, verify both Markdown destinations exist.
8. Deny microphone permission and verify a human-readable Windows microphone error is shown without leaving a partial job.
9. Start another recording, press Escape or Cancel, and verify no new Markdown/job output is created.
10. Close Obsidian completely, capture text and voice, then open Obsidian. Verify all output is already present without requiring either capture plugin.
11. Inspect `.quick-capture\bridge-processed` and `.quick-capture\bridge-failed`. Force a missing audio path and verify the failure sidecar is retained for diagnosis. Record once from a muted/silent device and stop: verify the inline **No audio** warning appeared during recording, the WebM is discarded, no job is enqueued, and Whisper is not invoked. Existing legacy silent jobs should explain that Whisper is installed and no speech was detected without telling the user to reinstall it.
12. Change the bridge to a second vault, repeat one text and one voice capture, and verify output routes only to the second vault.

## Text Note and Voice settings

1. Select **End of daily file** and verify Target heading and Missing heading are hidden. Select **Under a heading** and verify both fields appear together.
2. Use **Create the heading**, **Append at the end**, and **Show an error** with a missing target heading and verify each behavior.
3. Uncheck **Add capture timestamp** and verify both text and voice entries omit the timestamp heading while still using their normal destinations.
4. Use the Audio folder and Separate note folder **Browse...** buttons. Verify the selected paths are stored relative to the selected vault and default to `Voice Notes` and `Voice Transcriptions`.
5. Choose destination **Text Note**, **Separate transcription note**, and **Both**. Verify Separate note folder is hidden only for Text Note.
6. Hover the Fluent `Info` help markers and verify each tooltip uses the native system-colored rounded tooltip and explains the associated setting.

## Local Whisper

1. Enable transcription and select **Base multilingual**.
2. Choose **Install / repair Whisper** and verify progress/status updates and no terminal window appears.
3. Record a clear English sentence, a Spanish sentence, and, if possible, Chinese/Japanese speech. Verify separate notes contain real Whisper output and audio embeds.
4. Temporarily rename `whisper-cli.exe` or the selected model, record again, and verify the audio survives with a clear repair instruction and a failed job sidecar.
5. Select a model that is not installed and verify status reports it missing until installed.
6. Make two short recordings in rapid succession and verify both audio files save immediately while transcriptions run one at a time.
7. Block the network and click **Install / repair Whisper**. Verify a clear HTTP/checksum error appears and no partial model is treated as ready.

## Obsidian compatibility and visual inspection

1. Open the resulting text-note file in Obsidian and verify audio embeds resolve from the configured relative path.
2. If the legacy plugins remain enabled for comparison, verify their in-Obsidian commands still work and the public Daily Capture API remains compatible. This is optional and is not part of native bridge ownership.
3. Test the settings and both PiP panels in light and dark themes, at 200% UI zoom, with a narrow window, and with reduced motion enabled.
4. Verify visible keyboard focus on shortcut fields, the text editor, and recorder buttons. With NVDA if available, verify names for the recording indicator and Stop/Cancel controls.
5. From Settings, click **Check for updates**. Verify the current release reports that it is up to date and the status wraps inside the card. For a newer release, verify the installer checksum is validated after the download stream closes, Moment exits, and the normal NSIS installer starts without a file-lock error.

## Expected result

Every global shortcut must produce either the requested PiP action or a visible registration/error status. Native text and voice captures must remain durable across Obsidian being closed, bridge restarts, and removal of both legacy plugins. Whisper installation, bridge installation, and this manual plan are separate checks; this plan is intentionally pending execution.
