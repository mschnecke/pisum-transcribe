using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TextInserterTests
{
    private const string Transcript = "Grüße aus Köln – 5 €";

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TimeStep = TimeSpan.FromMilliseconds(100);
    private static readonly InsertionTarget Target = new(0x1234, 42, false);
    private static readonly TextInsertionSettings Paste = new(InsertionMethod.ClipboardPaste, true);
    private static readonly TextInsertionSettings PasteWithoutRestore = new(InsertionMethod.ClipboardPaste, false);
    private static readonly TextInsertionSettings Type = new(InsertionMethod.TypeText, true);

    private readonly IClipboardService _clipboard = A.Fake<IClipboardService>();
    private readonly IKeyboardInput _keyboard = A.Fake<IKeyboardInput>();
    private readonly IForegroundWindowTracker _tracker = A.Fake<IForegroundWindowTracker>();
    private readonly ISecureInput _secureInput = A.Fake<ISecureInput>();
    private readonly FakeTimeProvider _time = new();
    private readonly CapturingLogger<TextInserter> _logger = new();
    private readonly ClipboardSnapshot _snapshot = new TextSnapshot("invoice 4711");
    private readonly long _start;
    private readonly TextInserter _sut;
    private long _sequenceNumber = 1;

    public TextInserterTests()
    {
        _start = _time.GetTimestamp();
        A.CallTo(() => _tracker.IsForeground(Target)).Returns(true);
        A.CallTo(() => _clipboard.SequenceNumber).ReturnsLazily(() => _sequenceNumber);
        A.CallTo(() => _clipboard.TrySnapshotAsync()).Returns(_snapshot);
        A.CallTo(() => _clipboard.TrySetTextAsync(A<string>._, A<bool>._)).ReturnsLazily(() =>
        {
            _sequenceNumber++;
            return true;
        });
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).ReturnsLazily(() =>
        {
            _sequenceNumber++;
            return true;
        });
        _sut = CreateSut(false);
    }

    private TimeSpan Elapsed => _time.GetElapsedTime(_start);

    [Fact]
    public async Task InsertAsync_TargetNotInForeground_ReturnsTargetWindowChangedAndLeavesTranscriptOnClipboard()
    {
        // Arrange
        A.CallTo(() => _tracker.IsForeground(Target)).Returns(false);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.TargetWindowChanged);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _clipboard.TrySetTextAsync(A<string>._, true)).MustNotHaveHappened();
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_TargetWithoutWindow_ReturnsTargetWindowChangedWithoutAskingTracker()
    {
        // Arrange
        var noWindow = new InsertionTarget(0, 0, false);
        A.CallTo(() => _tracker.IsForeground(A<InsertionTarget>._)).Returns(true);

        // Act
        var outcome = await InsertAsync(Paste, noWindow);

        // Assert
        outcome.ShouldBe(InsertionOutcome.TargetWindowChanged);
        A.CallTo(() => _tracker.IsForeground(A<InsertionTarget>._)).MustNotHaveHappened();
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly();
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_ElevatedTarget_ReturnsTargetWindowElevatedAndLeavesTranscriptOnClipboard()
    {
        // Arrange
        var elevated = Target with {IsElevated = true};
        A.CallTo(() => _tracker.IsForeground(elevated)).Returns(true);

        // Act
        var outcome = await InsertAsync(Paste, elevated);

        // Assert
        outcome.ShouldBe(InsertionOutcome.TargetWindowElevated);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _clipboard.TrySnapshotAsync()).MustNotHaveHappened();
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_ElevatedTargetWhileSelfElevated_Pastes()
    {
        // Arrange
        var elevated = Target with {IsElevated = true};
        A.CallTo(() => _tracker.IsForeground(elevated)).Returns(true);
        var sut = CreateSut(true);

        // Act
        var outcome = await InsertAsync(Paste, elevated, sut);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InsertAsync_SecureInputOn_ReturnsSecureInputOnAndLeavesTranscriptOnClipboard()
    {
        // Arrange
        A.CallTo(() => _secureInput.IsEnabled).Returns(true);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.SecureInputOn);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, true)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly());
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_SecureInputOnBeforeTyping_ReturnsSecureInputOn()
    {
        // Arrange
        A.CallTo(() => _secureInput.IsEnabled).Returns(true);

        // Act
        var outcome = await InsertAsync(Type);

        // Assert
        outcome.ShouldBe(InsertionOutcome.SecureInputOn);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly();
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_SecureInputTurnsOffDuringModifierWait_PastesWithoutCheckingBeforeTheWait()
    {
        // Arrange
        var waitEnd = TimeSpan.FromMilliseconds(500);
        HoldModifiers(waitEnd);
        A.CallTo(() => _secureInput.IsEnabled).ReturnsLazily(() => Elapsed < waitEnd);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _secureInput.IsEnabled).MustHaveHappenedOnceExactly();
        A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InsertAsync_TargetChangedAndSecureInputOn_ReturnsTargetWindowChanged()
    {
        // Arrange
        A.CallTo(() => _tracker.IsForeground(Target)).Returns(false);
        A.CallTo(() => _secureInput.IsEnabled).Returns(true);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.TargetWindowChanged);
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_ModifierHeldForMoreThanTwoSeconds_ReturnsModifierKeysHeldAfterTwoSeconds()
    {
        // Arrange
        HoldModifiers(TimeSpan.MaxValue);
        var fallbackAt = TimeSpan.Zero;
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).ReturnsLazily(() =>
        {
            fallbackAt = Elapsed;
            return true;
        });

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.ModifierKeysHeld);
        fallbackAt.ShouldBeGreaterThanOrEqualTo(TextInserter.ModifierWait);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, true)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly());
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_CancelledDuringModifierWait_LeavesTranscriptOnClipboardAndThrows()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        A.CallTo(() => _keyboard.AreModifiersDown(A<bool>._)).ReturnsLazily(() =>
        {
            cancellation.Cancel();
            return true;
        });

        // Act
        var insert = _sut.InsertAsync(Transcript, Target, Paste, cancellation.Token);

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(insert.WaitAsync(SignalTimeout,
            TestContext.Current.CancellationToken));
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, true)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly());
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_ModifierReleasedAfter500Ms_PastesAfterRelease()
    {
        // Arrange
        HoldModifiers(TimeSpan.FromMilliseconds(500));
        var pastedAt = TimeSpan.Zero;
        A.CallTo(() => _keyboard.SendPaste()).Invokes(() => pastedAt = Elapsed);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedOnceExactly();
        pastedAt.ShouldBeInRange(TimeSpan.FromMilliseconds(500), TextInserter.ModifierWait);
    }

    [Fact]
    public async Task InsertAsync_FallbackWhileClipboardBusy_ReturnsClipboardUnavailableAndLogsReason()
    {
        // Arrange
        A.CallTo(() => _tracker.IsForeground(Target)).Returns(false);
        A.CallTo(() => _clipboard.TrySetTextAsync(A<string>._, A<bool>._)).Returns(false);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.ClipboardUnavailable);
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains(nameof(InsertionOutcome.TargetWindowChanged)));
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_FallbackWhileClipboardBusyAfterTranscriptWasSet_ReturnsReasonAndLogsWarning()
    {
        // Arrange: the transcript is set for the paste, and the clipboard is busy for the fallback.
        HoldModifiers(TimeSpan.MaxValue);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).Returns(false);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.ModifierKeysHeld);
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains(nameof(InsertionOutcome.ModifierKeysHeld)));
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_FallbackWhileClipboardBusyAfterUserCopied_ReturnsClipboardUnavailable()
    {
        // Arrange: the user copies something while still holding Shift, and the clipboard is busy for the fallback.
        var copied = false;
        A.CallTo(() => _keyboard.AreModifiersDown(A<bool>._)).ReturnsLazily(() =>
        {
            if (!copied)
            {
                copied = true;
                _sequenceNumber++;
            }

            return true;
        });
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).Returns(false);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.ClipboardUnavailable);
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_TypeTextSettings_TypesTextWithoutTouchingClipboard()
    {
        // Act
        var outcome = await InsertAsync(Type);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _keyboard.TypeText(Transcript)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _keyboard.SendPaste()).MustNotHaveHappened();
        A.CallTo(_clipboard).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_ClipboardPasteSettings_SetsTranscriptAndPastes()
    {
        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, true)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedOnceExactly());
        A.CallTo(() => _keyboard.TypeText(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_CtrlHeldBeforePaste_PastesWithoutWaiting()
    {
        // Arrange
        HoldControl(TimeSpan.MaxValue);
        var pastedAt = TimeSpan.MaxValue;
        A.CallTo(() => _keyboard.SendPaste()).Invokes(() => pastedAt = Elapsed);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        pastedAt.ShouldBe(TimeSpan.Zero);
        A.CallTo(() => _keyboard.AreModifiersDown(true)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_CtrlHeldBeforeTyping_TypesAfterRelease()
    {
        // Arrange
        HoldControl(TimeSpan.FromMilliseconds(500));
        var typedAt = TimeSpan.Zero;
        A.CallTo(() => _keyboard.TypeText(Transcript)).Invokes(() => typedAt = Elapsed);

        // Act
        var outcome = await InsertAsync(Type);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        typedAt.ShouldBeInRange(TimeSpan.FromMilliseconds(500), TextInserter.ModifierWait);
    }

    [Fact]
    public async Task InsertAsync_ForegroundChangesDuringModifierWait_ReturnsTargetWindowChangedAndSetsTranscriptAgain()
    {
        // Arrange
        HoldModifiers(TimeSpan.FromMilliseconds(500));
        A.CallTo(() => _tracker.IsForeground(Target)).ReturnsLazily(() => Elapsed == TimeSpan.Zero);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.TargetWindowChanged);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, true)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly());
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
        ShouldNotHaveSentKeystrokes();
    }

    [Fact]
    public async Task InsertAsync_ClipboardChangesDuringModifierWait_TypesTextAndDoesNotRestore()
    {
        // Arrange: the user copies something while still holding Shift.
        var copied = false;
        A.CallTo(() => _keyboard.AreModifiersDown(A<bool>._)).ReturnsLazily(() =>
        {
            if (!copied)
            {
                copied = true;
                _sequenceNumber++;
            }

            return Elapsed < TimeSpan.FromMilliseconds(500);
        });

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _keyboard.TypeText(Transcript)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _keyboard.SendPaste()).MustNotHaveHappened();
        A.CallTo(() => _clipboard.TrySetTextAsync(A<string>._, A<bool>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_ClipboardUnchangedAfterPaste_RestoresSnapshotAfterRestoreDelay()
    {
        // Arrange
        var pastedAt = TimeSpan.Zero;
        var restoredAt = TimeSpan.Zero;
        A.CallTo(() => _keyboard.SendPaste()).Invokes(() => pastedAt = Elapsed);
        A.CallTo(() => _clipboard.TryRestoreAsync(_snapshot)).ReturnsLazily(() =>
        {
            restoredAt = Elapsed;
            return true;
        });

        // Act
        var outcome = await InsertAsync(Paste);
        await CompletePendingRestoreAsync();

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _clipboard.TryRestoreAsync(_snapshot)).MustHaveHappenedOnceExactly();
        (restoredAt - pastedAt).ShouldBeGreaterThanOrEqualTo(TextInserter.RestoreDelay);
    }

    [Fact]
    public async Task InsertAsync_ClipboardChangedAfterPaste_DoesNotRestore()
    {
        // Arrange: the user copies something right after the paste.
        A.CallTo(() => _keyboard.SendPaste()).Invokes(() => _sequenceNumber++);

        // Act
        var outcome = await InsertAsync(Paste);
        await CompletePendingRestoreAsync();

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_SensitiveSnapshot_PastesWithoutHistoryExclusionAndDoesNotRestore()
    {
        // Arrange
        A.CallTo(() => _clipboard.TrySnapshotAsync()).Returns(_snapshot with {IsSensitive = true});

        // Act
        var outcome = await InsertAsync(Paste);
        await CompletePendingRestoreAsync();

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, false)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_TwoPastesInARow_RestoresOriginalTextBothTimes()
    {
        // Arrange: a clipboard that holds what was last set or restored.
        var content = "invoice 4711";
        A.CallTo(() => _clipboard.TrySnapshotAsync()).ReturnsLazily(() => new TextSnapshot(content));
        A.CallTo(() => _clipboard.TrySetTextAsync(A<string>._, A<bool>._)).ReturnsLazily((string text, bool _) =>
        {
            content = text;
            _sequenceNumber++;
            return true;
        });
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).ReturnsLazily((ClipboardSnapshot snapshot) =>
        {
            content = ((TextSnapshot) snapshot).Text;
            _sequenceNumber++;
            return true;
        });

        // Act
        var first = await InsertAsync(Paste);
        await CompletePendingRestoreAsync();
        var afterFirst = content;
        var second = await InsertAsync(Paste);
        await CompletePendingRestoreAsync();

        // Assert
        first.ShouldBe(InsertionOutcome.Inserted);
        second.ShouldBe(InsertionOutcome.Inserted);
        afterFirst.ShouldBe("invoice 4711");
        content.ShouldBe("invoice 4711");
        A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task InsertAsync_RestoreDisabled_NeitherSnapshotsNorRestores()
    {
        // Act
        var outcome = await InsertAsync(PasteWithoutRestore);
        await CompletePendingRestoreAsync();

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _clipboard.TrySnapshotAsync()).MustNotHaveHappened();
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public async Task InsertAsync_Paste_ExcludesTranscriptFromHistoryOnlyWhenRestorePlanned(bool restoreClipboard,
        bool sensitive,
        bool expectedExclusion)
    {
        // Arrange
        A.CallTo(() => _clipboard.TrySnapshotAsync()).Returns(_snapshot with {IsSensitive = sensitive});

        // Act
        await InsertAsync(Paste with {RestoreClipboard = restoreClipboard});

        // Assert
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, expectedExclusion)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _clipboard.TrySetTextAsync(Transcript, !expectedExclusion)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_ClipboardBusyDuringSnapshot_TypesTextAndReturnsInserted()
    {
        // Arrange
        A.CallTo(() => _clipboard.TrySnapshotAsync()).Returns((ClipboardSnapshot?) null);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _keyboard.TypeText(Transcript)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _keyboard.SendPaste()).MustNotHaveHappened();
        A.CallTo(() => _clipboard.TrySetTextAsync(A<string>._, A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_ClipboardBusyWhileSettingTranscript_TypesTextAndReturnsInserted()
    {
        // Arrange
        A.CallTo(() => _clipboard.TrySetTextAsync(A<string>._, A<bool>._)).Returns(false);

        // Act
        var outcome = await InsertAsync(Paste);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _keyboard.TypeText(Transcript)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _keyboard.SendPaste()).MustNotHaveHappened();
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_ClipboardBusyAtRestore_ReturnsInsertedAndLogsWarning()
    {
        // Arrange
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).Returns(false);

        // Act
        var outcome = await InsertAsync(Paste);
        await CompletePendingRestoreAsync();

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _clipboard.TryRestoreAsync(_snapshot)).MustHaveHappenedOnceExactly();
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task InsertAsync_Paste_ReturnsInsertedBeforeRestoreDelay()
    {
        // Act
        var outcome = await _sut.InsertAsync(Transcript, Target, Paste, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        Elapsed.ShouldBeLessThan(TextInserter.RestoreDelay);
        A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAsync_SecondPaste300MsAfterFirst_SnapshotsAfterFirstRestoreAndEndsWithOriginalContent()
    {
        // Arrange: a clipboard that holds what was last set or restored.
        var content = "invoice 4711";
        var snapshots = new List<(string Text, TimeSpan At)>();
        A.CallTo(() => _clipboard.TrySnapshotAsync()).ReturnsLazily(() =>
        {
            lock (snapshots)
            {
                snapshots.Add((content, Elapsed));
            }

            return new TextSnapshot(content);
        });
        A.CallTo(() => _clipboard.TrySetTextAsync(A<string>._, A<bool>._)).ReturnsLazily((string text, bool _) =>
        {
            content = text;
            _sequenceNumber++;
            return true;
        });
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).ReturnsLazily((ClipboardSnapshot snapshot) =>
        {
            content = ((TextSnapshot) snapshot).Text;
            _sequenceNumber++;
            return true;
        });
        var first = await _sut.InsertAsync(Transcript, Target, Paste, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMilliseconds(300));

        // Act
        var second = _sut.InsertAsync("Zweiter Satz", Target, Paste, TestContext.Current.CancellationToken);
        int snapshotsWhileRestorePending;
        lock (snapshots)
        {
            snapshotsWhileRestorePending = snapshots.Count;
        }

        var secondOutcome = await AdvanceUntilCompletedAsync(second);
        await CompletePendingRestoreAsync();

        // Assert
        first.ShouldBe(InsertionOutcome.Inserted);
        secondOutcome.ShouldBe(InsertionOutcome.Inserted);
        snapshotsWhileRestorePending.ShouldBe(1);
        snapshots.Select(snapshot => snapshot.Text).ShouldBe(["invoice 4711", "invoice 4711"]);
        snapshots[1].At.ShouldBeGreaterThanOrEqualTo(TextInserter.RestoreDelay);
        content.ShouldBe("invoice 4711");
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task StopAsync_RestorePending_CompletesOnlyAfterRestore()
    {
        // Arrange
        await _sut.InsertAsync(Transcript, Target, Paste, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Act
        var stop = _sut.StopAsync(TestContext.Current.CancellationToken);
        var completedBeforeDelay = stop.IsCompleted;
        _time.Advance(TextInserter.RestoreDelay);
        await stop.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        completedBeforeDelay.ShouldBeFalse();
        A.CallTo(() => _clipboard.TryRestoreAsync(_snapshot)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InsertAsync_PreviousRestoreFailed_InsertsAndStopAsyncDoesNotThrow()
    {
        // Arrange
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._))
            .ThrowsAsync(new InvalidOperationException("The clipboard thread has ended."));
        await InsertAsync(Paste);

        // Act
        var outcome = await InsertAsync(Paste);
        await CompletePendingRestoreAsync();

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        A.CallTo(() => _keyboard.SendPaste()).MustHaveHappenedTwiceExactly();
        A.CallTo(() => _clipboard.TryRestoreAsync(A<ClipboardSnapshot>._)).MustHaveHappenedTwiceExactly();
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task InsertAsync_AnyOutcome_NeverLogsTranscript()
    {
        // Arrange
        A.CallTo(() => _tracker.IsForeground(Target)).Returns(false);

        // Act
        await InsertAsync(Paste);

        // Assert
        _logger.Entries.ShouldNotBeEmpty();
        _logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("Köln"));
    }

    private TextInserter CreateSut(bool isSelfElevated)
    {
        return new TextInserter(_clipboard, _keyboard, _tracker, _secureInput, _time, _logger, isSelfElevated);
    }

    /// <summary>
    /// Runs an insertion and advances the fake time in small steps, because the insertion continues on other threads
    /// and starts each wait a little later. The clipboard restore after a paste may still be pending.
    /// </summary>
    private async Task<InsertionOutcome> InsertAsync(TextInsertionSettings settings,
                                                     InsertionTarget? target = null,
                                                     TextInserter? sut = null)
    {
        var insert = (sut ?? _sut).InsertAsync(Transcript, target ?? Target, settings,
            TestContext.Current.CancellationToken);
        return await AdvanceUntilCompletedAsync(insert);
    }

    /// <summary>
    /// Waits through <see cref="TextInserter.StopAsync"/> for the clipboard restore that runs after a paste.
    /// </summary>
    private async Task CompletePendingRestoreAsync()
    {
        await AdvanceUntilCompletedAsync(_sut.StopAsync(TestContext.Current.CancellationToken));
    }

    private async Task<T> AdvanceUntilCompletedAsync<T>(Task<T> task)
    {
        await AdvanceUntilCompletedAsync((Task) task);
        return await task;
    }

    private async Task AdvanceUntilCompletedAsync(Task task)
    {
        for (var step = 0; step < 100 && !task.IsCompleted; step++)
        {
            _time.Advance(TimeStep);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Holds Shift, which delays both paste and typing, until the fake time reaches <paramref name="until"/>.
    /// </summary>
    private void HoldModifiers(TimeSpan until)
    {
        A.CallTo(() => _keyboard.AreModifiersDown(A<bool>._)).ReturnsLazily(() => Elapsed < until);
    }

    /// <summary>
    /// Holds only Ctrl, which delays typing but not a paste, until the fake time reaches <paramref name="until"/>.
    /// </summary>
    private void HoldControl(TimeSpan until)
    {
        A.CallTo(() => _keyboard.AreModifiersDown(A<bool>._))
            .ReturnsLazily((bool includePasteModifier) => includePasteModifier && Elapsed < until);
    }

    private void ShouldNotHaveSentKeystrokes()
    {
        A.CallTo(() => _keyboard.SendPaste()).MustNotHaveHappened();
        A.CallTo(() => _keyboard.TypeText(A<string>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A snapshot of a clipboard that holds nothing but text. <see cref="TextInserter"/> only reads
    /// <see cref="ClipboardSnapshot.IsSensitive"/> and hands the snapshot back, so its contents are the test's own.
    /// </summary>
    private sealed record TextSnapshot(string Text, bool IsSensitive = false) : ClipboardSnapshot(IsSensitive);
}
