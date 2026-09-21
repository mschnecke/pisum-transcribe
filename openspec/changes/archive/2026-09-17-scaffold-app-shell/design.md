## Context

The repository holds only an empty `Pisum.Transcribe.slnx`, `global.json` (SDK `10.0.400`, test runner `Microsoft.Testing.Platform`), `nuget.config` (nuget.org only) and `docs/idea.md`. `docs/idea.md` fixes the architecture: a WPF tray app with H.NotifyIcon, and background services for hotkey, audio, transcription and text insertion.

This is the first of eight planned changes. This design also sets the layout and conventions that the later changes rely on:

1. `scaffold-app-shell`
2. `add-model-management`
3. `add-transcription-engine`
4. `add-push-to-talk-recording`
5. `add-text-insertion`
6. `add-dictation-workflow`
7. `add-settings-window`
8. `add-voice-activity-detection`

Packaging and distribution (Velopack, signing, WinGet/Chocolatey, CI) are deferred to a later change.

## Goals / Non-Goals

**Goals:**
- A buildable and testable solution with one app project and one test project.
- A hosting model (dependency injection, logging, background services) that later changes extend by adding services, without restructuring.
- A settings store that later changes extend by adding their own settings section.

**Non-Goals:**
- No settings UI (see `add-settings-window`).
- No installer, auto-update, code signing or CI pipeline.
- No splitting into several class libraries. The user asked for one project named `Pisum.Transcribe`, and feature folders give enough separation.

## Decisions

### D1: Project layout

```
Directory.Build.props            Nullable, ImplicitUsings, LangVersion, TreatWarningsAsErrors, GenerateDocumentationFile
Directory.Packages.props         ManagePackageVersionsCentrally=true
src/Pisum.Transcribe/
  Pisum.Transcribe.csproj        WinExe, net10.0-windows, UseWPF, RuntimeIdentifier=win-x64,
                                 InternalsVisibleTo Pisum.Transcribe.Tests,
                                 StartupObject=Pisum.Transcribe.Program
  Program.cs                     [STAThread] Main: single-instance guard, bootstrap logger, App.Run
  App.xaml(.cs)                  ShutdownMode=OnExplicitShutdown, no StartupUri
  GlobalUsing.cs
  Hosting/                       AppHost bootstrap, AppPaths, SingleInstanceGuard, ShutdownCoordinator,
                                 service registration extensions
  Tray/                          TrayIconService (H.NotifyIcon TaskbarIcon + context menu)
  Settings/                      AppSettings, ISettingsStore, JsonSettingsStore
  (later) SpeechModels/, Transcription/, Recording/, TextInsertion/, Dictation/, VoiceActivity/
tests/Pisum.Transcribe.Tests/
  Pisum.Transcribe.Tests.csproj  Exe, net10.0-windows, xunit.v3 (MTP v2), Shouldly, FakeItEasy
  Traits.cs                      Category = Unit | Integration | Hardware
  <folders mirror src>
```

Each feature folder is its namespace (`Pisum.Transcribe.<Feature>`). Registration is one extension method per feature, such as `services.AddSettings()`, so that later changes add a single line in the host builder. The code follows the company .NET conventions: sealed classes by default, `_camelCase` fields, `Async` suffixes, no file headers, XML docs on public members, and test methods named `Method_Scenario_Expected`.

*Alternative considered:* separate `Pisum.Transcribe.Core` and `Pisum.Transcribe.App` projects. This was rejected for now. With a single app and a single developer, the extra project boundary adds friction without a consumer.

### D2: Generic Host inside WPF

**Entry point.** `Program.Main` is the entry point (`<StartupObject>`), because the `Main` that WPF generates from `App.xaml` runs too late for the single-instance guard. `App.xaml` is built as a `Page` instead of an `ApplicationDefinition`, so WPF generates no second `Main`, and `App` can take `AppPaths` through its constructor. `Main` runs on the STA UI thread and does, in order:

1. Acquire the single-instance guard (D4).
2. Create the Serilog bootstrap logger (D5), so failures before the host exists are logged.
3. Create `App`, call `InitializeComponent()` and `Run()`.
4. In `finally`: release the guard and call `Log.CloseAndFlush()`.

**Host.** `App.OnStartup` builds a `Microsoft.Extensions.Hosting` host with `Host.CreateEmptyApplicationBuilder`, registers services, and starts it. The empty builder avoids defaults that do not fit a windowless app: `appsettings.json` and environment-variable configuration read from an arbitrary working directory, and the console and Windows Event Log logging providers. Background features are `IHostedService`s. The UI thread stays the WPF dispatcher, and services that touch the UI marshal to it through `Application.Current.Dispatcher`. `HostOptions`:
- `ShutdownTimeout` = 4 s, which leaves room inside the 5 s exit budget.
- `BackgroundServiceExceptionBehavior` = `StopHost` (the default, set explicitly), so a failed background service ends the app through the shutdown path below.

**Shutdown path.** The WPF application and the host each have a lifetime. `ShutdownCoordinator` is the only code that ends the app, so both lifetimes end together. `RequestShutdown(reason)` runs once, and later calls are ignored.

```
  Tray "Exit"  ------------------------------------> RequestShutdown(UserExit)   exit code 0
  Dispatcher.UnhandledException (Handled = true) --> RequestShutdown(Error)      exit code 1
  host ApplicationStopping, not requested by us  --> RequestShutdown(Error)      exit code 1
     (e.g. a BackgroundService threw -> StopHost)

  RequestShutdown, on the UI thread:
    1. start a 4.5 s watchdog on a thread-pool timer -> Log.CloseAndFlush(),
                                                        TerminateProcess(self, exit code)
    2. UserExit: remove the tray icon now, so the user sees the exit at once
       Error:    show the error notification and keep the icon until step 4
    3. await host.StopAsync()   (ShutdownTimeout 4 s; awaited, so the UI thread
                                 stays free for services that marshal to it)
    4. Error: wait until the notification has been shown for 3 s, then remove the icon
       dispose the host
    5. Application.Current.Shutdown(exit code) -> Main's finally (see Entry point)
```

The coordinator never blocks the UI thread while waiting for the host. A blocking wait would deadlock any service that marshals to the dispatcher while it stops, and the exit would never finish. The watchdog makes the 5 s limit a guarantee: a service that does not stop in time is not waited for, and the process ends. The host does not enforce `ShutdownTimeout` against a service that ignores its cancellation token, so the watchdog is the only hard limit. `add-transcription-engine` already bounds its own stop to 3 s, which fits inside `ShutdownTimeout`.

*Measured during implementation:* the watchdog fires at 4.5 s, not 5 s, and ends the process with `TerminateProcess`, not `Environment.Exit`. `Environment.Exit` runs process-exit handlers that took about 330 ms, so a 5 s watchdog ended the process after about 5.35 s. With 4.5 s and `TerminateProcess`, a stuck shutdown ends after about 4.52 s, also when a service blocks the UI thread. The log is flushed before, and nothing else relies on process-exit handlers. Removing the tray icon dismisses its notification: with an immediate removal the error notification was never visible, so on errors the icon stays at least 3 s after the notification.

*Why:* this gives dependency injection, logging and ordered start and stop at no cost, and it matches the company's other .NET code. *Alternative:* manual composition in `App.xaml.cs`, which does not scale to the eight features.

### D3: Tray icon with H.NotifyIcon.Wpf 2.4.1

`TrayIconService` creates a `TaskbarIcon` in code, not XAML, because the app has no window to host it. It sets the tooltip "Pisum Transcribe", sets an embedded `.ico`, and builds a `ContextMenu` with **Exit**. Exit raises `ExitRequested`, which `ShutdownCoordinator` handles with `RequestShutdown(UserExit)` (D2). An event instead of a direct call avoids a circular dependency, because the coordinator uses `TrayIconService` to show the error notification and remove the icon.

The icon is shown with `ForceCreate(enablesEfficiencyMode: false)`. The default `true` puts the whole process into Windows Efficiency mode: EcoQoS execution-speed throttling and `IDLE_PRIORITY_CLASS`. That would starve the push-to-talk keyboard hook, which Windows silently removes when it responds too slowly, as well as audio capture and transcription.

The service exposes a small API that later changes use to add menu items and change the icon and tooltip: `AddMenuItem`, `SetStatus(icon, tooltip)` and `ShowNotification(title, message)`.

### D4: Single instance through a named mutex

`SingleInstanceGuard` opens the named mutex `Local\Pisum.Transcribe.SingleInstance` in `Main`, before the logger and WPF start, and waits up to 6 s to own it (`WaitOne`). The 6 s are just over the 5 s exit budget (D2).

- **Acquired:** the app starts. The guard owns the mutex until `Main` ends and releases it on the same thread, because mutex ownership belongs to the thread that acquired it.
- **`AbandonedMutexException`:** counts as acquired. It is raised when the owner ended without releasing the mutex while this launch was waiting, for example after a crash or a watchdog exit (D2). Without a waiter, Windows destroys the mutex with the last handle, and the next launch simply creates it.
- **Timeout:** another instance is running. The process exits with code 0 without creating a tray icon or writing to the log.

*Why wait instead of exiting at once:* exiting can take up to 5 s. A launch right after **Exit** then waits for the old process and starts, instead of ending silently. A real duplicate launch ends after the wait, which the user does not notice, because it never shows anything.

`Local\` limits the guard to the logon session. That matches global hotkeys and simulated input, which only work within the session. The mutex name is a constructor parameter, so tests use a unique name per test and do not collide with a running app or with tests running in parallel. Bringing the first instance forward is not needed, because the app has no window.

### D5: Serilog file logging

`Serilog.Extensions.Hosting` writes to `Serilog.Sinks.File` at `%LOCALAPPDATA%\Pisum Transcribe\logs\pisum-transcribe-.log` with `rollingInterval: Day` and `retainedFileCountLimit: 7`. Minimum level is Information and Debug in DEBUG builds.

**Bootstrap logger.** `Main` creates `Log.Logger` with `CreateBootstrapLogger()` right after the single-instance guard, so errors before the host exists are logged. `services.AddSerilog(...)` replaces it once the host is built. The logger is created after the guard, because a running instance holds today's log file open, and a duplicate launch must not write to it. `Main` calls `Log.CloseAndFlush()` in `finally`.

**Unhandled errors.** The app exits visibly on errors it can still act on:

| Source | Handling |
|---|---|
| `Dispatcher.UnhandledException` | Log as error, set `Handled = true`, `RequestShutdown(Error)` (D2) |
| `BackgroundService` throws | The host logs it and stops (`StopHost`). `ApplicationStopping` without a requested shutdown → `RequestShutdown(Error)` |
| `AppDomain.UnhandledException` | Always fatal: log as fatal and `Log.CloseAndFlush()` before the process dies. No notification. |
| `TaskScheduler.UnobservedTaskException` | Log as error and keep running. The task has already ended, so no running work is affected. |

The error notification says that Pisum Transcribe stopped because of an error and that details are in the log. The exit code is 1.

Privacy rule for all later changes: **never log transcript text or audio data**, only lengths, durations and status codes.

### D6: Settings store

`AppSettings` is a sealed record with one nested record per feature section. This change adds only `SchemaVersion = 1`. Later changes add their own section (`Model`, `Transcription`, `Recording`, `TextInsertion`, `General`, `VoiceActivity`) along with the defaults for it.

**Shape rules**, so that partial and older files load correctly:
- **Sections:** `AppSettings` holds each section as an `init` property with a `new()` default, such as `public ModelSettings Model { get; init; } = new();`. `AppSettings` does not take sections as constructor parameters. A section parameter cannot have a non-null default, so an older file without that section would load it as `null`.
- **Section records:** these may use constructor parameters with default values, such as `ModelSettings(string SelectedModelId = "canary-1b-v2-q8_0")`. System.Text.Json uses a parameter's default value when the property is missing from the file.
- **Non-constant defaults:** a setting whose default is not a compile-time constant, such as a list, is an `init` property with an initializer, not a constructor parameter.
- **`null` in the file:** a file whose whole content is `null` loads as the defaults. After loading, a section that the file sets to `null` is replaced by its default. This change has no sections yet, so each change that adds a section also adds the `null` replacement and its test.
- **Documentation:** these rules are part of the XML doc on `AppSettings`, so later changes find them where they add their section.

`ISettingsStore` exposes `AppSettings Current`, `Task SaveAsync(AppSettings, CancellationToken)` and a `Load()` that runs at startup.

`JsonSettingsStore` uses System.Text.Json with camelCase property names, unknown properties ignored, and enums written as camelCase strings (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`, e.g. `clipboardPaste`, `cpu`). Later changes use these rules for their enum values.

- **Corrupt file:** a `JsonException` renames the file to `settings.json.corrupt`, overwriting any older one, logs a warning and falls back to defaults.
- **Safe writes:** the store writes to `settings.json.tmp`, flushes to disk, then calls `File.Move(tmp, settings.json, overwrite: true)`, which uses an atomic replace on NTFS.

`AppPaths` centralizes `%LOCALAPPDATA%\Pisum Transcribe\` and its `logs\` and `models\` subfolders. Tests pass a temporary root through the constructor.

*Why `%LOCALAPPDATA%` and not `%APPDATA%`:* the settings reference machine-specific files (downloaded models) and hardware choices (backend). They should not roam.

### D7: Test project on xunit v3

- **Packages:** `xunit.v3` 4.0.1 uses Microsoft.Testing.Platform v2 by default, which matches `global.json`. Assertions use `Shouldly` 4.3.0 and fakes use `FakeItEasy` 9.0.1.
- **Project file:** the test project is `OutputType=Exe`, as xunit v3 requires, and targets `net10.0-windows` so it can reference the WPF app project.
- **Categories:** tests carry `[Trait("Category", "Unit" | "Integration" | "Hardware")]`.
- **Hardware tests:** tests that need a microphone, a GPU or a downloaded model are marked `[Fact(Explicit = true)]`. The default `dotnet test` run stays hermetic.

## Risks / Trade-offs

- [H.NotifyIcon.Wpf targets `net10.0-windows7.0` and also .NET Framework, and it is maintained by one person.] → It is isolated behind `TrayIconService`, so replacing it with the WinForms `NotifyIcon` touches one class.
- [A test project that references a `WinExe` project can hit SDK warnings about executable references.] → Set `<ValidateExecutableReferencesMatchSelfContained>false</ValidateExecutableReferencesMatchSelfContained>` if needed, and verify with `dotnet test`.
- [`TreatWarningsAsErrors` combined with `GenerateDocumentationFile` makes missing XML docs build errors.] → CS1591 fires only for publicly visible members. Feature types are `internal sealed` unless they must be public, which keeps the documentation burden small.
- [The error notification may disappear when the tray icon is removed, before the user can read it.] → Confirmed during implementation: an immediate removal dismissed the notification before it was visible. On an error exit the icon now stays at least 3 s after the notification (D2), which was verified as readable. Error exits therefore take about 3 s.
- [When Windows logs off or shuts down, WPF ends the app itself and `ShutdownCoordinator` does not run, so hosted services are not stopped.] → Accepted: settings writes are atomic (D6), log writes are not buffered, and `Main`'s `finally` still flushes the log. Later changes that write files must survive a crash or power loss anyway.
- [The watchdog's `TerminateProcess` skips the rest of a service's stop and all process-exit handlers, so that service gets no clean shutdown.] → Accepted: the 5 s exit guarantee has priority. The log is flushed before, and settings writes are atomic (D6), so a forced exit cannot corrupt `settings.json`. A later change must not rely on `AppDomain.ProcessExit` for cleanup.
- [The same user in two concurrent sessions (e.g. multi-session RDP) runs two instances that share `%LOCALAPPDATA%\Pisum Transcribe\`. The log file is opened exclusively, so the second instance's file logging fails.] → Accepted: rare on client Windows, where a user has one interactive session at a time.
