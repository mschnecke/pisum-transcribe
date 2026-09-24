The order follows the dependencies. First the platform-neutral seams and the per-platform tables, then the macOS hook and the grant, then the capture. Windows must stay green after every group: `dotnet test Pisum.Transcribe.slnx` on Windows runs unchanged.

## 1. Seams, defaults and names

- [x] 1.1 Add `IHotkeyKeyState` (D1) and `Recording/Windows/WindowsHotkeyKeyState` on `Windows.Win32.PInvoke.GetAsyncKeyState`. `SharpHookPushToTalkHotkey` takes it in place of the `isKeyDown` delegate and loses its `DllImport`. Verify: `SharpHookPushToTalkHotkeyTests` pass with a fake `IHotkeyKeyState`, and `dotnet build` passes for both frameworks.
- [x] 1.2 Add `IHookAccess` (D1) with a Windows implementation that always allows the start. `StartAsync` runs the hook only when it's allowed, and logs at Information otherwise. `RunHookAsync` tells `ErrorAxApiRevoked` from other results: the revoke shows "Push-to-talk stopped" with the text of D3 and is reported to `IHookAccess`; everything else keeps "Push-to-talk unavailable". Verify: unit tests with a fake `IHookAccess` and a fake hook see no run and no notification when it isn't allowed, the revoke text and one report for `ErrorAxApiRevoked`, and the old text for `ErrorSetWindowsHookEx`.
- [x] 1.3 Make `HotkeyParser.DefaultKeyName` and `DefaultHotkey` per platform (D5): `VcRightMeta` on macOS, `VcRightControl` on Windows. Switch `JsonSettingsStoreTests` and `HotkeyParserTests` from `"VcRightControl"` to `DefaultKeyName` (spec `push-to-talk-hotkey` "Hotkey setting"). Verify: the tests pass on the Mac and on Windows.
- [x] 1.4 Add `HotkeyKeyNames` with `Windows`, `MacOS` and `Current` (D6), and make `HotkeyText` and `HotkeyRecorder` use it. Verify: `HotkeyTextTests` and `HotkeyRecorderTests` cover both tables on either host, including "Right Command", the order Control, Option, Shift, Command, fn rejected on macOS (after 5.1), and both rejection messages (spec `settings-window` "Hotkey editor").
- [x] 1.5 ~~Add `DictationSectionViewModel.ShowsFnHint` and its `hint` line in `SettingsDialog.axaml` (D6).~~ Added, then removed after the checks by hand of 5.1: the hook never sees fn as held, so fn isn't a hotkey key and needs no hint (D6).

## 2. The grant in effect

- [x] 2.1 Replace `IPermissions.IsAccessibilityGrantedAtStart` with `IsAccessibilityInEffect`, `OnAccessibilityRevoked()` and `AccessibilityInEffectChanged` (D4), in `MacPermissions` and every fake. `PermissionsViewModel.AreRequiredGranted` reads the new property. Verify: `PermissionsViewModelTests` see `AreRequiredGranted` false after a revoke, also when the Accessibility state reads granted again.
- [x] 2.2 Make `RelaunchService` start its check timer also when `AccessibilityInEffectChanged` turns the grant false (D4, spec `macos-permissions` "Relaunch after the Accessibility grant"). Verify: `RelaunchServiceTests` with a fake `IPermissions` that starts in effect, is revoked and then granted see one relaunch, and still wait for a running download.

## 3. The hook on macOS

- [x] 3.1 Add `Recording/MacOS/MacHotkeyKeyState` (D2): `CGEventSourceKeyState` on the HID system state, and `CGSessionCopyCurrentDictionary` for on console and not locked, through `DllImport`. Add the `MacOS/` entries to both `.csproj.DotSettings` files where they're missing. Verify: macOS `Integration` tests read an idle key as up, and the test host's session as on console and not locked. A unit test with a fake session reader sees a held key reported as not held when the screen is locked or the session is off console.
- [x] 3.2 Add `Recording/MacOS/MacHookAccess` (D3): allowed when `IPermissions.IsAccessibilityInEffect`, and a revoke calls `OnAccessibilityRevoked()` through `IUiDispatcher`. Verify: unit tests with a fake `IPermissions` and `InlineUiDispatcher`.
- [x] 3.3 Register the hook on macOS in `AddRecording()` (D1, D3, D9): `UioHookProvider.Instance.PromptUserIfAxApiDisabled = false` before the hook is created, `KeyTypedEnabled` left off, the macOS seams, and `SharpHookPushToTalkHotkey` as `IPushToTalkHotkey` and a hosted service. Remove `InactivePushToTalkHotkey` and its tests. Verify: the macOS host-building test resolves every hosted service, and `dotnet build` passes for both frameworks.

## 4. Capture on macOS

- [x] 4.1 Add the AudioToolbox and CoreAudio interop in `Recording/MacOS/` (D7): `AudioQueueNewInput`, the buffer calls, `AudioQueueStart`, `AudioQueueStop`, `AudioQueueDispose`, `AudioQueueSetProperty`, `AudioObjectGetPropertyData`, `AudioObjectHasProperty` and the property listener calls, with `AudioStreamBasicDescription` and `AudioObjectPropertyAddress`. Verify: a macOS `Integration` test reads the default input device and whether it has a mute property, without throwing.
- [x] 4.2 Add `AudioQueueCaptureSessionFactory` with the checks before opening, in the order of D7: the permission through `IUiDispatcher`, the device, mute (spec `audio-recording` "Microphone access blocked", "Microphone muted", "No microphone available"). Verify: unit tests with a fake status and device reader see `MicrophoneAccessDeniedException` for 0, 1 and 2, `NoMicrophoneException` for an unknown device and `MicrophoneMutedException` for a muted one, each without a queue created.
- [x] 4.3 Add `AudioQueueCaptureSession` (D7): the 16 kHz mono Float32 queue pinned to the device, the `[UnmanagedCallersOnly]` input callback with a `GCHandle`, three 50 ms buffers, `silent` always false, and `StopAsync` and `DisposeAsync` that stop, dispose and free the handle in that order. Register the factory and `AudioRecorder` on macOS. Verify: macOS `Hardware` tests `Recording/MacOS/AudioRecorderHardwareTests`, mirroring the Windows ones: about 32,000 samples in 2 s, the time to first audio, 50 cycles with a stable count of open file descriptors, and abort.
- [x] 4.4 Add the default-device listener to the session (D8): a new device swaps the queue off CoreAudio's thread under the session's lock, and `kAudioObjectUnknown` raises `Stopped(MicrophoneDisconnectedException)`. The listener is removed before the handle is freed (spec `audio-recording` "Default recording device", "Microphone lost during recording"). Verify: unit tests of the swap and loss logic behind a fake device source, and a macOS `Hardware` test that switches the input device during a recording when a second device is present, skipped otherwise.
- [x] 4.5 Add a macOS `Hardware` test for a muted input device (spec "Microphone muted"). Verify: it fails with `MicrophoneMutedException` when the device has a mute property and it's set, and is skipped otherwise.

## 5. End to end and docs

- [x] 5.1 Run the checks by hand of D10 on the dev Mac with the dev bundle and its Debug log. They include the revoke, the grant again and the relaunch, fn rejected by the editor, the lock, a user switch, secure input, the tap timeout, Quit while holding, and AirPods. Verify: each step as described, noted in the PR, with the answer to the open question.
- [x] 5.2 Update the docs:
  - `CLAUDE.md`: the recording on macOS in the layout and in the macOS registration of `AppHost.Create`, the default hotkey per platform, and the AudioQueue interop under macOS APIs
  - `docs/roadmap.md`: the change done

  Verify: the texts match the code.
- [x] 5.3 Run `openspec validate add-macos-recording --strict`, and `dotnet build` plus `dotnet test Pisum.Transcribe.slnx` on the Mac and in CI on Windows. Verify: all pass.
