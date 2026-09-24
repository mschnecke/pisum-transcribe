using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.SettingsWindow;
using SharpHook.Data;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class HotkeyRecorderTests
{
    private readonly HotkeyRecorder _sut = new(HotkeyKeyNames.Windows);

    [Theory]
    [InlineData(KeyCode.VcLeftControl, KeyCode.VcLeftMeta)]
    [InlineData(KeyCode.VcLeftMeta, KeyCode.VcLeftControl)]
    public void OnKey_LeftCtrlAndLeftWinInEitherOrder_CapturesBoth(KeyCode first, KeyCode second)
    {
        // Act
        _sut.OnKey(first, true);
        _sut.OnKey(second, true);
        _sut.OnKey(first, false);
        var state = _sut.OnKey(second, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Captured);
        _sut.Keys.ShouldBe([KeyCode.VcLeftControl, KeyCode.VcLeftMeta], true);
        HotkeyText.Format(_sut.Keys, HotkeyKeyNames.Windows).ShouldBe("Left Ctrl+Left Win");
    }

    [Fact]
    public void OnKey_RightCtrlAlone_IsCapturedAsRightCtrl()
    {
        // Act
        _sut.OnKey(KeyCode.VcRightControl, true);
        _sut.OnKey(KeyCode.VcRightControl, false);

        // Assert
        _sut.State.ShouldBe(HotkeyRecordingState.Captured);
        HotkeyText.Format(_sut.Keys, HotkeyKeyNames.Windows).ShouldBe("Right Ctrl");
    }

    [Fact]
    public void OnKey_LetterAlone_IsRejectedWithWindowsMessage()
    {
        // Act
        _sut.OnKey(KeyCode.VcA, true);
        var state = _sut.OnKey(KeyCode.VcA, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Rejected);
        _sut.Keys.ShouldBe([KeyCode.VcA]);
        _sut.RejectedMessage.ShouldBe(
            "The hotkey must include Ctrl, Alt, Shift, the Windows key or a function key F1–F24.");
    }

    [Fact]
    public void OnKey_LetterAloneOnMacOS_IsRejectedWithMacMessage()
    {
        // Arrange
        var sut = new HotkeyRecorder(HotkeyKeyNames.MacOS);

        // Act
        sut.OnKey(KeyCode.VcA, true);
        var state = sut.OnKey(KeyCode.VcA, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Rejected);
        sut.RejectedMessage.ShouldBe(
            "The hotkey must include Control, Option, Shift, Command or a function key F1–F24.");
    }

    [Fact]
    public void OnKey_RightCommandAloneOnMacOS_IsCapturedAsRightCommand()
    {
        // Arrange
        var sut = new HotkeyRecorder(HotkeyKeyNames.MacOS);

        // Act
        sut.OnKey(KeyCode.VcRightMeta, true);
        var state = sut.OnKey(KeyCode.VcRightMeta, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Captured);
        HotkeyText.Format(sut.Keys, HotkeyKeyNames.MacOS).ShouldBe("Right Command");
    }

    [Theory]
    [InlineData(KeyCode.VcFunction)]
    [InlineData(KeyCode.VcChangeInputSource)] // what the hook reports for the Globe/fn key on a MacBook Air
    public void OnKey_FnAloneOnMacOS_IsRejected(KeyCode key)
    {
        // Arrange
        var sut = new HotkeyRecorder(HotkeyKeyNames.MacOS);

        // Act
        sut.OnKey(key, true);
        var state = sut.OnKey(key, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Rejected);
    }

    [Fact]
    public void OnKey_FnAloneOnWindows_IsRejected()
    {
        // Act
        _sut.OnKey(KeyCode.VcFunction, true);
        var state = _sut.OnKey(KeyCode.VcFunction, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Rejected);
    }

    [Theory]
    [InlineData(KeyCode.VcF1)]
    [InlineData(KeyCode.VcF12)]
    [InlineData(KeyCode.VcF13)]
    [InlineData(KeyCode.VcF24)]
    public void OnKey_FunctionKeyAlone_IsCaptured(KeyCode key)
    {
        // Act
        _sut.OnKey(key, true);
        var state = _sut.OnKey(key, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Captured);
        HotkeyText.Format(_sut.Keys, HotkeyKeyNames.Windows).ShouldBe(key.ToString()[2..]);
    }

    [Fact]
    public void OnKey_Escape_CancelsWithoutKeys()
    {
        // Arrange
        _sut.OnKey(KeyCode.VcLeftControl, true);

        // Act
        var state = _sut.OnKey(KeyCode.VcEscape, true);
        _sut.OnKey(KeyCode.VcEscape, false);
        _sut.OnKey(KeyCode.VcLeftControl, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Cancelled);
        _sut.State.ShouldBe(HotkeyRecordingState.Cancelled);
        _sut.Keys.ShouldBeEmpty();
    }

    [Fact]
    public void Cancel_WhileKeysHeld_EndsRecordingAndIgnoresLaterKeys()
    {
        // Arrange
        _sut.OnKey(KeyCode.VcLeftAlt, true);

        // Act
        _sut.Cancel();
        _sut.OnKey(KeyCode.VcLeftAlt, false);

        // Assert
        _sut.State.ShouldBe(HotkeyRecordingState.Cancelled);
        _sut.Keys.ShouldBeEmpty();
    }

    [Fact]
    public void OnKey_KeyUpOfKeyHeldBeforeRecording_IsIgnored()
    {
        // Act: the Enter that chose Change… is released after the recording started.
        var state = _sut.OnKey(KeyCode.VcEnter, false);

        // Assert
        state.ShouldBe(HotkeyRecordingState.Recording);
    }

    [Fact]
    public void OnKey_AutoRepeatAndChangingCombination_KeepsFirstLargestCombination()
    {
        // Act
        _sut.OnKey(KeyCode.VcLeftControl, true);
        _sut.OnKey(KeyCode.VcLeftControl, true);
        _sut.OnKey(KeyCode.VcLeftMeta, true);
        _sut.OnKey(KeyCode.VcLeftMeta, false);
        _sut.OnKey(KeyCode.VcLeftShift, true);
        _sut.OnKey(KeyCode.VcLeftShift, false);
        _sut.OnKey(KeyCode.VcLeftControl, false);

        // Assert
        _sut.State.ShouldBe(HotkeyRecordingState.Captured);
        HotkeyText.Format(_sut.Keys, HotkeyKeyNames.Windows).ShouldBe("Left Ctrl+Left Win");
    }

    [Fact]
    public void Keys_Captured_ParseBackFromSettingNames()
    {
        // Arrange
        _sut.OnKey(KeyCode.VcRightMeta, true);
        _sut.OnKey(KeyCode.VcF13, true);
        _sut.OnKey(KeyCode.VcRightMeta, false);
        _sut.OnKey(KeyCode.VcF13, false);

        // Act
        var names = HotkeyText.Order(_sut.Keys, HotkeyKeyNames.Windows).Select(key => key.ToString()).ToList();
        var parsed = HotkeyParser.Parse(names, NullLogger.Instance);

        // Assert
        names.ShouldBe(["VcRightMeta", "VcF13"]);
        parsed.ShouldBe(_sut.Keys, true);
    }
}
