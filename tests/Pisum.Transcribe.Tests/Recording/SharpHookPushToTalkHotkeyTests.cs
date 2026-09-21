using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;
using SharpHook.Data;
using SharpHook.Testing;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SharpHookPushToTalkHotkeyTests : IDisposable
{
    private const ushort RightControlRawCode = 0xA3;

    private readonly TestGlobalHook _hook = new();
    private readonly ISettingsStore _settingsStore = A.Fake<ISettingsStore>();
    private readonly ITrayIconService _trayIcon = A.Fake<ITrayIconService>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly CapturingLogger<SharpHookPushToTalkHotkey> _logger = new();
    private readonly HashSet<int> _physicallyDownRawCodes = [];
    private readonly List<string> _signals = [];
    private readonly SharpHookPushToTalkHotkey _sut;
    private int _keyStateReadings;

    public SharpHookPushToTalkHotkeyTests()
    {
        A.CallTo(() => _settingsStore.Current).Returns(new AppSettings());

        // Distinctive raw codes, so the privacy test can search the log for them.
        _hook.KeyCodeToRawCode = key => key == KeyCode.VcRightControl ? RightControlRawCode : RawCodeOf(key);

        _sut = new SharpHookPushToTalkHotkey(_hook, _settingsStore, _trayIcon, _timeProvider, _logger, IsKeyDown,
            action => action());
        _sut.Pressed += (_, _) => _signals.Add("Pressed");
        _sut.Released += (_, _) => _signals.Add("Released");
        _sut.Cancelled += (_, _) => _signals.Add("Cancelled");
    }

    public void Dispose()
    {
        _hook.Dispose();
    }

    [Fact]
    public async Task KeyPressed_Hotkey_RaisesPressed()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        Press(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed"]);
    }

    [Fact]
    public async Task KeyReleased_Hotkey_RaisesReleased()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);

        // Act
        Release(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed", "Released"]);
    }

    [Fact]
    public async Task KeyPressed_OtherKeyWhileHotkeyHeld_RaisesCancelledAndNoReleased()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);

        // Act
        Press(KeyCode.VcC);
        Release(KeyCode.VcC);
        Release(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed", "Cancelled"]);
    }

    [Fact]
    public async Task KeyPressed_HotkeyFromSettings_RaisesPressedOnlyWhenAllKeysHeld()
    {
        // Arrange
        A.CallTo(() => _settingsStore.Current).Returns(new AppSettings
        {
            Recording = new RecordingSettings {Hotkey = ["vcLeftControl", "vcLeftMeta"]},
        });
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        Press(KeyCode.VcLeftMeta);
        var afterFirstKey = _signals.ToList();
        Press(KeyCode.VcLeftControl);

        // Assert
        afterFirstKey.ShouldBeEmpty();
        _signals.ShouldBe(["Pressed"]);
    }

    [Fact]
    public async Task KeyEvents_Simulated_RaiseNothing()
    {
        // Arrange
        _hook.EventMask = _ => EventMask.SimulatedEvent;
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        Press(KeyCode.VcRightControl);
        Press(KeyCode.VcV);
        Release(KeyCode.VcV);
        Release(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBeEmpty();
    }

    [Fact]
    public async Task KeyPressed_SimulatedOtherKeyWhileHotkeyHeld_DoesNotCancel()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);

        // Act
        _hook.EventMask = _ => EventMask.SimulatedEvent;
        Press(KeyCode.VcV);
        Release(KeyCode.VcV);
        _hook.EventMask = _ => EventMask.None;
        Release(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed", "Released"]);
    }

    [Fact]
    public async Task StartAsync_HookFailsToRun_LogsErrorAndShowsNotification()
    {
        // Arrange
        _hook.RunResult = UioHookResult.ErrorSetWindowsHookEx;

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error);
        A.CallTo(() => _trayIcon.ShowNotification(SharpHookPushToTalkHotkey.UnavailableTitle,
                SharpHookPushToTalkHotkey.UnavailableMessage))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StopAsync_Running_DisposesHookWithoutReportingFailure()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await _sut.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        _hook.IsDisposed.ShouldBeTrue();
        _logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
        A.CallTo(() => _trayIcon.ShowNotification(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task StopAsync_WhileHotkeyHeld_StopsChecking()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);

        // Act
        await _sut.StopAsync(TestContext.Current.CancellationToken);
        _physicallyDownRawCodes.Clear();
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // Assert
        _keyStateReadings.ShouldBe(0);
        _signals.ShouldBe(["Pressed"]);
    }

    [Fact]
    public async Task Log_TypingAndCancelByOtherKey_ContainsNoTypedKeys()
    {
        // Arrange
        KeyCode[] typedKeys = [KeyCode.VcQ, KeyCode.VcZ, KeyCode.VcX, KeyCode.VcP, KeyCode.VcC];
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        foreach (var key in typedKeys[..^1])
        {
            Press(key);
            Release(key);
        }

        Press(KeyCode.VcRightControl);
        Press(KeyCode.VcC);
        Release(KeyCode.VcC);
        Release(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed", "Cancelled"]);
        _logger.Entries.ShouldNotBeEmpty();
        var logText = string.Join('\n', _logger.Entries.Select(entry =>
            entry.Message + '\n' + string.Join('\n', entry.Properties.Select(property => $"{property.Value}"))));
        foreach (var key in typedKeys)
        {
            logText.ShouldNotContain(key.ToString());
            logText.ShouldNotContain(RawCodeOf(key).ToString());
        }
    }

    [Fact]
    public async Task CheckTimer_HeldKeyReadsUpOnTwoTicks_RaisesCancelledAndNextPressRaisesPressed()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);

        // Act: the key-up happened where the hook could not see it.
        _physicallyDownRawCodes.Clear();
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval);
        var afterFirstTick = _signals.ToList();
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval);
        var afterSecondTick = _signals.ToList();
        Press(KeyCode.VcRightControl);

        // Assert
        afterFirstTick.ShouldBe(["Pressed"]);
        afterSecondTick.ShouldBe(["Pressed", "Cancelled"]);
        _signals.ShouldBe(["Pressed", "Cancelled", "Pressed"]);
    }

    [Fact]
    public async Task CheckTimer_UpReadingFollowedByDownReading_DoesNotCancel()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);

        // Act
        _physicallyDownRawCodes.Clear();
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval);
        _physicallyDownRawCodes.Add(RightControlRawCode);
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval);
        _physicallyDownRawCodes.Clear();
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval);

        // Assert
        _signals.ShouldBe(["Pressed"]);
    }

    [Fact]
    public async Task CheckTimer_KeyUpEventBetweenTicks_RaisesReleasedAndNoCancelled()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval);

        // Act
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval / 2);
        Release(KeyCode.VcRightControl);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // Assert
        _signals.ShouldBe(["Pressed", "Released"]);
    }

    [Fact]
    public async Task CheckTimer_NoHotkeyKeyDown_ReadsNoKeyState()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval);
        Release(KeyCode.VcRightControl);
        var readingsWhileHeld = _keyStateReadings;

        // Act
        Press(KeyCode.VcA);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        Release(KeyCode.VcA);

        // Assert
        readingsWhileHeld.ShouldBe(1);
        _keyStateReadings.ShouldBe(readingsWhileHeld);
    }

    [Fact]
    public async Task CheckTimer_HotkeyKeyLeftOverAfterCancel_ClearsStateWithoutAnotherCancelled()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);
        Press(KeyCode.VcC);
        Release(KeyCode.VcC);

        // Act: the hotkey key-up happened where the hook could not see it.
        _physicallyDownRawCodes.Clear();
        _timeProvider.Advance(SharpHookPushToTalkHotkey.CheckInterval * 2);
        Press(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed", "Cancelled", "Pressed"]);
    }

    [Fact]
    public async Task KeyPressed_WhileSuspended_RaisesNoSignal()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        _sut.Suspend();

        // Act
        Press(KeyCode.VcRightControl);
        Release(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBeEmpty();
    }

    [Fact]
    public async Task Suspend_WhileHotkeyHeld_RaisesCancelledAndNoReleased()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);

        // Act
        _sut.Suspend();
        Release(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed", "Cancelled"]);
    }

    [Fact]
    public async Task RawKey_OnlyWhileSuspended_ReportsKeyEvents()
    {
        // Arrange
        var rawKeys = new List<(KeyCode Key, bool IsPressed)>();
        _sut.RawKey += (_, e) => rawKeys.Add((e.Key, e.IsPressed));
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        Press(KeyCode.VcA);
        Release(KeyCode.VcA);
        _sut.Suspend();
        Press(KeyCode.VcLeftControl);
        Press(KeyCode.VcLeftMeta);
        Release(KeyCode.VcLeftMeta);
        Release(KeyCode.VcLeftControl);
        _sut.Resume();
        Press(KeyCode.VcB);
        Release(KeyCode.VcB);

        // Assert
        rawKeys.ShouldBe([
            (KeyCode.VcLeftControl, true),
            (KeyCode.VcLeftMeta, true),
            (KeyCode.VcLeftMeta, false),
            (KeyCode.VcLeftControl, false),
        ]);
        _signals.ShouldBeEmpty();
    }

    [Fact]
    public async Task RawKey_SimulatedWhileSuspended_IsNotReported()
    {
        // Arrange
        var rawKeys = 0;
        _sut.RawKey += (_, _) => rawKeys++;
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        _sut.Suspend();
        _hook.EventMask = _ => EventMask.SimulatedEvent;

        // Act
        Press(KeyCode.VcV);
        Release(KeyCode.VcV);

        // Assert
        rawKeys.ShouldBe(0);
    }

    [Fact]
    public async Task Resume_AfterSuspend_NextPressRaisesPressed()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        _sut.Suspend();

        // Act
        _sut.Resume();
        Press(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed"]);
    }

    [Fact]
    public async Task Log_KeysWhileSuspended_ContainsNoKeys()
    {
        // Arrange
        KeyCode[] typedKeys = [KeyCode.VcQ, KeyCode.VcZ, KeyCode.VcX];
        _sut.RawKey += (_, _) => { };
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        _sut.Suspend();

        // Act
        foreach (var key in typedKeys)
        {
            Press(key);
            Release(key);
        }

        _sut.Resume();

        // Assert
        var logText = string.Join('\n', _logger.Entries.Select(entry =>
            entry.Message + '\n' + string.Join('\n', entry.Properties.Select(property => $"{property.Value}"))));
        foreach (var key in typedKeys)
        {
            logText.ShouldNotContain(key.ToString());
            logText.ShouldNotContain(RawCodeOf(key).ToString());
        }
    }

    [Fact]
    public async Task SetHotkey_NewKeys_SwitchesTriggerKey()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _sut.SetHotkey(new HashSet<KeyCode> {KeyCode.VcLeftControl, KeyCode.VcLeftMeta});
        Press(KeyCode.VcRightControl);
        Release(KeyCode.VcRightControl);
        var afterOldHotkey = _signals.ToList();
        Press(KeyCode.VcLeftMeta);
        Press(KeyCode.VcLeftControl);
        Release(KeyCode.VcLeftControl);

        // Assert
        afterOldHotkey.ShouldBeEmpty();
        _signals.ShouldBe(["Pressed", "Released"]);
    }

    [Fact]
    public async Task SetHotkey_WhileHotkeyHeld_RaisesCancelledAndNoReleased()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        Press(KeyCode.VcRightControl);

        // Act
        _sut.SetHotkey(new HashSet<KeyCode> {KeyCode.VcF13});
        Release(KeyCode.VcRightControl);

        // Assert
        _signals.ShouldBe(["Pressed", "Cancelled"]);
    }

    private static ushort RawCodeOf(KeyCode key)
    {
        return (ushort) (40000 + (int) key);
    }

    private bool IsKeyDown(int rawCode)
    {
        _keyStateReadings++;
        return _physicallyDownRawCodes.Contains(rawCode);
    }

    private void Press(KeyCode key)
    {
        _physicallyDownRawCodes.Add(key == KeyCode.VcRightControl ? RightControlRawCode : RawCodeOf(key));
        _hook.SimulateKeyPress(key);
    }

    private void Release(KeyCode key)
    {
        _physicallyDownRawCodes.Remove(key == KeyCode.VcRightControl ? RightControlRawCode : RawCodeOf(key));
        _hook.SimulateKeyRelease(key);
    }
}
