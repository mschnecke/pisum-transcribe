## Context

See proposal.md, Why. The requirements are in `specs/app-updates/spec.md` and the `settings-window` delta. This design builds on `add-msi-installer` (archived 2026-09-22). It keeps the parts of the superseded Velopack plan (`22e256f`, `openspec/changes/add-installer-and-updates/design.md`, D3, D4 and D9) that don't depend on Velopack.

Current state that the approach depends on:
- **The version.** `Program.cs` logs `AssemblyInformationalVersionAttribute`, and the SDK appends the commit: `1.1.1+b917e0ee…`, `0.1.0-rc.2+4ac1cd3…`. `build-msi.ps1` passes `-p:Version=<tag without v>`. A build that isn't a release reports the version recorded in `Directory.Build.props` (packaging spec). `release.yml` commits that version to `main` before it pushes the tag, so `main` always records the latest release.
- **Releases.** Tags have the form `v<major>.<minor>.<patch>`, optionally with `-rc.N`. `release.yml` marks a tag with a suffix as a pre-release. The published releases are `v1.1.1` (latest) and `v0.1.0-rc.1` (pre-release).
- **The tray menu.** `ITrayIconService.AddMenuItem(string header, Action onClick, Func<bool>? isVisible)` fixes the header when the item is added. `isVisible` runs on the UI thread each time the menu opens (`TrayIconService.UpdateMenuItemVisibility`). Items appear above Exit's separator in the order they are added. Today that's **Download model…**, **Cancel transcription**, **Settings…**. `ShowNotification(title, message)` shows a balloon, and a click on it does nothing.
- **Settings.** Sections follow the rules in `AppSettings`, including the null replacement in `JsonSettingsStore.Load`. `SettingsViewModel.BuildSettings` writes each section from `_baseline with { … }`, and `ApplyBaseline` rebases unedited fields when settings are saved elsewhere. The General section holds only "Start with Windows", which lives in the registry and not in the settings file. `ISettingsStore.Changed` is raised after a save on the thread that saved.
- **Hosting.** `BackgroundServiceExceptionBehavior.StopHost`: an exception that escapes `ExecuteAsync` ends the app. A service must stop within the 4 s `ShutdownTimeout`. Features register `TimeProvider.System` with `TryAddSingleton`.
- **HTTP.** `ModelStore` uses a named client from `IHttpClientFactory`, and its tests use `FakeHttpMessageHandler`.

## Goals / Non-Goals

**Goals:**
- The check can't harm the app. No failure reaches the user, ends the host or delays dictation.
- Nothing from a network response reaches the shell except digits that were parsed from it.
- Every part is testable without a network, with `FakeTimeProvider` and `FakeHttpMessageHandler`.

**Non-Goals:**
- Handling a click on the notification. `ITrayIconService` has no click event, and the tray item is the way to the release page.
- Authenticated requests, conditional requests (`ETag`) and caching.
- A shared semantic-versioning type for the rest of the app.

## Decisions

### D1: One background service in a new `Updates/` feature

`Updates/UpdateCheckService : BackgroundService`, registered by `services.AddUpdates()`. `AppHost.Create` calls it last, after `AddSettingsWindow()`, so the item sits directly above Exit, below **Settings…**.

```
ExecuteAsync
  wait first delay (random 1..10 min) ----------+
                                                 |  woken early when the option
  loop:                                          |  turns on (D4)
    option on?  --no--> skip                     |
       | yes                                     |
       v                                         |
    check (D2) --> compare (D3) --> notice (D5)  |
       | any failure: log warning, keep notice   |
       v                                         |
    wait 24 h --------------------------------- -+
```

- **Dependencies:** `IHttpClientFactory`, `ISettingsStore`, `ITrayIconService`, `TimeProvider` and `ILogger<UpdateCheckService>`.
- **Test seams,** as optional constructor parameters in the style of `ModelStore`'s `getAvailableFreeSpace`: the first delay, the running version (default: the informational version of the entry assembly), a URL opener (default: `Process.Start` with `UseShellExecute = true`) and a UI-thread invoker (default: `Application.Current.Dispatcher.InvokeAsync`, as in `SettingsViewModel`).
- **One check is one method.** The loop calls an internal `CheckOnceAsync(CancellationToken)`, which requests, compares and updates the notice (D2, D3, D5). Unit tests and the explicit test against the real API (task 4.4) call it directly, so they don't have to go through the timer.
- **No exception escapes a check.** Each check catches everything except an `OperationCanceledException` from the stopping token, because an escaping exception stops the host. Shutdown cancels the delay and the request, so the service stops at once.
- **The first delay** is `TimeSpan.FromMinutes(1 + Random.Shared.NextDouble() * 9)`.

*Rejected:* putting the item at the top of the menu. **Download model…** and **Cancel transcription** are usually hidden, so the menu normally has two or three items, and the notification is what gets the user's attention. The top would need a position parameter or a second list in `TrayIconService`, which would make this the one feature item not added the usual way. Worth revisiting if the menu grows.

*Rejected:* an `IReleaseSource` interface over the HTTP call. `FakeHttpMessageHandler` already makes the call testable, as it does for `ModelStore`. *Rejected:* Octokit, a package for one GET request.

### D2: The request

`GET https://api.github.com/repos/mschnecke/pisum-transcript/releases/latest` with the headers `User-Agent: Pisum-Transcribe`, `Accept: application/vnd.github+json` and `X-GitHub-Api-Version: 2022-11-28`. It's sent through a named client, `UpdateCheckService.HttpClientName`, with a 30-second timeout.

- GitHub rejects requests that have no user agent. The value names the app without its version (spec: Privacy).
- `releases/latest` never returns a draft or a pre-release, so no filtering is needed in the app.
- Only `tag_name` is read from the response, with `System.Text.Json`. A missing or non-string `tag_name` is a failure.
- A non-success status is a failure. The warning names the status code. `404` means that the repository has no stable release, and `403` or `429` means the rate limit was reached. No status gets special handling.
- The log never contains the response body.
- **Redirects are followed,** with the handler's defaults. After a rename or a transfer of the repository, GitHub answers the old address with `301` and the repository's new address, and every installed copy has the old address compiled in. Following keeps those copies getting notices. The redirected request carries the same headers, and there are no cookies or credentials to carry. .NET doesn't follow a redirect from HTTPS to HTTP. The release page link (D5) has the same property, because GitHub also redirects the old web address.

*Rejected:* not following redirects, so that a `3xx` is a failure. After a rename, every installed copy would stop getting notices, and no one would see an error. *Rejected:* following redirects only within `api.github.com`. That's custom redirect handling against a threat that isn't realistic, GitHub itself redirecting somewhere hostile, and the requests carry nothing to protect.

*Rejected:* reading the redirect of `https://github.com/mschnecke/pisum-transcript/releases/latest`. It's not rate-limited, but it's page behavior rather than a documented API. It stays the fallback if the rate limit becomes a problem (Risks).

### D3: Comparing versions

An internal `ReleaseVersion(int Major, int Minor, int Patch, bool IsPreRelease)` with two parsers:
- **Own version:** drop everything from `+`, split off a `-` suffix, and parse the core as exactly three non-negative integers. `IsPreRelease` is true when there was a suffix. If this fails, the service logs one warning and makes no checks. That can only happen with a hand-made `-p:Version`.
- **Tag:** `^v(\d+)\.(\d+)\.(\d+)$`, with invariant culture. Anything else, including a pre-release suffix, is a failure (spec: Version comparison).

The latest release is newer when its core is greater, or when the cores are equal and the own version is a pre-release. The latest release never has a suffix, so two pre-release suffixes are never compared.

*Rejected:* NuGet.Versioning or a SemVer package, for a comparison with only one shape. *Rejected:* `System.Version` alone, which can't parse a suffix.

### D4: The setting, and how a change takes effect

- **Section:** `Updates` with `UpdateSettings(bool CheckAutomatically = true)`, stored as `"updates": { "checkAutomatically": true }`. It follows the section rules in `AppSettings`, including the null replacement in `JsonSettingsStore.Load` and its test.
- **UI:** a checkbox "Check for _updates automatically" under **Start with Windows** in the General section. Its hint: "Asks GitHub once a day whether a new version of Pisum Transcribe exists. Nothing else is sent." `GeneralSectionViewModel` gets the value from the baseline. `BuildSettings` writes `Updates`, and `ApplyBaseline` rebases it like the other sections.
- **No `SettingsApplier` case.** The service reads `Current.Updates.CheckAutomatically` before each check, and subscribes to `ISettingsStore.Changed` itself:
  - **off to on:** wakes the loop, which checks at once. That satisfies "within 1 minute". The wake-up is a signal that the loop awaits together with its delay, so the `Changed` handler never blocks the saving thread.
  - **on to off:** clears the notice (D5). The next loop turn skips the check.
- **CLAUDE.md's settings rule** gets a third way next to "read at the start of each dictation" and "a case in `SettingsApplier`": read by its own service before each use.

*Rejected:* a `SettingsApplier` case. It would couple the update feature to the settings window for one transition that only this service cares about.

### D5: The notice

- **State.** The newest version a successful check found to be newer (`_available`, or none), and the versions already announced in this process (`_announced`). The state is kept in memory only. `_available` is written by the loop and read on the UI thread, so it's one immutable reference that is replaced as a whole (`volatile`).
- **After a successful check:** a newer version replaces `_available`. If `_announced` doesn't contain that version, it's added and `ShowNotification` runs on the UI thread. If no release is newer, `_available` is cleared. A failed check changes nothing.
- **Menu item.** It's added once in `StartAsync`, on the UI thread, with the D6 overload:
  - header: `() => $"Pisum Transcribe {_available} is available…"`
  - visible: `() => _available is not null`. Turning the option off clears `_available` (D4), so the item disappears.
  - click: opens the release page.
- **Notification.** The title is "Pisum Transcribe 1.2.0 is available", and the message is "Choose it in the tray menu to open the release page."
- **The URL is built from the parsed numbers** as `https://github.com/mschnecke/pisum-transcript/releases/tag/v{Major}.{Minor}.{Patch}`. The response's `html_url` is never opened, so only digits that passed D3's parser reach `ShellExecute`. A failed launch, such as one with no browser registered, is logged as a warning.

### D6: A tray menu item with a changing header

`ITrayIconService` gets the overload `AddMenuItem(Func<string> header, Action onClick, Func<bool>? isVisible = null)`. The existing string overload calls it with `() => header`, so current callers don't change. `TrayIconService` stores the function next to `isVisible`, and `UpdateMenuItemVisibility` sets `Header` for every visible item when the menu opens.

*Rejected:* adding the item only when an update is found. Its text still couldn't change when a newer version appears. *Rejected:* returning a handle with a settable header. That's more API than one caller needs.

### D7: Documentation

- **README:**
  - "Audio and text never leave the machine. The app goes online only to download a speech model." becomes, in substance: "…The app goes online to download a speech model and, unless you turn it off in the settings, once a day to ask GitHub whether a new version exists."
  - *Data and privacy* gets a paragraph: the check's request carries only the IP address and a user agent without a version, and sends no audio, text, settings, logs or identifiers.
  - The tray menu table gets the item, "Shown only when a newer version is released".
  - The General section entry names the option.
- **CLAUDE.md:** `Updates/` in the layout, and the third way for a setting to take effect (D4).
- **`docs/roadmap.md`:** step 14 is proposed. It is marked done when the change is archived.

## Risks / Trade-offs

- **[Many copies behind one IP address, such as a company network, share GitHub's 60 requests per hour. The same users could fail every day, silently.]** → The random first delay spreads the starts over 9 minutes. Failures are logged. If it becomes a problem, switch to the redirect of `/releases/latest` (D2) or to conditional requests, since a `304` doesn't count against the limit.
- **[On a computer that sleeps, a check can come later than 24 hours after the previous one.]** → Accepted. The notice isn't urgent, and every start checks.
- **[A new notification at every start while the user doesn't update.]** → At most one per start, and the tray item is the quiet reminder. Storing announced versions was considered and left out (proposal).
- **[Development builds share `settings.json` with the installed app.]** → Turning the check off in a `dotnet run` build turns it off for the installed app too. Accepted, as for every other setting. A development build of the current `main` gets no notice, because it reports the latest release's version.
- **[GitHub picks the "latest" release by the date of its commit, or by an explicit `make_latest`.]** → A hotfix for an older line could become "latest". D3 then finds nothing newer, and no wrong notice appears. Releases here are linear.
- **[A release whose tag has another form never counts.]** → `release.yml` produces only `v<major>.<minor>.<patch>[-suffix]` (packaging spec). A tag it can't read is logged as a warning.

## Migration Plan

Nothing to migrate. A settings file without `updates` loads the check as on. Removing the feature later leaves an `updates` property, which the store ignores (`UnmappedMemberHandling.Skip`).
