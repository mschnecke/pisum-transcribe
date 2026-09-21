using Pisum.Transcribe.Recording;
using SharpHook.Data;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class PushToTalkDetectorTests
{
    private static readonly HashSet<KeyCode> RightControl = [KeyCode.VcRightControl];
    private static readonly HashSet<KeyCode> ControlWin = [KeyCode.VcLeftControl, KeyCode.VcLeftMeta];

    [Fact]
    public void OnKeyDown_SingleKeyHotkey_RaisesPressed()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);

        // Act
        var signal = sut.OnKeyDown(KeyCode.VcRightControl);

        // Assert
        signal.ShouldBe(PushToTalkSignal.Pressed);
    }

    [Fact]
    public void OnKeyUp_AfterPressed_RaisesReleasedOnce()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);
        sut.OnKeyDown(KeyCode.VcRightControl);

        // Act
        var first = sut.OnKeyUp(KeyCode.VcRightControl);
        var second = sut.OnKeyUp(KeyCode.VcRightControl);

        // Assert
        first.ShouldBe(PushToTalkSignal.Released);
        second.ShouldBeNull();
    }

    [Theory]
    [InlineData(KeyCode.VcLeftControl, KeyCode.VcLeftMeta)]
    [InlineData(KeyCode.VcLeftMeta, KeyCode.VcLeftControl)]
    public void OnKeyDown_MultiKeyHotkeyInAnyOrder_RaisesPressedOnSecondKey(KeyCode firstKey, KeyCode secondKey)
    {
        // Arrange
        var sut = new PushToTalkDetector(ControlWin);

        // Act
        var first = sut.OnKeyDown(firstKey);
        var second = sut.OnKeyDown(secondKey);

        // Assert
        first.ShouldBeNull();
        second.ShouldBe(PushToTalkSignal.Pressed);
    }

    [Fact]
    public void OnKeyDown_AutoRepeat_RaisesNoFurtherSignal()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);
        sut.OnKeyDown(KeyCode.VcRightControl);

        // Act
        var repeats = Enumerable.Range(0, 20).Select(_ => sut.OnKeyDown(KeyCode.VcRightControl)).ToList();

        // Assert
        repeats.ShouldAllBe(signal => signal == null);
        sut.OnKeyUp(KeyCode.VcRightControl).ShouldBe(PushToTalkSignal.Released);
    }

    [Fact]
    public void OnKeyDown_OtherKeyWhileActive_RaisesCancelledAndNoReleasedAfterwards()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);
        sut.OnKeyDown(KeyCode.VcRightControl);

        // Act
        var cancel = sut.OnKeyDown(KeyCode.VcC);
        var otherKeyUp = sut.OnKeyUp(KeyCode.VcC);
        var hotkeyUp = sut.OnKeyUp(KeyCode.VcRightControl);

        // Assert
        cancel.ShouldBe(PushToTalkSignal.Cancelled);
        otherKeyUp.ShouldBeNull();
        hotkeyUp.ShouldBeNull();
    }

    [Fact]
    public void OnKeyDown_OtherKeyWhileInactive_RaisesNothing()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);

        // Act
        var signal = sut.OnKeyDown(KeyCode.VcC);

        // Assert
        signal.ShouldBeNull();
    }

    [Fact]
    public void OnKeyDown_AfterCancelAndRelease_RaisesPressed()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);
        sut.OnKeyDown(KeyCode.VcRightControl);
        sut.OnKeyDown(KeyCode.VcC);
        sut.OnKeyUp(KeyCode.VcC);
        sut.OnKeyUp(KeyCode.VcRightControl);

        // Act
        var signal = sut.OnKeyDown(KeyCode.VcRightControl);

        // Assert
        signal.ShouldBe(PushToTalkSignal.Pressed);
    }

    [Fact]
    public void OnKeyDown_HotkeyCompletedAgainBeforeAllKeysUpAfterCancel_RaisesNothing()
    {
        // Arrange
        var sut = new PushToTalkDetector(ControlWin);
        sut.OnKeyDown(KeyCode.VcLeftControl);
        sut.OnKeyDown(KeyCode.VcLeftMeta);
        sut.OnKeyDown(KeyCode.VcC);
        sut.OnKeyUp(KeyCode.VcLeftMeta);

        // Act
        var signal = sut.OnKeyDown(KeyCode.VcLeftMeta);

        // Assert
        signal.ShouldBeNull();
    }

    [Fact]
    public void OnKeyUp_OtherKeyWhileActive_RaisesNothing()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);
        sut.OnKeyDown(KeyCode.VcLeftShift);
        sut.OnKeyDown(KeyCode.VcRightControl);

        // Act
        var signal = sut.OnKeyUp(KeyCode.VcLeftShift);

        // Assert
        signal.ShouldBeNull();
        sut.OnKeyUp(KeyCode.VcRightControl).ShouldBe(PushToTalkSignal.Released);
    }

    [Fact]
    public void Reset_WhileActive_RaisesCancelledAndClearsDownKeys()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);
        sut.OnKeyDown(KeyCode.VcRightControl);

        // Act
        var signal = sut.Reset();

        // Assert
        signal.ShouldBe(PushToTalkSignal.Cancelled);
        sut.DownKeys.ShouldBeEmpty();
        sut.OnKeyUp(KeyCode.VcRightControl).ShouldBeNull();
    }

    [Fact]
    public void Reset_WhileInactive_RaisesNothing()
    {
        // Arrange
        var sut = new PushToTalkDetector(ControlWin);
        sut.OnKeyDown(KeyCode.VcLeftControl);

        // Act
        var signal = sut.Reset();

        // Assert
        signal.ShouldBeNull();
        sut.DownKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Reset_AfterCancelWithHotkeyStillDown_NextPressRaisesPressed()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);
        sut.OnKeyDown(KeyCode.VcRightControl);
        sut.OnKeyDown(KeyCode.VcC);

        // Act
        var resetSignal = sut.Reset();
        var pressSignal = sut.OnKeyDown(KeyCode.VcRightControl);

        // Assert
        resetSignal.ShouldBeNull();
        pressSignal.ShouldBe(PushToTalkSignal.Pressed);
    }

    [Fact]
    public void Reset_AfterPressed_NextPressRaisesPressed()
    {
        // Arrange
        var sut = new PushToTalkDetector(RightControl);
        sut.OnKeyDown(KeyCode.VcRightControl);
        sut.Reset();

        // Act
        var signal = sut.OnKeyDown(KeyCode.VcRightControl);

        // Assert
        signal.ShouldBe(PushToTalkSignal.Pressed);
    }

    [Fact]
    public void DownKeys_HotkeyAndOtherKeysDown_HoldsOnlyHotkeyKeys()
    {
        // Arrange
        var sut = new PushToTalkDetector(ControlWin);

        // Act
        sut.OnKeyDown(KeyCode.VcA);
        sut.OnKeyDown(KeyCode.VcLeftControl);
        sut.OnKeyDown(KeyCode.VcB);

        // Assert
        sut.DownKeys.ShouldBe([KeyCode.VcLeftControl], ignoreOrder: true);
    }
}
