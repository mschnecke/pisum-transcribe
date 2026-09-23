## 1. Check the registration on Windows 10

- [ ] 1.1 Repeat the D3 spike on Windows 10 22H2 (Windows 11 passed). A throwaway exe writes only `HKCU\Software\Classes\AppUserModelId\Pisum.Transcribe`, shows the error toast and exits right after `Show`. Verify:
  - The toast shows with the name "Pisum Transcribe" and the icon.
  - Settings, System, Notifications lists the app.
  - A second process reads one toast from `ToastNotificationManager.History`.

  If it fails, stop and switch to D2's fallback, the AUMID on the Start Menu shortcut, and update the `packaging` delta before going on.

## 2. The target framework

- [ ] 2.1 Change the target framework to `net10.0-windows10.0.19041.0` in `Pisum.Transcribe.csproj` and `Pisum.Transcribe.Tests.csproj`. Verify: `dotnet build Pisum.Transcribe.slnx` passes with warnings as errors, and `dotnet test Pisum.Transcribe.slnx` passes.
- [ ] 2.2 Exclude `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` from ReadyToRun with `<PublishReadyToRunExclude>` items, with a comment that points to design D5. Copy `Tray/TrayIcon.png` to the output (`CopyToOutputDirectory`). Verify: after `./packaging/windows/build-msi.ps1 -Version 0.1.0-dev.1`, the published folder holds `TrayIcon.png`, and `Microsoft.Windows.SDK.NET.dll` is about 24 MB, not about 53 MB.

## 3. The toast notifier

- [ ] 3.1 Add `Notifications/Windows/ToastNotifier : INotifier` (design D1). It builds a `ToastGeneric` toast with the title and the message as two XML-escaped text elements, and shows it with `ToastNotificationManager.CreateToastNotifier("Pisum.Transcribe")`. It never reads `Setting`, and it logs any exception from creating, building or showing as a Warning, with no title or message text, and doesn't throw. Add a `Notifications/Windows` entry to both `.csproj.DotSettings` files. Verify, with the toast building and showing kept behind a small seam, in `ToastNotifierTests`:
  - `BuildToastXml_TitleAndMessage_HasToastGenericWithBothTexts`
  - `BuildToastXml_TextWithXmlCharacters_EscapesThem`
  - `Show_NotifierThrows_LogsWarningAndDoesNotThrow`
- [ ] 3.2 Add the AUMID registration (design D2), a hosted service or startup step that writes `DisplayName` = `Pisum Transcribe` and `IconUri` = the full path of `TrayIcon.png` next to the exe under `Software\Classes\AppUserModelId\Pisum.Transcribe`, through `IUserRegistry`. A failed write is logged as a Warning and doesn't stop the start. Verify in `ToastRegistrationTests`, against a fake `IUserRegistry`:
  - `Start_Always_WritesDisplayNameAndIconUri`
  - `Start_RegistryThrows_LogsWarningAndContinues`
- [ ] 3.3 Add `services.AddNotifications()` and call it from `AppHost.Create` (design D4). It registers `ToastNotifier` as the `INotifier` and the registration from task 3.2. Remove the `INotifier` registration from `AddTray()`, delete `TrayBalloonNotifier`, and delete `TrayIconService.ShowNotification`. Verify: `dotnet build Pisum.Transcribe.slnx` passes, and a test resolves `INotifier` from the built host as `ToastNotifier`.
- [ ] 3.4 Add `ToastNotifierHardwareTests` (`[Fact(Explicit = true)]`), which registers the AUMID, shows a toast and finds it in `ToastNotificationManager.History`, then removes it from the history. Verify: it passes with `--filter-trait "Category=Hardware" --explicit on`.

## 4. The error exit

- [ ] 4.1 Remove `ErrorNotificationDuration` and the wait from `ShutdownCoordinator` (design D4). On an error it shows the notification, stops the host, removes the tray icon and disposes the host. Update the XML doc that names the balloon. Verify: `ShutdownCoordinatorTests` pass without the wait and still check the order, the notification before stopping the host.

## 5. Packaging

- [ ] 5.1 Add an uninstall-only removal of `HKCU\Software\Classes\AppUserModelId\Pisum.Transcribe` to `Pisum.Transcribe.wxs`: `reg.exe delete ... /f` through `WixQuietExec64`, with `Return="ignore"` and the condition `REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE`, as for the startup entry. Verify: `build-msi.ps1` builds and validates the MSI, and the check in task 6.1 passes.
- [ ] 5.2 Add the Windows SDK projection (`Microsoft.Windows.SDK.NET`) and C#/WinRT (`WinRT.Runtime`) to `THIRD-PARTY-NOTICES.md`, with the license texts their packages carry. Verify: both files in the published folder have an entry, as the `packaging` notices requirement asks.

## 6. Check by hand and documentation

- [ ] 6.1 Check by hand, installed from the MSI and started with `dotnet run`:
  - Every notification the specs name shows as a toast from "Pisum Transcribe" with its icon.
  - The error toast stays in the notification center after the process ended.
  - With notifications turned off for the app, nothing shows and the app keeps running.
  - An upgrade keeps the registry key and the user's notification setting, and an uninstall removes the key.
- [ ] 6.2 Update the documentation. Verify by reading it:
  - `README.md`: the minimum is Windows 10 version 2004 or Windows 11, x64.
  - `CLAUDE.md`: the target framework, the `Notifications/` folder with `ToastNotifier` and `AddNotifications()`, and the `Tray/` entry without `TrayBalloonNotifier`.
