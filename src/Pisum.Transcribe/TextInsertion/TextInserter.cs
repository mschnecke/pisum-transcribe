using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Settings;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Inserts a transcript by pasting or typing, and falls back to leaving it on the clipboard whenever the input could
/// go to the wrong window or be changed by held modifier keys.
/// </summary>
/// <remarks>
/// <para>
/// The slow clipboard work (snapshot, set, a busy clipboard) runs before the modifier wait, so it overlaps with the
/// user releasing the keys. Nothing slow runs between the final foreground check and the keystrokes.
/// </para>
/// <para>
/// An insertion returns right after the paste, and the clipboard restore finishes in the background. The next
/// insertion and <see cref="StopAsync"/> wait for it, so the user's clipboard is always put back. Insertions must not
/// overlap.
/// </para>
/// </remarks>
internal sealed class TextInserter : ITextInserter, IHostedService
{
    /// <summary>
    /// How long to wait for the user to release the modifier keys.
    /// </summary>
    public static readonly TimeSpan ModifierWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often the modifier keys are read during the wait.
    /// </summary>
    public static readonly TimeSpan ModifierPollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// How long after Ctrl+V the clipboard is restored, so the target application has read the transcript.
    /// </summary>
    public static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(750);

    private readonly IClipboardService _clipboard;
    private readonly IKeyboardInput _keyboard;
    private readonly IForegroundWindowTracker _tracker;
    private readonly ISecureInput _secureInput;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TextInserter> _logger;
    private readonly bool _isSelfElevated;
    private Task _pendingRestore = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="keyboard">The keyboard input.</param>
    /// <param name="tracker">The foreground window tracker.</param>
    /// <param name="secureInput">The secure input state, checked together with the foreground window.</param>
    /// <param name="timeProvider">The time provider for the modifier wait and the restore delay.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="isSelfElevated">Whether this process runs elevated.</param>
    public TextInserter(IClipboardService clipboard,
                        IKeyboardInput keyboard,
                        IForegroundWindowTracker tracker,
                        ISecureInput secureInput,
                        TimeProvider timeProvider,
                        ILogger<TextInserter> logger,
                        bool isSelfElevated)
    {
        _clipboard = clipboard;
        _keyboard = keyboard;
        _tracker = tracker;
        _secureInput = secureInput;
        _timeProvider = timeProvider;
        _logger = logger;
        _isSelfElevated = isSelfElevated;
    }

    /// <inheritdoc />
    public async Task<InsertionOutcome> InsertAsync(string text,
                                                    InsertionTarget target,
                                                    TextInsertionSettings settings,
                                                    CancellationToken cancellationToken)
    {
        // Before any other step, so the snapshot holds the user's content and not the previous transcript.
        await _pendingRestore.ConfigureAwait(false);

        // Without a window, an empty foreground at insertion would match, and Ctrl+V would go nowhere.
        if (target.Window == 0 || !_tracker.IsForeground(target))
        {
            return await FallBackAsync(text, InsertionOutcome.TargetWindowChanged).ConfigureAwait(false);
        }

        if (target.IsElevated && !_isSelfElevated)
        {
            _logger.LogInformation("The target window belongs to elevated process {ProcessId}", target.ProcessId);
            return await FallBackAsync(text, InsertionOutcome.TargetWindowElevated).ConfigureAwait(false);
        }

        var paste = settings.Method == InsertionMethod.ClipboardPaste
            ? await PrepareClipboardAsync(text, settings.RestoreClipboard).ConfigureAwait(false)
            : null;

        var waitStarted = _timeProvider.GetTimestamp();
        while (true)
        {
            // A held Ctrl, or Command on macOS, only matters before typing: Ctrl plus Ctrl+V is still Ctrl+V.
            bool released;
            try
            {
                released = await WaitForModifiersAsync(paste is null, waitStarted, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Otherwise the transcript would stay on the clipboard excluded from the history, with no restore.
                await FallBackAsync(text, InsertionOutcome.ModifierKeysHeld, paste).ConfigureAwait(false);
                throw;
            }

            if (!released)
            {
                return await FallBackAsync(text, InsertionOutcome.ModifierKeysHeld, paste).ConfigureAwait(false);
            }

            // The final gate: no await between here and the keystrokes.
            if (!_tracker.IsForeground(target))
            {
                return await FallBackAsync(text, InsertionOutcome.TargetWindowChanged, paste).ConfigureAwait(false);
            }

            if (_secureInput.IsEnabled)
            {
                _logger.LogInformation("Secure input is on, the text is not sent to process {ProcessId}",
                    target.ProcessId);
                return await FallBackAsync(text, InsertionOutcome.SecureInputOn, paste).ConfigureAwait(false);
            }

            // After secure input, so a notification names the more specific cause when both apply.
            if (!_keyboard.CanPostEvents())
            {
                _logger.LogInformation("Keystrokes are not allowed, the text is not sent to process {ProcessId}",
                    target.ProcessId);
                return await FallBackAsync(text, InsertionOutcome.KeystrokesNotAllowed, paste).ConfigureAwait(false);
            }

            if (paste is null || _clipboard.SequenceNumber == paste.SequenceNumber)
            {
                break;
            }

            // The user copied something during the wait. It stays on the clipboard, and the rest of the wait is for typing.
            _logger.LogInformation("The clipboard changed before the paste, typing the text instead");
            paste = null;
        }

        if (paste is null)
        {
            _keyboard.TypeText(text);
            _logger.LogDebug("Typed {Length} characters", text.Length);
            return InsertionOutcome.Inserted;
        }

        _keyboard.SendPaste();
        _logger.LogDebug("Pasted {Length} characters", text.Length);
        if (paste.Restore is { } snapshot)
        {
            _pendingRestore = RestoreAsync(snapshot, paste.SequenceNumber);
        }

        return InsertionOutcome.Inserted;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits for a pending clipboard restore, so the user's clipboard is put back before the process ends.
    /// </summary>
    /// <param name="cancellationToken">Not used; the restore takes at most about 1.75 s.</param>
    /// <returns>A task that completes when no restore is pending.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _pendingRestore;
    }

    /// <summary>
    /// Places the transcript on the clipboard, after taking a snapshot if the clipboard is restored.
    /// </summary>
    /// <returns>The prepared paste, or <see langword="null"/> to type instead because the clipboard failed.</returns>
    private async Task<PreparedPaste?> PrepareClipboardAsync(string text, bool restoreClipboard)
    {
        ClipboardSnapshot? snapshot = null;
        if (restoreClipboard)
        {
            snapshot = await _clipboard.TrySnapshotAsync().ConfigureAwait(false);
            if (snapshot is null)
            {
                // The service has logged why: a busy clipboard, or a pasteboard that may not be read.
                _logger.LogInformation("The clipboard could not be read, typing the text instead");
                return null;
            }

            if (snapshot.IsSensitive)
            {
                _logger.LogInformation("The clipboard content is marked as sensitive, it will not be restored");
            }
        }

        // Kept out of the history only when it is replaced by a restore. Otherwise it stays as the user's content.
        var restore = snapshot is {IsSensitive: false} ? snapshot : null;
        if (!await _clipboard.TrySetTextAsync(text, restore is not null).ConfigureAwait(false))
        {
            _logger.LogInformation("The clipboard could not be set, typing the text instead");
            return null;
        }

        return new PreparedPaste(_clipboard.SequenceNumber, restore);
    }

    private async Task<bool> WaitForModifiersAsync(bool includePasteModifier,
                                                   long waitStarted,
                                                   CancellationToken cancellationToken)
    {
        while (_keyboard.AreModifiersDown(includePasteModifier))
        {
            if (_timeProvider.GetElapsedTime(waitStarted) >= ModifierWait)
            {
                return false;
            }

            await Task.Delay(ModifierPollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Restores the snapshot after <see cref="RestoreDelay"/>. Never throws, so awaiting it cannot fail the next
    /// insertion or the shutdown.
    /// </summary>
    private async Task RestoreAsync(ClipboardSnapshot snapshot, long sequenceNumber)
    {
        try
        {
            // Not cancellable: the paste has happened, and skipping the restore would lose the user's clipboard.
            await Task.Delay(RestoreDelay, _timeProvider, CancellationToken.None).ConfigureAwait(false);
            if (_clipboard.SequenceNumber != sequenceNumber)
            {
                _logger.LogInformation("The clipboard changed after the paste, its previous contents are not restored");
                return;
            }

            if (!await _clipboard.TryRestoreAsync(snapshot).ConfigureAwait(false))
            {
                _logger.LogWarning("The clipboard was busy after the paste, the transcript stays on the clipboard");
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Restoring the clipboard after the paste failed");
        }
    }

    /// <summary>
    /// Leaves the transcript on the clipboard as the user's content, without history exclusion or a restore.
    /// </summary>
    /// <param name="text">The transcript.</param>
    /// <param name="reason">Why the text was not inserted.</param>
    /// <param name="paste">The paste the transcript was set for, or <see langword="null"/> if none was.</param>
    private async Task<InsertionOutcome> FallBackAsync(string text,
                                                       InsertionOutcome reason,
                                                       PreparedPaste? paste = null)
    {
        if (await _clipboard.TrySetTextAsync(text, false).ConfigureAwait(false))
        {
            _logger.LogInformation("Text not inserted ({Reason}), {Length} characters were left on the clipboard",
                reason,
                text.Length);
            return reason;
        }

        // The transcript set for the paste is still on the clipboard, and keeps its history exclusion if it had one.
        if (paste is not null && _clipboard.SequenceNumber == paste.SequenceNumber)
        {
            _logger.LogWarning(
                "Text not inserted ({Reason}), and the clipboard was busy, but it still holds the {Length} characters",
                reason,
                text.Length);
            return reason;
        }

        _logger.LogWarning("Text not inserted ({Reason}), and the clipboard was busy, so the text was not delivered",
            reason);
        return InsertionOutcome.ClipboardUnavailable;
    }

    /// <param name="SequenceNumber">The clipboard sequence number right after the transcript was set.</param>
    /// <param name="Restore">The snapshot to restore after the paste, or <see langword="null"/> for no restore.</param>
    private sealed record PreparedPaste(long SequenceNumber, ClipboardSnapshot? Restore);
}
