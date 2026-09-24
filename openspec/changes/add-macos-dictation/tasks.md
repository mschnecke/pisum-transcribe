The order follows the dependencies. The shared seams come first, then the helper, then the macOS implementations, then the registration, and the checks at the end. Windows must stay green after every group: `dotnet test Pisum.Transcribe.slnx` on Windows runs unchanged.

## 1. Shared seams

- [ ] 1.1 Add `Dictation/IDictationState` and `DictationState` (D5). `DictationController` sets it active when a press passes the engine-ready check, and inactive wherever `_state` returns to `Idle`, through one pair of methods, `BeginDictation` and `EndDictation`. That includes `StopAsync`, which sets `Idle` directly after aborting a recording at exit. Verify: `DictationControllerTests` for a completed dictation, a cancelled one, an aborted one, a start that fails, a stop during a recording, and a press while the engine isn't ready (never active).
- [ ] 1.2 Add `Hosting/IProcessActivity` and `Hosting/Windows/NoProcessActivity` (D6). `DictationController` begins an activity in `BeginDictation` and disposes it in `EndDictation`. `TranscribeCppTranscriber.ProcessWorkItems` wraps each work item, and `VoiceActivityWarmupService.WarmUp` wraps the warm-up. Verify: tests with a recording fake: one activity per dictation that ends with it, also on failure; one activity per load and per run work item; one around the warm-up; and none while idle (spec `dictation` "Background activity").
- [ ] 1.3 Add `Dictation/IHotkeyAvailability`, `HotkeyUnavailableReason` and `Dictation/Windows/AlwaysAvailableHotkey` (D4). `DictationFeedback` takes the availability, renders its reason in the idle phase after the engine's reasons, and re-renders on `Changed`. Add `DictationMessages` for "Accessibility access needed for the hotkey" and "Paused while secure input is on". Verify: `DictationFeedbackTests` for each order of the reasons, for the reason ignored during recording and transcribing, and for the return to ready (spec `dictation` "Tray icon states").
- [ ] 1.4 Add `InsertionOutcome.KeystrokesNotAllowed` and `IKeyboardInput.CanPostEvents`, always `true` in `Windows/SharpHookKeyboardInput`. `TextInserter` checks it at the final gate right after secure input and falls back with `KeystrokesNotAllowed` (D10, spec `text-insertion` "Keystroke permission" and "Insertion outcome"). Verify: new `TextInserterTests` with a fake keyboard input: the outcome, the transcript on the clipboard without exclusion and without a restore, no keystrokes, and secure input winning when both apply.
- [ ] 1.5 Add `case InsertionOutcome.SecureInputOn` and `case InsertionOutcome.KeystrokesNotAllowed` to `DictationController.NotifyOutcome`, with `DictationMessages.SecureInputReason` and `KeystrokesNotAllowedReason` (D8, D10). Verify: `DictationControllerTests` cases show the "copied to the clipboard" notification with "secure input is on, for example in a password field" and with "Accessibility access isn't in effect" (spec `dictation` "Insertion fallback notification").
- [ ] 1.6 Register `IOverlayPlatform` in `AddDictation()` and pass it to `RecordingOverlayWindow` from `DictationFeedback`. Change `GetWorkArea` to `GetWorkArea(Window overlay, nint targetWindow, out uint dpi)` with the new contract, and call `Configure` before and after `Show` in `ShowStarting` (D2). Verify: `RecordingOverlayWindowTests` pass with `FakeOverlayPlatform` and record `Configure` before and after `Show`, and `Dictation/Windows/RecordingOverlayWindowHardwareTests` pass on Windows (the Windows run happens in CI and by hand on Windows).

## 2. The Swift helper

- [ ] 2.1 Add `Overlay.swift` with `pisum_overlay_configure(nswindow)` (floating level, `ignoresMouseEvents`, and the collection behavior of D2) and `Activity.swift` with `pisum_activity_begin(reason)` and `pisum_activity_end(token)` (D6, D9). Raise `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` to 4, and add the functions to `Hosting/MacOS/PisumMac`. Verify: `MacNativeLibraryIntegrationTests` see ABI 4; a macOS `Integration` test begins and ends an activity and finds it in `pmset -g assertions` meanwhile; a null window pointer returns an error status.

## 3. The macOS implementations

- [ ] 3.1 Add `Hosting/MacOS/MacProcessActivity` on the helper, a no-op with one log line when `MacNativeLibrary.IsAvailable` is false (D6). Verify: a unit test for the unavailable path; the `Integration` test of 2.1 goes through it.
- [ ] 3.2 `MacKeyboardInput.CanPostEvents` calls `CGPreflightPostEventAccess()` through `DllImport` (D10). Verify: a macOS `Hardware` test with the Accessibility grant of the terminal or IDE reads `true`, and skips without the grant; the revoke and re-grant case is part of the check by hand (4.2).
- [ ] 3.3 Add `TryReadFrame` to `IFocusedWindowReader` and `AccessibilityFocusedWindowReader` (`AXPosition`, `AXSize`, `AXValueGetValue`). `MacForegroundWindowTracker` reads it at the capture and returns it from `TryGetFrame(captureNumber, out frame)` for the latest capture only. Register the tracker as itself too (D3). Verify: unit tests behind the fake reader: the frame of the latest capture, none for an older capture number, and a capture that succeeds without a frame; a macOS `Hardware` test reads a TextEdit window's frame and compares it with the window server's bounds (`TextEditDocument`, skipped without the Accessibility grant).
- [ ] 3.4 Add `Dictation/MacOS/MacOverlayPlatform` (D2): the frame from the tracker, `ScreenFromPoint` with the primary screen as the fallback, `WorkingArea` with `dpi = 96`, and `Configure` through `pisum_overlay_configure`. Add the `Dictation/MacOS/` entries to both `.csproj.DotSettings` files. Verify: unit tests of the screen choice behind a screens seam (a frame on the primary screen, on a second screen with a negative origin, off every screen, and no frame); a macOS `Hardware` test on Avalonia.Native that shows the overlay for a TextEdit target and checks, through `CGWindowListCopyWindowInfo`, that its bounds are the bottom center of `NSScreen.visibleFrame`, flipped, and that it is at the floating level (spec `dictation` "Recording overlay behavior").
- [ ] 3.5 Add `Dictation/MacOS/MacHotkeyAvailability` as a hosted service (D4): `IPermissions.IsAccessibilityInEffect` and its change event, the 2 s `ISecureInput` poll on the UI thread through `TimeProvider`, paused while `IDictationState.IsActive`. Verify: unit tests with `FakeTimeProvider`: the Accessibility reason before secure input; a secure-input change seen within one interval; no poll during a dictation and a poll again after it; and `Changed` raised only when the reason changes.
- [ ] 3.6 `RelaunchService` takes `IDictationState` and waits before the relaunch while a dictation is active, after the download wait and the notice, logging once (D7). Verify: `RelaunchServiceTests`: a grant during a dictation relaunches only after `ActiveChanged` to inactive; a grant with the setup window open waits past its notice; and a dictation that ends before the notice ends changes nothing (spec `macos-permissions` "Relaunch after the Accessibility grant").

## 4. Registration and end to end

- [ ] 4.1 In `AppHost.Create`, call `AddVoiceActivity()` and `AddDictation()` on both platforms, in the same order, and register `IProcessActivity` per platform. `AddDictation()` picks `MacHotkeyAvailability` or `AlwaysAvailableHotkey` and the overlay platform (D1). Verify: `AppHostMacTests` resolves `IDictationState`, `IHotkeyAvailability`, `IOverlayPlatform`, `IProcessActivity` and every hosted service; the Windows host-building tests still pass.
- [ ] 4.2 Run the dev bundle (`dotnet run --project src/Pisum.Transcribe -f net10.0`) and check by hand on the dev Mac. Verify, with each result noted in the PR:
  - dictate into TextEdit: the menu bar icon is red, then amber, then the template; the overlay sits above the Dock; the text appears
  - the very first dictation after a start goes into TextEdit in full screen on its own Space; the overlay shows there and TextEdit keeps the focus
  - Mission Control and Command+Tab during a hold don't show the overlay; clicks go through it
  - `pmset -g assertions` shows the activity during the model load at start and during a dictation, and none while idle
  - Terminal with Secure Keyboard Entry: the icon shows "Paused while secure input is on" within 3 s and returns within 3 s
  - a password field focused during a transcription: the notification with the secure-input reason, and nothing typed into the field
  - the microphone permission turned off: the notification names *System Settings → Privacy & Security → Microphone*, and the orange indicator stays off
  - **Quit Pisum Transcribe** and a logout during a recording: the orange indicator goes off before the process ends, and nothing is inserted
  - revoking Accessibility while running: the icon says "Accessibility access needed for the hotkey"; a revoke during a long transcription followed by a re-grant: the transcript is left on the clipboard with "Accessibility access isn't in effect", and the relaunch follows only after the dictation has ended
  - a second display, if one becomes available: the overlay on the target window's screen; otherwise noted as not checked
- [ ] 4.3 Run the checks by hand moved here from `add-macos-text-insertion` (its task 4.3), now through real dictations: the *ask* state, a clipboard manager, Spotlight's clipboard history, Universal Clipboard, a launcher panel, 1Password, and dictating "Grüße aus Köln – 5 € 👋" over two lines into Terminal, iTerm2, VS Code, a JetBrains IDE and, if one is available, a UTM or Parallels guest. Verify: each result noted in the PR; every app except the guest gets the exact text. Spotlight, the launcher panel and the guest are only recorded.

## 5. Docs and validation

- [ ] 5.1 Update the docs. Verify: the texts match the code.
  - `CLAUDE.md`:
    - the layout: `Dictation/MacOS/`, `Hosting/IProcessActivity` and the helper's new files
    - the macOS registration of `AppHost.Create`, which now includes voice activity detection and the dictation
    - the activity functions as the third exception to the UI-thread rule
    - ABI 4
    - the tray's hotkey reasons
  - `docs/roadmap.md`:
    - the checkpoint after #18 done, with its numbers and the unchanged defaults
    - #18 and #19 merged
    - this change's status
- [ ] 5.2 Run `openspec validate add-macos-dictation --strict`, then `dotnet build Pisum.Transcribe.slnx` and `dotnet test Pisum.Transcribe.slnx` on the Mac, and CI on Windows and macOS. Verify: all pass.
