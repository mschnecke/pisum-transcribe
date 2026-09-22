## Why

Pisum Transcribe is to ship on macOS as a public release next to Windows. `move-windows-shell-to-avalonia` puts the app on a shell that runs on both platforms, but nothing starts on a Mac yet. This change is the first macOS step: the app starts on a Mac, lives in the menu bar, keeps its data where macOS expects it, and ends cleanly at **Quit** and at logout. Every later macOS step builds on it: setup and permissions, recording, text insertion, the Metal backend, dictation, the login item and packaging.

## What Changes

- **A macOS build of the app** for Apple silicon, macOS 14 or later. The project gains a second target framework next to the Windows one. Windows-only code stays out of it through the `Windows/` folders from `extract-ui-seams`.
- **Menu bar app:**
  - The app runs as an agent: no Dock icon, no main window, no entry in the app switcher.
  - Its icon sits in the menu bar as a monochrome template image, which follows light and dark mode.
  - The menu has the same items as on Windows. The last item is **Quit Pisum Transcribe**, the macOS name for **Exit**.
- **Ending:**
  - **Quit**, and the end of the macOS session (log out, shut down, restart), end the app as **Exit** does on Windows: background work stops, the icon goes, and the process ends within 5 seconds.
  - The app never holds up a logout.
- **One instance per user:** a second launch ends without a second menu bar icon, whether it comes from Finder, from the terminal or at login.
- **Data and logs:**
  - Settings and speech models live in `~/Library/Application Support/Pisum Transcribe/`.
  - Logs go to `~/Library/Logs/Pisum Transcribe/`, where Console.app shows them.
  - Nothing roams or leaves the machine, as on Windows.
- **Notifications:**
  - Every notification the specs name appears as a macOS notification from "Pisum Transcribe".
  - macOS asks the user once for permission. The setup window of a later change will ask up front instead.
- **Swift helper:** a small native library, `libPisumMac.dylib`, built from Swift sources in the repository with the Command Line Tools. It carries the calls into Apple's Objective-C APIs, starting with notifications. C APIs are called from C# directly.
- **Developer build:**
  - Building on a Mac also produces a `Pisum Transcribe.app` for development, so the app runs with its own identity and its own permissions.
  - It is signed with a local signing identity when one is configured, and ad hoc otherwise.
- **Continuous integration:** every pull request and push to `main` is also built and tested on macOS. Packaging and releases for macOS are not part of this change.
- Not included:
  - the permissions and the combined setup window (the next change, `add-macos-setup`)
  - recording, the hotkey, text insertion, the Metal backend and dictation, which are the later macOS changes
  - starting at login
  - the `.pkg` installer and its release
  - Intel Macs and macOS 13 or earlier

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `app-shell`:
  - "Tray-resident startup" covers the macOS menu bar and the missing Dock icon.
  - "Exit from tray" names **Quit Pisum Transcribe** on macOS.
  - "Single instance per user session" is per user on macOS, across launches from Finder, the terminal and login.
  - "Local data folder" and "Local rolling log files" name the macOS folders.
  - "Unhandled errors end the application visibly" names the macOS notification.
  - A new requirement, "End with the macOS session", covers log out, shut down and restart, next to "End with the Windows session".
- `packaging`: "Continuous integration checks every change" builds and tests on macOS as well as on Windows.
- `settings-storage`: "Settings file location" names `~/Library/Application Support/Pisum Transcribe/settings.json` on macOS.
- `model-management`: "Installed model detection" names the macOS models folder.
- `settings-window`: "Opening the settings window" is the **Settings…** menu item only on macOS. A click on the menu bar icon opens the menu, as macOS convention has it.

The spec deltas are written after the spike of `move-windows-shell-to-avalonia` (its design D3). M1 and M4 decide how the menu bar icon and the end of the session work.

## Impact

- **Depends on:** `move-windows-shell-to-avalonia`, which must be done first, and its spike's M1 (agent app, menu bar) and M4 (Quit, logout).
- **Code:**
  - `Pisum.Transcribe.csproj` gets the second target framework, the macOS packages (`Avalonia.Native`, the macOS runtime packages of transcribe.cpp, ONNX Runtime and SharpHook), and the targets for the Swift helper and the dev `.app`.
  - `Hosting/`: `AppPaths` (logs), `SingleInstanceGuard` (per-user scope on macOS), and macOS code for ending the process and for the end of the session.
  - `Tray/`: the menu bar template icon and the **Quit** label.
  - A macOS implementation of `INotifier`.
- **New files:**
  - `src/Pisum.Transcribe.MacNative/`: the Swift sources of `libPisumMac.dylib`
  - `Info.plist` and the entitlements template for the dev `.app`
  - the menu bar template PNGs and the `.icns` app icon, rendered by `tools/generate-tray-icon.cs`
- **Tests:**
  - The test project targets the host's framework only.
  - New macOS `Integration` tests call the Swift helper and skip on Windows.
- **CI:** `ci.yml` gets a `macos-latest` job that builds and tests.
- **Docs:**
  - `CLAUDE.md`: layout, commands, and developing on a Mac, including the signing identity
  - `README.md`: that macOS is coming, but no macOS install instructions yet
- **Build requirements on a Mac:** the .NET SDK from `global.json` and the Xcode Command Line Tools (`swiftc`, `codesign`). Full Xcode is not needed.
