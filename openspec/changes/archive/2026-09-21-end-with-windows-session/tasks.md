## 1. Reproduce in unit tests

- [x] 1.1 Add `SessionEnd` to `ShutdownReason`, with an XML doc like the other values ("The Windows session ends: sign-out, shutdown or restart. Exit code 0."), but don't change the coordinator yet. In `ShutdownCoordinatorTests`, switch the coordinator's logger to a `CapturingLogger<ShutdownCoordinator>`. Then add `RequestShutdownAsync_SessionEnd_RemovesIconBeforeStoppingHostAndExitsWithCode0`, modelled on `ExitRequested_Raised_RemovesIconBeforeStoppingHostAndExitsWithCode0`. It asserts that:
  - the icon is removed before the host stops and is disposed, and no notification is shown;
  - the application shuts down with exit code 0;
  - the log has the `Shutting down, reason {Reason}` entry with the reason `SessionEnd`.

  Verify: the test fails today, because the coordinator treats every reason other than `UserExit` as an error: it shows the notification and exits with code 1.
- [x] 1.2 Replace `RequestShutdownAsync_CalledTwice_SecondCallIsIgnored` with `RequestShutdownAsync_CalledWhileRunning_ReturnsTheRunningShutdown` (design D4):
  1. The host's `StopAsync` returns the task of a `TaskCompletionSource`.
  2. Request `UserExit`, then `SessionEnd`, and check that the second task is not yet complete.
  3. Complete the host stop and await both tasks.
  4. Assert that the application shut down once with exit code 0 and the host stopped once.

  Verify: the test fails today, because the second call completes at once.

## 2. Fix

- [x] 2.1 In `ShutdownCoordinator.RequestShutdownAsync`, change the exit code and the tray icon branch from `reason == ShutdownReason.UserExit` to `reason != ShutdownReason.Error` (design D3). Update the class `<remarks>` so that it names `SessionEnd` next to `UserExit`. Verify: 1.1 passes, and the other coordinator tests still pass.
- [x] 2.2 Keep the task of the first shutdown and return it on every later call, with calls from any thread still safe, as the `Interlocked` flag is today (design D4). Update the method's `<summary>`: only the first call starts a shutdown, and later calls return its task. Verify: 1.2 passes, and so do `RequestShutdownAsync_HostNeverStops_ExitsProcessAtExitTimeout` and the error test.
- [x] 2.3 Add an `internal static` helper in `src/Pisum.Transcribe/Hosting/` that processes the dispatcher's messages until a task has completed (design D2):
  - If the task has already completed, it returns at once.
  - Otherwise it pushes a `DispatcherFrame`. A continuation registered with `TaskContinuationOptions.ExecuteSynchronously` sets `Continue = false`.
  - A comment says why it must not be an `await`.
  - Add its unit test class in `tests/Pisum.Transcribe.Tests/Hosting/`. It runs on an STA thread, like `TrayIconServiceTests.RunOnStaThread`, with that thread's dispatcher:
    - One test: a dispatcher operation first queues an operation at `Normal` and then completes the task, which is a `TaskCompletionSource` without `RunContinuationsAsynchronously`, like an `async` method's task. It asserts that the wait returned before the queued operation ran, and that the operation still ran afterwards.
    - A second test: an already completed task returns without running queued operations.

  Verify: both tests pass. Then replace the continuation with an `await` for a moment and confirm that the first test fails. Revert.
- [x] 2.4 In `App.xaml.cs`, override `OnSessionEnding` (design D1):
  1. Call `base.OnSessionEnding(e)`.
  2. If `_shutdownCoordinator` is still `null`, return.
  3. Otherwise request `ShutdownReason.SessionEnd` and wait with the helper from 2.3.
  4. Never set `e.Cancel`.

  Verify: `dotnet build Pisum.Transcribe.slnx` has no warnings. The behavior is checked in 3.2 and 3.3.

## 3. Verification

- [x] 3.1 Run `dotnet build Pisum.Transcribe.slnx`, `dotnet test Pisum.Transcribe.slnx` and `openspec validate end-with-windows-session --strict`. Verify: no warnings, all tests pass, and validation reports no issues. The spec scenario "Background work does not stop in time" is covered by `RequestShutdownAsync_HostNeverStops_ExitsProcessAtExitTimeout`, because the watchdog starts for every reason.
- [x] 3.2 Manual check without signing out (design D5, spec scenario "User signs out, shuts down or restarts"):
  1. Start the app with `dotnet run --project src/Pisum.Transcribe` and an installed model.
  2. Wait until the log shows `Model … is ready`.
  3. In a second PowerShell window, run the snippet below. It sends `WM_QUERYENDSESSION` with `ENDSESSION_LOGOFF` to each top-level window of the process, one at a time, as Windows does.

  Baseline before the fix (reproduced on `a446820`): WPF's `HwndWrapper` window answers 1 after 2 ms, and the process ends about 870 ms later with exit code 0. Nothing is logged after the model became ready.

  Verify after the fix:
  - The log shows `Shutting down, reason "SessionEnd"`, then `Hosting stopping` and `Hosting stopped`.
  - The first `HwndWrapper` window answers 1, now only after roughly the host's stop time.
  - The process ends within 5 s with exit code 0.
  - The log has no `Shutdown did not complete cleanly`.

  ```powershell
  Add-Type @'
  using System;
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  public static class SessionEnd
  {
      private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
      [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
      [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
      [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
      [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam,
          IntPtr lParam, uint flags, uint timeout, out IntPtr result);

      public static void Send(int processId)
      {
          var windows = new List<IntPtr>();
          EnumWindows((hwnd, _) => { uint id; GetWindowThreadProcessId(hwnd, out id); if (id == processId) windows.Add(hwnd); return true; }, IntPtr.Zero);
          foreach (var hwnd in windows)
          {
              var name = new StringBuilder(256);
              GetClassName(hwnd, name, name.Capacity);
              var started = DateTime.Now;
              IntPtr result;
              // WM_QUERYENDSESSION, ENDSESSION_LOGOFF, SMTO_ABORTIFHUNG, 5 s
              var sent = SendMessageTimeout(hwnd, 0x0011, IntPtr.Zero, new IntPtr(0x80000000L), 0x0002, 5000, out result);
              Console.WriteLine("{0:HH:mm:ss.fff} 0x{1:X} {2}: sent={3} result={4} ({5:0} ms)", started, hwnd.ToInt64(), name,
                  sent != IntPtr.Zero, result, (DateTime.Now - started).TotalMilliseconds);
          }
      }
  }
  '@
  $process = Get-Process Pisum.Transcribe
  # Opens the handle now, so the exit code can be read after the process has ended.
  $null = $process.Handle
  $watch = [Diagnostics.Stopwatch]::StartNew()
  [SessionEnd]::Send($process.Id)
  "Ended: $($process.WaitForExit(5000)) after $($watch.ElapsedMilliseconds) ms, exit code $($process.ExitCode)"
  ```
- [x] 3.3 Manual check with a real sign-out (spec scenarios "User signs out, shuts down or restarts" and "Sign-out during a recording"):
  1. Start the app with an installed model and wait until the model is ready.
  2. Hold right Ctrl until the overlay shows "Recording".
  3. While still holding it, sign out with the mouse (Start > account > **Sign out**).
  4. Sign in again and open that day's log.

  Verify:
  - The log shows `Shutting down, reason "SessionEnd"`, then `The recording was aborted at exit after …`, then `Hosting stopped`.
  - There is no `Shutdown did not complete cleanly`.
  - Nothing was inserted.
  - Windows did not show Pisum Transcribe as preventing the sign-out.

  If `Shutdown did not complete cleanly` appears, apply the fallback from the design's COM risk: start `host.StopAsync()` on the thread pool. Then repeat 3.2 and 3.3. A restart of Windows is optional: it sends the same message, only without the logoff bit.

## 4. Documentation

- [x] 4.1 Extend the **Shutdown** bullet in `CLAUDE.md`. It should say that the end of the Windows session reaches `ShutdownCoordinator` through `App.OnSessionEnding`, which waits for the shutdown while the UI thread keeps processing messages, and that `SessionEnding` must never be cancelled, because that would veto the sign-out. Verify: the bullet says both.
