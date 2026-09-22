## Why

Notifications are balloon tips of the tray icon today. The Avalonia shell (`move-windows-shell-to-avalonia`) removes H.NotifyIcon, and Avalonia's tray icon can't show notifications at all. Windows 11 already turns balloons into toasts, but without a proper sender name or icon. This change shows every notification as a Windows toast from "Pisum Transcribe", behind `extract-ui-seams`' `INotifier`, on today's WPF shell. It carries no regret even if the Avalonia spike fails.

## What Changes

- **Notifications become Windows toasts** from "Pisum Transcribe", with the app's icon:
  - They stay in Windows' notification center until the user clears them, also after the app has ended.
  - Windows lists Pisum Transcribe in its notification settings, where the user can turn its notifications off. Focus Assist and Do Not Disturb apply.
  - Every existing notification keeps its trigger and its text. None of them gets a click action.
- **Registration:** at every start, the app registers its AppUserModelID for the current user in the registry, with its name and icon. Uninstalling removes that registration. An upgrade keeps it.
- **The target framework** becomes `net10.0-windows10.0.19041.0`, for the Windows notification API. The minimum Windows version becomes Windows 10 version 2004.
- Not included:
  - click actions or buttons on notifications
  - macOS notifications, which come with `add-macos-shell`

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `app-shell`: "Unhandled errors end the application visibly" names a Windows notification instead of a tray notification. It stays in the notification center after the process has ended.
- `packaging`: "Uninstalling" also removes the app's notification registration. The Windows installer requirement names Windows 10 version 2004 or later.

The spec deltas are written after the first task, which checks that a toast shows with the registry registration alone (design D3).

## Impact

- **Depends on:** `extract-ui-seams` (`INotifier`).
- **Code:**
  - a new `ToastNotifier : INotifier` and the registration at startup
  - `Pisum.Transcribe.csproj` (the target framework)
  - `TrayBalloonNotifier` goes
  - `ShutdownCoordinator`: `ErrorNotificationDuration` goes, so the error exit no longer waits 3 s (design D4)
- **Packaging:** `Pisum.Transcribe.wxs` gets an uninstall-only removal of the registration, with the same kind of quiet custom action as the startup entry. `TrayIcon.png` from `add-monochrome-tray-icons` ships next to the exe for the registration's icon. Without that change, the tool renders it here.
- **Tests:** the toast content and the registration values, against fakes. Showing a real toast is a `Hardware` test.
- **Docs:** `README.md` names the minimum Windows version.
- **User-visible:** notifications look like other apps' notifications, and they stay in the notification center.
