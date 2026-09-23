## Context

See proposal.md for the motivation. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design (2026-09-22) and from explore mode on 2026-09-23. This design turns them into an approach. D-numbers of that design are written "shell D8".

**Current state:**
- `ModelSetupHostedService` opens `ModelSetupWindow` at startup when the selected model isn't installed, and adds **Download model…** to the menu. `ModelSetupViewModel` raises `CloseRequested` after a successful download or on **Close**.
- Downloads also start from the settings window's model section (`ModelItemViewModel`, through `ModelDownloadViewModel`). So a download can run while the setup window is closed.
- `ModelStore` deletes `*.partial` files at startup and can't resume a download. Its free space comes from the `getAvailableFreeSpace` delegate, which defaults to `DriveInfo` (`statfs`).
- `MacNotifier.StartAsync` calls `pisum_notifications_start`, which sets the notification delegate *and* asks for permission, at every start (shell D8).
- `Info.plist` has no `NSMicrophoneUsageDescription`. Without it, macOS ends a process that asks for the microphone. The bundle is signed without the hardened runtime, so no audio-input entitlement is needed.
- `ShutdownCoordinator` is the only code that ends the app. It stops the host within 4 s, and a watchdog ends the process after 4.5 s. A second instance waits up to 6 s for the first one's mutex (`SingleInstanceGuard.WaitTimeout`).

**Facts from the spikes that shape this design:**
- **Spike M3.** `AXIsProcessTrusted()` turns true at once after the grant, but SharpHook's `CGPreflightPostEventAccess()` sees the grant only in a new process. So `AXIsProcessTrusted()` is the right signal to *detect* the grant, and a relaunch is needed to *use* it.
- **Spike M5.** An alert about reading the pasteboard blocks the thread that reads until the user answers. On macOS 27 without the developer preview, `accessBehavior` is 2 (always allow).

## Goals / Non-Goals

**Goals:**
- The later macOS changes can assume that the two required grants are in place, or that the menu bar menu offers to set them up.
- Windows behaves exactly as today: its setup window, its tests and its close rule stay the same.

**Non-Goals:**
- Starting the keyboard hook after the relaunch (`add-macos-recording`). This change relaunches the app; the new process does nothing new with the grant yet. `add-macos-recording` builds on this change (roadmap, 2026-09-23): it reads `IPermissions` and the helper's microphone status, relies on the relaunch instead of restarting the hook, and raises the helper ABI after this change's 2.
- Checks before capture (a denied microphone, a muted device), which `add-macos-recording` owns. This change only shows the microphone's state in setup.
- Delaying the relaunch while a dictation runs, because there is no dictation on macOS yet. `add-macos-dictation` adds that condition to the relaunch rule (D4).

## Decisions

### D1: A `Permissions` feature with a macOS implementation, and the window's permission part as an optional view model

- A new feature folder `Permissions/` (namespace `Pisum.Transcribe.Permissions`):
  - platform-neutral: `IPermissions` (the state of each permission, `Changed`, the request calls), `Permission` and `PermissionState` enums, and `PermissionsViewModel` with the four rows
  - `Permissions/MacOS/`: `MacPermissions` (the checks and requests, D2 and D3) and `RelaunchService` (D4, with the background check of D5)
- `AddPermissions()` registers the macOS types. `AppHost.Create` calls it only on macOS (`#if !WINDOWS`, next to the other per-platform feature choices of shell D14).
- `ModelSetupViewModel` takes `PermissionsViewModel?`, which is `null` on Windows and outside an app bundle. The window shows the permission part only when it's set. This is the pattern `SettingsViewModel` already uses for the optional `IStartupRegistration` (shell D14).
- On macOS the heading changes to "Set up Pisum Transcribe". When the selected model is already installed, the model part collapses to one line ("Speech model: Canary 1B Flash, installed"), so a window opened only for a permission doesn't offer a download.

*Rejected:*
- **A second window for the permissions:** the issue and the decision ask for one window, and two windows would split the close rule.
- **Permission rows inside `SpeechModels/`:** the menu item, the relaunch and the grant checks aren't about models, and `add-macos-recording` will read `IPermissions` too.

### D2: How each state is read

| Permission | State | Request | Where |
|---|---|---|---|
| Accessibility | `AXIsProcessTrusted()` | `AXIsProcessTrustedWithOptions({kAXTrustedCheckOptionPrompt: true})` | `DllImport` (ApplicationServices, CoreFoundation) |
| Microphone | `AVCaptureDevice.authorizationStatus(for: .audio)` | `AVCaptureDevice.requestAccess(for: .audio)`, or System Settings when denied | Swift helper |
| Notifications | `UNUserNotificationCenter.getNotificationSettings`, asynchronous | `requestAuthorization([.alert, .sound])` | Swift helper |
| Paste from other apps | `NSPasteboard.general.accessBehavior` (macOS 15.4+) | one read of the pasteboard (D6) | Swift helper |

- **C APIs through `DllImport`**, Objective-C APIs through the helper, as `CLAUDE.md` requires. The prompt option is a `CFDictionary` built with `CFDictionaryCreate`. The key `kAXTrustedCheckOptionPrompt` and `kCFBooleanTrue` are exported data symbols, read with `NativeLibrary.GetExport`.
- **Microphone:** `restricted` (for example a device management profile) shows as denied (spec "Microphone state from the system").
- **System Settings links:** they open through `Process.Start` with `UseShellExecute`, as the license links do:
  - `x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone`
  - `…?Privacy_Accessibility`
  - `…?Privacy_Pasteboard`, the Paste from other apps page. On 2026-09-23 on macOS 27.0 (26A428), `SecurityPrivacyExtension.appex` declared this anchor next to `Privacy_Microphone` and `Privacy_Accessibility`, and `open` with the link showed the page.
- **`NSMicrophoneUsageDescription`** in `MacOS/Info.plist`: "Pisum Transcribe records your voice while you hold the push-to-talk key and transcribes it on this Mac."

### D3: Helper ABI 2

New and changed functions in `src/Pisum.Transcribe.MacNative/`. One `.swift` file per area: `Microphone.swift`, `Pasteboard.swift`, `Bundle.swift`, and changes to `Notifications.swift`.

- `pisum_has_bundle() -> Int32`: 1 when `Bundle.main.bundleIdentifier` is set. It replaces the guess from the notification status and decides D8.
- `pisum_microphone_status() -> Int32`: 0 not determined, 1 restricted, 2 denied, 3 authorized.
- `pisum_microphone_request(callback, context)`: the callback gets the new status on a background thread.
- `pisum_notifications_start() -> Int32`: **changed meaning.** It now only sets the delegate and no longer asks for permission, so it takes no callback.
- `pisum_notifications_request(callback, context)`: the old request.
- `pisum_notifications_status(callback, context)`: 0 not determined, 2 denied, 3 authorized (and provisional), 1 without a bundle.
- `pisum_pasteboard_access_behavior() -> Int32`: −1 before macOS 15.4, otherwise the raw value (0 default, 1 ask, 2 always allow, 3 always deny).
- `pisum_pasteboard_probe() -> Int32`: reads the general pasteboard's string once and discards it. **It's the only helper function that must NOT run on the UI thread**, because an alert blocks the reading thread (spike M5). Its doc comment and `PisumMac` say so.
- `pisum_abi_version` returns 2, and `MacNativeLibrary.ExpectedAbiVersion` becomes 2, because the meaning of `pisum_notifications_start` changes.

### D4: The relaunch

```
 AXIsProcessTrusted: false --(checked)--> true         (only a transition in this process counts)
        |
        v
 IModelStore.IsDownloading ? ---- yes ---> row: "Restarts when the download is finished"
        | no                                    |  DownloadStateChanged -> not downloading
        v  <------------------------------------+
 window open ? "Pisum Transcribe restarts to turn on the hotkey" (3 s)
        |
        v
 ShutdownCoordinator.RequestShutdownAsync(ShutdownReason.Relaunch)
        |-- first step:  /usr/bin/open -n "<bundle path>"   (the new process waits for the mutex, up to 6 s)
        '-- then as Quit: stop the host (<= 4 s), watchdog at 4.5 s, _exit(0)
```

- **Only a transition counts.** `RelaunchService` stores the state it saw first. A process that starts with the grant never relaunches, so there's no relaunch loop. A grant that's revoked and granted again while the app runs relaunches again, which is right, because the old process's event tap would stay dead.
- **Waiting for a download:** `IModelStore` gains `bool IsDownloading` and `event EventHandler? DownloadStateChanged`. They cover downloads from both the setup window and the settings window, because both run through the store. The relaunch runs when the last download ends, however it ended.
- **The new instance is started first**, at the beginning of `ShutdownAsync` for `Relaunch`, not after the host stopped. The watchdog can end the process at 4.5 s, and anything scheduled after that point would be lost. The new process waits for the mutex, and 6 s covers the 4.5 s limit.
  - `open -n` is needed, because without it `open` activates the running instance instead of starting a second one.
  - The bundle path is `AppContext.BaseDirectory` up to and including the enclosing `.app`. When no `.app` encloses it, there is no relaunch (D8).
- **`ShutdownReason.Relaunch`,** exit code 0. It is handled as `UserExit` otherwise, and the log names it.
- **The 3 s notice** is a timer through `TimeProvider`, so tests use `FakeTimeProvider`. When the window is closed, the relaunch runs without a notice. The user granted Accessibility in System Settings on their own, and the menu bar icon comes back.
- **Under `dotnet run`,** `open -W` returns when the first process ends. The relaunched app keeps running on its own. `CLAUDE.md` says so.

*Rejected:*
- **Relaunch at once:** it deletes a running download (no resume, `*.partial` removed at startup).
- **Relaunch only on a button:** a user who closes the window never gets a working hotkey.
- **Starting the new process after the host stopped:** the watchdog can cut it off.
- **`NSWorkspace.openApplication` through the helper:** it needs an async completion and a new ABI function, and `open -n` does the same.

### D5: Polling and the menu item

- **While the setup window is open:** `PermissionsViewModel` refreshes every 1 s. A refresh is two C calls, one helper call and one asynchronous notification-settings call. That meets "within 2 seconds".
- **While the window is closed,** only Accessibility is checked, every 2 s, and only while it isn't granted in this process. That's all the relaunch needs (spec: within 10 s). A granted process stops checking, so an idle app with its permissions in place does nothing.
- **One menu item** (user decision, 2026-09-23). `ModelSetupHostedService` keeps adding the only setup item, and picks its label and its visibility by whether a `PermissionsViewModel` is set (D1):

  | Platform | Label | `isVisible` |
  |---|---|---|
  | Windows | **Download model…** | the model isn't installed |
  | macOS, app bundle | **Set up Pisum Transcribe…** | the model isn't installed, or Accessibility or the microphone isn't granted |
  | macOS, no bundle (D8) | **Download model…** | the model isn't installed |

  `isVisible` runs when the menu opens, so a revoked grant needs no polling.
- The permissions feature adds no menu item of its own. It reaches the window for the relaunch notice (D4) through a small `ISetupWindow` interface that `ModelSetupHostedService` implements, so there is one window instance.

*Rejected:* a separate **Set up permissions…** next to **Download model…**. With no model and no grants, both would show and open the same window.

### D6: The Paste from other apps row

| `accessBehavior` | Row | Action |
|---|---|---|
| −1 (before 15.4) | granted | none |
| 2 always allow | granted | none |
| 0 default | not yet asked | one `pisum_pasteboard_probe()` on a thread-pool thread, when the window opens |
| 1 ask | "asks each time" | button **Open Settings…** |
| 3 always deny | denied | button **Open Settings…** |

- The probe runs once per process and only while the window is open, so the macOS alert appears in front of the setup window and not during an insertion. Afterwards the state is 1 or 2, and the row follows it.
- The row is optional. The window closes by the required rows alone (close rule C, explore mode 2026-09-23), and `add-macos-text-insertion` types the text while the state isn't 2.

### D7: When the window closes, and the notification request

- `ModelSetupViewModel` gets one `IsComplete` rule: the selected model installed, and on macOS both required permissions granted. It raises `CloseRequested` when the rule becomes true, after a download or after a permission change, in either order. On Windows the rule is the model alone, as today.
- `ModelSetupHostedService` opens the window at startup when the rule is false.
- **Close rule C:** when the window opens and `pisum_notifications_status` reports "not determined", the permissions part calls `pisum_notifications_request`. The prompt then appears together with the window, well before the required rows are done, and closing doesn't wait for it.
- `MacNotifier.StartAsync` calls only the new `pisum_notifications_start` (the delegate). It no longer asks, and it learns whether notifications are denied from the status call, which it logs.

### D8: Outside an app bundle

With `pisum_has_bundle() == 0` (the bare executable, and the macOS `Integration` tests' host), permissions belong to the terminal. `AddPermissions()` then registers nothing but a null `PermissionsViewModel`:
- no rows
- **Download model…** as on Windows, instead of **Set up Pisum Transcribe…**
- no relaunch
- the window follows the Windows rule

It logs once that permissions are skipped outside an app bundle.

### D9: Disk space and Time Machine through CoreFoundation

- **Free space:** `CFURLCopyResourcePropertyForKey(url, kCFURLVolumeAvailableCapacityForImportantUsageKey)` returns a `CFNumber`, read with `CFNumberGetValue(kCFNumberSInt64Type)`. On failure it falls back to `DriveInfo` and logs a warning. This becomes the `getAvailableFreeSpace` delegate in the macOS registration of `ModelStore`, so `ModelStore`'s check stays shared.
  - On the development Mac on 2026-09-22: `DriveInfo` reported 133.00 GB and the important-usage capacity 147.03 GB.
- **Backup exclusion:** `CFURLSetResourcePropertyForKey(url, kCFURLIsExcludedFromBackupKey, kCFBooleanTrue)` on the models folder.
  - `ModelStore` gets a second optional delegate, `excludeFromBackup`, which is a no-op on Windows.
  - It is called after `Directory.CreateDirectory` at startup (next to the `*.partial` cleanup) and before each download. Both calls are idempotent and cheap.
  - The attribute stays with the folder, and Time Machine skips the folder's contents.
  - A failure logs a warning and doesn't stop the download.
  - *Rejected:* `tmutil addexclusion`, which starts a process for an attribute that CoreFoundation sets directly.
- Both use the same small `MacOS/CoreFoundation` interop class, as D2's Accessibility prompt does.

### D10: Tests

- **Unit, both platforms:** `ModelSetupViewModel`'s close rule with a fake `IPermissions` in every order, and the Windows rule unchanged. `ModelStore` calls `excludeFromBackup` at startup and before a download.
- **Unit, macOS folder:**
  - `RelaunchService`: transition only, waits for `IsDownloading`, 3 s through `FakeTimeProvider`, no relaunch when it starts granted. The coordinator call is faked.
  - `PermissionsViewModel`: row states from fake states, the notification request only when not determined, and the probe only at state 0.
- **`ShutdownCoordinator`:** `Relaunch` starts the new process first (a fake launcher), then stops the host, with exit code 0.
- **macOS `Integration`:** each new helper function returns a valid value from the test host. The test host has no bundle, so `pisum_has_bundle` returns 0. The CoreFoundation free space is at least `DriveInfo`'s. The exclusion on a `TempDirectory` shows in `tmutil isexcluded`.
- **macOS `Integration`, the relaunch:** the bundle path from the dev bundle's build output, and the launcher's `open -n` starting a new process of the real dev bundle. Skipped when the bundle hasn't been built.
- **The whole relaunch chain** is checked by hand in the first-run check (task 6.1), because a test can't grant Accessibility.

*Rejected:* a test-only switch in the app (an environment variable or a signal) to trigger the relaunch in a `Hardware` test. It would stay in the shipped app for good, and it would add little. The rules are covered by unit tests, the waiting for the old instance by `SingleInstanceGuard`'s tests, and the system parts by the test above.
- **Manual:** the whole first run on a Mac after `tccutil reset All io.github.mschnecke.pisum-transcribe`.

## Risks / Trade-offs

- **[Ad hoc signed dev builds lose their grants at every build]** → shell D9's self-signed identity. `CLAUDE.md` already describes it, and the setup window now makes a lost grant visible instead of silent.
- **[The Paste from other apps link isn't documented by Apple, and a later macOS could rename the anchor]** → it was checked on macOS 27.0 (D2). Should it stop working, the page falls back to Privacy & Security.
- **[The relaunch ends the settings window while it's open, like Quit]** → accepted. A grant while the settings window has unsaved edits is rare, and Quit behaves the same way today.
- **[The notification prompt and the window appear together]** → that's the choice in close rule C. The prompt comes from macOS and doesn't block the window.
- **[`AXIsProcessTrustedWithOptions`'s prompt doesn't appear again after the first time]** → macOS shows it once per app. The row's **Allow…** then also opens `Privacy_Accessibility` directly, so the button always leads somewhere.
- **[A relaunch while `open -n` fails, for example with the bundle moved while running]** → the coordinator logs the failure and still quits, as the user has granted and expects a restart. The next manual start works.

## Migration Plan

There is no macOS release yet (`add-macos-packaging` comes later). The ABI moves from 1 to 2 in the same build as the C# side, so no mixed versions exist. Windows is unchanged. Rollback is reverting the change.
