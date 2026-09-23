## MODIFIED Requirements

### Requirement: Continuous integration checks every change
Every pull request to `main` and every push to `main` SHALL be built and tested on Windows and on macOS. The run SHALL fail if the build on either platform produces a warning or error, or if any test in the default test run fails on either platform. The run SHALL NOT need a microphone, a GPU, a downloaded speech model or the user's desktop, and the hardware tests, which need one of them or use the real clipboard, foreground window or keyboard input, SHALL NOT run.

#### Scenario: Pull request with a failing test
- **WHEN** a pull request to `main` contains a change that makes a test fail
- **THEN** the check for that pull request fails and shows the failing test

#### Scenario: Change that fails only on macOS
- **WHEN** a pull request to `main` contains a change that builds and passes its tests on Windows, but fails to build or fails a test on macOS
- **THEN** the check for that pull request fails and shows the macOS failure

#### Scenario: Direct push to main
- **WHEN** a commit is pushed directly to `main`
- **THEN** a build and test run on Windows and on macOS starts for that commit

#### Scenario: Hardware tests don't run
- **WHEN** the continuous integration run completes
- **THEN** none of the hardware tests was run on either platform
