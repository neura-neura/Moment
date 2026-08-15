# Moment manual test plan

Automated checks cover recurring-note transforms, insertion modes, headings, localized filenames, timestamps, configurable output folders, WebM routing, durable voice jobs, the native Release build, and verified Whisper downloads. The checks below require a real Windows microphone, physical F13-F24 or Stream Deck input, WinUI rendering, Windows tray behavior, or human visual inspection. **This plan has been updated for the standalone Moment app and has not been executed.**

## 1. Test workspace

1. Create or back up a disposable workspace whose path contains spaces.
2. Configure the recurring-note folder, filename format, and insertion behavior in Moment.
3. Moment must be the only capture process.
4. Confirm the workspace contains no `.moment` folder before the first capture.

## 2. First launch, tray, and installer

1. Install `MomentSetup-x64.exe` with the Desktop shortcut option enabled by default.
2. Leave **Launch Moment** checked on the installer finish page and verify the app opens in the foreground.
3. Close the settings window and verify Moment remains in the notification tray.
4. Open Moment from the tray and verify the settings window is restored and focused.
5. Launch Moment again while it is already running. Verify no second process is created: the existing window is restored and focused.
6. Enable **Start with Windows**, sign out/in, and verify Moment starts in the tray without stealing focus.
7. Uninstall Moment and verify its installed files, shortcuts, and uninstall entry are removed without deleting the selected workspace or its notes.

## 3. Workspace and recurring-note settings

1. Choose the disposable workspace and save settings.
2. Verify the UI says **Selected workspace** and the button says **Choose workspace...**.
3. Choose a **Text notes folder** with **Browse...** and verify the selected absolute path is displayed.
4. Choose a folder outside the workspace and save. Verify the external folder is accepted and used for the next Text Note.
5. Configure a filename format with localized month text and a prefix such as `Journal-`.
6. Verify **Recurring note** explains that one new note is created for each calendar day.
7. Test **End of recurring note**, **Beginning of recurring note**, and **Under a heading**.
8. For **Under a heading**, verify **Target heading** and **Missing heading** appear only for that insertion mode.
9. Test all missing-heading behaviors: create the heading, append at the end, and show an error.
10. Toggle the timestamp option off and confirm a text capture contains no timestamp heading. Toggle it on and confirm the configured format is used.
11. Capture twice with the same filename. Verify the existing recurring note is reused and the second entry is appended without overwriting the first.

## 4. Global shortcuts and text PiP

1. Register ordinary modifier combinations and physical F13-F24/Stream Deck keys.
2. Verify the settings page reports registration failures instead of silently accepting an unavailable shortcut.
3. Trigger the text shortcut while another application is focused.
4. Verify the compact PiP text surface appears at the intended lower-right position, uses native window behavior, receives focus immediately, and accepts typing without a click.
5. Press Enter to save and Shift+Enter to insert a line break. Verify Escape or changing focus closes the surface without saving.
6. Confirm the generated Markdown entry contains the capture text and, when enabled, the timestamp.

## 5. Voice PiP and audio storage

1. Select a physical microphone in **Voice → Recording**.
2. Use **Audio folder → Browse...** to choose a folder inside or outside the workspace, save settings, and verify the next WebM recording is written there.
3. Trigger the voice shortcut and verify the compact centered-top PiP surface appears without a title or unnecessary status text.
4. Speak and verify the waveform responds. Stop with the same shortcut and verify the surface closes without a disabled-state flicker.
5. Confirm the recording is a WebM/Opus file in the configured **Audio folder**.
6. Start a recording with a muted or silent microphone. Verify the PiP changes to a compact **No audio** warning while recording, the recording is discarded on stop, and no transcription job is created.
7. Cancel a recording and verify no audio file or queue job remains.

## 6. Local transcription

1. Enable local transcription and verify the Whisper status is clear before recording.
2. Use **Install / repair Whisper** and confirm the engine/model files are validated or downloaded into `%LOCALAPPDATA%\Moment\whisper`.
3. Use **Transcriptions folder → Browse...** to choose a folder inside or outside the workspace, save settings, and verify the selected destination is used.
4. Record a spoken note and verify the selected destination:
   - **Text Note** writes to the recurring note.
   - **Separate transcription note** writes to the configured Transcriptions folder.
   - **Both** writes to both destinations.
5. Verify the configured transcription filename format and prefix are applied.
6. Disable or corrupt Whisper deliberately, record a voice note, and verify the audio remains in Voice Notes while Moment shows an actionable Windows notification. Retry only after Whisper is ready.
7. Verify silent recordings are not sent to Whisper.

## 7. Durable queue and restart behavior

1. During a voice capture, close Moment or terminate it after the WebM is finalized.
2. Relaunch Moment and verify the pending job under `.moment\capture\pending` is processed.
3. Verify completed jobs move to `.moment\capture\completed`.
4. Force a missing audio path and verify the job moves to `.moment\capture\failed` with an `.error.txt` sidecar.
5. Use **Retry failed jobs** and verify retryable jobs return to `pending`.
6. Verify the old `.quick-capture` queue is neither created nor read.

## 8. Compatible Markdown editor verification

1. Open the generated recurring note and transcript in any compatible Markdown editor and verify formatting, headings, timestamps, and audio references.
2. Confirm Moment does not need the editor to be running during capture.
3. Verify each configured output folder receives its own output, including folders outside the selected workspace, and that switching to a second workspace routes subsequent queue state only there.

## Completion criteria

Every global shortcut must produce the requested PiP action or a visible registration/error status. Text and voice captures must remain durable across Moment restarts, Windows sign-in, and a closed Markdown editor. Whisper installation, installer behavior, and this manual plan are separate checks; this plan remains intentionally pending execution.
