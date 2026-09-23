## Context

See proposal.md, Why. It's one of the five Windows changes before the macOS port (see `extract-ui-seams`' design for the graph). It builds on `extract-ui-seams`' `INotifier`, whose first implementation is the H.NotifyIcon balloon.

Current state:
- **Callers:** notifications come from `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `ShutdownCoordinator`. None of them handles a click.
- **From `extract-ui-seams` (its D3):**
  - `INotifier.Show` may be called from any thread. The balloon notifier queues itself on the UI thread.
  - `AddTray()` registers the balloon notifier as the `INotifier`. The `Notifications/` folder holds only `INotifier` and has no `Add` method.
  - After an error, `ShutdownCoordinator` keeps the tray icon for `ErrorNotificationDuration` (3 s) after the notification, because removing the icon dismisses the balloon. No spec asks for the wait.
- **The target framework** is `net10.0-windows`, which has no WinRT projections. The test project has the same target framework.
- **`Tray/TrayIcon.png`** (256 px, from `add-monochrome-tray-icons`) exists, but isn't copied to the output.
- **`build-msi.ps1`** publishes self-contained with ReadyToRun, not trimmed (`add-msi-installer` D2). The MSI takes every published file through a `<Files>` pattern.
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
- **Failures:** a toast that can't be shown, for example because the user turned notifications off, is logged as a Warning and never throws. That covers creating the notifier, building the XML and `Show`. Warning, because a release build logs from Information up, and the log is where a user finds out why a notification didn't appear. It's rare, so it doesn't fill the log.
- **No check before showing:** `ToastNotifier.Setting` throws `0x80070490` (element not found) until the app has shown its first toast, so the notifier doesn't read it (D3 spike).
- **Consequences:**
  - The target framework becomes `net10.0-windows10.0.19041.0` for the projections, in the app and in the test project. The minimum Windows version becomes Windows 10 version 2004. Those builds are out of support.
  - The projection ships with the app (D5).
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

Before the spec deltas were written, a spike showed a toast from an unpackaged exe with only the registry AUMID. The check is still due on Windows 10 22H2.
- **Pass:** it shows with the name "Pisum Transcribe" and its icon, and the app is listed in the notification settings.
- **Fail:** D2's fallback, the AUMID on the Start Menu shortcut. The `packaging` delta then names the shortcut instead of the registration.
- **Also checked:** a toast shown right before the process ends still appears. D4 relies on that.

**Result on Windows 11 (build 26200, 2026-09-23): pass.** The spike wrote only the registry key, showed the error toast and exited right after `Show`:
- The toast showed with the name "Pisum Transcribe" and the icon. Windows listed the app in its notification settings and created its own `HKCU\...\Notifications\Settings\Pisum.Transcribe` key.
- A second process read one toast from `ToastNotificationManager.History` for the AUMID, so the toast outlived the process without a COM activator.
- `ToastNotifier.Setting` threw before the first toast and returned `Enabled` after it (see D1).

This was check W4 of the Avalonia spike, but it doesn't depend on Avalonia, so it moved here.

### D4: The registration of services and the error exit

- **`AddNotifications()`:** the `Notifications/` folder gets its `Add` method. It registers `ToastNotifier` as the `INotifier` and the AUMID registration at startup. The `INotifier` registration leaves `AddTray()`, and `TrayBalloonNotifier` goes.
- **`ErrorNotificationDuration` goes:** the wait kept the tray icon so that the balloon stayed visible. A toast stays in the notification center after the process ends (D3 checks that), so the error exit no longer waits 3 s. `ShutdownCoordinatorTests` lose the wait and keep the order: the notification, then stopping the host.

### D5: The projection without ReadyToRun

- **The cost:** the Windows target framework adds `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll`. The app can't trim them, because WPF doesn't support trimming. Measured in the D3 spike, with the publish settings of `build-msi.ps1`:

  | Publish | The two files | Compressed (gzip) |
  |---|---|---|
  | ReadyToRun | 52.8 + 1.3 MB | 14.5 MB |
  | Both excluded from ReadyToRun | 23.7 + 0.5 MB | 6.1 MB |

- **Decision:** `Pisum.Transcribe.csproj` excludes both files from ReadyToRun with `<PublishReadyToRunExclude>` items. A toast doesn't need startup speed, and the spike showed toasts and read the history with the excluded build.

*Rejected:*
- **ReadyToRun for the projection:** it doubles its size for code that runs a few times per session.
- **The toast COM interfaces declared by hand,** with `RoGetActivationFactory` through CsWin32 and the target framework left at `net10.0-windows`: no projection at all, but hand-written vtables for three or four WinRT interfaces that the compiler can't check.

## Risks / Trade-offs

- [Toasts don't show for an unpackaged exe with only the registry AUMID on some Windows 10 builds] → D3 on 22H2, and the shortcut fallback.
- [A user who turned notifications off for the app misses the "stopped because of an error" notice] → Accepted. It's the user's choice, and the log records it.
- [Toasts outlive the app in the notification center, including stale ones such as "model loading"] → Accepted. They are informational. The `app-shell` delta says so.
- [Windows 10 before version 2004 is no longer supported] → Accepted: those builds are out of support. The README states the minimum.
- [The MSI grows by about 6 MB compressed for the projection] → Accepted, with D5 halving it.

## Migration Plan

1. D3 on Windows 10 22H2 (Windows 11 passed). If it fails, D2's fallback and the `packaging` delta change.
2. The target framework, in the app and the test project, and D5. `ToastNotifier` and the registration, in `AddNotifications()` (D4). `TrayBalloonNotifier` and `ErrorNotificationDuration` go.
3. The MSI's uninstall removal. CI validates the MSI.
4. A check by hand: every notification the specs name, installed and with `dotnet run`, and an upgrade and an uninstall.

**Rollback:** revert the pull request. The AUMID key of a build that was installed and then rolled back is left behind, which is harmless. The next uninstall of a build with this change removes it.
