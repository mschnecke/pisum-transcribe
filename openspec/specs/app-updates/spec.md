# app-updates Specification

## Purpose

Tells users when a newer stable version of Pisum Transcribe has been released, with a tray notice that leads to the release page, without downloading or installing anything and without sending any user data.

## Requirements

### Requirement: Daily update check
While "Check for updates automatically" is on, the application SHALL ask GitHub for the latest release of the repository `mschnecke/pisum-transcript` once between 1 and 10 minutes after it starts, and then every 24 hours while it runs. The time of the first check SHALL be chosen at random within that range at each start. Drafts and releases marked as pre-releases SHALL NOT count as the latest release. While the option is off, the application SHALL NOT send the request.

#### Scenario: First check after start
- **WHEN** the application starts with "Check for updates automatically" on
- **THEN** it asks GitHub for the latest release between 1 and 10 minutes later
- **AND** not before 1 minute has passed

#### Scenario: Daily check
- **WHEN** the application has been running for 24 hours since its previous check
- **THEN** it asks GitHub for the latest release again

#### Scenario: Check turned off
- **WHEN** "Check for updates automatically" is off
- **THEN** the application sends no request to GitHub while it runs

#### Scenario: Pre-release is not offered
- **WHEN** the newest release on the Releases page is `v1.3.0-rc.1`, marked as a pre-release, and the latest release is `v1.2.0`
- **THEN** the check compares against `1.2.0`

### Requirement: Version comparison
A release SHALL count as newer only if its tag has the form `v<major>.<minor>.<patch>` without a pre-release suffix, and either its version is higher than the application's own version, or the two versions are equal and the application's own version has a pre-release suffix. The build metadata of the application's own version, the part after `+`, SHALL be ignored. A tag of any other form SHALL NOT count as newer.

#### Scenario: Newer release
- **WHEN** the application runs version `1.1.1+b917e0e` and the latest release is `v1.2.0`
- **THEN** the release counts as newer

#### Scenario: Same version
- **WHEN** the application runs version `1.1.1` and the latest release is `v1.1.1`
- **THEN** the release does not count as newer

#### Scenario: Older release
- **WHEN** the application runs version `1.3.0` and the latest release is `v1.2.0`
- **THEN** the release does not count as newer

#### Scenario: Final release after its release candidate
- **WHEN** the application runs version `1.2.0-rc.1` and the latest release is `v1.2.0`
- **THEN** the release counts as newer

#### Scenario: Tag with a pre-release suffix
- **WHEN** the latest release has the tag `v1.2.0-rc.1`
- **THEN** the release does not count as newer

### Requirement: Update notice
When a check finds a newer release, the tray menu SHALL show an item **Pisum Transcribe `<version>` is available…**, where `<version>` is the release's version without the leading `v`. Choosing the item SHALL open that release's page, `https://github.com/mschnecke/pisum-transcript/releases/tag/v<version>`, in the default browser. The application SHALL show one notification that names the version, once per version for as long as the application runs. The item SHALL stay until the application ends, until a later successful check finds no newer release, or until "Check for updates automatically" is turned off. When a later check finds a release that is newer still, the item SHALL show that version and a notification SHALL announce it. The application SHALL NOT download or install the release.

#### Scenario: Newer release found
- **WHEN** the application runs version `1.1.1` and a check finds `v1.2.0`
- **THEN** the tray menu shows **Pisum Transcribe 1.2.0 is available…**
- **AND** a notification announces version 1.2.0

#### Scenario: Open the release page
- **WHEN** the user chooses **Pisum Transcribe 1.2.0 is available…**
- **THEN** the default browser opens `https://github.com/mschnecke/pisum-transcript/releases/tag/v1.2.0`

#### Scenario: Same release on the next day
- **WHEN** a check found `v1.2.0` and announced it, and the next day's check finds `v1.2.0` again
- **THEN** the tray item still shows 1.2.0
- **AND** no second notification is shown

#### Scenario: A newer release on a later day
- **WHEN** a check found `v1.2.0`, and a later check finds `v1.3.0`
- **THEN** the tray menu shows **Pisum Transcribe 1.3.0 is available…**
- **AND** a notification announces version 1.3.0

#### Scenario: Announced again after a restart
- **WHEN** version 1.2.0 was announced, the application restarts still running version 1.1.1, and its first check finds `v1.2.0`
- **THEN** a notification announces version 1.2.0 again

#### Scenario: No newer release
- **WHEN** a check finds no newer release
- **THEN** the tray menu shows no update item
- **AND** no notification is shown

### Requirement: Silent failure
A check that fails SHALL NOT show an error to the user: no notification, dialog or change of the tray icon. Failures SHALL include no network connection, a response that does not arrive within 30 seconds, an HTTP error status including GitHub's rate limit, and a response whose tag cannot be read. The application SHALL log a warning for a failed check and SHALL try again at the next daily check. An update item that is already shown SHALL stay. Dictation SHALL NOT be affected by a check, whether it succeeds or fails.

#### Scenario: Offline
- **WHEN** a check runs while the computer has no network connection
- **THEN** no notification or dialog is shown
- **AND** a warning is written to the log
- **AND** the next check runs 24 hours later

#### Scenario: Rate limit
- **WHEN** GitHub answers a check with HTTP status 403 or 429
- **THEN** no notification or dialog is shown
- **AND** the log entry names the status code

#### Scenario: Earlier notice survives a failed check
- **WHEN** a check found `v1.2.0`, and the next day's check fails
- **THEN** the tray menu still shows **Pisum Transcribe 1.2.0 is available…**

### Requirement: Privacy of the update check
The check SHALL send an HTTPS GET request to `api.github.com`, and SHALL follow the redirects that GitHub answers with, such as after the repository is renamed. It SHALL NOT follow a redirect from HTTPS to HTTP. No request of the check SHALL contain audio, transcript text, settings, logs, credentials, cookies, or any identifier of the user, the computer or the installation. Its user agent SHALL name the application and SHALL NOT contain its version. Log entries about the check SHALL contain only versions, HTTP status codes and durations.

#### Scenario: Request content
- **WHEN** a check sends its request
- **THEN** the request is a GET to `https://api.github.com/repos/mschnecke/pisum-transcript/releases/latest` without a body
- **AND** it carries no cookie, no authorization header and no identifier of the user, the computer or the installation
- **AND** its user agent does not contain the application's version

#### Scenario: Repository renamed
- **WHEN** GitHub answers the check with `301 Moved Permanently` and a new address of the repository's latest release
- **THEN** the check sends the same request to that address, with no more content than the first request
- **AND** compares the version of the release found there
