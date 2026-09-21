## Context

See proposal.md, Why. `ShutdownCoordinator` is the only code meant to end the application, but nothing in the application handles the end of the Windows session, so WPF handles it on its own.

WPF's `Application.RunInternal` creates a hidden top-level window. Its hook answers `WM_QUERYENDSESSION` in `WmQueryEndSession`:
1. It raises `SessionEnding`.
2. If the handler did not set `Cancel`, it calls `Application.Shutdown()` and returns TRUE. With `Cancel`, it returns FALSE, which vetoes the session end, and Windows shows the application as preventing the sign-out.

`Shutdown()` posts `ShutdownCallback` at `DispatcherPriority.Normal`. That callback closes the windows, disposes the hidden window and shuts down the dispatcher. `app.Run()` then returns, `Program.Main` flushes the log, and the runtime exits. The capture, hook and worker threads are all background threads, so nothing keeps the process alive. WPF does not handle `WM_ENDSESSION`, and `ShutdownMode="OnExplicitShutdown"` does not prevent any of this.

The coordinator stops the host with the UI thread free. The UI thread has to keep processing messages while the host stops, for two reasons:
- The coordinator's `await`s resume on the dispatcher.
- `DictationFeedback.StopAsync` hides the overlay through `Dispatcher.InvokeAsync`.

## Goals / Non-Goals

**Goals:**
- The end of the session goes through `ShutdownCoordinator`, the same path as **Exit**, with the UI thread still processing messages while the host stops.
- Windows waits for the shutdown, so it cannot end the process in the middle of it.

**Non-Goals:**
- Staying alive when another application cancels the sign-out. WPF decides at the query, before Windows knows whether the session really ends. The only way around it is to subclass WPF's private hidden window, which is not worth it for this case.
- Registering for "restart apps" after a restart or update (`RegisterApplicationRestart`).
- Changing which hosted services stop, or their order.
- The `ConsoleLifetime` that the empty host builder registers. It logs "Press Ctrl+C to shut down." and never receives logoff events in a GUI process. It plays no part in this bug.

## Decisions

### D1: `App.OnSessionEnding` runs the coordinator's shutdown before WPF answers Windows

`App` overrides `OnSessionEnding`:
1. It calls `base.OnSessionEnding`.
2. It requests `ShutdownReason.SessionEnd` from the coordinator, without setting `Cancel`.
3. It waits until the shutdown has finished (D2), and then returns.

WPF's `Shutdown()` in `WmQueryEndSession` then has no effect, because the coordinator has already called `Application.Shutdown(exitCode)`. So the coordinator's exit code stands, and WPF answers TRUE. While the handler runs, Windows waits for the answer, so the shutdown cannot be cut short.

If the coordinator does not exist yet, the override returns without waiting and WPF's default applies. That happens only when the query arrives before `OnStartup` has created it.

A Restart Manager request to close the application (`ENDSESSION_CLOSEAPP`, from an installer) arrives as the same message and takes the same path.

- *Alternative: set `Cancel`, then shut down on `WM_ENDSESSION`.* Cancelling vetoes the session end, and Windows shows the application as preventing the sign-out.
- *Alternative: let WPF end the application and stop the host after `app.Run()` returns.* The dispatcher is gone by then, so the coordinator's continuations and the overlay hide can no longer run. Windows may also end the process at any time after the query, so the stop might not finish.
- *Alternative: subclass WPF's hidden window, answer the query without shutting down, and clean up on `WM_ENDSESSION`.* It would keep the application alive when the sign-out is cancelled. But it depends on a private window that has to be found by its class name. See Non-Goals.

### D2: A nested dispatcher frame that ends through a synchronous continuation

The wait runs a nested `DispatcherFrame` until the shutdown task has completed, so the UI thread keeps processing messages. A new internal helper in `Hosting/` does this: "process messages until this task completes". If the task has already completed, it returns without pushing a frame.

The frame ends through a continuation on the task, registered with `TaskContinuationOptions.ExecuteSynchronously`, that sets `frame.Continue = false`. The order is what matters:
- The coordinator's `finally` calls `Application.Shutdown(exitCode)`. That posts WPF's `ShutdownCallback` at `Normal` *before* the coordinator's task completes.
- The coordinator's last step runs on the UI thread. So a synchronous continuation sets `Continue` in that same dispatcher operation.
- WPF's frame loop checks `Continue` between Win32 messages, and `ProcessQueue` runs one dispatcher operation per message. So the frame ends before `ShutdownCallback` runs, and the callback runs in the outer loop after the hook has returned.
- An `await` in the helper would queue its continuation *behind* `ShutdownCallback`. The callback would then run inside the frame, and WPF would dispose its hidden window while still inside that window's `WM_QUERYENDSESSION` hook.

The WPF facts above come from `WindowsBase` 10.0.8 (decompiled). `HwndSubclass` calls window hooks through `Dispatcher.Invoke(Send)` without `DisableProcessing`. `PushFrame` throws only when processing is disabled or the dispatcher has shut down, so a nested frame is allowed inside `SessionEnding`.

The order is the one part of this change that could break without anyone noticing, so the helper gets a unit test on an STA thread, as `TrayIconServiceTests` does. The test checks that an operation queued just before the task completes runs only after the wait has returned.

- *Alternative: block on the task (`Wait`).* That is a deadlock: the coordinator's continuations and the overlay hide need the UI thread.
- *Alternative: the coordinator skips `Application.Shutdown` for `SessionEnd` and lets WPF's own `Shutdown()` do it.* That doesn't cover an **Exit** or error shutdown that is already running (D4), which calls `Application.Shutdown` anyway. The synchronous continuation covers every case, and it needs no special case in the coordinator.
- *Alternative: inline the frame in `App` without its own test.* Only the manual sign-out check would then cover the order.

### D3: `ShutdownReason.SessionEnd` behaves like `UserExit`

The new value ends with exit code 0 and removes the tray icon at once. No one would see a notification during a sign-out, and nothing reads the exit code. The two branches in `RequestShutdownAsync` change from `reason == UserExit` to `reason != Error`. The existing `Shutting down, reason {Reason}` entry becomes `Shutting down, reason "SessionEnd"`.

- *Alternative: reuse `UserExit`.* Then the log could not tell a sign-out from **Exit**, which is the diagnostic gap #13 describes.
- *Alternative: two values, from WPF's `ReasonSessionEnding` (`Logoff` and `Shutdown`).* WPF derives it from the `ENDSESSION_LOGOFF` bit alone. A restart and a Restart Manager close both read as `Shutdown`, and nothing behaves differently based on it.

### D4: A later request returns the shutdown that is already running

Today a second `RequestShutdownAsync` call returns at once. That was harmless as long as no caller waited: tray Exit, UI errors and the host stopping by itself all start the shutdown and move on. `SessionEnding` waits, though. If the session ends while an **Exit** or error shutdown is still running, `OnSessionEnding` would return at once, and WPF's `Shutdown()` would end the dispatcher in the middle of the running shutdown. The coordinator hasn't reached its `finally` yet, so `IsShuttingDown` is still false.

`RequestShutdownAsync` therefore keeps the task of the first shutdown and returns it on every later call. The first reason still decides the exit code and the tray icon. This also covers a second `WM_QUERYENDSESSION` that arrives while the first one's frame is still running: it waits for the same task in a second, nested frame.

- *Alternative: cut the 3 s error notification wait short when the session ends.* It would save at most 3 s in a rare case, at the cost of one more cancellation path. The watchdog already keeps the sign-out below 5 s.

### D5: Only the `App` glue is verified by hand

A test process can host only one WPF `Application`, and it cannot be created again after it has shut down. So `OnSessionEnding` itself is covered by manual checks, like the rest of `App.xaml.cs`. A PowerShell snippet sends `WM_QUERYENDSESSION` with `ENDSESSION_LOGOFF` to each top-level window of the process (see tasks.md). It takes the same path as a real sign-out, without signing out. It also sends the message from another process, as Windows does, so it covers the COM limitation in the risks. A real sign-out during a recording confirms the rest.

## Risks / Trade-offs

- [The sign-out now waits for the host to stop.] → The log shows 146–218 ms from `Hosting stopping` to `Hosting stopped` for **Exit**, 218 ms of them with a recording aborted at exit. The coordinator's 4.5 s watchdog bounds it below Windows' 5 s limit for an answer.
- [While the UI thread handles a message that another process *sent*, COM refuses outgoing calls from it to objects in another apartment (`RPC_E_CANTCALLOUT_ININPUTSYNCCALL`). `host.StopAsync()` starts on the UI thread, so a hosted service's `StopAsync` that makes such a call before its first `await` hands off the thread would fail. The WASAPI abort in `DictationController` most likely runs on a thread-pool thread by then.] → The failure is fast and doesn't hang. The coordinator logs `Shutdown did not complete cleanly` and still ends the application. The real sign-out check looks for that entry. If it appears, the coordinator starts `host.StopAsync()` on the thread pool.
- [Other messages and dispatcher operations run inside the nested frame, for example a hotkey event or input to an open settings window.] → On **Exit** the UI thread is just as free while the host stops, so nothing new can run. The dictation controller unsubscribes from the hotkey when it stops.
- [The order in D2 depends on WPF internals: `WmQueryEndSession`, `CriticalShutdown` and the frame loop.] → The STA test pins the helper's order against the WPF version in use. Manual check 1 covers `WmQueryEndSession`, and the log entries show a regression.
- [The reproduction script got the answer 0 from one of the process's `IME` windows after 38 ms, while the process was already ending. In a real sign-out, 0 would count as a veto.] → These are windows that Windows itself creates for the thread, and the query to WPF's window was already answered TRUE. The real sign-out check confirms that Windows doesn't list Pisum Transcribe as preventing the sign-out.
