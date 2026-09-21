## Why

`docs/idea.md` describes Pisum Transcribe, a Windows push-to-talk dictation app. The repository has no code yet, only an empty `Pisum.Transcribe.slnx`. Every later change (model download, transcription, recording, text insertion, dictation) needs the same base: a process that lives in the system tray, runs as a single instance, writes logs, and saves user settings. This change builds that base and the test project the other changes will use.

## What Changes

- Add the WPF application project `Pisum.Transcribe` (product name "Pisum Transcribe") targeting `net10.0-windows` / `win-x64`, and register it in `Pisum.Transcribe.slnx`.
- Add the test project `Pisum.Transcribe.Tests` using xunit v3 on Microsoft.Testing.Platform, with Shouldly and FakeItEasy, and register it in the solution.
- Add shared build settings (`Directory.Build.props`) and central package management (`Directory.Packages.props`).
- Start the app as a tray-only process: no main window, a tray icon with a tooltip and an **Exit** menu item.
- Allow only one running instance per Windows user session.
- End the app within 5 seconds of choosing **Exit**, even if background work does not stop in time.
- Log unhandled errors. Errors on the UI thread or in a background service show a tray notification and end the app cleanly.
- Write rolling log files to the user's local application data folder. Logs contain no usage telemetry, and nothing leaves the machine.
- Add a JSON settings store under the user's local application data folder. It survives missing or corrupt files, and later changes extend it with their own settings.
- Update `CLAUDE.md` with the new project layout and commands.

## Capabilities

### New Capabilities
- `app-shell`: Tray-resident application lifecycle. Covers startup without a main window, the tray icon and Exit, the single-instance guard, the local data folder, local log files and the handling of unhandled errors.
- `settings-storage`: Saving and loading user settings in a per-user JSON file. Covers defaults, recovery from corrupt files, and safe writes.

### Modified Capabilities
<!-- None: no specs exist yet. -->

## Impact

- New code: `src/Pisum.Transcribe/`, `tests/Pisum.Transcribe.Tests/`, `Directory.Build.props`, `Directory.Packages.props`.
- Solution: `Pisum.Transcribe.slnx` gains both projects.
- New dependencies: `H.NotifyIcon.Wpf`, `Microsoft.Extensions.Hosting`, `Serilog.Extensions.Hosting`, `Serilog.Sinks.File`; for tests, `xunit.v3`, `Shouldly`, `FakeItEasy`.
- User machine: creates `%LOCALAPPDATA%\Pisum Transcribe\` (holding `settings.json` and `logs\`).
- Docs: `CLAUDE.md` gets the project layout, and its SDK note is corrected (`global.json` pins `10.0.400`).
- Every later change in this plan builds on this one: `add-model-management`, `add-transcription-engine`, `add-push-to-talk-recording`, `add-text-insertion`, `add-dictation-workflow`, `add-settings-window` and `add-voice-activity-detection`.
