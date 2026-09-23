The order follows the dependencies. First the helper ABI and the CoreFoundation interop, then the model store, then the permissions feature, then the window and the relaunch. Windows must stay green after every group: `dotnet test Pisum.Transcribe.slnx` on Windows runs unchanged.

## 1. Helper ABI 2 and the plist

- [x] 1.1 Add `NSMicrophoneUsageDescription` to `MacOS/Info.plist` with the text from D2. Verify: `plutil -lint` passes on the built bundle's plist, and `plutil -extract NSMicrophoneUsageDescription raw` prints the text.
- [x] 1.2 Split `Notifications.swift` (D3):
  - `pisum_notifications_start()` only sets the delegate
  - add `pisum_notifications_request(callback, context)` and `pisum_notifications_status(callback, context)`
  - add `pisum_has_bundle()` in `Bundle.swift`
  - `MacNotifier` calls only the new start and logs the status

  Verify: `MacNotifierTests` with a fake `INotificationCenter` see no request at start, and a macOS `Integration` test gets 1 from the status call and 0 from `pisum_has_bundle` in the test host.
- [x] 1.3 Add `Microphone.swift` with `pisum_microphone_status` and `pisum_microphone_request` (D3). Verify: a macOS `Integration` test gets a status from 0 to 3.
- [x] 1.4 Add `Pasteboard.swift` with `pisum_pasteboard_access_behavior` and `pisum_pasteboard_probe` (D3, D6). The probe's Swift doc and its `PisumMac` doc say it must not run on the UI thread. Verify: a macOS `Integration` test gets −1 or 0–3 from the access behavior. The probe isn't called in tests, because it may alert.
- [x] 1.5 Raise `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` to 2, and add the new functions to `PisumMac`. Verify: the ABI `Integration` test expects 2, and `dotnet build` passes on the Mac and on Windows.
- [x] 1.6 Add a `MacOS/` CoreFoundation interop class (D2, D9) with:
  - the important-usage free space of a folder
  - setting `kCFURLIsExcludedFromBackupKey`
  - `AXIsProcessTrusted` and `AXIsProcessTrustedWithOptions` with the prompt option, whose constants are read with `NativeLibrary.GetExport`

  Verify: macOS `Integration` tests:
  - the free space of a `TempDirectory` is at least `DriveInfo.AvailableFreeSpace`
  - after the exclusion, `tmutil isexcluded` reports the folder as excluded
  - `AXIsProcessTrusted` returns without throwing

## 2. Model store

- [x] 2.1 Give `ModelStore` an optional `excludeFromBackup` delegate, called after the models folder is created at startup and before each download (D9). A failure is logged as a warning. On macOS, register it with the CoreFoundation call, and the important-usage free space as `getAvailableFreeSpace`, falling back to `DriveInfo` on failure (spec `model-management` "Disk space check", "Models folder excluded from backups"). Verify: `ModelStoreTests` see the delegate called at start and before a download, and a download that continues when it throws. On macOS a download of a model larger than the `statfs` space but smaller than the important-usage space is allowed, checked through the delegate.
- [x] 2.2 Add `IsDownloading` and `DownloadStateChanged` to `IModelStore` (D4). The event is raised when the first download starts and when the last one ends, whether it succeeded, failed or was cancelled. Verify: `ModelStoreTests` cover each ending, and two parallel downloads of different models raise the end only once both are over.

## 3. The Permissions feature

- [x] 3.1 Add `Permissions/` (D1) with `IPermissions`, `Permission`, `PermissionState` and `PermissionsViewModel` (four rows, required flags, **Allow…** and **Open Settings…** commands), and `Permissions/MacOS/MacPermissions` over the helper and CoreFoundation (D2). Add the `MacOS/` folder entries to both `.csproj.DotSettings` files. Verify: `PermissionsViewModelTests` map every state of a fake `IPermissions` to the right row text and buttons, and a restricted microphone shows as denied.
- [x] 3.2 Implement the requests (spec `macos-permissions` "Asking for Accessibility and Microphone"): the microphone prompt when not determined, otherwise its settings link; the Accessibility prompt, plus its settings link after the first time. Verify: unit tests with a fake `IPermissions` and a fake URL opener see the right call per state.
- [x] 3.3 Implement the Paste row (D6): the probe once per process on a thread-pool thread when the window opens at state 0, and **Open Settings…** at 1 and 3. **Open Settings…** opens `x-apple.systempreferences:com.apple.preference.security?Privacy_Pasteboard` (D2). Verify: unit tests see the probe only at state 0, only once, and never on the UI thread (a fake dispatcher records the thread).
- [x] 3.4 Add the 1 s refresh while the window is open and the notification request at window open when not determined (D5, D7), through `TimeProvider`. Verify: with `FakeTimeProvider`, a changed fake state shows in the row after 1 s, and the request is made once only when not determined.
- [x] 3.5 Add `AddPermissions()`, registered in `AppHost.Create` on macOS only (D1). Outside an app bundle it registers no rows and no relaunch, so the menu keeps **Download model…**, and logs once (D8, spec "Permissions outside an app bundle"). Verify: the macOS host-building test resolves every hosted service, and a unit test with `has_bundle` faked to 0 gets a null `PermissionsViewModel` and the log entry.

## 4. The setup window

- [x] 4.1 Give `ModelSetupViewModel` the optional `PermissionsViewModel` and the `IsComplete` close rule (D7, spec `model-management` "First-run setup"). Verify: headless tests with fakes close the window in either order (model first, grants first), keep it open while only optional rows are open, and the Windows rule (no permissions) closes after the download as today.
- [x] 4.2 Extend `ModelSetupWindow.axaml`: the permission part (only when set), the heading "Set up Pisum Transcribe" on macOS, and the collapsed model line when the model is installed (D1). Compiled bindings with `x:DataType`. Verify: a headless test finds the four rows with the view model set and none without it, and the collapsed line when the model is installed.
- [x] 4.3 Let `ModelSetupHostedService` open the window at startup when `IsComplete` is false, and expose one window instance through `ISetupWindow` (D5). Verify: unit tests open the window with the model installed and a required grant missing (macOS), and not with everything complete.
- [x] 4.4 Make the setup menu item one item per platform (D5, spec `macos-permissions` "Set up menu item", `model-management` "Reopening setup from the tray"): **Download model…** on Windows and without a bundle, and **Set up Pisum Transcribe…** on macOS with a bundle, visible while the model or a required grant is missing. Verify: unit tests with a fake `ITrayIconService` see exactly one setup item in each of the three cases, with its label, and its visibility following the fake model and grants at the moment the check runs.

## 5. The relaunch

- [x] 5.1 Add `ShutdownReason.Relaunch`. `ShutdownCoordinator` starts `/usr/bin/open -n "<bundle>"` through an injectable launcher as the first step of `ShutdownAsync` for that reason, then ends as for `UserExit` with exit code 0 (D4). A launcher failure is logged, and the shutdown continues. Verify: `ShutdownCoordinatorTests` see the launcher called before the host stops, exit code 0, and the shutdown finishing when the launcher throws.
- [x] 5.2 Add `RelaunchService` in `Permissions/MacOS/` (D4, D5): Accessibility checked every 2 s while it's not granted, only a false-to-true transition counts, it waits for `IModelStore.IsDownloading` to become false, and there's a 3 s notice in the window when it's open, then `RequestShutdownAsync(Relaunch)`. The bundle path comes from `AppContext.BaseDirectory`, with no relaunch without an enclosing `.app`. Verify: unit tests with `FakeTimeProvider` and fakes:
  - no relaunch when granted at start
  - relaunch 3 s after a transition
  - a download delays it until `DownloadStateChanged`
  - a grant while the window is closed relaunches within 10 s
  - no relaunch without a bundle
- [x] 5.3 Show the row texts of the relaunch in the window ("Restarts when the download is finished", "Pisum Transcribe restarts to turn on the hotkey"). Verify: headless tests see each text in its state.
- [x] 5.4 Add a macOS `Integration` test of the relaunch's parts that touch the system (D10). There is no test-only switch in the app.
  - The bundle path resolves to `…/Pisum Transcribe.app` from the dev bundle's `Contents/MacOS/` folder, and to nothing from a folder without an enclosing `.app`.
  - The launcher's `open -n` on the real dev bundle starts a new process, which the test finds by the bundle ID with a new pid, then ends with `SIGTERM`.
  - The test calls `Assert.SkipWhen` when the dev bundle hasn't been built.

  Verify: it passes on the dev Mac.

## 6. End to end and docs

- [ ] 6.1 Run the first start on the dev Mac after `tccutil reset All io.github.mschnecke.pisum-transcribe` and deleting the models folder. This is the only check of the whole relaunch chain, from a real grant to one running instance (D10):
  - the window opens with four rows and the notification prompt
  - granting the microphone updates the row within 2 s
  - granting Accessibility during the download shows the waiting text, and the app relaunches after the download
  - after the relaunch the window stays closed
  - revoking the microphone brings back **Set up Pisum Transcribe…**
  - `tmutil isexcluded` reports the models folder

  Verify: every step as described, noted in the PR.
- [x] 6.2 Update the docs:
  - `CLAUDE.md`: the `Permissions/` folder, helper ABI 2, the relaunch under `dotnet run`, and `tccutil reset` for a fresh first run
  - `docs/roadmap.md`: the change done

  Verify: the texts match the code.
- [ ] 6.3 Run `openspec validate add-macos-setup --strict`, and `dotnet build` plus `dotnet test Pisum.Transcribe.slnx` on the Mac and in CI on Windows. Verify: all pass.
