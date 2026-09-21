## Context

This change builds on `scaffold-app-shell` (host, settings, WPF dispatcher) and `add-push-to-talk-recording`, which brings in SharpHook 8.0 and its `SharpHook.Simulation.EventSimulator` (Windows `SendInput`, including Unicode text entry). Several Windows facts shape the approach:
- **UIPI:** `SendInput` into a higher-integrity window fails silently.
- **Foreground lock:** `SetForegroundWindow` from a background process usually fails, so the app cannot reliably re-focus a target.
- **Clipboard ownership:** the clipboard is a shared, lockable OS resource.
- **Clipboard history:** Windows honors the `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard` clipboard formats.

## Goals / Non-Goals

**Goals:**
- Never type into the wrong window. When unsure, fall back to the clipboard and tell the caller why.
- Preserve the user's clipboard in the common case (text).
- Keep all logic unit-testable behind small OS seams.

**Non-Goals:**
- No re-focusing the original window when focus changed. That is unreliable under the foreground lock, and surprising.
- No UI Automation `TextPattern` insertion, which has poor coverage across browsers, Electron apps and terminals.
- No perfect restore of non-HGLOBAL clipboard formats (GDI bitmaps, metafiles, delayed rendering).
- No user notifications. The caller (`add-dictation-workflow`) turns outcomes into messages.

## Decisions

### D1: API

```csharp
internal sealed record InsertionTarget(nint WindowHandle, int ProcessId, bool IsElevated);
internal enum InsertionOutcome { Inserted, TargetWindowChanged, TargetWindowElevated, ModifierKeysHeld, ClipboardUnavailable }

internal interface IForegroundWindowTracker { InsertionTarget CaptureForeground(); bool IsForeground(InsertionTarget target); }
internal interface ITextInserter { Task<InsertionOutcome> InsertAsync(string text, InsertionTarget target, TextInsertionSettings settings, CancellationToken ct); }
```

The caller passes the settings, and `TextInserter` does not read `ISettingsStore`. A dictation therefore finishes with the settings it started with, even when the user saves new ones during transcription, as `add-settings-window` requires.

`TextInserter.InsertAsync` runs these steps in order:
1. `IsForeground(target)` → else fall back with `TargetWindowChanged`. A target with window handle 0, captured while no window was in the foreground, falls back with `TargetWindowChanged` without asking the tracker. Otherwise a foreground that is again empty at insertion would match it, and Ctrl+V would go to no window while reporting `Inserted`.
2. `target.IsElevated && !selfElevated` → fall back with `TargetWindowElevated`.
3. For `clipboardPaste`, prepare the clipboard: snapshot it if restore is enabled, set the transcript, and record the sequence number (D4). The transcript gets the history-exclusion formats only when a restore is planned, meaning restore is enabled and the snapshot is not sensitive. Otherwise the transcript stays on the clipboard as the user's content and belongs in the history. A busy clipboard switches the insertion to typing.
4. Wait for modifiers, polling every 20 ms for up to 2 s: Shift, Alt and Win before a paste, plus Ctrl before typing. Still held → fall back with `ModifierKeysHeld`. A cancelled token also falls back, logged as `ModifierKeysHeld`, and then rethrows `OperationCanceledException`, so a transcript that step 3 placed with the exclusion formats does not stay on the clipboard without a restore.
5. Final gate, with no await between it and the keystrokes: `IsForeground(target)` → else fall back with `TargetWindowChanged`. Before a paste, the clipboard sequence number must still match step 3. If it changed, the user copied something during the wait: switch to typing, skip the restore, and repeat steps 4 and 5 for typing within the same 2 s.
6. Send Ctrl+V, or type the text.
7. After a paste with restore enabled: wait 750 ms, then restore (D4).

"Fall back" means `TrySetTextAsync(text, excludeFromHistory: false)` with no restore, then return the reason. This also replaces a transcript that step 3 placed with the exclusion formats. If the clipboard stays busy, return `ClipboardUnavailable` instead and log the original reason. The exception is a clipboard that still holds the transcript from step 3, with the sequence number unchanged: the transcript is delivered, so return the reason and log a warning. It keeps the exclusion formats if it had them.

*Why this order:* the slow work comes before the checks: the snapshot, the set, and a busy clipboard, which can take about 1 s per call. It overlaps with the user letting go of the keys, and nothing slow runs between the final gate and the keystrokes. A held Ctrl only matters before typing: for a paste, Ctrl plus Ctrl+V is still Ctrl+V, so a user holding the right Ctrl hotkey again for the next dictation does not push the current transcript onto the clipboard.

### D2: OS seams

- `IClipboardService`: `TrySnapshotAsync()`, `TrySetTextAsync(text, excludeFromHistory)`, `TryRestoreAsync(snapshot)`, `SequenceNumber`. The `Try*Async` methods return false or null when the clipboard is busy. A snapshot tells whether its content was marked as sensitive (D4).
- `IKeyboardInput`: `SendPaste()`, `TypeText(string)`, `AreModifiersDown(bool includeControl)`.
- `IForegroundWindowTracker`.

The production implementations are thin. `TextInserter`'s decision logic is tested with FakeItEasy fakes of all three.

### D3: Win32 access through CsWin32

`NativeMethods.txt` lists: `GetForegroundWindow`, `GetWindowThreadProcessId`, `OpenProcess`, `OpenProcessToken`, `GetTokenInformation`, `CloseHandle`, `GetAsyncKeyState`, `GetClipboardSequenceNumber`, and the `TOKEN_ELEVATION` struct. `GetTokenInformation` fills that struct through an untyped buffer, so CsWin32 generates it only when it is listed.

**Elevation check:** `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `OpenProcessToken(TOKEN_QUERY)` → `GetTokenInformation(TokenElevation)`. If any of these calls fails, the target is treated as elevated, the safe side. Usually that is `OpenProcess` or `OpenProcessToken` with access denied for a process of higher integrity; the process ID and the Win32 error are logged. The app's own elevation is computed once the same way. If its own token cannot be read, the app counts as not elevated, so elevated targets still fall back.

*Why CsWin32 over hand-written `LibraryImport`:* the generated signatures and struct layouts are correct by construction, and it is a build-time-only dependency. The hand-written `GetAsyncKeyState` import in `SharpHookPushToTalkHotkey` stays as it is.

### D4: Clipboard via WPF `Clipboard` on a dedicated STA thread

`Clipboard` requires an STA thread. `WpfClipboardService` owns one background STA thread that runs its own `Dispatcher` (`Dispatcher.Run()`) and queues every call there with `Dispatcher.InvokeAsync`. The thread is shut down with `Dispatcher.InvokeShutdown` when the host stops.

The UI dispatcher is never used for the clipboard. In .NET 10, every WPF clipboard call (get, set, flush, clear) goes through `System.Private.Windows.Ole.ClipboardCore<T>`, which retries a busy clipboard 10 times with a 100 ms sleep on the calling thread. WPF does not expose these values. A busy clipboard therefore fails after about 1 s with a `COMException` (`CLIPBRD_E_CANT_OPEN`), which the service turns into false. The service has no retry loop of its own.

- **Snapshot:** `Clipboard.GetDataObject()`, then for each `GetFormats(autoConvert: false)`, `GetData(format, false)`, copied into a new `DataObject`. A format that throws or returns null is skipped. Text formats (`UnicodeText`, `Text`) always survive this. Images and files usually do, but delay-rendered formats may not; that limitation is accepted. Reading every format makes the owning application render formats it only provides on request, which can take a while for a large Excel range; D1 step 3 runs before the modifier wait, so this overlaps with the user letting go of the keys. The snapshot counts as sensitive when the content carries `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` = 0 or `CanUploadToCloudClipboard` = 0, unless it also carries `Pisum.Transcribe.Restored` (see below). A history or cloud value that cannot be read counts as 0. The copy leaves out these three formats and `Pisum.Transcribe.Restored`, because the restore sets its own markers. Password managers set these formats: KeePassXC sets all three, and Bitwarden on Windows sets only the last two.
- **Set with history exclusion:** `DataObject` with `UnicodeText`, plus `ExcludeClipboardContentFromMonitorProcessing` (a 1-byte `MemoryStream`), `CanIncludeInClipboardHistory` = DWORD 0 and `CanUploadToCloudClipboard` = DWORD 0 as 4-byte `MemoryStream`s. Then `Clipboard.SetDataObject(obj, copy: true)`. Only the presence of `ExcludeClipboardContentFromMonitorProcessing` counts, but it needs data: WPF puts no zero-length data on the Windows clipboard. An empty stream would be visible only to WPF reads inside this process, and clipboard history would record the transcript.
- **Sequence number:** `SequenceNumber` reads `GetClipboardSequenceNumber()` directly. It works on any thread, so the final gate in D1 step 5 needs no call to the clipboard thread.
- **Restore guard:** record the sequence number right after setting. At restore time (750 ms after Ctrl+V, `Task.Delay`), restore only if the number is unchanged and the snapshot is not sensitive. The caller's cancellation token does not cancel this delay: once Ctrl+V is sent, skipping the restore would lose the user's clipboard. The token cancels only the modifier wait (D1 step 4). If the snapshot was empty, the restore clears the clipboard. A busy clipboard at restore time leaves the transcript on the clipboard; this is logged, and the outcome stays `Inserted`.
- **Restore without a second history entry:** the restored `DataObject` gets `CanIncludeInClipboardHistory` = 0 and `CanUploadToCloudClipboard` = 0, because its content is already in the history from when the user copied it. It also gets the private format `Pisum.Transcribe.Restored` (a 1-byte `MemoryStream`, for the same reason). Without it, the next dictation's snapshot would count the restored content as sensitive because of the two history formats, and skip its restore. Only content that was not sensitive is ever restored, so the private format never hides a password.
- **Sensitive content is not restored:** KeePass, KeePassXC and Bitwarden clear the clipboard when their timer fires only if it still holds the text they copied. They compare the text, not the owner or the sequence number, so a restored password is still cleared on time. The exception is a timer that fires during the 750 ms before the restore: the manager sees the transcript and skips its one-time clear, and the restore then puts the password back for good. Skipping the restore avoids this. It leaves the transcript on the clipboard and the password gone, as if the manager had cleared it early.

*Alternative:* raw Win32 clipboard (`OpenClipboard` / `EnumClipboardFormats` / HGLOBAL copies). This is more faithful for some formats and would allow a shorter busy timeout, but it needs an owner window and far more code. WPF's OLE clipboard is good enough for the "text restored exactly, others best-effort" contract, and about 1 s for a busy clipboard is accepted.

### D5: Keystrokes via SharpHook `EventSimulator`

A single `EventSimulator` instance is created with `EventSimulator.Create(applicationName, simulationProvider: null)` and disposed with the host. The application name only matters on Linux.
- **Paste:** one `Sequence()` with `AddKeyPress(VcLeftControl)`, `AddKeyPress(VcV)`, `AddKeyRelease(VcV)`, `AddKeyRelease(VcLeftControl)`, then `Simulate()`, which posts the four events in one call. SharpHook documents this only as posting a sequence. Whether libuiohook turns it into a single `SendInput` call, which would keep a key the user presses from landing between the events, is not documented.
- **Type:** the text is normalized to `\n` line endings and split on `\n`. Each part goes through `SimulateTextEntry(part)`, which uses `SendInput` with `KEYEVENTF_UNICODE` and handles surrogate pairs, followed by an Enter press and release between parts.
- **Result codes:** a `UioHookResult` other than `Success` is logged and reported as `Inserted` anyway. UIPI failures were already excluded by the elevation check, and a silent partial failure cannot be detected reliably.

*Why no own `SendInput` code:* SharpHook is already a dependency, and its simulator is tested upstream.

### D6: Settings

`TextInsertionSettings(InsertionMethod Method = ClipboardPaste, bool RestoreClipboard = true)`. The 750 ms restore delay and the 2 s modifier wait are internal constants, not settings. The busy-clipboard timeout is WPF's built-in 10 × 100 ms (D4).

## Risks / Trade-offs

- [Some apps read the clipboard lazily on paste and may still be reading when the restore happens after 750 ms (e.g. slow remote desktop sessions).] → The delay is a single constant and is easy to tune. Users can disable restore.
- [Windows Terminal renders `VK_PACKET` (type-text) input incorrectly in some versions.] → `clipboardPaste` is the default. `typeText` is only a fallback for a busy clipboard or when the user chooses it.
- [Treating a token that cannot be read, usually access denied, as elevated may produce false positives for protected processes that accept input.] → The outcome is safe (text on clipboard, user informed), just less convenient. Log the process ID for diagnosis.
- [The elevation check cannot trigger on the target laptop. UAC is off there (`EnableLUA` = 0), and the app's own token reports `TokenIsElevated` = 1. With UAC on, the hook sees no hotkey press while an elevated window has focus (`add-push-to-talk-recording` notes), so a target captured at the press is almost never elevated.] → Kept as a cheap safety net and covered by unit tests. The manual check is not reproducible on the target laptop.
- [A snapshot of a very large clipboard item, e.g. a huge image, costs memory and time.] → Accepted for v1. Measure in manual testing, and skip formats above 50 MB if it proves a problem.
- [A busy clipboard blocks the clipboard thread for about 1 s per call, and a fallback after a busy preparation may need a second call.] → It never blocks the UI thread, and clipboard locks usually last milliseconds. A fallback that still cannot copy reports `ClipboardUnavailable`, so the caller never claims the text was copied.
- [Content marked sensitive is not restored, so a password the user copied is gone from the clipboard after a dictation.] → Intended: a password manager whose clear timer fires during the 750 ms before the restore would otherwise leave the password on the clipboard for good. The user copies it again.
- [An application that marks ordinary content with `CanIncludeInClipboardHistory` = 0 or `CanUploadToCloudClipboard` = 0 loses its restore as well.] → Accepted. The transcript stays on the clipboard, and the user copies the content again.
- [Whether Windows adds restored content to the Win+V history a second time without the markers is untested.] → The restore sets the markers anyway (D4). The manual Win+V check in `add-dictation-workflow` covers it.
- [Sending Left Ctrl while the user's physical right Ctrl hotkey was just released, because of key-up event races.] → The modifier wait (D1 step 4) runs before any keystroke. A held Ctrl is harmless for Ctrl+V and is waited for only before typing.
