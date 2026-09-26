using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.Dictation;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacHotkeyAvailabilityTests
{
    private readonly IPermissions _permissions = A.Fake<IPermissions>();
    private readonly ISecureInput _secureInput = A.Fake<ISecureInput>();
    private readonly DictationState _dictationState = new();
    private readonly FakeTimeProvider _time = new();
    private readonly MacHotkeyAvailability _sut;
    private int _changes;

    public MacHotkeyAvailabilityTests()
    {
        A.CallTo(() => _permissions.IsAccessibilityInEffect).Returns(true);
        _sut = new MacHotkeyAvailability(_permissions, _secureInput, _dictationState, new InlineUiDispatcher(), _time);
        _sut.Changed += (_, _) => _changes++;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartAsync_GrantInEffectSecureInputOff_HasNoReason()
    {
        // Act
        await _sut.StartAsync(Ct);

        // Assert
        _sut.Reason.ShouldBeNull();
        _changes.ShouldBe(0);
    }

    [Fact]
    public async Task StartAsync_GrantNotInEffectAndSecureInputOn_ReportsTheAccessibilityReason()
    {
        // Arrange
        A.CallTo(() => _permissions.IsAccessibilityInEffect).Returns(false);
        A.CallTo(() => _secureInput.IsEnabled).Returns(true);

        // Act
        await _sut.StartAsync(Ct);

        // Assert
        _sut.Reason.ShouldBe(HotkeyUnavailableReason.AccessibilityNotInEffect);
    }

    [Fact]
    public async Task AccessibilityInEffectChanged_Revoked_ReportsTheAccessibilityReason()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        A.CallTo(() => _permissions.IsAccessibilityInEffect).Returns(false);

        // Act
        _permissions.AccessibilityInEffectChanged += Raise.WithEmpty();

        // Assert
        _sut.Reason.ShouldBe(HotkeyUnavailableReason.AccessibilityNotInEffect);
        _changes.ShouldBe(1);
    }

    [Fact]
    public async Task Poll_SecureInputTurnsOnThenOff_ReportsEachChangeWithinOneInterval()
    {
        // Arrange
        await _sut.StartAsync(Ct);

        // Act
        A.CallTo(() => _secureInput.IsEnabled).Returns(true);
        _time.Advance(MacHotkeyAvailability.PollInterval);
        var whileOn = _sut.Reason;
        A.CallTo(() => _secureInput.IsEnabled).Returns(false);
        _time.Advance(MacHotkeyAvailability.PollInterval);

        // Assert
        whileOn.ShouldBe(HotkeyUnavailableReason.SecureInputOn);
        _sut.Reason.ShouldBeNull();
        _changes.ShouldBe(2);
    }

    [Fact]
    public async Task Poll_ReasonUnchanged_DoesNotRaiseChanged()
    {
        // Arrange
        A.CallTo(() => _secureInput.IsEnabled).Returns(true);
        await _sut.StartAsync(Ct);

        // Act
        _time.Advance(MacHotkeyAvailability.PollInterval * 3);

        // Assert
        _sut.Reason.ShouldBe(HotkeyUnavailableReason.SecureInputOn);
        _changes.ShouldBe(1);
    }

    [Fact]
    public async Task Poll_DuringDictation_DoesNotReadSecureInputAndReadsItOnceTheDictationEnds()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _dictationState.SetActive(true);
        Fake.ClearRecordedCalls(_secureInput);

        // Act
        _time.Advance(MacHotkeyAvailability.PollInterval * 3);
        var readsDuringDictation = SecureInputReads();
        A.CallTo(() => _secureInput.IsEnabled).Returns(true);
        _dictationState.SetActive(false);

        // Assert
        readsDuringDictation.ShouldBe(0);
        SecureInputReads().ShouldBe(1);
        _sut.Reason.ShouldBe(HotkeyUnavailableReason.SecureInputOn);
    }

    [Fact]
    public async Task StopAsync_Always_StopsThePoll()
    {
        // Arrange
        await _sut.StartAsync(Ct);

        // Act
        await _sut.StopAsync(Ct);
        Fake.ClearRecordedCalls(_secureInput);
        _time.Advance(MacHotkeyAvailability.PollInterval * 3);

        // Assert
        SecureInputReads().ShouldBe(0);
    }

    private int SecureInputReads()
    {
        return Fake.GetCalls(_secureInput).Count(call => call.Method.Name == $"get_{nameof(ISecureInput.IsEnabled)}");
    }
}
