## Why

When Windows signs out, shuts down or restarts while Pisum Transcribe runs, the application ends without the shutdown that **Exit** runs (issue #13). WPF answers Windows' session-end query by shutting the application down by itself. `ShutdownCoordinator` is bypassed, the host never stops, and the log has no `Shutting down, reason …` entry. A running dictation is not ended the way "Dictation ends when the application exits" requires. With **Start with Windows** on, the end of the session is the most common way the application ends. A reproduction on `main` at `a446820` confirmed it: the process ended 868 ms after `WM_QUERYENDSESSION`, and nothing was logged after the model became ready.

## What Changes

- When the Windows session ends, the application runs the same shutdown as **Exit**, with a new shutdown reason for the end of the session.
  - All background work stops and the tray icon is removed.
  - A running dictation ends as it does at **Exit**.
  - The process ends within 5 seconds, with exit code 0.
- The shutdown runs before the application answers Windows' session-end query. The application never vetoes the query, so it never prevents the session from ending. The sign-out waits only for the host to stop, which takes 150–220 ms for **Exit** in the logs and never more than the 4.5 s watchdog.
- The log records that the application ended because the Windows session ended.
- If the end of the session arrives while a shutdown is already running (**Exit**, or an error), the application waits for that shutdown to finish instead of ending in the middle of it.
- Not included:
  - The application still ends as soon as Windows asks, as WPF does today, even if another application then cancels the sign-out. Staying alive in that case would need a hook into WPF's private window.
  - Starting the application again after a restart ("restart apps") is not part of this change.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `app-shell`: new requirement "End with the Windows session".
- `dictation`: requirement "Dictation ends when the application exits" gets a scenario for a sign-out during a recording. Its text does not change.

## Impact

- Code:
  - `src/Pisum.Transcribe/App.xaml.cs`: handles the end of the session.
  - `src/Pisum.Transcribe/Hosting/ShutdownCoordinator.cs` and `ShutdownReason.cs`: new reason. A later request returns the shutdown that is already running.
  - A new helper in `src/Pisum.Transcribe/Hosting/` that keeps the UI thread processing messages while it waits.
  - Unit tests for the coordinator and the helper.
- No new dependencies, settings or UI.
- User-visible:
  - The log shows why the application ended after a sign-out, shutdown or restart.
  - A recording that runs at sign-out is aborted and its overlay hidden before the process ends.
- Documentation: the Shutdown bullet in `CLAUDE.md` says that the end of the Windows session also goes through `ShutdownCoordinator`.
- Relates to issues #13 and #10 (MR !11). #10 explicitly left the end of the session to this change.
