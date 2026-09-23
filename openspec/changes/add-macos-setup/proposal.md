## Why

Dictation on macOS needs permissions that Windows doesn't ask for: Accessibility for the push-to-talk hotkey and for inserting text, and the microphone. If macOS asks for them only when the hotkey is first pressed, a microphone prompt takes the focus from the target app mid-dictation. The hotkey also stays dead until the app is restarted, and a denied microphone records silence without an error. This change asks for every permission once, up front, in the setup window that already downloads the speech model. The later macOS changes (`add-macos-recording`, `add-macos-text-insertion`, `add-macos-dictation`) can then rely on the grants being in place.

Tracked in issue #16. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design, and from explore mode on 2026-09-23.

## What Changes

- **One setup window on macOS for the model and the permissions.** The model part stays as it is. Below it are four permission rows, each showing its state, which updates on its own:
  - **Accessibility** (required), for the hotkey and text insertion
  - **Microphone** (required)
  - **Notifications** (optional)
  - **Paste from other apps** (optional), for macOS's pasteboard privacy
- **When the window opens and closes:**
  - At startup, the window opens when the selected model isn't installed or a required permission is missing.
  - It closes on its own once the model is installed and both required permissions are granted, which can happen in any order.
  - The model download keeps running while the user grants permissions.
- **How each permission is asked:**
  - Accessibility and Microphone each have an **Allow…** button in their row. A denied permission's button opens the matching page of System Settings.
  - The notification permission is asked when the window opens, if the user hasn't answered it yet. It is no longer asked at every start.
  - The Paste row settles itself where the system allows it. Where pasteboard privacy asks on every read, the row leads the user to System Settings.
- **Relaunch after the Accessibility grant.** macOS makes the grant visible to the keyboard hook only in a new process. After the grant, the window says that the app restarts, and the app then relaunches itself. If a model download is running, the relaunch waits until the download ends. After the relaunch, the window opens again if anything required is still missing.
- **One Set up Pisum Transcribe… item in the menu bar menu** takes the place of **Download model…** on macOS. It is shown while the model is missing or a required permission is missing, including a permission that was revoked while the app runs.
- **The microphone's state is read from macOS**, not guessed from the audio, because a denied microphone delivers silence and no error.
- **Disk space check on macOS:** the free space counts what macOS can free for a download the user starts, including purgeable space, the way Finder counts it.
- **The models folder is excluded from Time Machine**, because a model is 1–2 GB and can always be downloaded again. Settings and logs stay in backups.
- **Windows doesn't change.** Its setup window stays a model-only window.
- Not included:
  - the keyboard hook itself, microphone capture, the checks before capture and the microphone-muted message (`add-macos-recording`)
  - reading the pasteboard for text insertion (`add-macos-text-insertion`)
  - starting at login (`add-macos-login-item`)

## Capabilities

### New Capabilities
- `macos-permissions`: the permissions Pisum Transcribe needs on macOS, and how it asks for them and follows them:
  - the permission rows of the setup window, how each is asked and how its state updates
  - the relaunch after the Accessibility grant
  - the **Set up Pisum Transcribe…** menu item
  - the Paste from other apps row
  - the behavior outside an app bundle

### Modified Capabilities
- `model-management`:
  - "First-run setup" opens and closes the window on macOS by the model and the required permissions too.
  - "Disk space check" counts, on macOS, the space available for a download the user starts.
  - "Reopening setup from the tray" names **Set up Pisum Transcribe…** as the item on macOS.
  - A new requirement, "Models folder excluded from backups", covers Time Machine.
- `app-shell`: "Notifications come from Pisum Transcribe" asks for the permission in the setup window on macOS, instead of at the first start.

## Impact

- **Depends on:** `add-macos-shell` (#15), which is merged.
- **Code:**
  - `SpeechModels/`: the setup window gains a macOS-only permissions part, and `ModelSetupHostedService` opens and closes it by the new rule. The free space delegate of `ModelStore` gets a macOS implementation, and the models folder is marked as excluded from backups.
  - A macOS-only permissions service in a `MacOS/` folder: the grant checks, the background check of Accessibility, the relaunch and the tray item.
  - `Hosting/`: a new `ShutdownReason.Relaunch`. `ShutdownCoordinator` starts the new process as its last step.
  - `Notifications/MacOS/MacNotifier`: no longer asks for permission at every start.
  - `MacOS/Info.plist`: `NSMicrophoneUsageDescription`, without which macOS ends the app when it asks for the microphone.
- **Swift helper:** new functions for the microphone's authorization, the notification settings and the pasteboard's `accessBehavior`, and a raised `pisum_abi_version`. The Accessibility, CoreFoundation and Time Machine calls are C APIs, called through `DllImport`.
- **Tests:** unit tests for the window's open and close rules and for the relaunch rule, which use fakes for the grants. macOS `Integration` tests call the new helper functions. A `Hardware` test covers the relaunch.
- **Docs:** `CLAUDE.md` (layout, the permissions on a dev Mac) and `docs/roadmap.md`.
