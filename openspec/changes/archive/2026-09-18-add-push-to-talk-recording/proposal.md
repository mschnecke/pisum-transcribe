## Why

Pisum Transcribe is a hold-to-talk tool: the user holds a key in any application, speaks, and releases the key. That requires a global hotkey that reports both key press and key release (`RegisterHotKey` reports only presses), plus microphone capture in the format Canary expects: 16 kHz mono float. Both have to exist before the dictation workflow can connect them to the transcription engine.

## What Changes

- Add a **global push-to-talk hotkey** based on SharpHook 8.0. It reports *pressed* when all hotkey keys are held, *released* when any of them is let go, and *cancelled* when another key is pressed while holding (e.g. the user typed Right Ctrl+C). Key auto-repeat and simulated key events, such as the app's own paste keystrokes, are ignored. When a key release cannot be seen, because an elevated window, the UAC prompt or the lock screen took focus, the hotkey reports *cancelled* instead of staying held. Key events are used only for the hotkey and are never stored or logged.
- Add the `recording.hotkey` setting, defaulting to **Right Ctrl** held alone. A modifier-only default passes through harmlessly to the focused application, because SharpHook's background hooks cannot suppress keys.
- Add **microphone recording** based on NAudio 3.1 `WasapiRecorder`:
  - Captures from the Windows default recording device and follows default-device changes during a recording.
  - Delivers 16 kHz mono float samples.
  - Opens the microphone only while recording.
  - Stops automatically at a caller-supplied maximum duration.
- Report clear errors for blocked microphone access (Windows privacy settings), a missing microphone, a muted microphone, and a microphone disconnected mid-recording.
- Keep audio in memory only; never write it to disk. `docs/idea.md` suggests the obsolete `WasapiCapture`; NAudio 3 replaces it with `WasapiRecorder`.

## Capabilities

### New Capabilities
- `push-to-talk-hotkey`: The system-wide hold-to-talk key combination. Covers press, release and cancel semantics, auto-repeat and simulated key handling, recovery from a missed release, and the persisted hotkey setting with its default.
- `audio-recording`: Microphone capture for dictation. Covers the output format, default-device behavior, the maximum duration, microphone privacy and error conditions.

### Modified Capabilities
<!-- None. -->

## Impact

- New code: `src/Pisum.Transcribe/Recording/`.
- New dependencies: `SharpHook` 8.0.0 (bundles the native `uiohook.dll`, MIT), `NAudio.Wasapi` 3.1.0 (MIT, pinned: an unlisted `22.0.0` version exists on NuGet and must not be picked up), and `SharpHook.Testing` 8.0.0 for tests only.
- Settings: new `recording` section (`hotkey`).
- OS: Windows shows its microphone-in-use indicator while recording, and microphone access must be allowed for desktop apps under Settings › Privacy & security › Microphone.
- Depends on `scaffold-app-shell`. `add-dictation-workflow` depends on this change, and `add-settings-window` adds the hotkey editor.
