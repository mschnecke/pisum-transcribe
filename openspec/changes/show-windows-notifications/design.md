## Context

See proposal.md, Why. It's one of the five Windows changes before the macOS port (see `extract-ui-seams`' design for the graph). It builds on `extract-ui-seams`' `INotifier`, whose first implementation is the H.NotifyIcon balloon.

Current state:
- **Callers:** notifications come from `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `ShutdownCoordinator`. None of them handles a click.
- **From `extract-ui-seams` (its D3):**
  - `INotifier.Show` may be called from any thread. The balloon notifier queues itself on the UI thread.
  - `AddTray()` registers the balloon notifier as the `INotifier`. The `Notifications/` folder holds only `INotifier` and has no `Add` method.
  - After an error, `ShutdownCoordinator` keeps the tray icon for `ErrorNotificationDuration` (3 s) after the notification, because removing the icon dismisses the balloon. No spec asks for the wait.
- **The target framework** is `net10.0-windows`, which has no WinRT projections.
- **The MSI** creates a Start Menu shortcut. On uninstall it removes the startup entry with two quiet `reg.exe` custom actions, whose condition is `REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE` (`add-msi-installer` D3).
- **Avalonia 12.1** has no API for OS notifications. `WindowNotificationManager` shows notifications inside a window only (checked in its API docs).

## Goals / Non-Goals

**Goals:**
- Every notification the specs name appears as a toast from "Pisum Transcribe", installed or started with `dotnet run`.
- A failed toast never throws into a caller.

**Non-Goals:**
- Click actions, buttons, or an activation handler.
- Grouping or replacing earlier notifications.

## Decisions

### D1: WinRT toasts

- **`ToastNotifier : INotifier`** builds a `ToastGeneric` template with the title and the text. It shows it with `ToastNotificationManager.CreateToastNotifier(aumid)`.
- **No actions and no activation handler,** because no notification has a click action today.
- **Failures:** a toast that can't be shown, for example because the user turned notifications off, is logged at Debug and never throws.
- **Consequences:**
  - The target framework becomes `net10.0-windows10.0.19041.0` for the projections, and the minimum Windows version becomes Windows 10 version 2004. Those builds are out of support.
  - Notifications stay in the notification center after the app ends. That's the `app-shell` delta.
  - Focus Assist and Do Not Disturb apply, as they already do to balloons on Windows 11.

*Rejected:*
- **Windows App SDK's `AppNotificationManager`:** it needs the Windows App Runtime, which adds a dependency and a lot of payload to the MSI.
- **A second, hidden `Shell_NotifyIcon` for balloons after the Avalonia shell:** a hack that Windows 11 turns into a toast anyway.
- **In-window notifications (`WindowNotificationManager`):** the app has no window open when it notifies.

### D2: The AppUserModelID in the registry

- **What the app writes:** unpackaged apps need an AppUserModelID for toasts. At every start the app writes `HKCU\Software\Classes\AppUserModelId\Pisum.Transcribe` with:
  - `DisplayName` = `Pisum Transcribe`
  - `IconUri` pointing to `TrayIcon.png` next to the exe
- **At every start:** writing it each time also repairs a key someone deleted, and it works from `dotnet run` too.
- **The AUMID `Pisum.Transcribe`** is fixed and never changes, because Windows keys the user's notification settings to it.
- **Uninstall** removes the key with a quiet custom action, under the same uninstall-only condition as the startup entry. An upgrade keeps it.

*Rejected:*
- **The AUMID as a `System.AppUserModel.ID` property on the MSI's Start Menu shortcut:** there'd be no registry key to clean up, but builds that aren't installed would show no notifications. It's the fallback if D3 fails.

### D3: Check the registration first

The first task, before the spec deltas are written: show a toast from the unpackaged exe with only the registry AUMID, on Windows 10 22H2 and Windows 11.
- **Pass:** it shows with the name "Pisum Transcribe" and its icon, and the app is listed in the notification settings.
- **Fail:** D2's fallback, the AUMID on the Start Menu shortcut. The `packaging` delta then names the shortcut instead of the registration.
- **Also checked:** a toast shown right before the process ends still appears. D4 relies on that.

This was check W4 of the Avalonia spike, but it doesn't depend on Avalonia, so it moved here.

### D4: The registration of services and the error exit

- **`AddNotifications()`:** the `Notifications/` folder gets its `Add` method. It registers `ToastNotifier` as the `INotifier` and the AUMID registration at startup. The `INotifier` registration leaves `AddTray()`, and `TrayBalloonNotifier` goes.
- **`ErrorNotificationDuration` goes:** the wait kept the tray icon so that the balloon stayed visible. A toast stays in the notification center after the process ends (D3 checks that), so the error exit no longer waits 3 s. `ShutdownCoordinatorTests` lose the wait and keep the order: the notification, then stopping the host.

## Risks / Trade-offs

- [Toasts don't show for an unpackaged exe with only the registry AUMID on some Windows 10 builds] → D3 on 22H2, and the shortcut fallback.
- [A user who turned notifications off for the app misses the "stopped because of an error" notice] → Accepted. It's the user's choice, and the log records it.
- [Toasts outlive the app in the notification center, including stale ones such as "model loading"] → Accepted. They are informational. The `app-shell` delta says so.
- [Windows 10 before version 2004 is no longer supported] → Accepted: those builds are out of support. The README states the minimum.

## Migration Plan

1. D3, then the spec deltas.
2. `ToastNotifier` and the registration, in `AddNotifications()` (D4). `TrayBalloonNotifier` and `ErrorNotificationDuration` go.
3. The MSI's uninstall removal. CI validates the MSI.
4. A check by hand: every notification the specs name, installed and with `dotnet run`, and an upgrade and an uninstall.

**Rollback:** revert the pull request. The AUMID key of a build that was installed and then rolled back is left behind, which is harmless. The next uninstall of a build with this change removes it.
