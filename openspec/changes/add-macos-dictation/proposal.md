## Why

Every part of a dictation now works on macOS on its own: the hotkey and the microphone (#17), Metal (#18), text insertion (#19) and the setup with its permissions (#16). But `AppHost.Create` registers voice activity detection and the dictation only on Windows, so nothing connects them, and holding right Command does nothing. This change is the Mac MVP: hold, speak, release, and the text appears.

Tracked in GitHub issue #20. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design, from the handoffs of #16, #17 and #19, and from explore mode on 2026-09-24. Explore mode also ran the benchmark checkpoint after #18 and a placement spike (see the design's Context).

## What Changes

- **Dictation on macOS:** `AddVoiceActivity()` and `AddDictation()` are registered on both platforms. The shared `DictationController` and `DictationFeedback` run unchanged.
- **The recording overlay on macOS:**
  - it's the same Avalonia window
  - the Swift helper sets what Avalonia doesn't expose: a floating level, click-through, and a `collectionBehavior` that keeps the overlay out of Mission Control and shows it over a full-screen app on that app's Space
  - it's placed at the bottom center of the target window's screen, without the menu bar and the Dock
- **The menu bar icon follows the dictation** with the existing glyphs: a template at rest, red while recording, amber while transcribing.
- **The icon says when the hotkey can't work.** While no dictation runs, the menu bar icon shows *unavailable* with the reason:
  - when the Accessibility grant isn't in effect
  - when Secure Event Input is on, for example in a password field or Terminal with Secure Keyboard Entry

  Until now, the icon would have said "Ready" while the hotkey was dead. On Windows neither reason occurs.
- **The secure-input fallback notification:** a dictation that ran into secure input says that the text was copied to the clipboard because secure input is on.
- **No silent loss after a revoke and a re-grant:** macOS lets only a new process send keystrokes after a new Accessibility grant, and a dropped keystroke gives no error. Right before the keystrokes, the insertion therefore checks whether it may send them. If not, the transcript is left on the clipboard with the new outcome "keystrokes not allowed" and a notification, instead of a paste that goes nowhere followed by a clipboard restore that erases the transcript.
- **App Nap:** the model load and warm-up, each transcription, the voice activity warm-up and every dictation from the press until the insertion ends run inside a `ProcessInfo` activity, so macOS doesn't throttle them.
- **The relaunch after the Accessibility grant waits while a dictation is in progress,** so a re-grant during a transcription doesn't lose the transcript.
- **macOS wording:**
  - the blocked-microphone notification names *System Settings → Privacy & Security → Microphone* (the text exists since #17)
  - an exit during a recording turns off the orange microphone indicator
  - the menu item is **Quit Pisum Transcribe**, and a logout counts as a sign-out
- **Unchanged defaults:** the checkpoint after #18 confirmed them. Metal and Canary 1B v2 Q8_0 stay the Mac defaults.
- **Not included:**
  - opening System Settings by clicking a notification (both platforms, its own change)
  - open at login (#21)
  - the `.pkg` (#22)
- **Windows doesn't change,** apart from internal seams: the hotkey availability, which is always available there; the process activity, a no-op there; the dictation state; the keystroke permission, which is always granted there; and the overlay platform, now resolved through DI.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `text-insertion`:
  - "Keystroke permission" (new, next to "Secure input"): no keystrokes while macOS doesn't allow the application to send them.
  - "Insertion outcome": the outcome "keystrokes not allowed".
- `dictation`:
  - "Tray icon states": the macOS reasons for *unavailable* (Accessibility not in effect, secure input on), and the menu bar wording.
  - "Insertion fallback notification": the reasons "secure input is on" and "Accessibility access isn't in effect" on macOS.
  - "Error notifications": the macOS path of the blocked-microphone message.
  - "Dictation ends when the application exits": the orange microphone indicator, **Quit Pisum Transcribe** and a macOS logout.
  - "Recording overlay behavior": Mission Control, the window switcher and full-screen Spaces on macOS, and the screen's visible frame.
  - "Background activity" (new): on macOS, dictations and engine work aren't throttled by App Nap.
- `macos-permissions`:
  - "Relaunch after the Accessibility grant": the restart also waits while a dictation is in progress.

## Impact

- **Depends on:** #16, #17, #18 and #19, all merged. `add-macos-text-insertion` is archived, and its open check by hand (4.3) moves here.
- **Code:**
  - `Hosting/AppHost`: `AddVoiceActivity()` and `AddDictation()` on macOS.
  - `Hosting/`: `IProcessActivity`, with `MacOS/MacProcessActivity` (the helper) and `Windows/NoProcessActivity`.
  - `Dictation/`: `IHotkeyAvailability` (with `MacOS/MacHotkeyAvailability` and `Windows/AlwaysAvailableHotkey`), `IDictationState`, `MacOS/MacOverlayPlatform`, and the platform resolved through DI instead of `RecordingOverlayWindow.CreatePlatform`. `DictationFeedback` renders the availability. `DictationController` begins an activity, sets the dictation state, and handles `InsertionOutcome.SecureInputOn`.
  - `TextInsertion/`: `InsertionOutcome.KeystrokesNotAllowed`, and `IKeyboardInput.CanPostEvents`, which `TextInserter` checks at the final gate next to secure input.
  - `TextInsertion/MacOS/`: the tracker also reads the target window's frame (`AXPosition`, `AXSize`) at the capture, and `MacKeyboardInput.CanPostEvents` calls `CGPreflightPostEventAccess()`.
  - `Transcription/TranscribeCppTranscriber` and `VoiceActivity/VoiceActivityWarmupService`: an activity around each work item and around the warm-up.
  - `Permissions/MacOS/RelaunchService`: waits for `IDictationState`.
- **Swift helper:**
  - `Overlay.swift` (`pisum_overlay_configure`) and `Activity.swift` (`pisum_activity_begin`, `pisum_activity_end`)
  - `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` go from 3 to 4
  - the activity functions are safe on any thread, a third exception to the UI-thread rule
- **Tests:**
  - unit tests for the availability, the feedback's new states, the controller's activity and state, the relaunch wait and the overlay platform's placement
  - macOS `Integration` tests of the new helper functions
  - macOS `Hardware` tests of the overlay's native settings and placement
- **Docs:**
  - `CLAUDE.md`: layout, the macOS registration, ABI 4, the thread rule
  - `docs/roadmap.md`: the checkpoint after #18, and the planning state
