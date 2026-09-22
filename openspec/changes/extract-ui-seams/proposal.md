## Why

Pisum Transcribe is to ship on macOS next to Windows, on an Avalonia shell (`move-windows-shell-to-avalonia`). Today the services are tied to WPF and to the Windows tray in three places. About ten of them call `Application.Current.Dispatcher`. `ITrayIconService.SetStatus` takes a `System.Drawing.Icon`, a GDI+ type. Notifications are a method of the tray icon. This change puts a seam in each place while WPF still runs, with no visible change, so that the later changes can swap an implementation instead of rewriting callers. The seams are needed whichever shell macOS gets, so this change carries no regret even if the Avalonia spike fails.

## What Changes

- **UI thread:** a new `IUiDispatcher` (`Post`, `InvokeAsync`, `CheckAccess`) replaces every `Application.Current.Dispatcher` call in the services. Its first implementation wraps WPF's dispatcher. `move-windows-shell-to-avalonia` gives it an Avalonia one.
- **Tray status:** `ITrayIconService.SetStatus` takes a `TrayStatus` value (`Ready`, `Recording`, `Transcribing`, `Unavailable`) and the tooltip, instead of an `Icon`. The WPF tray maps the status to today's `DictationIcons`, so the icons look as they do now.
- **Notifications:** a new `INotifier` (`Show(title, message)`) takes over from `ITrayIconService.ShowNotification`, and its five callers switch to it. Its first implementation is today's balloon tip through the tray icon. `show-windows-notifications` gives it a toast implementation.
- **Platform folders:** code that is Windows-only and stays Windows-only moves into a `Windows/` subfolder of its feature folder, keeping the feature's namespace. That is WASAPI capture, the foreground window and elevation checks, and the startup registration. The tests follow.
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
  - `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService`, `SettingsViewModel`, `SettingsWindowService`, `ModelSetupHostedService` and `ShutdownCoordinator`, for the dispatcher and notifications
  - `Tray/ITrayIconService` and `TrayIconService`, and `DictationFeedback`, for the status
  - new files: `IUiDispatcher` with its WPF implementation, `INotifier` with its balloon implementation, and `TrayStatus`
  - files moved into `Windows/` subfolders: `Recording/WasapiCaptureSession(Factory)`, `TextInsertion/ForegroundWindowTracker` and `ProcessElevation`, and `SettingsWindow/StartupRegistration`, `IUserRegistry` and `UserRegistry`
- **Tests:** fakes of `IUiDispatcher` and `INotifier` where tests now fake the dispatcher delegate or the tray. The tests of moved files move with them.
- **Dependencies:** none.
- **User-visible:** nothing.
- **Unblocks:** `add-monochrome-tray-icons`, `show-windows-notifications` and `move-windows-shell-to-avalonia`.
