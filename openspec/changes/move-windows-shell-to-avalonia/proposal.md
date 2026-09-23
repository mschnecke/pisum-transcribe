## Why

Pisum Transcribe is to ship on macOS as a public release next to Windows. The engine side is already portable: TranscribeCppSharp, ONNX Runtime and SharpHook all ship `osx-arm64` binaries, and the Metal backend is in the native package. The shell isn't: WPF and H.NotifyIcon.Wpf run only on Windows. Avalonia UI becomes the shell on both platforms, so the windows, the tray and the app lifetime are written once.

The Windows app moves first, as a release of its own, before any macOS code: its existing specs are the regression checklist for the new shell, and Avalonia's quirks surface on one platform instead of two. Four smaller changes prepare the move on today's WPF shell, and this change is the swap itself:
- `extract-ui-seams`
- `use-win32-clipboard`
- `add-monochrome-tray-icons`
- `show-windows-notifications`

## What Changes

- **UI framework:** WPF becomes Avalonia 12.1.
  - The settings window, the model setup window and the recording overlay become Avalonia windows, with the same layout and behavior. Their look follows Avalonia's Fluent theme.
  - The app's startup, its UI thread and its shutdown run on Avalonia's application lifetime and dispatcher. `IUiDispatcher` from `extract-ui-seams` gets its Avalonia implementation. Exit, session end, single instance and the 5 s exit budget behave as today.
  - The three confirmation message boxes become one small Avalonia dialog.
- **Tray:** H.NotifyIcon.Wpf becomes Avalonia's tray icon.
  - The menu items, the tooltips and the icons from `add-monochrome-tray-icons` stay the same.
  - A left click on the icon opens the settings window, instead of a double-click. A double-click still ends with the settings window open, because a click on an open window brings it to the front.
  - Notifications are already toasts from `show-windows-notifications`, so removing H.NotifyIcon loses nothing.
- **Tests:** tests of windows and the tray move from STA threads with WPF to Avalonia's headless test platform where it can run them.
- **Packaging:**
  - The MSI's payload changes: the WPF and Windows Forms runtime go out, and Avalonia, SkiaSharp, HarfBuzz and ANGLE come in.
  - `THIRD-PARTY-NOTICES.md` and the native dependency guard follow the new payload.
- **Precondition:** a throwaway spike checks the parts that could sink the approach, on macOS first, before this change's implementation starts (design D3). It is done, and both halves are go: the macOS half on 2026-09-22 and the Windows half on 2026-09-23. The four preparing changes didn't wait for it.
- **Release:** the change ships as a Windows minor release of its own.
- Not included:
  - the seams, the clipboard, the tray icons and the toasts, which are the four preparing changes
  - any macOS code, the macOS build and its release, which are later changes
  - renaming the Vulkan backend setting, which belongs to the macOS Metal change
  - new features, new settings, or a redesign of the windows

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `settings-window`: "Opening the settings window" says a left click on the tray icon opens the window, instead of a double-click.

The notices requirement of `packaging` keeps its wording, while the notices file covers the new payload. The spike's W3 settled the click: `Clicked` reports a left click, on the release (design D3, D4).

## Impact

- **Depends on:**
  - `extract-ui-seams`, `use-win32-clipboard`, `add-monochrome-tray-icons` and `show-windows-notifications`, which must be done first
  - the spike (D3), which is done
- **Code:**
  - `Program.cs`, `App.xaml(.cs)` and `GlobalUsing.cs`
  - `Hosting/`: `DispatcherWait`, `ShutdownCoordinator`, and the Avalonia implementation of `IUiDispatcher`
  - `Tray/TrayIconService`, over Avalonia's `TrayIcon`
  - `Dictation/RecordingOverlayWindow`
  - `SettingsWindow/SettingsDialog` and `SettingsWindowService`
  - `SpeechModels/ModelSetupWindow` and `ModelSetupHostedService`
  - `Pisum.Transcribe.csproj` and `NativeMethods.txt`
- **Tests:** `RecordingOverlayWindowTests`, `DispatcherWaitTests`, `SettingsDialogTests` and `TrayIconServiceTests`.
- **Dependencies:**
  - added: Avalonia 12.1 (core, the Win32 backend, Skia, HarfBuzz, the Fluent theme), and `Avalonia.Headless` in the tests. `Avalonia.Headless.XUnit` 12.1.1 doesn't run on xunit.v3 4.x (spike T1)
  - removed: `H.NotifyIcon.Wpf`, and WPF through `UseWPF`
- **Packaging:** `packaging/windows/build-msi.ps1`, `assert-native-dependencies.ps1`, `THIRD-PARTY-NOTICES.md` and `packaging/third-party/`.
- **Docs:**
  - `CLAUDE.md`: layout, UI thread, tests, and the WPF mentions
  - `README.md`
  - `docs/roadmap.md`: the Windows and macOS steps
- **User-visible:**
  - the windows get Avalonia's Fluent look
  - a single click opens the settings
  - the MSI's size changes
