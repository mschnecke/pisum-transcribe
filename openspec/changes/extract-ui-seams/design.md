## Context

See proposal.md, Why. This is the first of five Windows changes before the macOS port (explore mode, 2026-09-22):

```
extract-ui-seams --+--> add-monochrome-tray-icons --+
                   +--> show-windows-notifications -+
use-win32-clipboard --------------------------------+--> move-windows-shell-to-avalonia --> add-macos-shell --> ...
```

Current state that the approach depends on:
- **The dispatcher:**
  - Seven services call `Application.Current.Dispatcher`.
  - Five of them take an `invokeOnUiThread` delegate for tests that defaults to it, for example `_invokeOnUiThread = invokeOnUiThread ?? (action => Application.Current.Dispatcher.InvokeAsync(action))`. They are `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `SettingsViewModel`.
  - `ModelSetupHostedService` and `SettingsWindowService` call it directly.
- **The tray:**
  - `ITrayIconService.SetStatus(Icon icon, string toolTip)` takes a `System.Drawing.Icon`.
  - `DictationIcons` draws the four icons with GDI+ at startup, and `DictationFeedback` passes them in.
- **Notifications:** `ITrayIconService.ShowNotification(title, message)` is a balloon tip, called from `DictationFeedback`, `TranscriberHostedService`, `SharpHookPushToTalkHotkey`, `UpdateCheckService` and `ShutdownCoordinator`. None of them handles a click.
- **The layout:** each feature folder is its own namespace (`CLAUDE.md`). The Windows-only code sits next to the portable code of its feature.

## Goals / Non-Goals

**Goals:**
- After this change, no service outside `Tray/` and the WPF windows touches a WPF or GDI+ type.
- Every test passes unchanged in what it asserts. Only the fakes change.

**Non-Goals:**
- Any change of behavior or of the look.
- Moving the WPF windows, `TrayIconService` or `DictationIcons`. The Avalonia shell and `add-monochrome-tray-icons` replace them, so moving them now would be churn.

## Decisions

### D1: `IUiDispatcher`

- **Members:** `Post(Action)`, `InvokeAsync(Action)` returning `Task`, `InvokeAsync<T>(Func<T>)` returning `Task<T>`, and `CheckAccess()`. That's what the callers use today.
- **Implementation:** `WpfUiDispatcher` wraps `Application.Current.Dispatcher`. It's registered once in `AppHost.Create`.
- **The switch:**
  - Every `Application.Current.Dispatcher` in a service becomes an injected `IUiDispatcher`.
  - Existing `invokeOnUiThread` parameters stay as test hooks and default to `IUiDispatcher.InvokeAsync`, so their tests don't change.
- **Not behind the seam:** `DispatcherWait`, `ShutdownCoordinator`'s WPF shutdown call, and `App`. They belong to the application lifetime, which `move-windows-shell-to-avalonia` replaces as a whole (its D7).

*Rejected:*
- **`SynchronizationContext`:** it exists on both frameworks. But it has no `CheckAccess`, and it hides which thread a service expects.

### D2: `TrayStatus`

- **`TrayStatus`** (`Ready`, `Recording`, `Transcribing`, `Unavailable`) replaces the `Icon` in `SetStatus(TrayStatus status, string toolTip)`.
- **The WPF `TrayIconService`** holds a `DictationIcons` and maps each status to today's icon, so nothing changes on screen.
- **`DictationFeedback`** passes the status instead of an icon, and no longer references `DictationIcons`.
- `add-monochrome-tray-icons` then replaces the icons inside the tray service alone.

### D3: `INotifier`

- **`INotifier.Show(string title, string message)`** takes over from `ITrayIconService.ShowNotification`, which goes. The five callers switch to it.
- **The first implementation, `TrayBalloonNotifier`,** calls the H.NotifyIcon tray's balloon, as today. It lives in `Tray/`, next to the service it wraps.
- **Thread:** callers marshal to the UI thread as they do today. `INotifier` makes no threading promise of its own, so the toast implementation may relax that later.
- `show-windows-notifications` replaces the implementation. `move-windows-shell-to-avalonia` then removes the balloon along with H.NotifyIcon.

### D4: `Windows/` subfolders

- **What moves:** code that is Windows-only and stays Windows-only after the Avalonia shell:
  - `Recording/Windows/`: `WasapiCaptureSession`, `WasapiCaptureSessionFactory`
  - `TextInsertion/Windows/`: `ForegroundWindowTracker`, `ProcessElevation`
  - `SettingsWindow/Windows/`: `StartupRegistration`, `IUserRegistry`, `UserRegistry`
  - The tests mirror it, for example `tests/…/Recording/Windows/WasapiCaptureSessionTests.cs`.
- **Namespaces:**
  - Files there keep the feature's namespace. The folder marks the platform, not a namespace.
  - A `.Windows` namespace segment would shadow CsWin32's `Windows.Win32` in unqualified names inside that namespace.
  - IDE settings that tie namespaces to folders get an exception for `Windows/` and later `MacOS/`.
- **What stays:**
  - WPF views, `TrayIconService` and `DictationIcons`, which the later changes replace.
  - Files that mix portable and Windows code, such as `SharpHookKeyboardInput` and `SharpHookPushToTalkHotkey` with their `GetAsyncKeyState`. A macOS change splits them when it needs to.
- **Later:** `add-macos-shell` adds the macOS target framework, excludes `**/Windows/**` from it, and uses `MacOS/` the same way.

## Risks / Trade-offs

- [A service's thread assumption changes silently when it moves to `IUiDispatcher`] → The members mirror the WPF calls one to one, and the existing tests of each service run unchanged.
- [Moving files breaks history lookup] → `git mv`, so `git log --follow` still works.
- [The balloon notifier lives in `Tray/` although it's a notifier] → Intended. It exists only until `show-windows-notifications`, and it wraps the tray.

## Migration Plan

1. `IUiDispatcher`, then `TrayStatus`, then `INotifier`, then the folders. Each step builds and passes the tests.
2. No release of its own is needed. It can ship with the next Windows release.

**Rollback:** revert the pull request. Nothing is visible, and no data changes.
