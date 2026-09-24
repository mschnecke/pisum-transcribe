## Context

See proposal.md for the motivation. The Mac already has every part of a dictation, and `AppHost.Create` doesn't register the two that connect them: `AddVoiceActivity()` and `AddDictation()` sit inside `#if WINDOWS`. Once they are registered, the shared code runs as it is. This design covers what breaks or is missing on the Mac at that point.

**What the code has today:**
- `RecordingOverlayWindow.CreatePlatform()` throws `PlatformNotSupportedException` outside Windows. `IOverlayPlatform.GetWorkArea(nint targetWindow, out uint dpi)` returns physical pixels and a DPI that `CalculateBounds` scales by.
- On macOS, `InsertionTarget.Window` is a capture number. `MacForegroundWindowTracker` keeps the AX element of the latest capture, and #19's design hands the overlay's placement to this change: read `AXPosition`/`AXSize` of the retained element, with no second capture.
- `DictationFeedback.Render()` decides the tray state from the dictation phase and `TranscriberStatus` only. `IHookAccess` and `IPermissions.IsAccessibilityInEffect` have no consumer outside Recording and Permissions, and `ISecureInput` is read only by `TextInserter`.
- `DictationController.NotifyOutcome` is a `switch` without a default and no `SecureInputOn` case, so that outcome is only logged.
- `TextInserter`'s final gate checks the foreground window (AX) and secure input. `CoreGraphicsKeyEvents` posts with `CGEventPost`, which returns `void`, so a keystroke that macOS drops can't be detected afterwards. After a revoke, the AX read fails, and the insertion falls back as "target window changed". After a re-grant in the same process, the AX read works again at once, while posting events only works in a new process (spike M3).
- `TranscribeCppTranscriber` runs every load, warm-up, transcription and reload as a work item on one worker thread (`ProcessWorkItems`). `VoiceActivityWarmupService` warms up on `Task.Run` at start.
- `RelaunchService` waits for `IModelStore.IsDownloading` before it relaunches, and knows nothing about dictations. `DictationController` is registered with `AddHostedService<>`, so no other service can resolve it.
- The menu bar glyphs for recording and transcribing already exist in `Tray/MacOS/`, and the tray icon shows them once `DictationFeedback` sets the status.
- The Swift helper is at ABI 3. Its functions are called on the UI thread, with the pasteboard's as the exceptions.

**The benchmark checkpoint after #18** (explore mode, 2026-09-24, MacBook Air M4 with 16 GB, macOS 27, a 10.5 s German clip recorded on the built-in microphone, German to English):

| Model | Backend | Load | Warm-up | Warm median of 5 |
|---|---|---|---|---|
| canary-1b-v2-q8_0 | Metal | 0.59 s | 0.25 s | 0.25 s |
| canary-1b-v2-q8_0 | CPU | 0.47 s | 0.13 s | 0.58 s |
| canary-1b-v2-q4_k_m | Metal | 0.25 s | 0.25 s | 0.23 s |
| canary-1b-v2-q4_k_m | CPU | 0.23 s | 0.08 s | 0.59 s |

- **Long clips on Metal** (Q8_0, max audio 400 s): 30 s took 0.69 s, 60 s 1.12 s, 120 s 3.04 s, 240 s 8.87 s and 399 s 19.01 s. None ran out of memory, so the return to the GPU after an out-of-memory error doesn't fire on this machine within the limit.
- **A cancelled run on the CPU** returned after 3.36 s for 60 s of audio, 8.23 s for 120 s and 51.96 s for 399 s.
- **The first run after either warm-up input on Metal** took 0.24 s and 0.25 s, so there is no first-run penalty.
- **Accuracy:** Q4_K_M produced the same German transcript as Q8_0, and differed only in a misheard name in the translation. The clip had only about 5 s of quiet speech, which is too little to justify a change of the default.
- **Decision (user):** Metal and Q8_0 stay the Mac defaults.

**The placement spike** (explore mode, 2026-09-24, Avalonia 12.1.1, the built-in display at 1470×956 points, Retina):
- `Screen.Bounds` (0,0 1470×956) and `Screen.WorkingArea` (0,33 1470×866) are in **points with a top-left origin**. `WorkingArea` equals `NSScreen.visibleFrame` flipped: the menu bar is 33 pt, the Dock 57 pt.
- `Window.Position` is in the same space. A window placed at (615, 827) had the window server bounds (615, 827, 240×48).
- `Screen.Scaling` reported **1** while `RenderScaling` was 2, so `Screen.Scaling` can't serve as a DPI source on macOS.
- `Topmost="True"` put the window at layer 3, `NSFloatingWindowLevel`.
- The AX frame (`AXPosition`, `AXSize`) uses the same space: global points with the origin at the primary screen's top-left.
- No second display was available, so multi-monitor placement is a check by hand.

## Goals / Non-Goals

**Goals:**
- The shared `DictationController`, `DictationFeedback` and `RecordingOverlayWindow` run on macOS without platform branches inside them. Platform differences stay behind seams registered by `Add<Feature>()`.
- Nothing on the UI thread waits for another application: no AX call and no pasteboard read there.
- Windows behaves exactly as before. Its new seam implementations are constants or no-ops.

**Non-Goals:**
- Clickable notifications that open System Settings. The blocked-microphone message names the path, and click actions come later for both platforms.
- The hook failing for reasons other than the Accessibility grant, such as another event tap error. The notification that #17 shows stays the only feedback for it.
- Following the target window when it moves during a hold. The overlay stays where the dictation started, as on Windows.
- A second display. The placement handles it, but it is checked by hand only when one is available.

## Decisions

### D1: Register voice activity detection and the dictation on both platforms

`AppHost.Create` moves `AddVoiceActivity()` and `AddDictation()` out of `#if WINDOWS`, in the same order: after `AddTextInsertion()`, so the host starts the dictation last and stops it first. ONNX Runtime ships for osx-arm64, and the Silero unit tests already run on the macOS CI. The macOS host-building test (`AppHostMacTests`) resolves every hosted service, which catches a missing registration.

### D2: The overlay platform through DI, with the placement in `Window.Position`'s units

- `AddDictation()` registers `IOverlayPlatform`: `Win32OverlayPlatform` on Windows and `MacOverlayPlatform` on macOS. `DictationFeedback` creates `new RecordingOverlayWindow(platform)` with it. The parameterless constructor stays for the XAML loader: on Windows it keeps creating `Win32OverlayPlatform`, and on macOS it creates a `MacOverlayPlatform` without a tracker, which places on the primary screen.
- `GetWorkArea` also receives the overlay window, because Avalonia's `Screens` belongs to a `TopLevel`: `GetWorkArea(Window overlay, nint targetWindow, out uint dpi)`. Its contract becomes "the work area in `Window.Position`'s units, and the DPI that `CalculateBounds` scales the overlay by". Win32 keeps physical pixels and the monitor's DPI and ignores the window.
- `MacOverlayPlatform.GetWorkArea`:
  1. asks the tracker for the frame of the capture (D3); without one, it uses the primary screen
  2. picks `overlay.Screens.ScreenFromPoint(center of the frame)`, falling back to the primary screen when the center is off every screen
  3. returns that screen's `WorkingArea` with `dpi = 96`, so the overlay keeps its size in points and macOS scales it for Retina (spike: `Screen.Scaling` is unreliable)
- `MacOverlayPlatform.Configure` gets the `NSWindow` pointer from `TryGetPlatformHandle()` and calls `pisum_overlay_configure`. It sets:
  - `level` = `.floating`
  - `ignoresMouseEvents` = true
  - `collectionBehavior` = `.transient`, `.ignoresCycle`, `.fullScreenAuxiliary`, `.canJoinAllSpaces`

  All of it is idempotent. The `level` duplicates what `Topmost` gives today, so the overlay doesn't depend on how Avalonia maps `Topmost`.
- **Configure also runs before the first Show,** not only after each Show. The collection behavior must be in place before the window is ordered front for the first time, or the first show can land on another Space (spike M2 applied it before the first `Show`). `RecordingOverlayWindow.ShowStarting` calls `Configure` before `Show()` and again after it. On Windows the call before `Show` is harmless, because Avalonia resets the extended styles on every `Show` and the call after it sets them again.

*Rejected:*
- **NSScreen through the helper:** a second coordinate system (bottom-left origin) and a conversion that Avalonia's `Screens` already does (spike).
- **Reading the AX frame in `GetWorkArea`:** it runs on the UI thread. A hung target app would hold the UI thread for the AX timeout (0.25 s).
- **An `NSPanel`:** spike M2 showed that the Avalonia window keeps the target app frontmost.

### D3: The tracker reads the target's frame at the capture

- `IFocusedWindowReader` gets `TryReadFrame(nint window, out Rect frame)`, which reads `AXPosition` and `AXSize` (`AXValueGetValue` with `kAXValueCGPointType` and `kAXValueCGSizeType`) under the existing 0.25 s messaging timeout.
- `MacForegroundWindowTracker.CaptureForeground` reads the frame right after the window, on the controller's thread, and stores it with the capture number. `TryGetFrame(nint captureNumber, out Rect frame)` returns it only for the latest capture.
- `AddTextInsertion()` registers `MacForegroundWindowTracker` as itself too, as `MacKeyboardInput` is, so `MacOverlayPlatform` can take it.
- When the frame can't be read, the capture still succeeds and the overlay goes to the primary screen. The target's identity matters for insertion. The frame only matters for placement.

### D4: The hotkey's availability, one seam for the tray

`Dictation/IHotkeyAvailability`:
- `HotkeyUnavailableReason? Reason`, with the values `AccessibilityNotInEffect` and `SecureInputOn`
- `event EventHandler? Changed`, raised on the UI thread

`AddDictation()` picks the implementation:
- **`Windows/AlwaysAvailableHotkey`:** `Reason` is always null, and it never raises `Changed`.
- **`MacOS/MacHotkeyAvailability`,** a hosted service:
  - it reads `IPermissions.IsAccessibilityInEffect` and follows `AccessibilityInEffectChanged`
  - while `IDictationState.IsActive` is false, it polls `ISecureInput.IsEnabled` every 2 s on the UI thread through `TimeProvider`
  - when the reason changes, it raises `Changed`
  - Accessibility comes before secure input, because without the grant the secure-input state is irrelevant

`DictationFeedback` takes `IHotkeyAvailability`. `Render()` shows the availability only in the idle phase, and only after the engine's reasons:

```
phase Recording / Transcribing  -> as today
engine not Ready                -> unavailable, engine reason (as today)
availability.Reason != null     -> unavailable, "Accessibility access needed for the hotkey"
                                                 or "Paused while secure input is on"
otherwise                       -> ready, "Ready (<backend>)"
```

- **No notification** when secure input turns on, because every password field would raise one. The revoke notification from #17 stays as it is.
- **The poll pauses during a dictation.** The tray shows the dictation's phase then anyway, and the insertion checks secure input itself at its final gate. `IDictationState.ActiveChanged` restarts it.
- **The poll runs on the UI thread,** where #19's risk note placed it: if a later macOS asserts the main thread for `IsSecureEventInputEnabled`, the value is already read there.

*Rejected:*
- **Reading `IHookAccess` or the hook's state:** `IHookAccess.IsAllowed` is the same value as `IsAccessibilityInEffect`, but it has no change event, and the hook itself has no "running" state to observe.
- **Only the secure-input hint (#19's handoff):** the icon would say "Ready" while the Accessibility grant isn't in effect, which contradicts the **Set up** item next to it (user decision).

### D5: `IDictationState`, set by the controller

- `Dictation/IDictationState` has `bool IsActive` and `event EventHandler? ActiveChanged`, raised on the thread that changed it. `DictationState` is a singleton registered in `AddDictation()`.
- `DictationController` sets it active when a press passes the engine-ready check, right before the capture and `ShowStarting`, and inactive wherever `_state` returns to `Idle`: after the insertion, after a cancel or abort, and when the start fails.
- The activity of D6 begins and ends at the same two points, so the controller has one pair of methods for the span, `BeginDictation` and `EndDictation`.
- Consumers:
  - `MacHotkeyAvailability` pauses its poll (D4)
  - `RelaunchService` waits (D7)
  - on Windows nobody reads it

*Rejected:* exposing the controller as a singleton next to its hosted service. The state is all that others need, and a small interface keeps Permissions from depending on the controller.

### D6: App Nap through `IProcessActivity`

- `Hosting/IProcessActivity.Begin(string reason)` returns an `IDisposable`.
  - `Hosting/MacOS/MacProcessActivity` calls `pisum_activity_begin(reason)`, which returns a retained token (`Unmanaged.passRetained` of the `NSObjectProtocol` from `ProcessInfo.processInfo.beginActivity(options: .userInitiated, reason:)`), and `Dispose` calls `pisum_activity_end(token)`.
  - Without the helper (`MacNativeLibrary.IsAvailable` false), it returns a no-op and logs once.
  - `Hosting/Windows/NoProcessActivity` returns a shared no-op.
  - It is registered in `AppHost.Create` next to the other hosting services, because Transcription, VoiceActivity and Dictation all use it.
- **What is wrapped:**
  - `TranscribeCppTranscriber.ProcessWorkItems` wraps each work item, loads and runs alike. That covers the load at start, reloads after a settings change, the return to the GPU and every transcription, with no special cases.
  - `VoiceActivityWarmupService.WarmUp` is wrapped as a whole.
  - `DictationController` begins an activity at the press and ends it with the dictation (D5). That covers the voice activity trim, the paste and the clipboard restore with its delays, which run outside the worker.

  Activities nest, so the controller's span and a work item may overlap.
- **`.userInitiated`** also keeps idle system sleep off for those seconds. That is harmless, and it makes the activity visible in `pmset -g assertions`, which the check by hand uses.
- **Why wrap at all,** although the overlay is visible during a dictation and a visible window usually keeps App Nap away: the loads and reloads run without a visible window (at start, after a settings change, the return to the GPU). The span also covers the first moments after a press, before the overlay is on screen. The activity can't help the event that arrives before it begins. Only disabling App Nap for the whole process would, and that was rejected in the shell design.
- **The thread rule:** `ProcessInfo`'s activity methods are thread-safe. The helper's activity functions are therefore the third exception to "call the helper on the UI thread", next to the pasteboard's. Sending them through `IUiDispatcher` would add latency before every load and every dictation.

*Rejected:*
- **`NSAppSleepDisabled` in Info.plist:** it keeps the idle app awake all day (shell design).
- **Wrapping only the dictation span and "the model load":** a transcription or reload outside a span, such as the return to the GPU, would stay throttled (user decision: per work item).

### D7: The relaunch waits for a dictation

- `RelaunchService` takes `IDictationState`. The wait sits right before the relaunch, after the download wait and after the 3 s notice:
  - if a dictation is active, the service subscribes to `ActiveChanged`, logs "The restart waits until the dictation has ended" once, and returns
  - when the dictation ends, `ActiveChanged` (raised on the controller's thread) queues the check on the UI thread, and the relaunch follows at once
- **No new text in the setup window** (user decision). When it is open, it keeps saying that Pisum Transcribe restarts, a little longer than 3 s.
- **The wait is bounded** by the maximum recording duration, the transcription and the insertion.
- **When it can happen:** only after a revoke during a dictation, followed by a re-grant before the dictation ends. With the grant not in effect since start, the hook never ran, so no dictation exists.

### D8: The secure-input notification and the macOS wording

- `NotifyOutcome` gets `case InsertionOutcome.SecureInputOn`, with `DictationMessages.SecureInputReason` = "secure input is on, for example in a password field", in the existing "copied to the clipboard" message.
- The blocked-microphone text already names the macOS path (`MicrophoneAccessDeniedException`, #17). Only the spec changes.
- Two texts named Windows controls on the Mac, found during implementation (user decision to fix them here): the fallback notification's "Paste it with Ctrl+V" becomes `DictationMessages.PasteShortcut`, Command+V on macOS, and `NoModelMessage` names **Set up Pisum Transcribe…** in the menu bar on macOS. A run without an app bundle shows **Download model…** instead, which only a development run sees.
- The orange indicator, **Quit Pisum Transcribe** and the logout need no code: `ShutdownCoordinator` already ends the dictation for every `ShutdownReason`, and the macOS recorder releases the AudioQueue on abort. They are checked by hand.

### D9: ABI 4

`Overlay.swift` adds `pisum_overlay_configure(void* nswindow) -> Int32`, and `Activity.swift` adds `pisum_activity_begin(const char* reason) -> void*` and `pisum_activity_end(void* token)`. `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` go from 3 to 4 together. An app with an older helper skips every helper call and logs the mismatch, as it does today.

### D10: The keystroke permission at the final gate

- `IKeyboardInput.CanPostEvents` is always `true` in `SharpHookKeyboardInput` on Windows. `MacKeyboardInput` calls `CGPreflightPostEventAccess()` (CoreGraphics, `DllImport`), a cheap C call that is safe on any thread. It is the check libuiohook uses, and spike M3 showed that it stays false after a grant made while the process runs.
- `TextInserter` checks it at the final gate, right after secure input, with no await in between. When it's false, the insertion falls back with `InsertionOutcome.KeystrokesNotAllowed`, like `SecureInputOn`: the transcript is left on the clipboard as the user's content, and nothing is restored.
- `NotifyOutcome` gets `case InsertionOutcome.KeystrokesNotAllowed` with `DictationMessages.KeystrokesNotAllowedReason` = "Accessibility access isn't in effect".
- What it covers:

  | Situation | Preflight | Outcome |
  |---|---|---|
  | grant in effect since start | true | inserted |
  | revoked, not granted again | false | fallback (today the AX failure already falls back as "target window changed") |
  | revoked, then granted again | false until the restart | fallback, instead of a dropped Command+V followed by a restore that erases the transcript |

- A false negative only costs a clipboard fallback with a notification. The transcript is never lost. The relaunch (D7) follows once the dictation has ended.
- The gate comes after secure input because secure input is the more common and more specific reason, and each notification should name the actual cause. Both reasons send no keystrokes, and both leave the transcript on the clipboard.

*Rejected:*
- **`IPermissions.IsAccessibilityInEffect` at the gate:** it would work, but `TextInsertion` would depend on the macOS-only Permissions feature. The preflight asks macOS the exact question: may this process post events now.
- **Keeping it as a risk checked by hand:** the silent loss of a transcript is the one failure the insertion must never have (user decision).

## Risks / Trade-offs

- **[`CGPreflightPostEventAccess` reports true while posting still fails]** → The spike only showed the opposite case (false after a grant in the same process). Posting can also fail for reasons the preflight doesn't know, and those stay undetectable, as they are today. The check by hand revokes and re-grants during a transcription and confirms the fallback.
- **[Avalonia changes its macOS coordinates]** → The spike measured 12.1.1. A test can't show the overlay on Avalonia.Native: AppKit creates windows only on the process's main thread, which the test platform owns (found during implementation). The check by hand of the placement (task 4.2) therefore runs after every Avalonia update, and `MacOverlayPlatform.ChooseWorkArea` is unit-tested with the spike's numbers.
- **[The configure-before-Show is too late for the first full-screen Space]** → The overlay window is created at startup (`DictationFeedback.StartAsync`), so its `NSWindow` exists and is configured long before the first press. The check by hand dictates into full-screen TextEdit as the very first dictation after a start.
- **[A second display with a negative origin]** → `ScreenFromPoint` works in the same global points as the AX frame, and negative coordinates are valid there. It is unchecked without hardware. The primary-screen fallback bounds the damage to "wrong screen".
- **[A cancelled CPU run blocks the next dictation for up to 52 s on a 399 s clip]** → Only after a CPU fallback, the same trade-off as on Windows. Metal ran 399 s without failing, so the fallback is unlikely on the target Mac.
- **[The secure-input poll wakes the app every 2 s]** → It is a cheap C call, it pauses during dictations, and it doesn't hold an activity, so App Nap still coalesces the timer when idle. Coalescing can delay the tray's update. The spec allows 3 s, and the check by hand measures it.
- **[Nobody listens to `IDictationState` on Windows]** → The seam costs a field write per dictation.

## Migration Plan

No data or settings change. The ABI bump makes a mismatched helper visible in the log instead of misbehaving. Rolling back means reverting the change, and the Mac then records nothing on the hotkey again, as before.
