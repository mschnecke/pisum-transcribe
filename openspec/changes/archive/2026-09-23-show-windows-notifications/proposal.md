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
- `app-shell`:
  - A new requirement "Notifications come from Pisum Transcribe": the sender name and icon, the entry in Windows' notification settings, and the notification center.
  - "Unhandled errors end the application visibly" names a Windows notification instead of a tray notification. It stays in the notification center after the process has ended.
- `packaging`: "Uninstalling" also removes the app's notification registration. The Windows installer requirement names Windows 10 version 2004 or later.

The spike for design D3 showed a toast with the registry registration alone on Windows 11, so the deltas name the registration. Windows 10 22H2 is still to be checked.

## Impact

- **Depends on:** `extract-ui-seams` (`INotifier`).
- **Code:**
  - a new `ToastNotifier : INotifier` and the registration at startup
  - `Pisum.Transcribe.csproj`: the target framework, the projection excluded from ReadyToRun (design D5), and `Tray/TrayIcon.png` copied to the output
  - `Pisum.Transcribe.Tests.csproj`: the same target framework, or it can't reference the app
  - `TrayBalloonNotifier` goes
  - `ShutdownCoordinator`: `ErrorNotificationDuration` goes, so the error exit no longer waits 3 s (design D4)
- **Packaging:**
  - `Pisum.Transcribe.wxs` gets an uninstall-only removal of the registration, with the same kind of quiet custom action as the startup entry.
  - `TrayIcon.png` (from `add-monochrome-tray-icons`) ships next to the exe for the registration's icon. The MSI's `<Files>` pattern picks it up.
  - The MSI grows by about 6 MB compressed, for the Windows projection (design D5).
- **Tests:** the toast content and the registration values, against fakes. Showing a real toast is a `Hardware` test.
- **Docs:** `README.md` names the minimum Windows version.
- **User-visible:** notifications look like other apps' notifications, and they stay in the notification center.
