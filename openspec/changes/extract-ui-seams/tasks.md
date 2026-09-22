## 1. UI dispatcher

- [ ] 1.1 Add `Hosting/IUiDispatcher` with `Post(Action)`, `InvokeAsync(Action)`, `InvokeAsync<T>(Func<T>)` and `CheckAccess()`, and `Hosting/WpfUiDispatcher` over `Application.Current.Dispatcher`. Register it as a singleton in `AppHost.Create` (design D1). Verify that these new STA tests in `WpfUiDispatcherTests` pass, each on a thread that runs a WPF dispatcher:
  - `InvokeAsync_FromOtherThread_RunsOnDispatcherThread`
  - `CheckAccess_OnDispatcherThread_ReturnsTrue`
  - `CheckAccess_OnOtherThread_ReturnsFalse`
- [ ] 1.2 Switch the seven services to an injected `IUiDispatcher` (design D1):
  - The `invokeOnUiThread` parameters of `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `SettingsViewModel` default to `IUiDispatcher.InvokeAsync`.
  - `ModelSetupHostedService` and `SettingsWindowService` call the dispatcher directly.

  Verify: `grep -rn "Application.Current" src/Pisum.Transcribe --include='*.cs'` finds only `WpfUiDispatcher`, and every existing test passes unchanged.

## 2. Tray status

- [ ] 2.1 Add `Tray/TrayStatus` (`Ready`, `Recording`, `Transcribing`, `Unavailable`) (design D2):
  - `ITrayIconService.SetStatus` becomes `SetStatus(TrayStatus status, string toolTip)`.
  - `TrayIconService` takes `DictationIcons` and maps each status to today's icon.
  - `DictationFeedback` passes the status and no longer references `DictationIcons`.

  Verify:
  - `DictationFeedbackTests` assert the status where they asserted the icon, with the same states.
  - A new STA test `TrayIconServiceTests.SetStatus_EachStatus_ShowsItsIcon` passes.
  - The tray looks as before in each state, checked by hand.

## 3. Notifications

- [ ] 3.1 Add `INotifier` with `Show(string title, string message)` in a new `Notifications/` feature folder (namespace `Pisum.Transcribe.Notifications`). Add `Tray/TrayBalloonNotifier`, which shows the balloon through the tray icon. `AddTray()` registers it as the `INotifier`. `ShowNotification` goes from `ITrayIconService`, and `TrayIconService` keeps the balloon as an internal method for the notifier (design D3). Verify: a new `TrayBalloonNotifierTests.Show_TitleAndMessage_ShowsBalloonWithBoth` passes.
- [ ] 3.2 Switch the five callers to `INotifier`: `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `ShutdownCoordinator`. `App.OnStartup` passes the notifier to `ShutdownCoordinator` (design D3). Verify:
  - Their tests fake `INotifier` instead of the tray's `ShowNotification`, and assert the same titles and messages.
  - `grep -rn "ShowNotification" src/Pisum.Transcribe --include='*.cs'` finds only `TrayIconService`'s internal method and `TrayBalloonNotifier`.

## 4. Windows folders

- [ ] 4.1 With `git mv`, move the Windows-only code that stays Windows-only into `Windows/` subfolders, keeping each file's namespace (design D4):
  - `Recording/Windows/`: `WasapiCaptureSession` and `WasapiCaptureSessionFactory`
  - `TextInsertion/Windows/`: `ForegroundWindowTracker` and `ProcessElevation`
  - `SettingsWindow/Windows/`: `StartupRegistration`, `IUserRegistry` and `UserRegistry`
  - The tests move the same way: `WasapiCaptureSessionTests`, `ForegroundWindowTrackerHardwareTests`, `StartupRegistrationTests`, `UserRegistryTests` and `FakeUserRegistry`.

  Verify: `dotnet build Pisum.Transcribe.slnx` passes with no warning, `dotnet test Pisum.Transcribe.slnx` passes, and `git log --follow` on a moved file shows its history.

## 5. Documentation

- [ ] 5.1 Update `CLAUDE.md`:
  - *UI thread:* services marshal through `IUiDispatcher`.
  - *Feature folders:* the platform subfolder rule (`Windows/`, later `MacOS/`, with the feature's namespace).
  - *Layout:* the `Notifications/` folder.
  - *Tray menu:* notifications go through `INotifier`.

  Verify: `CLAUDE.md` names `IUiDispatcher`, `INotifier` and the `Windows/` rule, and no longer tells services to use `Application.Current.Dispatcher`.
