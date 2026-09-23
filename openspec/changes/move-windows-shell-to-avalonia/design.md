## Context

See proposal.md, Why. This is the last of five Windows changes before the macOS port, and it ships on Windows alone:

```
extract-ui-seams --+--> add-monochrome-tray-icons --+
                   +--> show-windows-notifications -+
use-win32-clipboard --------------------------------+--> move-windows-shell-to-avalonia --> add-macos-shell --> ...
spike (D3) -----------------------------------------+
```

When this change starts, the four preparing changes are done:
- **`extract-ui-seams`:**
  - `IUiDispatcher` over WPF's dispatcher
  - `TrayStatus` in `ITrayIconService.SetStatus`
  - `INotifier`
  - Windows-only code in `Windows/` subfolders
- **`use-win32-clipboard`:** `Win32ClipboardService` without WPF, and a Win32 `TestWindow`.
- **`add-monochrome-tray-icons`:** the glyph set rendered at build time, and the tray following the taskbar's mode.
- **`show-windows-notifications`:** `ToastNotifier`, the AUMID registration, and the target framework `net10.0-windows10.0.19041.0`.

Current state that the approach depends on:
- **The UI is WPF:**
  - There are three windows with about 490 lines of XAML: `SettingsDialog` (329), `ModelSetupWindow` (107) and `RecordingOverlayWindow` (50).
  - There are three synchronous `MessageBox` confirmations: deleting a model, and closing either window while a download runs. The two close confirmations are asked in the `Closing` handler.
  - The license links are `Hyperlink` elements with `RequestNavigate`.
- **The application lifetime:**
  - `DispatcherWait` runs a WPF `DispatcherFrame` until a task completes.
  - `App.OnSessionEnding` routes the end of the Windows session, and Restart Manager's `ENDSESSION_CLOSEAPP`, to `ShutdownCoordinator` (`end-with-windows-session`).
  - `ShutdownCoordinator` gets `Shutdown` and `ExitProcess` as delegates. `ExitProcess` calls `TerminateProcess` through a `DllImport` in `App.xaml.cs`.
- **The tray** is H.NotifyIcon.Wpf's `TaskbarIcon` with a WPF `ContextMenu`. It updates visibility in `PreviewTrayContextMenuOpen` and raises `DoubleClicked`.
- **The overlay** sets `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED` on its HWND. It places itself with `MonitorFromWindow`, `GetMonitorInfo` and `GetDpiForMonitor` on the target window.
- **The tests:**
  - Windows and the tray run on new STA threads: `SettingsDialogTests`, `RecordingOverlayWindowTests`, `DispatcherWaitTests` and `TrayIconServiceTests`.
  - xunit.v3 4.0.1 on Microsoft.Testing.Platform.
- **Avalonia 12.1.1**, checked in the package's API docs:
  - the tray: `TrayIcon` with `Menu`, `Clicked`, `ToolTipText` and `MacOSProperties.IsTemplateIcon`, and `NativeMenu` with `Opening` and `NeedsUpdate`. The spike found that only the macOS backend raises `Opening` and `NeedsUpdate` (W3).
  - the lifetime: `IClassicDesktopStyleApplicationLifetime.ShutdownRequested`, and `ShutdownMode.OnExplicitShutdown`. `IsOSShutdown` is documented too, but the spike found it `internal`.
  - the dispatcher: `Dispatcher.PushFrame(DispatcherFrame)` and `Dispatcher.UnhandledException`
  - windows: `Window.ShowActivated`, `ShowInTaskbar`, `WindowDecorations`, and `TopLevel.TryGetPlatformHandle`
  - no API for OS notifications, no public hook into a window's message procedure, and no message box
- **Avalonia's package graph:**
  - `Avalonia.Desktop` pulls in the Win32, X11 and macOS backends.
  - `Avalonia.Win32` depends on `Avalonia.Angle.Windows.Natives`.
  - `Avalonia.Skia` depends on SkiaSharp 3.119 and HarfBuzzSharp 8.3.
  - `Avalonia` depends on `MicroCom.Runtime` and on `Avalonia.BuildServices`, Avalonia's build-time telemetry.
- **The sister project** `mschnecke/pisum-whisper` already runs Avalonia as a menu bar app on macOS (see `add-macos-shell`'s Context).
- **Spike results, macOS half and T1** (2026-09-22, MacBook Air M4, macOS 27, Avalonia 12.1.1, SharpHook 8.0.0; branch `spike/avalonia-shell`, see D3):
  - **M1 ✓:** an agent app with no Dock icon (`activationPolicy` accessory). `NativeMenu.Opening` runs on every open, after `NeedsUpdate`, and changes headers and `IsVisible`. `MacOSProperties.IsTemplateIcon` switches between template and colored at runtime.
  - **M2 ✓:** an Avalonia window with `ShowActivated=false` never activates the app. TextEdit stayed frontmost, the posted Cmd+V landed, and the same held with TextEdit in full screen: the overlay was on the full-screen Space. The native settings (`level`, `ignoresMouseEvents`, `collectionBehavior`) are set through `TryGetPlatformHandle()`, whose descriptor is `NSWindow` and which is available before the first `Show`. No `NSPanel` is needed.
  - **M3 ✓ with a catch:**
    - SharpHook's hook runs next to Avalonia's main loop, and the spike's own posted events arrive with `IsEventSimulated=True`.
    - Without the Accessibility permission, starting the hook fails at once with `ErrorAxApiDisabled`.
    - After the grant, the hook still fails until the **process restarts**. libuiohook checks `CGPreflightPostEventAccess()`, not `AXIsProcessTrusted()`, and that doesn't see a grant made while the process runs.
  - **M4 ✓ with a catch:**
    - A quit Apple event arrives as `ShutdownRequested`, and a `DispatcherFrame` wait inside the handler held the quit for the full 1.5 s.
    - **`IsOSShutdown` is `internal`** in 12.1.1, although the docs list it. Read by reflection, it was `false` even for a quit carrying the logout reason.
    - `lifetime.Shutdown()` doesn't raise `ShutdownRequested`.
    - A real logout wasn't run, because it would have ended the session.
  - **T1 ✗, fallback ✓:**
    - `Avalonia.Headless.XUnit` 12.1.1 fails test discovery on xunit.v3 4.0.1 with a `MissingMethodException` for `TestIntrospectionHelper.GetTestCaseDetails`.
    - Plain `[Fact]`s that run their body through `HeadlessUnitTestSession.Dispatch` pass. That includes the `DispatcherWait` pattern, a `DispatcherFrame` ended from another thread.
  - **One unexplained crash:** a startup abort from an unhandled managed exception, once in eight launches, not reproducible.
- **Spike results, Windows half** (2026-09-23, Windows 11 build 26200, 150 % on a 3840 × 2160 monitor, Avalonia 12.1.1 with `UseWin32().UseSkia().UseHarfBuzz()`; same branch, `spike/WindowsSpike.cs`):
  - **W1 ✓ with a catch:**
    - `TryGetPlatformHandle()` returns the `HWND` before the first `Show`.
    - **Avalonia resets the extended styles on every `Show`.** Set before it, they are gone after it (`TOPMOST|NOREDIRECTIONBITMAP` only). Set again right after each `Show`, they hold.
    - With the four styles set that way, Notepad kept the foreground, text typed after the overlay hid landed in it, and the placement on the target window's monitor matched to the pixel.
    - **`WS_EX_LAYERED` is needed.** With it, the overlay renders (`#E6202020` blends over white to `#363636`) and hit tests pass through it. Without it, it renders, but it takes the clicks.
    - By hand: a click on the overlay lands in the window behind it, and the overlay is absent from Alt+Tab and the taskbar.
  - **W2 ✓:**
    - Avalonia's `AvaloniaMessageWindow` is a hidden **top-level** window, not a message-only one, so Windows' end-of-session messages reach it. `Win32Platform.WndProc` raises `ShutdownRequested` for `WM_QUERYENDSESSION`.
    - `ENDSESSION_CLOSEAPP` and `ENDSESSION_LOGOFF`, sent from another process with no window created: the `DispatcherFrame` wait held the answer for 1.56 s, then `Exit` came with code 0, and the process ended 1.65 s after the message.
    - `IsOSShutdown` read `false` both times. **Exit** through `Shutdown()` doesn't raise `ShutdownRequested`.
  - **W3 ✓ with a catch:**
    - A left click raises `Clicked` on the release. A right click opens the menu and raises nothing. A double-click raises `Clicked` twice, 86 ms apart.
    - The icon and the tooltip update, and the 24 px frame of the status ICOs shows sharp at 150 % (`SM_CXSMICON` is 24).
    - **`NativeMenu.Opening` and `NeedsUpdate` never run on Windows.** The menu is Avalonia's own popup, `TrayPopupRoot` with `TrayIconMenuFlyoutPresenter`, and only `Avalonia.Native` raises `Opening`.
    - **Workaround, confirmed by hand:** a class handler on `Window.WindowOpenedEvent` that matches the type name `TrayPopupRoot`. Every open creates a new popup. The handler runs 27 to 415 ms before the presenter's `Loaded`, and headers and `IsVisible` set there show on that open without a flicker.
  - **Not checked:** the placement at 100 %, and a real sign-out. Both are in the regression pass (Migration Plan).

## Goals / Non-Goals

**Goals:**
- Every existing spec holds on the Avalonia shell, except the one modified requirement in the proposal. The specs are the regression checklist (Migration Plan).
- The view models and the services stay free of UI-framework types, apart from `IUiDispatcher`.

**Non-Goals:**
- A macOS target framework, macOS code or a macOS build.
- Visual design work beyond a faithful port. Where Avalonia's Fluent theme differs from WPF's look, the Fluent look wins unless it breaks a spec.
- Replacing SharpHook, NAudio, CsWin32 or the transcription engine. They are not UI.

## Decisions

### D1: Avalonia on both platforms

Avalonia 12.1 becomes the shell on Windows now and on macOS later. It gives one copy of the windows, the tray, the dispatcher and the application lifetime. Its headless test platform runs window tests on any OS.

*Rejected:*
- **Keeping WPF and adding a native AppKit head (`net10.0-macos`):** Windows would stay untouched, and macOS would get typed bindings for NSPasteboard, NSPanel, notifications and login items. But every window and every future setting would exist twice, the second time as AppKit code with no data binding. The build would also need Xcode and the `macos` workload.
- **Keeping WPF and adding an Avalonia head for macOS only:** the XAML would be close to shared, but it would still exist twice, and the Windows shell would drift from the macOS one.

### D2: Windows moves first, as a release of its own

The Avalonia shell ships on Windows as a release of its own before any macOS code lands. The Windows specs describe a known-good behavior that the new shell is checked against. Avalonia's quirks surface on one platform, and the macOS changes then start from a shell that already works.

*Rejected:*
- **Building the macOS head on Avalonia first and switching Windows at the end:** the Mac build would arrive sooner. But two platforms would change at once, and a regression on Windows would show up only at the end.

### D3: A spike decides whether to go ahead, and checks the Mac first

A throwaway spike on a branch, not in `main`, runs before any task of this change. The four preparing changes don't wait for it, because they are needed whichever way it goes. The risk that could sink D1 is on macOS, so the macOS checks come first, on the development Mac. If they fail, the Windows shell hasn't been touched.

| # | Check | Pass | Fallback |
|---|---|---|---|
| M1 | Agent app (`LSUIElement` in a minimal `.app`), `TrayIcon` with a template icon, menu items shown and hidden in `NativeMenu.Opening`, and `MacOSProperties.IsTemplateIcon` switched at runtime between the template glyph and the red glyph (`add-monochrome-tray-icons`) | Menu bar icon only, no Dock icon, items update when the menu opens, and the icon switches between template and colored | If the switch fails: every state as a colored image, with the glyph at rest in a gray that reads on light and dark menu bars |
| M2 | With TextEdit focused, show an overlay window with `ShowActivated=false`, hide it, then post Cmd+V. Repeat with TextEdit in full screen, with `collectionBehavior` `.fullScreenAuxiliary` and `.canJoinAllSpaces` set through the window's NSWindow handle | TextEdit stays the frontmost app and receives the paste, and the overlay shows over the full-screen TextEdit on its Space | The macOS overlay becomes a native `NSPanel` (`nonactivatingPanel`), decided in the macOS dictation change |
| M3 | SharpHook's global hook on its own thread while Avalonia runs the main loop, with and without the Accessibility permission | Press and release arrive with no deadlock. Without the permission, starting the hook fails with an error the app can detect | – |
| M4 | **Quit** from the menu, and logging out | `ShutdownRequested` arrives, and a `PushFrame` wait inside it holds the quit until the shutdown ends | A handler on `applicationShouldTerminate` through Objective-C interop |
| W1 | Overlay with the four extended styles through `TryGetPlatformHandle` | Notepad keeps the focus, clicks pass through, placement is correct on the target window's monitor at 100 % and 150 % | – |
| W2 | Sign-out, and `ENDSESSION_CLOSEAPP` sent from another process as in `end-with-windows-session`, with no window shown | `ShutdownRequested` arrives. `IsOSShutdown` isn't public, so D7 treats every `ShutdownRequested` on Windows as the end of the session. The `DispatcherWait` inside it ends the app within 4.5 s, and Windows doesn't list the app as blocking | A hidden top-level window of the app's own (CsWin32 `RegisterClassEx` and `CreateWindowEx`) that routes `WM_QUERYENDSESSION` |
| W3 | Tray on Win32 | `NativeMenu.Opening` runs before the menu shows. The spike notes which button raises `Clicked`, that a right click opens the menu, and that icon and tooltip updates show | – |
| T1 | `Avalonia.Headless.XUnit` 12.1.1, built against `xunit.v3.extensibility.core` 3.2.2, with xunit.v3 4.0.1 on Microsoft.Testing.Platform | An `[AvaloniaFact]` opens a window and passes | A fixture of the project's own on `Avalonia.Headless`'s `HeadlessUnitTestSession` |

The former W4, a toast with only the registry AUMID, doesn't depend on Avalonia. It moved to `show-windows-notifications` (its D3).

`add-macos-shell` adds checks M5 and M6 for later macOS changes, which can run in the same session on the Mac.

**Go/no-go:**
- M2, or its `NSPanel` fallback, must work.
- M1, M3 and M4 must work or have a named workaround.
- Otherwise this change stops and D1 is decided again. The four preparing changes stay useful either way.

**Result of the macOS half (2026-09-22): go.** M2 works without the `NSPanel` fallback, and M1, M3 and M4 work with the workarounds named in Context and in D7. T1 uses its fallback (D11).

**Result of the Windows half (2026-09-23): go.** W1–W3 pass without their fallbacks, with two changes to the design (Context):
- The overlay sets its extended styles again after each `Show` (D8).
- On Windows, the tray menu is updated when Avalonia's menu popup opens, instead of in `NativeMenu.Opening` (D4).

The fallback planned for W1, dropping `WS_EX_LAYERED`, is ruled out: without it, the overlay takes the clicks. W3 decided the click in `settings-window`: a left click, raised on the release.

### D4: Avalonia's tray icon

One `TrayIconService` over Avalonia's `TrayIcon` replaces the H.NotifyIcon one. `ITrayIconService` keeps its members from `extract-ui-seams`, including `SetStatus(TrayStatus, toolTip)`, with one exception:
- `DoubleClicked` becomes `Clicked`, raised by Avalonia's `TrayIcon.Clicked`, and `SettingsWindowService` opens the window on it.
  - On Windows, a left click raises it on the release. A right click only opens the menu.
  - A double-click raises it twice. The second one finds the window open and brings it to the front, which is harmless.

**The menu:**
- It is a `NativeMenu`. **Exit** stays last.
- The `Func<bool>` visibility and `Func<string>` header callbacks run once each time the menu opens, as they run in `PreviewTrayContextMenuOpen` today. They stay cheap and run on the UI thread.
- **On Windows, they run from a class handler on `Window.WindowOpenedEvent`** that matches Avalonia's menu popup by its type name, `TrayPopupRoot`. `NativeMenu.Opening` never runs on Windows, because the menu is Avalonia's own popup there and only the macOS backend raises `Opening` (spike W3, Context).
  - Each open creates a new popup, and the handler runs before the popup's content is loaded. Headers and visibility set there show on that open.
  - `TrayPopupRoot` is internal to `Avalonia.Win32`. A unit test checks that the type still exists under that name, so an Avalonia update that renames it fails the build's tests instead of freezing the menu unnoticed.
  - macOS keeps `NativeMenu.Opening` (spike M1), in `add-macos-shell`.
  - *Rejected:* updating the menu whenever the state behind an item changes, instead of when it opens. It works without an Avalonia internal, but every feature that adds an item would have to report its changes, and `ITrayIconService.AddMenuItem` would lose its callback contract. It stays the fallback if the class handler stops working.

**The icon:**
- It is created at startup, hidden until `Show`.
- It loads the icons of `add-monochrome-tray-icons` as `WindowIcon`s, and keeps following the taskbar's mode as that change's registry watch reports it.
- `Remove` disposes it and makes later calls no-ops, as today.

*Rejected:*
- **H.NotifyIcon's core package** (`H.NotifyIcon` 2.4.1, `net10.0`, no WPF), for the balloons, double-click and a native Win32 menu: macOS would need a second tray implementation over Avalonia anyway. User decision in explore mode.

### D5: Notifications (moved)

Moved to `show-windows-notifications`: WinRT toasts behind `INotifier`, and the AUMID in the registry. This change only removes H.NotifyIcon, and with it `TrayBalloonNotifier` if it's still there.

### D6: Package references

- **`UseWPF` goes.** The target framework stays `net10.0-windows10.0.19041.0`, from `show-windows-notifications`, and `RuntimeIdentifier` stays `win-x64`.
- **`global using System.Windows`** goes from `GlobalUsing.cs` with `UseWPF`. Without WPF it imports nothing the app needs. `extract-ui-seams` kept it, because the compiler finds every WPF type here anyway.
- **The Avalonia packages** are `Avalonia`, `Avalonia.Win32`, `Avalonia.Skia`, the HarfBuzz text shaping package and `Avalonia.Themes.Fluent`. They are pinned centrally in `Directory.Packages.props`. The app configures the Win32 backend and Skia explicitly instead of `UsePlatformDetect`. That keeps the X11 and macOS backends out of the MSI.
- **Build telemetry:** Avalonia's build telemetry (`Avalonia.BuildServices`) is turned off for local builds and CI, because the project sends nothing it doesn't need to. The spike confirms the opt-out.
- **Package pins:**
  - `CommunityToolkit.Mvvm` and the hosting packages stay.
  - `H.NotifyIcon.Wpf` goes from `Directory.Packages.props`.
- **The platform folders** are `extract-ui-seams`' D4.

*Rejected:*
- **`Avalonia.Desktop`:** it ships three backends where one is used.

### D7: UI thread and application lifetime

- **`IUiDispatcher`** (from `extract-ui-seams`) gets `AvaloniaUiDispatcher` over `Avalonia.Threading.Dispatcher.UIThread`, and the WPF implementation goes. No caller changes.
  - `InvokeAsync` maps to `Dispatcher.UIThread.InvokeAsync`, not `Post`. `Post` sends an exception to the dispatcher's `UnhandledException`, and `ShutdownCoordinator` would end the app. The contract keeps it in the returned task (`extract-ui-seams` D1).
  - It gets `WpfUiDispatcher`'s three tests, run through `HeadlessUnitTestSession.Dispatch` (the T1 fallback): the action runs on the UI thread, after the current operation, and an exception faults the task without reaching `UnhandledException`.
- **`Program.Main`:**
  - The single-instance guard and the bootstrap logger stay where they are.
  - `App.Run` becomes `AppBuilder.Configure<App>()…StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown)`.
  - `[STAThread]` stays, for COM.
- **`App.OnFrameworkInitializationCompleted`** does what `OnStartup` does today: it builds the host, creates `ShutdownCoordinator`, shows the tray, loads the settings and starts the host.
- **`ShutdownCoordinator`:**
  - Its `shutdownApplication` delegate becomes the lifetime's `Shutdown(exitCode)`.
  - It subscribes to `Dispatcher.UIThread.UnhandledException` instead of WPF's `DispatcherUnhandledException`.
  - `ExitProcess` keeps `TerminateProcess`, now through CsWin32 as the conventions require.
- **`DispatcherWait`** keeps its method and its reasoning (a synchronous continuation ends the frame) over Avalonia's `DispatcherFrame` and `Dispatcher.PushFrame`.
- **Session end:**
  - `IsOSShutdown` can't be used: it is `internal` in 12.1.1 (spike, Context).
  - On Windows, every `ShutdownRequested` is the end of the session. **Exit** calls `ShutdownCoordinator` directly, there is no main window, and `ShutdownMode.OnExplicitShutdown` is set, so nothing else raises it.
  - The handler calls `DispatcherWait.Until(RequestShutdownAsync(ShutdownReason.SessionEnd))`, as `OnSessionEnding` does.
  - It never cancels, because that would veto the end of the session.
  - Avalonia raises `ShutdownRequested` for `WM_QUERYENDSESSION`, including Restart Manager's `ENDSESSION_CLOSEAPP`, with no window shown (spike W2). Its hidden `AvaloniaMessageWindow` is the top-level window that Windows and Restart Manager find, as WPF's hidden window is today, and the answer waits until the handler returns.
  - On macOS a `ShutdownRequested` can also be a plain quit. `add-macos-shell` D5 tells the two apart through the quit Apple event.

### D8: The windows

- **Recording overlay:**
  - An Avalonia window with `ShowActivated=false`, `Topmost`, `ShowInTaskbar=false`, `WindowDecorations.None` and a transparent background.
  - It gets the same four extended styles through `TryGetPlatformHandle`, **right after every `Show`**, and the same placement code on the target HWND (W1).
    - Avalonia resets the extended styles on each `Show`, so styles set once before the first `Show` are lost (spike W1).
    - `ShowActivated=false` already keeps the focus in the target window during the moment between `Show` and the styles.
    - `WS_EX_LAYERED` stays: without it, the overlay takes the clicks.
  - The dot, the elapsed time and the spinner are ported with their timings.
- **Settings window and model setup window:**
  - The XAML is ported control by control, and the view models keep their bindings.
  - The license links become `HyperlinkButton`s that open the URL through the app's existing "open URL" delegate.
  - Opening or focusing a window keeps today's behavior: restore it if minimized, then `Activate()`.
- **Confirmations:**
  - A small modal `ConfirmDialog` (message, **Yes**, **No**, No as the default) replaces `MessageBox`. Avalonia's dialogs are asynchronous, so the close confirmation cancels `Closing` first and closes the window again after **Yes**.
  - When the app exits, the windows close without asking, as `settings-window` requires. A flag set by the shutdown skips the confirmation.

*Rejected:*
- **A third-party message box package:** the three confirmations don't justify another dependency and its notices.

**Port notes.** A scan of the three windows' XAML on 2026-09-22, with the planned replacement for each construct Avalonia lacks:

| WPF construct | Count | Avalonia |
|---|---|---|
| `DataTrigger`s that switch the settings sections by `Navigation.SelectedIndex` (a `ListBox` with Dictation, Model, Engine, Text insertion, General) | 5 | Keep the `ListBox`. Each section binds `IsVisible` to `#Navigation.SelectedIndex` through a small equality converter |
| Other triggers: `IsInstalled`, `Download.IsDownloading`, empty text | about 6 | Style classes (`Classes.installed="{Binding IsInstalled}"`), or `IsVisible` with the built-in `StringConverters` and `ObjectConverters` |
| `Visibility` with `BooleanToVisibilityConverter` | 13, 8 | `IsVisible` takes a `bool`, so the converter goes |
| `GroupBox` | 1 | A header `TextBlock` above a `Border`, or a `HeaderedContentControl` with a small template |
| `TextBlock` with `Run`s | 29 | `TextBlock.Inlines`, with `Run`, `Span`, `Bold` and `LineBreak`. It ports directly |
| Inline `Hyperlink` (the license links) | 2 | A `HyperlinkButton` in an `InlineUIContainer`, or a sentence reflowed so the link stands on its own. The wording may change slightly |
| `SystemColors.GrayTextBrushKey`, `ActiveBorderBrushKey` | 2 | The Fluent theme's secondary text and border brushes |
| The spinner's `DoubleAnimation` on a `RotateTransform`, started in code-behind | 1 | A keyframe `Animation` in a style (`IterationCount="INFINITE"`), switched on by a class |
| `ElementName` bindings | 6 | `#Name` syntax |
| `IsDefault`, `IsCancel`, `_` access keys, `AutomationProperties.Name` | 8 or more | The same in Avalonia |

- **Compiled bindings:** every view declares `x:DataType`, so a binding mistake fails the build instead of failing quietly at runtime.
- **To confirm while implementing:**
  - compiled bindings work with the `internal sealed` view models, because Avalonia compiles the XAML into the same assembly
  - Avalonia's generated `x:Name` fields stay `internal`, the default, so `GenerateDocumentationFile` raises no missing-doc errors
- *Rejected:* a `TabControl` with `TabStripPlacement="Left"` for the sections. It needs no converter, but it changes how the settings window looks and navigates.

### D9: Tray status and icons (moved)

`TrayStatus` moved to `extract-ui-seams` (its D2). The monochrome glyph set, the taskbar-mode detection and the icon tool on SkiaSharp moved to `add-monochrome-tray-icons`. This change loads those icons in Avalonia's tray (D4).

### D10: The clipboard (moved)

Moved to `use-win32-clipboard`: `Win32ClipboardService` on the Win32 API, behind `IClipboardService`.

### D11: Tests

- **Windows and the tray:**
  - Tests of windows and the tray run on Avalonia's headless platform: `SettingsDialogTests`, `RecordingOverlayWindowTests`, `DispatcherWaitTests` and `TrayIconServiceTests`. They drop their STA threads.
  - They use T1's fallback, because `Avalonia.Headless.XUnit` 12.1.1 fails discovery on xunit.v3 4.0.1 (spike, Context):
    - The test project references `Avalonia.Headless`, and `[assembly: AvaloniaTestApplication]` names a test app builder with `UseHeadless`.
    - A shared helper holds `HeadlessUnitTestSession.GetOrStartForAssembly`.
    - The tests are plain `[Fact]`s whose body runs through `Session.Dispatch(…, TestContext.Current.CancellationToken)`.
  - When a later `Avalonia.Headless.XUnit` supports xunit.v3 4.x, `[AvaloniaFact]` can replace the helper. That's a test-only change.
- **The overlay's native styles and placement** can't be seen headless. They stay `Hardware` tests on a real desktop.
- **Desktop tests** already use the Win32 `TestWindow` from `use-win32-clipboard`, so they don't change.
- **Conventions:** the `[Trait]` categories and the naming stay the same.

### D12: Payload, notices and the guard

- **Out:** the WPF and Windows Forms runtime, and with them `ThirdPartyNotices-Wpf.txt` and `ThirdPartyNotices-WinForms.txt` from `packaging/third-party/`. `H.NotifyIcon`'s section also goes from `THIRD-PARTY-NOTICES.md`.
- **In:**
  - Avalonia (MIT) and MicroCom.Runtime (MIT)
  - SkiaSharp (MIT), with Skia and the libraries `libSkiaSharp.dll` bundles, taken from SkiaSharp's own notices
  - HarfBuzzSharp (MIT) and HarfBuzz (Old MIT)
  - ANGLE (BSD-3-Clause) for `av_libglesv2.dll`
  - The list comes from the publish output, not from memory.
- **`assert-native-dependencies.ps1`** learns the new native DLLs and their imports.

### D13: Release and docs

- **Release:** the change ships as a Windows minor release of its own. A pre-release is the rehearsal, as for earlier packaging changes.
- **`docs/roadmap.md`** gets the Windows steps and the macOS steps: the four preparing changes, this change, `add-macos-shell`, and the later macOS changes that its design lists.
- **`CLAUDE.md`** replaces WPF in *Layout*, *UI thread*, *View models* and *Tests*.

## Risks / Trade-offs

- [The macOS overlay activates the app, so the paste goes to the wrong app] → M2 checks it first, and the `NSPanel` fallback exists. If both fail, this change stops before the Windows shell is touched (D3).
- [Avalonia doesn't raise `ShutdownRequested` for `WM_QUERYENDSESSION`, or raises it too late] → W2 found it raised in time from another process. A real sign-out is in the regression pass, with a hidden top-level window of the app's own as the fallback. The 4.5 s watchdog stays as the last line.
- [Avalonia resets the overlay's extended styles, so it takes the focus or the clicks] → It happened on every `Show` (W1). The overlay sets them again after each `Show` (D8), and the regression pass checks the focus and the click-through.
- [The tray menu's workaround depends on `TrayPopupRoot`, a type internal to `Avalonia.Win32`, and an Avalonia update renames it or stops opening the menu as a `Window`] → The unit test from D4 fails on a rename. The menu then keeps its last state, which is wrong but not a crash. The fallback in D4 is updating the menu when the state changes.
- [The Fluent look changes the windows' appearance] → Accepted (Non-Goals). A screenshot pass is part of the regression check.
- [A bigger or smaller MSI, with more native DLLs to guard] → D12 measures it, and the guard covers the new DLLs.
- [Avalonia's headless test package doesn't run on xunit.v3 4.x] → It happened (T1). The project's own session helper is in use (D11).
- [An unexplained startup abort, seen once in the spike] → The app's unhandled-exception logging names the exception if it happens again. It's watched in the regression pass and in `add-macos-shell`.
- [Porting the XAML changes a binding or a validation display without anyone noticing] → The view models don't change, and their tests stay. The settings window tests move to headless and assert the same things.

## Migration Plan

1. **Spike** (D3), on branch `spike/avalonia-shell`, never merged:
   - Done on 2026-09-22: M1–M6 on the development Mac, and T1.
   - Done on 2026-09-23: W1–W3 on Windows. The results are in Context.
   - Next: write the spec delta and `tasks.md`.
2. **The four preparing changes** are done. They can run in parallel with the spike.
3. **The Avalonia shell:**
   - the lifetime and `AvaloniaUiDispatcher`
   - the tray with the new icons
   - the windows and the confirmations
4. **Remove WPF and `H.NotifyIcon.Wpf`.**
5. **Packaging, notices, guard, docs.** CI builds and validates the MSI.
6. **Regression pass against the specs** on Windows 11 and Windows 10 22H2:
   - dictation end to end, the overlay's focus and click-through, and insertion into an elevated window
   - the overlay's placement at 100 % and 150 %, which the spike checked at 150 % only
   - the clipboard restore, and a real sign-out and restart, which the spike didn't run
   - every tray status and every notification, and the menu's items after each change of state
   - an upgrade from the previous MSI while the app runs, and an uninstall
7. **Release:** a pre-release, then the minor release.

**Rollback:** revert the pull request. The previous MSI stays installable. Going back to it from the release of this change means uninstalling first (`add-msi-installer` D2).

## Open Questions

- The Fluent theme's accent and density: Avalonia's defaults, or matched to Windows' accent color. That can be decided during the port without changing a spec.
