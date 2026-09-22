## Context

See proposal.md, Why. This is the first of five Windows changes before the macOS port (explore mode, 2026-09-22):

```
extract-ui-seams --+--> add-monochrome-tray-icons --+
                   +--> show-windows-notifications -+
use-win32-clipboard --------------------------------+--> move-windows-shell-to-avalonia --> add-macos-shell --> ...
```

Current state that the approach depends on:
- **The dispatcher:**
  - Seven services call `Application.Current.Dispatcher`, always its `InvokeAsync(Action)`.
  - Five of them take an `invokeOnUiThread` delegate for tests that defaults to it, for example `_invokeOnUiThread = invokeOnUiThread ?? (action => Application.Current.Dispatcher.InvokeAsync(action))`. They are `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `SettingsViewModel`, which passes it on to `DictationSectionViewModel` and `ModelSectionViewModel`. Every test passes `action => action()`.
  - `ModelSetupHostedService` and `SettingsWindowService` call it directly and return its task from `StartAsync`.
  - No caller uses `CheckAccess`, `BeginInvoke` or a result.
- **Exceptions on the dispatcher**, checked in the sources of WPF and of Avalonia 12.1.1:
  - `InvokeAsync(Action)` stores an exception of the action in the returned task. The delegates discard that task, so today the exception is logged at most as an unobserved task exception, and the app keeps running.
  - WPF's `BeginInvoke` and Avalonia's `Post` send it to the dispatcher's `UnhandledException` instead, and `ShutdownCoordinator` ends the app with an error.
  - On both frameworks, `InvokeAsync` queues the action even when it's called on the UI thread.
- **The tray:**
  - `ITrayIconService.SetStatus(Icon icon, string toolTip)` takes a `System.Drawing.Icon`. It's the only interface that carries a WPF or GDI+ type.
  - `DictationIcons` draws the four icons with GDI+ at startup, and `DictationFeedback` passes them in.
  - H.NotifyIcon disposes the icon it replaces and the icon it holds when it's disposed, so `TrayIconService` hands it a copy. Two tests guard that.
- **Notifications:**
  - `ITrayIconService.ShowNotification(title, message)` is a balloon tip, called from `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `ShutdownCoordinator`. None of them handles a click.
  - `TranscriberHostedService` and `SharpHookPushToTalkHotkey` reach the UI thread only to show a notification.
  - H.NotifyIcon's balloon goes straight to `Shell_NotifyIcon`. After the icon is removed, though, it reads a WPF dependency property on its way to an `ObjectDisposedException`, so it's called on the UI thread.
  - After an error, `ShutdownCoordinator` keeps the tray icon for `ErrorNotificationDuration` (3 s) after the notification, because removing the icon dismisses the balloon. No spec asks for the wait.
- **The layout:**
  - Each feature folder is its own namespace (`CLAUDE.md`). The Windows-only code sits next to the portable code of its feature.
  - The repository has no `.editorconfig` and no `.DotSettings`. Rider flags a namespace that doesn't match its folder.
  - CsWin32 is imported with `using Windows.Win32…;` directives at the top of each file.
  - `GlobalUsing.cs` imports `System.Windows` into every file.

## Goals / Non-Goals

**Goals:**
- After this change:
  - every service reaches the UI thread through `IUiDispatcher`, and only `AppHost` reads `Application.Current`
  - no interface carries a WPF or GDI+ type
  - every notification goes through `INotifier`
- What still uses WPF is code that a later change replaces: the application lifetime (`App`, `DispatcherWait`, `ShutdownCoordinator`, `WpfUiDispatcher`), the tray and `DictationIcons`, the three windows and the services that open them, and `WpfClipboardService`.
- Every test passes unchanged in what it asserts. Only the fakes change.

**Non-Goals:**
- Any change of behavior or of the look.
- Moving the WPF windows, `TrayIconService` or `DictationIcons`. The Avalonia shell and `add-monochrome-tray-icons` replace them, so moving them now would be churn.
- Removing `global using System.Windows`. When `move-windows-shell-to-avalonia` drops `UseWPF`, the compiler finds every WPF type anyway.

## Decisions

### D1: `IUiDispatcher`

- **Member:** `Task InvokeAsync(Action action)`, the only call the services make today. Fire-and-forget callers discard the task (`_ = …`). The two `StartAsync` methods return it.
- **Contract,** in its XML doc:
  - The action is queued on the UI thread, also when the caller is on it. `SettingsViewModel` relies on that: it saves on the UI thread, the store raises `Changed` there, and `ApplyBaseline` must run after the save returns.
  - An exception thrown by the action faults the returned task and never reaches the dispatcher's unhandled-exception handler. That's today's behavior.
- **Implementation:** `WpfUiDispatcher` takes a `Dispatcher` and calls its `InvokeAsync`. `AppHost.Create`, which runs on the UI thread, registers it as a singleton with `Application.Current.Dispatcher`. Taking the dispatcher lets its tests run on an STA thread that has a dispatcher but no WPF `Application`.
- **The switch:**
  - The `invokeOnUiThread` delegates go.
  - `DictationFeedback`, `UpdateCheckService`, `SettingsWindowService` and `ModelSetupHostedService` take an `IUiDispatcher`. `SettingsWindowService` passes it to `SettingsViewModel`, which passes it to `DictationSectionViewModel` and `ModelSectionViewModel`.
  - `SharpHookPushToTalkHotkey` and `TranscriberHostedService` used the delegate only for a notification, so they lose it and get no dispatcher (D3).
- **Tests:**
  - The service tests use `InlineUiDispatcher`, a shared helper at the test project root. It runs the action at once and lets an exception propagate, as today's `action => action()` does.
  - It breaks the contract on purpose: a faulted task that the service discards would hide a failing assertion.
  - `WpfUiDispatcherTests` cover the queueing and the exception rule on a real dispatcher. The Avalonia implementation gets the same tests.
- **Not behind the seam:** `DispatcherWait`, `ShutdownCoordinator`'s WPF shutdown call and its handler for unhandled exceptions, and `App`. They belong to the application lifetime, which `move-windows-shell-to-avalonia` replaces as a whole (its D7).

*Rejected:*
- **`Post(Action)`:** on both frameworks it sends an exception to the unhandled-exception handler, and `ShutdownCoordinator` then ends the app. Every call today uses `InvokeAsync`, so a `Post` member invites a silent change of behavior. Add it with a caller that wants that behavior.
- **`CheckAccess` and `InvokeAsync<T>`:** no caller, today or in the planned changes. Add them with their first caller.
- **Keeping the `invokeOnUiThread` delegates as test hooks:** the constructors change anyway for `INotifier` and `TrayStatus`, and every test would pass a dispatcher it never uses next to the delegate.
- **`SynchronizationContext`:** it exists on both frameworks. But it hides which thread a service expects, and its `Post` has `Post`'s exception behavior above.

*Parked:* a failed fire-and-forget action is logged today only if its task is finalized, which may never happen. The seam is the place to log it, but that's new behavior, and this change has none.

### D2: `TrayStatus`

- **`TrayStatus`** (`Ready`, `Recording`, `Transcribing`, `Unavailable`) replaces the `Icon` in `SetStatus(TrayStatus status, string toolTip)`. No interface then carries a GDI+ type.
- **The WPF `TrayIconService`** takes `DictationIcons` and maps each status to today's icon with `internal static Icon IconFor(TrayStatus status, DictationIcons icons)`, so nothing changes on screen. It still hands H.NotifyIcon a copy.
- **`DictationFeedback`** passes the status instead of an icon, and no longer references `DictationIcons`.
- **Tests:**
  - `IconFor` is tested directly. The `TaskbarIcon` holds a copy, which a test can't compare with the expected icon.
  - The two tests that guard the copy are rewritten for statuses: showing Ready again after Recording works, and `Remove` leaves `DictationIcons`' icons usable.
  - `DictationFeedbackTests` assert the status where they asserted the icon.
- **`DictationIcons` stays in `Dictation/`,** although only the tray uses it now:
  - `Tray/` and `Dictation/` then depend on each other, which is harmless within one assembly.
  - `add-monochrome-tray-icons` deletes `DictationIcons` next, and its tasks already name its current place and registration.
- `add-monochrome-tray-icons` then replaces the icons inside the tray service alone, and extends `IconFor` by the taskbar mode.

### D3: `INotifier`

- **`INotifier.Show(string title, string message)`** takes over from `ITrayIconService.ShowNotification`, which leaves the interface. The five callers switch to it.
- **Thread:** `Show` may be called from any thread and returns without waiting. An implementation that needs a particular thread marshals to it itself:
  - The balloon queues itself on the UI thread.
  - A toast (`show-windows-notifications`) doesn't need a particular thread.
  - The macOS notifier (`add-macos-shell`) reaches the main thread through `IUiDispatcher`.
  - So `SharpHookPushToTalkHotkey` and `TranscriberHostedService` no longer know about the UI thread, and `DictationFeedback.Notify` and `UpdateCheckService` call the notifier directly.
- **`INotifier`** lives in a new `Notifications/` feature folder (namespace `Pisum.Transcribe.Notifications`). The folder has no `Add` method yet, because it has nothing to register.
- **The first implementation, `TrayBalloonNotifier`,** takes `IUiDispatcher` and `TrayIconService`:
  - `Show` queues the tray's balloon with `InvokeAsync`, so a balloon after `Remove` fails inside the task, as today.
  - It lives in `Tray/`, next to the service it wraps.
  - `TrayIconService` keeps the balloon as an internal method, `ShowNotification`.
- **Registration:**
  - `AddTray()` registers `TrayIconService` once, forwards `ITrayIconService` to it, and registers `TrayBalloonNotifier` as the `INotifier`. That's how `AddSettingsWindow()` registers `StartupRegistration`.
  - Registering `TrayIconService` a second time would create a second tray icon that's never shown, and every notification would go there.
  - `App.OnStartup` still resolves the tray first, on the UI thread.
- **`ShutdownCoordinator`** keeps `ErrorNotificationDuration`:
  - After an error it keeps the tray icon for 3 s after the notification, because removing the icon dismisses the balloon.
  - That assumes the balloon notifier, and the field's XML doc says so.
  - `show-windows-notifications` revisits it, because a toast stays after the process ended.
- **Later:** `show-windows-notifications` creates `AddNotifications()` with the toast, moves the `INotifier` registration there, and removes `TrayBalloonNotifier`. `move-windows-shell-to-avalonia` removes H.NotifyIcon.

*Rejected:*
- **Callers marshal to the UI thread, as today:** the toast doesn't need it, the macOS notifier marshals anyway, and two services would keep a dispatcher only for a notification.
- **`TrayIconService` as the `INotifier`:** `ITrayIconService` is called on the UI thread, and `INotifier` from any thread. One class with two thread rules invites mistakes.
- **`AddNotifications()` now:** it would register a `Tray/` type from `Notifications/`, only to change it in the next change.

### D4: `Windows/` subfolders

- **What moves:** code that is Windows-only and stays Windows-only after the Avalonia shell:
  - `Recording/Windows/`: `WasapiCaptureSession`, `WasapiCaptureSessionFactory`
  - `TextInsertion/Windows/`: `ForegroundWindowTracker`, `ProcessElevation`
  - `SettingsWindow/Windows/`: `StartupRegistration`, `IUserRegistry`, `UserRegistry`
  - The tests mirror it, for example `tests/…/Recording/Windows/WasapiCaptureSessionTests.cs`.
- **Namespaces:**
  - Files there keep the feature's namespace. The folder marks the platform, not a namespace.
  - The move is then a pure rename. No file content, caller or test `using` changes, and `git log --follow` sees each file unchanged.
  - `add-macos-shell` picks an implementation in each `Add<Feature>()` with `#if`. With one namespace, the `#if` wraps the registration only, not the `using`s.
  - A `.Windows` segment would also shadow CsWin32's `Windows.Win32` in a fully qualified name inside that namespace. Today's `using` directives at the top of each file aren't affected, so that's a trap for later rather than a break now.
- **Rider:**
  - Rider's inspection "Namespace does not correspond to file location" would flag the moved files, and its quick fix adds the `.Windows` segment.
  - `Pisum.Transcribe.csproj.DotSettings` and `Pisum.Transcribe.Tests.csproj.DotSettings` mark each `Windows/` folder as not a namespace provider, one entry per folder.
  - A later change that adds a platform folder adds its entry.
- **What stays:**
  - WPF views, `TrayIconService` and `DictationIcons`, which the later changes replace.
  - Files that mix portable and Windows code, such as `SharpHookKeyboardInput` and `SharpHookPushToTalkHotkey` with their `GetAsyncKeyState`. A macOS change splits them when it needs to.
- **Later:** `add-macos-shell` adds the macOS target framework, excludes `**/Windows/**` from it, and uses `MacOS/` the same way.

## Risks / Trade-offs

- [A service's thread assumption changes silently when it moves to `IUiDispatcher`] → `WpfUiDispatcher` calls WPF's `InvokeAsync`, which every call uses today, and its tests pin the queueing and the exception rule. The existing tests of each service run unchanged in what they assert.
- [A later implementation maps `InvokeAsync` to `Post`, and a failed UI action ends the app] → The contract is in `IUiDispatcher`'s XML doc, and `WpfUiDispatcherTests` hold the tests that the Avalonia implementation copies.
- [`InlineUiDispatcher` runs the action inline, so the service tests don't see the queueing] → As today with `action => action()`. The queueing is tested once, on the real dispatcher.
- [A second registration of `TrayIconService` sends every notification to an icon that isn't shown, and no test with fakes notices] → The forwarding registration, and a check by hand that a notification shows.
- [Moving files breaks history lookup] → `git mv`, and the contents don't change, so `git log --follow` still works.
- [The balloon notifier lives in `Tray/` although it's a notifier] → Intended. It exists only until `show-windows-notifications`, and it wraps the tray.

## Migration Plan

1. `IUiDispatcher`, then `INotifier` with its callers, then the other services on `IUiDispatcher`, then `TrayStatus`, then the folders. Notifications come before the switch, so the two services that lose the UI thread change only once. Each step builds and passes the tests.
2. No release of its own is needed. It can ship with the next Windows release.

**Rollback:** revert the pull request. Nothing is visible, and no data changes.
