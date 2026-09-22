## 1. UI dispatcher

- [x] 1.1 Add `Hosting/IUiDispatcher` with one member, `Task InvokeAsync(Action action)` (design D1). Its XML doc states the contract:
  - The action is queued on the UI thread, also when the caller is on it.
  - An exception thrown by the action faults the returned task and never reaches the dispatcher's unhandled-exception handler.

  Add `Hosting/WpfUiDispatcher`, which takes a `Dispatcher` and calls its `InvokeAsync`. `AppHost.Create` registers it as a singleton with `Application.Current.Dispatcher`. Verify that these new STA tests in `WpfUiDispatcherTests` pass, each on a thread that runs a WPF dispatcher:
  - `InvokeAsync_FromOtherThread_RunsOnDispatcherThread`
  - `InvokeAsync_OnDispatcherThread_RunsAfterCurrentOperation`
  - `InvokeAsync_ActionThrows_FaultsTaskWithoutUnhandledException`

## 2. Notifications

- [x] 2.1 Add `INotifier` with `Show(string title, string message)` in a new `Notifications/` feature folder (namespace `Pisum.Transcribe.Notifications`). Its XML doc says that `Show` may be called from any thread and returns without waiting (design D3). Then:
  - Add `Tray/TrayBalloonNotifier`, which takes `IUiDispatcher` and `TrayIconService` and queues the balloon with `InvokeAsync`.
  - `ShowNotification` leaves `ITrayIconService` and stays on `TrayIconService` as an internal method.
  - `AddTray()` registers `TrayIconService` once, forwards `ITrayIconService` to it as `AddSettingsWindow()` does for `StartupRegistration`, and registers `TrayBalloonNotifier` as the `INotifier`.

  Verify: a new STA test `TrayBalloonNotifierTests.Show_FromAnyThread_QueuesOnUiDispatcher` passes, with a fake `IUiDispatcher` that doesn't run the action.
- [x] 2.2 Switch the five callers to `INotifier` (design D3):
  - `DictationFeedback.Notify` and `UpdateCheckService` call it directly, without the UI thread.
  - `SharpHookPushToTalkHotkey` and `TranscriberHostedService` call it directly and lose their `invokeOnUiThread` parameter, which they used only for the notification.
  - `ShutdownCoordinator` gets it from `App.OnStartup`. It keeps `ErrorNotificationDuration`, whose XML doc now says that removing the icon dismisses a balloon.

  Verify:
  - Their tests fake `INotifier` instead of the tray's `ShowNotification`, and assert the same titles and messages. `ShutdownCoordinatorTests` assert the same order: the notification, stopping the host, then removing the icon.
  - `grep -rn "ShowNotification" src/Pisum.Transcribe --include='*.cs'` finds only `TrayIconService`'s internal method and `TrayBalloonNotifier`.
  - A notification shows as a balloon, checked by hand.

## 3. Dispatcher switch

- [x] 3.1 Add `InlineUiDispatcher` at the test project root. It runs the action at once and lets an exception propagate, and its XML doc says why it differs from the contract (design D1). Then switch the other services to an injected `IUiDispatcher`, and remove their `invokeOnUiThread` parameters:
  - `DictationFeedback` and `UpdateCheckService` replace the delegate.
  - `SettingsWindowService` and `ModelSetupHostedService` replace `Application.Current.Dispatcher`.
  - `SettingsWindowService` passes its dispatcher to `SettingsViewModel`, which passes it to `DictationSectionViewModel` and `ModelSectionViewModel` instead of the delegate.
  - Their tests pass an `InlineUiDispatcher` instead of `action => action()`.

  Verify:
  - `grep -rn "Application.Current" src/Pisum.Transcribe --include='*.cs'` finds only `AppHost`.
  - `grep -rn "invokeOnUiThread" src/Pisum.Transcribe --include='*.cs'` finds nothing.
  - Every existing test passes unchanged in what it asserts.

## 4. Tray status

- [x] 4.1 Add `Tray/TrayStatus` (`Ready`, `Recording`, `Transcribing`, `Unavailable`) (design D2):
  - `ITrayIconService.SetStatus` becomes `SetStatus(TrayStatus status, string toolTip)`, and `ITrayIconService` no longer uses `System.Drawing`.
  - `TrayIconService` takes `DictationIcons` and maps each status with `internal static Icon IconFor(TrayStatus status, DictationIcons icons)`. It still hands H.NotifyIcon a copy.
  - `DictationFeedback` passes the status and no longer references `DictationIcons`, which stays in `Dictation/` with its registration.

  Verify:
  - `DictationFeedbackTests` assert the status where they asserted the icon, with the same states and tooltips.
  - A new test `TrayIconServiceTests.IconFor_EachStatus_ReturnsItsIcon` passes.
  - `SetStatus_IconShownAgainAfterAnother_DoesNotThrow` and `Remove_AfterSetStatus_LeavesCallerIconUsable` are rewritten as `SetStatus_StatusShownAgainAfterAnother_DoesNotThrow` and `Remove_AfterSetStatus_LeavesDictationIconsUsable`, and pass.
  - The tray looks as before in each state, checked by hand.

## 5. Windows folders

- [x] 5.1 With `git mv`, move the Windows-only code that stays Windows-only into `Windows/` subfolders, keeping each file's namespace and content (design D4):
  - `Recording/Windows/`: `WasapiCaptureSession` and `WasapiCaptureSessionFactory`
  - `TextInsertion/Windows/`: `ForegroundWindowTracker` and `ProcessElevation`
  - `SettingsWindow/Windows/`: `StartupRegistration`, `IUserRegistry` and `UserRegistry`
  - The tests move the same way: `WasapiCaptureSessionTests`, `ForegroundWindowTrackerHardwareTests`, `StartupRegistrationTests`, `UserRegistryTests` and `FakeUserRegistry`.

  Verify: `dotnet build Pisum.Transcribe.slnx` passes with no warning, `dotnet test Pisum.Transcribe.slnx` passes, and `git log --follow` on a moved file shows its history.
- [ ] 5.2 Add `src/Pisum.Transcribe/Pisum.Transcribe.csproj.DotSettings` and `tests/Pisum.Transcribe.Tests/Pisum.Transcribe.Tests.csproj.DotSettings`, which mark each new `Windows/` folder as not a namespace provider (in Rider: the folder's properties, *Namespace provider* off) (design D4). Verify: in Rider, no moved file shows "Namespace does not correspond to file location".

## 6. Documentation

- [x] 6.1 Update `CLAUDE.md`:
  - *UI thread:* services marshal through `IUiDispatcher.InvokeAsync`, which queues the action and keeps an exception in the returned task.
  - *Notifications:* they go through `INotifier`, which may be called from any thread.
  - *Feature folders:* the platform subfolder rule (`Windows/`, later `MacOS/`, with the feature's namespace), and the `.DotSettings` entry that each new platform folder needs.
  - *Layout:* the `Notifications/` folder.
  - *Tests:* `InlineUiDispatcher` among the shared helpers.

  Verify: `CLAUDE.md` names `IUiDispatcher`, `INotifier`, `InlineUiDispatcher` and the `Windows/` rule, and no longer tells services to use `Application.Current.Dispatcher`.
