## 1. Tray: a menu item with a changing header

- [ ] 1.1 Add `AddMenuItem(Func<string> header, Action onClick, Func<bool>? isVisible = null)` to `ITrayIconService` and `TrayIconService`. The string overload calls it with `() => header`. `UpdateMenuItemVisibility` sets `Header` for each visible item when the menu opens (design D6). Make that method `internal` so a test can call it. Verify: all current callers compile unchanged, and a new STA test in `TrayIconServiceTests` passes:
  - `AddMenuItem_HeaderFunction_ShowsCurrentTextWhenMenuOpens`: the header function returns "A", then "B", and after each refresh the item's `Header` is that text.

## 2. Settings: the `Updates` section

- [ ] 2.1 Add `UpdateSettings(bool CheckAutomatically = true)` and `AppSettings.Updates`, following the section rules in `AppSettings`. Add the null replacement to `JsonSettingsStore.Load` (design D4). Verify that these new tests in `JsonSettingsStoreTests` pass:
  - `Load_UpdatesSectionMissing_ChecksAutomatically`
  - `Load_UpdatesSectionNull_ChecksAutomatically`
  - a save and load round trip writes `"updates": { "checkAutomatically": false }` and reads it back.
- [ ] 2.2 In the settings window, add `CheckForUpdates` to `GeneralSectionViewModel`, taken from the baseline. Write it in `SettingsViewModel.BuildSettings`, rebase it in `ApplyBaseline`, and add the checkbox "Check for _updates automatically" with its hint under **Start with Windows** in `SettingsDialog.xaml` (design D4). Verify that these new tests in `SettingsViewModelTests` pass:
  - `Constructor_DefaultSettings_ChecksForUpdates`
  - `Save_CheckForUpdatesTurnedOff_SavesUpdatesSectionOff`
  - `SettingsChangedElsewhere_CheckForUpdatesNotEdited_ShowsNewValue`
  - `SettingsDialogTests` still passes with the new checkbox.

## 3. Update check: versions

- [ ] 3.1 Add `Updates/ReleaseVersion` with a parser for the app's own informational version and a parser for a release tag, plus the "is newer" rule (design D3, spec "Version comparison"). Verify that the new `ReleaseVersionTests` pass, one case per spec scenario, plus:
  - `ParseOwn_WithBuildMetadata_IgnoresIt`
  - `ParseOwn_Invalid_Fails`
  - `ParseTag_WithoutV_Fails`
  - `ParseTag_WithPreReleaseSuffix_Fails`
  - `ParseTag_WithFourParts_Fails`

## 4. Update check: the service

- [ ] 4.1 Add `Updates/UpdateCheckService : BackgroundService` and `UpdatesServiceCollectionExtensions.AddUpdates()`. `AddUpdates()` registers the named `HttpClient` with a 30-second timeout and `TimeProvider.System` with `TryAddSingleton`. Call it last in `AppHost.Create` (design D1, D2). Implement:
  - the random first delay, then the 24-hour loop.
  - the request with its three headers, reading only `tag_name`.
  - catching every failure except shutdown cancellation, and logging only versions, status codes and durations.

  Verify that these new tests in `UpdateCheckServiceTests` pass, using `FakeTimeProvider` and `FakeHttpMessageHandler`:
  - `Start_BeforeFirstDelay_SendsNoRequest`, and `Start_AfterFirstDelay_SendsOneRequest`
  - `Check_After24Hours_SendsNextRequest`
  - `Check_Request_IsGetToLatestReleaseWithUserAgentWithoutVersion`: checks the URL, the absence of a body, cookie and authorization header, and that the user agent doesn't contain the version.
  - `Check_OptionOff_SendsNoRequest`
  - `Check_HttpError_LogsWarningWithStatusCodeWithoutNotification`, for 403, 404 and 429
  - `Check_NetworkFailureOrTimeout_LogsWarningWithoutNotification`
  - `Check_TagUnreadable_LogsWarningWithoutNotification`
  - `Check_Failure_KeepsServiceRunningAndChecksNextDay`
  - `Stop_DuringDelayOrRequest_StopsAtOnce`
  - `AddUpdates_NamedClient_FollowsRedirects`: resolve the named client's primary handler through `HttpClientFactoryOptions`, and check that `AllowAutoRedirect` is on (design D2, spec scenario "Repository renamed"). The handler follows redirects itself, so a test with `FakeHttpMessageHandler` can't exercise the redirect. This test only guards against a configuration change that turns it off.
- [ ] 4.2 Add the notice: the menu item added in `StartAsync` through the header-function overload, the notification once per version per process, clearing the notice when no release is newer, and opening the release page through the URL built from the parsed version (design D5). Verify that these new tests pass:
  - `Check_NewerRelease_ShowsItemWithVersionAndOneNotification`
  - `Check_SameReleaseNextDay_NoSecondNotification`
  - `Check_EvenNewerRelease_UpdatesItemAndNotifiesAgain`
  - `Check_NoNewerRelease_HidesItem`
  - `Check_FailureAfterNotice_KeepsItem`
  - `MenuItem_Chosen_OpensReleasePageBuiltFromVersion`: the response's `html_url` points elsewhere, and the opened URL is `https://github.com/mschnecke/pisum-transcript/releases/tag/v1.2.0`.
- [ ] 4.3 Subscribe to `ISettingsStore.Changed`. When the option turns on, wake the loop so it checks at once. When it turns off, clear the notice (design D4). Verify that these new tests pass:
  - `SettingsChanged_TurnedOn_ChecksWithinOneMinute`
  - `SettingsChanged_TurnedOff_HidesItemAndStopsChecks`
  - `SettingsChanged_OtherSetting_DoesNotCheck`
- [ ] 4.4 Add the explicit test `Check_RealGitHubApi_FindsNewerStableRelease` in `Updates/UpdateCheckServiceGitHubTests`, following `ModelStoreDownloadTests`: `[Trait(Traits.Category, Traits.Categories.Hardware)]`, `[Fact(Explicit = true)]`, and a class doc that says it asks the real GitHub API and needs internet access. Build the named client through `AddUpdates()` on a new `ServiceCollection`, so the real headers, timeout and redirect setting are used. Run `CheckOnceAsync` with the running version `0.0.1` (design D1). Assert that no warning is logged, that exactly one notification is shown, and that the item's header matches `^Pisum Transcribe \d+\.\d+\.\d+ is available…$`, without pinning a version. Verify: the default test run skips it, and `dotnet test Pisum.Transcribe.slnx --filter-class "*.UpdateCheckServiceGitHubTests" --explicit on` passes.

## 5. Documentation

- [ ] 5.1 Update `README.md` as design D7 describes: the online statement, *Data and privacy*, the tray menu table and the General settings. Update `CLAUDE.md`: `Updates/` in the layout, the third way for a setting to take effect, and `Hardware` tests that need "a microphone, a GPU, a downloaded model or internet access". Say the same in the doc comment of `Traits.Categories.Hardware`. Mark step 14 as proposed in `docs/roadmap.md`. Verify: the README no longer says the app goes online *only* to download a model, and every document names the check the same way.

## 6. Verification

- [ ] 6.1 Run `dotnet build Pisum.Transcribe.slnx`, `dotnet test Pisum.Transcribe.slnx` and `openspec validate add-update-check --strict`. Verify: the build has no warnings, all tests pass, and validation reports no issues.
- [ ] 6.2 Check by hand with `dotnet run --project src/Pisum.Transcribe --property:Version=1.0.0`. Within 10 minutes, verify that:
  - a notification announces the latest release,
  - the tray menu shows **Pisum Transcribe `<latest>` is available…** above Exit,
  - choosing the item opens the release page,
  - the log has one entry with both versions and nothing else.

  Then turn the option off and save: the item disappears. Turn it on and save: within 1 minute, the log shows a new check, and no second notification appears. Run once more without `--property:Version` and verify that no notice appears.
