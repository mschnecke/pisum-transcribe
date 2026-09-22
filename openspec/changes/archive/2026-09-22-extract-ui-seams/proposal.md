## Why

Pisum Transcribe is to ship on macOS next to Windows, on an Avalonia shell (`move-windows-shell-to-avalonia`). Today the services are tied to WPF and to the Windows tray in three places. Seven of them call `Application.Current.Dispatcher`. `ITrayIconService.SetStatus` takes a `System.Drawing.Icon`, a GDI+ type. Notifications are a method of the tray icon. This change puts a seam in each place while WPF still runs, with no visible change, so that the later changes can swap an implementation instead of rewriting callers. The seams are needed whichever shell macOS gets, so this change carries no regret even if the Avalonia spike fails.

## What Changes

- **UI thread:** a new `IUiDispatcher` with one member, `InvokeAsync(Action)`, replaces every `Application.Current.Dispatcher` call in the services and the `invokeOnUiThread` test delegates that default to it. It queues the action, also on the UI thread, and an exception in the action only faults the returned task, as WPF's and Avalonia's `InvokeAsync` do. Its first implementation wraps WPF's dispatcher. `move-windows-shell-to-avalonia` gives it an Avalonia one.
- **Tray status:** `ITrayIconService.SetStatus` takes a `TrayStatus` value (`Ready`, `Recording`, `Transcribing`, `Unavailable`) and the tooltip, instead of an `Icon`. The WPF tray maps the status to today's `DictationIcons`, so the icons look as they do now.
- **Notifications:** a new `INotifier` (`Show(title, message)`), which may be called from any thread, takes over from `ITrayIconService.ShowNotification`, and its five callers switch to it. Its first implementation is today's balloon tip, which it shows on the UI thread itself. `show-windows-notifications` gives it a toast implementation. The keyboard hook and the transcriber's hosted service then no longer need the UI thread.
- **Platform folders:** code that is Windows-only and stays Windows-only moves into a `Windows/` subfolder of its feature folder, keeping the feature's namespace. That is WASAPI capture, the foreground window and elevation checks, and the startup registration. The tests follow. Rider's project settings keep these folders out of the namespace.
- No visible change, and no spec change.
- Not included:
  - the Avalonia shell, the toasts, the new tray icons and the clipboard rewrite, which are their own changes
  - moving WPF windows or other code that the Avalonia shell replaces anyway
  - splitting files that mix Windows and portable code, such as `SharpHookKeyboardInput`, which the macOS changes split when they need to

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
<!-- None. This is a refactor with no change in behavior, so `.openspec.yaml` sets `skip_specs: true`. -->

## Impact

- **Code:**
  - `DictationFeedback`, `UpdateCheckService`, `SettingsViewModel` with `DictationSectionViewModel` and `ModelSectionViewModel`, `SettingsWindowService` and `ModelSetupHostedService`, for the dispatcher
  - `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `ShutdownCoordinator`, for notifications, and `App.OnStartup`, which passes the notifier to `ShutdownCoordinator`
  - `Tray/ITrayIconService` and `TrayIconService`, and `DictationFeedback`, for the status
  - `AppHost`, which registers the WPF dispatcher, and `AddTray()`, which registers one tray icon for both `ITrayIconService` and the balloon notifier
  - new files: `IUiDispatcher` with its WPF implementation, `INotifier` with its balloon implementation, and `TrayStatus`
  - files moved into `Windows/` subfolders: `Recording/WasapiCaptureSession(Factory)`, `TextInsertion/ForegroundWindowTracker` and `ProcessElevation`, and `SettingsWindow/StartupRegistration`, `IUserRegistry` and `UserRegistry`
  - new `Pisum.Transcribe.csproj.DotSettings` and `Pisum.Transcribe.Tests.csproj.DotSettings`, which keep the `Windows/` folders out of the namespace in Rider
- **Tests:** a shared `InlineUiDispatcher` replaces the `action => action()` delegates, and a fake `INotifier` replaces the tray's `ShowNotification` in the tests of its callers. The tests assert the same states, titles and messages. The tests of moved files move with them.
- **Dependencies:** none.
- **User-visible:** nothing.
- **Unblocks:** `add-monochrome-tray-icons`, `show-windows-notifications` and `move-windows-shell-to-avalonia`.
