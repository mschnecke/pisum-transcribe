## Why

Nothing tells a user that a new version of Pisum Transcribe exists. Since `add-msi-installer`, installing an update takes a double-click: the MSI closes the running app, replaces it and starts the new version. The missing step comes before that: learning that a new version exists. This change adds that notice. It replaces the update half of the superseded Velopack plan `add-installer-and-updates`, without downloading or installing anything.

## What Changes

- **Update check:** a few minutes after start, then once a day, the app asks GitHub's API for the repository's latest release (`releases/latest`), so drafts and pre-releases never count. It compares that version with its own.
  - The first check waits a random 1 to 10 minutes. It doesn't compete with loading the model at sign-in, and many copies behind one IP address don't all ask at the same moment.
  - Development builds check like any other build. A build of the current `main` reports the version of the latest release, so it gets no notice. To try the notice, override the version: `--property:Version=1.0.0`.
- **Notice:** when the latest release is newer, the tray menu shows **Pisum Transcribe `<version>` is available…**, which opens that release's page in the browser. One notification announces the version, once per version while the app runs. Nothing is stored between starts.
- **Failures are silent:** being offline, a GitHub error, the rate limit, a timeout or a tag that can't be read is logged as a warning and tried again at the next daily check. The user never sees an error.
- **Setting:** "Check for updates automatically" in the settings window's General section is on by default. Turning it off stops the checks and hides a notice that is showing. Turning it on starts a check within a minute. Both take effect without a restart.
- **Privacy:** the check sends no audio, text, settings, logs or identifiers. The request carries only what every HTTPS request carries: the IP address, and a user agent that names the app without its version. The README's "The app goes online only to download a speech model" and its *Data and privacy* section change to name the check.
- **Tray menu API:** `ITrayIconService.AddMenuItem` gets an overload whose header is read each time the menu opens, so an item's text can include a value that is only known later.
- Not included:
  - downloading or installing the update. Updating in one click waits for code signing.
  - a "Check for updates now" menu item, release notes in the app, and pre-release channels.
  - remembering across restarts which version was already announced.
  - WinGet, which comes later as its own change.

## Capabilities

### New Capabilities
- `app-updates`: the daily check for a newer stable release, how versions are compared, the tray notice and notification, silent failures, and what the request may carry.

### Modified Capabilities
- `settings-window`:
  - New requirement: the "Check for updates automatically" option in the General section, on by default and stored in the settings file.
  - "Apply changes without restart" adds how turning the check on or off takes effect.

## Impact

- **New code:** a feature folder `src/Pisum.Transcribe/Updates/` with `services.AddUpdates()`, a hosted service that runs the check, and the version comparison. A named `HttpClient` for the GitHub API. Tests in `tests/Pisum.Transcribe.Tests/Updates/`, including one explicit test against the real GitHub API that the default test run and CI skip, as they skip `ModelStoreDownloadTests`.
- **Changed code:**
  - `Settings/`: a new `UpdateSettings` section in `AppSettings`, with the null replacement in `JsonSettingsStore`.
  - `SettingsWindow/`: the checkbox in the General section, and the save and rebase of the new section in `SettingsViewModel`.
  - `Tray/`: the `AddMenuItem` overload with a header function.
  - `Hosting/AppHost.cs`: registers the feature.
- **No new packages.** `Microsoft.Extensions.Http` and `System.Text.Json` are already referenced.
- **Documentation:**
  - `README.md`: the online statement, *Data and privacy*, the tray menu table and the General settings.
  - `CLAUDE.md`: the `Updates/` folder in the layout, and a third way for a setting to take effect: read by its own service before each use.
  - `CLAUDE.md` and the doc comment of `Traits.Categories.Hardware`: `Hardware` tests also cover tests that need internet access.
  - `docs/roadmap.md`: step 14 is proposed.
- **Network:** one HTTPS request to `api.github.com` per check, unauthenticated. GitHub allows 60 such requests per hour per IP address.
- **User-visible:** a new tray item and a notification when a newer version exists, and a new option in the settings window.
