## Why

On macOS the push-to-talk hotkey sees no keys yet (`InactivePushToTalkHotkey`), and there is no microphone capture. Dictation on the Mac (`add-macos-dictation`) needs both, and it needs them to fail clearly: a denied or muted microphone on macOS delivers digital zeros and no error, and a keyboard hook without the Accessibility grant stays dead until the process restarts. This change brings the keyboard hook and the microphone capture to macOS, with right Command as the default hotkey. It builds on the permissions and the relaunch of `add-macos-setup`.

Tracked in issue #17. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design, and from explore mode on 2026-09-24.

## What Changes

- **The keyboard hook on macOS.** SharpHook's event tap runs as on Windows, with the same push-to-talk rules.
  - **Right Command** is the default hotkey on macOS. Right Ctrl stays the default on Windows. A Mac `settings.json` from a dev build that already stores right Ctrl keeps it; there is no migration, because no Mac build was released.
  - The hook starts only in a process that had the Accessibility grant at start. Without it, the hook isn't started, and nothing is shown: the setup window and its menu item already say that the grant is missing. The hook never shows macOS's own Accessibility prompt.
  - When the grant is revoked while the app runs, a notification says so and points to the setup window. The grant then counts as not in effect, so granting it again relaunches the app, as after the first grant.
  - Any other failure of the hook shows today's "Push-to-talk unavailable" notification.
- **Key state on macOS:**
  - A missed release is recovered by reading the physical key state, which also works while secure input hides key events.
  - While a hotkey key is held, a locked screen or a switch to another user counts as a release that can't be seen, so a held hotkey is cancelled within 1 second, as on Windows.
  - Key events that software posts are ignored. A virtual keyboard such as Karabiner-Elements' counts as a keyboard.
- **Microphone capture on macOS** at 16 kHz mono from the default input device:
  - Before the microphone opens, a denied microphone fails at once with "microphone access blocked", and a muted input device with "microphone muted", instead of waiting 3 s and reporting "not responding".
  - A change of the default input device during a recording continues on the new device. When no input device remains, the recording ends with "microphone disconnected".
- **The hotkey editor on macOS** uses Mac key names (`fn`, `Right Command`, `Left Option`, …) and accepts fn/Globe. While the hotkey includes fn, a hint says to set macOS's "Press 🌐 key to" to "Do Nothing".
- **Not included:**
  - connecting the hotkey to the recording, the tray states, the overlay and the error notifications on macOS (`add-macos-dictation`, #20). The `dictation` requirements "Error notifications" and "Dictation ends when the application exits" get their macOS wording there.
  - text insertion and the secure-input hint in the tray (`add-macos-text-insertion`, #19)
- **Windows doesn't change,** apart from internal seams: the key-state read and the hotkey editor's key names move behind per-platform implementations.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `push-to-talk-hotkey`:
  - "Hotkey setting": the default is right Command on macOS.
  - "Global detection": on macOS, the hook runs only with the Accessibility grant in effect, and a revoke is reported.
  - "Simulated key events ignored": events posted by software on macOS; a virtual keyboard device counts as a keyboard.
  - "Reset on session switch": the locked screen and fast user switching on macOS, within 1 second.
  - "Missed release recovery": the physical key state on macOS, also under secure input.
- `audio-recording`:
  - "Default recording device": the default input device on macOS, including a change during a recording.
  - "Microphone opened only while recording": the orange microphone indicator in the menu bar.
  - "Start completes when audio flows": only digital zeros count as silence on macOS.
  - "Microphone access blocked": the macOS microphone permission, checked before the microphone opens.
  - "Microphone muted": the muted input device on macOS, checked before the microphone opens.
- `settings-window`: "Hotkey editor": Mac key names, fn/Globe, the hint about "Press 🌐 key to", and the Mac wording of the rejection message.
- `macos-permissions`: the Accessibility grant counts as in effect only while the keyboard hook can use it, so a grant after a revoke relaunches the app too.

## Impact

- **Depends on:** `add-macos-setup` (#16), which is merged. It uses `IPermissions`, the Swift helper's microphone status and `RelaunchService`.
- **Code:**
  - `Recording/`: `SharpHookPushToTalkHotkey` registered on macOS, with the key-state read, the grant check and the revoke behind a platform seam. `HotkeyParser`'s default per platform. `MacOS/AudioQueueCaptureSessionFactory` and its session on AudioToolbox and CoreAudio. `InactivePushToTalkHotkey` is removed.
  - `SettingsWindow/`: `HotkeyText` and `HotkeyRecorder` get per-platform names and rules, and the dialog shows the fn hint.
  - `Permissions/`: the Accessibility grant "in effect" in place of "granted at start", and `RelaunchService` follows it.
  - `Settings/RecordingSettings`: the default hotkey from `HotkeyParser`.
- **Swift helper:** no new function. `pisum_abi_version` stays 2. AudioQueue, CoreAudio, `CGEventSourceKeyState` and `CGSessionCopyCurrentDictionary` are C APIs, called through `DllImport`.
- **Tests:** unit tests for the hotkey's new paths and the per-platform names, macOS `Integration` tests for the key-state and session reads and the device reads, and macOS `Hardware` tests that mirror the Windows recorder tests. The hotkey on a real keyboard is checked by hand through the Debug log.
- **Docs:** `CLAUDE.md` (layout, the macOS registration) and `docs/roadmap.md`.
